<#
    verificar-api-cuadre.ps1
    Fecha: 2026-09-09

    Prueba de humo del API de Hostinger. Se corre ANTES de subir el parche del cuadre y otra
    vez DESPUES, y se comparan las dos salidas: si alguna ruta que hoy funciona deja de
    responder igual, el parche rompio algo y hay que restaurar el respaldo.

    Solo hace GET. No escribe nada en la base ni sube archivos.

    Uso:
        .\verificar-api-cuadre.ps1 -Salida antes.json
        # ... subir el parche ...
        .\verificar-api-cuadre.ps1 -Salida despues.json
        .\verificar-api-cuadre.ps1 -Comparar antes.json despues.json
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://lightyellow-porpoise-679527.hostingersite.com',
    [string]$Salida = '',
    [string[]]$Comparar = @(),
    [int]$TimeoutSec = 90
)

$ErrorActionPreference = 'Stop'

if ($Comparar.Count -eq 2) {
    $a = Get-Content $Comparar[0] -Raw | ConvertFrom-Json
    $b = Get-Content $Comparar[1] -Raw | ConvertFrom-Json

    Write-Host ""
    Write-Host ("{0,-40} {1,-12} {2,-12} {3}" -f 'RUTA', 'ANTES', 'DESPUES', 'VEREDICTO')
    Write-Host ('-' * 90)

    $rotas = 0
    foreach ($rutaAntes in $a) {
        $rutaDespues = $b | Where-Object { $_.ruta -eq $rutaAntes.ruta } | Select-Object -First 1
        if ($null -eq $rutaDespues) {
            Write-Host ("{0,-40} {1,-12} {2,-12} {3}" -f $rutaAntes.ruta, $rutaAntes.estado, '(falta)', 'REVISAR') -ForegroundColor Red
            $rotas++
            continue
        }

        # Las rutas del cuadre son las que agrega el parche: pasar de 404 a 200 es el cambio
        # que se busca, no una falla. Cualquier OTRA ruta que cambie de codigo si es falla.
        $esRutaNueva = $rutaAntes.ruta -like '*/api/pos/cuadre*'

        $veredicto = 'igual'
        $color = 'Green'
        if ($rutaAntes.estado -ne $rutaDespues.estado) {
            if ($esRutaNueva -and $rutaAntes.estado -eq 404 -and $rutaDespues.estado -eq 200) {
                $veredicto = 'ruta nueva, ya responde'
                $color = 'Cyan'
            }
            else {
                $veredicto = 'CAMBIO EL HTTP'
                $color = 'Red'
                $rotas++
            }
        }
        elseif ($rutaAntes.elementos -ne $rutaDespues.elementos) {
            # Los datos vivos cambian solos durante el dia; esto es aviso, no falla.
            $veredicto = "conteo {0} -> {1} (dato vivo)" -f $rutaAntes.elementos, $rutaDespues.elementos
            $color = 'Yellow'
        }

        Write-Host ("{0,-40} {1,-12} {2,-12} {3}" -f $rutaAntes.ruta, $rutaAntes.estado, $rutaDespues.estado, $veredicto) -ForegroundColor $color
    }

    Write-Host ""
    if ($rotas -gt 0) {
        Write-Host "$rotas ruta(s) cambiaron de codigo HTTP. RESTAURAR EL RESPALDO." -ForegroundColor Red
        exit 1
    }
    Write-Host "Ninguna ruta existente cambio de codigo HTTP." -ForegroundColor Green
    exit 0
}

# Rutas que hoy ya funcionan y NO deben cambiar. Si alguna de estas se rompe, el parche
# toco algo que no debia.
$rutas = @(
    '/',
    '/health',
    '/api/taxis/registros?dateFrom=2026-09-01&dateTo=2026-09-09',
    '/api/taxis/catalogo',
    '/api/taxis/tarifas',
    '/api/taxis/taxistas',
    '/api/taxis/vendedores',
    '/api/pos/camiones',
    '/api/pos/transportes',
    '/api/pos/guias',
    # Rutas nuevas del parche: dan 404 antes de subir y 200 despues. Es el unico cambio esperado.
    '/api/pos/cuadre?sucursal=plaza28&dateFrom=2026-09-09&dateTo=2026-09-09',
    '/api/pos/cuadre/disponibles'
)

$resultados = @()

foreach ($ruta in $rutas) {
    $url = $BaseUrl.TrimEnd('/') + $ruta
    $estado = 0
    $elementos = -1
    $muestra = ''

    try {
        $respuesta = Invoke-WebRequest -Uri $url -Method Get -TimeoutSec $TimeoutSec -UseBasicParsing
        $estado = [int]$respuesta.StatusCode
        $cuerpo = $respuesta.Content
        try {
            $json = $cuerpo | ConvertFrom-Json
            if ($json -is [System.Array]) { $elementos = $json.Count }
            elseif ($null -ne $json) { $elementos = ($json.PSObject.Properties | Measure-Object).Count }
        }
        catch { $elementos = -1 }
        $muestra = $cuerpo.Substring(0, [Math]::Min(160, $cuerpo.Length))
    }
    catch {
        if ($_.Exception.Response) { $estado = [int]$_.Exception.Response.StatusCode }
        else { $estado = -1 }
        $muestra = $_.Exception.Message
    }

    $color = if ($estado -eq 200) { 'Green' } elseif ($estado -eq 404) { 'Yellow' } else { 'Red' }
    Write-Host ("{0,-6} {1}" -f $estado, $ruta) -ForegroundColor $color

    $resultados += [pscustomobject]@{
        ruta      = $ruta
        estado    = $estado
        elementos = $elementos
        muestra   = $muestra
    }
}

if ($Salida) {
    $resultados | ConvertTo-Json -Depth 5 | Set-Content -Path $Salida -Encoding utf8
    Write-Host ""
    Write-Host "Guardado en $Salida" -ForegroundColor Cyan
}
