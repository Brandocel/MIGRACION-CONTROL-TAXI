<#
    Carga las reglas de comision de Casco Viejo en dbo.ControlTaxiComisiones.

    Fuente: CALCULO DE COMISIONES, PTO MORELOS.xlsx, hoja CASCO, confirmado con el negocio el
    19/09/2026. Reemplaza la carga de julio, que habia leido mal el Excel (puso la retencion del
    banco de 19 % como si fuera comision de agencia) y por eso se habia quedado apagada.

    Lo que hace:
      1. Muestra las reglas de Casco que hay hoy.
      2. Pide confirmacion escribiendo SI.
      3. Respalda la tabla completa en dbo.ControlTaxiComisiones_Respaldo_20260919.
      4. Apaga las reglas de Casco que esten activas.
      5. Da de alta las 14 reglas nuevas, activas.

    Si ya se habia aplicado, no toca nada.

    Para regresar como estaba:
        powershell -NoProfile -ExecutionPolicy Bypass -File .\aplicar-reglas-comisiones-casco.ps1 -Revertir

    Queda fuera a proposito (decidido el 19/09/2026):
      - comision de vendedor ("deportiva") y meta diaria;
      - tabulador automatico de degustacion;
      - FARMACIAS (pendiente): sin regla, sigue con el 10 % de respaldo de siempre.
#>
[CmdletBinding()]
param(
    [switch]$Revertir
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-CascoCredential {
    $candidatos = @(
        (Join-Path $scriptDir 'Config\casco.credentials.dat'),
        (Join-Path (Split-Path -Parent $scriptDir) 'Config\casco.credentials.dat'),
        (Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) 'Config\casco.credentials.dat')
    )
    foreach ($ruta in $candidatos) {
        if (Test-Path $ruta) { return $ruta }
    }
    return $null
}

$credPath = Find-CascoCredential
if (-not $credPath) {
    Write-Host 'No encontre Config\casco.credentials.dat. Este script solo se corre en la maquina de Casco Viejo.' -ForegroundColor Red
    exit 1
}

Add-Type -AssemblyName System.Security
$cr = [Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes($credPath),
    [Text.Encoding]::UTF8.GetBytes('ControlTaxiDesktop.Casco.Credentials.v1'),
    [Security.Cryptography.DataProtectionScope]::LocalMachine)) | ConvertFrom-Json

$cn = New-Object System.Data.SqlClient.SqlConnection "Server=$($cr.SqlServer);Database=$($cr.Database);User Id=$($cr.SqlUser);Password=$($cr.SqlPassword);TrustServerCertificate=True;Encrypt=False;Connect Timeout=15"
$cn.Open()

function Invoke-Sql([string]$sql) {
    $cmd = $cn.CreateCommand()
    $cmd.CommandTimeout = 120
    $cmd.CommandText = $sql
    return $cmd.ExecuteScalar()
}

function Show-Rules {
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = @'
SELECT Id, Proveedor, ConTarjeta, Activo,
       COALESCE(CONVERT(nvarchar(20), ComisionAgencia), '-') AS Agencia,
       COALESCE(CONVERT(nvarchar(20), ComisionTaxista), '-') AS Taxista,
       COALESCE(OrigenExcel, '') AS Origen
FROM dbo.ControlTaxiComisiones
WHERE BranchCode = N'CV'
ORDER BY Activo DESC, Proveedor, ConTarjeta DESC;
'@
    $rd = $cmd.ExecuteReader()
    $n = 0
    while ($rd.Read()) {
        $n++
        $estado = if ($rd['Activo']) { 'ACTIVA ' } else { 'apagada' }
        $tarjeta = if ($rd['ConTarjeta']) { 'tarjeta ' } else { 'efectivo' }
        '  {0,3} {1} {2,-24} {3}  agencia={4,-7} taxi/guia={5,-7} {6}' -f $rd['Id'], $estado, $rd['Proveedor'], $tarjeta, $rd['Agencia'], $rd['Taxista'], $rd['Origen']
    }
    $rd.Close()
    if ($n -eq 0) { '  (no hay reglas de Casco)' }
}

try {
    if (-not (Invoke-Sql "SELECT OBJECT_ID(N'dbo.ControlTaxiComisiones', N'U')")) {
        Write-Host 'No existe dbo.ControlTaxiComisiones en esta base. No hago nada.' -ForegroundColor Red
        exit 1
    }

    Write-Host ''
    Write-Host "Servidor: $($cr.SqlServer)   base: $($cr.Database)" -ForegroundColor Cyan
    Write-Host 'Reglas de Casco hoy:' -ForegroundColor Cyan
    Show-Rules
    Write-Host ''

    if ($Revertir) {
        if (-not (Invoke-Sql "SELECT OBJECT_ID(N'dbo.ControlTaxiComisiones_Respaldo_20260919', N'U')")) {
            Write-Host 'No existe el respaldo del 19/09/2026: no hay nada que revertir.' -ForegroundColor Yellow
            exit 0
        }
        $ok = Read-Host 'Esto APAGA las reglas del 19/09/2026 y regresa el estado anterior. Escribe SI para continuar'
        if ($ok -ne 'SI') { Write-Host 'Cancelado. No se toco nada.'; exit 0 }
        Invoke-Sql @'
SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE dbo.ControlTaxiComisiones SET Activo = 0
WHERE BranchCode = N'CV' AND OrigenExcel = N'CASCO 2026-09-19';
UPDATE c SET c.Activo = r.Activo
FROM dbo.ControlTaxiComisiones c
INNER JOIN dbo.ControlTaxiComisiones_Respaldo_20260919 r ON r.Id = c.Id;
COMMIT TRANSACTION;
'@ | Out-Null
        Write-Host 'REVERTIDO. Reglas despues del cambio:' -ForegroundColor Green
        Show-Rules
        exit 0
    }

    $existenNuevas = [int](Invoke-Sql "SELECT COUNT(*) FROM dbo.ControlTaxiComisiones WHERE BranchCode = N'CV' AND OrigenExcel = N'CASCO 2026-09-19'")
    $activasNuevas = [int](Invoke-Sql "SELECT COUNT(*) FROM dbo.ControlTaxiComisiones WHERE BranchCode = N'CV' AND OrigenExcel = N'CASCO 2026-09-19' AND Activo = 1")
    $otrasActivas = [int](Invoke-Sql "SELECT COUNT(*) FROM dbo.ControlTaxiComisiones WHERE BranchCode = N'CV' AND Activo = 1 AND OrigenExcel <> N'CASCO 2026-09-19'")
    if ($existenNuevas -gt 0 -and $activasNuevas -eq $existenNuevas -and $otrasActivas -eq 0) {
        Write-Host 'Las reglas del 19/09/2026 YA estan cargadas y activas. No se toco nada.' -ForegroundColor Yellow
        exit 0
    }

    Write-Host 'Se van a apagar las reglas activas de arriba (si hay) y a cargar estas 14:' -ForegroundColor Cyan
    Write-Host '  BIKE CID              10 % agencia   | quita dejada     | 19 % solo con tarjeta'
    Write-Host '  TAXIS/VANS            10 % taxista   | quita dejada     | 19 % solo con tarjeta'
    Write-Host '  CALLE                 sin comision de taxi ni agencia'
    Write-Host '  EXTREME               10 % guia      | no quita dejada  | 19 % solo con tarjeta'
    Write-Host '  AVENTURAS MAYAS       10 % guia + 2 % agencia | -$100 por cada $1,000 desde $1,000'
    Write-Host '  MAJESTIC              8 % guia + 4 % agencia  | 19 % SIEMPRE, con o sin tarjeta'
    Write-Host '  VENTAS ENTRE TIENDAS  50 % (com. Matilde)     | 19 % solo con tarjeta'
    Write-Host '  FARMACIAS             sin regla (pendiente): sigue en 10 % de respaldo'
    Write-Host ''
    $ok = Read-Host 'Escribe SI para aplicar'
    if ($ok -ne 'SI') { Write-Host 'Cancelado. No se toco nada.'; exit 0 }

    # Respaldo y columnas nuevas: van antes y aparte del INSERT, porque SQL Server compila el lote
    # completo y no deja usar en el mismo lote una columna recien agregada.
    Invoke-Sql @'
IF OBJECT_ID(N'dbo.ControlTaxiComisiones_Respaldo_20260919', N'U') IS NULL
    SELECT * INTO dbo.ControlTaxiComisiones_Respaldo_20260919 FROM dbo.ControlTaxiComisiones;
IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'AplicaRetencion') IS NULL
    ALTER TABLE dbo.ControlTaxiComisiones ADD AplicaRetencion BIT NULL;
IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'AplicaDejada') IS NULL
    ALTER TABLE dbo.ControlTaxiComisiones ADD AplicaDejada BIT NULL;
IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'AplicaGasto') IS NULL
    ALTER TABLE dbo.ControlTaxiComisiones ADD AplicaGasto BIT NULL;
IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'AplicaDegustacion') IS NULL
    ALTER TABLE dbo.ControlTaxiComisiones ADD AplicaDegustacion BIT NULL;
'@ | Out-Null

    if ($existenNuevas -gt 0) {
        # Ya se habian cargado y luego se revirtieron: se vuelven a prender, no se duplican.
        Invoke-Sql @'
SET XACT_ABORT ON;
BEGIN TRANSACTION;
UPDATE dbo.ControlTaxiComisiones SET Activo = 0
WHERE BranchCode = N'CV' AND Activo = 1 AND OrigenExcel <> N'CASCO 2026-09-19';
UPDATE dbo.ControlTaxiComisiones SET Activo = 1
WHERE BranchCode = N'CV' AND OrigenExcel = N'CASCO 2026-09-19';
COMMIT TRANSACTION;
'@ | Out-Null
    } else {
    Invoke-Sql @'
SET XACT_ABORT ON;
BEGIN TRANSACTION;

UPDATE dbo.ControlTaxiComisiones SET Activo = 0 WHERE BranchCode = N'CV' AND Activo = 1;

INSERT INTO dbo.ControlTaxiComisiones
    (BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
     ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
     AplicaRetencion, AplicaDejada, AplicaGasto, AplicaDegustacion,
     Activo, ReglaNombre, OrigenExcel, RequiereValidacion, Observaciones)
VALUES
    (N'CV', N'BIKE CID',             N'GENERAL', 1, 0, NULL, 0.1000, NULL,   NULL, NULL, 1, 1, 1, 1, 1, N'BIKE CID C/TARJETA (19/09/2026)',             N'CASCO 2026-09-19', 0, N'10% agencia. Quita 19% banco, dejada, gasto y degustacion.'),
    (N'CV', N'BIKE CID',             N'GENERAL', 0, 0, NULL, 0.1000, NULL,   NULL, NULL, 0, 1, 1, 1, 1, N'BIKE CID S/TARJETA (19/09/2026)',             N'CASCO 2026-09-19', 0, N'10% agencia. Quita dejada, gasto y degustacion.'),
    (N'CV', N'TAXIS/VANS',           N'GENERAL', 1, 0, NULL, NULL,   0.1000, NULL, NULL, 1, 1, 1, 1, 1, N'TAXIS/VANS C/TARJETA (19/09/2026)',           N'CASCO 2026-09-19', 0, N'10% taxista. Quita 19% banco, dejada, gasto y degustacion.'),
    (N'CV', N'TAXIS/VANS',           N'GENERAL', 0, 0, NULL, NULL,   0.1000, NULL, NULL, 0, 1, 1, 1, 1, N'TAXIS/VANS S/TARJETA (19/09/2026)',           N'CASCO 2026-09-19', 0, N'10% taxista. Quita dejada, gasto y degustacion.'),
    (N'CV', N'CALLE',                N'GENERAL', 1, 0, NULL, NULL,   NULL,   NULL, NULL, 1, 0, 1, 1, 1, N'CALLE C/TARJETA (19/09/2026)',                N'CASCO 2026-09-19', 0, N'Sin comision de taxi ni agencia (en el Excel solo lleva comision de vendedor, que quedo fuera).'),
    (N'CV', N'CALLE',                N'GENERAL', 0, 0, NULL, NULL,   NULL,   NULL, NULL, 0, 0, 1, 1, 1, N'CALLE S/TARJETA (19/09/2026)',                N'CASCO 2026-09-19', 0, N'Sin comision de taxi ni agencia (en el Excel solo lleva comision de vendedor, que quedo fuera).'),
    (N'CV', N'EXTREME',              N'GENERAL', 1, 0, NULL, NULL,   0.1000, NULL, NULL, 1, 0, 1, 1, 1, N'EXTREME C/TARJETA (19/09/2026)',              N'CASCO 2026-09-19', 0, N'10% guia (confirmado 19/09/2026). Quita 19% banco, gasto y degustacion. No quita dejada.'),
    (N'CV', N'EXTREME',              N'GENERAL', 0, 0, NULL, NULL,   0.1000, NULL, NULL, 0, 0, 1, 1, 1, N'EXTREME S/TARJETA (19/09/2026)',              N'CASCO 2026-09-19', 0, N'10% guia (confirmado 19/09/2026). Quita gasto y degustacion. No quita dejada.'),
    (N'CV', N'AVENTURAS MAYAS',      N'GENERAL', 1, 0, NULL, 0.0200, 0.1000, NULL, NULL, 1, 0, 1, 1, 1, N'AVENTURAS MAYAS C/TARJETA (19/09/2026)',      N'CASCO 2026-09-19', 0, N'10% guia + 2% agencia. Desde $1,000 se quitan $100 por cada $1,000. Quita 19% banco, gasto y degustacion.'),
    (N'CV', N'AVENTURAS MAYAS',      N'GENERAL', 0, 0, NULL, 0.0200, 0.1000, NULL, NULL, 0, 0, 1, 1, 1, N'AVENTURAS MAYAS S/TARJETA (19/09/2026)',      N'CASCO 2026-09-19', 0, N'10% guia + 2% agencia. Desde $1,000 se quitan $100 por cada $1,000. Quita gasto y degustacion.'),
    (N'CV', N'MAJESTIC',             N'GENERAL', 1, 0, NULL, 0.0400, 0.0800, NULL, NULL, 1, 0, 1, 1, 1, N'MAJESTIC C/TARJETA (19/09/2026)',             N'CASCO 2026-09-19', 0, N'8% guia + 4% agencia. Quita 19% banco, gasto y degustacion.'),
    (N'CV', N'MAJESTIC',             N'GENERAL', 0, 0, NULL, 0.0400, 0.0800, NULL, NULL, 1, 0, 1, 1, 1, N'MAJESTIC S/TARJETA (19/09/2026)',             N'CASCO 2026-09-19', 0, N'8% guia + 4% agencia. El 19% se quita TAMBIEN sin tarjeta (confirmado 19/09/2026).'),
    (N'CV', N'VENTAS ENTRE TIENDAS', N'GENERAL', 1, 0, NULL, 0.5000, NULL,   NULL, NULL, 1, 0, 1, 1, 1, N'VENTAS ENTRE TIENDAS C/TARJETA (19/09/2026)', N'CASCO 2026-09-19', 0, N'50% com. Matilde. Quita 19% banco, gasto y degustacion.'),
    (N'CV', N'VENTAS ENTRE TIENDAS', N'GENERAL', 0, 0, NULL, 0.5000, NULL,   NULL, NULL, 0, 0, 1, 1, 1, N'VENTAS ENTRE TIENDAS S/TARJETA (19/09/2026)', N'CASCO 2026-09-19', 0, N'50% com. Matilde. Quita gasto y degustacion.');

COMMIT TRANSACTION;
'@ | Out-Null
    }

    Write-Host ''
    Write-Host 'APLICADO. Reglas de Casco ahora:' -ForegroundColor Green
    Show-Rules
    Write-Host ''
    Write-Host 'Respaldo: dbo.ControlTaxiComisiones_Respaldo_20260919. Para revertir: -Revertir' -ForegroundColor Cyan
}
finally {
    $cn.Close()
}
