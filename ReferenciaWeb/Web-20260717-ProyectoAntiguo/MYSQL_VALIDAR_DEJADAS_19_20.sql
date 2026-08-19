-- Hostinger / MySQL: validacion de mkt2_trip_records para reportes.
-- No borra ni actualiza datos.

SET @desde = '2026-06-18';
SET @hasta = '2026-06-20';

DROP TEMPORARY TABLE IF EXISTS tmp_dejadas_reporte;

CREATE TEMPORARY TABLE tmp_dejadas_reporte AS
SELECT
    DATE(recordDate) AS Fecha,
    recordId,
    recordDate,
    driverName,
    hotel,
    origin,
    site,
    destination,
    serviceType,
    badgeId,
    unitNumber,
    CAST(tripCost AS DECIMAL(18,2)) AS Dejada,
    CAST(passengerCount AS SIGNED) AS Pax,
    payoutStatus,
    ROW_NUMBER() OVER (
        PARTITION BY
            DATE(recordDate),
            recordId,
            CAST(tripCost AS DECIMAL(18,2)),
            CAST(passengerCount AS SIGNED),
            UPPER(TRIM(COALESCE(driverName, ''))),
            UPPER(TRIM(COALESCE(hotel, origin, '')))
        ORDER BY recordDate, recordId
    ) AS DuplicadoNumero,
    COUNT(*) OVER (
        PARTITION BY
            DATE(recordDate),
            recordId,
            CAST(tripCost AS DECIMAL(18,2)),
            CAST(passengerCount AS SIGNED),
            UPPER(TRIM(COALESCE(driverName, ''))),
            UPPER(TRIM(COALESCE(hotel, origin, '')))
    ) AS Duplicados
FROM mkt2_trip_records
WHERE DATE(recordDate) BETWEEN @desde AND @hasta
  AND (
        assignedBranchCode = '28'
     OR TRIM(COALESCE(assignedBranchCode, '')) = ''
     OR UPPER(COALESCE(site, '')) LIKE '%PLAZA 28%'
     OR UPPER(COALESCE(assignedBranch, '')) LIKE '%PLAZA 28%'
  );

SELECT
    Fecha,
    CASE
        WHEN UPPER(COALESCE(driverName, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(hotel, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(origin, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(serviceType, '')) LIKE '%PRUEB%'
            THEN 'EXCLUIR_PRUEBA'
        WHEN Dejada IN (1, 5, 10, 20)
            THEN 'REVISAR_MONTO_BAJO'
        WHEN (
                UPPER(COALESCE(serviceType, '')) LIKE '%MAJESTIC%'
             OR UPPER(COALESCE(serviceType, '')) LIKE '%MAESTIC%'
             OR UPPER(COALESCE(driverName, '')) LIKE '%GUIA:%'
             )
          AND Dejada <= 1
            THEN 'EXCLUIR_MONTO_TECNICO'
        WHEN Dejada = 100
          AND UPPER(TRIM(COALESCE(hotel, ''))) = 'ZONA HOTELERA'
            THEN 'EXCLUIR_ZONA_HOTELERA_100'
        WHEN Duplicados > 1 AND DuplicadoNumero > 1
            THEN 'EXCLUIR_DUPLICADO'
        ELSE 'CONTAR'
    END AS Revision,
    COUNT(*) AS Registros,
    SUM(Dejada) AS TotalDejada,
    SUM(Pax) AS TotalPax
FROM tmp_dejadas_reporte
GROUP BY Fecha, Revision
ORDER BY Fecha, Revision;

SELECT
    Fecha,
    SUM(CASE
        WHEN UPPER(COALESCE(driverName, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(hotel, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(origin, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(serviceType, '')) LIKE '%PRUEB%'
          OR Dejada IN (1, 5, 10, 20)
          OR (Duplicados > 1 AND DuplicadoNumero > 1)
            THEN 0
        WHEN (
                UPPER(COALESCE(serviceType, '')) LIKE '%MAJESTIC%'
             OR UPPER(COALESCE(serviceType, '')) LIKE '%MAESTIC%'
             OR UPPER(COALESCE(driverName, '')) LIKE '%GUIA:%'
             )
          AND Dejada <= 1
            THEN 0
        WHEN Dejada = 100 AND UPPER(TRIM(COALESCE(hotel, ''))) = 'ZONA HOTELERA'
            THEN 0
        ELSE Dejada
    END) AS TotalQueDebeSalir,
    CASE
        WHEN Fecha = '2026-06-18' THEN 20100
        WHEN Fecha = '2026-06-19' THEN 30300
        WHEN Fecha = '2026-06-20' THEN 15200
        ELSE NULL
    END AS TotalEsperado,
    SUM(CASE
        WHEN UPPER(COALESCE(driverName, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(hotel, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(origin, '')) LIKE '%PRUEB%'
          OR UPPER(COALESCE(serviceType, '')) LIKE '%PRUEB%'
          OR Dejada IN (1, 5, 10, 20)
          OR (Duplicados > 1 AND DuplicadoNumero > 1)
            THEN 0
        ELSE Pax
    END) AS PaxQueDebeSalir,
    CASE WHEN Fecha = '2026-06-19' THEN 346 ELSE NULL END AS PaxEsperado
FROM tmp_dejadas_reporte
GROUP BY Fecha
ORDER BY Fecha;

SELECT
    'DUPLICADOS_REVISAR' AS Nota,
    Fecha,
    recordId,
    Duplicados,
    DuplicadoNumero,
    recordDate,
    driverName,
    hotel,
    origin,
    serviceType,
    badgeId,
    unitNumber,
    Dejada,
    Pax,
    payoutStatus
FROM tmp_dejadas_reporte
WHERE Duplicados > 1
ORDER BY Fecha, recordId, DuplicadoNumero;

SELECT
    'PRUEBAS_O_MONTOS_BAJOS_REVISAR' AS Nota,
    Fecha,
    recordId,
    recordDate,
    driverName,
    hotel,
    origin,
    serviceType,
    badgeId,
    unitNumber,
    Dejada,
    Pax,
    payoutStatus
FROM tmp_dejadas_reporte
WHERE UPPER(COALESCE(driverName, '')) LIKE '%PRUEB%'
   OR UPPER(COALESCE(hotel, '')) LIKE '%PRUEB%'
   OR UPPER(COALESCE(origin, '')) LIKE '%PRUEB%'
   OR UPPER(COALESCE(serviceType, '')) LIKE '%PRUEB%'
   OR Dejada IN (1, 5, 10, 20)
   OR (Duplicados > 1 AND DuplicadoNumero > 1)
ORDER BY Fecha, recordDate, recordId;
