/*
  Ventas de prueba para validar comisiones de Casco Viejo.
  Ambiente autorizado: REYNA / compuadmoCasco / joyeriaCasco.
  No ejecutar en produccion.
*/

SET NOCOUNT ON;

DECLARE @fecha smalldatetime = '2026-07-28';

IF DB_NAME() NOT IN (N'compuadmoCasco', N'joyeriaCasco')
BEGIN
    RAISERROR('Ejecutar este script por secciones en compuadmoCasco y joyeriaCasco solamente.', 16, 1);
    RETURN;
END;

