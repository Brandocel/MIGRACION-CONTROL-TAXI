@echo off
setlocal

set "APP_DIR=C:\Users\reyna\Desktop\CONTROL TAXI\PUBLICAR_CONTROL_TAXI"
set "DOTNET_EXE=C:\Program Files\dotnet\dotnet.exe"
set "URL_SERVIDOR=http://26.38.252.71:5298"
set "URL_ESCUCHA=http://0.0.0.0:5298"

if not exist "%DOTNET_EXE%" (
  set "DOTNET_EXE=C:\Program Files (x86)\dotnet\dotnet.exe"
)

if not exist "%DOTNET_EXE%" (
  echo No se encontro dotnet.exe.
  pause
  exit /b 1
)

if not exist "%APP_DIR%\ControlTaxiWeb.dll" (
  echo No se encontro ControlTaxiWeb.dll en:
  echo %APP_DIR%
  pause
  exit /b 1
)

netstat -ano | findstr /R /C:":5298 .*LISTENING" >nul
if errorlevel 1 (
  start "Control Taxi Servidor" /min "%DOTNET_EXE%" "%APP_DIR%\ControlTaxiWeb.dll" --urls "%URL_ESCUCHA%"
  timeout /t 4 /nobreak >nul
)

start "" "%URL_SERVIDOR%"
exit /b 0
