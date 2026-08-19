/*
    Prueba local de ventas para comisiones Casco Viejo.
    Ambiente autorizado:
      Server: REYNA
      BranchCode: CV
      Bases: compuadmoCasco, joyeriaCasco

    Folio original: 0005
    folio_operacion: 5

    IMPORTANTE:
    - No ejecutar en produccion.
    - No toca Plaza 28.
    - Las filas quedan identificadas por CTTEST-CV-0005-*.
*/

SET XACT_ABORT ON;

BEGIN TRAN;

INSERT INTO compuadmoCasco.dbo.RemisioM
    (folio_remision, folio_factura, fecha, tipof, estatus, stotal, iva, total, saldo, fecha_cobro, descuento, tipo, tipo_cambio, hora, moneda, efectivo, tarjeta, dolares, cotizadolar, folio_operacion, folioregistro, observaciones)
VALUES
    (N'CTTEST-CV-0005-C1', N'CTTEST-CV-0005-C1', '2026-07-20T12:00:00', N'CONTADO', N'A', 120, 0, 120, 0, '2026-07-20T12:00:00', 0, 0, 1, N'12:00', N'M', 120, 0, 0, 1, 5, 5, N'PRUEBA CONTROL TAXI CV FOLIO 0005'),
    (N'CTTEST-CV-0005-C2', N'CTTEST-CV-0005-C2', '2026-07-20T12:05:00', N'CONTADO', N'A', 350, 0, 350, 0, '2026-07-20T12:05:00', 0, 0, 1, N'12:05', N'M', 350, 0, 0, 1, 5, 5, N'PRUEBA CONTROL TAXI CV FOLIO 0005'),
    (N'CTTEST-CV-0005-C3', N'CTTEST-CV-0005-C3', '2026-07-20T12:10:00', N'CONTADO', N'A', 180, 0, 180, 0, '2026-07-20T12:10:00', 0, 0, 1, N'12:10', N'M', 180, 0, 0, 1, 5, 5, N'PRUEBA CONTROL TAXI CV FOLIO 0005');

INSERT INTO joyeriaCasco.dbo.RemisioM
    (folio_pedido, folio_factura, fecha, tipo, estatus, stotal, iva, total, saldo, fecha_cobro, descuento, moneda, tipo_cambio, hora, procesado, ncorte, peso, costotienda, cuantosvend, utilidadbruta, usuario, folio_operacion, folio_registro, observaciones)
VALUES
    (N'CTTEST-CV-0005-J1', N'CTTEST-CV-0005-J1', '2026-07-20T12:15:00', N'CONTADO', N'A', 500, 0, 500, 0, '2026-07-20T12:15:00', 0, 1, 1, '2026-07-20T12:15:00', N'S', 0, 0, 0, 1, 0, N'CT', 5, 5, N'PRUEBA CONTROL TAXI CV FOLIO 0005'),
    (N'CTTEST-CV-0005-J2', N'CTTEST-CV-0005-J2', '2026-07-20T12:20:00', N'CONTADO', N'A', 275, 0, 275, 0, '2026-07-20T12:20:00', 0, 1, 1, '2026-07-20T12:20:00', N'S', 0, 0, 0, 1, 0, N'CT', 5, 5, N'PRUEBA CONTROL TAXI CV FOLIO 0005'),
    (N'CTTEST-CV-0005-J3', N'CTTEST-CV-0005-J3', '2026-07-20T12:25:00', N'CONTADO', N'A', 125, 0, 125, 0, '2026-07-20T12:25:00', 0, 1, 1, '2026-07-20T12:25:00', N'S', 0, 0, 0, 1, 0, N'CT', 5, 5, N'PRUEBA CONTROL TAXI CV FOLIO 0005');

COMMIT;

SELECT SUM(stotal) AS TotalCompuadmo
FROM compuadmoCasco.dbo.RemisioM
WHERE folio_operacion = 5
  AND folio_remision LIKE N'CTTEST-CV-0005-%';

SELECT SUM(stotal) AS TotalJoyeria
FROM joyeriaCasco.dbo.RemisioM
WHERE folio_operacion = 5
  AND folio_factura LIKE N'CTTEST-CV-0005-%';

/*
    Reversion segura de esta prueba:

    SET XACT_ABORT ON;
    BEGIN TRAN;

    DELETE FROM compuadmoCasco.dbo.RemisioM
    WHERE folio_operacion = 5
      AND folio_remision LIKE N'CTTEST-CV-0005-%'
      AND observaciones = N'PRUEBA CONTROL TAXI CV FOLIO 0005';

    DELETE FROM joyeriaCasco.dbo.RemisioM
    WHERE folio_operacion = 5
      AND folio_factura LIKE N'CTTEST-CV-0005-%'
      AND observaciones = N'PRUEBA CONTROL TAXI CV FOLIO 0005';

    COMMIT;
*/
