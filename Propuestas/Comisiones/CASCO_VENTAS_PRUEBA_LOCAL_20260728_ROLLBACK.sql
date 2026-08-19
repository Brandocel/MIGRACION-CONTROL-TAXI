/*
  Borra solamente ventas/pagos de prueba PRUEBA-CV creados para validar comisiones locales.
  Ejecutar solo en REYNA sobre compuadmoCasco y joyeriaCasco.
*/

USE [compuadmoCasco];
GO
DELETE FROM dbo.pagosM WHERE folio_factura LIKE N'PRUEBA-CV-%' OR referencia IN (N'PRUEBA_COMISION_CV', N'PRUEBA_CV');
DELETE FROM dbo.RemisioM WHERE folio_remision LIKE N'PRUEBA-CV-%' OR observaciones LIKE N'PRUEBA_COMISION_CV%';
GO

USE [joyeriaCasco];
GO
DELETE FROM dbo.pagosM WHERE folio_factura LIKE N'PRUEBA-CV-%' OR referencia IN (N'PRUEBA_COMISION_CV', N'PRUEBA_CV');
DELETE FROM dbo.RemisioM WHERE folio_factura LIKE N'PRUEBA-CV-%' OR observaciones LIKE N'PRUEBA_COMISION_CV%';
GO
