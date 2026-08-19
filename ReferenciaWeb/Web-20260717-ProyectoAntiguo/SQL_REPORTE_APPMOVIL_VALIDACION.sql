USE mkt2;
GO

DECLARE @Desde date = '2026-06-18';
DECLARE @Hasta date = '2026-06-20';

IF OBJECT_ID('tempdb..#ReporteAppMovil') IS NOT NULL DROP TABLE #ReporteAppMovil;

;WITH Reporte AS
(
    SELECT
        CAST(a.fecha_operacion AS date) AS Fecha,
        a.folio_app,
        a.folio_app_original,
        a.vendedor_nombre,
        a.hotel,
        a.tipo_operacion,
        a.folio_gafete,
        a.unidad,
        CAST(COALESCE(a.pax, 0) AS int) AS Pax,
        CAST(COALESCE(a.total, 0) AS decimal(18,2)) AS Dejada,
        CASE
            WHEN UPPER(COALESCE(a.vendedor_nombre, '') + ' ' + COALESCE(a.hotel, '') + ' ' + COALESCE(a.notas, '')) LIKE '%PRUEB%'
              OR UPPER(COALESCE(a.vendedor_nombre, '') + ' ' + COALESCE(a.hotel, '') + ' ' + COALESCE(a.notas, '')) LIKE '%TEST%'
                THEN 'EXCLUIR_PRUEBA'
            WHEN COALESCE(a.total, 0) = 1
                THEN 'EXCLUIR_MONTO_TECNICO'
            WHEN COALESCE(a.total, 0) = 100
              AND UPPER(LTRIM(RTRIM(COALESCE(a.hotel, '')))) = 'ZONA HOTELERA'
                THEN 'EXCLUIR_ZONA_HOTELERA_100'
            ELSE 'CONTAR'
        END AS Revision
    FROM dbo.AppMovilRegistro a
    WHERE a.fecha_operacion >= @Desde
      AND a.fecha_operacion < DATEADD(day, 1, @Hasta)
)
SELECT *
INTO #ReporteAppMovil
FROM Reporte;

SELECT
    Fecha,
    Revision,
    COUNT(*) AS Registros,
    SUM(Dejada) AS TotalDejada,
    SUM(Pax) AS TotalPax
FROM #ReporteAppMovil
GROUP BY Fecha, Revision
ORDER BY Fecha, Revision;

SELECT
    Fecha,
    SUM(CASE WHEN Revision = 'CONTAR' THEN Dejada ELSE 0 END) AS TotalQueSaleEnReporte,
    SUM(CASE WHEN Revision = 'CONTAR' THEN Pax ELSE 0 END) AS PaxQueSaleEnReporte,
    CASE
        WHEN Fecha = '2026-06-18' THEN 20100
        WHEN Fecha = '2026-06-19' THEN 30300
        WHEN Fecha = '2026-06-20' THEN 15200
    END AS TotalEsperado,
    SUM(CASE WHEN Revision = 'CONTAR' THEN Dejada ELSE 0 END)
      - CASE
            WHEN Fecha = '2026-06-18' THEN 20100
            WHEN Fecha = '2026-06-19' THEN 30300
            WHEN Fecha = '2026-06-20' THEN 15200
        END AS Diferencia
FROM #ReporteAppMovil
GROUP BY Fecha
ORDER BY Fecha;

SELECT
    Fecha,
    folio_app,
    vendedor_nombre,
    hotel,
    tipo_operacion,
    folio_gafete,
    unidad,
    Pax,
    Dejada,
    Revision
FROM #ReporteAppMovil
WHERE Revision <> 'CONTAR'
ORDER BY Fecha, Revision, folio_app;
