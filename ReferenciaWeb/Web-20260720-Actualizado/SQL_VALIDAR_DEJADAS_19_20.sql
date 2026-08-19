USE mkt;
GO

DECLARE @Desde date = '2026-06-18';
DECLARE @Hasta date = '2026-06-20';

IF OBJECT_ID('tempdb..#dejadas_reporte') IS NOT NULL DROP TABLE #dejadas_reporte;

;WITH Base AS
(
    SELECT
        CAST(d.fecha AS date) AS Fecha,
        COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), d.codigorecepcion, d.idstaff, '') AS FolioReporte,
        d.folioregistro,
        d.folioregistrostr,
        d.codigorecepcion,
        d.idstaff,
        d.fecha,
        d.hora,
        d.nombrevendedor,
        d.nombrestaff,
        d.hotel,
        d.nombrealmacen,
        d.tipotransporte,
        d.gafete,
        d.unidad,
        d.telefono,
        CAST(COALESCE(d.total, 0) AS decimal(18,2)) AS Dejada,
        CAST(COALESCE(d.totalventa, 0) AS decimal(18,2)) AS Venta,
        CAST(COALESCE(d.comision, 0) AS decimal(18,2)) AS Comision,
        CAST(COALESCE(d.pago, 0) AS decimal(18,2)) AS Pago,
        CAST(COALESCE(d.pax, 0) AS int) AS Pax,
        ROW_NUMBER() OVER (
            PARTITION BY
                CAST(d.fecha AS date),
                COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), d.codigorecepcion, d.idstaff, ''),
                CAST(COALESCE(d.total, 0) AS decimal(18,2)),
                CAST(COALESCE(d.pax, 0) AS int),
                UPPER(LTRIM(RTRIM(COALESCE(d.nombrestaff, d.nombrevendedor, '')))),
                UPPER(LTRIM(RTRIM(COALESCE(d.hotel, d.nombrealmacen, ''))))
            ORDER BY d.fecha, d.hora, d.codigorecepcion
        ) AS DuplicadoNumero,
        COUNT(*) OVER (
            PARTITION BY
                CAST(d.fecha AS date),
                COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), d.codigorecepcion, d.idstaff, ''),
                CAST(COALESCE(d.total, 0) AS decimal(18,2)),
                CAST(COALESCE(d.pax, 0) AS int),
                UPPER(LTRIM(RTRIM(COALESCE(d.nombrestaff, d.nombrevendedor, '')))),
                UPPER(LTRIM(RTRIM(COALESCE(d.hotel, d.nombrealmacen, ''))))
        ) AS Duplicados
    FROM dbo.dejadas d
    WHERE d.fecha >= @Desde
      AND d.fecha < DATEADD(day, 1, @Hasta)
),
Marcado AS
(
    SELECT *,
        CASE
            WHEN UPPER(COALESCE(nombrevendedor, '')) LIKE '%PRUEB%'
              OR UPPER(COALESCE(nombrestaff, '')) LIKE '%PRUEB%'
              OR UPPER(COALESCE(hotel, '')) LIKE '%PRUEB%'
              OR UPPER(COALESCE(nombrealmacen, '')) LIKE '%PRUEB%'
              OR UPPER(COALESCE(tipotransporte, '')) LIKE '%PRUEB%'
                THEN 'EXCLUIR_PRUEBA'
            WHEN Dejada IN (1, 5, 10, 20)
                THEN 'REVISAR_MONTO_BAJO'
            WHEN (
                    UPPER(COALESCE(tipotransporte, '')) LIKE '%MAJESTIC%'
                 OR UPPER(COALESCE(tipotransporte, '')) LIKE '%MAESTIC%'
                 OR UPPER(COALESCE(nombrevendedor, '')) LIKE '%GUIA:%'
                 )
              AND Dejada <= 1
                THEN 'EXCLUIR_MONTO_TECNICO'
            WHEN Dejada = 100
              AND UPPER(LTRIM(RTRIM(COALESCE(hotel, '')))) = 'ZONA HOTELERA'
                THEN 'EXCLUIR_ZONA_HOTELERA_100'
            WHEN Duplicados > 1 AND DuplicadoNumero > 1
                THEN 'EXCLUIR_DUPLICADO'
            ELSE 'CONTAR'
        END AS Revision
    FROM Base
)
SELECT *
INTO #dejadas_reporte
FROM Marcado;

SELECT
    Fecha,
    Revision,
    COUNT(*) AS Registros,
    SUM(Dejada) AS TotalDejada,
    SUM(Pax) AS TotalPax
FROM #dejadas_reporte
GROUP BY Fecha, Revision
ORDER BY Fecha, Revision;

SELECT
    Fecha,
    COUNT(*) AS RegistrosContados,
    SUM(Dejada) AS TotalDejadaContada,
    SUM(Pax) AS TotalPaxContado
FROM #dejadas_reporte
WHERE Revision = 'CONTAR'
GROUP BY Fecha
ORDER BY Fecha;

SELECT
    'VALIDACION_OBJETIVO' AS Nota,
    Fecha,
    SUM(CASE WHEN Revision = 'CONTAR' THEN Dejada ELSE 0 END) AS TotalQueDebeSalir,
    CASE
        WHEN Fecha = '2026-06-18' THEN 20100
        WHEN Fecha = '2026-06-19' THEN 30300
        WHEN Fecha = '2026-06-20' THEN 15200
        ELSE NULL
    END AS TotalEsperado,
    SUM(CASE WHEN Revision = 'CONTAR' THEN Pax ELSE 0 END) AS PaxQueDebeSalir,
    CASE WHEN Fecha = '2026-06-19' THEN 346 ELSE NULL END AS PaxEsperado
FROM #dejadas_reporte
GROUP BY Fecha
ORDER BY Fecha;

SELECT
    'DUPLICADOS_REVISAR' AS Nota,
    Fecha,
    FolioReporte,
    Duplicados,
    DuplicadoNumero,
    CONVERT(varchar(19), fecha, 120) AS FechaSql,
    CONVERT(varchar(30), hora, 121) AS HoraSql,
    nombrevendedor,
    nombrestaff,
    hotel,
    tipotransporte,
    gafete,
    unidad,
    Dejada,
    Pax,
    Revision
FROM #dejadas_reporte
WHERE Duplicados > 1
ORDER BY Fecha, FolioReporte, DuplicadoNumero;

SELECT
    'PRUEBAS_O_MONTOS_BAJOS_REVISAR' AS Nota,
    Fecha,
    FolioReporte,
    CONVERT(varchar(19), fecha, 120) AS FechaSql,
    CONVERT(varchar(30), hora, 121) AS HoraSql,
    nombrevendedor,
    nombrestaff,
    hotel,
    tipotransporte,
    gafete,
    unidad,
    Dejada,
    Pax,
    Revision
FROM #dejadas_reporte
WHERE Revision <> 'CONTAR'
   OR Dejada IN (1, 5, 10, 20)
ORDER BY Fecha, Revision, FolioReporte;

/*
-- OPCIONAL: si despues de revisar quieres marcar duplicados/pruebas para que NO cuenten,
-- NO lo ejecutes sin revisar la lista de arriba.
-- Este bloque no borra; deja el importe en 0 solo para filas claramente excluidas.

BEGIN TRAN;

UPDATE d
SET total = 0,
    totalefectivo = 0,
    totaltarjeta = 0,
    pago = 0,
    comision = 0
FROM dbo.dejadas d
INNER JOIN #dejadas_reporte r
    ON r.Fecha = CAST(d.fecha AS date)
   AND r.folioregistro = d.folioregistro
   AND COALESCE(r.codigorecepcion, '') = COALESCE(d.codigorecepcion, '')
   AND r.Revision IN ('EXCLUIR_PRUEBA', 'EXCLUIR_MONTO_TECNICO', 'EXCLUIR_ZONA_HOTELERA_100', 'EXCLUIR_DUPLICADO');

-- Revisa @@ROWCOUNT y si todo esta bien:
-- COMMIT;
-- Si no:
ROLLBACK;
*/
