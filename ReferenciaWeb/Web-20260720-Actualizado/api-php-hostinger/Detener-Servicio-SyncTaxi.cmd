@echo off
setlocal

set "TASK_NAME=SyncTaxiHostingerServicio"

echo Deteniendo sincronizador oculto...
schtasks /End /TN "%TASK_NAME%" >nul 2>nul
schtasks /Change /TN "%TASK_NAME%" /DISABLE

echo.
echo Listo. El sincronizador queda detenido/deshabilitado.
pause
