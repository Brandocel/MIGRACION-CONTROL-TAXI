@echo off
setlocal

set "TASK_NAME=SyncTaxiHostingerServicio"
set "BASE=%~dp0"
set "VBS=%BASE%iniciar-sincronizador-automatico.vbs"
set "INSTALLER=%BASE%Instalar-Servicio-SyncTaxi.cmd"

echo Iniciando sincronizador oculto...
schtasks /Query /TN "%TASK_NAME%" >nul 2>nul
if errorlevel 1 (
    echo La tarea automatica no existe aun. Se intentara instalar...
    if exist "%INSTALLER%" (
        call "%INSTALLER%"
        goto :eof
    ) else (
        echo No se encontro el instalador:
        echo %INSTALLER%
        pause
        exit /b 1
    )
)

schtasks /Change /TN "%TASK_NAME%" /ENABLE >nul 2>nul
schtasks /Run /TN "%TASK_NAME%" >nul 2>nul
if exist "%VBS%" (
    start "" /min wscript.exe "%VBS%"
)

echo.
echo Listo. El sincronizador esta activo.
pause
