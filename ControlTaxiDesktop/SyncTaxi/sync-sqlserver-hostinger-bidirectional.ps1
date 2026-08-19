param(
    [string]$ApiBaseUrl = "https://lightyellow-porpoise-679527.hostingersite.com",
    [string]$SyncToken = "HokaTaxisSync2050",
    [string]$SqlServer = "26.38.252.71\SQLEXPRESS",
    [string]$SqlUser = "sa",
    [string]$SqlPassword = "",
    [string]$MktDatabase = "mkt",
    [string]$BranchCode = "28",
    [int]$PullLimit = 500,
    [switch]$IntegratedSecurity,
    [switch]$PullOnly,
    [switch]$RunLocalMirrorReview
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Find-WorkspaceRoot {
    $current = Get-Item -LiteralPath (Split-Path -Parent $MyInvocation.MyCommand.Path)
    while ($null -ne $current) {
        if (Test-Path (Join-Path $current.FullName "CONTROL TAXI.sln")) { return $current.FullName }
        if (Test-Path (Join-Path $current.FullName "ControlTaxiDesktop.exe")) { return $current.FullName }
        $current = $current.Parent
    }
    return Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Apply-JsonConfigFile([string]$configPath) {
    if (-not (Test-Path -LiteralPath $configPath)) { return $false }
    try {
        $localConfig = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($localConfig.PSObject.Properties['SqlServer']   -and $localConfig.SqlServer)   { $script:SqlServer   = [string]$localConfig.SqlServer }
        if ($localConfig.PSObject.Properties['SqlUser']     -and $localConfig.SqlUser)     { $script:SqlUser     = [string]$localConfig.SqlUser }
        if ($localConfig.PSObject.Properties['SqlPassword'] -and $null -ne $localConfig.SqlPassword) { $script:SqlPassword = [string]$localConfig.SqlPassword }
        if ($localConfig.PSObject.Properties['MktDatabase'] -and $localConfig.MktDatabase) { $script:MktDatabase = [string]$localConfig.MktDatabase }
        if ($localConfig.PSObject.Properties['ApiBaseUrl']  -and $localConfig.ApiBaseUrl)  { $script:ApiBaseUrl  = [string]$localConfig.ApiBaseUrl }
        if ($localConfig.PSObject.Properties['SyncToken']   -and $localConfig.SyncToken)   { $script:SyncToken   = [string]$localConfig.SyncToken }
        if ($localConfig.PSObject.Properties['BranchCode']  -and $localConfig.BranchCode)  { $script:BranchCode  = [string]$localConfig.BranchCode }
        if ($localConfig.PSObject.Properties['IntegratedSecurity'] -and [bool]$localConfig.IntegratedSecurity) { $script:IntegratedSecurity = $true }
        if ($localConfig.PSObject.Properties['PullOnly']          -and [bool]$localConfig.PullOnly)          { $script:PullOnly          = $true }
        Write-Host "Config local aplicada desde $(Split-Path -Leaf $configPath) (Server=$SqlServer, DB=$MktDatabase, IntegratedSecurity=$IntegratedSecurity, PullOnly=$PullOnly)."
        return $true
    } catch {
        Write-Warning "No se pudo leer $(Split-Path -Leaf $configPath): $($_.Exception.Message)."
        return $false
    }
}

function Try-ApplyCascoCredential {
    if (-not [string]::IsNullOrWhiteSpace($script:SqlPassword) -and $script:SqlPassword -notin @("TU_PASSWORD", "TU_PASSWORD_REAL")) {
        return
    }

    $workspaceRoot = Find-WorkspaceRoot
    $credentialPath = Join-Path $workspaceRoot "Config\casco.credentials.dat"
    if (-not (Test-Path -LiteralPath $credentialPath)) { return }

    try {
        Add-Type -AssemblyName System.Security
        $entropy = [System.Text.Encoding]::UTF8.GetBytes("ControlTaxiDesktop.Casco.Credentials.v1")
        $encrypted = [System.IO.File]::ReadAllBytes($credentialPath)
        $jsonBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
            $encrypted,
            $entropy,
            [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
        $credentialJson = [System.Text.Encoding]::UTF8.GetString($jsonBytes)
        $credential = $credentialJson | ConvertFrom-Json
        if ($null -ne $credential) {
            if ([string]::IsNullOrWhiteSpace($script:SqlServer) -or $script:SqlServer -eq "192.168.1.70,50807") { $script:SqlServer = [string]$credential.SqlServer }
            if ([string]::IsNullOrWhiteSpace($script:SqlUser) -or $script:SqlUser -eq "sa") { $script:SqlUser = [string]$credential.SqlUser }
            if ([string]::IsNullOrWhiteSpace($script:MktDatabase) -or $script:MktDatabase -eq "mkt") { $script:MktDatabase = [string]$credential.Database }
            $script:SqlPassword = [string]$credential.SqlPassword
            if (-not [string]::IsNullOrWhiteSpace([string]$credential.BranchCode)) { $script:BranchCode = [string]$credential.BranchCode }
            Write-Host "Credencial cifrada de Casco aplicada desde Config\\casco.credentials.dat."
        }
    } catch {
        Write-Warning "No se pudo leer la credencial cifrada de Casco: $($_.Exception.Message)"
    }
}

# --- Configuracion local opcional -------------------------------------------
# Si existe "sync.local.config.json" junto a este script, sus valores tienen
# prioridad sobre los parametros por defecto de arriba. Sirve para apuntar el
# sincronizador a una base especifica sin tocar el codigo ni los valores de
# produccion. En el servidor de produccion no existe ese archivo, asi que su
# comportamiento no cambia.
# Ejemplo de contenido:
# {
#   "SqlServer": "SERVIDOR\\INSTANCIA",
#   "SqlUser": "sa",
#   "SqlPassword": "********",
#   "MktDatabase": "mkt",
#   "ApiBaseUrl": "https://lightyellow-porpoise-679527.hostingersite.com",
#   "SyncToken": "HokaTaxisSync2050",
#   "BranchCode": "28"
# }
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$cascoConfigPath = Join-Path $scriptDirectory "sync.casco.config.json"
$localConfigPath = Join-Path $scriptDirectory "sync.local.config.json"
$isCascoBootstrap = $BranchCode -eq "CV" -or $ApiBaseUrl -match "/casco-api/?$" -or (Test-Path -LiteralPath $cascoConfigPath)
if ($isCascoBootstrap) {
    [void](Apply-JsonConfigFile $cascoConfigPath)
}
if (-not $isCascoBootstrap) {
    [void](Apply-JsonConfigFile $localConfigPath)
} elseif (Test-Path -LiteralPath $localConfigPath) {
    Write-Host "Modo Casco detectado: se omitio sync.local.config.json para no mezclar Plaza 28."
}
Try-ApplyCascoCredential

function New-SqlConnection {
    $attempts = New-Object System.Collections.Generic.List[string]
    if ($IntegratedSecurity -or [string]::IsNullOrWhiteSpace($SqlUser)) {
        [void]$attempts.Add("Server=$SqlServer;Database=$MktDatabase;Integrated Security=True;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3;")
    } else {
        [void]$attempts.Add("Server=$SqlServer;Database=$MktDatabase;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;Encrypt=False;Connect Timeout=3;")
    }

    $lastError = $null
    foreach ($connectionString in $attempts) {
        try {
            $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
            $connection.Open()
            return $connection
        } catch {
            $lastError = $_.Exception
            Write-Warning "No se pudo abrir SQL con '$connectionString': $($lastError.Message)"
        }
    }

    throw $lastError
}

function Add-Parameter($Command, [string]$Name, $Value) {
    $parameter = $Command.Parameters.AddWithValue($Name, $Value)
    if ($null -eq $Value) {
        $parameter.Value = [DBNull]::Value
    }
}

function Text($Value) {
    if ($null -eq $Value) { return "" }
    return [string]$Value
}

function Text-Max($Value, [int]$MaxLength) {
    $valueText = Text $Value
    if ($MaxLength -gt 0 -and $valueText.Length -gt $MaxLength) {
        return $valueText.Substring(0, $MaxLength)
    }
    return $valueText
}

function Split-Badges($Value) {
    $raw = Text $Value
    if ([string]::IsNullOrWhiteSpace($raw)) { return @() }
    $raw = $raw.Replace([char]0x00A0, ' ').Replace([char]0x2007, ' ').Replace([char]0x202F, ' ')
    $items = New-Object System.Collections.Generic.List[string]
    foreach ($token in @($raw -split '[\s\p{Z},;/|]+')) {
        $clean = (Text $token).Trim()
        if ([string]::IsNullOrWhiteSpace($clean)) { continue }

        if ($clean -match '^\d+$') {
            [void]$items.Add($clean)
            continue
        }

        $digitMatches = [regex]::Matches($clean, '\d+')
        if ($digitMatches.Count -gt 1 -and $clean -notmatch '[A-Za-z]') {
            foreach ($match in $digitMatches) {
                [void]$items.Add($match.Value)
            }
            continue
        }

        [void]$items.Add($clean)
    }

    return @($items | Select-Object -Unique)
}

function First-Badge($Value) {
    $badges = @(Split-Badges $Value)
    if ($badges.Count -eq 0) { return "" }
    return $badges[0]
}

function Resolve-LocalOperationSite($Record) {
    $assignedBranchName = (Text $Record.assignedBranchName).Trim()
    if (-not [string]::IsNullOrWhiteSpace($assignedBranchName)) {
        return $assignedBranchName
    }

    $assignedBranch = (Text $Record.assignedBranch).Trim()
    if (-not [string]::IsNullOrWhiteSpace($assignedBranch)) {
        $withoutCode = [regex]::Replace($assignedBranch, '^\s*[^-]+-\s*', '').Trim()
        if (-not [string]::IsNullOrWhiteSpace($withoutCode)) {
            return $withoutCode
        }
        return $assignedBranch
    }

    return Text $Record.site
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

function DateTimeOffset-LocalClock($Value) {
    $valueText = (Text $Value).Trim()
    if ([string]::IsNullOrWhiteSpace($valueText)) { return [DBNull]::Value }

    $offsetDate = [datetimeoffset]::MinValue
    $formats = @(
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fffffff",
        "yyyy-MM-ddTHH:mm:sszzz",
        "yyyy-MM-ddTHH:mm:ss.fffffffzzz",
        "yyyy-MM-ddTHH:mm:ss'Z'",
        "yyyy-MM-ddTHH:mm:ss.fffffff'Z'",
        "yyyy-MM-dd"
    )

    if ([datetimeoffset]::TryParseExact($valueText, $formats, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$offsetDate) -or
        [datetimeoffset]::TryParseExact($valueText, $formats, [Globalization.CultureInfo]::GetCultureInfo("es-MX"), [Globalization.DateTimeStyles]::None, [ref]$offsetDate) -or
        [datetimeoffset]::TryParse($valueText, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$offsetDate) -or
        [datetimeoffset]::TryParse($valueText, [Globalization.CultureInfo]::GetCultureInfo("es-MX"), [Globalization.DateTimeStyles]::None, [ref]$offsetDate)) {
        if ($valueText -match '(Z|[+-]\d{2}:\d{2})$') {
            return $offsetDate.LocalDateTime
        }
        return $offsetDate.DateTime
    }

    return [DBNull]::Value
}

function Date-Value($Value) {
    if ($Value -is [datetime]) { return $Value }
    if ($Value -is [datetimeoffset]) { return $Value.DateTime }
    if ($null -eq $Value -or $Value -eq [DBNull]::Value) { return [DBNull]::Value }
    $valueText = Text $Value
    if ([string]::IsNullOrWhiteSpace($valueText)) { return [DBNull]::Value }

    $offsetDate = DateTimeOffset-LocalClock $valueText
    if ($offsetDate -ne [DBNull]::Value) { return $offsetDate }

    $date = [datetime]::MinValue
    $formats = @(
        "dd/MM/yyyy HH:mm:ss",
        "dd/MM/yyyy HH:mm",
        "dd/MM/yyyy",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss.fffffff",
        "yyyy-MM-dd"
    )
    if ([datetime]::TryParseExact($valueText, $formats, [Globalization.CultureInfo]::GetCultureInfo("es-MX"), [Globalization.DateTimeStyles]::None, [ref]$date)) {
        return $date
    }
    if ([datetime]::TryParse($valueText, [ref]$date)) { return $date }
    return [DBNull]::Value
}

function Resolve-PayoutMirror($Record) {
    $payoutDateValue = Date-Value $Record.payoutDate
    $payoutUser = Text-Max $Record.payoutUser 80
    $payoutTicket = Text-Max $Record.payoutTicket 80
    $rawStatus = (Text $Record.payoutStatus).Trim()

    $isPaid = $false
    if (-not [string]::IsNullOrWhiteSpace($rawStatus) -and $rawStatus -match 'pagad') {
        $isPaid = $true
    } elseif ($payoutDateValue -ne [DBNull]::Value) {
        $isPaid = $true
    } elseif (-not [string]::IsNullOrWhiteSpace($payoutUser)) {
        $isPaid = $true
    } elseif (-not [string]::IsNullOrWhiteSpace($payoutTicket)) {
        $isPaid = $true
    }

    $normalizedStatus = if ($isPaid) { "pagado" } else { "pendiente" }
    return @{
        Status = $normalizedStatus
        Date = $payoutDateValue
        User = $payoutUser
        Ticket = $payoutTicket
        IsPaid = $isPaid
    }
}

function Record-DateTimeValue($Record) {
    $recordDate = Text $Record.recordDate
    if ([string]::IsNullOrWhiteSpace($recordDate)) { $recordDate = Text $Record.operationDate }
    if ([string]::IsNullOrWhiteSpace($recordDate)) { $recordDate = Text $Record.tripDate }
    if ([string]::IsNullOrWhiteSpace($recordDate)) { $recordDate = Text $Record.createdAt }
    if ([string]::IsNullOrWhiteSpace($recordDate)) { $recordDate = Text $Record.created_at }
    if ([string]::IsNullOrWhiteSpace($recordDate)) { $recordDate = Text $Record.fecha }

    $timeCandidates = @(
        (Text $Record.recordTime),
        (Text $Record.operationTime),
        (Text $Record.tripTime),
        (Text $Record.captureTime),
        (Text $Record.capturedAt),
        (Text $Record.createdTime),
        (Text $Record.hora),
        (Text $Record.time)
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    if ($timeCandidates.Count -gt 0) {
        $dateOnly = Date-Value $recordDate
        if ($dateOnly -ne [DBNull]::Value) {
            $dateText = $dateOnly.ToString("dd/MM/yyyy")
            return Date-Value "$dateText $($timeCandidates[0])"
        }
    }

    $parsed = Date-Value $recordDate
    if ($parsed -eq [DBNull]::Value) {
        throw "El registro '$($Record.recordId)' no trae fecha/hora de operacion valida. No se insertara con la hora de sincronizacion."
    }
    return $parsed
}

function Invoke-HostingerApiWithCurl([string]$Method, [string]$Uri, $Body = $null) {
    $arguments = New-Object System.Collections.Generic.List[string]
    [void]$arguments.Add("-sS")
    [void]$arguments.Add("-f")
    [void]$arguments.Add("-X")
    [void]$arguments.Add($Method)
    [void]$arguments.Add("-H")
    [void]$arguments.Add("X-Sync-Token: $SyncToken")

    $tempFile = $null
    try {
        if ($null -ne $Body) {
            $json = $Body | ConvertTo-Json -Depth 20 -Compress
            $tempFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $tempFile -Value $json -Encoding UTF8
            [void]$arguments.Add("-H")
            [void]$arguments.Add("Content-Type: application/json; charset=utf-8")
            [void]$arguments.Add("--data-binary")
            [void]$arguments.Add("@$tempFile")
        }

        [void]$arguments.Add($Uri)
        $responseText = & curl.exe @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "curl.exe fallo con codigo $LASTEXITCODE llamando $Method $Uri"
        }
        if ([string]::IsNullOrWhiteSpace($responseText)) {
            return $null
        }
        return $responseText | ConvertFrom-Json
    } finally {
        if ($tempFile -and (Test-Path $tempFile)) {
            Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-HostingerApi([string]$Method, [string]$Path, $Body = $null) {
    $headers = @{ "X-Sync-Token" = $SyncToken }
    $uri = "$ApiBaseUrl$Path"
    try {
        if ($null -eq $Body) {
            return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
        }

        $json = $Body | ConvertTo-Json -Depth 20 -Compress
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json; charset=utf-8" -Body $json
    } catch {
        Write-Host "Error llamando API con PowerShell: $Method $uri"
        if ($_.Exception.Response -and $_.Exception.Response.GetResponseStream()) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $responseText = $reader.ReadToEnd()
            if (-not [string]::IsNullOrWhiteSpace($responseText)) {
                Write-Host "Respuesta Hostinger: $responseText"
            }
        }
        Write-Host "Reintentando API con curl.exe..."
        return Invoke-HostingerApiWithCurl $Method $uri $Body
    }
}

function Test-IsCascoApi {
    return $BranchCode -eq "CV" -or $ApiBaseUrl -match "/casco-api/?$"
}

function Get-HostingerRecentTripRecords {
    $dateTo = (Get-Date).ToString("yyyy-MM-dd")
    $dateFrom = (Get-Date).AddDays(-14).ToString("yyyy-MM-dd")
    return @(Invoke-HostingerApi "Get" "/api/taxis/registros?dateFrom=$dateFrom&dateTo=$dateTo")
}

function Ensure-AppTables($Connection) {
    $command = $Connection.CreateCommand()
    $command.CommandText = @"
IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilRegistro (
        id_app_movil_registro INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistro PRIMARY KEY,
        folio_app NVARCHAR(60) NOT NULL,
        folio_app_original NVARCHAR(60) NOT NULL DEFAULT '',
        folio_pos NVARCHAR(100) NOT NULL DEFAULT '',
        fecha_operacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        id_catalogo INT NULL,
        folio_gafete NVARCHAR(300) NOT NULL DEFAULT '',
        vendedor_clave NVARCHAR(50) NOT NULL DEFAULT '',
        vendedor_nombre NVARCHAR(150) NOT NULL DEFAULT '',
        telefono_taxista NVARCHAR(30) NOT NULL DEFAULT '',
        telefono_contacto NVARCHAR(30) NOT NULL DEFAULT '',
        nacionalidad NVARCHAR(120) NOT NULL DEFAULT '',
        placas NVARCHAR(50) NOT NULL DEFAULT '',
        modelo_vehiculo NVARCHAR(150) NOT NULL DEFAULT '',
        unidad NVARCHAR(50) NOT NULL DEFAULT '',
        hotel NVARCHAR(200) NOT NULL DEFAULT '',
        origen NVARCHAR(150) NOT NULL DEFAULT '',
        sitio NVARCHAR(150) NOT NULL DEFAULT '',
        destino NVARCHAR(150) NOT NULL DEFAULT '',
        pax INT NOT NULL DEFAULT 0,
        tipo_operacion NVARCHAR(80) NOT NULL DEFAULT '',
        total DECIMAL(18,2) NOT NULL DEFAULT 0,
        efectivo DECIMAL(18,2) NOT NULL DEFAULT 0,
        tarjeta DECIMAL(18,2) NOT NULL DEFAULT 0,
        estado_pago_dejada NVARCHAR(20) NOT NULL DEFAULT 'pendiente',
        fecha_pago_dejada DATETIME2 NULL,
        usuario_pago_dejada NVARCHAR(180) NOT NULL DEFAULT '',
        ticket_pago_dejada NVARCHAR(80) NOT NULL DEFAULT '',
        usuario_movil NVARCHAR(80) NOT NULL DEFAULT 'hostinger',
        notas NVARCHAR(MAX) NOT NULL DEFAULT '',
        detalle_json NVARCHAR(MAX) NOT NULL DEFAULT '',
        estado_sync NVARCHAR(30) NOT NULL DEFAULT 'SINCRONIZADO',
        fecha_creacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioApp ON dbo.AppMovilRegistro(folio_app);
END;

IF OBJECT_ID('dbo.AppMovilFolioControl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilFolioControl (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilFolioControl PRIMARY KEY,
        FolioAppOriginal NVARCHAR(60) NOT NULL,
        FolioControl NVARCHAR(60) NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilFolioControl_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Original ON dbo.AppMovilFolioControl(FolioAppOriginal);
    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Control ON dbo.AppMovilFolioControl(FolioControl);
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_app_original') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD folio_app_original NVARCHAR(60) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_pos') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD folio_pos NVARCHAR(100) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'id_catalogo') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD id_catalogo INT NULL;
IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_gafete') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD folio_gafete NVARCHAR(300) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_taxista') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD telefono_taxista NVARCHAR(30) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_contacto') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD telefono_contacto NVARCHAR(30) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'nacionalidad') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD nacionalidad NVARCHAR(120) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'placas') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD placas NVARCHAR(50) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'modelo_vehiculo') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD modelo_vehiculo NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'unidad') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD unidad NVARCHAR(50) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'sitio') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD sitio NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'destino') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD destino NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'estado_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD estado_pago_dejada NVARCHAR(20) NOT NULL DEFAULT 'pendiente';
IF COL_LENGTH('dbo.AppMovilRegistro', 'fecha_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD fecha_pago_dejada DATETIME2 NULL;
IF COL_LENGTH('dbo.AppMovilRegistro', 'usuario_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD usuario_pago_dejada NVARCHAR(180) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'ticket_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD ticket_pago_dejada NVARCHAR(80) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_status') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_status NVARCHAR(30) NOT NULL DEFAULT 'pendiente';
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_date') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_date DATETIME2 NULL;
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_user') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_user NVARCHAR(80) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_ticket') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_ticket NVARCHAR(80) NOT NULL DEFAULT '';

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistro') AND name = 'UX_AppMovilRegistro_FolioApp')
   AND NOT EXISTS (
        SELECT folio_app
        FROM dbo.AppMovilRegistro
        WHERE NULLIF(LTRIM(RTRIM(folio_app)), '') IS NOT NULL
        GROUP BY folio_app
        HAVING COUNT(1) > 1
   )
BEGIN
    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioApp ON dbo.AppMovilRegistro(folio_app)
    WHERE folio_app IS NOT NULL AND folio_app <> '';
END;

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistro') AND name = 'UX_AppMovilRegistro_FolioOriginal')
   AND NOT EXISTS (
        SELECT folio_app_original
        FROM dbo.AppMovilRegistro
        WHERE NULLIF(LTRIM(RTRIM(folio_app_original)), '') IS NOT NULL
        GROUP BY folio_app_original
        HAVING COUNT(1) > 1
   )
BEGIN
    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioOriginal ON dbo.AppMovilRegistro(folio_app_original)
    WHERE folio_app_original IS NOT NULL AND folio_app_original <> '';
END;

IF OBJECT_ID('dbo.AppMovilRegistroGafetes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilRegistroGafetes (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistroGafetes PRIMARY KEY,
        FolioApp NVARCHAR(60) NOT NULL,
        IdCatalogo INT NULL,
        FolioGafete NVARCHAR(20) NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilRegistroGafetes_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppMovilRegistroGafetes_FolioApp_Gafete ON dbo.AppMovilRegistroGafetes(FolioApp, FolioGafete);
    CREATE INDEX IX_AppMovilRegistroGafetes_Taxista ON dbo.AppMovilRegistroGafetes(IdCatalogo, FolioGafete);
END;

IF OBJECT_ID('dbo.AppMovilFoliosBloqueados', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilFoliosBloqueados (
        Folio NVARCHAR(60) NOT NULL CONSTRAINT PK_AppMovilFoliosBloqueados PRIMARY KEY,
        Motivo NVARCHAR(200) NOT NULL DEFAULT '',
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilFoliosBloqueados_Fecha DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID('dbo.gafete', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.gafete (
        matricula NVARCHAR(50) NULL,
        gafete INT NOT NULL,
        fecha DATETIME NULL,
        venta NVARCHAR(10) NULL,
        hora DATETIME NULL,
        folioperacion NVARCHAR(50) NULL
    );
    CREATE INDEX IX_gafete_folio_gafete ON dbo.gafete(folioperacion, gafete);
    CREATE INDEX IX_gafete_fecha ON dbo.gafete(fecha);
END;

IF OBJECT_ID('dbo.dejadas', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'nacionalidad') IS NULL
BEGIN
    ALTER TABLE dbo.dejadas ADD nacionalidad NVARCHAR(120) NULL;
END;

IF OBJECT_ID('dbo.dejadas', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'folioregistrostr') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'gafete') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'fecha') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.dejadas') AND name = 'UX_dejadas_AppMovil_Folio_Gafete_Fecha')
   AND NOT EXISTS (
        SELECT folioregistrostr, gafete, CONVERT(date, fecha)
        FROM dbo.dejadas
        WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioregistrostr, ''))), '') IS NOT NULL
          AND NULLIF(LTRIM(RTRIM(COALESCE(gafete, ''))), '') IS NOT NULL
        GROUP BY folioregistrostr, gafete, CONVERT(date, fecha)
        HAVING COUNT(1) > 1
   )
BEGIN
    CREATE UNIQUE INDEX UX_dejadas_AppMovil_Folio_Gafete_Fecha
    ON dbo.dejadas(folioregistrostr, gafete, fecha)
    WHERE folioregistrostr IS NOT NULL AND gafete IS NOT NULL;
END;

IF OBJECT_ID('dbo.gafete', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.gafete', 'folioperacion') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gafete') AND name = 'UX_gafete_AppMovil_Folio_Gafete_Fecha_A')
   AND NOT EXISTS (
        SELECT folioperacion, gafete, CONVERT(date, fecha)
        FROM dbo.gafete
        WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioperacion, ''))), '') IS NOT NULL
          AND UPPER(LTRIM(RTRIM(COALESCE(venta, '')))) = 'A'
        GROUP BY folioperacion, gafete, CONVERT(date, fecha)
        HAVING COUNT(1) > 1
   )
BEGIN
    CREATE UNIQUE INDEX UX_gafete_AppMovil_Folio_Gafete_Fecha_A
    ON dbo.gafete(folioperacion, gafete, fecha)
    WHERE folioperacion IS NOT NULL AND venta = 'A';
END;
"@
    [void]$command.ExecuteNonQuery()
}

function Resolve-FolioControl($Connection, [string]$RecordId) {
    $existing = $Connection.CreateCommand()
    $existing.CommandText = @"
SELECT TOP (1) FolioControl
FROM dbo.AppMovilFolioControl
WHERE FolioAppOriginal = @recordId;
"@
    Add-Parameter $existing "@recordId" $RecordId
    $current = $existing.ExecuteScalar()
    if ($null -ne $current -and $current -ne [DBNull]::Value -and -not [string]::IsNullOrWhiteSpace([string]$current)) {
        return [string]$current
    }

    $next = $Connection.CreateCommand()
    $next.CommandText = @"
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRANSACTION;

DECLARE @existente NVARCHAR(60);
SELECT TOP (1) @existente = FolioControl
FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK)
WHERE FolioAppOriginal = @recordId;

IF NULLIF(LTRIM(RTRIM(@existente)), '') IS NOT NULL
BEGIN
    COMMIT TRANSACTION;
    SELECT @existente;
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK) WHERE FolioControl = @recordId OR FolioAppOriginal = @recordId)
   AND NOT EXISTS (SELECT 1 FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK) WHERE folio_app = @recordId OR folio_app_original = @recordId)
   AND NOT EXISTS (SELECT 1 FROM dbo.AppMovilFoliosBloqueados WITH (UPDLOCK, HOLDLOCK) WHERE Folio = @recordId)
BEGIN
    INSERT INTO dbo.AppMovilFolioControl (FolioAppOriginal, FolioControl)
    VALUES (@recordId, @recordId);

    COMMIT TRANSACTION;
    SELECT @recordId;
    RETURN;
END;

DECLARE @ultimo INT;
SELECT @ultimo = ISNULL(MAX(
    CASE
        WHEN ISNUMERIC(FolioControl) = 1 THEN CAST(FolioControl AS INT)
        WHEN FolioControl LIKE 'AP%' AND ISNUMERIC(SUBSTRING(FolioControl, 3, 20)) = 1 THEN CAST(SUBSTRING(FolioControl, 3, 20) AS INT)
        ELSE 0
    END), 0)
FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK);
 
SELECT @ultimo = CASE WHEN AppMax.MaxFolio > @ultimo THEN AppMax.MaxFolio ELSE @ultimo END
FROM (
    SELECT ISNULL(MAX(
        CASE
            WHEN ISNUMERIC(folio_app) = 1 THEN CAST(folio_app AS INT)
            WHEN ISNUMERIC(folio_app_original) = 1 THEN CAST(folio_app_original AS INT)
            ELSE 0
        END), 0) AS MaxFolio
    FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
) AppMax;

DECLARE @folioControl NVARCHAR(60) = RIGHT('0000' + CONVERT(NVARCHAR(20), @ultimo + 1), 4);

WHILE EXISTS (SELECT 1 FROM dbo.AppMovilFolioControl WHERE FolioControl = @folioControl OR FolioAppOriginal = @folioControl)
   OR EXISTS (SELECT 1 FROM dbo.AppMovilRegistro WHERE folio_app = @folioControl OR folio_app_original = @folioControl)
   OR EXISTS (SELECT 1 FROM dbo.AppMovilFoliosBloqueados WHERE Folio = @folioControl)
BEGIN
    SET @ultimo = @ultimo + 1;
    SET @folioControl = RIGHT('0000' + CONVERT(NVARCHAR(20), @ultimo + 1), 4);
END;

INSERT INTO dbo.AppMovilFolioControl (FolioAppOriginal, FolioControl)
VALUES (@recordId, @folioControl);

COMMIT TRANSACTION;
SELECT @folioControl;
"@
    Add-Parameter $next "@recordId" $RecordId
    return [string]$next.ExecuteScalar()
}

function Test-FolioBlocked($Connection, [string]$RecordId) {
    if ([string]::IsNullOrWhiteSpace($RecordId)) { return $false }
    $command = $Connection.CreateCommand()
    $command.CommandText = @"
IF OBJECT_ID('dbo.AppMovilFoliosBloqueados', 'U') IS NULL
    SELECT 0;
ELSE
    SELECT CASE WHEN EXISTS (
        SELECT 1
        FROM dbo.AppMovilFoliosBloqueados
        WHERE Folio = @recordId
           OR (ISNUMERIC(Folio) = 1 AND ISNUMERIC(@recordId) = 1 AND CONVERT(bigint, Folio) = CONVERT(bigint, @recordId))
    ) THEN 1 ELSE 0 END;
"@
    Add-Parameter $command "@recordId" $RecordId
    return ([int]$command.ExecuteScalar()) -gt 0
}

function Save-AppMobileRegistro($Connection, [string]$FolioControl, $Record, [decimal]$TripCost, [decimal]$CashAmount, [decimal]$CardAmount) {
    $localSite = Resolve-LocalOperationSite $Record
    $command = $Connection.CreateCommand()
    $command.CommandText = @"
IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NULL RETURN;

IF EXISTS (
    SELECT 1
    FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
    WHERE folio_app = @folioControl
       OR folio_app_original = @folioOriginal
)
BEGIN
    UPDATE dbo.AppMovilRegistro
    SET folio_app_original = @folioOriginal,
        id_catalogo = @catalogId,
        folio_gafete = @badgeId,
        folio_pos = @ticket,
        fecha_operacion = @recordDate,
        vendedor_nombre = @driverName,
        telefono_taxista = @driverPhone,
        telefono_contacto = @contactPhone,
        nacionalidad = @nationality,
        placas = @plate,
        modelo_vehiculo = @vehicleModel,
        unidad = @unitNumber,
        hotel = @hotel,
        origen = @origin,
        sitio = @site,
        destino = @destination,
        pax = @passengerCount,
        tipo_operacion = @serviceType,
        total = @tripCost,
        efectivo = @cashAmount,
        tarjeta = @cardAmount,
        usuario_movil = @mobileUser,
        notas = @notes,
        estado_sync = 'SINCRONIZADO',
        payout_status = @payoutStatus,
        payout_date = @payoutDate,
        payout_user = @payoutUser,
        payout_ticket = @payoutTicket,
        estado_pago_dejada = @payoutStatus,
        fecha_pago_dejada = @payoutDate,
        usuario_pago_dejada = @payoutUser,
        ticket_pago_dejada = @payoutTicket
    WHERE folio_app = @folioControl
       OR folio_app_original = @folioOriginal;
END
ELSE
BEGIN
    INSERT INTO dbo.AppMovilRegistro
    (
        folio_app, folio_app_original, id_catalogo, folio_gafete, folio_pos, fecha_operacion,
        vendedor_nombre, telefono_taxista, telefono_contacto, nacionalidad, placas, modelo_vehiculo,
        unidad, hotel, origen, sitio, destino, pax, tipo_operacion, total, efectivo, tarjeta,
        usuario_movil, notas, detalle_json, estado_sync,
        payout_status, payout_date, payout_user, payout_ticket,
        estado_pago_dejada, fecha_pago_dejada, usuario_pago_dejada, ticket_pago_dejada
    )
    VALUES
    (
        @folioControl, @folioOriginal, @catalogId, @badgeId, @ticket, @recordDate,
        @driverName, @driverPhone, @contactPhone, @nationality, @plate, @vehicleModel,
        @unitNumber, @hotel, @origin, @site, @destination, @passengerCount, @serviceType, @tripCost, @cashAmount, @cardAmount,
        @mobileUser, @notes, @detailJson, 'SINCRONIZADO',
        @payoutStatus, @payoutDate, @payoutUser, @payoutTicket,
        @payoutStatus, @payoutDate, @payoutUser, @payoutTicket
    );
END;
"@
    Add-Parameter $command "@folioControl" (Text-Max $FolioControl 60)
    Add-Parameter $command "@folioOriginal" (Text-Max $Record.recordId 60)
    Add-Parameter $command "@catalogId" (Int-Value $Record.catalogId)
    Add-Parameter $command "@badgeId" (Text-Max $Record.badgeId 300)
    Add-Parameter $command "@ticket" (Text-Max (Text $Record.ticketSale) 100)
    Add-Parameter $command "@recordDate" (Record-DateTimeValue $Record)
    Add-Parameter $command "@driverName" (Text-Max $Record.driverName 150)
    Add-Parameter $command "@driverPhone" (Text-Max $Record.driverPhone 30)
    Add-Parameter $command "@contactPhone" (Text-Max $Record.contactPhone 30)
    Add-Parameter $command "@nationality" (Text-Max $Record.nationality 120)
    Add-Parameter $command "@plate" (Text-Max $Record.plate 50)
    Add-Parameter $command "@vehicleModel" (Text-Max $Record.vehicleModel 150)
    Add-Parameter $command "@unitNumber" (Text-Max $Record.unitNumber 50)
    Add-Parameter $command "@hotel" (Text-Max $Record.hotel 200)
    Add-Parameter $command "@origin" (Text-Max $Record.origin 150)
    Add-Parameter $command "@site" (Text-Max $localSite 150)
    Add-Parameter $command "@destination" (Text-Max $Record.destination 150)
    Add-Parameter $command "@passengerCount" (Int-Value $Record.passengerCount)
    Add-Parameter $command "@serviceType" (Text-Max $Record.serviceType 80)
    Add-Parameter $command "@tripCost" $TripCost
    Add-Parameter $command "@cashAmount" $CashAmount
    Add-Parameter $command "@cardAmount" $CardAmount
    $mobileUser = Text $Record.userName
    if ([string]::IsNullOrWhiteSpace($mobileUser)) { $mobileUser = Text $Record.usuarioMovil }
    if ([string]::IsNullOrWhiteSpace($mobileUser)) { $mobileUser = "hostinger" }

    $payout = Resolve-PayoutMirror $Record

    Add-Parameter $command "@mobileUser" (Text-Max $mobileUser 80)
    Add-Parameter $command "@notes" (Text $Record.notes)
    Add-Parameter $command "@detailJson" (($Record | ConvertTo-Json -Depth 20 -Compress))
    Add-Parameter $command "@payoutStatus" (Text-Max $payout.Status 30)
    Add-Parameter $command "@payoutDate" $payout.Date
    Add-Parameter $command "@payoutUser" $payout.User
    Add-Parameter $command "@payoutTicket" $payout.Ticket
    [void]$command.ExecuteNonQuery()
}

function Save-DejadaInsertOnly($Connection, [string]$FolioControl, $Record, [decimal]$TripCost, [decimal]$CashAmount, [decimal]$CardAmount) {
    $localSite = Resolve-LocalOperationSite $Record
    $badges = @(Split-Badges $Record.badgeId)
    if ($badges.Count -eq 0) {
        $badges = @("")
    }

    $payout = Resolve-PayoutMirror $Record

    foreach ($badge in $badges) {
        try {
        $command = $Connection.CreateCommand()
        $command.CommandText = @"
IF OBJECT_ID('dbo.dejadas', 'U') IS NULL RETURN;
IF COL_LENGTH('dbo.dejadas', 'nacionalidad') IS NULL ALTER TABLE dbo.dejadas ADD nacionalidad NVARCHAR(120) NOT NULL DEFAULT '';

DECLARE @folioBigint BIGINT = CASE WHEN ISNUMERIC(@folioControl) = 1 THEN CAST(@folioControl AS BIGINT) ELSE 0 END;
DECLARE @horaTexto NVARCHAR(20) = CONVERT(NVARCHAR(20), CONVERT(TIME, @recordDate), 100);

IF EXISTS (
    SELECT 1
    FROM dbo.dejadas WITH (UPDLOCK, HOLDLOCK)
    WHERE (
        LTRIM(RTRIM(COALESCE(idstaff, ''))) = @folioControl
        OR LTRIM(RTRIM(COALESCE(folioregistrostr, ''))) = @folioControl
        OR LTRIM(RTRIM(COALESCE(codigorecepcion, ''))) = @folioControl
    )
    AND CONVERT(date, fecha) <> CONVERT(date, @recordDate)
)
BEGIN
    RETURN;
END;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.dejadas WITH (UPDLOCK, HOLDLOCK, TABLOCKX)
    WHERE (
        (
            LTRIM(RTRIM(COALESCE(idstaff, ''))) = @folioControl
            OR LTRIM(RTRIM(COALESCE(folioregistrostr, ''))) = @folioControl
            OR LTRIM(RTRIM(COALESCE(codigorecepcion, ''))) = @folioControl
        )
        AND CONVERT(date, fecha) = CONVERT(date, @recordDate)
        AND UPPER(LTRIM(RTRIM(COALESCE(gafete, '')))) = UPPER(LTRIM(RTRIM(@badgeId)))
    )
    OR (
        UPPER(LTRIM(RTRIM(COALESCE(nombrestaff, '')))) = UPPER(LTRIM(RTRIM(@driverName)))
        AND ABS(DATEDIFF(MINUTE, fecha, @recordDate)) <= 2
        AND UPPER(LTRIM(RTRIM(COALESCE(hotel, '')))) = UPPER(LTRIM(RTRIM(@hotel)))
        AND UPPER(LTRIM(RTRIM(COALESCE(gafete, '')))) = UPPER(LTRIM(RTRIM(@badgeId)))
        AND UPPER(LTRIM(RTRIM(COALESCE(tipotransporte, '')))) = UPPER(LTRIM(RTRIM(@serviceType)))
        AND ABS(COALESCE(total, 0) - @tripCost) < 0.01
    )
)
BEGIN
    INSERT INTO dbo.dejadas (
        idstaff,nombrestaff,nombrealmacen,idalmacen,fecha,hora,idcajero,nombrecajero,total,
        codigorecepcion,folioregistro,folioregistrostr,unidad,pax,hotel,nombrevendedor,
        tipotransporte,telefono,horaentrada,horasalida,totalventa,comision,pago,
        totalefectivo,totaltarjeta,totalgastos,gafete,nacionalidad
    ) VALUES (
        @folioControl,LEFT(@driverName, 100),LEFT(@site, 100),1,CONVERT(DATETIME, CONVERT(DATE, @recordDate)),@horaTexto,0,'APP MOVIL',@tripCost,
        @folioControl,COALESCE(@folioBigint, 0),@folioControl,LEFT(@unitNumber, 20),@passengerCount,LEFT(@hotel, 100),LEFT(@driverName, 100),
        LEFT(@serviceType, 10),LEFT(@driverPhone, 12),@horaTexto,@horaTexto,0,0,@pagoImporte,
        @cashAmount,@cardAmount,0,LEFT(@badgeId, 10),LEFT(@nationality, 120)
    );
END
ELSE
BEGIN
    UPDATE dbo.dejadas
    SET nacionalidad = CASE
            WHEN NULLIF(LTRIM(RTRIM(@nationality)), '') IS NULL THEN nacionalidad
            ELSE LEFT(@nationality, 120)
        END,
        pago = CASE
            WHEN @pagoImporte > 0 THEN @pagoImporte
            ELSE pago
        END,
        fechapago = CASE
            WHEN @pagoImporte > 0 AND @payoutDate IS NOT NULL THEN @payoutDate
            WHEN @pagoImporte > 0 AND @payoutDate IS NULL AND fechapago IS NULL THEN @recordDate
            ELSE fechapago
        END
    WHERE (
        (
            LTRIM(RTRIM(COALESCE(idstaff, ''))) = @folioControl
            OR LTRIM(RTRIM(COALESCE(folioregistrostr, ''))) = @folioControl
            OR LTRIM(RTRIM(COALESCE(codigorecepcion, ''))) = @folioControl
        )
        AND CONVERT(date, fecha) = CONVERT(date, @recordDate)
        AND UPPER(LTRIM(RTRIM(COALESCE(gafete, '')))) = UPPER(LTRIM(RTRIM(@badgeId)))
    )
    OR (
        UPPER(LTRIM(RTRIM(COALESCE(nombrestaff, '')))) = UPPER(LTRIM(RTRIM(@driverName)))
        AND ABS(DATEDIFF(MINUTE, fecha, @recordDate)) <= 2
        AND UPPER(LTRIM(RTRIM(COALESCE(hotel, '')))) = UPPER(LTRIM(RTRIM(@hotel)))
        AND UPPER(LTRIM(RTRIM(COALESCE(gafete, '')))) = UPPER(LTRIM(RTRIM(@badgeId)))
        AND UPPER(LTRIM(RTRIM(COALESCE(tipotransporte, '')))) = UPPER(LTRIM(RTRIM(@serviceType)))
        AND ABS(COALESCE(total, 0) - @tripCost) < 0.01
    );
END;
"@
        Add-Parameter $command "@folioControl" (Text-Max $FolioControl 50)
        Add-Parameter $command "@recordDate" (Record-DateTimeValue $Record)
        Add-Parameter $command "@driverName" (Text-Max $Record.driverName 100)
        Add-Parameter $command "@site" (Text-Max $localSite 100)
        Add-Parameter $command "@tripCost" $TripCost
        Add-Parameter $command "@unitNumber" (Text-Max $Record.unitNumber 20)
        Add-Parameter $command "@passengerCount" (Int-Value $Record.passengerCount)
        Add-Parameter $command "@hotel" (Text-Max $Record.hotel 100)
        Add-Parameter $command "@serviceType" (Text-Max $Record.serviceType 10)
        Add-Parameter $command "@driverPhone" (Text-Max $Record.driverPhone 12)
        Add-Parameter $command "@nationality" (Text-Max $Record.nationality 120)
        Add-Parameter $command "@cashAmount" $CashAmount
        Add-Parameter $command "@cardAmount" $CardAmount
        Add-Parameter $command "@pagoImporte" $(if ($payout.IsPaid) { $TripCost } else { [decimal]0 })
        Add-Parameter $command "@payoutDate" $payout.Date
        Add-Parameter $command "@badgeId" (Text-Max $badge 10)
        Add-Parameter $command "@catalogId" (Int-Value $Record.catalogId)
        [void]$command.ExecuteNonQuery()
        } catch {
            Write-Warning "No se pudo insertar dejada nueva para folio ${FolioControl}, gafete ${badge}: $($_.Exception.Message)"
        }
    }
}

function Save-GafeteInsertOnly($Connection, [string]$FolioControl, $Record) {
    $rawBadge = Text $Record.badgeId
    if ([string]::IsNullOrWhiteSpace($rawBadge)) { return }

    $badges = @(Split-Badges $rawBadge)
    foreach ($badge in $badges) {
        $badgeNumber = 0
        if (-not [int]::TryParse($badge, [ref]$badgeNumber)) {
            Write-Warning "Gafete no numerico omitido para folio ${FolioControl}: $badge"
            continue
        }

        try {
            $command = $Connection.CreateCommand()
            $command.CommandText = @"
IF OBJECT_ID('dbo.gafete', 'U') IS NULL RETURN;
IF COL_LENGTH('dbo.gafete', 'folioperacion') IS NULL ALTER TABLE dbo.gafete ADD folioperacion NVARCHAR(50) NULL;

IF NOT EXISTS (
    SELECT 1
    FROM dbo.gafete WITH (UPDLOCK, HOLDLOCK)
    WHERE (
          COALESCE(CONVERT(NVARCHAR(50), matricula), '') = @folioControl
          OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), '') = @folioControl
      )
      AND gafete = @badgeNumber
      AND CONVERT(date, fecha) = CONVERT(date, @recordDate)
)
AND NOT EXISTS (
    SELECT 1
    FROM dbo.gafete WITH (UPDLOCK, HOLDLOCK)
    WHERE (
          COALESCE(CONVERT(NVARCHAR(50), matricula), '') = @folioControl
          OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), '') = @folioControl
      )
      AND gafete = @badgeNumber
      AND UPPER(COALESCE(venta, '')) IN ('R', 'S')
)
BEGIN
    INSERT INTO dbo.gafete (matricula,gafete,fecha,venta,hora,folioperacion)
    VALUES (@folioControl,@badgeNumber,CONVERT(DATETIME, CONVERT(DATE, @recordDate)),'A',@recordDate,@folioControl);
END;
"@
            Add-Parameter $command "@badgeNumber" $badgeNumber
            Add-Parameter $command "@catalogId" (Int-Value $Record.catalogId)
            Add-Parameter $command "@recordDate" (Record-DateTimeValue $Record)
            Add-Parameter $command "@folioControl" (Text-Max $FolioControl 50)
            [void]$command.ExecuteNonQuery()
        } catch {
            Write-Warning "No se pudo insertar gafete nuevo para folio ${FolioControl}, gafete ${badge}: $($_.Exception.Message)"
        }
    }
}

function Save-AppMobileRecordGafetes($Connection, [string]$FolioControl, $Record) {
    $badges = @(Split-Badges $Record.badgeId)
    if ($badges.Count -eq 0) { return }

    foreach ($badge in $badges) {
        try {
            $command = $Connection.CreateCommand()
            $command.CommandText = @"
IF OBJECT_ID('dbo.AppMovilRegistroGafetes', 'U') IS NULL RETURN;
INSERT INTO dbo.AppMovilRegistroGafetes (FolioApp, IdCatalogo, FolioGafete)
SELECT @folioControl, @catalogId, @badgeId
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.AppMovilRegistroGafetes
    WHERE FolioApp = @folioControl
      AND UPPER(LTRIM(RTRIM(FolioGafete))) = UPPER(LTRIM(RTRIM(@badgeId)))
);
"@
            Add-Parameter $command "@folioControl" (Text-Max $FolioControl 60)
            Add-Parameter $command "@catalogId" (Int-Value $Record.catalogId)
            Add-Parameter $command "@badgeId" (Text-Max $badge 20)
            [void]$command.ExecuteNonQuery()
        } catch {
            Write-Warning "No se pudo relacionar gafete ${badge} con folio ${FolioControl}: $($_.Exception.Message)"
        }
    }
}

function Save-RelationTicketTaxista($Connection, [string]$FolioControl, $Record, [decimal]$TripCost) {
    try {
        $command = $Connection.CreateCommand()
        $command.CommandText = @"
IF OBJECT_ID('dbo.RelacionTicketTaxista', 'U') IS NULL RETURN;

IF EXISTS (
    SELECT 1
    FROM dbo.RelacionTicketTaxista WITH (UPDLOCK, HOLDLOCK)
    WHERE FolioApp = @folioOriginal
       OR FolioOperacion = @folioControl
)
BEGIN
    UPDATE dbo.RelacionTicketTaxista
    SET FolioOperacion = @folioControl,
        FolioPos = @folioPos,
        Gafete = @badgeId,
        TaxistaId = @catalogId,
        TaxistaNombre = @driverName,
        Vendedor = @sellerName,
        TransporteTipo = @serviceType,
        Dejada = @tripCost,
        Observaciones = @notes,
        Usuario = @userName,
        FechaActualizacion = SYSUTCDATETIME()
    WHERE FolioApp = @folioOriginal
       OR FolioOperacion = @folioControl;
END
ELSE
BEGIN
    INSERT INTO dbo.RelacionTicketTaxista
        (FolioApp, FolioOperacion, FolioPos, Gafete, TaxistaId, TaxistaNombre, Vendedor, TransporteTipo, Dejada, Observaciones, Usuario)
    VALUES
        (@folioOriginal, @folioControl, @folioPos, @badgeId, @catalogId, @driverName, @sellerName, @serviceType, @tripCost, @notes, @userName);
END;
"@
        Add-Parameter $command "@folioOriginal" (Text-Max $Record.recordId 60)
        Add-Parameter $command "@folioControl" (Text-Max $FolioControl 60)
        Add-Parameter $command "@folioPos" (Text-Max (Text $Record.ticketSale) 100)
        Add-Parameter $command "@badgeId" (Text-Max $Record.badgeId 300)
        Add-Parameter $command "@catalogId" (Int-Value $Record.catalogId)
        Add-Parameter $command "@driverName" (Text-Max $Record.driverName 150)
        Add-Parameter $command "@sellerName" (Text-Max $Record.sellerName 150)
        Add-Parameter $command "@serviceType" (Text-Max $Record.serviceType 80)
        Add-Parameter $command "@tripCost" $TripCost
        Add-Parameter $command "@notes" (Text-Max $Record.notes 300)
        Add-Parameter $command "@userName" "HOSTINGER_SYNC"
        [void]$command.ExecuteNonQuery()
    } catch {
        Write-Warning "No se pudo guardar RelacionTicketTaxista para folio ${FolioControl}: $($_.Exception.Message)"
    }
}

function Test-AppMobileRecordSaved($Connection, [string]$FolioControl, $Record) {
    $dejada = $Connection.CreateCommand()
    $dejada.CommandText = @"
SELECT COUNT(1)
FROM dbo.dejadas
WHERE (
        LTRIM(RTRIM(COALESCE(idstaff, ''))) = @folioControl
     OR LTRIM(RTRIM(COALESCE(folioregistrostr, ''))) = @folioControl
     OR LTRIM(RTRIM(COALESCE(codigorecepcion, ''))) = @folioControl
  )
  AND CONVERT(date, fecha) = CONVERT(date, @recordDate);
"@
    Add-Parameter $dejada "@folioControl" (Text-Max $FolioControl 50)
    Add-Parameter $dejada "@recordDate" (Record-DateTimeValue $Record)
    $dejadaOk = ([int]$dejada.ExecuteScalar()) -gt 0

    $badges = @(Split-Badges $Record.badgeId)
    if ($badges.Count -eq 0) {
        return $dejadaOk
    }

    foreach ($badge in $badges) {
        $badgeNumber = 0
        if (-not [int]::TryParse($badge, [ref]$badgeNumber)) {
            $appBadge = $Connection.CreateCommand()
            $appBadge.CommandText = @"
IF OBJECT_ID('dbo.AppMovilRegistroGafetes', 'U') IS NULL
    SELECT 0;
ELSE
    SELECT COUNT(1)
    FROM dbo.AppMovilRegistroGafetes
    WHERE FolioApp = @folioControl
      AND UPPER(LTRIM(RTRIM(FolioGafete))) = UPPER(LTRIM(RTRIM(@badgeId)));
"@
            Add-Parameter $appBadge "@folioControl" (Text-Max $FolioControl 60)
            Add-Parameter $appBadge "@badgeId" (Text-Max $badge 20)
            $appBadgeOk = ([int]$appBadge.ExecuteScalar()) -gt 0
            if ($appBadgeOk) {
                continue
            }
            return $false
        }

        $dejadaBadge = $Connection.CreateCommand()
        $dejadaBadge.CommandText = @"
SELECT COUNT(1)
FROM dbo.dejadas
WHERE (
        LTRIM(RTRIM(COALESCE(idstaff, ''))) = @folioControl
     OR LTRIM(RTRIM(COALESCE(folioregistrostr, ''))) = @folioControl
     OR LTRIM(RTRIM(COALESCE(codigorecepcion, ''))) = @folioControl
  )
  AND CONVERT(date, fecha) = CONVERT(date, @recordDate)
  AND UPPER(LTRIM(RTRIM(COALESCE(gafete, '')))) = UPPER(LTRIM(RTRIM(@badgeId)));
"@
        Add-Parameter $dejadaBadge "@folioControl" (Text-Max $FolioControl 50)
        Add-Parameter $dejadaBadge "@recordDate" (Record-DateTimeValue $Record)
        Add-Parameter $dejadaBadge "@badgeId" (Text-Max $badge 10)
        $dejadaBadgeOk = ([int]$dejadaBadge.ExecuteScalar()) -gt 0
        if (-not $dejadaBadgeOk) {
            return $false
        }

        $gafete = $Connection.CreateCommand()
        $gafete.CommandText = @"
IF OBJECT_ID('dbo.gafete', 'U') IS NULL
    SELECT 0;
ELSE
    SELECT COUNT(1)
    FROM dbo.gafete
    WHERE (
          COALESCE(CONVERT(NVARCHAR(50), matricula), '') = @folioControl
          OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), '') = @folioControl
      )
      AND gafete = @badgeNumber
      AND CONVERT(date, fecha) = CONVERT(date, @recordDate)
      AND (
            UPPER(COALESCE(venta, '')) NOT IN ('R', 'S')
            OR COALESCE(CONVERT(NVARCHAR(50), matricula), '') = @folioControl
            OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), '') = @folioControl
      );
"@
        Add-Parameter $gafete "@folioControl" (Text-Max $FolioControl 50)
        Add-Parameter $gafete "@recordDate" (Record-DateTimeValue $Record)
        Add-Parameter $gafete "@badgeNumber" $badgeNumber
        $gafeteOk = ([int]$gafete.ExecuteScalar()) -gt 0
        if (-not $gafeteOk) {
            return $false
        }
    }

    return $dejadaOk
}

function Test-AppMobileRegistroSaved($Connection, [string]$FolioControl, $Record) {
    $command = $Connection.CreateCommand()
    $command.CommandText = @"
SELECT COUNT(1)
FROM dbo.AppMovilRegistro
WHERE folio_app = @folioControl
   OR folio_app_original = @folioOriginal;
"@
    Add-Parameter $command "@folioControl" (Text-Max $FolioControl 60)
    Add-Parameter $command "@folioOriginal" (Text-Max $Record.recordId 60)
    return ([int]$command.ExecuteScalar()) -gt 0
}

function Save-AppMobileTripRecords($Records) {
    if (@($Records).Count -eq 0) { return @() }

    $connection = New-SqlConnection
    $savedIds = New-Object System.Collections.Generic.List[string]
    try {
        Ensure-AppTables $connection

        foreach ($record in @($Records)) {
            $recordId = Text $record.recordId
            if ([string]::IsNullOrWhiteSpace($recordId)) { continue }
            if (Test-FolioBlocked $connection $recordId) {
                Write-Host "Folio ${recordId} bloqueado; se omite para evitar duplicados."
                continue
            }

            $folioControl = Resolve-FolioControl $connection $recordId
            $tripCost = Decimal-Value $record.tripCost
            $paymentMethod = (Text $record.paymentMethod).ToUpperInvariant()
            $cashAmount = $tripCost
            $cardAmount = [decimal]0
            if ($paymentMethod.Contains("TARJETA")) {
                $cashAmount = [decimal]0
                $cardAmount = $tripCost
            }

            Save-AppMobileRegistro $connection $folioControl $record $tripCost $cashAmount $cardAmount
            Save-DejadaInsertOnly $connection $folioControl $record $tripCost $cashAmount $cardAmount
            Save-GafeteInsertOnly $connection $folioControl $record
            Save-AppMobileRecordGafetes $connection $folioControl $record
            Save-RelationTicketTaxista $connection $folioControl $record $tripCost
            $verified = $false
            if ($isCascoApi) {
                $verified = (Test-AppMobileRegistroSaved $connection $folioControl $record)
            } else {
                $verified = (Test-AppMobileRecordSaved $connection $folioControl $record)
            }

            if ($verified) {
                Write-Host "Folio ${folioControl} verificado en dejadas/gafete."
                [void]$savedIds.Add($recordId)
            } else {
                Write-Warning "Folio ${folioControl} NO quedo verificado en dejadas/gafete. No se marcara como sincronizado en Hostinger."
            }
        }
    } finally {
        $connection.Close()
    }

    return @($savedIds)
}

function Sync-RecentReturnedGafetesToHostinger {
    $connection = New-SqlConnection
    try {
        Ensure-AppTables $connection

        $command = $connection.CreateCommand()
        $command.CommandText = @"
IF OBJECT_ID('dbo.gafete', 'U') IS NULL
BEGIN
    SELECT TOP (0)
        CONVERT(NVARCHAR(80), '') AS BadgeId,
        CONVERT(NVARCHAR(10), '') AS Status,
        GETDATE() AS CreatedAt;
    RETURN;
END;

IF COL_LENGTH('dbo.gafete', 'folioperacion') IS NULL
    ALTER TABLE dbo.gafete ADD folioperacion NVARCHAR(50) NULL;

;WITH latest AS
(
    SELECT
        CONVERT(NVARCHAR(80), gafete) AS BadgeId,
        UPPER(LTRIM(RTRIM(COALESCE(venta, '')))) AS Venta,
        COALESCE(hora, fecha, GETDATE()) AS MovimientoFecha,
        ROW_NUMBER() OVER
        (
            PARTITION BY CONVERT(NVARCHAR(80), gafete)
            ORDER BY
                COALESCE(hora, fecha, GETDATE()) DESC,
                CASE UPPER(LTRIM(RTRIM(COALESCE(venta, ''))))
                    WHEN 'R' THEN 0
                    WHEN 'S' THEN 1
                    WHEN 'A' THEN 2
                    ELSE 3
                END
        ) AS rn
    FROM dbo.gafete
    WHERE gafete IS NOT NULL
      AND COALESCE(hora, fecha, GETDATE()) >= DATEADD(DAY, -14, GETDATE())
)
SELECT TOP (300)
    BadgeId,
    Venta AS Status,
    MovimientoFecha AS CreatedAt
FROM latest
WHERE rn = 1
  AND Venta IN ('R', 'S')
ORDER BY MovimientoFecha DESC;
"@
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        $table = New-Object System.Data.DataTable
        [void]$adapter.Fill($table)

        $items = @()
        foreach ($row in $table.Rows) {
            $badge = Text $row["BadgeId"]
            $status = (Text $row["Status"]).ToUpperInvariant()
            if ([string]::IsNullOrWhiteSpace($badge) -or ($status -ne "R" -and $status -ne "S")) { continue }

            $items += [ordered]@{
                badgeId = $badge.Trim()
                barcode = $badge.Trim()
                status = $status
                cycle = 1
                taxistaId = $null
                taxistaName = ""
                createdAt = ([datetime]$row["CreatedAt"]).ToString("s")
            }
        }

        if ($items.Count -eq 0) {
            Write-Host "No hay gafetes R/S recientes para subir a Hostinger."
            return
        }

        Write-Host "Subiendo estados R/S de gafetes a Hostinger: $($items.Count)"
        $payload = [ordered]@{
            mkt2_gafetes = $items
        }
        $response = Invoke-HostingerApi "Post" "/sync/push-changes?branchCode=$BranchCode" $payload
        Write-Host ($response | ConvertTo-Json -Depth 8)
    } catch {
        Write-Warning "No se pudieron subir estados R/S de gafetes a Hostinger: $($_.Exception.Message)"
    } finally {
        if ($connection) { $connection.Close() }
    }
}

function Save-RecentLocalLegacyMirrors {
    $connection = New-SqlConnection
    try {
        Ensure-AppTables $connection

        $command = $connection.CreateCommand()
        $command.CommandText = @"
SELECT TOP (200)
    folio_app AS recordId,
    id_catalogo AS catalogId,
    folio_gafete AS badgeId,
    vendedor_nombre AS driverName,
    telefono_taxista AS driverPhone,
    unidad AS unitNumber,
    nacionalidad AS nationality,
    hotel,
    sitio AS site,
    pax AS passengerCount,
    tipo_operacion AS serviceType,
    total AS tripCost,
    efectivo AS cashAmount,
    tarjeta AS cardAmount,
    fecha_operacion AS recordDate
FROM dbo.AppMovilRegistro
WHERE usuario_movil = 'hostinger'
  AND fecha_creacion >= DATEADD(DAY, -14, SYSDATETIME())
ORDER BY fecha_creacion DESC;
"@
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        $table = New-Object System.Data.DataTable
        [void]$adapter.Fill($table)

        foreach ($row in $table.Rows) {
            $folioControl = Text $row["recordId"]
            if ([string]::IsNullOrWhiteSpace($folioControl)) { continue }

            $record = [pscustomobject]@{
                recordId = $row["recordId"]
                catalogId = $row["catalogId"]
                badgeId = $row["badgeId"]
                driverName = $row["driverName"]
                driverPhone = $row["driverPhone"]
                unitNumber = $row["unitNumber"]
                nationality = $row["nationality"]
                hotel = $row["hotel"]
                site = $row["site"]
                passengerCount = $row["passengerCount"]
                serviceType = $row["serviceType"]
                tripCost = $row["tripCost"]
                recordDate = $row["recordDate"]
            }

            $tripCost = Decimal-Value $row["tripCost"]
            $cashAmount = Decimal-Value $row["cashAmount"]
            $cardAmount = Decimal-Value $row["cardAmount"]
            if ($cashAmount -eq 0 -and $cardAmount -eq 0) {
                $cashAmount = $tripCost
            }

            Save-AppMobileRegistro $connection $folioControl $record $tripCost $cashAmount $cardAmount
            Save-DejadaInsertOnly $connection $folioControl $record $tripCost $cashAmount $cardAmount
            Save-GafeteInsertOnly $connection $folioControl $record
            Save-AppMobileRecordGafetes $connection $folioControl $record
            Save-RelationTicketTaxista $connection $folioControl $record $tripCost
        }
    } finally {
        $connection.Close()
    }
}

Write-Host "Bajando registros de la app movil desde Hostinger..."
$isCascoApi = Test-IsCascoApi
$records = @()

if ($isCascoApi) {
    Write-Host "Modo Casco API detectado: se usara GET /api/taxis/registros."
    $records = Get-HostingerRecentTripRecords
    Write-Host "Registros recientes encontrados: $($records.Count)"
}
else {
    $pullResponse = Invoke-HostingerApi "Get" "/sync/pull-changes?limit=$PullLimit&branchCode=$BranchCode"
    $records = @($pullResponse.changes.mkt2_trip_records)
    Write-Host "Registros recibidos: $($records.Count)"

    if ($records.Count -eq 0) {
        Write-Host "No habia pendientes. Revisando registros recientes de la API..."
        $records = Get-HostingerRecentTripRecords
        Write-Host "Registros recientes encontrados: $($records.Count)"
    }
}

if ($records.Count -gt 0) {
    $savedRecordIds = @(Save-AppMobileTripRecords $records)

    if ($PullOnly -or $isCascoApi) {
        if ($isCascoApi) {
            Write-Host "Modo Casco API: se omite /sync/push-changes y /sync/mark-synced porque esas rutas no existen en este backend."
        }
        else {
            Write-Host "Modo PullOnly: se omite subir gafetes y marcar como sincronizado en Hostinger (no se escribe en produccion)."
        }
    } else {
        Sync-RecentReturnedGafetesToHostinger

        $markPayload = [ordered]@{
            mkt2_trip_records = $savedRecordIds
            mkt2_catalog_taxis = @()
            mkt2_gafetes = @()
        }

        if ($savedRecordIds.Count -gt 0) {
            Write-Host "Marcando registros verificados como sincronizados en Hostinger..."
            $markResponse = Invoke-HostingerApi "Post" "/sync/mark-synced?branchCode=$BranchCode" $markPayload
            Write-Host ($markResponse | ConvertTo-Json -Depth 8)
        } else {
            Write-Warning "No se marco nada como sincronizado porque ningun registro quedo verificado en SQL Server."
        }
    }
}

if ($RunLocalMirrorReview) {
    Write-Host "Revisando espejos faltantes en dejadas y gafete..."
    Save-RecentLocalLegacyMirrors
} else {
    Write-Host "Revision extra de espejos locales omitida; use -RunLocalMirrorReview para reparacion masiva."
}

Write-Host "Sincronizacion app movil terminada: $(Get-Date -Format 'dd/MM/yyyy HH:mm:ss')"
