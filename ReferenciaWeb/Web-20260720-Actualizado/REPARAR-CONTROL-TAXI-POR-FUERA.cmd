@echo off
setlocal
set "SCRIPT_DIR=%~dp0"

net session >nul 2>&1
if not "%errorlevel%"=="0" (
  echo Solicitando permisos de administrador...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%SCRIPT_DIR%' -Verb RunAs"
  exit /b
)

echo.
echo ============================================================
echo   CONTROL TAXI - REPARAR ACCESO POR FUERA
echo ============================================================
echo.

echo [1/5] Abriendo puertos de ENTRADA en Windows Firewall...
netsh advfirewall firewall delete rule name="Control Taxi Web 5298" >nul 2>&1
netsh advfirewall firewall delete rule name="Control Taxi SQL 1433" >nul 2>&1
netsh advfirewall firewall delete rule name="Control Taxi SQL Browser 1434" >nul 2>&1
netsh advfirewall firewall add rule name="Control Taxi Web 5298" dir=in action=allow protocol=TCP localport=5298 profile=any
netsh advfirewall firewall add rule name="Control Taxi SQL 1433" dir=in action=allow protocol=TCP localport=1433 profile=any
netsh advfirewall firewall add rule name="Control Taxi SQL Browser 1434" dir=in action=allow protocol=UDP localport=1434 profile=any

echo.
echo [2/5] Fijando SQL Server SQLEXPRESS al puerto TCP 1433...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='SilentlyContinue';" ^
  "$names='HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL';" ^
  "$instance=(Get-ItemProperty -Path $names).SQLEXPRESS;" ^
  "if ($instance) {" ^
  "  $tcp='HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\'+$instance+'\MSSQLServer\SuperSocketNetLib\Tcp';" ^
  "  $ipall=$tcp+'\IPAll';" ^
  "  Set-ItemProperty -Path $tcp -Name Enabled -Value 1;" ^
  "  Set-ItemProperty -Path $ipall -Name TcpDynamicPorts -Value '';" ^
  "  Set-ItemProperty -Path $ipall -Name TcpPort -Value '1433';" ^
  "  Write-Host 'SQL SQLEXPRESS configurado en TCP 1433.';" ^
  "} else { Write-Host 'No encontre la instancia SQLEXPRESS en el registro. Revisa SQL Server Configuration Manager.'; exit 2 }"

echo.
echo [3/5] Reiniciando servicios de SQL Server...
net stop MSSQL$SQLEXPRESS /y
net start MSSQL$SQLEXPRESS
net start SQLBrowser

echo.
echo [4/5] Arrancando Control Taxi en http://0.0.0.0:5298 ...
set "ASPNETCORE_ENVIRONMENT=Production"
set "CONTROLTAXI_URLS=http://0.0.0.0:5298"
set "CONTROLTAXI_BROWSER_URL=http://localhost:5298"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Start-ControlTaxi.ps1"

echo.
echo [5/5] Probando puertos locales...
powershell.exe -NoProfile -Command "Test-NetConnection 127.0.0.1 -Port 5298 | Select-Object ComputerName,RemotePort,TcpTestSucceeded | Format-List"
powershell.exe -NoProfile -Command "Test-NetConnection 127.0.0.1 -Port 1433 | Select-Object ComputerName,RemotePort,TcpTestSucceeded | Format-List"

echo.
echo ============================================================
echo   SI LOCAL SALE TRUE:
echo   Desde fuera abre: http://26.38.252.71:5298
echo.
echo   SI DESDE FUERA NO ABRE:
echo   Falta redireccionar en router/VPN:
echo   TCP 5298  -> IP local de esta PC servidor
echo   TCP 1433  -> IP local de esta PC servidor
echo ============================================================
echo.
pause
