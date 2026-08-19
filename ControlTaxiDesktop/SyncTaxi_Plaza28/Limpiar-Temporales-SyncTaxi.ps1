param(
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$files = @(Get-ChildItem -LiteralPath $scriptDir -File -Filter "sync-sqlserver-hostinger-*.tmp" -ErrorAction SilentlyContinue)
$count = $files.Count
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
if ($null -eq $bytes) { $bytes = 0 }

Write-Host "Temporales encontrados en raiz: $count"
Write-Host ("Espacio aproximado: {0:N2} MB" -f ($bytes / 1MB))

if ($count -eq 0) { return }

if ($WhatIf) {
    $files | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
    return
}

$files | Remove-Item -Force
Write-Host "Temporales eliminados. Los logs nuevos quedan en la carpeta logs."
