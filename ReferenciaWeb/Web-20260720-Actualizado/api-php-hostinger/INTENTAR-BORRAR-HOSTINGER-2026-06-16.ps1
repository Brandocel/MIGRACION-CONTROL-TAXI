param(
    [string]$ApiBaseUrl = "https://lightyellow-porpoise-679527.hostingersite.com",
    [string]$SyncToken = "HokaTaxisSync2050",
    [datetime]$Fecha = "2026-06-16"
)

$ErrorActionPreference = "Stop"

$dateText = $Fecha.ToString("yyyy-MM-dd")
$headers = @{ "X-Sync-Token" = $SyncToken }
$recordsUrl = "$ApiBaseUrl/api/taxis/registros?dateFrom=$dateText&dateTo=$dateText"

Write-Host "Consultando Hostinger: $recordsUrl"
$records = @(Invoke-RestMethod -Method Get -Uri $recordsUrl -Headers $headers)
Write-Host "Registros encontrados para $($Fecha.ToString('dd/MM/yyyy')): $($records.Count)"

if ($records.Count -eq 0) {
    Write-Host "No hay registros que borrar en Hostinger para esa fecha."
    return
}

$deleted = 0
$failed = 0
$notSupported = $false

foreach ($record in $records) {
    $recordId = [string]$record.recordId
    if ([string]::IsNullOrWhiteSpace($recordId)) { continue }

    $url = "$ApiBaseUrl/api/taxis/registros/$([uri]::EscapeDataString($recordId))"
    try {
        Write-Host "Borrando Hostinger recordId=$recordId ..."
        Invoke-RestMethod -Method Delete -Uri $url -Headers $headers | Out-Null
        $deleted++
    } catch {
        $failed++
        $status = $null
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
        }
        if ($status -eq 404 -or $status -eq 405) {
            $notSupported = $true
        }
        Write-Warning "No se pudo borrar recordId=$recordId. HTTP=$status $($_.Exception.Message)"
    }
}

Write-Host "Resultado Hostinger: borrados=$deleted fallidos=$failed"
if ($notSupported) {
    Write-Warning "El API de Hostinger no expone ruta DELETE para registros. En ese caso hay que borrar directamente en la BD de Hostinger/phpMyAdmin o agregar esa ruta al API."
}
