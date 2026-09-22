@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0revisar-sincronizador-plaza28.ps1" %*
pause
