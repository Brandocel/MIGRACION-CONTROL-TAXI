/*
  Ventas de prueba de farmacia/compuadmo para Casco local.
  Servidor: REYNA
  Base: compuadmoCasco
  Reversion: CASCO_VENTAS_PRUEBA_LOCAL_20260728_ROLLBACK.sql
*/

USE [compuadmoCasco];
GO

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.RemisioM WHERE folio_remision LIKE N'PRUEBA-CV-%')
BEGIN
    RAISERROR('Ya existen ventas PRUEBA-CV en compuadmoCasco. Ejecuta el rollback antes de volver a cargar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.RemisioM
(
    folio_remision, folio_factura, fecha, tipof, cliente, vendedor, estatus,
    stotal, iva, total, saldo, fecha_cobro, observaciones, descuento, tipo,
    tipo_cambio, letras, usuario, hora, almacen, moneda, oservicio,
    efectivo, tarjeta, dolares, cotizadolar, folio_operacion, guia, codigoguia, folioregistro
)
VALUES
(N'PRUEBA-CV-0031-C01', N'0', '2026-07-28', N'P', 0, 1, N'A', 300.00, 0.00, 300.00, 300.00, NULL, N'PRUEBA_COMISION_CV folio 0031 compuadmo 1', 0, 0, 1, N'', N'hoka', CONVERT(nvarchar(20), GETDATE(), 108), N'186', NULL, N'', 300.00, 0.00, 0.00, 1, 31, 0, N'', 31),
(N'PRUEBA-CV-0031-C02', N'0', '2026-07-28', N'P', 0, 1, N'A', 450.00, 0.00, 450.00, 450.00, NULL, N'PRUEBA_COMISION_CV folio 0031 compuadmo 2', 0, 0, 1, N'', N'hoka', CONVERT(nvarchar(20), GETDATE(), 108), N'186', NULL, N'', 0.00, 450.00, 0.00, 1, 31, 0, N'', 31),
(N'PRUEBA-CV-0032-C01', N'0', '2026-07-28', N'P', 0, 1, N'A', 800.00, 0.00, 800.00, 800.00, NULL, N'PRUEBA_COMISION_CV folio 0032 compuadmo 1', 0, 0, 1, N'', N'hoka', CONVERT(nvarchar(20), GETDATE(), 108), N'186', NULL, N'', 800.00, 0.00, 0.00, 1, 32, 0, N'', 32),
(N'PRUEBA-CV-0033-C01', N'0', '2026-07-28', N'P', 0, 1, N'A', 1500.00, 0.00, 1500.00, 1500.00, NULL, N'PRUEBA_COMISION_CV folio 0033 compuadmo 1', 0, 0, 1, N'', N'hoka', CONVERT(nvarchar(20), GETDATE(), 108), N'186', NULL, N'', 0.00, 1500.00, 0.00, 1, 33, 0, N'', 33);

INSERT INTO dbo.pagosM
(
    folio_factura, cliente, fecha_pago, fecha_cobro, tipo_pago, referencia, total, moneda, almacen, tipocambio
)
VALUES
(N'PRUEBA-CV-0031-C01', 0, GETDATE(), GETDATE(), N'EFECTIVO', N'PRUEBA_COMISION_CV', 300.00, 0, N'186', 1),
(N'PRUEBA-CV-0031-C02', 0, GETDATE(), GETDATE(), N'TARJETA', N'PRUEBA_COMISION_CV', 450.00, 1, N'186', 1),
(N'PRUEBA-CV-0032-C01', 0, GETDATE(), GETDATE(), N'EFECTIVO', N'PRUEBA_COMISION_CV', 800.00, 0, N'186', 1),
(N'PRUEBA-CV-0033-C01', 0, GETDATE(), GETDATE(), N'TARJETA', N'PRUEBA_COMISION_CV', 1500.00, 1, N'186', 1);

SELECT folio_operacion, COUNT(*) AS Tickets, SUM(stotal) AS VentaCompuadmo
FROM dbo.RemisioM
WHERE folio_remision LIKE N'PRUEBA-CV-%'
GROUP BY folio_operacion
ORDER BY folio_operacion;
GO
