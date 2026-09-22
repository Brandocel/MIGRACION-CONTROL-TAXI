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

# El cuadre se publica en Hoka Solutions cada 5 minutos, no en cada ciclo: el ciclo normal corre
# cada 15 segundos y armar el cuadre implica releer relaciones, comisiones, cortes y camiones del
# dia entero. Cada 5 minutos alcanza de sobra para que Hoka este al dia y no castiga a SQL Server.
$cuadrePushIntervalSeconds = Read-IntValue $config.CuadrePushIntervalSeconds 300
$cuadrePushTimeoutSeconds = Read-IntValue $config.CuadrePushTimeoutSeconds 180
$desktopExePath = Join-Path $workspaceRoot 'ControlTaxiDesktop.exe'

function Invoke-PushCuadre {
    <#
        Corre ControlTaxiDesktop.exe --push-cuadre, que calcula el cuadre del dia con las mismas
        reglas del Excel y lo sube a la API de Hostinger.

        Se llama al ejecutable y no se reimplementa el calculo aqui porque las reglas de comision
        (guias al 8 %, venta duplicada del mismo taxista, codigos cortos del POS) viven en C# y ya
        costaron varias correcciones. Traducirlas a PowerShell garantizaria que Hoka y el Excel
        terminaran dando numeros distintos.
    #>
    param(
        [string]$ExePath,
        [int]$TimeoutSeconds
    )

    if (-not (Test-Path $ExePath)) {
        Write-Log "CUADRE: no se encontro ControlTaxiDesktop.exe en $ExePath; se omite la publicacion."
        return $false
    }

    $proceso = Start-Process -FilePath $ExePath -ArgumentList '--push-cuadre' -PassThru -WindowStyle Hidden
    if (-not $proceso.WaitForExit($TimeoutSeconds * 1000)) {
        try { $proceso.Kill() } catch { }
        Write-Log "CUADRE: la publicacion paso de $TimeoutSeconds segundos y se corto."
        return $false
    }

    if ($proceso.ExitCode -eq 0) {
        Write-Log "CUADRE: publicado en Hoka."
        return $true
    }

    # El detalle del fallo lo escribe el propio ejecutable, para no repetir el mensaje en dos logs.
    Write-Log "CUADRE: la publicacion fallo (codigo $($proceso.ExitCode)). Ver Logs\push-cuadre.txt."
    return $false
}

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

# En el primer ciclo se publica de inmediato, para que al arrancar el sincronizador (o al
# reiniciar la maquina) Hoka quede al dia sin esperar los 5 minutos.
$ultimoCuadrePush = [DateTime]::MinValue

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

            # El ciclo corre en un job para poder cortarlo si se cuelga. Antes se
            # llamaba directo: si la red o SQL no respondian, el loop se quedaba
            # esperando para siempre y dejaba de bajar registros sin avisar.
            $job = Start-Job -ScriptBlock {
                param([string]$Ruta, [hashtable]$Parametros)
                & $Ruta @Parametros *>&1
            } -ArgumentList $legacySyncPath, $syncParams

            $output = @()
            if (Wait-Job -Job $job -Timeout $maxRunSeconds) {
                $output = @(Receive-Job -Job $job)
            } else {
                Stop-Job -Job $job -ErrorAction SilentlyContinue
                $output = @(Receive-Job -Job $job -ErrorAction SilentlyContinue)
                $lastError = "El ciclo paso de $maxRunSeconds segundos y se corto."
                Write-Log "TIMEOUT: $lastError"
            }
            Remove-Job -Job $job -Force -ErrorAction SilentlyContinue

            $fallas = New-Object System.Collections.Generic.List[string]
            foreach ($line in $output) {
                $text = [string]$line
                if ([string]::IsNullOrWhiteSpace($text)) { continue }
                $safeText = $text -replace [regex]::Escape($sqlPassword), '********'
                Write-Log "SALIDA: $safeText"

                if ($safeText -match 'Registros recibidos:\s*(\d+)') { $received = [int]$matches[1] }
                if ($safeText -match 'Registros recientes encontrados:\s*(\d+)') { $received = [int]$matches[1] }
                if ($safeText -match 'Folio .+ verificado en dejadas/gafete') { $inserted++ }
                if ($safeText -match 'Camiones guardados en SQL Server:\s*(\d+)') { $inserted += [int]$matches[1] }

                # Lo que el script hijo reporta como problema sin tronar: antes se
                # perdia en el log y el estado seguia diciendo que todo iba bien.
                if ($safeText -match 'NO quedo verificado|No se pudo|no se guardo|bloqueado; se omite|Error llamando API|No se marco nada') {
                    [void]$fallas.Add($safeText)
                }
            }

            if ($fallas.Count -gt 0) {
                $omitted = $fallas.Count
                if ([string]::IsNullOrWhiteSpace($lastError)) {
                    $lastError = "$($fallas.Count) avisos en el ciclo. Primero: $($fallas[0])"
                }
            }

            if ([string]::IsNullOrWhiteSpace($lastError)) { $lastHttp = 200 }
            Write-Log "Plaza 28 sync terminado"

            # La publicacion del cuadre va en su propio try: si Hostinger no contesta, el ciclo de
            # sincronizacion normal no debe marcarse como fallido por eso.
            if (((Get-Date) - $ultimoCuadrePush).TotalSeconds -ge $cuadrePushIntervalSeconds) {
                # El reloj se corre haya salido bien o mal: si Hostinger esta caido no tiene caso
                # reintentar cada 15 segundos, se espera al siguiente turno de 5 minutos.
                $ultimoCuadrePush = Get-Date
                try {
                    [void](Invoke-PushCuadre -ExePath $desktopExePath -TimeoutSeconds $cuadrePushTimeoutSeconds)
                }
                catch {
                    Write-Log "CUADRE ERROR: $($_.Exception.Message)"
                }
            }
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
