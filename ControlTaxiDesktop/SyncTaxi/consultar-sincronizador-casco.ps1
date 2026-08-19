[CmdletBinding()]
param()

$scriptFolder = Split-Path -Parent $MyInvocation.MyCommand.Path
$installerPath = Join-Path $scriptFolder 'instalar-sincronizador-casco.ps1'

if (-not (Test-Path $installerPath)) {
    throw "No se encontro el instalador base: $installerPath"
}

& $installerPath -Status
