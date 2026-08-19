param(
    [string]$TaskName = "SyncTaxiHostingerServicio",
    [int]$EveryMinutes = 0
)

$ErrorActionPreference = "Stop"

$folder = Split-Path -Parent $MyInvocation.MyCommand.Path
$vbsPath = Join-Path $folder "sync-sqlserver-hostinger-hidden.vbs"

if (-not (Test-Path -LiteralPath $vbsPath)) {
    throw "No se encontro el sincronizador: $vbsPath"
}

$action = New-ScheduledTaskAction -Execute "wscript.exe" -Argument "`"$vbsPath`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description "Sincroniza registros nuevos de la app movil hacia SQL Server mkt sin actualizar existentes." -Force | Out-Null

Write-Host "Tarea instalada: $TaskName"
Write-Host "Se inicia automaticamente al prender el servidor."
Write-Host "El loop interno corre cada 20 segundos."
Write-Host "Ejecuta: $vbsPath"
