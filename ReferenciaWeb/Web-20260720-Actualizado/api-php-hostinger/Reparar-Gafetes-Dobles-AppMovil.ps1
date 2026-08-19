param(
    [string]$SqlServer = ".",
    [string]$SqlUser = "",
    [string]$SqlPassword = "",
    [string]$MktDatabase = "mkt"
)

$ErrorActionPreference = "Stop"

function Text($Value) {
    if ($null -eq $Value -or $Value -eq [DBNull]::Value) { return "" }
    return [string]$Value
}

function Split-Badges($Value) {
    $raw = Text $Value
    if ([string]::IsNullOrWhiteSpace($raw)) { return @() }
    return @($raw -split '[,;/|]' | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Add-Parameter($Command, [string]$Name, $Value) {
    $parameter = $Command.Parameters.AddWithValue($Name, $Value)
    if ($null -eq $Value) {
        $parameter.Value = [DBNull]::Value
    }
}

function Int-Value($Value) {
    $result = 0
    if ([int]::TryParse((Text $Value), [ref]$result)) { return $result }
    return 0
}

function Decimal-Value($Value) {
    $result = [decimal]0
    if ([decimal]::TryParse((Text $Value), [ref]$result)) { return $result }
    return [decimal]0
}

if ([string]::IsNullOrWhiteSpace($SqlUser)) {
    $connectionString = "Server=$SqlServer;Database=$MktDatabase;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;"
} else {
    $connectionString = "Server=$SqlServer;Database=$MktDatabase;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;Encrypt=False;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()

try {
    $setup = $connection.CreateCommand()
    $setup.CommandText = @"
IF OBJECT_ID('dbo.gafete', 'U') IS NULL
    THROW 51000, 'No existe dbo.gafete.', 1;

IF COL_LENGTH('dbo.gafete', 'folioperacion') IS NULL
    ALTER TABLE dbo.gafete ADD folioperacion NVARCHAR(50) NULL;

IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.gafete')
      AND c.name = 'matricula'
      AND t.name NOT IN ('nvarchar', 'varchar', 'nchar', 'char')
) ALTER TABLE dbo.gafete ALTER COLUMN matricula NVARCHAR(50) NULL;

IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.gafete')
      AND c.name = 'folioperacion'
      AND t.name NOT IN ('nvarchar', 'varchar', 'nchar', 'char')
) ALTER TABLE dbo.gafete ALTER COLUMN folioperacion NVARCHAR(50) NULL;
"@
    [void]$setup.ExecuteNonQuery()

    $select = $connection.CreateCommand()
    $select.CommandText = @"
SELECT TOP (500)
    folio_app,
    folio_gafete,
    fecha_operacion,
    vendedor_nombre,
    telefono_taxista,
    unidad,
    hotel,
    pax,
    tipo_operacion,
    total,
    efectivo,
    tarjeta,
    id_catalogo
FROM dbo.AppMovilRegistro
WHERE NULLIF(LTRIM(RTRIM(folio_gafete)), '') IS NOT NULL
  AND folio_gafete LIKE '%,%'
ORDER BY fecha_operacion DESC;
"@
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $select
    $table = New-Object System.Data.DataTable
    [void]$adapter.Fill($table)

    $gafetesInserted = 0
    $dejadasInserted = 0
    foreach ($row in $table.Rows) {
        $folio = Text $row["folio_app"]
        $date = $row["fecha_operacion"]
        foreach ($badge in @(Split-Badges $row["folio_gafete"])) {
            $badgeNumber = 0
            if (-not [int]::TryParse($badge, [ref]$badgeNumber)) { continue }

            $insert = $connection.CreateCommand()
            $insert.CommandText = @"
IF NOT EXISTS (
    SELECT 1
    FROM dbo.gafete WITH (UPDLOCK, HOLDLOCK)
    WHERE UPPER(COALESCE(CONVERT(NVARCHAR(10), venta), '')) = 'A'
      AND (
          COALESCE(CONVERT(NVARCHAR(50), matricula), '') = @folio
          OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), '') = @folio
      )
      AND gafete = @badge
      AND CONVERT(date, fecha) = CONVERT(date, @fecha)
)
BEGIN
    INSERT INTO dbo.gafete (matricula, gafete, fecha, venta, hora, folioperacion)
    VALUES (@folio, @badge, CONVERT(DATETIME, CONVERT(DATE, @fecha)), 'A', @fecha, @folio);
    SELECT 1;
END
ELSE
BEGIN
    SELECT 0;
END;
"@
            Add-Parameter $insert "@folio" $folio
            Add-Parameter $insert "@badge" $badgeNumber
            Add-Parameter $insert "@fecha" $date
            $gafetesInserted += [int]$insert.ExecuteScalar()

            $dejada = $connection.CreateCommand()
            $dejada.CommandText = @"
IF OBJECT_ID('dbo.dejadas', 'U') IS NULL RETURN;
IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID('dbo.dejadas')
      AND c.name = 'idstaff'
      AND t.name NOT IN ('nvarchar', 'varchar', 'nchar', 'char')
) ALTER TABLE dbo.dejadas ALTER COLUMN idstaff NVARCHAR(50) NULL;

DECLARE @folioBigint BIGINT = CASE WHEN ISNUMERIC(@folio) = 1 THEN CAST(@folio AS BIGINT) ELSE 0 END;
DECLARE @horaTexto NVARCHAR(20) = CONVERT(NVARCHAR(20), CONVERT(TIME, @fecha), 100);

IF NOT EXISTS (
    SELECT 1
    FROM dbo.dejadas WITH (UPDLOCK, HOLDLOCK)
    WHERE (
            LTRIM(RTRIM(COALESCE(idstaff, ''))) = @folio
         OR LTRIM(RTRIM(COALESCE(folioregistrostr, ''))) = @folio
         OR LTRIM(RTRIM(COALESCE(codigorecepcion, ''))) = @folio
      )
      AND CONVERT(date, fecha) = CONVERT(date, @fecha)
      AND UPPER(LTRIM(RTRIM(COALESCE(gafete, '')))) = UPPER(LTRIM(RTRIM(@badgeText)))
)
BEGIN
    INSERT INTO dbo.dejadas (
        idstaff,nombrestaff,nombrealmacen,idalmacen,fecha,hora,idcajero,nombrecajero,total,
        codigorecepcion,folioregistro,folioregistrostr,unidad,pax,hotel,nombrevendedor,
        tipotransporte,telefono,horaentrada,horasalida,totalventa,comision,pago,
        totalefectivo,totaltarjeta,totalgastos,gafete,idtaxi,adl,men,inf
    ) VALUES (
        @folio,LEFT(@driverName, 100),'Plaza 28',1,CONVERT(DATETIME, CONVERT(DATE, @fecha)),@horaTexto,0,'APP MOVIL',@tripCost,
        @folio,COALESCE(@folioBigint, 0),@folio,LEFT(@unitNumber, 20),@passengerCount,LEFT(@hotel, 100),LEFT(@driverName, 100),
        LEFT(@serviceType, 10),LEFT(@driverPhone, 12),@horaTexto,@horaTexto,0,0,0,
        @cashAmount,@cardAmount,0,LEFT(@badgeText, 10),@catalogId,@passengerCount,0,0
    );
    SELECT 1;
END
ELSE
BEGIN
    SELECT 0;
END;
"@
            Add-Parameter $dejada "@folio" $folio
            Add-Parameter $dejada "@fecha" $date
            Add-Parameter $dejada "@badgeText" $badge
            Add-Parameter $dejada "@driverName" (Text $row["vendedor_nombre"])
            Add-Parameter $dejada "@driverPhone" (Text $row["telefono_taxista"])
            Add-Parameter $dejada "@unitNumber" (Text $row["unidad"])
            Add-Parameter $dejada "@passengerCount" (Int-Value $row["pax"])
            Add-Parameter $dejada "@hotel" (Text $row["hotel"])
            Add-Parameter $dejada "@serviceType" (Text $row["tipo_operacion"])
            Add-Parameter $dejada "@tripCost" (Decimal-Value $row["total"])
            Add-Parameter $dejada "@cashAmount" (Decimal-Value $row["efectivo"])
            Add-Parameter $dejada "@cardAmount" (Decimal-Value $row["tarjeta"])
            Add-Parameter $dejada "@catalogId" (Int-Value $row["id_catalogo"])
            $dejadasInserted += [int]$dejada.ExecuteScalar()
        }
    }

    Write-Host "Gafetes dobles reparados. Filas nuevas en dbo.gafete: $gafetesInserted"
    Write-Host "Dejadas dobles reparadas. Filas nuevas en dbo.dejadas: $dejadasInserted"
} finally {
    $connection.Close()
}
