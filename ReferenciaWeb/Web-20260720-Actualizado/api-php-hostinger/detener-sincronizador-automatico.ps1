$ErrorActionPreference = "SilentlyContinue"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$lockFile = Join-Path $scriptDir "sync-sqlserver-hostinger-loop.pid"

if (Test-Path $lockFile) {
    $pidText = Get-Content -Path $lockFile | Select-Object -First 1
    if ($pidText) {
        Stop-Process -Id ([int]$pidText) -Force
    }
    Remove-Item -Path $lockFile -Force
}

Get-CimInstance Win32_Process |
    Where-Object { $_.CommandLine -like "*sync-sqlserver-hostinger-loop.ps1*" } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force }

Write-Host "Sincronizador automatico detenido."
