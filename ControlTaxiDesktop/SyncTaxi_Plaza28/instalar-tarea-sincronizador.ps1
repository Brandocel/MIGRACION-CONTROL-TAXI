param(
    [string]$TaskName = "SyncTaxiHostingerServicio",
    [int]$EveryMinutes = 0
)

$ErrorActionPreference = "Stop"

$folder = Split-Path -Parent $MyInvocation.MyCommand.Path
$vbsPath = Join-Path $folder "iniciar-sincronizador-automatico.vbs"

if (-not (Test-Path -LiteralPath $vbsPath)) {
    throw "No se encontro el sincronizador: $vbsPath"
}

$action = New-ScheduledTaskAction -Execute "wscript.exe" -Argument "`"$vbsPath`""
$triggers = @(
    (New-ScheduledTaskTrigger -AtStartup),
    (New-ScheduledTaskTrigger -AtLogOn)
)
$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Days 0)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings -Description "Mantiene activo el sincronizador SyncTaxi y lo reinicia al arrancar Windows." -Force | Out-Null

Write-Host "Tarea instalada: $TaskName"
Write-Host "Se inicia automaticamente al prender el servidor y al iniciar sesion."
Write-Host "Windows intentara reiniciarla si se detiene por error."
Write-Host "El loop interno corre cada 60 segundos."
Write-Host "Ejecuta: $vbsPath"
