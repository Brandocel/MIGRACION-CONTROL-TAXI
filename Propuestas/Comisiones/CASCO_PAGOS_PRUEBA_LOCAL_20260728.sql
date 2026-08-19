/*
  Pagos de prueba para ventas PRUEBA-CV.
  Se ejecuta despues de cargar RemisioM. Usa tipo_pago corto porque la columna mide 4 caracteres.
*/

USE [compuadmoCasco];
GO
SET NOCOUNT ON;

INSERT INTO dbo.pagosM
(
    folio_factura, cliente, fecha_pago, fecha_cobro, tipo_pago, referencia, total, moneda, almacen, tipocambio
)
SELECT v.folio_factura, v.cliente, GETDATE(), GETDATE(), v.tipo_pago, N'PRUEBA_CV', v.total, v.moneda, N'186', 1
FROM (VALUES
    (N'PRUEBA-CV-0031-C01', 0, N'EF', 300.00, 0),
    (N'PRUEBA-CV-0031-C02', 0, N'TA', 450.00, 1),
    (N'PRUEBA-CV-0032-C01', 0, N'EF', 800.00, 0),
    (N'PRUEBA-CV-0033-C01', 0, N'TA', 1500.00, 1)
) AS v(folio_factura, cliente, tipo_pago, total, moneda)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.pagosM p
    WHERE p.folio_factura = v.folio_factura
);
GO

USE [joyeriaCasco];
GO
SET NOCOUNT ON;

INSERT INTO dbo.pagosM
(
    folio_factura, cliente, fecha_pago, fecha_cobro, tipo_pago, referencia, total, moneda
)
SELECT v.folio_factura, v.cliente, GETDATE(), GETDATE(), v.tipo_pago, N'PRUEBA_CV', v.total, v.moneda
FROM (VALUES
    (N'PRUEBA-CV-0031-J01', 0, N'TA', 6500.00, 1),
    (N'PRUEBA-CV-0031-J02', 0, N'EF', 1200.00, 0),
    (N'PRUEBA-CV-0032-J01', 0, N'TA', 6500.00, 1),
    (N'PRUEBA-CV-0032-J02', 0, N'EF', 2300.00, 0),
    (N'PRUEBA-CV-0033-J01', 0, N'TA', 6500.00, 1)
) AS v(folio_factura, cliente, tipo_pago, total, moneda)
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.pagosM p
    WHERE p.folio_factura = v.folio_factura
);
GO
