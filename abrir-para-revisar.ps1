# =============================================================================
#  ABRIR CONTROL TAXI EN MODO REVISION
# =============================================================================
#  Abre el Desktop para mirarlo sin tocar produccion:
#
#    - No arranca el sincronizador de Hostinger.
#    - No pide ni usa las credenciales de Casco Viejo.
#    - Lee la base local DatosLocal\ControlTaxi.db.
#
#  Al cerrar la aplicacion, todo queda como estaba.
#  Para el arranque real de sucursal usa los .cmd numerados de
#  Release-ControlTaxi-Plaza28-Produccion, no este script.
# =============================================================================

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$exe  = Join-Path $root 'ControlTaxiDesktop\bin\Release\net9.0-windows\ControlTaxiDesktop.exe'
$sync = Join-Path $root 'ControlTaxiDesktop\bin\Release\net9.0-windows\SyncTaxi'

function Escribir($texto, $color) { Write-Host $texto -ForegroundColor $color }

Escribir "" 'White'
Escribir "  CONTROL TAXI - MODO REVISION" 'Cyan'
Escribir "  ----------------------------" 'Cyan'
Escribir "" 'White'

# --- 1. Ya esta abierto? ----------------------------------------------------
$abierto = Get-Process ControlTaxiDesktop -ErrorAction SilentlyContinue
if ($abierto) {
    Escribir "  Ya hay una ventana abierta (proceso $($abierto.Id))." 'Yellow'
    Escribir "  Cierrala antes de volver a abrir." 'Yellow'
    Escribir "" 'White'
    Read-Host "  Enter para salir"
    exit 0
}

# --- 2. Compilar si hace falta ----------------------------------------------
if (-not (Test-Path $exe)) {
    Escribir "  No hay ejecutable. Compilando (tarda ~30 segundos)..." 'Yellow'
    dotnet build (Join-Path $root 'CONTROL TAXI.sln') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Escribir "  La compilacion fallo. Revisa los errores de arriba." 'Red'
        Read-Host "  Enter para salir"
        exit 1
    }
}
Escribir "  Ejecutable listo." 'Green'

# --- 3. Desactivar el sincronizador mientras revisas ------------------------
$desactivados = @()
if (Test-Path $sync) {
    foreach ($vbs in Get-ChildItem -Path $sync -Filter '*.vbs' -ErrorAction SilentlyContinue) {
        Rename-Item -LiteralPath $vbs.FullName -NewName "$($vbs.Name).off" -Force
        $desactivados += (Join-Path $sync "$($vbs.Name).off")
    }
}
if ($desactivados.Count -gt 0) {
    Escribir "  Sincronizador desactivado ($($desactivados.Count) lanzadores)." 'Green'
} else {
    Escribir "  No habia sincronizador que desactivar." 'Gray'
}

# --- 4. Abrir ----------------------------------------------------------------
$env:CONTROL_TAXI_START_CASCO_SERVICES = 'false'

Escribir "" 'White'
Escribir "  Abriendo... entra con uno de estos usuarios:" 'White'
Escribir "    Guadalupe   REYNA   admin   WEB      (sucursal Plaza 28)" 'Gray'
Escribir "    ReynaV                               (sucursal Casco Viejo)" 'Gray'
Escribir "" 'White'
Escribir "  Esta ventana se queda esperando. NO la cierres:" 'Yellow'
Escribir "  cuando cierres Control Taxi, reactiva el sincronizador sola." 'Yellow'
Escribir "" 'White'

try {
    $proceso = Start-Process -FilePath $exe -WorkingDirectory $root -PassThru
    $proceso.WaitForExit()
    Escribir "  Control Taxi cerrado." 'Green'
}
finally {
    # --- 5. Dejar todo como estaba ------------------------------------------
    foreach ($off in $desactivados) {
        if (Test-Path $off) {
            Rename-Item -LiteralPath $off -NewName ([IO.Path]::GetFileNameWithoutExtension($off)) -Force
        }
    }
    if ($desactivados.Count -gt 0) { Escribir "  Sincronizador reactivado." 'Green' }
    Escribir "" 'White'
}
