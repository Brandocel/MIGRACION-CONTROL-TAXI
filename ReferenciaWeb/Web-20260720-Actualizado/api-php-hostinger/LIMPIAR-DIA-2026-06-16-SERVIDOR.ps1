param(
    [string]$SqlServer = "26.38.252.71\SQLEXPRESS",
    [string]$SqlUser = "sa",
    [string]$SqlPassword = "hoka",
    [string]$MktDatabase = "mkt",
    [datetime]$Fecha = "2026-06-16"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Data

$connectionString = "Server=$SqlServer;Database=$MktDatabase;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;Encrypt=False;Connect Timeout=10;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    $command.CommandText = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Fecha date = @fecha;
DECLARE @Folios TABLE (Folio NVARCHAR(80) NOT NULL PRIMARY KEY);

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NOT NULL
BEGIN
    INSERT INTO @Folios (Folio)
    SELECT DISTINCT Folio
    FROM (
        SELECT NULLIF(LTRIM(RTRIM(folio_app)), '') AS Folio
        FROM dbo.AppMovilRegistro
        WHERE CAST(fecha_operacion AS date) = @Fecha
        UNION
        SELECT NULLIF(LTRIM(RTRIM(folio_app_original)), '')
        FROM dbo.AppMovilRegistro
        WHERE CAST(fecha_operacion AS date) = @Fecha
        UNION
        SELECT NULLIF(LTRIM(RTRIM(folio_pos)), '')
        FROM dbo.AppMovilRegistro
        WHERE CAST(fecha_operacion AS date) = @Fecha
    ) x
    WHERE Folio IS NOT NULL;
END;

DECLARE @DeletedAppGafetes INT = 0;
DECLARE @DeletedApp INT = 0;
DECLARE @DeletedControl INT = 0;
DECLARE @DeletedDejadas INT = 0;
DECLARE @DeletedGafete INT = 0;

IF OBJECT_ID('dbo.AppMovilRegistroGafetes', 'U') IS NOT NULL
BEGIN
    DELETE rg
    FROM dbo.AppMovilRegistroGafetes rg
    WHERE EXISTS (
        SELECT 1 FROM @Folios f
        WHERE rg.FolioApp = f.Folio
           OR (ISNUMERIC(rg.FolioApp) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, rg.FolioApp) = CONVERT(bigint, f.Folio))
    );
    SET @DeletedAppGafetes = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.AppMovilRegistro
    WHERE CAST(fecha_operacion AS date) = @Fecha;
    SET @DeletedApp = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.AppMovilFolioControl', 'U') IS NOT NULL
BEGIN
    DELETE fc
    FROM dbo.AppMovilFolioControl fc
    WHERE EXISTS (
        SELECT 1 FROM @Folios f
        WHERE fc.FolioAppOriginal = f.Folio
           OR fc.FolioControl = f.Folio
           OR (ISNUMERIC(fc.FolioAppOriginal) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, fc.FolioAppOriginal) = CONVERT(bigint, f.Folio))
           OR (ISNUMERIC(fc.FolioControl) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, fc.FolioControl) = CONVERT(bigint, f.Folio))
    );
    SET @DeletedControl = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.dejadas', 'U') IS NOT NULL
BEGIN
    DELETE d
    FROM dbo.dejadas d
    WHERE CAST(d.fecha AS date) = @Fecha
      AND (
            UPPER(LTRIM(RTRIM(COALESCE(d.nombrecajero, '')))) IN ('APP MOVIL', 'HOSTINGER')
         OR EXISTS (
                SELECT 1 FROM @Folios f
                WHERE d.idstaff = f.Folio
                   OR d.folioregistrostr = f.Folio
                   OR d.codigorecepcion = f.Folio
                   OR (ISNUMERIC(d.idstaff) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, d.idstaff) = CONVERT(bigint, f.Folio))
                   OR (ISNUMERIC(d.folioregistrostr) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, d.folioregistrostr) = CONVERT(bigint, f.Folio))
                   OR (ISNUMERIC(d.codigorecepcion) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, d.codigorecepcion) = CONVERT(bigint, f.Folio))
            )
      );
    SET @DeletedDejadas = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.gafete', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.gafete', 'folioperacion') IS NULL
        ALTER TABLE dbo.gafete ADD folioperacion NVARCHAR(50) NULL;

    DELETE g
    FROM dbo.gafete g
    WHERE CAST(g.fecha AS date) = @Fecha
      AND UPPER(COALESCE(CONVERT(NVARCHAR(10), g.venta), '')) = 'A'
      AND EXISTS (
            SELECT 1 FROM @Folios f
            WHERE CONVERT(NVARCHAR(80), g.matricula) = f.Folio
               OR CONVERT(NVARCHAR(80), g.folioperacion) = f.Folio
               OR (ISNUMERIC(CONVERT(NVARCHAR(80), g.matricula)) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, g.matricula) = CONVERT(bigint, f.Folio))
               OR (ISNUMERIC(CONVERT(NVARCHAR(80), g.folioperacion)) = 1 AND ISNUMERIC(f.Folio) = 1 AND CONVERT(bigint, g.folioperacion) = CONVERT(bigint, f.Folio))
      );
    SET @DeletedGafete = @@ROWCOUNT;
END;

COMMIT TRANSACTION;

SELECT
    @Fecha AS Fecha,
    (SELECT COUNT(1) FROM @Folios) AS FoliosDetectados,
    @DeletedAppGafetes AS AppMovilRegistroGafetes,
    @DeletedApp AS AppMovilRegistro,
    @DeletedControl AS AppMovilFolioControl,
    @DeletedDejadas AS Dejadas,
    @DeletedGafete AS Gafete;
"@
    $parameter = $command.Parameters.Add("@fecha", [System.Data.SqlDbType]::Date)
    $parameter.Value = $Fecha.Date
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    $table = New-Object System.Data.DataTable
    [void]$adapter.Fill($table)
    $table | Format-Table -AutoSize
    Write-Host "Listo: limpieza local del servidor para $($Fecha.ToString('dd/MM/yyyy'))."
}
finally {
    $connection.Close()
}
