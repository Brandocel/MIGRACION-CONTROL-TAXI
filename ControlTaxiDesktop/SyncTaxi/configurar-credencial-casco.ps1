[CmdletBinding()]
param(
    [string]$SqlServer = "192.168.1.70,50807",
    [string]$SqlUser = "sa",
    [string]$Database = "mkt",
    [string]$BranchCode = "CV",
    [string]$SqlPassword
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$scriptPath = $MyInvocation.MyCommand.Path
$syncFolder = Split-Path -Parent $scriptPath

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

function ConvertFrom-SecureStringPlainText {
    param([securestring]$SecureString)

    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    }
    finally {
        if ($ptr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
        }
    }
}

if ($BranchCode -ne 'CV') {
    throw "Configuracion invalida: BranchCode debe ser CV."
}

if ($Database -ne 'mkt') {
    throw "Configuracion invalida: Database debe ser mkt para produccion Casco."
}

if ([string]::IsNullOrWhiteSpace($SqlPassword)) {
    $securePassword = Read-Host "Contrasena SQL de Casco" -AsSecureString
    $SqlPassword = ConvertFrom-SecureStringPlainText $securePassword
}

if ([string]::IsNullOrWhiteSpace($SqlPassword)) {
    throw "La contrasena SQL no puede estar vacia."
}

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source'] = $SqlServer
$builder['Initial Catalog'] = $Database
$builder['User ID'] = $SqlUser
$builder['Password'] = $SqlPassword
$builder['TrustServerCertificate'] = $true
$builder['Encrypt'] = $false
$builder['Connect Timeout'] = 5

$connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandText = "SELECT 1;"
    [void]$command.ExecuteScalar()
}
finally {
    $connection.Dispose()
}

$workspaceRoot = Resolve-WorkspaceRoot
$configDir = Join-Path $workspaceRoot 'Config'
New-Item -ItemType Directory -Force -Path $configDir | Out-Null
$credentialPath = Join-Path $configDir 'casco.credentials.dat'

$credential = [ordered]@{
    SqlServer = $SqlServer
    SqlUser = $SqlUser
    SqlPassword = $SqlPassword
    Database = $Database
    BranchCode = $BranchCode
}

$json = $credential | ConvertTo-Json -Compress
$jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($json)
$entropy = [System.Text.Encoding]::UTF8.GetBytes("ControlTaxiDesktop.Casco.Credentials.v1")
$encrypted = [System.Security.Cryptography.ProtectedData]::Protect(
    $jsonBytes,
    $entropy,
    [System.Security.Cryptography.DataProtectionScope]::LocalMachine)

[System.IO.File]::WriteAllBytes($credentialPath, $encrypted)

Write-Host "Credencial de Casco guardada cifrada correctamente."
Write-Host "Ruta: $credentialPath"
Write-Host "Servidor: $SqlServer"
Write-Host "Base: $Database"
Write-Host "Sucursal: $BranchCode"
