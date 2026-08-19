Set shell = CreateObject("WScript.Shell")
scriptPath = CreateObject("Scripting.FileSystemObject").GetParentFolderName(WScript.ScriptFullName) & "\sync-sqlserver-hostinger-bidirectional.ps1"
command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & scriptPath & """ -SqlServer ""."" -MktDatabase ""mkt"" -BranchCode ""28"""
shell.Run command, 0, False
