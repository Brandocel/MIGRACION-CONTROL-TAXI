# Crea (o actualiza) un usuario del escritorio Control Taxi en dbo.ControlTaxiUsuarios.
#
# Los usuarios viven en el SQL Server de cada sucursal, no en el programa: un usuario de Casco
# Viejo no existe en Plaza 28 y al reves. Por eso el script pide el servidor y la base.
#
# La contrasena NUNCA se escribe en la linea de comandos ni se guarda en un archivo: se teclea
# oculta y solo viaja el hash PBKDF2 (SHA256, 100,000 vueltas, sal de 16 bytes), que es el mismo
# formato que valida el programa.
#
# Ejemplos:
#   .\crear-usuario-controltaxi.ps1 -Servidor ".\SQLEXPRESS" -BaseDatos mkt -Sucursal CV -Integrada
#   .\crear-usuario-controltaxi.ps1 -Servidor "192.168.1.70,50807" -BaseDatos mkt -Sucursal CV

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Servidor,
    [string]$BaseDatos = 'mkt',
    [Parameter(Mandatory = $true)][ValidateSet('CV', 'P28')][string]$Sucursal,
    [string]$Usuario,
    [string]$NombreCompleto,
    [ValidateSet('Administrador', 'Usuario')][string]$Rol = 'Administrador',
    [string]$UsuarioSql = 'sa',
    [switch]$Integrada,
    [switch]$SinAutorizarComision
)

$ErrorActionPreference = 'Stop'

function Nuevo-HashControlTaxi([string]$contrasena) {
    # Mismo formato que LocalUserRepository.HashPassword del programa:
    # PBKDF2$vueltas$sal$hash, con SHA256, 100,000 vueltas, sal de 16 bytes y llave de 32.
    #
    # Se arma con las clases viejas de .NET a proposito: Windows PowerShell 5.1 (el que trae
    # Windows) no tiene RandomNumberGenerator::Fill ni Rfc2898DeriveBytes::Pbkdf2, que son de
    # .NET moderno, y el script tronaba con "no contiene ningun metodo llamado Fill".
    $vueltas = 100000
    $sal = New-Object byte[] 16
    $generador = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    try { $generador.GetBytes($sal) } finally { $generador.Dispose() }

    $derivador = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
        $contrasena, $sal, $vueltas, [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    try { $derivada = $derivador.GetBytes(32) } finally { $derivador.Dispose() }

    return 'PBKDF2$' + $vueltas + '$' + [Convert]::ToBase64String($sal) + '$' + [Convert]::ToBase64String($derivada)
}

function Leer-Oculto([string]$mensaje) {
    $seguro = Read-Host -Prompt $mensaje -AsSecureString
    $puntero = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($seguro)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringUni($puntero) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($puntero) }
}

if ([string]::IsNullOrWhiteSpace($Usuario)) { $Usuario = Read-Host 'Nombre de usuario para entrar al programa' }
if ([string]::IsNullOrWhiteSpace($Usuario)) { throw 'El nombre de usuario es obligatorio.' }
if ([string]::IsNullOrWhiteSpace($NombreCompleto)) { $NombreCompleto = $Usuario }

$clave = Leer-Oculto "Contrasena para $Usuario"
if ([string]::IsNullOrWhiteSpace($clave)) { throw 'La contrasena no puede ir vacia.' }
$confirma = Leer-Oculto 'Escribela otra vez'
if ($clave -cne $confirma) { throw 'Las contrasenas no coinciden.' }

# Mismo hash que genera el programa (LocalUserRepository.HashPassword).
$hash = Nuevo-HashControlTaxi $clave
$clave = $null
$confirma = $null

$cadena = "Server=$Servidor;Database=$BaseDatos;TrustServerCertificate=True;Encrypt=False;Connect Timeout=15;"
if ($Integrada) {
    $cadena += 'Integrated Security=True;'
} else {
    $claveSql = Leer-Oculto "Contrasena de SQL Server para el usuario $UsuarioSql"
    $cadena += "User Id=$UsuarioSql;Password=$claveSql;"
    $claveSql = $null
}

Add-Type -AssemblyName System.Data | Out-Null
$conexion = New-Object System.Data.SqlClient.SqlConnection $cadena
$conexion.Open()
try {
    $autoriza = if ($SinAutorizarComision) { 0 } else { 1 }
    $sql = @"
IF OBJECT_ID(N'dbo.ControlTaxiUsuarios', N'U') IS NULL
    THROW 50001, 'Esta base no tiene la tabla dbo.ControlTaxiUsuarios.', 1;

IF EXISTS (SELECT 1 FROM dbo.ControlTaxiUsuarios WHERE UPPER(Username) = UPPER(@usuario))
    UPDATE dbo.ControlTaxiUsuarios
       SET PasswordHash = @hash, NombreCompleto = @nombre, Rol = @rol, BranchCode = @sucursal,
           Activo = 1, PuedeVerInicio = 1, PuedeVerRelaciones = 1, PuedeVerGafetes = 1,
           PuedeVerReportes = 1, PuedeVerVentas = 1, PuedePagarDejadas = 1, PuedeVerComisiones = 1,
           PuedeAutorizarComision = @autoriza, FechaActualizacion = SYSUTCDATETIME()
     WHERE UPPER(Username) = UPPER(@usuario);
ELSE
    INSERT INTO dbo.ControlTaxiUsuarios
        (Username, PasswordHash, NombreCompleto, Rol, BranchCode, Activo,
         PuedeVerInicio, PuedeVerRelaciones, PuedeVerGafetes, PuedeVerReportes,
         PuedeVerVentas, PuedePagarDejadas, PuedeVerComisiones, PuedeAutorizarComision,
         FechaCreacion, FechaActualizacion)
    VALUES (@usuario, @hash, @nombre, @rol, @sucursal, 1, 1, 1, 1, 1, 1, 1, 1, @autoriza,
            SYSUTCDATETIME(), SYSUTCDATETIME());
"@
    $comando = $conexion.CreateCommand()
    $comando.CommandText = $sql
    [void]$comando.Parameters.AddWithValue('@usuario', $Usuario)
    [void]$comando.Parameters.AddWithValue('@hash', $hash)
    [void]$comando.Parameters.AddWithValue('@nombre', $NombreCompleto)
    [void]$comando.Parameters.AddWithValue('@rol', $Rol)
    [void]$comando.Parameters.AddWithValue('@sucursal', $Sucursal)
    [void]$comando.Parameters.AddWithValue('@autoriza', $autoriza)
    [void]$comando.ExecuteNonQuery()

    $lectura = $conexion.CreateCommand()
    $lectura.CommandText = "SELECT Username, NombreCompleto, Rol, BranchCode, Activo, PuedeAutorizarComision FROM dbo.ControlTaxiUsuarios WHERE UPPER(Username) = UPPER(@usuario);"
    [void]$lectura.Parameters.AddWithValue('@usuario', $Usuario)
    $lector = $lectura.ExecuteReader()
    while ($lector.Read()) {
        Write-Host ''
        Write-Host 'USUARIO LISTO' -ForegroundColor Green
        Write-Host ("  Usuario:   {0}" -f $lector['Username'])
        Write-Host ("  Nombre:    {0}" -f $lector['NombreCompleto'])
        Write-Host ("  Rol:       {0}" -f $lector['Rol'])
        Write-Host ("  Sucursal:  {0}" -f $lector['BranchCode'])
        Write-Host ("  Activo:    {0}" -f $lector['Activo'])
        Write-Host ("  Autoriza comisiones: {0}" -f $lector['PuedeAutorizarComision'])
    }
    $lector.Close()
    Write-Host ''
    Write-Host 'Entra al programa con ese usuario y la contrasena que acabas de escribir.' -ForegroundColor Cyan
}
finally {
    $conexion.Close()
}
