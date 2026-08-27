# =============================================================================
#  Indices que aceleran la pantalla de Comisiones.  EJECUTAR EN EL SERVIDOR.
#
#  Por que hacen falta:
#  La consulta de comisiones filtra por CAST(folioregistro AS nvarchar(60)).
#  Envolver una columna en CAST impide que SQL Server use su indice, asi que
#  hacia un recorrido completo de remisioM en cada busqueda: de ahi la lentitud.
#  Con una columna calculada PERSISTED que tiene EXACTAMENTE la misma expresion,
#  mas su indice, SQL Server la reconoce sola y deja de recorrer la tabla.
#  No hay que cambiar la consulta.
#
#  Es idempotente: si ya existen, no hace nada.
#  No modifica datos, solo agrega estructura.
# =============================================================================

$ErrorActionPreference = 'Stop'
$server = 'SERVPLAZA28\SQLEXPRESS'

# Si la conexion integrada no funciona, cambia a:  $auth = '-U sa -P TU_CONTRASENA'
$usarIntegrada = $true

$sql = @'
-- QUOTED_IDENTIFIER ON es OBLIGATORIO para columnas calculadas con indice.
-- sqlcmd lo trae apagado y sin esto falla con Msg 1934.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

-------------------------------------------------------------------------------
-- compuadmo.dbo.remisioM  ->  folioregistro
-------------------------------------------------------------------------------
USE compuadmo;
IF COL_LENGTH('dbo.remisioM', 'folioregistro_txt') IS NULL
BEGIN
    ALTER TABLE dbo.remisioM
        ADD folioregistro_txt AS CAST(folioregistro AS nvarchar(60)) PERSISTED;
    PRINT 'compuadmo: columna folioregistro_txt creada.';
END
ELSE PRINT 'compuadmo: la columna ya existia.';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_remisioM_folioregistro_txt' AND object_id = OBJECT_ID('dbo.remisioM'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_remisioM_folioregistro_txt ON dbo.remisioM(folioregistro_txt);
    PRINT 'compuadmo: indice creado.';
END
ELSE PRINT 'compuadmo: el indice ya existia.';

-------------------------------------------------------------------------------
-- joyeria.dbo.remisioM  ->  folio_registro  (ojo: con guion bajo, distinto nombre)
-------------------------------------------------------------------------------
USE joyeria;
IF COL_LENGTH('dbo.remisioM', 'folio_registro_txt') IS NULL
BEGIN
    ALTER TABLE dbo.remisioM
        ADD folio_registro_txt AS CAST(folio_registro AS nvarchar(60)) PERSISTED;
    PRINT 'joyeria: columna folio_registro_txt creada.';
END
ELSE PRINT 'joyeria: la columna ya existia.';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_remisioM_folio_registro_txt' AND object_id = OBJECT_ID('dbo.remisioM'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_remisioM_folio_registro_txt ON dbo.remisioM(folio_registro_txt);
    PRINT 'joyeria: indice creado.';
END
ELSE PRINT 'joyeria: el indice ya existia.';
'@

$file = Join-Path $env:TEMP 'crear-indices-comisiones.sql'
Set-Content -Path $file -Value $sql -Encoding utf8

Write-Host "Creando indices en $server ..." -ForegroundColor Cyan
Write-Host "(puede tardar unos minutos: remisioM es grande)" -ForegroundColor DarkGray

if ($usarIntegrada) {
    sqlcmd -S $server -E -b -i $file
} else {
    $pwd = Read-Host "Contrasena de sa" -AsSecureString
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($pwd))
    sqlcmd -S $server -U sa -P $plain -b -i $file
}

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "LISTO. Indices verificados/creados." -ForegroundColor Green
    Write-Host "Abre Comisiones y busca un rango de varios dias: debe responder mucho mas rapido."
} else {
    Write-Host "ERROR: revisa el mensaje de arriba. No se aplico nada a medias (cada paso es condicional)." -ForegroundColor Red
}
