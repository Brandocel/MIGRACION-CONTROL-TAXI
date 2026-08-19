param(
    [string]$ExcelPath = "C:\Users\reyna\Downloads\IMPORTE DEJADAS.xlsx",
    [string]$SqlServer = ".",
    [string]$SqlUser = "",
    [string]$SqlPassword = "",
    [string]$Database = "mkt"
)

$ErrorActionPreference = "Stop"

function New-SqlConnection {
    if ([string]::IsNullOrWhiteSpace($SqlUser)) {
        $cs = "Server=$SqlServer;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;"
    } else {
        $cs = "Server=$SqlServer;Database=$Database;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;Encrypt=False;"
    }
    $connection = New-Object System.Data.SqlClient.SqlConnection $cs
    $connection.Open()
    return $connection
}

function Add-Parameter($Command, [string]$Name, $Value) {
    $parameter = $Command.Parameters.AddWithValue($Name, $Value)
    if ($null -eq $Value) { $parameter.Value = [DBNull]::Value }
}

function Normalize-Text($Value) {
    if ($null -eq $Value) { return "" }
    return (([string]$Value).Trim() -replace '\s+', ' ').ToUpperInvariant()
}

function To-DecimalOrNull($Value) {
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $result = [decimal]0
    if ([decimal]::TryParse($text, [Globalization.NumberStyles]::Any, [Globalization.CultureInfo]::InvariantCulture, [ref]$result)) {
        return $result
    }
    if ([decimal]::TryParse($text, [Globalization.NumberStyles]::Any, [Globalization.CultureInfo]::GetCultureInfo("es-MX"), [ref]$result)) {
        return $result
    }
    return $null
}

function Get-ColumnName([string]$CellRef) {
    return ($CellRef -replace '[0-9]', '').ToUpperInvariant()
}

function Read-XlsxSheets([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "No existe el Excel: $Path" }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $copyPath = Join-Path $env:TEMP ("importe_dejadas_" + [guid]::NewGuid().ToString() + ".xlsx")
    Copy-Item -LiteralPath $Path -Destination $copyPath -Force
    $zip = [System.IO.Compression.ZipFile]::OpenRead($copyPath)

    try {
        $shared = @()
        $sharedEntry = $zip.GetEntry("xl/sharedStrings.xml")
        if ($sharedEntry) {
            $reader = New-Object IO.StreamReader($sharedEntry.Open())
            [xml]$sharedXml = $reader.ReadToEnd()
            $reader.Close()
            foreach ($si in $sharedXml.GetElementsByTagName("si")) {
                $text = ""
                foreach ($t in $si.GetElementsByTagName("t")) { $text += $t.InnerText }
                $shared += $text
            }
        }

        $reader = New-Object IO.StreamReader($zip.GetEntry("xl/workbook.xml").Open())
        [xml]$workbook = $reader.ReadToEnd()
        $reader.Close()

        $reader = New-Object IO.StreamReader($zip.GetEntry("xl/_rels/workbook.xml.rels").Open())
        [xml]$rels = $reader.ReadToEnd()
        $reader.Close()

        $relMap = @{}
        foreach ($rel in $rels.Relationships.Relationship) {
            $relMap[$rel.Id] = $rel.Target
        }

        $result = @{}
        foreach ($sheet in $workbook.workbook.sheets.sheet) {
            $target = $relMap[$sheet.Id]
            $sheetPath = if ($target.StartsWith("/")) { $target.TrimStart("/") } elseif ($target.StartsWith("xl/")) { $target } else { "xl/$target" }
            $reader = New-Object IO.StreamReader($zip.GetEntry($sheetPath).Open())
            [xml]$sheetXml = $reader.ReadToEnd()
            $reader.Close()

            $rows = @()
            foreach ($row in $sheetXml.worksheet.sheetData.row) {
                $item = @{}
                foreach ($cell in $row.c) {
                    $value = ""
                    if ($cell.v) {
                        $value = [string]$cell.v
                        if ($cell.t -eq "s" -and $value -match '^\d+$') {
                            $value = $shared[[int]$value]
                        }
                    }
                    $item[(Get-ColumnName $cell.r)] = $value
                }
                if ($item.Count -gt 0) { $rows += [pscustomobject]$item }
            }
            $result[[string]$sheet.name] = @($rows)
        }
        return $result
    } finally {
        if ($zip) { $zip.Dispose() }
        Remove-Item -LiteralPath $copyPath -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-Scalar($Connection, $Transaction, [string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.Transaction = $Transaction
    $command.CommandText = $Sql
    return $command.ExecuteScalar()
}

function Invoke-NonQuery($Connection, $Transaction, [string]$Sql) {
    $command = $Connection.CreateCommand()
    $command.Transaction = $Transaction
    $command.CommandText = $Sql
    return $command.ExecuteNonQuery()
}

function Get-ExistingTipo($Connection, $Transaction, [string]$UnitName) {
    $command = $Connection.CreateCommand()
    $command.Transaction = $Transaction
    $command.CommandText = @"
SELECT TOP (1) tipo
FROM dbo.transporte
WHERE UPPER(LTRIM(RTRIM(COALESCE(tipo, '')))) = @name
   OR UPPER(LTRIM(RTRIM(COALESCE(nombre, '')))) = @name
ORDER BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(tipo, '')))) = @name THEN 0 ELSE 1 END;
"@
    Add-Parameter $command "@name" $UnitName
    $value = $command.ExecuteScalar()
    if ($null -ne $value -and $value -ne [DBNull]::Value) { return [string]$value }
    return $null
}

function New-TipoCode($Connection, $Transaction, [string]$UnitName, [hashtable]$UsedCodes) {
    $base = ($UnitName -replace '[^A-Z0-9]', '')
    if ([string]::IsNullOrWhiteSpace($base)) { $base = "TIPO" }
    if ($base.Length -gt 10) { $base = $base.Substring(0, 10) }

    for ($i = 0; $i -lt 100; $i++) {
        $code = $base
        if ($i -gt 0) {
            $suffix = [string]$i
            $prefixLength = [Math]::Min(10 - $suffix.Length, $base.Length)
            $code = $base.Substring(0, $prefixLength) + $suffix
        }
        if ($UsedCodes.ContainsKey($code)) { continue }

        $command = $Connection.CreateCommand()
        $command.Transaction = $Transaction
        $command.CommandText = "SELECT COUNT(1) FROM dbo.transporte WHERE UPPER(LTRIM(RTRIM(COALESCE(tipo, '')))) = @code;"
        Add-Parameter $command "@code" $code
        if ([int]$command.ExecuteScalar() -eq 0) {
            $UsedCodes[$code] = $true
            return $code
        }
    }
    throw "No se pudo generar tipo para $UnitName"
}

$sheets = Read-XlsxSheets $ExcelPath
$rates = @()
foreach ($row in @($sheets["Hoja1"])) {
    $unit = Normalize-Text $row.C
    $amount = To-DecimalOrNull $row.D
    if ([string]::IsNullOrWhiteSpace($unit) -or $unit -eq "UNIDAD") { continue }
    if ($null -eq $amount) { continue }
    $rates += [pscustomobject]@{ Unit = $unit; Amount = $amount }
}

$hotels = @()
foreach ($row in @($sheets["Hoja2"])) {
    $hotel = Normalize-Text $row.C
    if ([string]::IsNullOrWhiteSpace($hotel)) { continue }
    $hotels += $hotel
}
$hotels = @($hotels | Sort-Object -Unique)

$connection = New-SqlConnection
$transaction = $connection.BeginTransaction()
try {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    [void](Invoke-NonQuery $connection $transaction "SELECT * INTO dbo.transporte_backup_importe_dejadas_$stamp FROM dbo.transporte;")
    if ([int](Invoke-Scalar $connection $transaction "SELECT COUNT(1) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='AppDejadaEquivalencia';") -gt 0) {
        [void](Invoke-NonQuery $connection $transaction "SELECT * INTO dbo.AppDejadaEquivalencia_backup_importe_dejadas_$stamp FROM dbo.AppDejadaEquivalencia;")
    }

    [void](Invoke-NonQuery $connection $transaction @"
IF OBJECT_ID('dbo.AppDejadaEquivalencia', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppDejadaEquivalencia (
        id_dejada_equivalencia INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppDejadaEquivalencia PRIMARY KEY,
        unidad_texto NVARCHAR(100) NOT NULL,
        unidad_normalizada NVARCHAR(100) NULL,
        tipo_transporte NVARCHAR(20) NOT NULL DEFAULT '',
        dejada DECIMAL(18,2) NOT NULL DEFAULT 0,
        fuente NVARCHAR(80) NOT NULL DEFAULT ''
    );
END;

IF OBJECT_ID('dbo.AppMovilHoteles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilHoteles (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilHoteles PRIMARY KEY,
        HotelNombre NVARCHAR(200) NOT NULL,
        HotelNormalizado NVARCHAR(200) NOT NULL,
        Activo BIT NOT NULL DEFAULT 1,
        Fuente NVARCHAR(80) NOT NULL DEFAULT '',
        FechaActualizacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
"@)

    $usedCodes = @{}
    $insertedRates = 0
    $updatedRates = 0
    $insertedTransports = 0
    $updatedTransports = 0
    foreach ($rate in $rates) {
        $tipo = Get-ExistingTipo $connection $transaction $rate.Unit
        $transportExists = $true
        if ([string]::IsNullOrWhiteSpace($tipo)) {
            $tipo = New-TipoCode $connection $transaction $rate.Unit $usedCodes
            $transportExists = $false
        }

        if ($transportExists) {
            $command = $connection.CreateCommand()
            $command.Transaction = $transaction
            $command.CommandText = @"
UPDATE dbo.transporte
SET dejada = @amount,
    minimo = @amount,
    maximo = @amount
WHERE UPPER(LTRIM(RTRIM(COALESCE(tipo, '')))) = @unit
   OR UPPER(LTRIM(RTRIM(COALESCE(nombre, '')))) = @unit;
"@
            Add-Parameter $command "@amount" ([single]$rate.Amount)
            Add-Parameter $command "@unit" $rate.Unit
            $updatedTransports += $command.ExecuteNonQuery()
        } else {
            $command = $connection.CreateCommand()
            $command.Transaction = $transaction
            $command.CommandText = @"
INSERT INTO dbo.transporte (tipo,nombre,moneda,impuestos,dejada,comision,minimo,maximo,dpto,efectivo,tarjeta,amexco)
VALUES (@tipo,@nombre,'E',16,@amount,10,@amount,@amount,NULL,0,19,21);
"@
            Add-Parameter $command "@tipo" $tipo
            Add-Parameter $command "@nombre" ($(if ($rate.Unit.Length -gt 50) { $rate.Unit.Substring(0, 50) } else { $rate.Unit }))
            Add-Parameter $command "@amount" ([single]$rate.Amount)
            [void]$command.ExecuteNonQuery()
            $insertedTransports++
        }

        $command = $connection.CreateCommand()
        $command.Transaction = $transaction
        $command.CommandText = @"
IF EXISTS (
    SELECT 1 FROM dbo.AppDejadaEquivalencia
    WHERE UPPER(LTRIM(RTRIM(unidad_texto))) = @unit
       OR UPPER(LTRIM(RTRIM(COALESCE(unidad_normalizada, '')))) = @unit
)
BEGIN
    UPDATE dbo.AppDejadaEquivalencia
    SET tipo_transporte = @tipo,
        dejada = @amount,
        fuente = 'IMPORTE DEJADAS XLSX'
    WHERE UPPER(LTRIM(RTRIM(unidad_texto))) = @unit
       OR UPPER(LTRIM(RTRIM(COALESCE(unidad_normalizada, '')))) = @unit;
    SELECT 'U';
END
ELSE
BEGIN
    INSERT INTO dbo.AppDejadaEquivalencia (unidad_texto, tipo_transporte, dejada, fuente)
    VALUES (@unit, @tipo, @amount, 'IMPORTE DEJADAS XLSX');
    SELECT 'I';
END;
"@
        Add-Parameter $command "@unit" $rate.Unit
        Add-Parameter $command "@tipo" $tipo
        Add-Parameter $command "@amount" $rate.Amount
        $action = [string]$command.ExecuteScalar()
        if ($action -eq "I") { $insertedRates++ } else { $updatedRates++ }
    }

    $insertedHotels = 0
    $updatedHotels = 0
    foreach ($hotel in $hotels) {
        $command = $connection.CreateCommand()
        $command.Transaction = $transaction
        $command.CommandText = @"
IF EXISTS (SELECT 1 FROM dbo.AppMovilHoteles WHERE HotelNormalizado = @hotel)
BEGIN
    UPDATE dbo.AppMovilHoteles
    SET HotelNombre = @hotel,
        Activo = 1,
        Fuente = 'IMPORTE DEJADAS XLSX',
        FechaActualizacion = SYSUTCDATETIME()
    WHERE HotelNormalizado = @hotel;
    SELECT 'U';
END
ELSE
BEGIN
    INSERT INTO dbo.AppMovilHoteles (HotelNombre, HotelNormalizado, Activo, Fuente)
    VALUES (@hotel, @hotel, 1, 'IMPORTE DEJADAS XLSX');
    SELECT 'I';
END;
"@
        Add-Parameter $command "@hotel" $hotel
        $action = [string]$command.ExecuteScalar()
        if ($action -eq "I") { $insertedHotels++ } else { $updatedHotels++ }
    }

    $transaction.Commit()
    Write-Host "Importacion terminada."
    Write-Host "Importes Excel: $($rates.Count). Equivalencias insertadas: $insertedRates, actualizadas: $updatedRates."
    Write-Host "Transporte insertados: $insertedTransports, filas actualizadas: $updatedTransports."
    Write-Host "Hoteles Excel: $($hotels.Count). Hoteles insertados: $insertedHotels, actualizados: $updatedHotels."
    Write-Host "Respaldos creados con sufijo: $stamp"
} catch {
    $transaction.Rollback()
    throw
} finally {
    $connection.Close()
}
