Option Explicit

Dim shell, fso, scriptFolder, workspaceRoot
Dim releaseExePath, debugExePath, releaseDllPath, debugDllPath, dotnetPath
Dim toolPath, usesDotnet, wmi, processes, process, commandLine
Dim badgeSyncRunning, autoSyncRunning, badgeCommand, autoCommand

Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

scriptFolder = fso.GetParentFolderName(WScript.ScriptFullName)
workspaceRoot = ResolveWorkspaceRoot(scriptFolder)

releaseExePath = workspaceRoot & "\Tools\ControlTaxiDesktop.Tools.exe"
debugExePath = workspaceRoot & "\ControlTaxiDesktop.Tools\bin\Debug\net9.0-windows\ControlTaxiDesktop.Tools.exe"
releaseDllPath = workspaceRoot & "\ControlTaxiDesktop.Tools\bin\Release\net9.0-windows\ControlTaxiDesktop.Tools.dll"
debugDllPath = workspaceRoot & "\ControlTaxiDesktop.Tools\bin\Debug\net9.0-windows\ControlTaxiDesktop.Tools.dll"

usesDotnet = False
If fso.FileExists(releaseExePath) Then
    toolPath = releaseExePath
ElseIf fso.FileExists(debugExePath) Then
    toolPath = debugExePath
ElseIf fso.FileExists(releaseDllPath) Then
    toolPath = releaseDllPath
    usesDotnet = True
ElseIf fso.FileExists(debugDllPath) Then
    toolPath = debugDllPath
    usesDotnet = True
Else
    WScript.Quit 0
End If

Set wmi = GetObject("winmgmts:\\.\root\cimv2")
Set processes = wmi.ExecQuery("SELECT Name, CommandLine FROM Win32_Process")
badgeSyncRunning = False
autoSyncRunning = False

For Each process In processes
    commandLine = ""
    On Error Resume Next
    commandLine = process.CommandLine
    On Error GoTo 0

    If InStr(1, commandLine, "ControlTaxiDesktop.Tools", vbTextCompare) > 0 _
        And InStr(1, commandLine, "casco-badge-sync-watch", vbTextCompare) > 0 Then
        badgeSyncRunning = True
    End If

    If InStr(1, commandLine, "ControlTaxiDesktop.Tools", vbTextCompare) > 0 _
        And InStr(1, commandLine, "casco-auto-sync-watch", vbTextCompare) > 0 Then
        autoSyncRunning = True
    End If
Next

shell.CurrentDirectory = workspaceRoot

If usesDotnet Then
    dotnetPath = "C:\Program Files\dotnet\dotnet.exe"
    If Not fso.FileExists(dotnetPath) Then
        WScript.Quit 0
    End If

    badgeCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""Set-Location -LiteralPath '" & Replace(workspaceRoot, "'", "''") & "'; $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; & '" & Replace(dotnetPath, "'", "''") & "' '" & Replace(toolPath, "'", "''") & "' casco-badge-sync-watch"""
    autoCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""Set-Location -LiteralPath '" & Replace(workspaceRoot, "'", "''") & "'; $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; & '" & Replace(dotnetPath, "'", "''") & "' '" & Replace(toolPath, "'", "''") & "' casco-auto-sync-watch"""
Else
    badgeCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""Set-Location -LiteralPath '" & Replace(workspaceRoot, "'", "''") & "'; & '" & Replace(toolPath, "'", "''") & "' casco-badge-sync-watch"""
    autoCommand = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command ""Set-Location -LiteralPath '" & Replace(workspaceRoot, "'", "''") & "'; & '" & Replace(toolPath, "'", "''") & "' casco-auto-sync-watch"""
End If

If Not badgeSyncRunning Then
    shell.Run badgeCommand, 0, False
End If

If Not autoSyncRunning Then
    shell.Run autoCommand, 0, False
End If

WScript.Quit 0

Function ResolveWorkspaceRoot(currentSyncFolder)
    Dim parentFolder, grandParentFolder
    parentFolder = fso.GetParentFolderName(currentSyncFolder)
    grandParentFolder = fso.GetParentFolderName(parentFolder)

    If fso.FileExists(parentFolder & "\Tools\ControlTaxiDesktop.Tools.exe") _
        Or fso.FileExists(parentFolder & "\Desktop\ControlTaxiDesktop.exe") _
        Or fso.FileExists(parentFolder & "\SyncTaxi\sync.casco.config.json") Then
        ResolveWorkspaceRoot = parentFolder
        Exit Function
    End If

    If fso.FileExists(grandParentFolder & "\Tools\ControlTaxiDesktop.Tools.exe") _
        Or fso.FileExists(grandParentFolder & "\Desktop\ControlTaxiDesktop.exe") _
        Or fso.FileExists(grandParentFolder & "\SyncTaxi\sync.casco.config.json") Then
        ResolveWorkspaceRoot = grandParentFolder
        Exit Function
    End If

    ResolveWorkspaceRoot = parentFolder
End Function
