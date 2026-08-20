# =============================================================================
#  Borra el caso de prueba TEST-9001 de la base LOCAL .\SQLEXPRESS.
#  Solo toca los registros cuyo folio es exactamente TEST-9001 / TESTPOS9001.
# =============================================================================

$ErrorActionPreference = 'Stop'
$server = '.\SQLEXPRESS'

$sql = @'
-- Necesario por la columna calculada indexada de remisioM (ver inyectar-caso-prueba.ps1).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @folio     nvarchar(60) = N'TEST-9001';
DECLARE @ticketPos nvarchar(60) = N'TESTPOS9001';

DELETE FROM compuadmo.dbo.remisioM            WHERE folio_remision = @ticketPos;
DELETE FROM mkt.dbo.RelacionTicketTaxista     WHERE FolioApp       = @folio;
DELETE FROM mkt.dbo.AppMovilRegistro          WHERE folio_app      = @folio;

SELECT 'Restantes' AS Estado,
       (SELECT COUNT(*) FROM mkt.dbo.AppMovilRegistro      WHERE folio_app      = @folio) AS AppMovil,
       (SELECT COUNT(*) FROM mkt.dbo.RelacionTicketTaxista WHERE FolioApp       = @folio) AS Relacion,
       (SELECT COUNT(*) FROM compuadmo.dbo.remisioM        WHERE folio_remision = @ticketPos) AS Venta;
'@

$file = Join-Path $env:TEMP 'limpiar-caso-prueba.sql'
Set-Content -Path $file -Value $sql -Encoding utf8

Write-Host "Borrando caso de prueba TEST-9001 de $server ..." -ForegroundColor Cyan
sqlcmd -S $server -E -b -i $file

if ($LASTEXITCODE -eq 0) {
    Write-Host "LISTO. Caso de prueba eliminado de SQL Server." -ForegroundColor Green
} else {
    Write-Host "ERROR: el borrado en SQL Server fallo. Revisa el mensaje de arriba." -ForegroundColor Red
}

# -----------------------------------------------------------------------------
#  Snapshot local de comisiones (SQLite)
#
#  Al pagar una comision, la app guarda una copia en DatosLocal\ControlTaxi.db
#  (tabla LocalComisiones) y es ESA copia la que decide si queda saldo. Si no se
#  borra, al repetir la prueba el boton PAGAR responde "La comision ya esta
#  pagada" aunque en SQL Server la comision este pendiente.
# -----------------------------------------------------------------------------
$raiz    = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlite  = Get-Command sqlite3 -ErrorAction SilentlyContinue

$bases = @(
    (Join-Path $raiz 'DatosLocal\ControlTaxi.db'),
    (Join-Path $raiz 'ControlTaxiDesktop\bin\Release\net9.0-windows\DatosLocal\ControlTaxi.db'),
    (Join-Path $raiz 'ControlTaxiDesktop\bin\Debug\net9.0-windows\DatosLocal\ControlTaxi.db')
) | Where-Object { Test-Path $_ }

if (-not $sqlite) {
    Write-Host ""
    Write-Host "AVISO: no se encontro sqlite3 en el PATH." -ForegroundColor Yellow
    Write-Host "Borra a mano la fila de LocalComisiones cuyo Folio sea C-TEST-9001-TESTPOS9001"
    Write-Host "en: $($bases -join ', ')"
} else {
    foreach ($db in $bases) {
        # No todas las copias tienen la tabla; se pregunta antes de borrar.
        $tabla = & $sqlite.Source $db "SELECT name FROM sqlite_master WHERE type='table' AND name='LocalComisiones';"
        if ($tabla) {
            & $sqlite.Source $db "DELETE FROM LocalComisiones WHERE Folio LIKE '%TEST-9001%' OR VentaFolio LIKE '%TEST-9001%';"
            Write-Host "Snapshot local limpiado en: $db" -ForegroundColor Green
        }
    }
}
