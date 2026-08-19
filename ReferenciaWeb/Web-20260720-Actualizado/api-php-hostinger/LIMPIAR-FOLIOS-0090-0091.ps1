param(
    [string]$SqlServer = "26.38.252.71\SQLEXPRESS",
    [string]$SqlUser = "sa",
    [string]$SqlPassword = "hoka",
    [string]$MktDatabase = "mkt"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Data

$connectionString = "Server=$SqlServer;Database=$MktDatabase;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;Encrypt=False;Connect Timeout=10;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.CommandText = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.AppMovilFoliosBloqueados', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilFoliosBloqueados (
        Folio NVARCHAR(60) NOT NULL CONSTRAINT PK_AppMovilFoliosBloqueados PRIMARY KEY,
        Motivo NVARCHAR(200) NOT NULL DEFAULT '',
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilFoliosBloqueados_Fecha DEFAULT SYSUTCDATETIME()
    );
END;

DECLARE @Folios TABLE (Folio NVARCHAR(60) NOT NULL PRIMARY KEY, FolioNumero BIGINT NULL);
INSERT INTO @Folios (Folio, FolioNumero)
VALUES ('0090', 90), ('0091', 91);

INSERT INTO dbo.AppMovilFoliosBloqueados (Folio, Motivo)
SELECT Folio, 'Bloqueado por duplicado 15/06/2026'
FROM @Folios f
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.AppMovilFoliosBloqueados b
    WHERE b.Folio = f.Folio
       OR (ISNUMERIC(b.Folio) = 1 AND CONVERT(BIGINT, b.Folio) = f.FolioNumero)
);

DECLARE @DeletedGafetesRelacion INT = 0;
DECLARE @DeletedControl INT = 0;
DECLARE @DeletedRegistro INT = 0;
DECLARE @DeletedDejadas INT = 0;
DECLARE @DeletedGafete INT = 0;

IF OBJECT_ID('dbo.AppMovilRegistroGafetes', 'U') IS NOT NULL
BEGIN
    DELETE rg
    FROM dbo.AppMovilRegistroGafetes rg
    JOIN @Folios f
      ON LTRIM(RTRIM(COALESCE(rg.FolioApp, ''))) = f.Folio
      OR (ISNUMERIC(rg.FolioApp) = 1 AND CONVERT(BIGINT, rg.FolioApp) = f.FolioNumero);
    SET @DeletedGafetesRelacion = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.AppMovilFolioControl', 'U') IS NOT NULL
BEGIN
    DELETE fc
    FROM dbo.AppMovilFolioControl fc
    JOIN @Folios f
      ON LTRIM(RTRIM(COALESCE(fc.FolioAppOriginal, ''))) = f.Folio
      OR LTRIM(RTRIM(COALESCE(fc.FolioControl, ''))) = f.Folio
      OR (ISNUMERIC(fc.FolioAppOriginal) = 1 AND CONVERT(BIGINT, fc.FolioAppOriginal) = f.FolioNumero)
      OR (ISNUMERIC(fc.FolioControl) = 1 AND CONVERT(BIGINT, fc.FolioControl) = f.FolioNumero);
    SET @DeletedControl = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NOT NULL
BEGIN
    DELETE r
    FROM dbo.AppMovilRegistro r
    JOIN @Folios f
      ON LTRIM(RTRIM(COALESCE(r.folio_app, ''))) = f.Folio
      OR LTRIM(RTRIM(COALESCE(r.folio_app_original, ''))) = f.Folio
      OR LTRIM(RTRIM(COALESCE(r.folio_pos, ''))) = f.Folio
      OR (ISNUMERIC(r.folio_app) = 1 AND CONVERT(BIGINT, r.folio_app) = f.FolioNumero)
      OR (ISNUMERIC(r.folio_app_original) = 1 AND CONVERT(BIGINT, r.folio_app_original) = f.FolioNumero)
      OR (ISNUMERIC(r.folio_pos) = 1 AND CONVERT(BIGINT, r.folio_pos) = f.FolioNumero);
    SET @DeletedRegistro = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.dejadas', 'U') IS NOT NULL
BEGIN
    DELETE d
    FROM dbo.dejadas d
    JOIN @Folios f
      ON LTRIM(RTRIM(COALESCE(d.idstaff, ''))) = f.Folio
      OR LTRIM(RTRIM(COALESCE(d.folioregistrostr, ''))) = f.Folio
      OR LTRIM(RTRIM(COALESCE(d.codigorecepcion, ''))) = f.Folio
      OR (ISNUMERIC(d.idstaff) = 1 AND CONVERT(BIGINT, d.idstaff) = f.FolioNumero)
      OR (ISNUMERIC(d.folioregistrostr) = 1 AND CONVERT(BIGINT, d.folioregistrostr) = f.FolioNumero)
      OR (ISNUMERIC(d.codigorecepcion) = 1 AND CONVERT(BIGINT, d.codigorecepcion) = f.FolioNumero)
      OR (ISNUMERIC(d.folioregistro) = 1 AND CONVERT(BIGINT, d.folioregistro) = f.FolioNumero)
    WHERE UPPER(LTRIM(RTRIM(COALESCE(d.nombrecajero, '')))) IN ('APP MOVIL', 'HOSTINGER')
       OR LTRIM(RTRIM(COALESCE(d.codigorecepcion, ''))) IN ('0090', '0091')
       OR LTRIM(RTRIM(COALESCE(d.idstaff, ''))) IN ('0090', '0091');
    SET @DeletedDejadas = @@ROWCOUNT;
END;

IF OBJECT_ID('dbo.gafete', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.gafete', 'folioperacion') IS NULL
        ALTER TABLE dbo.gafete ADD folioperacion NVARCHAR(50) NULL;

    DELETE g
    FROM dbo.gafete g
    JOIN @Folios f
      ON LTRIM(RTRIM(COALESCE(CONVERT(NVARCHAR(60), g.matricula), ''))) = f.Folio
      OR LTRIM(RTRIM(COALESCE(CONVERT(NVARCHAR(60), g.folioperacion), ''))) = f.Folio
      OR (ISNUMERIC(CONVERT(NVARCHAR(60), g.matricula)) = 1 AND CONVERT(BIGINT, g.matricula) = f.FolioNumero)
      OR (ISNUMERIC(CONVERT(NVARCHAR(60), g.folioperacion)) = 1 AND CONVERT(BIGINT, g.folioperacion) = f.FolioNumero)
    WHERE UPPER(COALESCE(CONVERT(NVARCHAR(10), g.venta), '')) = 'A';
    SET @DeletedGafete = @@ROWCOUNT;
END;

COMMIT TRANSACTION;

SELECT
    @DeletedGafetesRelacion AS AppMovilRegistroGafetes,
    @DeletedControl AS AppMovilFolioControl,
    @DeletedRegistro AS AppMovilRegistro,
    @DeletedDejadas AS Dejadas,
    @DeletedGafete AS Gafete;
"@

    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
    $table = New-Object System.Data.DataTable
    [void]$adapter.Fill($table)
    $table | Format-Table -AutoSize
    Write-Host "Listo: 0090 y 0091 quedaron bloqueados para que no se reutilicen ni se vuelvan a sincronizar."
}
finally {
    $connection.Close()
}
