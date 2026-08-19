# =============================================================================
#  CONFIGURAR LA CONEXION AL SQL SERVER DE ESTE EQUIPO
# =============================================================================
#  Crea la credencial cifrada que el Desktop necesita para iniciar sesion,
#  apuntando al SQL Server instalado en esta maquina en lugar del servidor
#  de produccion de Plaza 28.
#
#  La contrasena se pide en pantalla, no se guarda en ningun archivo de texto
#  y no aparece en el historial. Queda cifrada con DPAPI y solo se puede leer
#  desde este mismo equipo.
#
#  Se ejecuta una sola vez. Despues basta con ABRIR-PARA-REVISAR.cmd.
# =============================================================================

[CmdletBinding()]
param(
    [string]$SqlServer = ".\SQLEXPRESS",
    [string]$SqlUser   = "sa",
    [string]$Database  = "mkt",
    [string]$BranchCode = "28"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$root      = $PSScriptRoot
$configDir = Join-Path $root 'Config'
$target    = Join-Path $configDir 'plaza28.credentials.dat'

function Escribir($t, $c) { Write-Host $t -ForegroundColor $c }

Escribir "" 'White'
Escribir "  CONFIGURAR SQL SERVER LOCAL" 'Cyan'
Escribir "  ---------------------------" 'Cyan'
Escribir "  Servidor : $SqlServer" 'Gray'
Escribir "  Usuario  : $SqlUser" 'Gray'
Escribir "  Base     : $Database" 'Gray'
Escribir "" 'White'

# --- 1. Pedir la contrasena sin mostrarla -----------------------------------
$secure = Read-Host "  Contrasena de '$SqlUser' en $SqlServer" -AsSecureString
$bstr   = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try   { $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }

if ([string]::IsNullOrWhiteSpace($password)) {
    Escribir "  No escribiste nada. Cancelado." 'Red'
    exit 1
}

# --- 2. Probar que realmente conecta ----------------------------------------
Escribir "" 'White'
Escribir "  Probando la conexion..." 'Yellow'

$builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$builder['Data Source']              = $SqlServer
$builder['Initial Catalog']          = $Database
$builder['User ID']                  = $SqlUser
$builder['Password']                 = $password
$builder['Encrypt']                  = $false
$builder['TrustServerCertificate']   = $true
$builder['Connect Timeout']          = 8

try {
    $cn = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM dbo.ControlTaxiUsuarios;"
    $cuantos = $cmd.ExecuteScalar()
    $cn.Close()
    Escribir "  Conexion correcta. Usuarios que puede ver el login: $cuantos" 'Green'
}
catch {
    Escribir "" 'White'
    Escribir "  NO CONECTO:" 'Red'
    Escribir "  $($_.Exception.Message)" 'Red'
    Escribir "" 'White'
    Escribir "  Revisa que la contrasena sea la correcta y que el servicio" 'Yellow'
    Escribir "  SQL Server (SQLEXPRESS) este corriendo." 'Yellow'
    exit 1
}

# --- 3. Guardar cifrada donde la aplicacion la busca -------------------------
if (-not (Test-Path $configDir)) { New-Item -ItemType Directory -Path $configDir | Out-Null }

$payload = [ordered]@{
    SqlServer   = $SqlServer
    SqlUser     = $SqlUser
    SqlPassword = $password
    Database    = $Database
    BranchCode  = $BranchCode
} | ConvertTo-Json -Compress

$entropy   = [Text.Encoding]::UTF8.GetBytes("ControlTaxiDesktop.Plaza28.Credentials.v1")
$encrypted = [Security.Cryptography.ProtectedData]::Protect(
    [Text.Encoding]::UTF8.GetBytes($payload),
    $entropy,
    [Security.Cryptography.DataProtectionScope]::LocalMachine)

[IO.File]::WriteAllBytes($target, $encrypted)
$password = $null

Escribir "" 'White'
Escribir "  Credencial guardada y cifrada en:" 'Green'
Escribir "  Config\plaza28.credentials.dat" 'Gray'
Escribir "" 'White'
Escribir "  Listo. Ya puedes abrir con ABRIR-PARA-REVISAR.cmd" 'Cyan'
Escribir "  y entrar con tu usuario y sucursal Plaza 28." 'Cyan'
Escribir "" 'White'
