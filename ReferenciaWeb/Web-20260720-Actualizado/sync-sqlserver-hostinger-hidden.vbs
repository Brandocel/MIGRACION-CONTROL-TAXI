Set shell = CreateObject("WScript.Shell")
scriptPath = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName) & "\api-php-hostinger\sync-sqlserver-hostinger-bidirectional.ps1"
command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & scriptPath & """ -SqlServer "".\SQLEXPRESS"""
shell.Run command, 0, False
