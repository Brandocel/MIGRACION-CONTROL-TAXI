<#
    Diagnostico de solo lectura del sincronizador de Plaza 28.

    Responde una pregunta concreta: de los registros que la app movil ya subio a
    Hostinger, cuales NO estan en SQL Server local. Ademas muestra si el
    sincronizador esta vivo y que fue lo ultimo que hizo.

    No escribe en Hostinger ni en SQL Server. Solo consulta.

    Uso:
        powershell -NoProfile -ExecutionPolicy Bypass -File .\revisar-sincronizador-plaza28.ps1
        powershell -NoProfile -ExecutionPolicy Bypass -File .\revisar-sincronizador-plaza28.ps1 -Dias 3
        powershell -NoProfile -ExecutionPolicy Bypass -File .\revisar-sincronizador-plaza28.ps1 -Fecha 2026-09-01
#>
[CmdletBinding()]
param(
    [int]$Dias = 1,
    [string]$Fecha = ''
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$configPath = Join-Path $scriptDir 'sync.plaza28.config.json'

function Resolve-WorkspaceRoot {
    $candidates = @(
        (Split-Path -Parent $scriptDir),
        (Split-Path -Parent (Split-Path -Parent $scriptDir)),
        $scriptDir
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if ((Test-Path (Join-Path $candidate 'Config')) -or
            (Test-Path (Join-Path $candidate 'SyncTaxi_Plaza28\sync.plaza28.config.json'))) {
            return $candidate
        }
    }

    return (Split-Path -Parent $scriptDir)
}

function Write-Titulo([string]$Texto) {
    Write-Host ''
    Write-Host ('=' * 70) -ForegroundColor DarkGray
    Write-Host $Texto -ForegroundColor Cyan
    Write-Host ('=' * 70) -ForegroundColor DarkGray
}

$workspaceRoot = Resolve-WorkspaceRoot
$logRoot = Join-Path $workspaceRoot 'Logs\Plaza28Sync'

if (-not (Test-Path $configPath)) {
    Write-Host "FALTA sync.plaza28.config.json en $scriptDir" -ForegroundColor Red
    exit 1
}

$config = Get-Content -Path $configPath -Raw | ConvertFrom-Json

# --------------------------------------------------------------- credencial
$credential = $null
$credentialPath = Join-Path $workspaceRoot 'Config\plaza28.credentials.dat'
if (Test-Path $credentialPath) {
    try {
        Add-Type -AssemblyName System.Security
        $encrypted = [System.IO.File]::ReadAllBytes($credentialPath)
        $entropy = [System.Text.Encoding]::UTF8.GetBytes("ControlTaxiDesktop.Plaza28.Credentials.v1")
        $jsonBytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
            $encrypted, $entropy, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
        $credential = [System.Text.Encoding]::UTF8.GetString($jsonBytes) | ConvertFrom-Json
    } catch {
        Write-Host "No se pudo descifrar Config\plaza28.credentials.dat: $($_.Exception.Message)" -ForegroundColor Red
    }
}

$sqlServer = if ($credential -and $credential.SqlServer) { [string]$credential.SqlServer } else { [string]$config.SqlServer }
$sqlUser = if ($credential -and $credential.SqlUser) { [string]$credential.SqlUser } else { [string]$config.SqlUser }
$mktDatabase = if ($credential -and $credential.Database) { [string]$credential.Database } else { [string]$config.MktDatabase }
$sqlPassword = $null
if ($credential -and -not [string]::IsNullOrWhiteSpace($credential.SqlPassword)) {
    $sqlPassword = [string]$credential.SqlPassword
} elseif (-not [string]::IsNullOrWhiteSpace($env:PLAZA28_SQL_PASSWORD)) {
    $sqlPassword = $env:PLAZA28_SQL_PASSWORD
}

# ------------------------------------------------------------------ rangos
if (-not [string]::IsNullOrWhiteSpace($Fecha)) {
    $desde = [datetime]::ParseExact($Fecha, 'yyyy-MM-dd', $null)
    $hasta = $desde
} else {
    $hasta = (Get-Date).Date
    $desde = $hasta.AddDays(-1 * [Math]::Max(0, $Dias - 1))
}
$desdeTexto = $desde.ToString('yyyy-MM-dd')
$hastaTexto = $hasta.ToString('yyyy-MM-dd')

Write-Titulo "SINCRONIZADOR PLAZA 28 - diagnostico $desdeTexto a $hastaTexto"
Write-Host "Maquina    : $env:COMPUTERNAME"
Write-Host "Carpeta    : $workspaceRoot"
Write-Host "SQL Server : $sqlServer / base $mktDatabase / usuario $sqlUser"
Write-Host "Hostinger  : $($config.ApiBaseUrl)"
if ($sqlPassword) {
    Write-Host "Credencial : disponible" -ForegroundColor Green
} else {
    Write-Host "Credencial : NO DISPONIBLE" -ForegroundColor Red
}

# ------------------------------------------------------ estado del proceso
Write-Titulo '1. ESTADO DEL SINCRONIZADOR'

$tarea = Get-ScheduledTask -TaskName 'ControlTaxi Plaza28 Sync' -ErrorAction SilentlyContinue
if ($tarea) {
    $info = Get-ScheduledTaskInfo -TaskName $tarea.TaskName
    Write-Host "Tarea programada : $($tarea.TaskName) [$($tarea.State)]"
    Write-Host "Ultima corrida   : $($info.LastRunTime)  resultado $($info.LastTaskResult)"
} else {
    Write-Host 'Tarea programada : NO EXISTE. El sincronizador no arranca solo.' -ForegroundColor Red
}

$procesos = @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -match 'sync-plaza28-loop' })
if ($procesos.Count -gt 0) {
    foreach ($p in $procesos) {
        Write-Host "Proceso vivo     : PID $($p.ProcessId) desde $($p.CreationDate)" -ForegroundColor Green
    }
} else {
    Write-Host 'Proceso vivo     : NINGUNO. El loop no esta corriendo.' -ForegroundColor Red
}

$statusPath = Join-Path $logRoot 'plaza28-auto-sync-status.json'
if (Test-Path $statusPath) {
    $status = Get-Content -Path $statusPath -Raw | ConvertFrom-Json
    $ultimo = [datetime]$status.LastCycleAt
    $minutos = [int]((Get-Date) - $ultimo).TotalMinutes
    if ($minutos -le 10) {
        Write-Host "Ultimo ciclo     : $ultimo (hace $minutos min)" -ForegroundColor Green
    } else {
        Write-Host "Ultimo ciclo     : $ultimo (hace $minutos min)" -ForegroundColor Red
    }
    Write-Host "Duracion         : $([int]($status.LastDurationMs / 1000)) s   recibidos $($status.LastReceivedCount)   guardados $($status.LastInsertedCount)"
    if (-not [string]::IsNullOrWhiteSpace([string]$status.LastError)) {
        Write-Host "Ultimo error     : $($status.LastError)" -ForegroundColor Red
    }
} else {
    Write-Host 'Archivo de estado: NO EXISTE (el loop nunca completo un ciclo aqui).' -ForegroundColor Red
}

# -------------------------------------------------------------- Hostinger
Write-Titulo '2. HOSTINGER (lo que la app movil ya subio)'

$headers = @{ 'X-Sync-Token' = [string]$config.SyncToken }
$uriRegistros = "$($config.ApiBaseUrl)/api/taxis/registros?dateFrom=$desdeTexto&dateTo=$hastaTexto&branchCode=$($config.BranchCode)"
$remotos = @()
try {
    $remotos = @(Invoke-RestMethod -Method Get -Uri $uriRegistros -Headers $headers -TimeoutSec 120)
    # Invoke-RestMethod a veces entrega el arreglo JSON como un solo objeto, y
    # entonces @() lo envuelve en vez de expandirlo: quedaria 1 en vez de 28.
    if ($remotos.Count -eq 1 -and $remotos[0] -is [array]) { $remotos = @($remotos[0]) }
    Write-Host "Registros en Hostinger : $($remotos.Count)"
} catch {
    Write-Host "ERROR consultando Hostinger: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Sin conexion a Hostinger no hay nada que bajar. Revisa internet y firewall.' -ForegroundColor Red
    exit 2
}

try {
    $uriPendientes = "$($config.ApiBaseUrl)/sync/pull-changes?limit=1000&branchCode=$($config.BranchCode)"
    $pendientes = Invoke-RestMethod -Method Get -Uri $uriPendientes -Headers $headers -TimeoutSec 120
    $pendCount = @($pendientes.changes.mkt2_trip_records).Count
    Write-Host "Pendientes sin marcar  : $pendCount"
    if ($pendCount -ge 500) {
        Write-Host 'ATENCION: la cola de pendientes llego al limite y puede estar tapando registros nuevos.' -ForegroundColor Yellow
    }
} catch {
    Write-Host "No se pudo leer /sync/pull-changes: $($_.Exception.Message)" -ForegroundColor Yellow
}

# ------------------------------------------------------------ SQL Server
Write-Titulo '3. SQL SERVER LOCAL (lo que el escritorio realmente ve)'

if (-not $sqlPassword) {
    Write-Host 'Sin credencial SQL no se puede comparar. Corre 1-CONFIGURAR-CREDENCIAL-PLAZA28.cmd' -ForegroundColor Red
    exit 3
}

$connectionString = "Server=$sqlServer;Database=$mktDatabase;User Id=$sqlUser;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=False;Connect Timeout=30"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
try {
    $connection.Open()
} catch {
    Write-Host "ERROR conectando a SQL Server: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Si esto falla, el sincronizador tampoco puede guardar nada.' -ForegroundColor Red
    exit 4
}

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.CommandText = @"
SELECT LTRIM(RTRIM(COALESCE(folio_app_original, folio_app))) AS Folio
FROM dbo.AppMovilRegistro
WHERE fecha_operacion >= @desde
  AND fecha_operacion < DATEADD(DAY, 1, @hasta);
"@
    $pDesde = $command.CreateParameter(); $pDesde.ParameterName = '@desde'; $pDesde.Value = $desde; [void]$command.Parameters.Add($pDesde)
    $pHasta = $command.CreateParameter(); $pHasta.ParameterName = '@hasta'; $pHasta.Value = $hasta; [void]$command.Parameters.Add($pHasta)

    $tabla = New-Object System.Data.DataTable
    $adaptador = New-Object System.Data.SqlClient.SqlDataAdapter $command
    [void]$adaptador.Fill($tabla)

    $locales = @{}
    foreach ($fila in $tabla.Rows) {
        $folio = [string]$fila['Folio']
        if (-not [string]::IsNullOrWhiteSpace($folio)) { $locales[$folio] = $true }
    }

    Write-Host "Registros en SQL Server: $($locales.Count)"

    $faltantes = @()
    foreach ($remoto in $remotos) {
        $folio = [string]$remoto.recordId
        if (-not $locales.ContainsKey($folio)) { $faltantes += $remoto }
    }

    Write-Host ''
    if ($faltantes.Count -eq 0) {
        Write-Host "TODO BAJO: los $($remotos.Count) registros de Hostinger estan en SQL Server." -ForegroundColor Green
    } else {
        Write-Host "FALTAN $($faltantes.Count) de $($remotos.Count) registros en SQL Server:" -ForegroundColor Red
        foreach ($f in ($faltantes | Sort-Object { [string]$_.recordDate })) {
            $cuando = [string]$f.recordDate
            if ($cuando.Length -gt 16) { $cuando = $cuando.Substring(0, 16) }
            Write-Host ("  folio {0,-8} {1}  {2,-25} {3}" -f $f.recordId, $cuando, $f.driverName, $f.serviceType)
        }

        $bloqueados = $connection.CreateCommand()
        $bloqueados.CommandText = @"
IF OBJECT_ID('dbo.AppMovilFoliosBloqueados', 'U') IS NULL
    SELECT CONVERT(NVARCHAR(60), '') AS Folio WHERE 1 = 0;
ELSE
    SELECT LTRIM(RTRIM(CONVERT(NVARCHAR(60), Folio))) AS Folio FROM dbo.AppMovilFoliosBloqueados;
"@
        $tablaBloqueados = New-Object System.Data.DataTable
        $adaptadorBloqueados = New-Object System.Data.SqlClient.SqlDataAdapter $bloqueados
        [void]$adaptadorBloqueados.Fill($tablaBloqueados)
        $setBloqueados = @{}
        foreach ($fila in $tablaBloqueados.Rows) { $setBloqueados[[string]$fila['Folio']] = $true }

        $conBloqueo = @($faltantes | Where-Object { $setBloqueados.ContainsKey([string]$_.recordId) })
        if ($conBloqueo.Count -gt 0) {
            Write-Host ''
            Write-Host "De esos, $($conBloqueo.Count) estan en dbo.AppMovilFoliosBloqueados y el sync los omite a proposito:" -ForegroundColor Yellow
            Write-Host ('  ' + (($conBloqueo | ForEach-Object { $_.recordId }) -join ', '))
        }
    }

    $sinDejada = $connection.CreateCommand()
    $sinDejada.CommandTimeout = 120
    $sinDejada.CommandText = @"
SELECT COUNT(1)
FROM dbo.AppMovilRegistro a
WHERE a.fecha_operacion >= @desde
  AND a.fecha_operacion < DATEADD(DAY, 1, @hasta)
  AND NOT EXISTS (
      SELECT 1 FROM dbo.dejadas d
      WHERE d.folioregistrostr = a.folio_app
        AND d.fecha >= CONVERT(DATETIME, CONVERT(DATE, a.fecha_operacion))
        AND d.fecha < DATEADD(DAY, 1, CONVERT(DATETIME, CONVERT(DATE, a.fecha_operacion))));
"@
    $p1 = $sinDejada.CreateParameter(); $p1.ParameterName = '@desde'; $p1.Value = $desde; [void]$sinDejada.Parameters.Add($p1)
    $p2 = $sinDejada.CreateParameter(); $p2.ParameterName = '@hasta'; $p2.Value = $hasta; [void]$sinDejada.Parameters.Add($p2)
    $cuantosSinDejada = [int]$sinDejada.ExecuteScalar()
    if ($cuantosSinDejada -gt 0) {
        Write-Host ''
        Write-Host "$cuantosSinDejada registros bajaron pero NO tienen dejada. El sync los reintenta cada ciclo y nunca los marca." -ForegroundColor Yellow
    }
} finally {
    $connection.Close()
}

# -------------------------------------------------------------------- log
Write-Titulo '4. ULTIMAS LINEAS DEL LOG'

$logPath = Join-Path $logRoot 'plaza28-auto-sync.log'
if (Test-Path $logPath) {
    Get-Content -Path $logPath -Tail 20 | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    Write-Host "Log completo: $logPath"
} else {
    Write-Host "No hay log en $logPath" -ForegroundColor Yellow
}

Write-Host ''
