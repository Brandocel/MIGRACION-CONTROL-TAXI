<#
Parche aditivo al index.php del API de Hostinger (2026-09-10).

Problema: /sync/pull-changes devolvia los registros de la app SIN el arreglo
sellerBadges (los pares vendedor/gafete). Solo /api/taxis/registros lo pegaba.
El sincronizador de Plaza 28 baja primero por pull-changes, asi que el
escritorio recibia los registros nuevos sin vendedores.

Cambio: envolver el SELECT de mkt2_trip_records de pull-changes con
attach_trip_record_sellers(), que ya existe en el archivo. Una sola linea.

Uso:
  .\aplicar-parche-pull-sellerbadges.ps1 -Ruta C:\ruta\index.php
Hace respaldo index.php.backup-AAAAMMDD-HHmmss y aborta si el ancla no calza
o si el parche ya esta aplicado.
#>
param([Parameter(Mandatory = $true)][string]$Ruta)

if (-not (Test-Path $Ruta)) { throw "No existe $Ruta" }
$src = [System.IO.File]::ReadAllText($Ruta, [System.Text.Encoding]::UTF8)

$ancla = "'mkt2_trip_records' => rows(`$db, `"SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,sellerKey,sellerName,adultCount,youthCount,minorCount,noShowCount,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket,createdAt,updatedAt,source,syncStatus FROM mkt2_trip_records WHERE `$tripWhere ORDER BY COALESCE(updatedAt, createdAt) ASC LIMIT `$limit`", `$tripParams),"
$nuevo = "'mkt2_trip_records' => attach_trip_record_sellers(`$db, rows(`$db, `"SELECT recordId,catalogId,badgeId,driverName,driverPhone,contactPhone,nationality,plate,vehicleModel,unitNumber,hotel,origin,site,destination,sellerKey,sellerName,adultCount,youthCount,minorCount,noShowCount,passengerCount,serviceType,tripCost,paymentMethod,notes,recordDate,assignedBranch,assignedBranchCode,assignedBranchName,payoutStatus,payoutDate,payoutUser,payoutTicket,createdAt,updatedAt,source,syncStatus FROM mkt2_trip_records WHERE `$tripWhere ORDER BY COALESCE(updatedAt, createdAt) ASC LIMIT `$limit`", `$tripParams)),"

if ($src.Contains($nuevo)) { Write-Host "El parche ya estaba aplicado. Sin cambios."; exit 0 }
if ($src -notmatch [regex]::Escape('function attach_trip_record_sellers')) { throw "Este index.php no tiene attach_trip_record_sellers(); no es la version con vendedores/gafetes." }
$veces = ([regex]::Matches($src, [regex]::Escape($ancla))).Count
if ($veces -ne 1) { throw "El ancla aparece $veces veces (se esperaba 1). No se toca nada." }

$respaldo = "$Ruta.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Copy-Item $Ruta $respaldo
$out = $src.Replace($ancla, $nuevo)
[System.IO.File]::WriteAllText($Ruta, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "OK. Respaldo en $respaldo"
Write-Host "Rollback: Copy-Item '$respaldo' '$Ruta' -Force"
