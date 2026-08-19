[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'

$taskName = 'ControlTaxi Casco Sync'
$scriptPath = $MyInvocation.MyCommand.Path
$syncFolder = Split-Path -Parent $scriptPath
$configPath = Join-Path $syncFolder 'sync.casco.config.json'
$launcherPath = Join-Path $syncFolder 'iniciar-sincronizador-casco-oculto.vbs'

function Resolve-WorkspaceRoot {
    $candidates = @(
        (Split-Path -Parent $syncFolder),
        (Split-Path -Parent (Split-Path -Parent $syncFolder))
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if ((Test-Path (Join-Path $candidate 'Tools\ControlTaxiDesktop.Tools.exe')) -or
            (Test-Path (Join-Path $candidate 'Desktop\ControlTaxiDesktop.exe')) -or
            (Test-Path (Join-Path $candidate 'SyncTaxi\sync.casco.config.json'))) {
            return $candidate
        }
    }

    return (Split-Path -Parent $syncFolder)
}

$workspaceRoot = Resolve-WorkspaceRoot
$logRoot = Join-Path $workspaceRoot 'Logs\CascoSync'
$logPath = Join-Path $logRoot 'casco-badge-sync.log'

function Write-SetupLog {
    param(
        [string]$Level,
        [string]$Message
    )

    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $line = "[{0}] level={1} stage=task-setup {2}" -f ([DateTimeOffset]::Now.ToString("o")), $Level, $Message
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

function Get-TaskSafe {
    try {
        return Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
    }
    catch {
        return $null
    }
}

function Resolve-ToolPath {
    $publishedExe = Join-Path $workspaceRoot 'Tools\ControlTaxiDesktop.Tools.exe'
    $publishedExeAlt = Join-Path $workspaceRoot 'Release-ControlTaxi-Casco-Produccion\Tools\ControlTaxiDesktop.Tools.exe'
    $releaseDll = Join-Path $workspaceRoot 'ControlTaxiDesktop.Tools\bin\Release\net9.0-windows\ControlTaxiDesktop.Tools.dll'
    $debugDll = Join-Path $workspaceRoot 'ControlTaxiDesktop.Tools\bin\Debug\net9.0-windows\ControlTaxiDesktop.Tools.dll'

    if (Test-Path $publishedExe) { return $publishedExe }
    if (Test-Path $publishedExeAlt) { return $publishedExeAlt }
    if (Test-Path $releaseDll) { return $releaseDll }
    if (Test-Path $debugDll) { return $debugDll }

    throw "No se encontro ControlTaxiDesktop.Tools publicado ni compilado. Verifica la carpeta Tools o compila primero ControlTaxiDesktop.Tools."
}

function Test-CascoConfig {
    if (-not (Test-Path $configPath)) {
        throw "No se encontro $configPath"
    }

    $config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
    if ($config.BranchCode -ne 'CV') {
        throw "Configuracion invalida: BranchCode debe ser CV y hoy vale '$($config.BranchCode)'."
    }

    if ($config.MktDatabase -ne 'mkt') {
        throw "Configuracion invalida: MktDatabase debe ser mkt y hoy vale '$($config.MktDatabase)'."
    }

    if ([string]::IsNullOrWhiteSpace($config.ApiBaseUrl) -or $config.ApiBaseUrl -notmatch '/casco-api/?$') {
        throw "Configuracion invalida: ApiBaseUrl debe contener /casco-api."
    }

    if ($config.BranchCode -eq '28') {
        throw 'Configuracion invalida: detectado posible flujo de Plaza 28.'
    }

    return $config
}

if ($Status) {
    $task = Get-TaskSafe
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
    Write-Host "Launcher: $launcherPath"
    Write-Host "Log: $logPath"
    exit 0
}

if ($Uninstall) {
    $task = Get-TaskSafe
    if ($task) {
        Write-SetupLog -Level 'INFO' -Message "Desinstalando tarea programada '$taskName'."
        Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Out-Null
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "Tarea '$taskName' desinstalada."
    }
    else {
        Write-Host "La tarea '$taskName' no existe."
    }

    exit 0
}

$null = Test-CascoConfig
$toolPath = Resolve-ToolPath

if (-not (Test-Path $launcherPath)) {
    throw "No se encontro el lanzador oculto: $launcherPath"
}

Write-SetupLog -Level 'INFO' -Message "Validacion previa correcta. BranchCode=CV, MktDatabase=mkt, ApiBaseUrl Casco confirmada. Tool=$toolPath"

$existingTask = Get-TaskSafe
if ($existingTask) {
    Write-SetupLog -Level 'INFO' -Message "Actualizando tarea existente '$taskName'."
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Out-Null
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
else {
    Write-SetupLog -Level 'INFO' -Message "Creando tarea programada '$taskName'."
}

$action = New-ScheduledTaskAction -Execute 'wscript.exe' -Argument "`"$launcherPath`""
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -MultipleInstances IgnoreNew `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit (New-TimeSpan -Days 3650)
$description = 'Sincronizador oculto y automatico exclusivo de gafetes Casco Viejo.'
$registeredMode = $null

try {
    $startupTrigger = New-ScheduledTaskTrigger -AtStartup
    $systemPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

    Register-ScheduledTask `
        -TaskName $taskName `
        -Description $description `
        -Action $action `
        -Trigger $startupTrigger `
        -Settings $settings `
        -Principal $systemPrincipal | Out-Null

    $registeredMode = 'SYSTEM / AtStartup'
}
catch {
    Write-SetupLog -Level 'WARN' -Message "No fue posible registrar la tarea con SYSTEM al iniciar Windows. Se intentara modo usuario actual. Error=$($_.Exception.Message)"

    $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $logonTrigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
    $userPrincipal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest

    Register-ScheduledTask `
        -TaskName $taskName `
        -Description ($description + ' Modo fallback: usuario actual al iniciar sesion.') `
        -Action $action `
        -Trigger $logonTrigger `
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
Write-Host "Launcher: $launcherPath"
Write-Host "Tool: $toolPath"
Write-Host "Modo de registro: $registeredMode"
Write-Host "Log: $logPath"
Write-Host "Estado actual: $($task.State)"
Write-Host "Ultimo resultado: $($taskInfo.LastTaskResult)"
