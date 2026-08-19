@echo off
set "PROJECT_DIR=%~dp0"
set "ASPNETCORE_ENVIRONMENT=Production"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -NoProfile -File "%PROJECT_DIR%Start-ControlTaxi.ps1"
