<#
    aplicar-parche-cuadre.ps1
    Fecha: 2026-09-09

    Aplica el parche del cuadre (PARCHE-CUADRE-API-2026-09-09.php) sobre un index.php del API
    de Hostinger, sin editar a mano.

    Por que un script y no copiar-pegar: el archivo del servidor tiene 2089 lineas y la cadena
    "Ruta no encontrada" aparece dos veces. Pegar en el lugar equivocado rompe el ruteo de todo
    el API. Este script ubica los anclajes por posicion relativa a las funciones, se niega a
    correr si algo no calza, y deja respaldo.

    IMPORTANTE: el archivo de entrada debe ser el index.php QUE ESTA EN EL SERVIDOR AHORITA,
    descargado hoy. No la copia local del 04/09: puede haber cambios subidos despues.

    Uso:
        .\aplicar-parche-cuadre.ps1 -IndexPath "C:\ruta\index-servidor-descargado.php"

    Salida:
        <IndexPath>.backup-AAAAMMDD-HHMMSS   respaldo intacto del archivo original
        <IndexPath>.parchado.php             archivo nuevo, listo para subir

    El script NO sube nada. La subida se hace a mano y se verifica con
    verificar-api-cuadre.ps1 antes y despues.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$IndexPath,

    [string]$ParchePath = (Join-Path $PSScriptRoot 'PARCHE-CUADRE-API-2026-09-09.php')
)

$ErrorActionPreference = 'Stop'

function Fallar([string]$mensaje) {
    Write-Host ""
    Write-Host "ABORTADO: $mensaje" -ForegroundColor Red
    Write-Host "No se modifico ningun archivo." -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $IndexPath)) { Fallar "No existe el index.php indicado: $IndexPath" }
if (-not (Test-Path $ParchePath)) { Fallar "No existe el archivo de parche: $ParchePath" }

$original = Get-Content -Path $IndexPath -Raw -Encoding UTF8
$parche = Get-Content -Path $ParchePath -Raw -Encoding UTF8

# --- Guardas -------------------------------------------------------------------------------

if ($original -match 'mkt2_cuadre_snapshots') {
    Fallar "Este index.php YA tiene el parche del cuadre aplicado. Nada que hacer."
}

foreach ($simbolo in @('function route_sync', 'function route_pos_get', 'function upsert', 'function exec_sql', 'function date_param', 'function mysql_datetime')) {
    if ($original -notmatch [regex]::Escape($simbolo)) {
        Fallar "El index.php no contiene '$simbolo'. No parece el API correcto, o cambio de estructura."
    }
}

# --- Extraer los tres bloques del archivo de parche -----------------------------------------

function Extraer([string]$texto, [string]$desde, [string]$hasta) {
    $inicio = $texto.IndexOf($desde)
    if ($inicio -lt 0) { Fallar "No se encontro el marcador '$desde' en el archivo de parche." }
    $inicio = $texto.IndexOf("`n", $inicio) + 1

    if ([string]::IsNullOrEmpty($hasta)) {
        return $texto.Substring($inicio).TrimEnd()
    }

    $fin = $texto.IndexOf($hasta, $inicio)
    if ($fin -lt 0) { Fallar "No se encontro el marcador de cierre '$hasta' en el archivo de parche." }
    return $texto.Substring($inicio, $fin - $inicio).TrimEnd()
}

$marca1 = '// BLOQUE 1 -'
$marca2 = '// BLOQUE 2 -'
$marca3 = '// BLOQUE 3 -'

$bloque1 = Extraer $parche $marca1 $marca2
$bloque2 = Extraer $parche $marca2 $marca3
$bloque3 = Extraer $parche $marca3 $null

# Los bloques 2 y 3 arrastran el comentario de cabecera del bloque siguiente; se recorta
# desde la primera linea de codigo real hacia adelante.
function SoloCodigo([string]$bloque) {
    $lineas = $bloque -split "`r?`n"
    $primera = 0
    for ($i = 0; $i -lt $lineas.Count; $i++) {
        if ($lineas[$i] -match '^\s*(if|function|/\*\*)\s*\(?') { $primera = $i; break }
    }
    $ultima = $lineas.Count - 1
    while ($ultima -gt $primera -and ($lineas[$ultima].Trim() -eq '' -or $lineas[$ultima] -match '^\s*//\s*=+\s*$' -or $lineas[$ultima] -match '^\s*//')) {
        $ultima--
    }
    return ($lineas[$primera..$ultima] -join "`r`n")
}

$bloque1 = SoloCodigo $bloque1
$bloque2 = SoloCodigo $bloque2
$bloque3 = SoloCodigo $bloque3

foreach ($par in @(@('BLOQUE 1', $bloque1, 'function ensure_cuadre_schema'), @('BLOQUE 2', $bloque2, "/sync/push-cuadre"), @('BLOQUE 3', $bloque3, "/api/pos/cuadre"))) {
    if ($par[1] -notmatch [regex]::Escape($par[2])) {
        Fallar ("{0} quedo mal recortado: no contiene '{1}'." -f $par[0], $par[2])
    }
}

# --- Anclaje 1: fin de route_sync -----------------------------------------------------------

$anclaSync = "    json_response(['error' => 'Ruta sync no encontrada', 'path' => `$path], 404);"
if (([regex]::Matches($original, [regex]::Escape($anclaSync))).Count -ne 1) {
    Fallar "El anclaje de route_sync no aparece exactamente una vez. Revisar el archivo a mano."
}

# --- Anclaje 2: el 404 que sigue a 'function route_pos_get' ---------------------------------
# La cadena 'Ruta no encontrada' sale dos veces (fin de route() y fin de route_pos_get).
# Se toma la PRIMERA que aparece despues de la firma de route_pos_get.

$posRoutePosGet = $original.IndexOf('function route_pos_get')
if ($posRoutePosGet -lt 0) { Fallar "No se hallo 'function route_pos_get'." }

$anclaPos = "    json_response(['error' => 'Ruta no encontrada', 'path' => `$path], 404);"
$posAnclaPos = $original.IndexOf($anclaPos, $posRoutePosGet)
if ($posAnclaPos -lt 0) { Fallar "No se hallo el 404 de cierre de route_pos_get." }

# --- Armar el archivo parchado --------------------------------------------------------------

$nuevo = $original

# Bloque 3 primero: se inserta mas adelante en el archivo, asi la posicion del anclaje de
# route_sync (mas arriba) no se corre.
$nuevo = $nuevo.Substring(0, $posAnclaPos) + $bloque3 + "`r`n`r`n" + $nuevo.Substring($posAnclaPos)

$posAnclaSync = $nuevo.IndexOf($anclaSync)
$nuevo = $nuevo.Substring(0, $posAnclaSync) + $bloque2 + "`r`n`r`n" + $nuevo.Substring($posAnclaSync)

$nuevo = $nuevo.TrimEnd() + "`r`n`r`n" + $bloque1 + "`r`n"

# --- Verificaciones de salida ---------------------------------------------------------------

$aperturas = ([regex]::Matches($nuevo, '\{')).Count
$cierres = ([regex]::Matches($nuevo, '\}')).Count
$aperturasOrig = ([regex]::Matches($original, '\{')).Count
$cierresOrig = ([regex]::Matches($original, '\}')).Count
if (($aperturas - $cierres) -ne ($aperturasOrig - $cierresOrig)) {
    Fallar "El balance de llaves cambio respecto al original. El recorte de bloques salio mal."
}

foreach ($esperado in @('/sync/push-cuadre', '/api/pos/cuadre', 'function ensure_cuadre_schema', 'function cuadre_branch_code', 'function cuadre_payload_vacio')) {
    if ($nuevo -notmatch [regex]::Escape($esperado)) { Fallar "Falto '$esperado' en el archivo generado." }
}

$rutasOriginales = [regex]::Matches($original, "\`$path === '(/[^']+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$rutasNuevas = [regex]::Matches($nuevo, "\`$path === '(/[^']+)'") | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$perdidas = $rutasOriginales | Where-Object { $rutasNuevas -notcontains $_ }
if ($perdidas) { Fallar ("Se perdieron rutas existentes: " + ($perdidas -join ', ')) }

# --- Escribir -------------------------------------------------------------------------------

$sello = Get-Date -Format 'yyyyMMdd-HHmmss'
$respaldo = "$IndexPath.backup-$sello"
$salida = [System.IO.Path]::ChangeExtension($IndexPath, $null) + 'parchado.php'

Copy-Item -Path $IndexPath -Destination $respaldo
$utf8SinBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($salida, $nuevo, $utf8SinBom)

Write-Host ""
Write-Host "Parche aplicado." -ForegroundColor Green
Write-Host "  Respaldo : $respaldo"
Write-Host "  Generado : $salida"
Write-Host ""
Write-Host "Rutas nuevas:" -ForegroundColor Cyan
Write-Host "  POST /sync/push-cuadre            (requiere X-Sync-Token)"
Write-Host "  GET  /api/pos/cuadre"
Write-Host "  GET  /api/pos/cuadre/disponibles"
Write-Host ""
Write-Host "Antes de subir: correr 'php -l' sobre el archivo generado si hay PHP a la mano," -ForegroundColor Yellow
Write-Host "y correr verificar-api-cuadre.ps1 ANTES y DESPUES de subir para comparar." -ForegroundColor Yellow
