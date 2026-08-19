@echo off
echo Abriendo puertos de entrada para Control Taxi...
netsh advfirewall firewall delete rule name="Control Taxi Web 5298" >nul 2>&1
netsh advfirewall firewall delete rule name="Control Taxi SQL 1433" >nul 2>&1
netsh advfirewall firewall delete rule name="Control Taxi SQL Browser 1434" >nul 2>&1

netsh advfirewall firewall add rule name="Control Taxi Web 5298" dir=in action=allow protocol=TCP localport=5298
netsh advfirewall firewall add rule name="Control Taxi SQL 1433" dir=in action=allow protocol=TCP localport=1433
netsh advfirewall firewall add rule name="Control Taxi SQL Browser 1434" dir=in action=allow protocol=UDP localport=1434

echo.
echo Listo. Deben verse estas reglas en Reglas de entrada:
echo - Control Taxi Web 5298 TCP
echo - Control Taxi SQL 1433 TCP
echo - Control Taxi SQL Browser 1434 UDP
pause
