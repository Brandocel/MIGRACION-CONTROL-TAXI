Option Explicit

Dim shell, fso, appDir, serverScript, processes
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

appDir = fso.BuildPath(fso.GetParentFolderName(WScript.ScriptFullName), "PUBLICAR_CONTROL_TAXI")
serverScript = fso.BuildPath(appDir, "INICIAR_CONTROL_TAXI_OCULTO.vbs")

Set processes = GetObject("winmgmts:\\.\root\cimv2").ExecQuery( _
    "SELECT ProcessId FROM Win32_Process WHERE Name='ControlTaxiWeb.exe'")

If processes.Count = 0 Then
    shell.Run """" & serverScript & """", 0, True
    WScript.Sleep 3500
End If

shell.Run "http://localhost:5298/Pos/Login", 1, False
