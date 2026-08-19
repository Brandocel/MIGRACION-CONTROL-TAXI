@echo off
set "APP_DIR=%~dp0PUBLICAR_CONTROL_TAXI\"
if not exist "%APP_DIR%ControlTaxiWeb.exe" set "APP_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "if (-not (Get-Process ControlTaxiWeb -ErrorAction SilentlyContinue)) { Start-Process -FilePath '%APP_DIR%ControlTaxiWeb.exe' -ArgumentList '--urls','http://0.0.0.0:5298' -WorkingDirectory '%APP_DIR%' -WindowStyle Hidden }"
