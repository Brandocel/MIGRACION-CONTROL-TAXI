@echo off
setlocal

set "TASK_NAME=SyncTaxiHostingerServicio"
set "BASE=%~dp0"
set "VBS=%BASE%iniciar-sincronizador-automatico.vbs"
set "INSTALLER_PS=%BASE%instalar-tarea-sincronizador.ps1"

if not exist "%VBS%" (
    echo No se encontro el archivo:
    echo %VBS%
    pause
    exit /b 1
)

echo Instalando sincronizador oculto como tarea automatica de Windows...
echo Carpeta: %BASE%
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%INSTALLER_PS%" -TaskName "%TASK_NAME%"
if errorlevel 1 (
    echo.
    echo No se pudo instalar. Ejecuta este archivo como Administrador.
    pause
    exit /b 1
)

schtasks /Run /TN "%TASK_NAME%"

echo.
echo Listo. El sincronizador queda trabajando oculto al arrancar Windows.
echo El loop interno sincroniza cada 1 minuto y evita abrir procesos duplicados.
echo No debe salir ventana de PowerShell.
pause
