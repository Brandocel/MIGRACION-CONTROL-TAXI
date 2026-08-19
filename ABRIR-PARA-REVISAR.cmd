@echo off
REM ===========================================================================
REM  Doble clic aqui para abrir Control Taxi en modo revision.
REM  No arranca el sincronizador ni toca produccion.
REM ===========================================================================
title Control Taxi - Modo revision
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0abrir-para-revisar.ps1"
