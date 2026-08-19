/* Ejecutar en la base mkt antes de activar UNIQUE si hay dudas.
   Llaves recomendadas:
   - AppMovilRegistro: folio_app y folio_app_original.
   - dejadas: folioregistrostr + gafete + fecha.
   - gafete: folioperacion + gafete + fecha para movimientos A de app.
   - AppMovilRegistroGafetes: FolioApp + FolioGafete.
*/

SET NOCOUNT ON;

PRINT 'Duplicados AppMovilRegistro por folio_app';
SELECT folio_app, COUNT(*) AS repetidos
FROM dbo.AppMovilRegistro
WHERE NULLIF(LTRIM(RTRIM(folio_app)), '') IS NOT NULL
GROUP BY folio_app
HAVING COUNT(*) > 1
ORDER BY repetidos DESC, folio_app;

PRINT 'Duplicados AppMovilRegistro por folio_app_original';
SELECT folio_app_original, COUNT(*) AS repetidos
FROM dbo.AppMovilRegistro
WHERE NULLIF(LTRIM(RTRIM(folio_app_original)), '') IS NOT NULL
GROUP BY folio_app_original
HAVING COUNT(*) > 1
ORDER BY repetidos DESC, folio_app_original;

PRINT 'Duplicados dejadas por folio + gafete + fecha';
SELECT folioregistrostr, gafete, CONVERT(date, fecha) AS fecha, COUNT(*) AS repetidos
FROM dbo.dejadas
WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioregistrostr, ''))), '') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(COALESCE(gafete, ''))), '') IS NOT NULL
GROUP BY folioregistrostr, gafete, CONVERT(date, fecha)
HAVING COUNT(*) > 1
ORDER BY repetidos DESC, fecha DESC, folioregistrostr, gafete;

PRINT 'Duplicados gafete por folio + gafete + fecha en venta A';
SELECT folioperacion, gafete, CONVERT(date, fecha) AS fecha, COUNT(*) AS repetidos
FROM dbo.gafete
WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioperacion, ''))), '') IS NOT NULL
  AND UPPER(LTRIM(RTRIM(COALESCE(venta, '')))) = 'A'
GROUP BY folioperacion, gafete, CONVERT(date, fecha)
HAVING COUNT(*) > 1
ORDER BY repetidos DESC, fecha DESC, folioperacion, gafete;

PRINT 'Creando UNIQUE cuando no existan duplicados...';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistro') AND name = 'UX_AppMovilRegistro_FolioApp')
   AND NOT EXISTS (
        SELECT folio_app
        FROM dbo.AppMovilRegistro
        WHERE NULLIF(LTRIM(RTRIM(folio_app)), '') IS NOT NULL
        GROUP BY folio_app
        HAVING COUNT(*) > 1
   )
    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioApp ON dbo.AppMovilRegistro(folio_app)
    WHERE folio_app IS NOT NULL AND folio_app <> '';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistro') AND name = 'UX_AppMovilRegistro_FolioOriginal')
   AND NOT EXISTS (
        SELECT folio_app_original
        FROM dbo.AppMovilRegistro
        WHERE NULLIF(LTRIM(RTRIM(folio_app_original)), '') IS NOT NULL
        GROUP BY folio_app_original
        HAVING COUNT(*) > 1
   )
    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioOriginal ON dbo.AppMovilRegistro(folio_app_original)
    WHERE folio_app_original IS NOT NULL AND folio_app_original <> '';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.dejadas') AND name = 'UX_dejadas_AppMovil_Folio_Gafete_Fecha')
   AND NOT EXISTS (
        SELECT folioregistrostr, gafete, CONVERT(date, fecha)
        FROM dbo.dejadas
        WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioregistrostr, ''))), '') IS NOT NULL
          AND NULLIF(LTRIM(RTRIM(COALESCE(gafete, ''))), '') IS NOT NULL
        GROUP BY folioregistrostr, gafete, CONVERT(date, fecha)
        HAVING COUNT(*) > 1
   )
    CREATE UNIQUE INDEX UX_dejadas_AppMovil_Folio_Gafete_Fecha
    ON dbo.dejadas(folioregistrostr, gafete, fecha)
    WHERE folioregistrostr IS NOT NULL AND gafete IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gafete') AND name = 'UX_gafete_AppMovil_Folio_Gafete_Fecha_A')
   AND NOT EXISTS (
        SELECT folioperacion, gafete, CONVERT(date, fecha)
        FROM dbo.gafete
        WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioperacion, ''))), '') IS NOT NULL
          AND UPPER(LTRIM(RTRIM(COALESCE(venta, '')))) = 'A'
        GROUP BY folioperacion, gafete, CONVERT(date, fecha)
        HAVING COUNT(*) > 1
   )
    CREATE UNIQUE INDEX UX_gafete_AppMovil_Folio_Gafete_Fecha_A
    ON dbo.gafete(folioperacion, gafete, fecha)
    WHERE folioperacion IS NOT NULL AND venta = 'A';

PRINT 'Listo. Si alguna lista de duplicados salio con filas, limpia esas filas antes de esperar que se cree su UNIQUE.';
