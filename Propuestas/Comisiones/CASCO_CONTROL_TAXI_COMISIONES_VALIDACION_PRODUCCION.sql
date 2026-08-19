/*
    PRODUCCION CASCO VIEJO - mkt.dbo.ControlTaxiComisiones
    Ejecutar manualmente en la base mkt del servidor de Casco.
    No activa reglas por defecto; la semilla deja Activo = 0.
*/
SELECT
    Id,
    BranchCode,
    Proveedor,
    TipoServicio,
    ConTarjeta,
    VentaMinima,
    VentaMaxima,
    ComisionAgencia,
    ComisionTaxista,
    ComisionVendedor,
    ComisionDeportiva,
    AplicaDegustacion,
    AplicaGasto,
    Activo,
    ReglaNombre,
    OrigenExcel,
    RequiereValidacion,
    Observaciones,
    FechaActualizacion
FROM dbo.ControlTaxiComisiones
WHERE BranchCode = N'CV'
ORDER BY Proveedor, TipoServicio, ConTarjeta DESC, VentaMinima;
GO

SELECT
    BranchCode,
    Proveedor,
    TipoServicio,
    ConTarjeta,
    VentaMinima,
    VentaMaxima,
    COUNT(*) AS Duplicados
FROM dbo.ControlTaxiComisiones
WHERE BranchCode = N'CV'
GROUP BY
    BranchCode,
    Proveedor,
    TipoServicio,
    ConTarjeta,
    VentaMinima,
    VentaMaxima
HAVING COUNT(*) > 1;
GO

SELECT
    a.Id AS ReglaA,
    b.Id AS ReglaB,
    a.Proveedor,
    a.TipoServicio,
    a.ConTarjeta,
    a.VentaMinima AS DesdeA,
    a.VentaMaxima AS HastaA,
    b.VentaMinima AS DesdeB,
    b.VentaMaxima AS HastaB
FROM dbo.ControlTaxiComisiones a
INNER JOIN dbo.ControlTaxiComisiones b
    ON a.BranchCode = b.BranchCode
   AND a.Proveedor = b.Proveedor
   AND a.TipoServicio = b.TipoServicio
   AND a.ConTarjeta = b.ConTarjeta
   AND a.Id < b.Id
WHERE a.BranchCode = N'CV'
  AND ISNULL(a.VentaMaxima, 999999999.99) >= b.VentaMinima
  AND ISNULL(b.VentaMaxima, 999999999.99) >= a.VentaMinima;
GO

SELECT
    COUNT(*) AS TotalReglas,
    SUM(CASE WHEN RequiereValidacion = 1 THEN 1 ELSE 0 END) AS ReglasPendientes,
    SUM(CASE WHEN RequiereValidacion = 0 THEN 1 ELSE 0 END) AS ReglasClaras,
    SUM(CASE WHEN Activo = 1 THEN 1 ELSE 0 END) AS ReglasActivas,
    SUM(CASE WHEN Activo = 0 THEN 1 ELSE 0 END) AS ReglasInactivas
FROM dbo.ControlTaxiComisiones
WHERE BranchCode = N'CV';
GO

