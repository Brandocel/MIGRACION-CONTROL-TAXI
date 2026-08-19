Option Explicit

Dim shell, fso, scriptFolder, scriptPath, commandLine, wmi, processes, process
Dim currentCommandLine, alreadyRunning

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

scriptFolder = fso.GetParentFolderName(WScript.ScriptFullName)
scriptPath = scriptFolder & "\sync-plaza28-loop.ps1"

If Not fso.FileExists(scriptPath) Then
    WScript.Echo "No se encontro el sincronizador Plaza 28: " & scriptPath
    WScript.Quit 1
End If

Set wmi = GetObject("winmgmts:\\.\root\cimv2")
Set processes = wmi.ExecQuery("SELECT Name, CommandLine FROM Win32_Process")
alreadyRunning = False

For Each process In processes
    currentCommandLine = ""
    On Error Resume Next
    currentCommandLine = process.CommandLine
    On Error GoTo 0

    If InStr(1, currentCommandLine, "sync-plaza28-loop.ps1", vbTextCompare) > 0 Then
        alreadyRunning = True
    End If
Next

If Not alreadyRunning Then
    commandLine = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & scriptPath & """"
    shell.Run commandLine, 0, False
End If

WScript.Quit 0
