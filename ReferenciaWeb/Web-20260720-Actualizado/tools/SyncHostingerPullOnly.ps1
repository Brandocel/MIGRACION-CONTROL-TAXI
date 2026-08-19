param(
    [string]$ApiBaseUrl = "https://lightyellow-porpoise-679527.hostingersite.com",
    [string]$SyncToken = "HokaTaxisSync2050",
    [string]$SqlServer = "26.38.252.71\SQLEXPRESS",
    [string]$SqlUser = "sa",
    [string]$SqlPassword = "hoka",
    [string]$MktDatabase = "mkt",
    [int]$PullLimit = 500
)

$ErrorActionPreference = "Stop"

function New-SqlConnection {
    $connectionString = "Server=$SqlServer;Database=$MktDatabase;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;Encrypt=False;"
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $connection.Open()
    return $connection
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

function IntValue($Value) {
    $result = 0
    if ([int]::TryParse((Text $Value), [ref]$result)) { return $result }
    return 0
}

function DecimalValue($Value) {
    $result = [decimal]0
    if ([decimal]::TryParse((Text $Value), [ref]$result)) { return $result }
    return [decimal]0
}

function DateValue($Value) {
    $text = Text $Value
    if ([string]::IsNullOrWhiteSpace($text)) { return [DBNull]::Value }
    $date = [datetime]::MinValue
    if ([datetime]::TryParse($text, [ref]$date)) { return $date }
    return [DBNull]::Value
}

function Invoke-HostingerApi([string]$Method, [string]$Path, $Body = $null) {
    $headers = @{ "X-Sync-Token" = $SyncToken }
    $uri = "$ApiBaseUrl$Path"
    if ($null -eq $Body) {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers
    }
    $json = $Body | ConvertTo-Json -Depth 20 -Compress
    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json; charset=utf-8" -Body $json
}

function Ensure-AppTripTable {
    $connection = New-SqlConnection
    try {
        $command = $connection.CreateCommand()
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
IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_contacto') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD telefono_contacto NVARCHAR(30) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'nacionalidad') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD nacionalidad NVARCHAR(120) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'modelo_vehiculo') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD modelo_vehiculo NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'estado_sync') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD estado_sync NVARCHAR(30) NOT NULL DEFAULT 'SINCRONIZADO';
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_status') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_status NVARCHAR(30) NOT NULL DEFAULT 'pendiente';
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_date') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_date DATETIME2 NULL;
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_user') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_user NVARCHAR(80) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_ticket') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD payout_ticket NVARCHAR(40) NOT NULL DEFAULT '';
"@
        [void]$command.ExecuteNonQuery()
    } finally {
        $connection.Close()
    }
}

function Resolve-FolioControl($Connection, [string]$RecordId) {
    $existing = $Connection.CreateCommand()
    $existing.CommandText = @"
SELECT TOP (1) FolioControl
FROM dbo.AppMovilFolioControl
WHERE FolioAppOriginal = @recordId
   OR FolioControl = @recordId;
"@
    Add-Parameter $existing "@recordId" $RecordId
    $current = $existing.ExecuteScalar()
    if ($null -ne $current -and $current -ne [DBNull]::Value -and -not [string]::IsNullOrWhiteSpace([string]$current)) {
        return [string]$current
    }

    $next = $Connection.CreateCommand()
    $next.CommandText = @"
DECLARE @ultimo INT;
SELECT @ultimo = ISNULL(MAX(
    CASE
        WHEN TRY_CONVERT(INT, FolioControl) IS NOT NULL THEN TRY_CONVERT(INT, FolioControl)
        WHEN FolioControl LIKE 'AP%' AND TRY_CONVERT(INT, SUBSTRING(FolioControl, 3, 20)) IS NOT NULL THEN TRY_CONVERT(INT, SUBSTRING(FolioControl, 3, 20))
        ELSE 0
    END), 0)
FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK);

DECLARE @folioControl NVARCHAR(60) = RIGHT('0000' + CONVERT(NVARCHAR(20), @ultimo + 1), 4);

INSERT INTO dbo.AppMovilFolioControl (FolioAppOriginal, FolioControl)
VALUES (@recordId, @folioControl);

SELECT @folioControl;
"@
    Add-Parameter $next "@recordId" $RecordId
    return [string]$next.ExecuteScalar()
}

function Save-TripRecords($Records) {
    Ensure-AppTripTable
    $connection = New-SqlConnection
    try {
        foreach ($record in @($Records)) {
            $recordId = Text $record.recordId
            if ([string]::IsNullOrWhiteSpace($recordId)) {
                continue
            }
            $folioControl = Resolve-FolioControl $connection $recordId
            $tripCost = DecimalValue $record.tripCost
            $paymentMethod = (Text $record.paymentMethod).ToUpperInvariant()
            $cashAmount = $tripCost
            $cardAmount = [decimal]0
            if ($paymentMethod.Contains("TARJETA")) {
                $cashAmount = [decimal]0
                $cardAmount = $tripCost
            }

            $command = $connection.CreateCommand()
            $command.CommandText = @"
IF NOT EXISTS (
    SELECT 1
    FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
    WHERE folio_app = @folioControl
       OR folio_app_original = @recordId
)
BEGIN
INSERT INTO dbo.AppMovilRegistro (
    folio_app,folio_app_original,id_catalogo,folio_gafete,fecha_operacion,vendedor_nombre,telefono_taxista,telefono_contacto,nacionalidad,placas,modelo_vehiculo,unidad,hotel,origen,sitio,destino,pax,tipo_operacion,total,efectivo,tarjeta,usuario_movil,notas,detalle_json,estado_sync,payout_status,payout_date,payout_user,payout_ticket
) VALUES (
    @folioControl,@recordId,@catalogId,@badgeId,@recordDate,@driverName,@driverPhone,@contactPhone,@nationality,@plate,@vehicleModel,@unitNumber,@hotel,@origin,@site,@destination,@passengerCount,@serviceType,@tripCost,@cashAmount,@cardAmount,'hostinger',@notes,'','SINCRONIZADO',@payoutStatus,@payoutDate,@payoutUser,@payoutTicket
);
END;
"@
            Add-Parameter $command "@folioControl" $folioControl
            Add-Parameter $command "@recordId" $recordId
            Add-Parameter $command "@catalogId" (IntValue $record.catalogId)
            Add-Parameter $command "@badgeId" (Text $record.badgeId)
            Add-Parameter $command "@driverName" (Text $record.driverName)
            Add-Parameter $command "@driverPhone" (Text $record.driverPhone)
            Add-Parameter $command "@contactPhone" (Text $record.contactPhone)
            Add-Parameter $command "@nationality" (Text $record.nationality)
            Add-Parameter $command "@plate" (Text $record.plate)
            Add-Parameter $command "@vehicleModel" (Text $record.vehicleModel)
            Add-Parameter $command "@unitNumber" (Text $record.unitNumber)
            Add-Parameter $command "@hotel" (Text $record.hotel)
            Add-Parameter $command "@origin" (Text $record.origin)
            Add-Parameter $command "@site" (Text $record.site)
            Add-Parameter $command "@destination" (Text $record.destination)
            Add-Parameter $command "@passengerCount" (IntValue $record.passengerCount)
            Add-Parameter $command "@serviceType" (Text $record.serviceType)
            Add-Parameter $command "@tripCost" $tripCost
            Add-Parameter $command "@cashAmount" $cashAmount
            Add-Parameter $command "@cardAmount" $cardAmount
            Add-Parameter $command "@notes" (Text $record.notes)
            Add-Parameter $command "@recordDate" (DateValue $record.recordDate)
            Add-Parameter $command "@payoutStatus" (Text $record.payoutStatus)
            Add-Parameter $command "@payoutDate" (DateValue $record.payoutDate)
            Add-Parameter $command "@payoutUser" (Text $record.payoutUser)
            Add-Parameter $command "@payoutTicket" (Text $record.payoutTicket)
            [void]$command.ExecuteNonQuery()
        }
    } finally {
        $connection.Close()
    }
}

Write-Host "Bajando cambios desde Hostinger..."
$pullResponse = Invoke-HostingerApi "Get" "/sync/pull-changes?limit=$PullLimit"
$records = @($pullResponse.changes.mkt2_trip_records)
Write-Host "Registros encontrados: $($records.Count)"

if ($records.Count -gt 0) {
    Save-TripRecords $records
}

$markPayload = [ordered]@{
    mkt2_trip_records = @($records | ForEach-Object { $_.recordId })
    mkt2_catalog_taxis = @()
    mkt2_gafetes = @()
}

if ($records.Count -gt 0) {
    Write-Host "Marcando registros como sincronizados..."
    $markResponse = Invoke-HostingerApi "Post" "/sync/mark-synced" $markPayload
    Write-Host ($markResponse | ConvertTo-Json -Depth 8)
}

Write-Host "Pull terminado: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
