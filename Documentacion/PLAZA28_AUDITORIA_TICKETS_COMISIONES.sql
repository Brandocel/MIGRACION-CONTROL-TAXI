/*
Auditoria Plaza 28 - tickets y comisiones
Solo lectura. No ejecuta UPDATE/INSERT/DELETE.

Cambiar @FolioOperacion, @Ticket1, @Ticket2, @Taxista y fechas segun el caso.
*/

USE [mkt2];
GO

DECLARE @FolioOperacion nvarchar(60) = N'3108';
DECLARE @Ticket1 nvarchar(80) = N'BA14066729526';
DECLARE @Ticket2 nvarchar(80) = N'BA14066829527';
DECLARE @Taxista nvarchar(150) = N'MARIO MENDOZA';
DECLARE @FechaInicio date = '2026-07-30';
DECLARE @FechaFin date = '2026-07-30';

PRINT '1) Registro APP';
SELECT
    folio_app,
    folio_app_original,
    folio_pos,
    vendedor_nombre,
    seller_name,
    folio_gafete,
    hotel,
    sitio,
    unidad,
    tipo_operacion,
    pax,
    adult_count,
    youth_count,
    minor_count,
    total,
    fecha_operacion,
    fecha_creacion
FROM dbo.AppMovilRegistro
WHERE folio_app = @FolioOperacion
   OR folio_app_original = @FolioOperacion
   OR vendedor_nombre LIKE N'%' + @Taxista + N'%'
ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;

PRINT '2) Relacion Ticket Taxista';
SELECT *
FROM dbo.RelacionTicketTaxista
WHERE FolioApp = @FolioOperacion
   OR FolioOperacion = @FolioOperacion
   OR FolioPos LIKE N'%' + @Ticket1 + N'%'
   OR FolioPos LIKE N'%' + @Ticket2 + N'%';

PRINT '3) Dejadas';
SELECT
    folioregistro,
    folioregistrostr,
    codigorecepcion,
    nombrestaff,
    nombrevendedor,
    unidad,
    gafete,
    total,
    comision,
    pago,
    fecha,
    fechapago
FROM dbo.dejadas
WHERE codigorecepcion = @FolioOperacion
   OR folioregistrostr = @FolioOperacion
   OR CAST(folioregistro AS nvarchar(60)) = @FolioOperacion
ORDER BY fecha DESC, gafete;

PRINT '4) Tickets compuadmoPlaza directos y por observaciones';
SELECT
    N'compuadmoPlaza' AS Fuente,
    folio_remision AS Ticket,
    folio_factura,
    folio_operacion,
    folioregistro,
    fecha,
    estatus,
    CAST(total AS decimal(18,2)) AS total,
    CAST(stotal AS decimal(18,2)) AS stotal,
    LEN(folio_remision) AS longitud_ticket,
    LEFT(CONVERT(nvarchar(max), observaciones), 250) AS observaciones
FROM compuadmoPlaza.dbo.RemisioM
WHERE folio_remision IN (@Ticket1, @Ticket2)
   OR folio_factura IN (@Ticket1, @Ticket2)
   OR CAST(folioregistro AS nvarchar(60)) = @FolioOperacion
   OR CAST(folio_operacion AS nvarchar(60)) = @FolioOperacion
   OR CONVERT(nvarchar(max), observaciones) LIKE N'%' + @FolioOperacion + N'%'
   OR CONVERT(nvarchar(max), observaciones) LIKE N'%' + @Taxista + N'%'
ORDER BY fecha, Ticket;

PRINT '5) Tickets joyeriaPlaza directos y por observaciones';
SELECT
    N'joyeriaPlaza' AS Fuente,
    COALESCE(folio_factura, folio_pedido) AS Ticket,
    folio_pedido,
    folio_factura,
    folio_operacion,
    folio_registro,
    fecha,
    estatus,
    CAST(total AS decimal(18,2)) AS total,
    CAST(stotal AS decimal(18,2)) AS stotal,
    LEN(COALESCE(folio_factura, folio_pedido)) AS longitud_ticket,
    LEFT(CONVERT(nvarchar(max), observaciones), 250) AS observaciones
FROM joyeriaPlaza.dbo.RemisioM
WHERE folio_factura IN (@Ticket1, @Ticket2)
   OR folio_pedido IN (@Ticket1, @Ticket2)
   OR CAST(folio_registro AS nvarchar(60)) = @FolioOperacion
   OR CAST(folio_operacion AS nvarchar(60)) = @FolioOperacion
   OR CONVERT(nvarchar(max), observaciones) LIKE N'%' + @FolioOperacion + N'%'
   OR CONVERT(nvarchar(max), observaciones) LIKE N'%' + @Taxista + N'%'
ORDER BY fecha, Ticket;

PRINT '6) Pagos compuadmoPlaza';
SELECT
    p.folio_factura AS Ticket,
    p.tipo_pago,
    p.moneda,
    m.Nombre AS MonedaNombre,
    CAST(p.total AS decimal(18,2)) AS total,
    p.referencia
FROM compuadmoPlaza.dbo.pagosM p
LEFT JOIN compuadmoPlaza.dbo.monedas m ON m.moneda = p.moneda
WHERE p.folio_factura IN (@Ticket1, @Ticket2)
   OR p.folio_factura IN
   (
       SELECT folio_remision
       FROM compuadmoPlaza.dbo.RemisioM
       WHERE CAST(folioregistro AS nvarchar(60)) = @FolioOperacion
          OR CAST(folio_operacion AS nvarchar(60)) = @FolioOperacion
          OR CONVERT(nvarchar(max), observaciones) LIKE N'%' + @FolioOperacion + N'%'
   )
ORDER BY p.folio_factura;

PRINT '7) Pagos joyeriaPlaza';
SELECT
    p.folio_factura AS Ticket,
    p.tipo_pago,
    p.moneda,
    m.Nombre AS MonedaNombre,
    CAST(p.total AS decimal(18,2)) AS total,
    p.referencia
FROM joyeriaPlaza.dbo.pagosM p
LEFT JOIN joyeriaPlaza.dbo.monedas m ON m.Moneda = p.moneda
WHERE p.folio_factura IN (@Ticket1, @Ticket2)
   OR p.folio_factura IN
   (
       SELECT COALESCE(folio_factura, folio_pedido)
       FROM joyeriaPlaza.dbo.RemisioM
       WHERE CAST(folio_registro AS nvarchar(60)) = @FolioOperacion
          OR CAST(folio_operacion AS nvarchar(60)) = @FolioOperacion
          OR CONVERT(nvarchar(max), observaciones) LIKE N'%' + @FolioOperacion + N'%'
   )
ORDER BY p.folio_factura;

PRINT '8) Posibles tickets sin folio_operacion pero con folio en observaciones';
SELECT
    N'compuadmoPlaza' AS Fuente,
    folio_remision AS Ticket,
    folio_operacion,
    folioregistro,
    CAST(total AS decimal(18,2)) AS total,
    LEFT(CONVERT(nvarchar(max), observaciones), 250) AS observaciones
FROM compuadmoPlaza.dbo.RemisioM
WHERE COALESCE(folio_operacion, 0) = 0
  AND COALESCE(folioregistro, 0) = 0
  AND CONVERT(nvarchar(max), observaciones) LIKE N'%' + @FolioOperacion + N'%'
UNION ALL
SELECT
    N'joyeriaPlaza' AS Fuente,
    COALESCE(folio_factura, folio_pedido) AS Ticket,
    folio_operacion,
    folio_registro,
    CAST(total AS decimal(18,2)) AS total,
    LEFT(CONVERT(nvarchar(max), observaciones), 250) AS observaciones
FROM joyeriaPlaza.dbo.RemisioM
WHERE COALESCE(folio_operacion, 0) = 0
  AND COALESCE(folio_registro, 0) = 0
  AND CONVERT(nvarchar(max), observaciones) LIKE N'%' + @FolioOperacion + N'%';

