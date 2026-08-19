@echo off
setlocal
set "SCRIPT_DIR=%~dp0"

echo.
echo ============================================================
echo   CONTROL TAXI - DIAGNOSTICO DE RED
echo ============================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
  "Write-Host '[1] IPs de esta PC:';" ^
  "Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254*' } | Select-Object IPAddress,InterfaceAlias | Format-Table -AutoSize;" ^
  "Write-Host '';" ^
  "Write-Host '[2] Puerto 5298 escuchando:';" ^
  "$listen = Get-NetTCPConnection -LocalPort 5298 -State Listen -ErrorAction SilentlyContinue;" ^
  "if ($listen) { $listen | Select-Object LocalAddress,LocalPort,OwningProcess | Format-Table -AutoSize } else { Write-Host 'NO esta escuchando. La app Control Taxi esta apagada o no arranco.' -ForegroundColor Red };" ^
  "Write-Host '';" ^
  "Write-Host '[3] Prueba local localhost:5298:';" ^
  "$testLocal = Test-NetConnection 127.0.0.1 -Port 5298 -WarningAction SilentlyContinue;" ^
  "$testLocal | Select-Object ComputerName,RemotePort,TcpTestSucceeded | Format-List;" ^
  "Write-Host '[4] URLs que debes probar desde otra computadora en la misma red/VPN:';" ^
  "$ips = Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254*' };" ^
  "foreach ($ip in $ips) { Write-Host ('http://' + $ip.IPAddress + ':5298  (' + $ip.InterfaceAlias + ')') -ForegroundColor Cyan }"

echo.
echo Si el puerto 5298 dice NO esta escuchando, abre:
echo   Abrir-ControlTaxi.cmd
echo.
echo Si localhost funciona pero desde fuera no, falta firewall/router/VPN.
echo.
pause
