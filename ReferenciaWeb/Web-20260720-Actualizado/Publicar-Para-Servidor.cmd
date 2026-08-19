@echo off
setlocal
set "PROJECT_DIR=%~dp0"
set "PUBLISH_DIR=%PROJECT_DIR%PUBLICAR_CONTROL_TAXI"

dotnet publish "%PROJECT_DIR%ControlTaxiWeb.csproj" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" /p:UseAppHost=true
if errorlevel 1 (
  echo.
  echo No se pudo publicar. Revisa el error de arriba.
  pause
  exit /b 1
)

copy /Y "%PROJECT_DIR%Abrir-ControlTaxi.cmd" "%PUBLISH_DIR%\Abrir-ControlTaxi.cmd" >nul
copy /Y "%PROJECT_DIR%Abrir-ControlTaxi-Red.cmd" "%PUBLISH_DIR%\Abrir-ControlTaxi-Red.cmd" >nul
copy /Y "%PROJECT_DIR%Abrir-Puerto-ControlTaxi-5298.cmd" "%PUBLISH_DIR%\Abrir-Puerto-ControlTaxi-5298.cmd" >nul
copy /Y "%PROJECT_DIR%Start-ControlTaxi.ps1" "%PUBLISH_DIR%\Start-ControlTaxi.ps1" >nul
copy /Y "%PROJECT_DIR%INICIAR_CONTROL_TAXI_OCULTO.vbs" "%PUBLISH_DIR%\INICIAR_CONTROL_TAXI_OCULTO.vbs" >nul
copy /Y "%PROJECT_DIR%INICIAR_CONTROL_TAXI_OCULTO.cmd" "%PUBLISH_DIR%\INICIAR_CONTROL_TAXI_OCULTO.cmd" >nul
copy /Y "%PROJECT_DIR%INICIAR_CONTROL_TAXI_OCULTO.ps1" "%PUBLISH_DIR%\INICIAR_CONTROL_TAXI_OCULTO.ps1" >nul
copy /Y "%PROJECT_DIR%ABRIR_CONTROL_TAXI_SIN_CONSOLA.vbs" "%PUBLISH_DIR%\ABRIR_CONTROL_TAXI_SIN_CONSOLA.vbs" >nul
copy /Y "%PROJECT_DIR%Crear-AccesoDirecto-ControlTaxi.cmd" "%PUBLISH_DIR%\Crear-AccesoDirecto-ControlTaxi.cmd" >nul
copy /Y "%PROJECT_DIR%LEEME-CONTROL-TAXI-RED.txt" "%PUBLISH_DIR%\LEEME-CONTROL-TAXI-RED.txt" >nul

echo.
echo Listo. Copia al servidor esta carpeta:
echo %PUBLISH_DIR%
echo.
echo En el servidor, abre Crear-AccesoDirecto-ControlTaxi.cmd
pause
