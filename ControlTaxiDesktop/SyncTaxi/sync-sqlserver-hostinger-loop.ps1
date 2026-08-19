param(
    [int]$IntervalSeconds = 60,
    [int]$MaxRunSeconds = 180
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$syncScript = Join-Path $scriptDir "sync-sqlserver-hostinger-bidirectional.ps1"
$lockFile = Join-Path $scriptDir "sync-sqlserver-hostinger-loop.pid"
$logDir = Join-Path $scriptDir "logs"
$logFile = Join-Path $logDir "sync-sqlserver-hostinger-loop.log"
$outputFile = Join-Path $logDir "sync-sqlserver-hostinger.out.tmp"
$errorFile = Join-Path $logDir "sync-sqlserver-hostinger.err.tmp"

function Write-Log([string]$Message) {
    if (-not [string]::IsNullOrWhiteSpace($logDir) -and -not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    if ((Test-Path $logFile) -and ((Get-Item $logFile).Length -gt 5242880)) {
        $archive = Join-Path $logDir ("sync-sqlserver-hostinger-loop-{0}.log" -f (Get-Date -Format "yyyyMMddHHmmss"))
        Move-Item -LiteralPath $logFile -Destination $archive -Force
        Get-ChildItem -Path $logDir -Filter "sync-sqlserver-hostinger-loop-*.log" -File |
            Sort-Object LastWriteTime -Descending |
            Select-Object -Skip 10 |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }
    $line = "$(Get-Date -Format 'dd/MM/yyyy HH:mm:ss') $Message"
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

if (-not (Test-Path $syncScript)) {
    Write-Log "No se encontro el script de sincronizacion: $syncScript"
    throw "No se encontro el script de sincronizacion: $syncScript"
}

if (Test-Path $lockFile) {
    $existingPid = (Get-Content -Path $lockFile -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($existingPid) {
        $existingProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$existingPid" -ErrorAction SilentlyContinue
        if ($existingProcess -and $existingProcess.CommandLine -like "*sync-sqlserver-hostinger-loop.ps1*") {
            Write-Log "Ya hay un sincronizador automatico corriendo con PID $existingPid. No se inicia otro."
            exit 0
        }
    }
}

Set-Content -Path $lockFile -Value $PID -Encoding ASCII
Write-Log "Sincronizador automatico iniciado. Intervalo: $IntervalSeconds segundos. PID: $PID"

try {
    while ($true) {
        try {
            Write-Log "Ejecutando sincronizacion..."
            if (-not (Test-Path $logDir)) {
                New-Item -ItemType Directory -Path $logDir -Force | Out-Null
            }
            Remove-Item -LiteralPath $outputFile, $errorFile -Force -ErrorAction SilentlyContinue
            $quotedSyncScript = '"' + $syncScript + '"'
            $process = Start-Process -FilePath "powershell.exe" `
                -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $quotedSyncScript) `
                -WindowStyle Hidden `
                -RedirectStandardOutput $outputFile `
                -RedirectStandardError $errorFile `
                -PassThru

            if ($process.WaitForExit($MaxRunSeconds * 1000)) {
                Write-Log "Sincronizacion terminada con codigo $($process.ExitCode)."
                if (Test-Path $outputFile) {
                    Get-Content -Path $outputFile -ErrorAction SilentlyContinue |
                        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                        ForEach-Object { Write-Log "SALIDA: $_" }
                }
                if (Test-Path $errorFile) {
                    Get-Content -Path $errorFile -ErrorAction SilentlyContinue |
                        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                        ForEach-Object { Write-Log "ERROR: $_" }
                }
            } else {
                Write-Log "Sincronizacion excedio $MaxRunSeconds segundos. Se cerrara para reiniciar el ciclo."
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $outputFile, $errorFile -Force -ErrorAction SilentlyContinue
        } catch {
            Write-Log "Error en sincronizacion: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds $IntervalSeconds
    }
} finally {
    Remove-Item -Path $lockFile -Force -ErrorAction SilentlyContinue
    Write-Log "Sincronizador automatico detenido."
}
