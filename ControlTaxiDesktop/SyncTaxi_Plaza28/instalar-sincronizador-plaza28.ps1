[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'

$taskName = 'ControlTaxi Plaza28 Sync'
$legacyTaskName = 'SyncTaxiHostingerServicio'
$scriptPath = $MyInvocation.MyCommand.Path
$syncFolder = Split-Path -Parent $scriptPath
$configPath = Join-Path $syncFolder 'sync.plaza28.config.json'
$launcherPath = Join-Path $syncFolder 'iniciar-sincronizador-plaza28-oculto.vbs'
$loopPath = Join-Path $syncFolder 'sync-plaza28-loop.ps1'

function Resolve-WorkspaceRoot {
    $candidates = @(
        (Split-Path -Parent $syncFolder),
        (Split-Path -Parent (Split-Path -Parent $syncFolder)),
        $syncFolder
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if ((Test-Path (Join-Path $candidate 'Config')) -or
            (Test-Path (Join-Path $candidate 'SyncTaxi_Plaza28\sync.plaza28.config.json')) -or
            (Test-Path (Join-Path $candidate 'SyncTaxi\sync.plaza28.config.json'))) {
            return $candidate
        }
    }

    return (Split-Path -Parent $syncFolder)
}

$workspaceRoot = Resolve-WorkspaceRoot
$logRoot = Join-Path $workspaceRoot 'Logs\Plaza28Sync'
$logPath = Join-Path $logRoot 'plaza28-auto-sync.log'

function Write-SetupLog {
    param([string]$Level, [string]$Message)

    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $line = "[{0}] level={1} stage=task-setup {2}" -f ([DateTimeOffset]::Now.ToString("o")), $Level, $Message
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

function Get-TaskSafe {
    param([string]$Name)
    try {
        return Get-ScheduledTask -TaskName $Name -ErrorAction Stop
    }
    catch {
        return $null
    }
}

function Test-Plaza28Config {
    if (-not (Test-Path $configPath)) {
        throw "No se encontro $configPath"
    }

    $config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
    if ([string]$config.BranchCode -ne '28') {
        throw "Configuracion invalida: BranchCode debe ser 28 y hoy vale '$($config.BranchCode)'."
    }

    if ([string]::IsNullOrWhiteSpace($config.ApiBaseUrl) -or $config.ApiBaseUrl -match '/casco-api/?$') {
        throw "Configuracion invalida: ApiBaseUrl de Plaza 28 no debe apuntar a /casco-api."
    }

    if ([string]::IsNullOrWhiteSpace($config.MktDatabase)) {
        throw "Configuracion invalida: MktDatabase no puede estar vacia."
    }

    return $config
}

if ($Status) {
    $task = Get-TaskSafe $taskName
    if (-not $task) {
        Write-Host "La tarea '$taskName' no esta instalada."
        exit 0
    }

    $taskInfo = Get-ScheduledTaskInfo -TaskName $taskName
    Write-Host "TaskName: $taskName"
    Write-Host "State: $($task.State)"
    Write-Host "LastRunTime: $($taskInfo.LastRunTime)"
    Write-Host "LastTaskResult: $($taskInfo.LastTaskResult)"
    Write-Host "NextRunTime: $($taskInfo.NextRunTime)"
    Write-Host "Loop: $loopPath"
    Write-Host "Log: $logPath"
    exit 0
}

if ($Uninstall) {
    foreach ($name in @($taskName, $legacyTaskName)) {
        $task = Get-TaskSafe $name
        if ($task) {
            Write-SetupLog -Level 'INFO' -Message "Desinstalando tarea programada '$name'."
            Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue | Out-Null
            Unregister-ScheduledTask -TaskName $name -Confirm:$false
            Write-Host "Tarea '$name' desinstalada."
        }
    }

    exit 0
}

$null = Test-Plaza28Config
if (-not (Test-Path $launcherPath)) {
    throw "No se encontro el lanzador oculto: $launcherPath"
}
if (-not (Test-Path $loopPath)) {
    throw "No se encontro el loop automatico Plaza 28: $loopPath"
}

Write-SetupLog -Level 'INFO' -Message "Validacion previa correcta. BranchCode=28, ApiBaseUrl Plaza 28 confirmada."

foreach ($name in @($taskName, $legacyTaskName)) {
    $existingTask = Get-TaskSafe $name
    if ($existingTask) {
        Write-SetupLog -Level 'INFO' -Message "Quitando tarea existente '$name' para evitar duplicados."
        Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue | Out-Null
        Unregister-ScheduledTask -TaskName $name -Confirm:$false
    }
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$loopPath`""
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Days 3650)
$description = 'Sincronizador oculto y automatico exclusivo de Plaza 28.'
$registeredMode = $null

try {
    $startupTrigger = New-ScheduledTaskTrigger -AtStartup
    # Ademas del arranque: cada 5 minutos se intenta levantar. Si el proceso se
    # murio (cierre de sesion, reinicio de PowerShell, corte de energia), vuelve
    # solo. Si ya esta corriendo, el candado del loop y MultipleInstances
    # IgnoreNew impiden que haya dos.
    $repeatTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(2) `
        -RepetitionInterval (New-TimeSpan -Minutes 5) `
        -RepetitionDuration (New-TimeSpan -Days 3650)
    $systemPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

    Register-ScheduledTask `
        -TaskName $taskName `
        -Description $description `
        -Action $action `
        -Trigger @($startupTrigger, $repeatTrigger) `
        -Settings $settings `
        -Principal $systemPrincipal | Out-Null

    $registeredMode = 'SYSTEM / AtStartup'
}
catch {
    Write-SetupLog -Level 'WARN' -Message "No fue posible registrar con SYSTEM. Se intentara usuario actual. Error=$($_.Exception.Message)"

    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
    $repeatTrigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(2) `
        -RepetitionInterval (New-TimeSpan -Minutes 5) `
        -RepetitionDuration (New-TimeSpan -Days 3650)
    $userPrincipal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest

    Register-ScheduledTask `
        -TaskName $taskName `
        -Description ($description + ' Modo fallback: usuario actual al iniciar sesion.') `
        -Action $action `
        -Trigger @($logonTrigger, $repeatTrigger) `
        -Settings $settings `
        -Principal $userPrincipal | Out-Null

    $registeredMode = "Usuario actual / AtLogOn ($currentUser)"
}

Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 2

$task = Get-ScheduledTask -TaskName $taskName
$taskInfo = Get-ScheduledTaskInfo -TaskName $taskName

Write-SetupLog -Level 'INFO' -Message "Tarea instalada y lanzada. Mode=$registeredMode State=$($task.State) LastTaskResult=$($taskInfo.LastTaskResult)"

Write-Host "Tarea instalada: $taskName"
Write-Host "Loop: $loopPath"
Write-Host "Modo de registro: $registeredMode"
Write-Host "Log: $logPath"
Write-Host "Estado actual: $($task.State)"
Write-Host "Ultimo resultado: $($taskInfo.LastTaskResult)"
