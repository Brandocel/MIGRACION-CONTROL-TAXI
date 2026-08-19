@echo off
set "PROJECT_DIR=%~dp0"
set "SHORTCUT_NAME=Control Taxi.lnk"

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$desktop=[Environment]::GetFolderPath('Desktop'); $project=$env:PROJECT_DIR.TrimEnd('\'); $target=Join-Path $project 'Abrir-ControlTaxi.cmd'; $lnk=Join-Path $desktop $env:SHORTCUT_NAME; $shell=New-Object -ComObject WScript.Shell; $shortcut=$shell.CreateShortcut($lnk); $shortcut.TargetPath=$target; $shortcut.Arguments=''; $shortcut.WorkingDirectory=$project; $shortcut.IconLocation='C:\Windows\System32\shell32.dll,13'; $shortcut.Save(); Write-Host 'Acceso directo listo:' $lnk"

pause
