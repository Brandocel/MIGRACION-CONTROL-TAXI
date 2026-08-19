param(
    [int]$IntervalSeconds = 60,
    [int]$MaxRunSeconds = 180
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$syncScript = Join-Path $scriptDir "sync-sqlserver-hostinger-bidirectional.ps1"
$lockFile = Join-Path $scriptDir "sync-sqlserver-hostinger-loop.pid"
$logFile = Join-Path $scriptDir "sync-sqlserver-hostinger-loop.log"

function Write-Log([string]$Message) {
    $logDir = Split-Path -Parent $logFile
    if (-not [string]::IsNullOrWhiteSpace($logDir) -and -not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    $line = "$(Get-Date -Format 'dd/MM/yyyy HH:mm:ss') $Message"
    Add-Content -Path $logFile -Value $line -Encoding UTF8
}

function Clear-OldTempFiles {
    try {
        Get-ChildItem -Path $scriptDir -Filter "sync-sqlserver-hostinger-*.tmp" -File -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -lt (Get-Date).AddMinutes(-5) } |
            Remove-Item -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Log "No se pudieron limpiar temporales antiguos: $($_.Exception.Message)"
    }
}

if (-not (Test-Path $syncScript)) {
    Write-Log "No se encontro el script de sincronizacion: $syncScript"
    throw "No se encontro el script de sincronizacion: $syncScript"
}

if (Test-Path $lockFile) {
    $existingPid = (Get-Content -Path $lockFile -ErrorAction SilentlyContinue | Select-Object -First 1)
    if ($existingPid -and (Get-Process -Id ([int]$existingPid) -ErrorAction SilentlyContinue)) {
        Write-Log "Ya hay un sincronizador automatico corriendo con PID $existingPid. No se inicia otro."
        exit 0
    }
}

Set-Content -Path $lockFile -Value $PID -Encoding ASCII
Write-Log "Sincronizador automatico iniciado. Intervalo: $IntervalSeconds segundos. PID: $PID"
Clear-OldTempFiles

try {
    while ($true) {
        try {
            Clear-OldTempFiles
            Write-Log "Ejecutando sincronizacion..."
            $runStamp = Get-Date -Format "yyyyMMddHHmmssfff"
            $outputFile = Join-Path $scriptDir "sync-sqlserver-hostinger-$runStamp.out.tmp"
            $errorFile = Join-Path $scriptDir "sync-sqlserver-hostinger-$runStamp.err.tmp"
            $process = Start-Process -FilePath "powershell.exe" `
                -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $syncScript, "-RunLocalMirrorReview") `
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
            Remove-Item -Path $outputFile, $errorFile -Force -ErrorAction SilentlyContinue
        } catch {
            Write-Log "Error en sincronizacion: $($_.Exception.Message)"
        }

        Start-Sleep -Seconds $IntervalSeconds
    }
} finally {
    Remove-Item -Path $lockFile -Force -ErrorAction SilentlyContinue
    Write-Log "Sincronizador automatico detenido."
}
