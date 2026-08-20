# =============================================================================
#  DIAGNOSTICO - SOLO LECTURA. No modifica nada.
#
#  Busca comisiones que quedaron marcadas como PAGADAS en la base local
#  (DatosLocal\ControlTaxi.db, tabla LocalComisiones) pero que en SQL Server
#  siguen sin pago registrado (AppMovilRegistro.pago_comision).
#
#  Por que existe este desfase:
#  Hasta el 2026-08-20, pagar una comision solo escribia el snapshot local; el
#  registro en SQL Server no existia. Esas comisiones SI se cobraron, pero la
#  pantalla las muestra PENDIENTE y, al intentar pagarlas otra vez, responde
#  "La comision ya esta pagada" y no deja avanzar.
#
#  Corre esto ANTES de mandar el sistema a pruebas para saber cuantos casos hay.
# =============================================================================

$ErrorActionPreference = 'Stop'
$server = '.\SQLEXPRESS'
$raiz   = Split-Path -Parent $MyInvocation.MyCommand.Path

$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
if (-not $sqlite) {
    Write-Host "No se encontro sqlite3 en el PATH. No se puede leer la base local." -ForegroundColor Red
    return
}

$db = Join-Path $raiz 'DatosLocal\ControlTaxi.db'
if (-not (Test-Path $db)) {
    Write-Host "No existe la base local: $db" -ForegroundColor Red
    return
}

Write-Host "Leyendo comisiones marcadas como pagadas en la base local..." -ForegroundColor Cyan
$filas = & $sqlite.Source -separator '|' $db @"
SELECT Folio, VentaFolio, ImporteComision, Pagado, Estatus
FROM LocalComisiones
WHERE Pagado > 0;
"@

if (-not $filas) {
    Write-Host "No hay comisiones pagadas en la base local. Nada que revisar." -ForegroundColor Green
    return
}

$desfasadas = @()
$revisadas  = 0

foreach ($linea in $filas) {
    if ([string]::IsNullOrWhiteSpace($linea)) { continue }
    $p = $linea -split '\|'
    if ($p.Count -lt 5) { continue }

    $folio       = $p[0]
    $ventaFolio  = $p[1]
    $importe     = $p[2]
    $pagado      = $p[3]
    $revisadas++

    # En SQL Server el folio de operacion es VentaFolio (sin el prefijo "C-").
    $folioSql = if ([string]::IsNullOrWhiteSpace($ventaFolio)) { $folio -replace '^C-', '' } else { $ventaFolio }
    $folioSql = $folioSql -replace "'", "''"

    $consulta = "SET NOCOUNT ON; SELECT ISNULL(CONVERT(varchar(20), MAX(pago_comision)), '0') FROM mkt.dbo.AppMovilRegistro WHERE folio_app = '$folioSql' OR folio_app_original = '$folioSql';"
    $resultado = sqlcmd -S $server -E -h -1 -W -Q $consulta 2>$null

    $pagoSql = 0.0
    if ($resultado) { [double]::TryParse(($resultado | Select-Object -First 1).Trim(), [ref]$pagoSql) | Out-Null }

    if ($pagoSql -le 0) {
        $desfasadas += [pscustomobject]@{
            FolioLocal   = $folio
            FolioSql     = $folioSql
            Comision     = $importe
            PagadoLocal  = $pagado
            PagoEnSql    = $pagoSql
        }
    }
}

Write-Host ""
Write-Host "Comisiones pagadas revisadas: $revisadas" -ForegroundColor Cyan

if ($desfasadas.Count -eq 0) {
    Write-Host "TODO EN ORDEN: no hay comisiones cobradas en local que falten en SQL Server." -ForegroundColor Green
    return
}

Write-Host ""
Write-Host "DESFASADAS: $($desfasadas.Count)" -ForegroundColor Yellow
Write-Host "Estan cobradas en la base local pero SQL Server no tiene el pago." -ForegroundColor Yellow
Write-Host "En pantalla se veran PENDIENTE y el boton PAGAR respondera 'ya esta pagada'." -ForegroundColor Yellow
Write-Host ""
$desfasadas | Format-Table -AutoSize
Write-Host ""
Write-Host "Que hacer con estas: decidelo tu. Hay dos caminos y NO los aplico solo:" -ForegroundColor Cyan
Write-Host "  A) Si el dinero SI se entrego: pasar el pago a SQL Server para que queden PAGADAS."
Write-Host "  B) Si el dinero NO se entrego: limpiar el snapshot local para poder cobrarlas."
Write-Host "Pasame esta lista y armamos el script del caso que corresponda."
