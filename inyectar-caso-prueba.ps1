# =============================================================================
#  Caso de prueba end-to-end: pago de DEJADA (Relacion Ticket-Taxista)
#                              y pago de COMISION (Comisiones)
#
#  Crea UN viaje de prueba, identificable por el folio TEST-9001, en la base
#  LOCAL .\SQLEXPRESS. No toca ningun registro existente.
#
#  Estado inicial esperado:
#     Dejada   $500.00  -> PENDIENTE   (se paga en Relacion Ticket-Taxista)
#     Venta  $2,000.00  -> genera comision -> PENDIENTE (se paga en Comisiones)
#
#  Para borrarlo despues, ejecuta limpiar-caso-prueba.ps1
# =============================================================================

$ErrorActionPreference = 'Stop'
$server = '.\SQLEXPRESS'

$sql = @'
-- OBLIGATORIO: compuadmo.dbo.remisioM tiene una columna calculada PERSISTED con indice
-- (folioregistro_txt, la que se agrego para acelerar Comisiones). SQL Server rechaza
-- cualquier INSERT/DELETE sobre esa tabla si QUOTED_IDENTIFIER esta apagado, y sqlcmd lo
-- apaga por defecto -> Msg 1934.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @folio      nvarchar(60) = N'TEST-9001';
DECLARE @ticketPos  nvarchar(60) = N'TESTPOS9001';
DECLARE @taxista    nvarchar(120) = N'TAXISTA PRUEBA';
DECLARE @dejada     decimal(18,2) = 500.00;
DECLARE @venta      float          = 2000.00;
DECLARE @fecha      datetime2      = CAST(CAST(GETDATE() AS date) AS datetime2);

-------------------------------------------------------------------------------
-- 1) Operacion de la app movil (mkt.dbo.AppMovilRegistro)
--    Aqui vive la DEJADA y su estatus de pago.
-------------------------------------------------------------------------------
DELETE FROM mkt.dbo.AppMovilRegistro WHERE folio_app = @folio;

INSERT INTO mkt.dbo.AppMovilRegistro
  (folio_app, folio_pos, fecha_operacion, vendedor_clave, vendedor_nombre, hotel, pax,
   tipo_operacion, subtotal, iva, total, efectivo, tarjeta, dolares, tipo_cambio,
   usuario_movil, notas, detalle_json, pagos_json, origen, estado_sync, fecha_creacion,
   folio_app_original, folio_gafete, id_catalogo, telefono_taxista, telefono_contacto,
   placas, modelo_vehiculo, unidad, sitio, destino, nacionalidad,
   estado_pago_dejada, usuario_pago_dejada, ticket_pago_dejada,
   payout_status, payout_user, payout_ticket,
   seller_name, adult_count, youth_count, minor_count, no_show_count, seller_key)
VALUES
  (@folio, @ticketPos, @fecha, N'V-PRUEBA', @taxista, N'HOTEL PRUEBA', 2,
   N'VAN VERDE', 0, 0, @dejada, @dejada, 0, 0, 0,
   N'prueba', N'Caso de prueba automatizado', N'{}', N'{}', N'APP MOVIL', N'sincronizado', SYSDATETIME(),
   @folio, N'999', NULL, N'', N'',
   N'TEST-000', N'', N'9999', N'Tienda Plaza 28', N'', N'NACIONALES',
   N'pendiente', N'', N'',
   N'pendiente', N'', N'',
   @taxista, 2, 0, 0, 0, N'V-PRUEBA');

-------------------------------------------------------------------------------
-- 2) Relacion ticket <-> taxista (mkt.dbo.RelacionTicketTaxista)
--    Enlaza la operacion con el ticket de venta del POS.
-------------------------------------------------------------------------------
DELETE FROM mkt.dbo.RelacionTicketTaxista WHERE FolioApp = @folio;

INSERT INTO mkt.dbo.RelacionTicketTaxista
  (FolioApp, FolioOperacion, FolioPos, Gafete, TaxistaId, TaxistaNombre, TransporteTipo,
   Observaciones, Usuario, FechaCreacion, FechaActualizacion, Vendedor, Dejada,
   Nacionalidad, Pax, AdultCount, YouthCount, MinorCount, NoShowCount)
VALUES
  (@folio, @folio, @ticketPos, N'999', 999999, @taxista, N'VAN VERDE',
   N'Caso de prueba automatizado', N'prueba', SYSDATETIME(), SYSDATETIME(), @taxista, @dejada,
   N'NACIONALES', 2, 2, 0, 0, 0);

-------------------------------------------------------------------------------
-- 3) Venta de tienda (compuadmo.dbo.remisioM)
--    Sin esta venta, Comisiones no tiene sobre que calcular.
--
--    OJO con los tamaños: esta tabla es muy estrecha y guarda CODIGOS, no nombres:
--      cliente(6)  vendedor(4)  estatus(1)  usuario(4)  almacen(4)  moneda(1)
--    Las filas reales se ven asi:
--      folio_remision=BD14447522477  folio_factura=0  tipof=R  cliente=0
--      vendedor=210 (el gafete)  estatus=A  almacen=100  folioregistro=0
--    El enlace con la comision NO es por folioregistro (que va en 0), sino por
--    folio_remision = AppMovilRegistro.folio_pos.
-------------------------------------------------------------------------------
DELETE FROM compuadmo.dbo.remisioM WHERE folio_remision = @ticketPos;

INSERT INTO compuadmo.dbo.remisioM
  (folio_remision, folio_factura, fecha, tipof, cliente, vendedor, estatus,
   stotal, iva, total, saldo, observaciones, descuento, tipo_cambio,
   usuario, hora, almacen, efectivo, tarjeta, dolares, cotizadolar,
   folioregistro)
VALUES
  (@ticketPos, N'0', GETDATE(), N'R', N'0', N'999', N'A',
   @venta, 0, @venta, 0, N'CASO DE PRUEBA TEST-9001', 0, 1,
   N'test', CONVERT(nvarchar(8), GETDATE(), 108), N'100', @venta, 0, 0, 1,
   0);

-------------------------------------------------------------------------------
-- Verificacion
-------------------------------------------------------------------------------
SELECT 'AppMovilRegistro' AS Tabla, folio_app AS Folio,
       total AS Dejada, estado_pago_dejada AS EstDejada,
       COALESCE(pago_comision, 0) AS PagoComision
FROM mkt.dbo.AppMovilRegistro WHERE folio_app = @folio;

SELECT 'remisioM' AS Tabla, folio_remision AS Ticket, total AS Venta, estatus AS Estatus
FROM compuadmo.dbo.remisioM WHERE folio_remision = @ticketPos;
'@

$file = Join-Path $env:TEMP 'inyectar-caso-prueba.sql'
Set-Content -Path $file -Value $sql -Encoding utf8

Write-Host "Insertando caso de prueba TEST-9001 en $server ..." -ForegroundColor Cyan
sqlcmd -S $server -E -b -i $file

# -----------------------------------------------------------------------------
#  Snapshot local de comisiones (SQLite): se borra SIEMPRE al inyectar, para que
#  la prueba arranque limpia. Si queda un snapshot con saldo 0 de una corrida
#  anterior, el boton PAGAR de Comisiones responde "La comision ya esta pagada"
#  aunque en SQL Server siga pendiente: esa copia local es la que manda.
# -----------------------------------------------------------------------------
$raiz   = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue
$bases  = @(
    (Join-Path $raiz 'DatosLocal\ControlTaxi.db'),
    (Join-Path $raiz 'ControlTaxiDesktop\bin\Release\net9.0-windows\DatosLocal\ControlTaxi.db'),
    (Join-Path $raiz 'ControlTaxiDesktop\bin\Debug\net9.0-windows\DatosLocal\ControlTaxi.db')
) | Where-Object { Test-Path $_ }

if ($sqlite) {
    foreach ($db in $bases) {
        # No todas las copias de la base tienen la tabla (p.ej. la de bin\Debug recien creada),
        # asi que se pregunta antes: si no existe, no hay nada que limpiar.
        $tabla = & $sqlite.Source $db "SELECT name FROM sqlite_master WHERE type='table' AND name='LocalComisiones';"
        if ($tabla) {
            & $sqlite.Source $db "DELETE FROM LocalComisiones WHERE Folio LIKE '%TEST-9001%' OR VentaFolio LIKE '%TEST-9001%';"
            Write-Host "Snapshot local limpiado: $db" -ForegroundColor DarkGray
        }
    }
} else {
    Write-Host "AVISO: no hay sqlite3 en el PATH; si PAGAR dice 'ya esta pagada', borra a mano la fila C-TEST-9001-TESTPOS9001 de LocalComisiones." -ForegroundColor Yellow
}

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "LISTO. Caso de prueba creado." -ForegroundColor Green
    Write-Host ""
    Write-Host "Como probarlo:" -ForegroundColor Yellow
    Write-Host "  1. Relacion Ticket-Taxista -> busca 'TEST-9001'"
    Write-Host "     Debe aparecer con Pago dejada = PENDIENTE y el boton PAGAR visible."
    Write-Host "     Pulsa PAGAR -> debe quedar PAGADO."
    Write-Host ""
    Write-Host "  2. Comisiones -> filtra por la fecha de hoy o el folio TEST-9001"
    Write-Host "     Est. dejada debe decir PAGADA (lo que acabas de hacer)."
    Write-Host "     Est. comision debe decir PENDIENTE."
    Write-Host "     Pulsa PAGAR en esa fila -> Est. comision pasa a PAGADA."
    Write-Host ""
    Write-Host "  Los dos estatus deben moverse por separado: ese es el punto de la prueba."
} else {
    Write-Host "ERROR: la insercion fallo. Revisa el mensaje de arriba." -ForegroundColor Red
}
