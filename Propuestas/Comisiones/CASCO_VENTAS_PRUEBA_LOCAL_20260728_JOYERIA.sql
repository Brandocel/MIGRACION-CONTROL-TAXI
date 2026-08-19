/*
  Ventas de prueba de joyeria para Casco local.
  Servidor: REYNA
  Base: joyeriaCasco
  Reversion: CASCO_VENTAS_PRUEBA_LOCAL_20260728_ROLLBACK.sql
*/

USE [joyeriaCasco];
GO

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.RemisioM WHERE folio_factura LIKE N'PRUEBA-CV-%')
BEGIN
    RAISERROR('Ya existen ventas PRUEBA-CV en joyeriaCasco. Ejecuta el rollback antes de volver a cargar.', 16, 1);
    RETURN;
END;

INSERT INTO dbo.RemisioM
(
    folio_pedido, folio_factura, fecha, tipo, cliente, vendedor, estatus,
    stotal, iva, total, saldo, fecha_cobro, observaciones, descuento, moneda,
    tipo_cambio, letras, cajero, procesado, ncorte, hora, comisionista, peso,
    costotienda, cuantosvend, utilidadbruta, almacen, usuario, folio_operacion, folio_registro
)
VALUES
(NULL, N'PRUEBA-CV-0031-J01', '2026-07-28', N'P', 0, 0, 1, 6500.00, 0.00, 6500.00, 6500.00, NULL, N'PRUEBA_COMISION_CV folio 0031 joyeria 1', 0, 0, 1, N'', 0, N'0', 0, GETDATE(), N'', 0, 0, 1, 0, N'186', N'hoka', 31, 31),
(NULL, N'PRUEBA-CV-0031-J02', '2026-07-28', N'P', 0, 0, 1, 1200.00, 0.00, 1200.00, 1200.00, NULL, N'PRUEBA_COMISION_CV folio 0031 joyeria 2', 0, 0, 1, N'', 0, N'0', 0, GETDATE(), N'', 0, 0, 1, 0, N'186', N'hoka', 31, 31),
(NULL, N'PRUEBA-CV-0032-J01', '2026-07-28', N'P', 0, 0, 1, 6500.00, 0.00, 6500.00, 6500.00, NULL, N'PRUEBA_COMISION_CV folio 0032 joyeria 1', 0, 0, 1, N'', 0, N'0', 0, GETDATE(), N'', 0, 0, 1, 0, N'186', N'hoka', 32, 32),
(NULL, N'PRUEBA-CV-0032-J02', '2026-07-28', N'P', 0, 0, 1, 2300.00, 0.00, 2300.00, 2300.00, NULL, N'PRUEBA_COMISION_CV folio 0032 joyeria 2', 0, 0, 1, N'', 0, N'0', 0, GETDATE(), N'', 0, 0, 1, 0, N'186', N'hoka', 32, 32),
(NULL, N'PRUEBA-CV-0033-J01', '2026-07-28', N'P', 0, 0, 1, 6500.00, 0.00, 6500.00, 6500.00, NULL, N'PRUEBA_COMISION_CV folio 0033 joyeria 1', 0, 0, 1, N'', 0, N'0', 0, GETDATE(), N'', 0, 0, 1, 0, N'186', N'hoka', 33, 33);

INSERT INTO dbo.pagosM
(
    folio_factura, cliente, fecha_pago, fecha_cobro, tipo_pago, referencia, total, moneda
)
VALUES
(N'PRUEBA-CV-0031-J01', 0, GETDATE(), GETDATE(), N'TARJETA', N'PRUEBA_COMISION_CV', 6500.00, 1),
(N'PRUEBA-CV-0031-J02', 0, GETDATE(), GETDATE(), N'EFECTIVO', N'PRUEBA_COMISION_CV', 1200.00, 0),
(N'PRUEBA-CV-0032-J01', 0, GETDATE(), GETDATE(), N'TARJETA', N'PRUEBA_COMISION_CV', 6500.00, 1),
(N'PRUEBA-CV-0032-J02', 0, GETDATE(), GETDATE(), N'EFECTIVO', N'PRUEBA_COMISION_CV', 2300.00, 0),
(N'PRUEBA-CV-0033-J01', 0, GETDATE(), GETDATE(), N'TARJETA', N'PRUEBA_COMISION_CV', 6500.00, 1);

SELECT folio_operacion, COUNT(*) AS Tickets, SUM(stotal) AS VentaJoyeria
FROM dbo.RemisioM
WHERE folio_factura LIKE N'PRUEBA-CV-%'
GROUP BY folio_operacion
ORDER BY folio_operacion;
GO
