@echo off
setlocal
cd /d "%~dp0"
wscript "%~dp0iniciar-sincronizador-casco-oculto.vbs"
echo Sincronizador de Casco iniciado en modo oculto.
pause
