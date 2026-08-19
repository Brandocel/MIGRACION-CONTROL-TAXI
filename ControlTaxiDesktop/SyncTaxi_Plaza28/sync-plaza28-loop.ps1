[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $scriptDir 'sync.plaza28.config.json'
$legacySyncPath = Join-Path $scriptDir 'sync-sqlserver-hostinger-bidirectional.ps1'

function Resolve-WorkspaceRoot {
    $candidates = @(
        (Split-Path -Parent $scriptDir),
        (Split-Path -Parent (Split-Path -Parent $scriptDir)),
        $scriptDir
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if ((Test-Path (Join-Path $candidate 'Config')) -or
            (Test-Path (Join-Path $candidate 'SyncTaxi_Plaza28\sync.plaza28.config.json')) -or
            (Test-Path (Join-Path $candidate 'SyncTaxi\sync.plaza28.config.json'))) {
            return $candidate
        }
    }

    return (Split-Path -Parent $scriptDir)
}

$workspaceRoot = Resolve-WorkspaceRoot
$logRoot = Join-Path $workspaceRoot 'Logs\Plaza28Sync'
$logPath = Join-Path $logRoot 'plaza28-auto-sync.log'
$statusPath = Join-Path $logRoot 'plaza28-auto-sync-status.json'
$lockPath = Join-Path $logRoot 'plaza28-auto-sync.lock'

function Write-Log {
    param([string]$Message)

    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    if ((Test-Path $logPath) -and ((Get-Item $logPath).Length -gt 5242880)) {
        $archive = Join-Path $logRoot ("plaza28-auto-sync-{0}.log" -f (Get-Date -Format "yyyyMMddHHmmss"))
        Move-Item -LiteralPath $logPath -Destination $archive -Force
        Get-ChildItem -Path $logRoot -Filter "plaza28-auto-sync-*.log" -File |
            Sort-Object LastWriteTime -Descending |
            Select-Object -Skip 10 |
            Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Add-Content -Path $logPath -Value ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message) -Encoding UTF8
}

function Write-Status {
    param(
        [bool]$Configured,
        [bool]$PasswordAvailable,
        [int]$IntervalSeconds,
        [int]$LastHttp,
        [int]$LastReceivedCount,
        [int]$LastInsertedCount,
        [int]$LastOmittedCount,
        [string]$LastError,
        [long]$LastDurationMs,
        [bool]$LockActive
    )

    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $status = [ordered]@{
        Configured = $Configured
        PasswordAvailable = $PasswordAvailable
        IntervalSeconds = $IntervalSeconds
        LastCycleAt = [DateTimeOffset]::Now.ToString("o")
        LastHttp = $LastHttp
        LastReceivedCount = $LastReceivedCount
        LastInsertedCount = $LastInsertedCount
        LastOmittedCount = $LastOmittedCount
        LastError = $LastError
        LastDurationMs = $LastDurationMs
        LockActive = $LockActive
    }

    $status | ConvertTo-Json -Depth 4 | Set-Content -Path $statusPath -Encoding UTF8
}

function Read-Plaza28Credential {
    $credentialPath = Join-Path $workspaceRoot 'Config\plaza28.credentials.dat'
    if (-not (Test-Path $credentialPath)) {
        return $null
    }

    Add-Type -AssemblyName System.Security
    $encrypted = [System.IO.File]::ReadAllBytes($credentialPath)
    $entropy = [System.Text.Encoding]::UTF8.GetBytes("ControlTaxiDesktop.Plaza28.Credentials.v1")
    $jsonBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
        $encrypted,
        $entropy,
        [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $json = [System.Text.Encoding]::UTF8.GetString($jsonBytes)
    return $json | ConvertFrom-Json
}

function Read-IntValue {
    param($Value, [int]$Fallback)

    $parsed = 0
    if ($null -ne $Value -and [int]::TryParse([string]$Value, [ref]$parsed)) {
        return $parsed
    }

    return $Fallback
}

if (-not (Test-Path $configPath)) {
    Write-Log "Sincronizador Plaza 28 desactivado: falta sync.plaza28.config.json"
    Write-Status -Configured:$false -PasswordAvailable:$false -IntervalSeconds 0 -LastHttp 0 -LastReceivedCount 0 -LastInsertedCount 0 -LastOmittedCount 0 -LastError 'Falta sync.plaza28.config.json' -LastDurationMs 0 -LockActive:$false
    exit 1
}

if (-not (Test-Path $legacySyncPath)) {
    Write-Log "Sincronizador Plaza 28 desactivado: falta sync-sqlserver-hostinger-bidirectional.ps1"
    Write-Status -Configured:$false -PasswordAvailable:$false -IntervalSeconds 0 -LastHttp 0 -LastReceivedCount 0 -LastInsertedCount 0 -LastOmittedCount 0 -LastError 'Falta script legacy' -LastDurationMs 0 -LockActive:$false
    exit 1
}

$config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
if ([string]$config.BranchCode -ne '28') {
    Write-Log "Sincronizador Plaza 28 desactivado: BranchCode invalido '$($config.BranchCode)'"
    Write-Status -Configured:$true -PasswordAvailable:$false -IntervalSeconds 0 -LastHttp 0 -LastReceivedCount 0 -LastInsertedCount 0 -LastOmittedCount 0 -LastError 'BranchCode invalido' -LastDurationMs 0 -LockActive:$false
    exit 1
}

$credential = Read-Plaza28Credential
$sqlPassword = $null
if ($credential -and -not [string]::IsNullOrWhiteSpace($credential.SqlPassword)) {
    $sqlPassword = [string]$credential.SqlPassword
}
elseif (-not [string]::IsNullOrWhiteSpace($env:PLAZA28_SQL_PASSWORD)) {
    $sqlPassword = $env:PLAZA28_SQL_PASSWORD
}

$intervalSeconds = Read-IntValue $config.IntervalSeconds 15
$maxRunSeconds = Read-IntValue $config.MaxRunSeconds 180
$pullLimit = Read-IntValue $config.PullLimit 500

if ([string]::IsNullOrWhiteSpace($sqlPassword)) {
    Write-Log "Sincronizador Plaza 28 desactivado: falta credencial SQL cifrada o PLAZA28_SQL_PASSWORD"
    Write-Status -Configured:$true -PasswordAvailable:$false -IntervalSeconds $intervalSeconds -LastHttp 0 -LastReceivedCount 0 -LastInsertedCount 0 -LastOmittedCount 0 -LastError 'Falta credencial SQL' -LastDurationMs 0 -LockActive:$false
    exit 1
}

if (Test-Path $lockPath) {
    $existingPid = Get-Content -Path $lockPath -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($existingPid) {
        $existingProcess = Get-CimInstance Win32_Process -Filter "ProcessId=$existingPid" -ErrorAction SilentlyContinue
        if ($existingProcess -and $existingProcess.CommandLine -match 'sync-plaza28-loop.ps1') {
            Write-Log "Ya hay un sincronizador Plaza 28 corriendo con PID $existingPid. No se inicia otro."
            Write-Status -Configured:$true -PasswordAvailable:$true -IntervalSeconds $intervalSeconds -LastHttp 0 -LastReceivedCount 0 -LastInsertedCount 0 -LastOmittedCount 0 -LastError '' -LastDurationMs 0 -LockActive:$true
            exit 0
        }
    }
}

Set-Content -Path $lockPath -Value $PID -Encoding ASCII
Write-Log "Sincronizador Plaza 28 automatico iniciado. Intervalo: $intervalSeconds segundos. PID: $PID"

try {
    while ($true) {
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $lastHttp = 0
        $received = 0
        $inserted = 0
        $omitted = 0
        $lastError = ''

        try {
            Write-Log "Plaza 28 sync iniciado"
            $effectiveSqlServer = [string]$config.SqlServer
            $effectiveSqlUser = [string]$config.SqlUser
            $effectiveMktDatabase = [string]$config.MktDatabase

            if ($credential -and $credential.SqlServer) {
                $effectiveSqlServer = [string]$credential.SqlServer
            }

            if ($credential -and $credential.SqlUser) {
                $effectiveSqlUser = [string]$credential.SqlUser
            }

            if ($credential -and $credential.Database) {
                $effectiveMktDatabase = [string]$credential.Database
            }

            $syncParams = @{
                ApiBaseUrl = [string]$config.ApiBaseUrl
                SyncToken = [string]$config.SyncToken
                SqlServer = $effectiveSqlServer
                SqlUser = $effectiveSqlUser
                SqlPassword = $sqlPassword
                MktDatabase = $effectiveMktDatabase
                BranchCode = '28'
                PullLimit = $pullLimit
            }

            $output = & $legacySyncPath @syncParams 2>&1
            foreach ($line in $output) {
                $text = [string]$line
                if ([string]::IsNullOrWhiteSpace($text)) { continue }
                $safeText = $text -replace [regex]::Escape($sqlPassword), '********'
                Write-Log "SALIDA: $safeText"

                if ($safeText -match 'Registros recibidos:\s*(\d+)') { $received = [int]$matches[1] }
                if ($safeText -match 'Registros recientes encontrados:\s*(\d+)') { $received = [int]$matches[1] }
                if ($safeText -match 'Folio .+ verificado en dejadas/gafete') { $inserted++ }
                if ($safeText -match 'Revision extra de espejos locales omitida') { $lastHttp = 200 }
            }

            $lastHttp = 200
            Write-Log "Plaza 28 sync terminado"
        }
        catch {
            $lastError = $_.Exception.Message
            Write-Log "ERROR: $lastError"
        }
        finally {
            $stopwatch.Stop()
            Write-Status -Configured:$true -PasswordAvailable:$true -IntervalSeconds $intervalSeconds -LastHttp $lastHttp -LastReceivedCount $received -LastInsertedCount $inserted -LastOmittedCount $omitted -LastError $lastError -LastDurationMs $stopwatch.ElapsedMilliseconds -LockActive:$false
        }

        Start-Sleep -Seconds $intervalSeconds
    }
}
finally {
    Remove-Item -Path $lockPath -Force -ErrorAction SilentlyContinue
    Write-Log "Sincronizador Plaza 28 automatico detenido."
}
