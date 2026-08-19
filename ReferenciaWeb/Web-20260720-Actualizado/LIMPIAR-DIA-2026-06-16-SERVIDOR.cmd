@echo off
setlocal
set "BASE=%~dp0"
set "SCRIPT=%BASE%api-php-hostinger\LIMPIAR-DIA-2026-06-16-SERVIDOR.ps1"

if not exist "%SCRIPT%" (
  echo No se encontro "%SCRIPT%".
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
pause
