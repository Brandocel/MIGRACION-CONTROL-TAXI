@echo off
setlocal

set "TASK_NAME=SyncTaxiHostingerServicio"
set "LOG_FILE=%~dp0sync-sqlserver-hostinger-loop.log"

echo Estado del sincronizador:
echo.
schtasks /Query /TN "%TASK_NAME%" /V /FO LIST >nul 2>nul
if errorlevel 1 (
    echo La tarea automatica "%TASK_NAME%" no existe o no quedo instalada.
    echo Eso no significa que la sincronizacion este fallando:
    echo si abajo ves lineas nuevas en el log, ahorita esta corriendo manual/oculta.
    echo.
    echo Para dejarlo automatico al prender Windows, ejecuta como Administrador:
    echo   Instalar-Servicio-SyncTaxi.cmd
) else (
    schtasks /Query /TN "%TASK_NAME%" /V /FO LIST
)

echo.
echo Ultimas lineas del log:
if exist "%LOG_FILE%" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Get-Content -Path '%LOG_FILE%' | Select-Object -Last 20"
) else (
    echo No existe aun el log: %LOG_FILE%
)

echo.
pause
