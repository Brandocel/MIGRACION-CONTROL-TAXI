# Script para ejecutar ControlTaxiDesktop contra bases locales
# Este script establece las variables de entorno necesarias para que la aplicación
# se conecte a REYNA en lugar de al servidor de producción

$projectRoot = Split-Path -Parent $PSScriptRoot
$desktopExe = "ControlTaxiDesktop\bin\Release\net9.0-windows\ControlTaxiDesktop.exe"
$exePath = Join-Path -Path $projectRoot -ChildPath $desktopExe

if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: No se encontró el ejecutable en: $exePath" -ForegroundColor Red
    Write-Host "Asegúrate de haber compilado primero con: dotnet build ControlTaxiDesktop/ControlTaxiDesktop.csproj -c Release" -ForegroundColor Yellow
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "EJECUTAR CONTROL TAXI DESKTOP - MODO LOCAL" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "CONFIGURACION LOCAL:" -ForegroundColor Green
Write-Host "  Servidor: REYNA" -ForegroundColor Green
Write-Host "  Base Operación (mkt): mkt2" -ForegroundColor Green
Write-Host "  Base Compuadmo: compuadmoPlaza" -ForegroundColor Green
Write-Host "  Base Joyería: joyeriaPlaza" -ForegroundColor Green
Write-Host ""

Write-Host "Iniciando ControlTaxiDesktop..." -ForegroundColor Yellow
Write-Host "Ejecutable: $exePath" -ForegroundColor Gray
Write-Host ""

# Establecer variables de entorno para conectarse a REYNA
$env:PLAZA28_SQL_SERVER = "REYNA"
$env:PLAZA28_SQL_USER = "sa"
# La contrasena NO se guarda en el repositorio. Se toma de la variable de
# entorno PLAZA28_SQL_PASSWORD si ya existe; si no, se pide al ejecutar.
if ([string]::IsNullOrWhiteSpace($env:PLAZA28_SQL_PASSWORD)) {
    $securePassword = Read-Host -Prompt "Password SQL para $($env:PLAZA28_SQL_USER)@REYNA" -AsSecureString
    $env:PLAZA28_SQL_PASSWORD = [System.Net.NetworkCredential]::new("", $securePassword).Password
}
$env:PLAZA28_SQL_DATABASE = "mkt2"

# Cambiar al directorio del proyecto para que los archivos de configuración se encuentren correctamente
Push-Location -Path $projectRoot

# Ejecutar el Desktop
& $exePath

Pop-Location
