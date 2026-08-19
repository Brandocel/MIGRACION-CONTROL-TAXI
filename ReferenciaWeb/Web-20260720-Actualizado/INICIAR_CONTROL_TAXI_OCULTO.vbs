Option Explicit

Dim shell, fso, appDir, cmdPath, command
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

appDir = fso.GetParentFolderName(WScript.ScriptFullName)
cmdPath = fso.BuildPath(appDir, "INICIAR_CONTROL_TAXI_OCULTO.cmd")

If Not fso.FileExists(cmdPath) Then
    MsgBox "No se encontro INICIAR_CONTROL_TAXI_OCULTO.cmd en:" & vbCrLf & appDir, vbCritical, "Control Taxi"
    WScript.Quit 1
End If

command = "%COMSPEC% /c " & Chr(34) & cmdPath & Chr(34)
shell.CurrentDirectory = appDir
shell.Run command, 0, False
