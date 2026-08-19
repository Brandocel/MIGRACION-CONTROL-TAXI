@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar-sincronizador-plaza28.ps1" -Status
echo.
echo Ultimo log:
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-Content -Path '%~dp0..\Logs\Plaza28Sync\plaza28-auto-sync.log' -Tail 40 -ErrorAction SilentlyContinue"
pause
