@echo off
REM ===========================================================================
REM  Doble clic aqui UNA SOLA VEZ.
REM  Pide la contrasena de SQL Server y deja la conexion lista para entrar.
REM ===========================================================================
title Control Taxi - Configurar SQL local
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0configurar-sql-local.ps1"
echo.
pause
