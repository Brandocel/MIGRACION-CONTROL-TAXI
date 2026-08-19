@echo off
setlocal
set "APP_DIR=%~dp0PUBLICAR_CONTROL_TAXI"
set "DOTNET_EXE=C:\Program Files\dotnet\dotnet.exe"
set "URL=http://localhost:5298"
set "LISTEN=http://0.0.0.0:5298"

if not exist "%DOTNET_EXE%" (
  set "DOTNET_EXE=C:\Program Files (x86)\dotnet\dotnet.exe"
)

if not exist "%DOTNET_EXE%" (
  echo No se encontro dotnet.exe.
  echo Revisa que exista en:
  echo C:\Program Files\dotnet\dotnet.exe
  pause
  exit /b 1
)

if not exist "%APP_DIR%\ControlTaxiWeb.exe" if not exist "%APP_DIR%\ControlTaxiWeb.dll" (
  echo No se encontro ControlTaxiWeb.exe ni ControlTaxiWeb.dll en:
  echo %APP_DIR%
  pause
  exit /b 1
)

netstat -ano | findstr /R /C:":5298 .*LISTENING" >nul
if errorlevel 1 (
  if exist "%APP_DIR%\ControlTaxiWeb.exe" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%APP_DIR%\ControlTaxiWeb.exe' -ArgumentList @('--urls','%LISTEN%') -WorkingDirectory '%APP_DIR%' -WindowStyle Hidden"
  ) else (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%DOTNET_EXE%' -ArgumentList @('%APP_DIR%\ControlTaxiWeb.dll','--urls','%LISTEN%') -WorkingDirectory '%APP_DIR%' -WindowStyle Hidden"
  )
  timeout /t 4 /nobreak >nul
)

start "" "%URL%"
exit /b 0
