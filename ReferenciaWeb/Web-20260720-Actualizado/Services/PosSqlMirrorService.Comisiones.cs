using System.Data;
using System.Data.Common;
using System.Globalization;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        public async Task<PosComisionesViewModel?> TryGetComisionesAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            try
            {
                var inicio = fechaInicio?.Date;
                var fin = fechaFin?.Date;
                var setupConnection = _dbContext.Database.GetDbConnection();
                if (setupConnection.State != ConnectionState.Open)
                    await setupConnection.OpenAsync();
                await EnsurePosOperacionBeneficiariosTableAsync(setupConnection);
                await EnsureRelacionesTicketTaxistaTableAsync(setupConnection);
                await EnsureAppMovilComisionesColumnsAsync(setupConnection);
                await EnsureComisionPagosControlTableAsync(setupConnection);

                var normalized = Normalize(folioOperacion);
                var requestedFolio = normalized;
                if (normalized != null)
                    normalized = await ResolveFolioOperacionFromRelacionAsync(setupConnection, normalized) ?? normalized;
                _ = SyncAppMovilRegistrosFromApiAsync(requestedFolio ?? normalized, inicio, fin);
                var rows = new List<ComisionEfRow>();
                try
                {
                    rows = await GetComisionRowsFromEfAsync(normalized, inicio, fin);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No fue posible cargar comisiones POS enriquecidas; se intentara cargar POS basico.");
                    rows = await GetComisionRowsFromPosBasicAsync(normalized, inicio, fin);
                }
                if (rows.Count == 0)
                    rows = await GetComisionRowsFromPosBasicAsync(normalized, inicio, fin);
                var comisiones = rows.Select(row =>
                {
                    var transporte = FirstText(row.TransporteNombre, row.TransporteTipo);
                    var staff = row.Staff;
                    var guiaMatricula = row.GuiaMatricula;
                    var taxistaId = row.TaxistaId;
                    var taxistaNombre = row.TaxistaNombre;
                    var beneficiario = taxistaId > 0
                        ? $"{taxistaId} {taxistaNombre}".Trim()
                        : guiaMatricula > 0
                            ? $"GUIA {guiaMatricula}"
                            : (!string.IsNullOrWhiteSpace(transporte)
                                ? transporte
                                : (!string.IsNullOrWhiteSpace(staff) ? staff : "SIN BENEFICIARIO"));
                    var tipo = taxistaId > 0
                        ? "TAXISTA"
                        : guiaMatricula > 0
                            ? "GUIA"
                            : (!string.IsNullOrWhiteSpace(transporte)
                                ? "TRANSPORTE"
                                : (!string.IsNullOrWhiteSpace(staff) ? "STAFF" : "PENDIENTE"));
                    var pago = row.Pago;
                    var importe = row.ImporteComision;

                    return new PosComisionRowViewModel
                    {
                        Folio = row.Folio,
                        Ticket = row.TicketApp,
                        Fecha = FormatDate(row.Fecha),
                        Hotel = row.Hotel,
                        Pax = row.Pax,
                        BeneficiarioTipo = tipo,
                        Beneficiario = beneficiario,
                        Staff = staff,
                        Unidad = row.TransporteTipo,
                        NumeroUnidad = beneficiario,
                        FormaPago = row.FormaPago,
                        Vendedor = staff,
                        Transporte = row.TransporteTipo,
                        Venta = row.BaseComision,
                        VentaArtesania = row.Artesania,
                        VentaFarmacia = row.Farmacia,
                        VentaTienda = row.Compra,
                        VentaJoyeria = row.Joyeria,
                        Compra = row.Compra,
                        Joyeria = row.Joyeria,
                        Base = row.BaseComision,
                        Porcentaje = row.Porcentaje,
                        DescuentoAplicado = row.Descuento,
                        DeduccionDejada = row.Dejada,
                        DeduccionBebidas = row.Bebidas,
                        DeduccionDegustacion = row.Degustacion,
                        Importe = importe,
                        Pago = pago,
                        FechaPago = FormatDate(row.FechaPago),
                        Estatus = ResolveCommissionStatus(importe, pago)
                    };
                }).ToList();
                comisiones.AddRange(await GetComisionRowsFromDejadasSqlAsync(normalized, inicio, fin));
                if (!string.Equals(requestedFolio, normalized, StringComparison.OrdinalIgnoreCase))
                    comisiones.AddRange(await GetComisionRowsFromDejadasSqlAsync(requestedFolio, inicio, fin));
                comisiones.AddRange(await GetComisionRowsFromAppMovilSqlAsync(requestedFolio ?? normalized, inicio, fin));
                if (!string.Equals(requestedFolio, normalized, StringComparison.OrdinalIgnoreCase))
                    comisiones.AddRange(await GetComisionRowsFromAppMovilSqlAsync(normalized, inicio, fin));
                comisiones = ConsolidateDuplicateTicketCommissions(comisiones)
                    .OrderByDescending(x => TryParseComisionDate(x.Fecha) ?? DateTime.MinValue)
                    .ThenByDescending(x => x.Folio)
                    .ToList();
                if (comisiones.Count == 0)
                {
                    comisiones = await GetComisionRowsFromApiAsync(normalized, inicio, fin);
                }
                await ApplyAuthoritativeTransportRulesAsync(comisiones);
                NormalizeComisionPercentages(comisiones);
                await ApplyOperationalExpenseRulesAsync(comisiones);
                ApplyLargestSaleDejadaRule(comisiones);
                RecalculateCommissionsFromExcelRules(comisiones);
                await ApplyTicketPaymentBreakdownRulesAsync(comisiones);
                await ApplyComisionPagosControlAsync(comisiones);
                NormalizeComisionPayments(comisiones);
                comisiones = FilterComisionesForExactFolio(comisiones, requestedFolio ?? normalized);

                return new PosComisionesViewModel
                {
                    FolioOperacion = requestedFolio ?? normalized ?? string.Empty,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    TotalVenta = comisiones.Sum(x => x.Venta),
                    TotalBase = comisiones.Sum(x => x.Base),
                    TotalComision = comisiones.Sum(x => x.Importe),
                    TotalPagado = comisiones.Sum(x => x.Pago),
                    Comisiones = comisiones
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Comisiones desde POS.");
                var normalized = Normalize(folioOperacion);
                var comisiones = await GetComisionRowsFromApiAsync(normalized, fechaInicio?.Date, fechaFin?.Date);
                NormalizeComisionPercentages(comisiones);
                return new PosComisionesViewModel
                {
                    FolioOperacion = normalized ?? string.Empty,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    TotalVenta = comisiones.Sum(x => x.Venta),
                    TotalBase = comisiones.Sum(x => x.Base),
                    TotalComision = comisiones.Sum(x => x.Importe),
                    TotalPagado = comisiones.Sum(x => x.Pago),
                    Comisiones = comisiones
                };
            }
        }

        private async Task<List<PosComisionRowViewModel>> GetComisionRowsFromApiAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                var apiRows = await _appTaxiApi.GetTripRecordsAsync(normalized, fechaInicio, fechaFin);
                var transporteMap = await GetTransportCatalogMapAsync();
                return apiRows.Select(row =>
                {
                    var transporte = ResolveTransportFromCatalog(transporteMap, row.TipoOperacion);
                    var porcentaje = ToDecimal(transporte?.Comision);
                    return new PosComisionRowViewModel
                    {
                        Folio = row.FolioControl,
                        Ticket = row.Ticket,
                        Fecha = row.Hora,
                        Hotel = row.Hotel,
                        Pax = row.Pax,
                        BeneficiarioTipo = "TAXISTA",
                        Beneficiario = FirstText(row.Gafete, row.Vendedor),
                        Staff = row.Vendedor,
                        Unidad = row.TipoOperacion,
                        NumeroUnidad = FirstText(row.Placas, row.Unidad),
                        FormaPago = row.Tarjeta > 0m ? "T/C" : "MXN",
                        Vendedor = row.Vendedor,
                        Transporte = row.TipoOperacion,
                        Venta = 0m,
                        Base = 0m,
                        Porcentaje = porcentaje,
                        Importe = 0m,
                        Pago = 0m,
                        Estatus = "SIN CALCULAR"
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar comisiones desde la API APP_TAXI.");
                return new List<PosComisionRowViewModel>();
            }
        }

        private async Task<List<PosComisionRowViewModel>> GetComisionRowsFromAppMovilSqlAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var normalizedNumber = long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNormalized)
                ? parsedNormalized.ToString(CultureInfo.InvariantCulture)
                : null;
            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (10000)
    Folio,
    Ticket,
    Fecha,
    Taxista,
    Transporte,
    Hotel,
    Gafete,
    Unidad,
    NumeroUnidad,
    Pax,
    FormaPago,
    VentaArtesania,
    VentaFarmacia,
    VentaTienda,
    VentaJoyeria,
    Base,
    Porcentaje,
    Descuento,
    Dejada,
    Importe,
    Pago,
    FechaPago
FROM
(
    SELECT
        COALESCE(NULLIF(r.FolioOperacion, ''), v.id_registro, a.folio_app_original, a.folio_app, '') AS Folio,
        COALESCE(NULLIF(r.FolioPos, ''), NULLIF(a.folio_pos, ''), NULLIF(StoreVenta.Ticket, ''), '') AS Ticket,
        v.fecha_registro AS Fecha,
        COALESCE(v.nombre_taxista, a.vendedor_nombre, '') AS Taxista,
        COALESCE(v.tipo_servicio, a.tipo_operacion, '') AS Transporte,
        COALESCE(v.hotel, a.hotel, '') AS Hotel,
        COALESCE(v.folio_gafete, a.folio_gafete, '') AS Gafete,
        COALESCE(v.unidad, a.unidad, '') AS Unidad,
        COALESCE(v.placas, a.placas, '') AS NumeroUnidad,
        COALESCE(v.numero_personas, a.pax, 0) AS Pax,
        CASE WHEN COALESCE(a.tarjeta, 0) > 0 THEN 'T/C' ELSE 'MXN' END AS FormaPago,
        CAST(0 AS decimal(18,2)) AS VentaArtesania,
        CAST(0 AS decimal(18,2)) AS VentaFarmacia,
        COALESCE(StoreVenta.VentaTienda, 0) AS VentaTienda,
        COALESCE(StoreVenta.VentaJoyeria, 0) AS VentaJoyeria,
        COALESCE(StoreVenta.TotalVenta, 0) AS Base,
        CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS Porcentaje,
        CASE WHEN COALESCE(a.tarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS Descuento,
        COALESCE(a.total, 0) AS Dejada,
        Calc.Importe AS Importe,
        COALESCE(a.pago_comision, 0) AS Pago,
        a.fecha_pago_comision AS FechaPago,
        v.fecha_registro AS SourceDate
    FROM {PosTable("vw_AppMovilRegistrosViajes")} v
    LEFT JOIN {PosTable("AppMovilRegistro")} a
        ON a.folio_app = v.id_registro OR a.folio_app_original = v.id_registro
    LEFT JOIN {PosTable("RelacionTicketTaxista")} r
        ON r.FolioApp = v.id_registro
        OR r.FolioApp = a.folio_app
        OR r.FolioApp = a.folio_app_original
        OR r.FolioOperacion = v.id_registro
        OR r.FolioOperacion = a.folio_app
        OR r.FolioOperacion = a.folio_app_original
    LEFT JOIN {PosTable("transporte")} t
        ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(COALESCE(v.tipo_servicio, a.tipo_operacion, ''))))
        OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(COALESCE(v.tipo_servicio, a.tipo_operacion, ''))))
    OUTER APPLY
    (
        SELECT TOP (1)
            StoreRows.TotalVenta,
            StoreRows.VentaTienda,
            StoreRows.VentaJoyeria,
            StoreRows.Ticket,
            StoreRows.Fecha
        FROM
        (
            SELECT
                COALESCE(comp.total, 0) AS TotalVenta,
                COALESCE(comp.total, 0) AS VentaTienda,
                CAST(0 AS decimal(18,2)) AS VentaJoyeria,
                CONVERT(nvarchar(80), comp.folio_remision) AS Ticket,
                comp.fecha AS Fecha,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
                     AND CONVERT(nvarchar(80), comp.folio_remision) = LTRIM(RTRIM(a.folio_pos))
                        THEN 0
                    ELSE 1
                END AS MatchRank
            FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] comp
            WHERE NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
              AND CONVERT(nvarchar(80), comp.folio_remision) = LTRIM(RTRIM(a.folio_pos))
              AND UPPER(LTRIM(RTRIM(COALESCE(comp.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
            UNION ALL
            SELECT
                COALESCE(joy.total, 0) AS TotalVenta,
                CAST(0 AS decimal(18,2)) AS VentaTienda,
                COALESCE(joy.total, 0) AS VentaJoyeria,
                CONVERT(nvarchar(80), joy.folio_factura) AS Ticket,
                joy.fecha AS Fecha,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
                     AND CONVERT(nvarchar(80), joy.folio_factura) = LTRIM(RTRIM(a.folio_pos))
                        THEN 0
                    ELSE 1
                END AS MatchRank
            FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] joy
            WHERE NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
              AND CONVERT(nvarchar(80), joy.folio_factura) = LTRIM(RTRIM(a.folio_pos))
              AND UPPER(LTRIM(RTRIM(COALESCE(joy.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
        ) StoreRows
        ORDER BY StoreRows.MatchRank, StoreRows.Fecha DESC
    ) StoreVenta
    CROSS APPLY
    (
        SELECT
            CAST(COALESCE(StoreVenta.TotalVenta, 0) AS decimal(18,4)) AS TotalVenta,
            CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0) ELSE 0 END AS decimal(18,4)) AS ComisionFija,
            CAST(CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS decimal(18,4)) / 100 AS Porcentaje,
            CAST(CASE WHEN COALESCE(a.tarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100 AS Descuento,
            CAST(COALESCE(a.total, 0) AS decimal(18,4)) AS Dejada
    ) CalcBase
    CROSS APPLY
    (
        SELECT CAST(ROUND(
            CASE
                WHEN CalcBase.ComisionFija > 0 THEN CalcBase.ComisionFija
                WHEN CalcBase.Descuento = 0 THEN (CalcBase.TotalVenta - CalcBase.Dejada) * CalcBase.Porcentaje
                ELSE ((CalcBase.TotalVenta - (CalcBase.TotalVenta * CalcBase.Descuento)) - CalcBase.Dejada) * CalcBase.Porcentaje
            END,
            0,
            1
        ) AS decimal(18,2)) AS Importe
    ) Calc
    WHERE (@inicio IS NULL OR CAST(v.fecha_registro AS date) >= @inicio)
      AND (@fin IS NULL OR CAST(v.fecha_registro AS date) <= @fin)
      AND (
            @q IS NULL
         OR v.id_registro LIKE '%' + @q + '%'
         OR COALESCE(a.folio_app, '') LIKE '%' + @q + '%'
         OR COALESCE(a.folio_app_original, '') LIKE '%' + @q + '%'
         OR COALESCE(a.folio_pos, '') LIKE '%' + @q + '%'
         OR (@qNumber IS NOT NULL AND ISNUMERIC(v.id_registro) = 1 AND CONVERT(bigint, v.id_registro) = CONVERT(bigint, @qNumber))
         OR (@qNumber IS NOT NULL AND ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, @qNumber))
         OR (@qNumber IS NOT NULL AND ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, @qNumber))
         OR (@qNumber IS NOT NULL AND ISNUMERIC(COALESCE(a.folio_pos, '')) = 1 AND CONVERT(bigint, a.folio_pos) = CONVERT(bigint, @qNumber))
         OR COALESCE(v.folio_gafete, a.folio_gafete, '') LIKE '%' + @q + '%'
         OR COALESCE(v.nombre_taxista, a.vendedor_nombre, '') LIKE '%' + @q + '%'
         OR COALESCE(v.hotel, a.hotel, '') LIKE '%' + @q + '%'
         OR COALESCE(v.origen, a.origen, '') LIKE '%' + @q + '%'
         OR COALESCE(v.sitio, a.sitio, '') LIKE '%' + @q + '%'
         OR COALESCE(v.destino, a.destino, '') LIKE '%' + @q + '%'
         OR COALESCE(v.tipo_servicio, a.tipo_operacion, '') LIKE '%' + @q + '%'
      )

    UNION ALL

    SELECT
        COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), a.folio_app, '') AS Folio,
        COALESCE(NULLIF(r.FolioPos, ''), NULLIF(a.folio_pos, ''), NULLIF(StoreVenta.Ticket, ''), '') AS Ticket,
        COALESCE(a.fecha_operacion, a.fecha_creacion) AS Fecha,
        COALESCE(a.vendedor_nombre, '') AS Taxista,
        COALESCE(a.tipo_operacion, '') AS Transporte,
        COALESCE(a.hotel, '') AS Hotel,
        COALESCE(a.folio_gafete, '') AS Gafete,
        COALESCE(a.unidad, '') AS Unidad,
        COALESCE(a.placas, '') AS NumeroUnidad,
        COALESCE(a.pax, 0) AS Pax,
        CASE WHEN COALESCE(a.tarjeta, 0) > 0 THEN 'T/C' ELSE 'MXN' END AS FormaPago,
        CAST(0 AS decimal(18,2)) AS VentaArtesania,
        CAST(0 AS decimal(18,2)) AS VentaFarmacia,
        COALESCE(StoreVenta.VentaTienda, 0) AS VentaTienda,
        COALESCE(StoreVenta.VentaJoyeria, 0) AS VentaJoyeria,
        COALESCE(StoreVenta.TotalVenta, 0) AS Base,
        CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS Porcentaje,
        CASE WHEN COALESCE(a.tarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS Descuento,
        COALESCE(a.total, 0) AS Dejada,
        Calc.Importe AS Importe,
        COALESCE(a.pago_comision, 0) AS Pago,
        a.fecha_pago_comision AS FechaPago,
        COALESCE(a.fecha_operacion, a.fecha_creacion) AS SourceDate
    FROM {PosTable("AppMovilRegistro")} a
    LEFT JOIN {PosTable("RelacionTicketTaxista")} r
        ON r.FolioApp = a.folio_app
        OR r.FolioApp = a.folio_app_original
        OR r.FolioOperacion = a.folio_app
        OR r.FolioOperacion = a.folio_app_original
    LEFT JOIN {PosTable("transporte")} t
        ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(a.tipo_operacion)))
        OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(a.tipo_operacion)))
    OUTER APPLY
    (
        SELECT TOP (1)
            StoreRows.TotalVenta,
            StoreRows.VentaTienda,
            StoreRows.VentaJoyeria,
            StoreRows.Ticket,
            StoreRows.Fecha
        FROM
        (
            SELECT
                COALESCE(comp.total, 0) AS TotalVenta,
                COALESCE(comp.total, 0) AS VentaTienda,
                CAST(0 AS decimal(18,2)) AS VentaJoyeria,
                CONVERT(nvarchar(80), comp.folio_remision) AS Ticket,
                comp.fecha AS Fecha,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
                     AND CONVERT(nvarchar(80), comp.folio_remision) = LTRIM(RTRIM(a.folio_pos))
                        THEN 0
                    ELSE 1
                END AS MatchRank
            FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] comp
            WHERE NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
              AND CONVERT(nvarchar(80), comp.folio_remision) = LTRIM(RTRIM(a.folio_pos))
              AND UPPER(LTRIM(RTRIM(COALESCE(comp.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
            UNION ALL
            SELECT
                COALESCE(joy.total, 0) AS TotalVenta,
                CAST(0 AS decimal(18,2)) AS VentaTienda,
                COALESCE(joy.total, 0) AS VentaJoyeria,
                CONVERT(nvarchar(80), joy.folio_factura) AS Ticket,
                joy.fecha AS Fecha,
                CASE
                    WHEN NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
                     AND CONVERT(nvarchar(80), joy.folio_factura) = LTRIM(RTRIM(a.folio_pos))
                        THEN 0
                    ELSE 1
                END AS MatchRank
            FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] joy
            WHERE NULLIF(LTRIM(RTRIM(COALESCE(a.folio_pos, ''))), '') IS NOT NULL
              AND CONVERT(nvarchar(80), joy.folio_factura) = LTRIM(RTRIM(a.folio_pos))
              AND UPPER(LTRIM(RTRIM(COALESCE(joy.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
        ) StoreRows
        ORDER BY StoreRows.MatchRank, StoreRows.Fecha DESC
    ) StoreVenta
    CROSS APPLY
    (
        SELECT
            CAST(COALESCE(StoreVenta.TotalVenta, 0) AS decimal(18,4)) AS TotalVenta,
            CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0) ELSE 0 END AS decimal(18,4)) AS ComisionFija,
            CAST(CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS decimal(18,4)) / 100 AS Porcentaje,
            CAST(CASE WHEN COALESCE(a.tarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100 AS Descuento,
            CAST(COALESCE(a.total, 0) AS decimal(18,4)) AS Dejada
    ) CalcBase
    CROSS APPLY
    (
        SELECT CAST(ROUND(
            CASE
                WHEN CalcBase.ComisionFija > 0 THEN CalcBase.ComisionFija
                WHEN CalcBase.Descuento = 0 THEN (CalcBase.TotalVenta - CalcBase.Dejada) * CalcBase.Porcentaje
                ELSE ((CalcBase.TotalVenta - (CalcBase.TotalVenta * CalcBase.Descuento)) - CalcBase.Dejada) * CalcBase.Porcentaje
            END,
            0,
            1
        ) AS decimal(18,2)) AS Importe
    ) Calc
    WHERE NOT EXISTS (
            SELECT 1
            FROM {PosTable("vw_AppMovilRegistrosViajes")} v2
            WHERE v2.id_registro = a.folio_app OR v2.id_registro = a.folio_app_original
        )
      AND (@inicio IS NULL OR CAST(COALESCE(a.fecha_operacion, a.fecha_creacion) AS date) >= @inicio)
      AND (@fin IS NULL OR CAST(COALESCE(a.fecha_operacion, a.fecha_creacion) AS date) <= @fin)
      AND (
            @q IS NULL
         OR COALESCE(a.folio_app, '') LIKE '%' + @q + '%'
         OR COALESCE(a.folio_app_original, '') LIKE '%' + @q + '%'
         OR COALESCE(a.folio_pos, '') LIKE '%' + @q + '%'
         OR (@qNumber IS NOT NULL AND ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, @qNumber))
         OR (@qNumber IS NOT NULL AND ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, @qNumber))
         OR (@qNumber IS NOT NULL AND ISNUMERIC(COALESCE(a.folio_pos, '')) = 1 AND CONVERT(bigint, a.folio_pos) = CONVERT(bigint, @qNumber))
         OR COALESCE(a.folio_gafete, '') LIKE '%' + @q + '%'
         OR COALESCE(a.vendedor_nombre, '') LIKE '%' + @q + '%'
         OR COALESCE(a.hotel, '') LIKE '%' + @q + '%'
         OR COALESCE(a.origen, '') LIKE '%' + @q + '%'
         OR COALESCE(a.sitio, '') LIKE '%' + @q + '%'
         OR COALESCE(a.destino, '') LIKE '%' + @q + '%'
         OR COALESCE(a.tipo_operacion, '') LIKE '%' + @q + '%'
      )
) AppRows
ORDER BY SourceDate DESC",
                ("@inicio", fechaInicio?.Date),
                ("@fin", fechaFin?.Date),
                ("@q", normalized),
                ("@qNumber", normalizedNumber));

            return rows.Select(row =>
            {
                var baseComision = ToDecimal(PickObject(row, "Base"));
                var porcentaje = ToDecimal(PickObject(row, "Porcentaje"));
                var importe = ToDecimal(PickObject(row, "Importe"));
                var pago = ToDecimal(PickObject(row, "Pago"));
                return new PosComisionRowViewModel
                {
                    Folio = PickText(row, "Folio"),
                    Ticket = PickText(row, "Ticket"),
                    Fecha = PickDate(row, "Fecha"),
                    Hotel = PickText(row, "Hotel"),
                    Pax = PickInt(row, "Pax"),
                    BeneficiarioTipo = "TAXISTA",
                    Beneficiario = FirstText(PickText(row, "Gafete"), PickText(row, "Taxista")),
                    Staff = PickText(row, "Taxista"),
                    Unidad = PickText(row, "Unidad"),
                    NumeroUnidad = PickText(row, "NumeroUnidad"),
                    FormaPago = PickText(row, "FormaPago"),
                    Vendedor = PickText(row, "Taxista"),
                    Transporte = PickText(row, "Transporte"),
                    Venta = baseComision,
                    VentaArtesania = ToDecimal(PickObject(row, "VentaArtesania")),
                    VentaFarmacia = ToDecimal(PickObject(row, "VentaFarmacia")),
                    VentaTienda = ToDecimal(PickObject(row, "VentaTienda")),
                    VentaJoyeria = ToDecimal(PickObject(row, "VentaJoyeria")),
                    Base = baseComision,
                    Porcentaje = porcentaje,
                    DescuentoAplicado = ToDecimal(PickObject(row, "Descuento")),
                    DeduccionDejada = ToDecimal(PickObject(row, "Dejada")),
                    Importe = importe,
                    Pago = pago,
                    FechaPago = PickDate(row, "FechaPago"),
                    Estatus = ResolveCommissionStatus(importe, pago)
                };
            }).ToList();
        }

        private async Task<List<PosComisionRowViewModel>> GetComisionRowsFromDejadasSqlAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                var normalizedNumber = long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNormalized)
                    ? parsedNormalized.ToString(CultureInfo.InvariantCulture)
                    : null;
                var connection = _dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();
                var hasPlacas = await PosColumnExistsAsync(connection, "dejadas", "placas");
                var numeroUnidadSql = hasPlacas
                    ? "COALESCE(d.placas, d.unidad, '')"
                    : "COALESCE(d.unidad, '')";
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (10000)
    COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), d.codigorecepcion, '') AS Folio,
    COALESCE(d.codigorecepcion, '') AS Ticket,
    d.fecha AS Fecha,
    COALESCE(d.nombrestaff, d.nombrevendedor, '') AS Taxista,
    COALESCE(d.tipotransporte, '') AS Transporte,
    COALESCE(d.hotel, d.nombrealmacen, '') AS Hotel,
    COALESCE(d.gafete, '') AS Gafete,
    COALESCE(d.unidad, '') AS Unidad,
    {numeroUnidadSql} AS NumeroUnidad,
    COALESCE(d.pax, 0) AS Pax,
    CASE WHEN COALESCE(d.totaltarjeta, 0) > 0 THEN 'T/C' ELSE 'MXN' END AS FormaPago,
    CAST(0 AS decimal(18,2)) AS VentaArtesania,
    CAST(0 AS decimal(18,2)) AS VentaFarmacia,
    COALESCE(StoreVenta.VentaTienda, 0) AS VentaTienda,
    COALESCE(StoreVenta.VentaJoyeria, 0) AS VentaJoyeria,
    COALESCE(NULLIF(d.totalventa, 0), StoreVenta.TotalVenta, 0) AS Base,
    CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS Porcentaje,
    CASE WHEN COALESCE(d.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS Descuento,
    COALESCE(d.total, 0) AS Dejada,
    COALESCE(d.totalgastos, 0) AS Degustacion,
    Calc.Importe AS Importe,
    CAST(0 AS decimal(18,2)) AS Pago,
    d.fechapago AS FechaPago,
    CASE WHEN UPPER(COALESCE(d.nombrecajero, '')) = 'APP MOVIL' THEN 'APP MOVIL' ELSE 'SISTEMA/POS' END AS Fuente
FROM {PosTable("dejadas")} d
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(d.tipotransporte)))
    OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(d.tipotransporte)))
OUTER APPLY
(
        SELECT
            SUM(StoreRows.TotalVenta) AS TotalVenta,
            SUM(StoreRows.VentaTienda) AS VentaTienda,
            SUM(StoreRows.VentaJoyeria) AS VentaJoyeria
    FROM
    (
        SELECT
            COALESCE(comp.total, 0) AS TotalVenta,
            COALESCE(comp.total, 0) AS VentaTienda,
            CAST(0 AS decimal(18,2)) AS VentaJoyeria
        FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] comp
        WHERE comp.folioregistro = d.folioregistro
          AND CAST(comp.fecha AS date) = CAST(d.fecha AS date)
          AND UPPER(LTRIM(RTRIM(COALESCE(comp.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
        UNION ALL
        SELECT
            COALESCE(joy.total, 0) AS TotalVenta,
            CAST(0 AS decimal(18,2)) AS VentaTienda,
            COALESCE(joy.total, 0) AS VentaJoyeria
        FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] joy
        WHERE joy.folio_registro = d.folioregistro
          AND CAST(joy.fecha AS date) = CAST(d.fecha AS date)
          AND UPPER(LTRIM(RTRIM(COALESCE(joy.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
    ) StoreRows
) StoreVenta
CROSS APPLY
(
    SELECT
        CAST(COALESCE(NULLIF(d.totalventa, 0), StoreVenta.TotalVenta, 0) AS decimal(18,4)) AS TotalVenta,
        CAST(COALESCE(d.total, 0) AS decimal(18,4)) AS Dejada,
        CAST(0 AS decimal(18,4)) AS Bebidas,
        CAST(0 AS decimal(18,4)) AS Rep,
        CAST(COALESCE(d.totalgastos, 0) AS decimal(18,4)) AS Degustacion,
        CAST(CASE WHEN COALESCE(d.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100 AS Descuento,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0) ELSE 0 END AS decimal(18,4)) AS ComisionFija,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS decimal(18,4)) / 100 AS Porcentaje
) CalcBase
CROSS APPLY
(
    SELECT CAST(ROUND(
        CASE
            WHEN CalcBase.ComisionFija > 0 THEN CalcBase.ComisionFija
            WHEN CalcBase.Descuento = 0 THEN (CalcBase.TotalVenta - CalcBase.Dejada - CalcBase.Bebidas - CalcBase.Rep - CalcBase.Degustacion) * CalcBase.Porcentaje
            ELSE ((CalcBase.TotalVenta - (CalcBase.TotalVenta * CalcBase.Descuento)) - CalcBase.Dejada - CalcBase.Bebidas - CalcBase.Rep - CalcBase.Degustacion) * CalcBase.Porcentaje
        END,
        0,
        1
    ) AS decimal(18,2)) AS Importe
) Calc
WHERE (@inicio IS NULL OR CAST(d.fecha AS date) >= @inicio)
  AND (@fin IS NULL OR CAST(d.fecha AS date) <= @fin)
  AND (
        @q IS NULL
     OR CONVERT(nvarchar(60), d.folioregistro) LIKE '%' + @q + '%'
     OR d.folioregistrostr LIKE '%' + @q + '%'
     OR d.codigorecepcion LIKE '%' + @q + '%'
     OR (@qNumber IS NOT NULL AND d.folioregistro = CONVERT(bigint, @qNumber))
     OR (@qNumber IS NOT NULL AND ISNUMERIC(d.folioregistrostr) = 1 AND CONVERT(bigint, d.folioregistrostr) = CONVERT(bigint, @qNumber))
     OR (@qNumber IS NOT NULL AND ISNUMERIC(REPLACE(d.codigorecepcion, 'M', '')) = 1 AND CONVERT(bigint, REPLACE(d.codigorecepcion, 'M', '')) = CONVERT(bigint, @qNumber))
     OR d.gafete LIKE '%' + @q + '%'
     OR d.nombrestaff LIKE '%' + @q + '%'
     OR d.nombrevendedor LIKE '%' + @q + '%'
     OR d.hotel LIKE '%' + @q + '%'
     OR d.nombrealmacen LIKE '%' + @q + '%'
     OR d.tipotransporte LIKE '%' + @q + '%'
  )
ORDER BY d.fecha DESC, d.hora DESC, d.folioregistro DESC",
                    ("@inicio", fechaInicio?.Date),
                    ("@fin", fechaFin?.Date),
                    ("@q", normalized),
                    ("@qNumber", normalizedNumber));

                return rows.Select(row =>
                {
                    var baseComision = ToDecimal(PickObject(row, "Base"));
                    var porcentaje = ToDecimal(PickObject(row, "Porcentaje"));
                    var importe = ToDecimal(PickObject(row, "Importe"));
                    var pago = ToDecimal(PickObject(row, "Pago"));
                    return new PosComisionRowViewModel
                    {
                        Folio = PickText(row, "Folio"),
                        Ticket = PickText(row, "Ticket"),
                        Fecha = PickDate(row, "Fecha"),
                        Hotel = PickText(row, "Hotel"),
                        Pax = PickInt(row, "Pax"),
                        BeneficiarioTipo = "TAXISTA",
                        Beneficiario = FirstText(PickText(row, "Gafete"), PickText(row, "Taxista")),
                        Staff = PickText(row, "Taxista"),
                        Unidad = PickText(row, "Unidad"),
                        NumeroUnidad = PickText(row, "NumeroUnidad"),
                        FormaPago = PickText(row, "FormaPago"),
                        Vendedor = PickText(row, "Taxista"),
                        Transporte = PickText(row, "Transporte"),
                        Venta = baseComision,
                        VentaArtesania = ToDecimal(PickObject(row, "VentaArtesania")),
                        VentaFarmacia = ToDecimal(PickObject(row, "VentaFarmacia")),
                        VentaTienda = ToDecimal(PickObject(row, "VentaTienda")),
                        VentaJoyeria = ToDecimal(PickObject(row, "VentaJoyeria")),
                        Base = baseComision,
                        Porcentaje = porcentaje,
                        DescuentoAplicado = ToDecimal(PickObject(row, "Descuento")),
                        DeduccionDejada = ToDecimal(PickObject(row, "Dejada")),
                        DeduccionDegustacion = ToDecimal(PickObject(row, "Degustacion")),
                        Importe = importe,
                        Pago = pago,
                        FechaPago = PickDate(row, "FechaPago"),
                        Estatus = ResolveCommissionStatus(importe, pago)
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar comisiones desde dbo.dejadas.");
                return new List<PosComisionRowViewModel>();
            }
        }

        public async Task<List<PosComisionRowViewModel>> GetPagosComisionesReporteAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsureAppMovilComisionesColumnsAsync(connection);

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1000)
    COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), COALESCE(a.folio_app, '')) AS Folio,
    COALESCE(a.folio_pos, '') AS Ticket,
    a.fecha_operacion AS Fecha,
    a.fecha_pago_comision AS FechaPago,
    COALESCE(a.vendedor_nombre, '') AS Taxista,
    COALESCE(a.folio_gafete, '') AS Beneficiario,
    COALESCE(a.tipo_operacion, '') AS Transporte,
    COALESCE(a.total, 0) AS Base,
    COALESCE(NULLIF(t.comision, 0), 0) AS Porcentaje,
    Calc.Importe AS Importe,
    COALESCE(a.pago_comision, 0) AS Pago,
    'PAGADA' AS Estatus
FROM {PosTable("AppMovilRegistro")} a
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioApp = a.folio_app
    OR r.FolioApp = a.folio_app_original
    OR r.FolioOperacion = a.folio_app
    OR r.FolioOperacion = a.folio_app_original
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(a.tipo_operacion)))
    OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(a.tipo_operacion)))
CROSS APPLY
(
    SELECT
        CAST(COALESCE(a.total, 0) AS decimal(18,4)) AS TotalVenta,
        CAST(CASE WHEN COALESCE(a.tarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100 AS Descuento,
        CAST(COALESCE(NULLIF(t.comision, 0), 0) AS decimal(18,4)) / 100 AS Porcentaje
) CalcBase
CROSS APPLY
(
    SELECT CAST(ROUND(
        CASE
            WHEN CalcBase.Descuento = 0 THEN CalcBase.TotalVenta * CalcBase.Porcentaje
            ELSE (CalcBase.TotalVenta - (CalcBase.TotalVenta * CalcBase.Descuento)) * CalcBase.Porcentaje
        END,
        0,
        1
    ) AS decimal(18,2)) AS Importe
) Calc
WHERE COALESCE(a.pago_comision, 0) > 0
  AND (@inicio IS NULL OR CAST(a.fecha_pago_comision AS date) >= @inicio)
  AND (@fin IS NULL OR CAST(a.fecha_pago_comision AS date) <= @fin)
UNION ALL
SELECT TOP (1000)
    CONVERT(nvarchar(60), m.folioperacion) AS Folio,
    COALESCE(m.foliosoluone, '') AS Ticket,
    m.fecha AS Fecha,
    m.fechapago AS FechaPago,
    COALESCE(o.staffnombre, '') AS Taxista,
    COALESCE(CONVERT(nvarchar(60), o.idstaff), '') AS Beneficiario,
    COALESCE(m.transportetipo, o.transportetipo, '') AS Transporte,
    COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0) AS Base,
    CASE
        WHEN COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0) > 0
            THEN (COALESCE(m.comision, 0) * 100.0) / (COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0))
        ELSE 0
    END AS Porcentaje,
    COALESCE(m.comision, 0) AS Importe,
    COALESCE(m.pago, 0) AS Pago,
    'PAGADA' AS Estatus
FROM {PosTable("mov_operacion")} m
LEFT JOIN {PosTable("operacion")} o
    ON o.folio = m.folioperacion
WHERE COALESCE(m.pago, 0) > 0
  AND (@inicio IS NULL OR CAST(m.fechapago AS date) >= @inicio)
  AND (@fin IS NULL OR CAST(m.fechapago AS date) <= @fin)
ORDER BY FechaPago DESC",
                ("@inicio", fechaInicio?.Date),
                ("@fin", fechaFin?.Date));

            return rows.Select(row => new PosComisionRowViewModel
            {
                Folio = PickText(row, "Folio"),
                Ticket = PickText(row, "Ticket"),
                Fecha = PickDate(row, "Fecha"),
                FechaPago = PickDate(row, "FechaPago"),
                BeneficiarioTipo = "TAXISTA",
                Beneficiario = PickText(row, "Beneficiario"),
                Staff = PickText(row, "Taxista"),
                Transporte = PickText(row, "Transporte"),
                Base = ToDecimal(PickObject(row, "Base")),
                Porcentaje = ToDecimal(PickObject(row, "Porcentaje")),
                Importe = ToDecimal(PickObject(row, "Importe")),
                Pago = ToDecimal(PickObject(row, "Pago")),
                Estatus = PickText(row, "Estatus")
            }).ToList();
        }

        private static async Task EnsureAppMovilComisionesColumnsAsync(System.Data.Common.DbConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'comision_calculada') IS NULL
    ALTER TABLE {PosTable("AppMovilRegistro")} ADD comision_calculada decimal(18,2) NULL;
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'pago_comision') IS NULL
    ALTER TABLE {PosTable("AppMovilRegistro")} ADD pago_comision decimal(18,2) NULL;
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'fecha_pago_comision') IS NULL
    ALTER TABLE {PosTable("AppMovilRegistro")} ADD fecha_pago_comision datetime2 NULL;";
            await ExecuteNonQueryAsync(command);
        }

        private async Task<List<ComisionEfRow>> GetComisionRowsFromEfAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var normalizedNumber = long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNormalized)
                ? parsedNormalized.ToString(CultureInfo.InvariantCulture)
                : null;
            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (10000)
    CONVERT(nvarchar(60), m.folioperacion) AS Folio,
    COALESCE(NULLIF(r.FolioPos, ''), NULLIF(m.foliosoluone, ''), '') AS TicketApp,
    COALESCE(m.fecha, o.fecha) AS Fecha,
    COALESCE(o.hotel, '') AS Hotel,
    COALESCE(o.pax, 0) AS Pax,
    COALESCE(o.staffnombre, '') AS Staff,
    COALESCE(o.idstaff, 0) AS IdStaff,
    CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN 'T/C' ELSE 'MXN' END AS FormaPago,
    COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(b.TransporteTipo, ''), NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), '') AS TransporteTipo,
    COALESCE(t.nombre, NULLIF(r.TransporteTipo, ''), NULLIF(b.TransporteTipo, ''), NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), '') AS TransporteNombre,
    COALESCE(NULLIF(b.GuiaMatricula, 0), NULLIF(g.Matricula, 0), 0) AS GuiaMatricula,
    COALESCE(r.TaxistaId, b.TaxistaId, 0) AS TaxistaId,
    COALESCE(NULLIF(r.TaxistaNombre, ''), NULLIF(b.TaxistaNombre, ''), '') AS TaxistaNombre,
    COALESCE(dg.porcentaje, 0) AS GuiaPorcentaje,
    COALESCE(m.totalartesania, 0) AS Artesania,
    COALESCE(m.totalfarmacia, 0) AS Farmacia,
    COALESCE(m.totalcompra, 0) AS Compra,
    COALESCE(m.totaljoyeria, 0) AS Joyeria,
    COALESCE(m.dejada, 0) AS Dejada,
    COALESCE(m.totallicor, 0) AS Bebidas,
    COALESCE(m.totalgastos, 0) AS Degustacion,
    CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS Descuento,
    COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0) AS BaseComision,
    CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(t.comision, 0) END AS Porcentaje,
    CAST(ROUND(
        CASE
            WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0)
            WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0)
            WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0)
            WHEN (CAST(CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100) = 0
                THEN ((COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0)) - COALESCE(m.dejada, 0) - COALESCE(m.totallicor, 0) - COALESCE(m.totalgastos, 0)) * (CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN 0 ELSE COALESCE(t.comision, 0) END AS decimal(18,4)) / 100)
            ELSE (((COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0)) - ((COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0)) * (CAST(CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100))) - COALESCE(m.dejada, 0) - COALESCE(m.totallicor, 0) - COALESCE(m.totalgastos, 0)) * (CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN 0 ELSE COALESCE(t.comision, 0) END AS decimal(18,4)) / 100)
        END,
        0,
        1
    ) AS decimal(18,2)) AS ImporteComision,
    COALESCE(m.pago, 0) AS Pago,
    m.fechapago AS FechaPago
FROM {PosTable("mov_operacion")} m
LEFT JOIN {PosTable("operacion")} o
    ON o.folio = m.folioperacion
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioOperacion = CONVERT(nvarchar(60), m.folioperacion)
LEFT JOIN {AppTable("PosOperacionBeneficiarios")} b
    ON b.FolioOperacion = CONVERT(nvarchar(60), m.folioperacion)
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioAppOriginal = r.FolioApp
OUTER APPLY
(
    SELECT TOP (1) gf.matricula AS Matricula
    FROM {PosTable("gafete")} gf
    WHERE gf.folioperacion = m.folioperacion
      AND COALESCE(gf.matricula, 0) > 0
    ORDER BY gf.fecha DESC, gf.hora DESC
) g
LEFT JOIN {PosTable("deptoguia")} dg
    ON dg.matricula = COALESCE(NULLIF(b.GuiaMatricula, 0), NULLIF(g.Matricula, 0), 0)
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(b.TransporteTipo, ''), NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), ''))))
    OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(b.TransporteTipo, ''), NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), ''))))
WHERE (@inicio IS NULL OR CAST(COALESCE(m.fecha, o.fecha) AS date) >= @inicio)
  AND (@fin IS NULL OR CAST(COALESCE(m.fecha, o.fecha) AS date) <= @fin)
  AND (
        @q IS NULL
     OR CONVERT(nvarchar(60), m.folioperacion) LIKE '%' + @q + '%'
     OR COALESCE(r.FolioPos, '') LIKE '%' + @q + '%'
     OR COALESCE(r.FolioApp, '') LIKE '%' + @q + '%'
     OR COALESCE(c.FolioControl, '') LIKE '%' + @q + '%'
     OR COALESCE(r.TaxistaNombre, '') LIKE '%' + @q + '%'
     OR COALESCE(r.Gafete, '') LIKE '%' + @q + '%'
     OR COALESCE(o.staffnombre, '') LIKE '%' + @q + '%'
     OR COALESCE(o.hotel, '') LIKE '%' + @q + '%'
     OR COALESCE(m.transportetipo, '') LIKE '%' + @q + '%'
     OR (@qNumber IS NOT NULL AND m.folioperacion = CONVERT(bigint, @qNumber))
  )
ORDER BY COALESCE(m.fecha, o.fecha) DESC, m.folioperacion DESC",
                ("@inicio", fechaInicio?.Date),
                ("@fin", fechaFin?.Date),
                ("@q", normalized),
                ("@qNumber", normalizedNumber));

            return rows.Select(row => new ComisionEfRow(
                    PickText(row, "Folio"),
                    PickText(row, "TicketApp"),
                    PickNullableDate(row, "Fecha"),
                    PickText(row, "Hotel"),
                    PickInt(row, "Pax"),
                    PickText(row, "Staff"),
                    PickInt(row, "IdStaff"),
                    PickText(row, "FormaPago"),
                    PickText(row, "TransporteTipo"),
                    PickText(row, "TransporteNombre"),
                    PickInt(row, "GuiaMatricula"),
                    Convert.ToInt64(ToDecimal(PickObject(row, "TaxistaId"))),
                    PickText(row, "TaxistaNombre"),
                    ToDecimal(PickObject(row, "GuiaPorcentaje")),
                    ToDecimal(PickObject(row, "Artesania")),
                    ToDecimal(PickObject(row, "Farmacia")),
                    ToDecimal(PickObject(row, "Compra")),
                    ToDecimal(PickObject(row, "Joyeria")),
                    ToDecimal(PickObject(row, "Dejada")),
                    ToDecimal(PickObject(row, "Bebidas")),
                    ToDecimal(PickObject(row, "Degustacion")),
                    ToDecimal(PickObject(row, "Descuento")),
                    ToDecimal(PickObject(row, "BaseComision")),
                    ToDecimal(PickObject(row, "Porcentaje")),
                    ToDecimal(PickObject(row, "ImporteComision")),
                    ToDecimal(PickObject(row, "Pago")),
                    PickNullableDate(row, "FechaPago")))
                .ToList();
        }

        private async Task<List<ComisionEfRow>> GetComisionRowsFromPosBasicAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                var normalizedNumber = long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNormalized)
                    ? parsedNormalized.ToString(CultureInfo.InvariantCulture)
                    : null;
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (10000)
    CONVERT(nvarchar(60), m.folioperacion) AS Folio,
    COALESCE(m.foliosoluone, '') AS TicketApp,
    COALESCE(m.fecha, o.fecha) AS Fecha,
    COALESCE(o.hotel, '') AS Hotel,
    COALESCE(o.pax, 0) AS Pax,
    COALESCE(o.staffnombre, '') AS Staff,
    COALESCE(o.idstaff, 0) AS IdStaff,
    CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN 'T/C' ELSE 'MXN' END AS FormaPago,
    COALESCE(NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), '') AS TransporteTipo,
    COALESCE(t.nombre, NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), '') AS TransporteNombre,
    CAST(0 AS int) AS GuiaMatricula,
    CAST(0 AS bigint) AS TaxistaId,
    COALESCE(o.staffnombre, '') AS TaxistaNombre,
    CAST(0 AS decimal(18,2)) AS GuiaPorcentaje,
    COALESCE(m.totalartesania, 0) AS Artesania,
    COALESCE(m.totalfarmacia, 0) AS Farmacia,
    COALESCE(m.totalcompra, 0) AS Compra,
    COALESCE(m.totaljoyeria, 0) AS Joyeria,
    COALESCE(m.dejada, 0) AS Dejada,
    COALESCE(m.totallicor, 0) AS Bebidas,
    COALESCE(m.totalgastos, 0) AS Degustacion,
    CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS Descuento,
    COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0) AS BaseComision,
    CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(t.comision, 0) END AS Porcentaje,
    CAST(ROUND(
        CASE
            WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0)
            WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0)
            WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0)
            WHEN (CAST(CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100) = 0
                THEN ((COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0)) - COALESCE(m.dejada, 0) - COALESCE(m.totallicor, 0) - COALESCE(m.totalgastos, 0)) * (CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN 0 ELSE COALESCE(t.comision, 0) END AS decimal(18,4)) / 100)
            ELSE (((COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0)) - ((COALESCE(m.totalartesania, 0) + COALESCE(m.totalfarmacia, 0) + COALESCE(m.totalcompra, 0) + COALESCE(m.totaljoyeria, 0)) * (CAST(CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100))) - COALESCE(m.dejada, 0) - COALESCE(m.totallicor, 0) - COALESCE(m.totalgastos, 0)) * (CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN 0 ELSE COALESCE(t.comision, 0) END AS decimal(18,4)) / 100)
        END,
        0,
        1
    ) AS decimal(18,2)) AS ImporteComision,
    COALESCE(m.pago, 0) AS Pago,
    m.fechapago AS FechaPago
FROM {PosTable("mov_operacion")} m
LEFT JOIN {PosTable("operacion")} o
    ON o.folio = m.folioperacion
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), ''))))
    OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(COALESCE(NULLIF(m.transportetipo, ''), NULLIF(o.transportetipo, ''), ''))))
WHERE (@inicio IS NULL OR CAST(COALESCE(m.fecha, o.fecha) AS date) >= @inicio)
  AND (@fin IS NULL OR CAST(COALESCE(m.fecha, o.fecha) AS date) <= @fin)
  AND (
        @q IS NULL
     OR CONVERT(nvarchar(60), m.folioperacion) LIKE '%' + @q + '%'
     OR COALESCE(m.foliosoluone, '') LIKE '%' + @q + '%'
     OR COALESCE(o.staffnombre, '') LIKE '%' + @q + '%'
     OR COALESCE(o.hotel, '') LIKE '%' + @q + '%'
     OR COALESCE(m.transportetipo, '') LIKE '%' + @q + '%'
     OR COALESCE(o.transportetipo, '') LIKE '%' + @q + '%'
     OR (@qNumber IS NOT NULL AND m.folioperacion = CONVERT(bigint, @qNumber))
  )
ORDER BY COALESCE(m.fecha, o.fecha) DESC, m.folioperacion DESC",
                    ("@inicio", fechaInicio?.Date),
                    ("@fin", fechaFin?.Date),
                    ("@q", normalized),
                    ("@qNumber", normalizedNumber));

                return rows.Select(row => new ComisionEfRow(
                        PickText(row, "Folio"),
                        PickText(row, "TicketApp"),
                        PickNullableDate(row, "Fecha"),
                        PickText(row, "Hotel"),
                        PickInt(row, "Pax"),
                        PickText(row, "Staff"),
                        PickInt(row, "IdStaff"),
                        PickText(row, "FormaPago"),
                        PickText(row, "TransporteTipo"),
                        PickText(row, "TransporteNombre"),
                        PickInt(row, "GuiaMatricula"),
                        Convert.ToInt64(ToDecimal(PickObject(row, "TaxistaId"))),
                        PickText(row, "TaxistaNombre"),
                        ToDecimal(PickObject(row, "GuiaPorcentaje")),
                        ToDecimal(PickObject(row, "Artesania")),
                        ToDecimal(PickObject(row, "Farmacia")),
                        ToDecimal(PickObject(row, "Compra")),
                        ToDecimal(PickObject(row, "Joyeria")),
                        ToDecimal(PickObject(row, "Dejada")),
                        ToDecimal(PickObject(row, "Bebidas")),
                        ToDecimal(PickObject(row, "Degustacion")),
                        ToDecimal(PickObject(row, "Descuento")),
                        ToDecimal(PickObject(row, "BaseComision")),
                        ToDecimal(PickObject(row, "Porcentaje")),
                        ToDecimal(PickObject(row, "ImporteComision")),
                        ToDecimal(PickObject(row, "Pago")),
                        PickNullableDate(row, "FechaPago")))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar comisiones POS basicas desde mov_operacion.");
                return new List<ComisionEfRow>();
            }
        }

        private async Task<List<PosMovOperacion>> GetComisionMovimientosBaseAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (normalized != null || fechaInicio.HasValue || fechaFin.HasValue)
            {
                var rows = await _posContext.MovOperaciones.AsNoTracking()
                    .OrderByDescending(x => x.Fecha)
                    .ThenByDescending(x => x.FolioOperacion)
                    .Take(2000)
                    .ToListAsync();
                return rows.Where(x => DateInRange(x.Fecha, fechaInicio, fechaFin)).ToList();
            }

            var today = DateTime.Today;
            var latestDate = await _posContext.MovOperaciones.AsNoTracking()
                .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == today)
                .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date)
                ?? await _posContext.MovOperaciones.AsNoTracking()
                    .Where(x => x.Fecha.HasValue)
                    .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date);
            if (!latestDate.HasValue)
                return new List<PosMovOperacion>();

            return await _posContext.MovOperaciones.AsNoTracking()
                .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == latestDate.Value)
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FolioOperacion)
                .Take(200)
                .ToListAsync();
        }

        private static bool ComisionMatches(string normalized, PosMovOperacion mov, PosOperacion? op, PosRelacionTicketTaxista? rel, PosAppMovilFolioControl? control) =>
            TextMatches(mov.FolioOperacion?.ToString(CultureInfo.InvariantCulture), normalized) ||
            TextMatches(rel?.FolioPos, normalized) ||
            TextMatches(rel?.FolioApp, normalized) ||
            TextMatches(control?.FolioControl, normalized) ||
            TextMatches(rel?.TaxistaNombre, normalized) ||
            TextMatches(rel?.Gafete, normalized) ||
            TextMatches(op?.StaffNombre, normalized) ||
            TextMatches(op?.Hotel, normalized);

        private static DateTime? TryParseComisionDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                ? parsed
                : null;
        }

        private static DateTime? PickNullableDate(IReadOnlyDictionary<string, object?> row, string name)
        {
            var value = PickObject(row, name);
            if (value == null || value == DBNull.Value)
                return null;
            if (value is DateTime date)
                return date;
            return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.CurrentCulture, DateTimeStyles.None, out var current)
                || DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out current)
                ? current
                : null;
        }

        private async Task ApplyAuthoritativeTransportRulesAsync(List<PosComisionRowViewModel> comisiones)
        {
            if (comisiones.Count == 0)
                return;

            var transportes = await GetTransportCatalogMapAsync();
            foreach (var item in comisiones)
            {
                var transporte = ResolveTransportFromCatalog(transportes, item.Transporte);
                if (transporte == null)
                    continue;

                var nombre = FirstText(item.Transporte, item.Unidad, transporte.Nombre, transporte.Tipo);
                var agencyText = $"{item.Transporte} {item.Unidad} {item.Beneficiario} {item.Staff} {transporte.Nombre} {transporte.Tipo}";
                var majestic = IsMajesticTransport(agencyText) || IsMajesticTransport(transporte.Tipo);
                var salmoranTuribus = IsSalmoranOrTuribusTransport(agencyText) || IsSalmoranOrTuribusTransport(transporte.Tipo);
                var porcentaje = majestic ? 8m : salmoranTuribus ? 20m : ToDecimal(transporte.Comision);
                var comisionFija = porcentaje > 100m
                    ? porcentaje
                    : porcentaje <= 0m
                        ? FirstPositive(ToDecimal(transporte.Maximo), ToDecimal(transporte.Minimo))
                        : 0m;
                var tarjeta = IsCardPayment(item.FormaPago);
                var descuento = item.DescuentoAplicado > 0m
                    ? NormalizePercentValue(item.DescuentoAplicado)
                    : IsAmexPayment(item.FormaPago)
                        ? 21m
                        : tarjeta
                            ? 19m
                            : 0m;

                item.Porcentaje = comisionFija > 0m ? 0m : porcentaje;
                item.DescuentoAplicado = descuento;
                if (item.Base <= 0m)
                {
                    if (item.Importe < 0m)
                        item.Importe = 0m;
                    item.Estatus = ResolveCommissionStatus(item.Importe, item.Pago);
                    continue;
                }
                if (comisionFija > 0m)
                {
                    item.Importe = comisionFija;
                    item.Estatus = ResolveCommissionStatus(item.Importe, item.Pago);
                    continue;
                }

                var deducciones = CalculateExcelRuleDeductions(item);
                var neto = descuento > 0m
                    ? item.Base - (item.Base * (descuento / 100m))
                    : item.Base;
                item.Importe = Math.Truncate(Math.Max(0m, neto - deducciones) * (porcentaje / 100m));
                item.Estatus = ResolveCommissionStatus(item.Importe, item.Pago);
            }
        }

        private static decimal FirstPositive(params decimal[] values) =>
            values.FirstOrDefault(value => value > 0m);

        private async Task ApplyOperationalExpenseRulesAsync(List<PosComisionRowViewModel> comisiones)
        {
            var candidates = comisiones
                .Where(x => x.Base > 0m && x.Compra <= 0m && x.Joyeria <= 0m && !string.IsNullOrWhiteSpace(x.Ticket))
                .ToList();
            if (candidates.Count == 0)
                return;

            var expenseByTicket = await GetStoreExpensesByTicketAsync(candidates.Select(x => x.Ticket));
            foreach (var item in candidates)
            {
                var ticket = Normalize(item.Ticket);
                if (ticket == null || !expenseByTicket.TryGetValue(ticket, out var expense))
                    continue;
                if (item.Porcentaje <= 0m && item.Importe > 0m)
                {
                    if (item.Pago <= 0m)
                        item.Estatus = "PENDIENTE";
                    continue;
                }

                item.DeduccionDejada = expense.Dejada;
                item.DeduccionGastos = expense.GastosVarios;
                item.DeduccionDegustacion = expense.Degustacion;
                item.DeduccionReparacion = expense.Reparacion;
                item.DeduccionBebidas = expense.Bebidas;
                item.DeduccionCajasRegalo = expense.CajasRegalo;
                item.Importe = CalculateCommissionFromRule(item.Base, item.Porcentaje, item.DescuentoAplicado, item.Transporte, expense);
                if (item.Pago <= 0m)
                    item.Estatus = ResolveCommissionStatus(item.Importe, item.Pago);
            }
        }

        private static void ApplyLargestSaleDejadaRule(List<PosComisionRowViewModel> comisiones)
        {
            foreach (var group in comisiones
                .Where(item => item.DeduccionDejada > 0m && GetTransportCommissionRule(item.Transporte).SubtractDejada)
                .GroupBy(ComisionDejadaGroupKey, StringComparer.OrdinalIgnoreCase))
            {
                var items = group
                    .Where(item => item.DeduccionDejada > 0m)
                    .OrderByDescending(item => item.Base)
                    .ThenByDescending(item => item.Venta)
                    .ThenBy(item => item.Ticket, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (items.Count <= 1)
                    continue;

                var dejada = items.Max(item => item.DeduccionDejada);
                for (var i = 0; i < items.Count; i++)
                    items[i].DeduccionDejada = i == 0 ? dejada : 0m;
            }
        }

        private static string ComisionDejadaGroupKey(PosComisionRowViewModel item)
        {
            var folio = Normalize(item.Folio);
            if (!string.IsNullOrWhiteSpace(folio))
                return $"FOLIO|{folio}";

            var fecha = Normalize(item.Fecha);
            var beneficiario = Normalize(FirstText(item.Beneficiario, item.Staff, item.Vendedor));
            var hotel = Normalize(item.Hotel);
            return $"ROW|{fecha}|{beneficiario}|{hotel}";
        }

        private static void RecalculateCommissionsFromExcelRules(List<PosComisionRowViewModel> comisiones)
        {
            foreach (var item in comisiones.Where(item => item.Base > 0m && item.Porcentaje > 0m))
            {
                var neto = item.DescuentoAplicado > 0m
                    ? item.Base - (item.Base * (item.DescuentoAplicado / 100m))
                    : item.Base;
                item.Importe = Math.Truncate(Math.Max(0m, neto - CalculateExcelRuleDeductions(item)) * (item.Porcentaje / 100m));
                item.Estatus = ResolveCommissionStatus(item.Importe, item.Pago);
            }
        }

        private static decimal CalculateExcelRuleDeductions(PosComisionRowViewModel item)
        {
            var rule = GetTransportCommissionRule(item.Transporte);
            var deductions = 0m;
            if (rule.SubtractDejada) deductions += item.DeduccionDejada;
            if (rule.SubtractGastos) deductions += item.DeduccionGastos;
            if (rule.SubtractDegustacion) deductions += item.DeduccionDegustacion;
            if (rule.SubtractReparacion) deductions += item.DeduccionReparacion;
            if (rule.SubtractBebidas) deductions += item.DeduccionBebidas;
            if (rule.SubtractCajasRegalo) deductions += item.DeduccionCajasRegalo;
            return deductions;
        }

        private async Task<Dictionary<string, StoreExpenseBreakdown>> GetStoreExpensesByTicketAsync(IEnumerable<string?> tickets)
        {
            var validTickets = tickets
                .Select(Normalize)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10000)
                .ToList();
            if (validTickets.Count == 0)
                return new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            var parameters = validTickets
                .Select((ticket, index) =>
                {
                    var name = $"@ticket{index}";
                    AddParameter(command, name, ticket);
                    return name;
                })
                .ToList();
            var values = string.Join(", ", parameters.Select(x => $"({x})"));
            command.CommandText = $@"
SELECT Ticket, Referencia, Concepto, Total
FROM
(
    SELECT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(80), e.folio_remision)))) AS Ticket,
        UPPER(LTRIM(RTRIM(COALESCE(e.referencia, '')))) AS Referencia,
        UPPER(LTRIM(RTRIM(COALESCE(e.concepto, '')))) AS Concepto,
        CAST(COALESCE(e.total, 0) AS decimal(18,2)) AS Total
    FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[egresos] e
    INNER JOIN (VALUES {values}) v(Ticket)
        ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(80), e.folio_remision)))) = v.Ticket

    UNION ALL

    SELECT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(80), j.folio_factura)))) AS Ticket,
        UPPER(LTRIM(RTRIM(COALESCE(j.referencia, '')))) AS Referencia,
        UPPER(LTRIM(RTRIM(COALESCE(j.concepto, '')))) AS Concepto,
        CAST(COALESCE(j.total, 0) AS decimal(18,2)) AS Total
    FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[gastos] j
    INNER JOIN (VALUES {values}) v(Ticket)
        ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(80), j.folio_factura)))) = v.Ticket
) Expenses;";

            var rows = await ReadRowsAsync(command);
            var result = new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var ticket = PickText(row, "Ticket");
                if (string.IsNullOrWhiteSpace(ticket))
                    continue;

                var normalizedTicket = Normalize(ticket)!;
                var total = ToDecimal(PickObject(row, "Total"));
                var text = $"{PickText(row, "Referencia")} {PickText(row, "Concepto")}".ToUpperInvariant();
                result.TryGetValue(normalizedTicket, out var current);

                var updated = current with
                {
                    Dejada = current.Dejada + (text.Contains("DEJADA") ? total : 0m),
                    GastosVarios = current.GastosVarios + ((text.Contains("GASTOS VARIOS") || text.EndsWith(" GV") || text.Contains(" GV ")) ? total : 0m),
                    Degustacion = current.Degustacion + (text.Contains("DEGUST") ? total : 0m),
                    Reparacion = current.Reparacion + (text.Contains("REPARA") ? total : 0m),
                    Bebidas = current.Bebidas + (LooksLikeBeverage(text) ? total : 0m),
                    CajasRegalo = current.CajasRegalo + (text.Contains("CAJA") || text.Contains("REGALO") ? total : 0m)
                };
                result[normalizedTicket] = updated;
            }

            return result;
        }

        private static bool LooksLikeBeverage(string text) =>
            text.Contains("CERVEZA")
            || text.Contains("CORONA")
            || text.Contains("COCA")
            || text.Contains("AGUA")
            || text.Contains("CANTARITO")
            || text.Contains("REFRESCO")
            || text.Contains("BEBIDA");

        private static decimal CalculateCommissionFromRule(decimal baseAmount, decimal porcentaje, decimal descuento, string? transporte, StoreExpenseBreakdown expense)
        {
            var rule = GetTransportCommissionRule(transporte);
            var totalAfterDiscount = descuento <= 0m
                ? baseAmount
                : baseAmount - (baseAmount * (descuento / 100m));
            var deductions = 0m;
            if (rule.SubtractDejada)
                deductions += expense.Dejada;
            if (rule.SubtractGastos)
                deductions += expense.GastosVarios;
            if (rule.SubtractDegustacion)
                deductions += expense.Degustacion;
            if (rule.SubtractReparacion)
                deductions += expense.Reparacion;
            if (rule.SubtractBebidas)
                deductions += expense.Bebidas;
            if (rule.SubtractCajasRegalo)
                deductions += expense.CajasRegalo;

            var neto = Math.Max(0m, totalAfterDiscount - deductions);
            return Math.Truncate(neto * (porcentaje / 100m));
        }

        private async Task ApplyTicketPaymentBreakdownRulesAsync(List<PosComisionRowViewModel> comisiones)
        {
            var candidates = comisiones
                .Where(x => x.Base > 0m && x.Porcentaje > 0m && !string.IsNullOrWhiteSpace(x.Ticket))
                .ToList();
            if (candidates.Count == 0)
                return;

            var paymentsByTicket = await GetTicketPaymentBreakdownsAsync(candidates.Select(x => x.Ticket));
            foreach (var item in candidates)
            {
                if (!TryGetTicketPaymentBreakdown(paymentsByTicket, item.Ticket, out var payments) || payments == null || payments.Total <= 0m)
                    continue;

                item.FormaPago = payments.Description;

                var deductions = CalculateExcelRuleDeductions(item);

                var saleTotal = item.Base > 0m ? item.Base : payments.Total;
                var cappedBankNet = payments.NonCard + (payments.Card * 0.81m) + (payments.Amex * 0.79m);
                if (payments.Total > 0m && item.Base > payments.Total && cappedBankNet > deductions)
                    saleTotal = payments.Total;
                var scale = payments.Total > 0m && saleTotal > 0m ? saleTotal / payments.Total : 1m;
                var efectivo = payments.NonCard * scale;
                var tarjeta = payments.Card * scale;
                var amex = payments.Amex * scale;
                var netoBancario = efectivo + (tarjeta * 0.81m) + (amex * 0.79m);
                var descuentoEfectivo = saleTotal <= 0m ? 0m : ((saleTotal - netoBancario) / saleTotal) * 100m;

                item.Base = saleTotal;
                item.Venta = saleTotal;
                item.DescuentoAplicado = NormalizePercentValue(descuentoEfectivo);
                item.Importe = Math.Truncate(Math.Max(0m, netoBancario - deductions) * (item.Porcentaje / 100m));
                item.Estatus = ResolveCommissionStatus(item.Importe, item.Pago);
            }
        }

        private async Task<Dictionary<string, TicketPaymentBreakdown>> GetTicketPaymentBreakdownsAsync(IEnumerable<string?> tickets)
        {
            var validTickets = tickets
                .SelectMany(GetPaymentTicketCandidates)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10000)
                .ToList();
            if (validTickets.Count == 0)
                return new Dictionary<string, TicketPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            var parameters = validTickets
                .Select((ticket, index) =>
                {
                    var name = $"@payTicket{index}";
                    AddParameter(command, name, ticket);
                    return name;
                })
                .ToList();
            var values = string.Join(", ", parameters.Select(x => $"({x})"));
            var compuadmoPaymentDatabase = _compuadmoStoreDatabaseName;
            var joyeriaPaymentDatabase = _joyeriaStoreDatabaseName;
            command.CommandText = $@"
SELECT Ticket, PaymentName, Total
FROM
(
    SELECT
        UPPER(REPLACE(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura))), ' ', '')) AS Ticket,
        LTRIM(RTRIM(COALESCE(m.Nombre, ''))) AS PaymentName,
        CAST(COALESCE(p.total, 0) AS decimal(18,2)) AS Total
    FROM {QuoteSqlIdentifier(compuadmoPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[pagosM] p
    INNER JOIN {QuoteSqlIdentifier(compuadmoPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[monedas] m
        ON m.moneda = p.moneda
    INNER JOIN (VALUES {values}) v(Ticket)
        ON UPPER(REPLACE(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura))), ' ', '')) = v.Ticket

    UNION ALL

    SELECT
        UPPER(REPLACE(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura))), ' ', '')) AS Ticket,
        LTRIM(RTRIM(COALESCE(m.Nombre, ''))) AS PaymentName,
        CAST(COALESCE(p.total, 0) AS decimal(18,2)) AS Total
    FROM {QuoteSqlIdentifier(joyeriaPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[pagosM] p
    INNER JOIN {QuoteSqlIdentifier(joyeriaPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[Monedas] m
        ON m.moneda = p.moneda
    INNER JOIN (VALUES {values}) v(Ticket)
        ON UPPER(REPLACE(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura))), ' ', '')) = v.Ticket
) Payments
WHERE Total <> 0;";

            var rows = await ReadRowsAsync(command);
            var result = new Dictionary<string, TicketPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in rows.GroupBy(row => PickText(row, "Ticket"), StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                    continue;

                var nonCard = 0m;
                var card = 0m;
                var amex = 0m;
                var descriptions = new List<string>();
                foreach (var row in group)
                {
                    var name = PickText(row, "PaymentName");
                    var total = ToDecimal(PickObject(row, "Total"));
                    if (total == 0m)
                        continue;

                    descriptions.Add($"{name} {total:C2}");
                    if (IsAmexPayment(name))
                        amex += total;
                    else if (IsCardPayment(name))
                        card += total;
                    else
                        nonCard += total;
                }

                var description = string.Join(" / ", descriptions.Distinct(StringComparer.OrdinalIgnoreCase));
                result[group.Key] = new TicketPaymentBreakdown(nonCard, card, amex, string.IsNullOrWhiteSpace(description) ? "SIN PAGO" : description);
            }

            return result;
        }

        private static bool TryGetTicketPaymentBreakdown(
            Dictionary<string, TicketPaymentBreakdown> paymentsByTicket,
            string? ticket,
            out TicketPaymentBreakdown? payments)
        {
            foreach (var candidate in GetPaymentTicketCandidates(ticket))
            {
                if (paymentsByTicket.TryGetValue(candidate, out payments))
                    return true;
            }

            payments = null;
            return false;
        }

        private static IEnumerable<string> GetPaymentTicketCandidates(string? ticket)
        {
            var normalized = NormalizePaymentTicketKey(ticket);
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;

            foreach (var part in (ticket ?? string.Empty)
                .Split(new[] { '/', '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = NormalizePaymentTicketKey(part);
                if (!string.IsNullOrWhiteSpace(key))
                    yield return key;
            }
        }

        private static string? NormalizePaymentTicketKey(string? value)
        {
            var text = Normalize(value);
            return string.IsNullOrWhiteSpace(text)
                ? null
                : text.Replace(" ", string.Empty).ToUpperInvariant();
        }

        private static bool IsAmexPayment(string? paymentName) =>
            (paymentName ?? string.Empty).Contains("AMEX", StringComparison.OrdinalIgnoreCase)
            || (paymentName ?? string.Empty).Contains("AMERICAN", StringComparison.OrdinalIgnoreCase);

        private static bool IsCardPayment(string? paymentName)
        {
            var text = paymentName ?? string.Empty;
            return IsAmexPayment(text)
                || text.Contains("TARJ", StringComparison.OrdinalIgnoreCase)
                || text.Contains("T:CREDITO", StringComparison.OrdinalIgnoreCase)
                || text.Contains("T.CREDITO", StringComparison.OrdinalIgnoreCase)
                || text.Contains("CITI", StringComparison.OrdinalIgnoreCase)
                || text.Contains("BANAMEX", StringComparison.OrdinalIgnoreCase)
                || text.Contains("BBVA", StringComparison.OrdinalIgnoreCase)
                || text.Contains("BANCOMER", StringComparison.OrdinalIgnoreCase)
                || text.Contains("SANTANDER", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MIFEL", StringComparison.OrdinalIgnoreCase)
                || text.Contains("AFIRME", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MERCADO PAGO", StringComparison.OrdinalIgnoreCase)
                || text.Contains("VALES", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMajesticTransport(string? transporte)
        {
            var text = transporte ?? string.Empty;
            return text.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase)
                || text.Contains("MAESTIC", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSalmoranOrTuribusTransport(string? transporte)
        {
            var text = transporte ?? string.Empty;
            return text.Contains("SALMORAN", StringComparison.OrdinalIgnoreCase)
                || text.Contains("TURIBUS", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ADO", StringComparison.OrdinalIgnoreCase);
        }

        private static string PlazaDatabaseName(string databaseName) =>
            databaseName.EndsWith("Plaza", StringComparison.OrdinalIgnoreCase)
                ? databaseName
                : databaseName + "Plaza";

        private async Task<Dictionary<string, PosTransporte>> GetTransportCatalogMapAsync()
        {
            var transportes = await _posContext.Transportes.AsNoTracking().ToListAsync();
            var map = new Dictionary<string, PosTransporte>(StringComparer.OrdinalIgnoreCase);
            foreach (var transporte in transportes)
            {
                foreach (var key in GetTransportLookupKeys(transporte.Tipo, transporte.Nombre))
                {
                    if (!map.ContainsKey(key))
                        map[key] = transporte;
                }
            }

            return map;
        }

        private static PosTransporte? ResolveTransportFromCatalog(
            IReadOnlyDictionary<string, PosTransporte> transportes,
            string? transporte)
        {
            foreach (var key in GetTransportLookupKeys(transporte))
            {
                if (transportes.TryGetValue(key, out var exact))
                    return exact;
            }

            var compact = NormalizeTransportAlias(transporte);
            if (compact.Length == 0)
                return null;

            foreach (var pair in transportes)
            {
                if (pair.Key.StartsWith(compact, StringComparison.OrdinalIgnoreCase)
                    || compact.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }

            return null;
        }

        private static IEnumerable<string> GetTransportLookupKeys(params string?[] values)
        {
            foreach (var value in values)
            {
                var normalized = NormalizeKey(value);
                if (normalized.Length > 0)
                    yield return normalized;

                var compact = NormalizeTransportAlias(value);
                if (compact.Length > 0 && !string.Equals(compact, normalized, StringComparison.OrdinalIgnoreCase))
                    yield return compact;
            }
        }

        private static string NormalizeTransportAlias(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var ch in value.Trim().ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            return builder.ToString();
        }

        private static CommissionRule GetTransportCommissionRule(string? transporte)
        {
            var normalized = NormalizeKey(transporte);
            if (IsMajesticTransport(normalized))
                return new CommissionRule(false, true, true, true, true, true);
            return new CommissionRule(true, true, true, true, true, true);
        }

        private static decimal CalculatePosCommission(PosMovOperacion movimiento, PosTransporte? transporte)
        {
            var totalVenta = ToDecimal(movimiento.TotalArtesania) + ToDecimal(movimiento.TotalFarmacia) + ToDecimal(movimiento.TotalJoyeria) + ToDecimal(movimiento.TotalCompra);
            var rule = GetTransportCommissionRule(FirstText(transporte?.Nombre, transporte?.Tipo, movimiento.TransporteTipo));
            var dejada = rule.SubtractDejada ? ToDecimal(movimiento.Dejada) : 0m;
            var bebidas = rule.SubtractBebidas ? ToDecimal(movimiento.TotalLicor) : 0m;
            var degustacion = ToDecimal(movimiento.TotalGastos);
            var descuento = ToDecimal(movimiento.TotalTarjeta) > 0m
                ? ToDecimal(transporte?.Tarjeta)
                : ToDecimal(transporte?.Efectivo);
            var porcentaje = ToDecimal(transporte?.Comision) / 100m;
            var subtotal = descuento == 0m
                ? totalVenta - dejada - bebidas - degustacion
                : (totalVenta - (totalVenta * (descuento / 100m))) - dejada - bebidas - degustacion;
            return Math.Truncate(Math.Max(0m, subtotal) * porcentaje);
        }

        private static async Task EnsureComisionPagosControlTableAsync(DbConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF OBJECT_ID('{AppTable("PosComisionPagosControl").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
    CREATE TABLE {AppTable("PosComisionPagosControl")} (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PosComisionPagosControl PRIMARY KEY,
        FolioOperacion NVARCHAR(80) NOT NULL,
        FolioOriginal NVARCHAR(80) NOT NULL DEFAULT '',
        FolioNumero NVARCHAR(80) NOT NULL DEFAULT '',
        Pago DECIMAL(18,2) NOT NULL DEFAULT 0,
        FechaPago DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
        Usuario NVARCHAR(80) NOT NULL DEFAULT '',
        CONSTRAINT UX_PosComisionPagosControl_Folio UNIQUE (FolioOperacion)
    );
    CREATE INDEX IX_PosComisionPagosControl_Busqueda ON {AppTable("PosComisionPagosControl")}(FolioOriginal, FolioNumero);
END;";
            await ExecuteNonQueryAsync(command);
        }

        private async Task ApplyComisionPagosControlAsync(List<PosComisionRowViewModel> comisiones)
        {
            if (comisiones.Count == 0)
                return;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsureComisionPagosControlTableAsync(connection);

            var keys = comisiones
                .SelectMany(x => ComisionPagoKeys(x.Folio, x.Ticket))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();
            if (keys.Count == 0)
                return;

            var values = string.Join(",", keys.Select((_, index) => $"(@k{index})"));
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT c.FolioOperacion,c.FolioOriginal,c.FolioNumero,c.Pago,c.FechaPago
FROM {AppTable("PosComisionPagosControl")} c
INNER JOIN (VALUES {values}) v(Folio)
    ON c.FolioOperacion = v.Folio OR c.FolioOriginal = v.Folio OR c.FolioNumero = v.Folio
WHERE c.Pago > 0;";
            for (var i = 0; i < keys.Count; i++)
                AddParameter(command, $"@k{i}", keys[i]);

            var rows = await ReadRowsAsync(command);
            var paidByKey = new Dictionary<string, (decimal Pago, string Fecha, string Id)>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                var pago = ToDecimal(PickObject(row, "Pago"));
                var fecha = PickDate(row, "FechaPago");
                var id = FirstText(PickText(row, "FolioOperacion"), PickText(row, "FolioOriginal"), PickText(row, "FolioNumero"));
                foreach (var key in ComisionPagoKeys(PickText(row, "FolioOperacion"), PickText(row, "FolioOriginal"), PickText(row, "FolioNumero")))
                {
                    if (pago > 0m && !paidByKey.ContainsKey(key))
                        paidByKey[key] = (pago, fecha, id);
                }
            }

            var appliedPaymentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in comisiones)
            {
                var paid = ComisionPagoKeys(item.Folio, item.Ticket)
                    .Select(key => paidByKey.TryGetValue(key, out var value) ? value : default)
                    .Where(value => value.Pago > 0m)
                    .Where(value => string.IsNullOrWhiteSpace(value.Id) || !appliedPaymentIds.Contains(value.Id))
                    .OrderByDescending(value => value.Pago)
                    .FirstOrDefault();
                if (paid.Pago <= 0m)
                    continue;

                if (item.Importe <= 0m)
                    continue;

                item.Pago = Math.Min(item.Importe, Math.Max(item.Pago, paid.Pago));
                item.FechaPago = string.IsNullOrWhiteSpace(item.FechaPago) ? paid.Fecha : item.FechaPago;
                item.Estatus = "PAGADA";
                if (!string.IsNullOrWhiteSpace(paid.Id))
                    appliedPaymentIds.Add(paid.Id);
            }
        }

        private static void NormalizeComisionPayments(List<PosComisionRowViewModel> comisiones)
        {
            foreach (var item in comisiones)
            {
                item.Porcentaje = NormalizePercentValue(item.Porcentaje);
                item.DescuentoAplicado = NormalizePercentValue(item.DescuentoAplicado);
                if (item.Importe > 0m && item.Pago > item.Importe)
                    item.Pago = item.Importe;
                if (item.Importe == 0m)
                {
                    item.Pago = 0m;
                    item.Estatus = "SIN CALCULAR";
                }
                else if (item.Pago > 0m)
                {
                    item.Estatus = item.Pago >= item.Importe ? "PAGADA" : "PENDIENTE";
                }
                else
                {
                    item.Estatus = "PENDIENTE";
                }
            }
        }

        private static void NormalizeComisionPercentages(List<PosComisionRowViewModel> comisiones)
        {
            foreach (var item in comisiones)
            {
                item.Porcentaje = NormalizePercentValue(item.Porcentaje);
                item.DescuentoAplicado = NormalizePercentValue(item.DescuentoAplicado);
            }
        }

        private static decimal NormalizePercentValue(decimal value)
        {
            var normalized = value > 0m && value <= 1m
                ? value * 100m
                : value;
            return Math.Clamp(normalized, 0m, 100m);
        }

        private static List<PosComisionRowViewModel> ConsolidateDuplicateTicketCommissions(List<PosComisionRowViewModel> comisiones)
        {
            return comisiones
                .GroupBy(DuplicateTicketCommissionKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var items = group.ToList();
                    var primary = items
                        .OrderByDescending(item => item.Base > 0m)
                        .ThenByDescending(item => item.Importe)
                        .ThenByDescending(item => item.Pago)
                        .First();

                    primary.Pago = items.Max(item => item.Pago);
                    primary.FechaPago = FirstText(primary.FechaPago, items.Select(item => item.FechaPago).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));
                    primary.DeduccionDejada = items.Max(item => item.DeduccionDejada);
                    primary.DeduccionBebidas = items.Max(item => item.DeduccionBebidas);
                    primary.DeduccionCajasRegalo = items.Max(item => item.DeduccionCajasRegalo);
                    primary.DeduccionReparacion = items.Max(item => item.DeduccionReparacion);
                    primary.DeduccionDegustacion = items.Max(item => item.DeduccionDegustacion);
                    primary.DeduccionGastos = items.Max(item => item.DeduccionGastos);
                    primary.Base = items.Max(item => item.Base);
                    primary.Importe = items.Max(item => item.Importe);
                    primary.Venta = items.Max(item => item.Venta);
                    primary.VentaArtesania = items.Max(item => item.VentaArtesania);
                    primary.VentaFarmacia = items.Max(item => item.VentaFarmacia);
                    primary.VentaTienda = items.Max(item => item.VentaTienda);
                    primary.VentaJoyeria = items.Max(item => item.VentaJoyeria);
                    primary.Compra = items.Max(item => item.Compra);
                    primary.Joyeria = items.Max(item => item.Joyeria);
                    primary.Porcentaje = items.Max(item => item.Porcentaje);
                    primary.DescuentoAplicado = items.Max(item => item.DescuentoAplicado);
                    primary.Estatus = ResolveCommissionStatus(primary.Importe, primary.Pago);
                    return primary;
                })
                .ToList();
        }

        private static string ResolveCommissionStatus(decimal importe, decimal pago)
        {
            if (importe == 0m)
                return "SIN CALCULAR";

            if (pago > 0m && pago >= importe)
                return "PAGADA";

            return "PENDIENTE";
        }

        private static List<PosComisionRowViewModel> FilterComisionesForExactFolio(List<PosComisionRowViewModel> comisiones, string? requested)
        {
            var normalized = Normalize(requested);
            if (string.IsNullOrWhiteSpace(normalized) || !LooksLikeFolioSearch(normalized))
                return comisiones;

            var filtered = comisiones
                .Where(item => ComisionPagoKeys(item.Folio, item.Ticket).Contains(normalized, StringComparer.OrdinalIgnoreCase))
                .ToList();

            return filtered.Count > 0 ? filtered : comisiones;
        }

        private static bool LooksLikeFolioSearch(string value) =>
            value.All(char.IsDigit)
            || value.StartsWith("WEB", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("AP", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("BD", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("BX", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("M", StringComparison.OrdinalIgnoreCase);

        private static string DuplicateTicketCommissionKey(PosComisionRowViewModel item)
        {
            var folio = Normalize(item.Folio);
            if (!string.IsNullOrWhiteSpace(folio))
            {
                var fechaComision = TryParseComisionDate(item.Fecha);
                var fechaKey = fechaComision.HasValue
                    ? fechaComision.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                    : Normalize(item.Fecha) ?? string.Empty;
                return string.IsNullOrWhiteSpace(fechaKey)
                    ? $"FOLIO|{folio}"
                    : $"FOLIO|{folio}|{fechaKey}";
            }

            var ticket = Normalize(item.Ticket);
            if (!string.IsNullOrWhiteSpace(ticket))
                return $"TICKET|{ticket}";

            var fecha = Normalize(item.Fecha);
            var person = Normalize(FirstText(item.Staff, item.Beneficiario, item.Vendedor));
            var transport = Normalize(item.Transporte);
            var hotel = Normalize(item.Hotel);
            var baseAmount = item.Base.ToString("0.00", CultureInfo.InvariantCulture);
            return $"ROW|{fecha}|{person}|{transport}|{hotel}|{item.Pax}|{baseAmount}";
        }

        private static IEnumerable<string> ComisionPagoKeys(params string?[] values)
        {
            foreach (var value in values)
            {
                var text = Normalize(value);
                if (text == null)
                    continue;
                yield return text;
                if (text.Length > 1 && text[0] == 'M' && text[1..].All(char.IsDigit))
                    yield return text[1..];
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    yield return number.ToString(CultureInfo.InvariantCulture);
                    yield return number.ToString("D4", CultureInfo.InvariantCulture);
                    yield return "M" + number.ToString("D8", CultureInfo.InvariantCulture);
                }
            }
        }

        private static async Task UpsertComisionPagoControlAsync(DbConnection connection, DbTransaction? transaction, string normalized, string requestedFolio, string? normalizedNumber, decimal amount, string usuario)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
MERGE {AppTable("PosComisionPagosControl")} AS target
USING (SELECT @folio AS FolioOperacion) AS source
    ON target.FolioOperacion = source.FolioOperacion
WHEN MATCHED THEN
    UPDATE SET
        FolioOriginal = @folioOriginal,
        FolioNumero = COALESCE(@folioNumber, ''),
        Pago = CASE WHEN target.Pago > @pago THEN target.Pago ELSE @pago END,
        FechaPago = SYSDATETIME(),
        Usuario = @usuario
WHEN NOT MATCHED THEN
    INSERT (FolioOperacion,FolioOriginal,FolioNumero,Pago,FechaPago,Usuario)
    VALUES (@folio,@folioOriginal,COALESCE(@folioNumber, ''),@pago,SYSDATETIME(),@usuario);";
            AddParameter(command, "@folio", normalized);
            AddParameter(command, "@folioOriginal", requestedFolio);
            AddParameter(command, "@folioNumber", normalizedNumber);
            AddParameter(command, "@pago", amount);
            AddParameter(command, "@usuario", usuario);
            await ExecuteNonQueryAsync(command);

            foreach (var key in ComisionPagoKeys(requestedFolio).Where(x => !string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await using var alias = connection.CreateCommand();
                alias.Transaction = transaction;
                alias.CommandText = $@"
IF NOT EXISTS (SELECT 1 FROM {AppTable("PosComisionPagosControl")} WHERE FolioOperacion=@folio)
BEGIN
    INSERT INTO {AppTable("PosComisionPagosControl")} (FolioOperacion,FolioOriginal,FolioNumero,Pago,FechaPago,Usuario)
    VALUES (@folio,@folioOriginal,COALESCE(@folioNumber, ''),@pago,SYSDATETIME(),@usuario);
END
ELSE
BEGIN
    UPDATE {AppTable("PosComisionPagosControl")}
    SET Pago = CASE WHEN Pago > @pago THEN Pago ELSE @pago END,
        FechaPago = SYSDATETIME(),
        Usuario = @usuario
    WHERE FolioOperacion=@folio;
END;";
                AddParameter(alias, "@folio", key);
                AddParameter(alias, "@folioOriginal", requestedFolio);
                AddParameter(alias, "@folioNumber", normalizedNumber);
                AddParameter(alias, "@pago", amount);
                AddParameter(alias, "@usuario", usuario);
                await ExecuteNonQueryAsync(alias);
            }
        }

        private static async Task<decimal> ResolveComisionPagoAmountAsync(DbConnection connection, DbTransaction? transaction, string normalized, string requestedFolio, string? normalizedNumber)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
SELECT SUM(Importe) AS Importe
FROM
(
    SELECT COALESCE(NULLIF(m.pago, 0), NULLIF(m.comision, 0), 0) AS Importe
    FROM {PosTable("mov_operacion")} m
    WHERE CONVERT(nvarchar(30), m.folioperacion) IN (@folio,@folioOriginal)
       OR (@folioNumber IS NOT NULL AND m.folioperacion = CONVERT(bigint, @folioNumber))

    UNION ALL

    SELECT COALESCE(NULLIF(a.pago_comision, 0), NULLIF(a.comision_calculada, 0), 0) AS Importe
    FROM {PosTable("AppMovilRegistro")} a
    WHERE a.folio_app IN (@folio,@folioOriginal)
       OR a.folio_app_original IN (@folio,@folioOriginal)
       OR a.folio_pos IN (@folio,@folioOriginal)
       OR (@folioNumber IS NOT NULL AND ISNUMERIC(a.folio_app) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, @folioNumber))
       OR (@folioNumber IS NOT NULL AND ISNUMERIC(a.folio_app_original) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, @folioNumber))
       OR (@folioNumber IS NOT NULL AND ISNUMERIC(a.folio_pos) = 1 AND CONVERT(bigint, a.folio_pos) = CONVERT(bigint, @folioNumber))

    UNION ALL

    SELECT COALESCE(NULLIF(d.pago, 0), NULLIF(d.comision, 0), 0) AS Importe
    FROM {PosTable("dejadas")} d
    WHERE CONVERT(nvarchar(60), d.folioregistro) IN (@folio,@folioOriginal)
       OR d.folioregistrostr IN (@folio,@folioOriginal)
       OR d.codigorecepcion IN (@folio,@folioOriginal)
       OR (@folioNumber IS NOT NULL AND d.folioregistro = CONVERT(bigint, @folioNumber))
) Amounts
WHERE Importe > 0;";
            AddParameter(command, "@folio", normalized);
            AddParameter(command, "@folioOriginal", requestedFolio);
            AddParameter(command, "@folioNumber", normalizedNumber);
            var value = await ExecuteScalarAsync(command);
            return ToDecimal(value);
        }

        private async Task<decimal> ResolveComisionPagoAmountFromApiAsync(string folio)
        {
            try
            {
                var rows = await _appTaxiApi.GetTripRecordsAsync(folio, null, null);
                var row = rows.FirstOrDefault();
                if (row == null)
                    return 0m;
                var transporteMap = await GetTransportCatalogMapAsync();
                transporteMap.TryGetValue(NormalizeKey(row.TipoOperacion), out var transporte);
                var porcentaje = ToDecimal(transporte?.Comision);
                return 0m;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible calcular pago de comision desde API para folio {Folio}.", folio);
                return 0m;
            }
        }

        private sealed record TicketPaymentBreakdown(
            decimal NonCard,
            decimal Card,
            decimal Amex,
            string Description)
        {
            public decimal Total => NonCard + Card + Amex;
        }

        private sealed record ComisionEfRow(
            string Folio,
            string TicketApp,
            DateTime? Fecha,
            string Hotel,
            int Pax,
            string Staff,
            int IdStaff,
            string FormaPago,
            string TransporteTipo,
            string TransporteNombre,
            int GuiaMatricula,
            long TaxistaId,
            string TaxistaNombre,
            decimal GuiaPorcentaje,
            decimal Artesania,
            decimal Farmacia,
            decimal Compra,
            decimal Joyeria,
            decimal Dejada,
            decimal Bebidas,
            decimal Degustacion,
            decimal Descuento,
            decimal BaseComision,
            decimal Porcentaje,
            decimal ImporteComision,
            decimal Pago,
            DateTime? FechaPago);

        private readonly record struct StoreExpenseBreakdown(
            decimal Dejada,
            decimal GastosVarios,
            decimal Degustacion,
            decimal Reparacion,
            decimal Bebidas,
            decimal CajasRegalo);

        private readonly record struct CommissionRule(
            bool SubtractDejada,
            bool SubtractGastos,
            bool SubtractDegustacion,
            bool SubtractReparacion,
            bool SubtractBebidas,
            bool SubtractCajasRegalo);

public async Task<int> RecalcularComisionesAsync(string? folioOperacion = null, string usuario = "WEB", DateTime? fecha = null)
        {
            var normalized = Normalize(folioOperacion);
            if (normalized == null && await IsCorteClosedAsync(DateTime.Today))
                return 0;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsureRelacionesTicketTaxistaTableAsync(connection);
            if (normalized != null)
                normalized = await ResolveFolioOperacionFromRelacionAsync(connection, normalized);

            var model = await TryGetComisionesAsync(normalized, fecha, fecha);
            var rows = model?.Comisiones ?? new List<PosComisionRowViewModel>();
            await using var transaction = await connection.BeginTransactionAsync();
            var affected = 0;
            foreach (var row in rows)
                affected += await PersistCalculatedComisionAsync(connection, transaction, row, markAsPaid: false, usuario);
            await InsertPosAuditoriaAsync(connection, transaction, usuario, "Comisiones", "Recalcular", normalized ?? string.Empty, $"Comisiones recalculadas con formula de Excel. Filas: {affected}",
                BuildAuditJson(("FolioOperacion", normalized), ("Fecha", fecha?.Date), ("FilasActualizadas", affected)));
            await transaction.CommitAsync();
            return affected;
        }

        public async Task<bool> PayComisionAsync(string folioOperacion, string usuario)
        {
            var normalized = Normalize(folioOperacion);
            if (normalized == null)
                return false;
            var requestedFolio = normalized;
            var normalizedNumber = long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNormalized)
                ? parsedNormalized.ToString(CultureInfo.InvariantCulture)
                : null;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsurePosOperacionBeneficiariosTableAsync(connection);
            await EnsureRelacionesTicketTaxistaTableAsync(connection);
            await EnsureAppMovilComisionesColumnsAsync(connection);
            await EnsureComisionPagosControlTableAsync(connection);
            normalized = await ResolveFolioOperacionFromRelacionAsync(connection, normalized) ?? normalized;

            var model = await TryGetComisionesAsync(requestedFolio, null, null);
            var rows = (model?.Comisiones ?? new List<PosComisionRowViewModel>())
                .Where(x => ComisionPagoKeys(x.Folio, x.Ticket).Contains(normalized, StringComparer.OrdinalIgnoreCase)
                         || ComisionPagoKeys(x.Folio, x.Ticket).Contains(requestedFolio, StringComparer.OrdinalIgnoreCase))
                .Where(x => x.Importe > 0m && x.Importe > Math.Max(0m, x.Pago))
                .ToList();

            if (rows.Count == 0)
                return false;

            await using var transaction = await connection.BeginTransactionAsync();
            var affected = 0;
            foreach (var row in rows)
                affected += await PersistCalculatedComisionAsync(connection, transaction, row, markAsPaid: true, usuario);

            if (affected <= 0)
            {
                await transaction.RollbackAsync();
                _logger.LogWarning("Se intento pagar la comision del folio {Folio}, pero no se actualizo ningun registro real.", requestedFolio);
                return false;
            }

            var controlAmount = rows.Count > 0
                ? rows.Sum(x => Math.Max(0m, x.Importe))
                : await ResolveComisionPagoAmountAsync(connection, transaction, normalized, requestedFolio, normalizedNumber);
            if (controlAmount <= 0m)
                controlAmount = await ResolveComisionPagoAmountFromApiAsync(requestedFolio);
            if (controlAmount > 0m)
            {
                await UpsertComisionPagoControlAsync(connection, transaction, normalized, requestedFolio, normalizedNumber, controlAmount, usuario);
                affected = Math.Max(affected, 1);
            }
            if (affected > 0)
                await InsertPosAuditoriaAsync(connection, transaction, usuario, "Comisiones", "Pagar", normalized ?? string.Empty, "Comision pagada sin duplicar",
                    BuildAuditJson(("FolioOperacion", normalized)));
            await transaction.CommitAsync();
            return affected > 0;
        }

        private async Task<int> PersistCalculatedComisionAsync(DbConnection connection, DbTransaction transaction, PosComisionRowViewModel row, bool markAsPaid, string usuario)
        {
            var affected = 0;
            var amount = Math.Max(0m, row.Importe);
            var folio = Normalize(row.Folio) ?? string.Empty;
            var ticket = Normalize(row.Ticket) ?? string.Empty;
            var fecha = TryParseComisionDate(row.Fecha)?.Date;

            if ((row.Compra > 0m || row.Joyeria > 0m) && !string.IsNullOrWhiteSpace(folio))
                affected += await UpdateMovOperacionComisionAsync(connection, transaction, folio, ticket, fecha, amount, markAsPaid);

            affected += await UpdateAppMovilComisionAsync(connection, transaction, folio, ticket, fecha, amount, markAsPaid);
            affected += await UpdateDejadasComisionAsync(connection, transaction, folio, ticket, fecha, amount, markAsPaid);
            return affected;
        }

        private async Task<int> UpdateMovOperacionComisionAsync(DbConnection connection, DbTransaction transaction, string folio, string ticket, DateTime? fecha, decimal amount, bool markAsPaid)
        {
            if (string.IsNullOrWhiteSpace(folio))
                return 0;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
UPDATE m
SET comision = @importe,
    pago = CASE WHEN @markAsPaid = 1 THEN CASE WHEN COALESCE(m.pago, 0) > @importe THEN COALESCE(m.pago, 0) ELSE @importe END ELSE COALESCE(m.pago, 0) END,
    fechapago = CASE WHEN @markAsPaid = 1 THEN GETDATE() ELSE m.fechapago END
FROM {PosTable("mov_operacion")} m
WHERE CONVERT(nvarchar(30), m.folioperacion) = @folio
  AND (@fecha IS NULL OR CAST(m.fecha AS date) = @fecha);";
            AddParameter(command, "@importe", amount);
            AddParameter(command, "@markAsPaid", markAsPaid ? 1 : 0);
            AddParameter(command, "@folio", folio);
            AddParameter(command, "@fecha", fecha);
            return await ExecuteNonQueryAsync(command);
        }

        private async Task<int> UpdateAppMovilComisionAsync(DbConnection connection, DbTransaction transaction, string folio, string ticket, DateTime? fecha, decimal amount, bool markAsPaid)
        {
            if (string.IsNullOrWhiteSpace(folio) && string.IsNullOrWhiteSpace(ticket))
                return 0;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
UPDATE a
SET comision_calculada = @importe,
    pago_comision = CASE WHEN @markAsPaid = 1 THEN CASE WHEN COALESCE(a.pago_comision, 0) > @importe THEN COALESCE(a.pago_comision, 0) ELSE @importe END ELSE COALESCE(a.pago_comision, 0) END,
    fecha_pago_comision = CASE WHEN @markAsPaid = 1 THEN GETDATE() ELSE a.fecha_pago_comision END
FROM {PosTable("AppMovilRegistro")} a
WHERE (
        (@ticket <> '' AND a.folio_pos = @ticket)
     OR (
            @ticket = ''
        AND (
               a.folio_app = @folio
            OR a.folio_app_original = @folio
            OR a.folio_pos = @folio
        )
     )
  )
  AND (@fecha IS NULL OR CAST(COALESCE(a.fecha_operacion, a.fecha_creacion) AS date) = @fecha);";
            AddParameter(command, "@importe", amount);
            AddParameter(command, "@markAsPaid", markAsPaid ? 1 : 0);
            AddParameter(command, "@folio", folio);
            AddParameter(command, "@ticket", ticket);
            AddParameter(command, "@fecha", fecha);
            return await ExecuteNonQueryAsync(command);
        }

        private async Task<int> UpdateDejadasComisionAsync(DbConnection connection, DbTransaction transaction, string folio, string ticket, DateTime? fecha, decimal amount, bool markAsPaid)
        {
            if (string.IsNullOrWhiteSpace(folio) && string.IsNullOrWhiteSpace(ticket))
                return 0;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
UPDATE d
SET comision = @importe,
    pago = CASE WHEN @markAsPaid = 1 THEN CASE WHEN COALESCE(d.pago, 0) > @importe THEN COALESCE(d.pago, 0) ELSE @importe END ELSE COALESCE(d.pago, 0) END,
    fechapago = CASE WHEN @markAsPaid = 1 THEN GETDATE() ELSE d.fechapago END
FROM {PosTable("dejadas")} d
WHERE (
        (@ticket <> '' AND d.codigorecepcion = @ticket)
     OR (
            @ticket = ''
        AND (
               CONVERT(nvarchar(60), d.folioregistro) = @folio
            OR d.folioregistrostr = @folio
            OR d.codigorecepcion = @folio
        )
     )
  )
  AND (@fecha IS NULL OR CAST(d.fecha AS date) = @fecha);";
            AddParameter(command, "@importe", amount);
            AddParameter(command, "@markAsPaid", markAsPaid ? 1 : 0);
            AddParameter(command, "@folio", folio);
            AddParameter(command, "@ticket", ticket);
            AddParameter(command, "@fecha", fecha);
            return await ExecuteNonQueryAsync(command);
        }

        private static string RecalcularComisionesDiaSql => $@"
UPDATE m
SET comision = ROUND(
    CASE
        WHEN Calc.ComisionFija > 0 THEN Calc.ComisionFija
        WHEN Calc.Descuento = 0 THEN (Calc.TotalVenta - Calc.Dejada - Calc.Bebidas - Calc.Rep - Calc.Degustacion) * Calc.Porcentaje
        ELSE ((Calc.TotalVenta - (Calc.TotalVenta * Calc.Descuento)) - Calc.Dejada - Calc.Bebidas - Calc.Rep - Calc.Degustacion) * Calc.Porcentaje
    END, 0, 1)
FROM {PosTable("mov_operacion")} m
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(m.transportetipo)))
    OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(m.transportetipo)))
CROSS APPLY
(
    SELECT
        CAST(COALESCE(m.totaljoyeria, 0) + COALESCE(m.totalcompra, 0) AS decimal(18,4)) AS TotalVenta,
        CAST(COALESCE(m.dejada, 0) AS decimal(18,4)) AS Dejada,
        CAST(COALESCE(m.totallicor, 0) AS decimal(18,4)) AS Bebidas,
        CAST(0 AS decimal(18,4)) AS Rep,
        CAST(COALESCE(m.totalgastos, 0) AS decimal(18,4)) AS Degustacion,
        CAST(CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100 AS Descuento,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0) ELSE 0 END AS decimal(18,4)) AS ComisionFija,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS decimal(18,4)) / 100 AS Porcentaje
) Calc
WHERE CAST(m.fecha AS date) = COALESCE(
    (SELECT MAX(CAST(fecha AS date)) FROM {PosTable("mov_operacion")} WHERE CAST(fecha AS date) = CAST(GETDATE() AS date)),
    (SELECT MAX(CAST(fecha AS date)) FROM {PosTable("mov_operacion")})
);";

        private static string RecalcularComisionesFechaSql => $@"
UPDATE m
SET comision = ROUND(
    CASE
        WHEN Calc.ComisionFija > 0 THEN Calc.ComisionFija
        WHEN Calc.Descuento = 0 THEN (Calc.TotalVenta - Calc.Dejada - Calc.Bebidas - Calc.Rep - Calc.Degustacion) * Calc.Porcentaje
        ELSE ((Calc.TotalVenta - (Calc.TotalVenta * Calc.Descuento)) - Calc.Dejada - Calc.Bebidas - Calc.Rep - Calc.Degustacion) * Calc.Porcentaje
    END, 0, 1)
FROM {PosTable("mov_operacion")} m
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(m.transportetipo)))
    OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(m.transportetipo)))
CROSS APPLY
(
    SELECT
        CAST(COALESCE(m.totaljoyeria, 0) + COALESCE(m.totalcompra, 0) AS decimal(18,4)) AS TotalVenta,
        CAST(COALESCE(m.dejada, 0) AS decimal(18,4)) AS Dejada,
        CAST(COALESCE(m.totallicor, 0) AS decimal(18,4)) AS Bebidas,
        CAST(0 AS decimal(18,4)) AS Rep,
        CAST(COALESCE(m.totalgastos, 0) AS decimal(18,4)) AS Degustacion,
        CAST(CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100 AS Descuento,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0) ELSE 0 END AS decimal(18,4)) AS ComisionFija,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS decimal(18,4)) / 100 AS Porcentaje
) Calc
WHERE CAST(m.fecha AS date) = @fecha;";

        private static string RecalcularComisionFolioSql => $@"
UPDATE m
SET comision = ROUND(
    CASE
        WHEN Calc.ComisionFija > 0 THEN Calc.ComisionFija
        WHEN Calc.Descuento = 0 THEN (Calc.TotalVenta - Calc.Dejada - Calc.Bebidas - Calc.Rep - Calc.Degustacion) * Calc.Porcentaje
        ELSE ((Calc.TotalVenta - (Calc.TotalVenta * Calc.Descuento)) - Calc.Dejada - Calc.Bebidas - Calc.Rep - Calc.Degustacion) * Calc.Porcentaje
    END, 0, 1)
FROM {PosTable("mov_operacion")} m
LEFT JOIN {PosTable("operacion")} o
    ON o.folio = m.folioperacion
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioOperacion = CONVERT(nvarchar(60), m.folioperacion)
LEFT JOIN {AppTable("PosOperacionBeneficiarios")} b
    ON b.FolioOperacion = CONVERT(nvarchar(30), m.folioperacion)
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(COALESCE(r.TransporteTipo, b.TransporteTipo, m.transportetipo, o.transportetipo, ''))))
    OR UPPER(LTRIM(RTRIM(t.nombre))) = UPPER(LTRIM(RTRIM(COALESCE(r.TransporteTipo, b.TransporteTipo, m.transportetipo, o.transportetipo, ''))))
CROSS APPLY
(
    SELECT
        CAST(COALESCE(m.totaljoyeria, 0) + COALESCE(m.totalcompra, 0) AS decimal(18,4)) AS TotalVenta,
        CAST(COALESCE(m.dejada, 0) AS decimal(18,4)) AS Dejada,
        CAST(COALESCE(m.totallicor, 0) AS decimal(18,4)) AS Bebidas,
        CAST(0 AS decimal(18,4)) AS Rep,
        CAST(COALESCE(m.totalgastos, 0) AS decimal(18,4)) AS Degustacion,
        CAST(CASE WHEN COALESCE(m.totaltarjeta, 0) > 0 THEN COALESCE(t.tarjeta, 0) ELSE COALESCE(t.efectivo, 0) END AS decimal(18,4)) / 100 AS Descuento,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 THEN COALESCE(t.comision, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.maximo, 0) WHEN COALESCE(t.comision, 0) <= 0 AND COALESCE(t.minimo, 0) BETWEEN 1 AND 1000 THEN COALESCE(t.minimo, 0) ELSE 0 END AS decimal(18,4)) AS ComisionFija,
        CAST(CASE WHEN COALESCE(t.comision, 0) > 100 OR (COALESCE(t.comision, 0) <= 0 AND (COALESCE(t.maximo, 0) BETWEEN 1 AND 1000 OR COALESCE(t.minimo, 0) BETWEEN 1 AND 1000)) THEN 0 ELSE COALESCE(NULLIF(t.comision, 0), 0) END AS decimal(18,4)) / 100 AS Porcentaje
) Calc
WHERE CONVERT(nvarchar(30), m.folioperacion) = @folio;";
    }
}

