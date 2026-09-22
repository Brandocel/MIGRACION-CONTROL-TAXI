@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0aplicar-reglas-comisiones-casco.ps1" %*
pause
