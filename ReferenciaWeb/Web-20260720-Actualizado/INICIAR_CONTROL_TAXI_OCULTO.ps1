$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$appDir = $scriptDir
if (-not (Test-Path -LiteralPath (Join-Path $appDir 'ControlTaxiWeb.exe'))) {
    $appDir = Join-Path $scriptDir 'PUBLICAR_CONTROL_TAXI'
}
$exePath = Join-Path $appDir 'ControlTaxiWeb.exe'

if (Get-Process ControlTaxiWeb -ErrorAction SilentlyContinue) {
    exit 0
}

if (-not (Test-Path -LiteralPath $exePath)) {
    exit 1
}

Start-Process -FilePath $exePath -ArgumentList @('--urls', 'http://0.0.0.0:5298') -WorkingDirectory $appDir -WindowStyle Hidden
