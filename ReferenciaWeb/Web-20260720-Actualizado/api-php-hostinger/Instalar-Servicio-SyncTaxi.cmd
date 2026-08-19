@echo off
setlocal

set "TASK_NAME=SyncTaxiHostingerServicio"
set "BASE=%~dp0"
set "VBS=%BASE%iniciar-sincronizador-automatico.vbs"

if not exist "%VBS%" (
    echo No se encontro el archivo:
    echo %VBS%
    pause
    exit /b 1
)

echo Instalando sincronizador oculto como tarea automatica de Windows...
echo Carpeta: %BASE%
echo.

schtasks /Create /TN "%TASK_NAME%" /SC ONSTART /TR "wscript.exe \"%VBS%\"" /RL HIGHEST /F
if errorlevel 1 (
    echo.
    echo No se pudo instalar. Ejecuta este archivo como Administrador.
    pause
    exit /b 1
)

schtasks /Run /TN "%TASK_NAME%"

echo.
echo Listo. El sincronizador queda trabajando oculto al arrancar Windows.
echo El loop interno sincroniza cada 1 minuto.
echo No debe salir ventana de PowerShell.
pause
