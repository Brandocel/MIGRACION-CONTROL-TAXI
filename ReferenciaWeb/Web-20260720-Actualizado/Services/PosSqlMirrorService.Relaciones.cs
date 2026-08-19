using System.Data;
using System.Data.Common;
using System.Globalization;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        public async Task<PosRelacionesViewModel?> TryGetRelacionesAsync(string? busqueda = null, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();
                await EnsureRelacionesTicketTaxistaTableAsync(connection);
                await EnsureAppMovilPayoutColumnsAsync(connection);

                var normalized = Normalize(busqueda);
                _ = SyncAppMovilRegistrosFromApiAsync(normalized, fechaInicio, fechaFin);
                var queryFechaInicio = fechaInicio;
                var queryFechaFin = fechaFin;
                if (!queryFechaInicio.HasValue && !queryFechaFin.HasValue && string.IsNullOrWhiteSpace(normalized))
                {
                    queryFechaInicio = DateTime.Today;
                    queryFechaFin = DateTime.Today;
                }

                var rows = await ReadAppMovilRelacionRowsAsync(normalized, queryFechaInicio, queryFechaFin);

                var relations = MapRelacionRows(rows);
                var posRelations = await GetRelacionesRowsFromDejadasAsync(normalized, queryFechaInicio, queryFechaFin);
                relations = MergeRelacionDuplicates(relations.Concat(posRelations))
                    .OrderByDescending(HasOperacionOrTicket)
                    .ThenBy(x => x.Fuente.Contains("APP MOVIL", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(x => TryParseRelacionDate(x.Fecha) ?? DateTime.MinValue)
                    .Take(300)
                    .ToList();
                if (relations.Count == 0)
                    relations = await GetRelacionesRowsFromApiAsync(normalized, fechaInicio, fechaFin);
                await EnrichRelationNationalitiesFromLocalAsync(relations, queryFechaInicio, queryFechaFin);
                await EnrichRelationNationalitiesFromCatalogAsync(relations);
                if (string.IsNullOrWhiteSpace(normalized) || relations.Count <= 40)
                    await EnrichRelacionesWithCalculatedComisionesAsync(relations, queryFechaInicio, queryFechaFin);
                await EnrichRelacionesWithPaymentNamesAsync(relations);

                return new PosRelacionesViewModel
                {
                    Busqueda = normalized ?? string.Empty,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    Vendedores = await LoadRelationVendorOptionsAsync(connection),
                    Relaciones = relations
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar relaciones app movil / taxistas.");
                var normalized = Normalize(busqueda);
                var relations = MergeRelacionDuplicates(await GetRelacionesRowsFromDejadasAsync(normalized, fechaInicio, fechaFin));
                if (relations.Count == 0)
                    relations = await GetRelacionesRowsFromApiAsync(normalized, fechaInicio, fechaFin);
                await EnrichRelationNationalitiesFromLocalAsync(relations, fechaInicio, fechaFin);
                await EnrichRelationNationalitiesFromCatalogAsync(relations);
                if (string.IsNullOrWhiteSpace(normalized) || relations.Count <= 40)
                    await EnrichRelacionesWithCalculatedComisionesAsync(relations, fechaInicio, fechaFin);
                await EnrichRelacionesWithPaymentNamesAsync(relations);
                return new PosRelacionesViewModel
                {
                    Busqueda = normalized ?? string.Empty,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    Vendedores = await LoadRelationVendorOptionsAsync(_dbContext.Database.GetDbConnection()),
                    Relaciones = relations
                };
            }
        }

        public async Task<List<PosRelacionRowViewModel>> GetRelacionesReporteDejadasAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, string? busqueda = null)
        {
            var normalized = Normalize(busqueda);
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsureRelacionesTicketTaxistaTableAsync(connection);
            await EnsureAppMovilPayoutColumnsAsync(connection);

            var appRows = await ReadAppMovilRelacionRowsAsync(normalized, fechaInicio, fechaFin);
            var appRelations = MapRelacionRows(appRows)
                .Where(x => IsReportableDejadaRow(x, fechaInicio, fechaFin))
                .ToList();

            var appKeys = appRelations
                .Select(BuildReportRelacionKey)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var relations = appRelations;
            if (relations.Count > 0)
            {
                var posOnlySales = await GetRelacionesRowsFromDejadasAsync(normalized, fechaInicio, fechaFin, exactReport: true);
                posOnlySales = posOnlySales
                    .Where(x => IsReportableDejadaRow(x, fechaInicio, fechaFin))
                    .Where(x => x.Venta > 0m || !string.IsNullOrWhiteSpace(x.FolioPos))
                    .Where(x =>
                    {
                        var key = BuildReportRelacionKey(x);
                        return string.IsNullOrWhiteSpace(key) || !appKeys.Contains(key);
                    })
                    .ToList();

                if (posOnlySales.Count > 0)
                    relations = relations.Concat(posOnlySales).ToList();
            }
            else
            {
                relations = await GetRelacionesRowsFromDejadasAsync(normalized, fechaInicio, fechaFin, exactReport: true);
                relations = relations
                    .Where(x => IsReportableDejadaRow(x, fechaInicio, fechaFin))
                    .ToList();
            }

            relations = MergeRelacionDuplicates(relations)
                .Where(x => IsReportableDejadaRow(x, fechaInicio, fechaFin))
                .OrderBy(x => TryParseRelacionDate(x.Fecha) ?? DateTime.MaxValue)
                .ThenBy(x => x.FolioOperacion)
                .ThenBy(x => x.FolioControl)
                .ToList();

            await EnrichRelacionesWithCalculatedComisionesAsync(relations, fechaInicio, fechaFin);
            await EnrichRelacionesWithPaymentNamesAsync(relations);
            await EnrichRelationNationalitiesFromLocalAsync(relations, fechaInicio, fechaFin);
            await EnrichRelationNationalitiesFromCatalogAsync(relations);
            foreach (var relation in relations)
            {
                if (relation.Pago > 0m || relation.Comision != 0m)
                    relation.Estatus = ResolveCommissionStatus(relation.Comision, relation.Pago);
            }

            _ = SyncAppMovilRegistrosFromApiAsync(normalized, fechaInicio, fechaFin);
            return relations;
        }

        private static string BuildReportRelacionKey(PosRelacionRowViewModel row)
        {
            var fecha = TryParseRelacionDate(row.Fecha);
            var fechaKey = fecha.HasValue
                ? fecha.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : Normalize(row.Fecha) ?? string.Empty;
            var folio = FirstText(row.FolioApp, row.FolioControl, row.FolioOperacion, row.FolioPos);
            folio = NormalizeRelationToken(folio);
            return string.IsNullOrWhiteSpace(folio) ? string.Empty : $"{folio}|{fechaKey}";
        }

        private static bool IsReportableDejadaRow(PosRelacionRowViewModel row, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (!IsWithinDateRange(row.Fecha, fechaInicio, fechaFin))
                return false;

            var combined = $"{row.FolioControl} {row.FolioApp} {row.FolioOperacion} {row.FolioPos} {row.Vendedor} {row.TaxistaNombre} {row.Hotel} {row.Origen} {row.Sitio} {row.TransporteTipo} {row.Observaciones}";
            if (combined.Contains("PRUEBA", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("PRUEB", StringComparison.OrdinalIgnoreCase)
                || combined.Contains("TEST", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private List<PosRelacionRowViewModel> MapRelacionRows(List<Dictionary<string, object?>> rows)
        {
            return rows.Select(row =>
            {
                var taxistaId = Convert.ToInt64(ToDecimal(PickObject(row, "TaxistaId")));
                var comision = ToDecimal(PickObject(row, "Comision"));
                if (comision < 0m)
                    comision = 0m;
                var pago = ToDecimal(PickObject(row, "Pago"));
                var dejada = ToDecimal(PickObject(row, "Dejada"));
                var dejadaPagada = ToDecimal(PickObject(row, "DejadaPagada"));
                var estatusDejada = FirstText(PickText(row, "EstatusDejada"), dejadaPagada > 0 ? "pagado" : "pendiente");
                if (dejada <= 0m && dejadaPagada <= 0m)
                    estatusDejada = "sin importe";
                else if (dejadaPagada > 0m)
                    estatusDejada = "pagado";
                return new PosRelacionRowViewModel
                {
                    FolioControl = PickText(row, "FolioControl"),
                    FolioApp = PickText(row, "FolioApp"),
                    Fuente = FirstText(PickText(row, "Fuente"), "APP MOVIL"),
                    UsuarioOrigen = PickText(row, "UsuarioOrigen"),
                    FolioOperacion = PickText(row, "FolioOperacion"),
                    FolioPos = PickText(row, "FolioPos"),
                    FolioOperacionSugerido = PickText(row, "FolioOperacionSugerido"),
                    FolioPosSugerido = PickText(row, "FolioPosSugerido"),
                    Fecha = PickDate(row, "Fecha"),
                    Vendedor = PickText(row, "Vendedor"),
                    VendedorAsignado = PickText(row, "VendedorAsignado"),
                    Hotel = PickText(row, "Hotel"),
                    Sitio = PickText(row, "Sitio"),
                    Origen = PickText(row, "Origen"),
                    Destino = PickText(row, "Destino"),
                    Unidad = PickText(row, "Unidad"),
                    Placas = PickText(row, "Placas"),
                    Telefono = PickText(row, "Telefono"),
                    Nacionalidad = CleanNationality(PickText(row, "Nacionalidad")),
                    Pax = PickInt(row, "Pax"),
                    Gafete = PickText(row, "Gafete"),
                    TaxistaId = taxistaId,
                    TaxistaNombre = FirstText(PickText(row, "TaxistaNombre"), PickText(row, "Vendedor")),
                    TransporteTipo = PickText(row, "TransporteTipo"),
                    Venta = ToDecimal(PickObject(row, "Venta")),
                    Dejada = dejada,
                    DejadaPagada = dejadaPagada,
                    Comision = comision,
                    Pago = pago,
                    EstatusDejada = estatusDejada,
                    FechaPagoDejada = PickDate(row, "FechaPagoDejada"),
                    UsuarioPagoDejada = PickText(row, "UsuarioPagoDejada"),
                    TicketPagoDejada = PickText(row, "TicketPagoDejada"),
                    Observaciones = PickText(row, "Observaciones"),
                    IdStaff = PickText(row, "IdStaff"),
                    PuedePagarDejada = dejada > 0 && dejadaPagada <= 0 && !string.Equals(estatusDejada, "pagado", StringComparison.OrdinalIgnoreCase),
                    Estatus = taxistaId <= 0
                        ? "PENDIENTE DE LIGAR"
                        : string.IsNullOrWhiteSpace(PickText(row, "FolioOperacion"))
                            ? "PENDIENTE DE TICKET"
                            : pago > 0
                                ? "COMISION PAGADA"
                                : comision > 0
                                    ? "COMISION PENDIENTE"
                                    : "LIGADO"
                };
            }).ToList();
        }

        private async Task<List<Dictionary<string, object?>>> ReadAppMovilRelacionRowsAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var filters = new List<string>();
            var parameters = new List<(string Name, object? Value)>();

            if (fechaInicio.HasValue)
            {
                filters.Add("v.fecha_registro >= @fechaInicio");
                parameters.Add(("@fechaInicio", fechaInicio.Value.Date));
            }

            if (fechaFin.HasValue)
            {
                filters.Add("v.fecha_registro < @fechaFinExclusiva");
                parameters.Add(("@fechaFinExclusiva", fechaFin.Value.Date.AddDays(1)));
            }

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                filters.Add(@"(
       v.id_registro LIKE '%' + @q + '%'
    OR COALESCE(c.FolioControl, v.id_registro, '') LIKE '%' + @q + '%'
    OR COALESCE(r.FolioOperacion, '') LIKE '%' + @q + '%'
    OR COALESCE(r.FolioPos, '') LIKE '%' + @q + '%'
    OR COALESCE(r.Gafete, '') LIKE '%' + @q + '%'
    OR COALESCE(r.TaxistaNombre, '') LIKE '%' + @q + '%'
    OR CONVERT(nvarchar(60), COALESCE(v.id_catalogo, 0)) LIKE '%' + @q + '%'
    OR COALESCE(v.nombre_taxista, '') LIKE '%' + @q + '%'
    OR COALESCE(v.folio_gafete, '') LIKE '%' + @q + '%'
    OR COALESCE(v.telefono_taxista, '') LIKE '%' + @q + '%'
    OR COALESCE(v.placas, '') LIKE '%' + @q + '%'
    OR COALESCE(v.unidad, '') LIKE '%' + @q + '%'
    OR COALESCE(v.hotel, '') LIKE '%' + @q + '%'
    OR COALESCE(v.tipo_servicio, '') LIKE '%' + @q + '%'
)");
                parameters.Add(("@q", normalized));
            }

            var where = filters.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}WHERE {string.Join($"{Environment.NewLine}  AND ", filters)}";

            return await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (600)
    COALESCE(c.FolioControl, v.id_registro, '') AS FolioControl,
    v.id_registro AS FolioApp,
    'APP MOVIL' AS Fuente,
    COALESCE(NULLIF(a.usuario_movil, ''), 'hostinger') AS UsuarioOrigen,
    COALESCE(NULLIF(r.FolioOperacion, ''), '') AS FolioOperacion,
    COALESCE(NULLIF(r.FolioPos, ''), NULLIF(StoreVenta.Ticket, ''), '') AS FolioPos,
    COALESCE(NULLIF(r.Vendedor, ''), '') AS VendedorAsignado,
    COALESCE(CONVERT(nvarchar(60), s.FolioSugerido), '') AS FolioOperacionSugerido,
    COALESCE(NULLIF(StoreVenta.Ticket, ''), NULLIF(s.TicketSugerido, ''), '') AS FolioPosSugerido,
    v.fecha_registro AS Fecha,
    COALESCE(v.nombre_taxista, '') AS Vendedor,
    COALESCE(NULLIF(v.hotel, ''), NULLIF(a.hotel, ''), '') AS Hotel,
    COALESCE(NULLIF(v.origen, ''), NULLIF(a.origen, ''), '') AS Origen,
    COALESCE(NULLIF(v.sitio, ''), NULLIF(a.sitio, ''), '') AS Sitio,
    COALESCE(NULLIF(v.destino, ''), NULLIF(a.destino, ''), '') AS Destino,
    COALESCE(NULLIF(v.unidad, ''), NULLIF(a.unidad, ''), '') AS Unidad,
    COALESCE(NULLIF(v.placas, ''), NULLIF(a.placas, ''), '') AS Placas,
    COALESCE(NULLIF(v.telefono_taxista, ''), NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
    COALESCE(NULLIF(v.nacionalidad, ''), NULLIF(a.nacionalidad, ''), '') AS Nacionalidad,
    COALESCE(v.numero_personas, 0) AS Pax,
    COALESCE(NULLIF(v.folio_gafete, ''), NULLIF(a.folio_gafete, ''), NULLIF(r.Gafete, ''), '') AS Gafete,
    CASE
        WHEN COALESCE(v.id_catalogo, 0) > 0
         AND (
                COALESCE(r.TaxistaId, 0) = 0
             OR CONVERT(nvarchar(30), COALESCE(r.TaxistaId, 0)) = NULLIF(LTRIM(RTRIM(v.folio_gafete)), '')
         )
            THEN v.id_catalogo
        ELSE COALESCE(r.TaxistaId, v.id_catalogo, 0)
    END AS TaxistaId,
    CASE
        WHEN COALESCE(v.id_catalogo, 0) > 0
         AND (
                COALESCE(r.TaxistaId, 0) = 0
             OR CONVERT(nvarchar(30), COALESCE(r.TaxistaId, 0)) = NULLIF(LTRIM(RTRIM(v.folio_gafete)), '')
             OR NULLIF(LTRIM(RTRIM(r.TaxistaNombre)), '') IS NULL
         )
            THEN COALESCE(v.nombre_taxista, '')
        ELSE COALESCE(NULLIF(r.TaxistaNombre, ''), v.nombre_taxista, '')
    END AS TaxistaNombre,
    COALESCE(NULLIF(r.TransporteTipo, ''), v.tipo_servicio, '') AS TransporteTipo,
    COALESCE(NULLIF(m.TotalVenta, 0), StoreVenta.TotalVenta, 0) AS Venta,
    COALESCE(NULLIF(r.Dejada, 0), NULLIF(d.Dejada, 0), NULLIF(m.Dejada, 0), CASE WHEN ISNUMERIC(v.costo_viaje) = 1 THEN CAST(v.costo_viaje AS decimal(18,2)) ELSE 0 END, 0) AS Dejada,
    CASE
        WHEN UPPER(LTRIM(RTRIM(COALESCE(NULLIF(a.payout_status, ''), NULLIF(a.estado_pago_dejada, ''), '')))) IN ('PAGADO', 'PAGADA')
          OR COALESCE(d.Pago, m.Pago, 0) > 0
            THEN COALESCE(NULLIF(r.Dejada, 0), NULLIF(d.Dejada, 0), NULLIF(m.Dejada, 0), CASE WHEN ISNUMERIC(v.costo_viaje) = 1 THEN CAST(v.costo_viaje AS decimal(18,2)) ELSE 0 END, 0)
        ELSE 0
    END AS DejadaPagada,
    CASE
        WHEN UPPER(LTRIM(RTRIM(COALESCE(NULLIF(a.payout_status, ''), NULLIF(a.estado_pago_dejada, ''), '')))) IN ('PAGADO', 'PAGADA')
          OR COALESCE(d.Pago, m.Pago, 0) > 0
            THEN 'pagado'
        ELSE 'pendiente'
    END AS EstatusDejada,
    COALESCE(a.payout_date, a.fecha_pago_dejada) AS FechaPagoDejada,
    COALESCE(NULLIF(a.payout_user, ''), NULLIF(a.usuario_pago_dejada, ''), '') AS UsuarioPagoDejada,
    COALESCE(NULLIF(a.payout_ticket, ''), NULLIF(a.ticket_pago_dejada, ''), '') AS TicketPagoDejada,
    CAST(0 AS decimal(18,2)) AS Comision,
    COALESCE(NULLIF(d.Pago, 0), m.Pago, 0) AS Pago,
    COALESCE(NULLIF(r.Observaciones, ''), NULLIF(a.notas, ''), '') AS Observaciones
FROM {PosTable("vw_AppMovilRegistrosViajes")} v
LEFT JOIN {PosTable("AppMovilRegistro")} a
    ON a.folio_app = v.id_registro
    OR a.folio_app_original = v.id_registro
    OR a.folio_pos = v.id_registro
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioControl = v.id_registro OR c.FolioAppOriginal = v.id_registro
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioApp = v.id_registro OR r.FolioApp = a.folio_app_original
LEFT JOIN {PosTable("transporte")} t
    ON UPPER(LTRIM(RTRIM(t.tipo))) = UPPER(LTRIM(RTRIM(v.tipo_servicio)))
OUTER APPLY
(
    SELECT
        SUM(COALESCE(totaljoyeria, 0) + COALESCE(totalcompra, 0)) AS TotalVenta,
        SUM(COALESCE(dejada, 0)) AS Dejada,
        SUM(COALESCE(comision, 0)) AS Comision,
        SUM(COALESCE(pago, 0)) AS Pago
    FROM {PosTable("mov_operacion")} m
    WHERE CONVERT(nvarchar(60), m.folioperacion) = NULLIF(r.FolioOperacion, '')
) m
OUTER APPLY
(
    SELECT TOP (1)
        COALESCE(totalventa, 0) AS TotalVenta,
        COALESCE(total, 0) AS Dejada,
        COALESCE(comision, 0) AS Comision,
        COALESCE(pago, 0) AS Pago
    FROM {PosTable("dejadas")} d
    WHERE (
            CONVERT(nvarchar(60), d.folioregistro) = NULLIF(r.FolioOperacion, '')
         OR d.folioregistrostr = NULLIF(r.FolioOperacion, '')
          )
      AND CAST(d.fecha AS date) = CAST(v.fecha_registro AS date)
    ORDER BY fecha DESC
) d
OUTER APPLY
(
    SELECT TOP (1)
        StoreRows.TotalVenta,
        StoreRows.Ticket,
        StoreRows.Fecha
    FROM
    (
        SELECT
            CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(comp.estatus, '')))) = 'C' THEN 0 ELSE COALESCE(comp.total, 0) END AS TotalVenta,
            CONVERT(nvarchar(80), comp.folio_remision) AS Ticket,
            comp.fecha AS Fecha,
            COALESCE(comp.estatus, '') AS Estatus
        FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] comp
        WHERE ISNUMERIC(v.id_registro) = 1
          AND ISNUMERIC(CONVERT(nvarchar(50), comp.folioregistro)) = 1
          AND CAST(comp.folioregistro AS bigint) = CAST(v.id_registro AS bigint)
          AND CAST(comp.fecha AS date) = CAST(v.fecha_registro AS date)
        UNION ALL
        SELECT
            CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(joy.estatus, '')))) = 'C' THEN 0 ELSE COALESCE(joy.total, 0) END AS TotalVenta,
            CONVERT(nvarchar(80), joy.folio_factura) AS Ticket,
            joy.fecha AS Fecha,
            COALESCE(joy.estatus, '') AS Estatus
        FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] joy
        WHERE ISNUMERIC(v.id_registro) = 1
          AND ISNUMERIC(CONVERT(nvarchar(50), joy.folio_registro)) = 1
          AND CAST(joy.folio_registro AS bigint) = CAST(v.id_registro AS bigint)
          AND CAST(joy.fecha AS date) = CAST(v.fecha_registro AS date)
    ) StoreRows
      ORDER BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(StoreRows.Estatus, '')))) = 'C' THEN 0 ELSE 1 END, StoreRows.Fecha DESC
) StoreVenta
OUTER APPLY
(
    SELECT TOP (1)
        o.folio AS FolioSugerido,
        COALESCE(mo.foliosoluone, o.foliosoluone, '') AS TicketSugerido
    FROM {PosTable("operacion")} o
    LEFT JOIN {PosTable("mov_operacion")} mo
        ON mo.folioperacion = o.folio
    WHERE CAST(o.fecha AS date) = CAST(v.fecha_registro AS date)
      AND (
            NULLIF(LTRIM(RTRIM(v.hotel)), '') IS NULL
         OR UPPER(LTRIM(RTRIM(o.hotel))) = UPPER(LTRIM(RTRIM(v.hotel)))
         OR UPPER(o.hotel) LIKE '%' + UPPER(LTRIM(RTRIM(v.hotel))) + '%'
         OR UPPER(LTRIM(RTRIM(v.hotel))) LIKE '%' + UPPER(LTRIM(RTRIM(o.hotel))) + '%'
      )
    ORDER BY
        CASE WHEN UPPER(LTRIM(RTRIM(o.hotel))) = UPPER(LTRIM(RTRIM(v.hotel))) THEN 0 ELSE 1 END,
        o.fecha DESC,
        o.folio DESC
) s
{where}
ORDER BY v.fecha_registro DESC, v.id_registro DESC", parameters.ToArray());
        }

        private async Task<List<PosRelacionRowViewModel>> GetRelacionesRowsFromApiAsync(string? normalized, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            try
            {
                var apiRows = await _appTaxiApi.GetTripRecordsAsync(normalized, fechaInicio, fechaFin);
                var transporteMap = await GetTransportCatalogMapAsync();
                return apiRows
                    .OrderByDescending(x => x.Hora)
                    .Take(120)
                    .Select(x => new PosRelacionRowViewModel
                    {
                        FolioControl = x.FolioControl,
                        FolioApp = x.FolioControl,
                        Fuente = "APP MOVIL",
                        UsuarioOrigen = FirstText(x.UsuarioOrigen, "hostinger"),
                        FolioOperacion = string.Empty,
                        FolioPos = x.Ticket,
                        Fecha = x.Hora,
                        Vendedor = x.Vendedor,
                        Hotel = x.Hotel,
                        Origen = x.Origen,
                        Sitio = x.Sitio,
                        Destino = x.Destino,
                        Unidad = x.Unidad,
                        Placas = x.Placas,
                        Telefono = x.Telefono,
                        Nacionalidad = CleanNationality(x.Nacionalidad),
                        Pax = x.Pax,
                        Gafete = x.Gafete,
                        TaxistaId = 0,
                        TaxistaNombre = x.Vendedor,
                        TransporteTipo = x.TipoOperacion,
                        Venta = 0m,
                        Dejada = x.Total,
                        DejadaPagada = string.Equals(x.PayoutStatus, "pagado", StringComparison.OrdinalIgnoreCase) ? x.Total : 0m,
                        Comision = 0m,
                        Pago = 0m,
                        Estatus = "APP MOVIL",
                        EstatusDejada = FirstText(x.PayoutStatus, "pendiente"),
                        FechaPagoDejada = x.PayoutDate,
                        UsuarioPagoDejada = x.PayoutUser,
                        TicketPagoDejada = x.PayoutTicket,
                        PuedePagarDejada = x.Total > 0 && !string.Equals(x.PayoutStatus, "pagado", StringComparison.OrdinalIgnoreCase)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar relaciones desde la API APP_TAXI.");
                return new List<PosRelacionRowViewModel>();
            }
        }

        private async Task EnrichRelacionesWithCalculatedComisionesAsync(List<PosRelacionRowViewModel> relations, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (relations.Count == 0)
                return;

            var effectiveInicio = fechaInicio;
            var effectiveFin = fechaFin;
            if (!effectiveInicio.HasValue && !effectiveFin.HasValue)
            {
                var relationDates = relations
                    .Select(x => TryParseRelacionDate(x.Fecha)?.Date)
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
                if (relationDates.Count > 0)
                {
                    effectiveInicio = relationDates.First();
                    effectiveFin = relationDates.Last();
                }
            }

            var comisionesModel = await TryGetComisionesAsync(null, effectiveInicio, effectiveFin);
            var comisiones = comisionesModel?.Comisiones ?? new List<PosComisionRowViewModel>();
            if (comisiones.Count == 0)
                return;

            foreach (var relation in relations)
            {
                var relationDate = TryParseRelacionDate(relation.Fecha)?.Date;
                var relationTickets = new[]
                {
                    NormalizeRelationToken(relation.FolioPos),
                    NormalizeRelationToken(relation.FolioPosSugerido),
                    NormalizeRelationToken(relation.TicketPagoDejada)
                }.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var relationFolios = new[]
                {
                    NormalizeRelationToken(relation.FolioOperacion),
                    NormalizeRelationToken(relation.FolioOperacionSugerido),
                    NormalizeRelationToken(relation.FolioControl),
                    NormalizeRelationToken(relation.FolioApp)
                }.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

                bool MatchesComision(PosComisionRowViewModel item, bool requireDate)
                {
                    var itemTicket = NormalizeRelationToken(item.Ticket);
                    var itemFolio = NormalizeRelationToken(item.Folio);
                    var itemDate = TryParseComisionDate(item.Fecha)?.Date;
                    if (requireDate && relationDate.HasValue && itemDate.HasValue && relationDate.Value != itemDate.Value)
                        return false;

                    if (!string.IsNullOrWhiteSpace(itemTicket) && relationTickets.Contains(itemTicket))
                        return true;

                    return !string.IsNullOrWhiteSpace(itemFolio) && relationFolios.Contains(itemFolio);
                }

                var match = comisiones.FirstOrDefault(item => MatchesComision(item, requireDate: true))
                    ?? comisiones.FirstOrDefault(item => MatchesComision(item, requireDate: false));

                if (match != null)
                {
                    if (match.Venta > relation.Venta)
                        relation.Venta = match.Venta;
                    relation.Comision = match.Importe;
                    if (match.Pago > 0m)
                        relation.Pago = match.Pago;
                }
            }
        }

        private async Task EnrichRelacionesWithPaymentNamesAsync(List<PosRelacionRowViewModel> relations)
        {
            if (relations.Count == 0)
                return;

            var relationTickets = relations
                .SelectMany(GetRelationPaymentTickets)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10000)
                .ToList();
            if (relationTickets.Count == 0)
                return;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            var parameters = relationTickets
                .Select((ticket, index) =>
                {
                    var name = $"@relPayTicket{index}";
                    AddParameter(command, name, ticket);
                    return name;
                })
                .ToList();

            var values = string.Join(", ", parameters.Select(x => $"({x})"));
            var compuadmoPaymentDatabase = _compuadmoStoreDatabaseName;
            var joyeriaPaymentDatabase = _joyeriaStoreDatabaseName;
            command.CommandText = $@"
SELECT Ticket, FormaPago, Total
FROM
(
    SELECT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura)))) AS Ticket,
        LTRIM(RTRIM(COALESCE(m.Nombre, ''))) AS FormaPago,
        CAST(COALESCE(p.total, 0) AS decimal(18,2)) AS Total
    FROM {QuoteSqlIdentifier(compuadmoPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[pagosM] p
    INNER JOIN {QuoteSqlIdentifier(compuadmoPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[monedas] m
        ON m.moneda = p.moneda
    INNER JOIN (VALUES {values}) v(Ticket)
        ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura)))) = v.Ticket

    UNION ALL

    SELECT
        UPPER(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura)))) AS Ticket,
        LTRIM(RTRIM(COALESCE(m.Nombre, ''))) AS FormaPago,
        CAST(COALESCE(p.total, 0) AS decimal(18,2)) AS Total
    FROM {QuoteSqlIdentifier(joyeriaPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[pagosM] p
    INNER JOIN {QuoteSqlIdentifier(joyeriaPaymentDatabase)}.{QuoteSqlIdentifier(_storeSchemaName)}.[Monedas] m
        ON m.moneda = p.moneda
    INNER JOIN (VALUES {values}) v(Ticket)
        ON UPPER(LTRIM(RTRIM(CONVERT(nvarchar(120), p.folio_factura)))) = v.Ticket
) Pagos
WHERE Total <> 0;";

            var rows = await ReadRowsAsync(command);
            var paymentsByTicket = rows
                .GroupBy(row => PickText(row, "Ticket"), StringComparer.OrdinalIgnoreCase)
                .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                .ToDictionary(
                    group => group.Key,
                    group => string.Join(" / ", group
                        .Select(row =>
                        {
                            var name = PickText(row, "FormaPago");
                            var total = ToDecimal(PickObject(row, "Total"));
                            return total == 0m ? name : $"{name} {total:C2}";
                        })
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var relation in relations)
            {
                var formas = GetRelationPaymentTickets(relation)
                    .Select(ticket => paymentsByTicket.TryGetValue(ticket, out var formaPago) ? formaPago : string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                relation.FormaPago = string.Join(" / ", formas);
            }
        }

        private static IEnumerable<string> GetRelationPaymentTickets(PosRelacionRowViewModel relation)
        {
            return new[]
                {
                    relation.FolioPos,
                    relation.FolioPosSugerido,
                    relation.TicketPagoDejada
                }
                .SelectMany(SplitRelationTokens)
                .Select(NormalizeRelationToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!);
        }

        private static IEnumerable<string> SplitRelationTokens(string? value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static PosRelacionRowViewModel MergeRelacionGroup(IEnumerable<PosRelacionRowViewModel> items)
        {
            var list = items
                .OrderByDescending(item => item.Comision > 0m || item.Venta > 0m)
                .ThenByDescending(item => item.DejadaPagada > 0m || item.Pago > 0m)
                .ThenByDescending(HasOperacionOrTicket)
                .ThenBy(item => item.Fuente.Contains("APP MOVIL", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(item => !string.IsNullOrWhiteSpace(item.FolioPos))
                .ToList();

            var first = list.First();
            var bestFecha = list
                .Select(x => new
                {
                    Row = x,
                    Fecha = TryParseRelacionDate(x.Fecha)
                })
                .Where(x => x.Fecha.HasValue)
                .OrderBy(x => x.Row.Fuente.Contains("APP MOVIL", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.Fecha!.Value.TimeOfDay == TimeSpan.Zero ? 1 : 0)
                .ThenByDescending(x => x.Fecha)
                .Select(x => x.Row.Fecha)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            first.FolioPos = JoinDistinctRelationValues(list.Select(x => x.FolioPos));
            var identityRow = list
                .Where(x => !string.IsNullOrWhiteSpace(x.Gafete) || !string.IsNullOrWhiteSpace(x.TaxistaNombre))
                .OrderBy(x => x.Fuente.Contains("APP MOVIL", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.Gafete) ? 1 : 0)
                .ThenBy(x => string.IsNullOrWhiteSpace(x.TaxistaNombre) ? 1 : 0)
                .FirstOrDefault();
            if (identityRow != null)
            {
                first.Gafete = identityRow.Gafete;
                first.TaxistaId = identityRow.TaxistaId;
                first.TaxistaNombre = identityRow.TaxistaNombre;
            }
            first.TicketPagoDejada = JoinDistinctRelationValues(list.Select(x => x.TicketPagoDejada));
            first.Fecha = FirstText(bestFecha, first.Fecha, list.Select(x => x.Fecha).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.FolioControl = FirstText(first.FolioControl, list.Select(x => x.FolioControl).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.FolioApp = FirstText(first.FolioApp, list.Select(x => x.FolioApp).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.FolioOperacion = FirstText(first.FolioOperacion, list.Select(x => x.FolioOperacion).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Vendedor = FirstText(first.Vendedor, list.Select(x => x.Vendedor).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.VendedorAsignado = FirstText(first.VendedorAsignado, list.Select(x => x.VendedorAsignado).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.TaxistaNombre = FirstText(first.TaxistaNombre, list.Select(x => x.TaxistaNombre).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.TransporteTipo = FirstText(first.TransporteTipo, list.Select(x => x.TransporteTipo).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Nacionalidad = FirstText(CleanNationality(first.Nacionalidad), list.Select(x => CleanNationality(x.Nacionalidad)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Hotel = FirstText(first.Hotel, list.Select(x => x.Hotel).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Telefono = FirstText(first.Telefono, list.Select(x => x.Telefono).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Origen = FirstText(first.Origen, list.Select(x => x.Origen).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Sitio = FirstText(first.Sitio, list.Select(x => x.Sitio).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Destino = FirstText(first.Destino, list.Select(x => x.Destino).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Unidad = FirstText(first.Unidad, list.Select(x => x.Unidad).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Placas = FirstText(first.Placas, list.Select(x => x.Placas).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Pax = list.Max(x => x.Pax);
            first.Venta = list.Max(x => x.Venta);
            first.Dejada = list.Max(x => x.Dejada);
            first.DejadaPagada = list.Max(x => x.DejadaPagada);
            first.Comision = list.Max(x => x.Comision);
            first.Pago = list.Max(x => x.Pago);
            first.FormaPago = JoinDistinctRelationValues(list.Select(x => x.FormaPago));
            var dejadaPagada = first.DejadaPagada > 0m
                || list.Any(x => string.Equals(x.EstatusDejada, "pagado", StringComparison.OrdinalIgnoreCase));
            if (dejadaPagada && first.DejadaPagada <= 0m && first.Dejada > 0m)
                first.DejadaPagada = first.Dejada;
            first.PuedePagarDejada = first.Dejada > 0m && !dejadaPagada && list.Any(x => x.PuedePagarDejada);
            first.EstatusDejada = first.Dejada <= 0m && first.DejadaPagada <= 0m
                ? "sin importe"
                : dejadaPagada
                    ? "pagado"
                    : "pendiente";
            first.UsuarioPagoDejada = FirstText(first.UsuarioPagoDejada, list.Select(x => x.UsuarioPagoDejada).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.FechaPagoDejada = FirstText(first.FechaPagoDejada, list.Select(x => x.FechaPagoDejada).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            return first;
        }

        private static List<PosRelacionRowViewModel> MergeRelacionDuplicates(IEnumerable<PosRelacionRowViewModel> items)
        {
            var byFolio = items
                .GroupBy(BuildRelacionMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(MergeRelacionGroup)
                .ToList();

            return byFolio
                .GroupBy(BuildRelacionTripDuplicateKey, StringComparer.OrdinalIgnoreCase)
                .Select(MergeRelacionGroup)
                .ToList();
        }

        private static bool HasOperacionOrTicket(PosRelacionRowViewModel row)
        {
            return !string.IsNullOrWhiteSpace(row.FolioOperacion)
                || !string.IsNullOrWhiteSpace(row.FolioPos)
                || !string.IsNullOrWhiteSpace(row.TicketPagoDejada)
                || !string.IsNullOrWhiteSpace(row.FolioOperacionSugerido)
                || !string.IsNullOrWhiteSpace(row.FolioPosSugerido);
        }

        private static string BuildRelacionMergeKey(PosRelacionRowViewModel row)
        {
            var primary = FirstText(
                NormalizeRelationToken(row.FolioOperacion),
                NormalizeRelationToken(row.FolioControl),
                NormalizeRelationToken(row.TicketPagoDejada),
                NormalizeRelationToken(row.FolioPos),
                NormalizeRelationToken(row.FolioApp));

            if (!string.IsNullOrWhiteSpace(primary))
            {
                var fecha = TryParseRelacionDate(row.Fecha);
                var fechaKey = fecha.HasValue
                    ? fecha.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                    : Normalize(row.Fecha) ?? string.Empty;
                return string.IsNullOrWhiteSpace(fechaKey) ? primary : $"{primary}|{fechaKey}";
            }

            return string.Join("|", new[]
            {
                row.Fecha ?? string.Empty,
                Normalize(row.Hotel) ?? string.Empty,
                Normalize(row.TaxistaNombre) ?? string.Empty,
                row.Dejada.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static string BuildRelacionTripDuplicateKey(PosRelacionRowViewModel row)
        {
            if (!row.Fuente.Contains("APP MOVIL", StringComparison.OrdinalIgnoreCase))
                return BuildRelacionMergeKey(row);

            var fecha = TryParseRelacionDate(row.Fecha);
            var fechaKey = fecha.HasValue
                ? fecha.Value.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)
                : Normalize(row.Fecha) ?? string.Empty;

            var tripKey = string.Join("|", new[]
            {
                "APPTRIP",
                fechaKey,
                Normalize(row.TaxistaNombre) ?? string.Empty,
                Normalize(row.Hotel) ?? string.Empty,
                Normalize(row.Unidad) ?? string.Empty,
                Normalize(row.Telefono) ?? string.Empty,
                Normalize(row.Gafete) ?? string.Empty,
                row.Pax.ToString(CultureInfo.InvariantCulture),
                row.Dejada.ToString(CultureInfo.InvariantCulture)
            });

            if (tripKey.Replace("|", string.Empty).Length > "APPTRIP".Length)
                return tripKey;

            return BuildRelacionMergeKey(row);
        }

        private async Task EnrichRelationNationalitiesFromCatalogAsync(List<PosRelacionRowViewModel> relations)
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in relations.Where(x => string.IsNullOrWhiteSpace(CleanNationality(x.Nacionalidad))).Take(80))
            {
                var nationality = await ResolveCatalogNationalityAsync(
                    cache,
                    row.TaxistaId > 0 ? row.TaxistaId.ToString(CultureInfo.InvariantCulture) : null,
                    row.TaxistaNombre,
                    row.Gafete);
                if (!string.IsNullOrWhiteSpace(nationality))
                    row.Nacionalidad = nationality;
            }
        }

        private async Task EnrichRegistroNationalitiesFromCatalogAsync(List<PosOperacionRowViewModel> rows)
        {
            await EnrichRegistroNationalitiesFromLocalAsync(rows);

            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.Where(x => string.IsNullOrWhiteSpace(CleanNationality(x.Nacionalidad))).Take(100))
            {
                var nationality = await ResolveCatalogNationalityAsync(
                    cache,
                    row.CatalogId?.ToString(CultureInfo.InvariantCulture),
                    row.Vendedor,
                    row.Gafete);
                if (!string.IsNullOrWhiteSpace(nationality))
                    row.Nacionalidad = nationality;
            }
        }

        private async Task EnrichRelationNationalitiesFromLocalAsync(List<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (rows.Count == 0 || rows.All(x => !string.IsNullOrWhiteSpace(CleanNationality(x.Nacionalidad))))
                return;

            var local = await LoadLocalNationalityRowsAsync(fechaInicio, fechaFin);
            foreach (var row in rows.Where(x => string.IsNullOrWhiteSpace(CleanNationality(x.Nacionalidad))))
            {
                var match = local.FirstOrDefault(info => LocalNationalityMatchesFolio(info, row.FolioControl, row.FolioApp, row.FolioOperacion, row.FolioPos, row.TicketPagoDejada));
                if (!string.IsNullOrWhiteSpace(match?.Nacionalidad))
                    row.Nacionalidad = match.Nacionalidad;
            }
        }

        private async Task EnrichRegistroNationalitiesFromLocalAsync(List<PosOperacionRowViewModel> rows)
        {
            if (rows.Count == 0 || rows.All(x => !string.IsNullOrWhiteSpace(CleanNationality(x.Nacionalidad))))
                return;

            var dates = rows.Select(x => TryParseRegistroDate(x.Hora)?.Date).Where(x => x.HasValue).Select(x => x!.Value).ToList();
            var fechaInicio = dates.Count > 0 ? dates.Min() : (DateTime?)null;
            var fechaFin = dates.Count > 0 ? dates.Max() : (DateTime?)null;
            var local = await LoadLocalNationalityRowsAsync(fechaInicio, fechaFin);
            foreach (var row in rows.Where(x => string.IsNullOrWhiteSpace(CleanNationality(x.Nacionalidad))))
            {
                var match = local.FirstOrDefault(info => LocalNationalityMatchesFolio(info, row.FolioControl, row.FolioOperacion, row.Ticket));
                if (!string.IsNullOrWhiteSpace(match?.Nacionalidad))
                    row.Nacionalidad = match.Nacionalidad;
            }
        }

        private async Task<List<LocalNationalityInfo>> LoadLocalNationalityRowsAsync(DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (2000)
    COALESCE(folio_app, '') AS FolioApp,
    COALESCE(folio_app_original, '') AS FolioOriginal,
    COALESCE(folio_pos, '') AS FolioPos,
    COALESCE(vendedor_nombre, '') AS Taxista,
    COALESCE(folio_gafete, '') AS Gafete,
    COALESCE(unidad, '') AS Unidad,
    COALESCE(nacionalidad, '') AS Nacionalidad,
    fecha_operacion AS Fecha
FROM {PosTable("AppMovilRegistro")}
WHERE NULLIF(LTRIM(RTRIM(COALESCE(nacionalidad, ''))), '') IS NOT NULL
  AND UPPER(LTRIM(RTRIM(COALESCE(nacionalidad, '')))) NOT IN ('NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-')
  AND (@inicio IS NULL OR fecha_operacion >= @inicio)
  AND (@finExclusiva IS NULL OR fecha_operacion < @finExclusiva)
ORDER BY fecha_operacion DESC",
                    ("@inicio", fechaInicio?.Date),
                    ("@finExclusiva", fechaFin?.Date.AddDays(1)));

                return rows
                    .Select(row => new LocalNationalityInfo(
                        PickText(row, "FolioApp"),
                        PickText(row, "FolioOriginal"),
                        PickText(row, "FolioPos"),
                        PickText(row, "Taxista"),
                        PickText(row, "Gafete"),
                        PickText(row, "Unidad"),
                        CleanNationality(PickText(row, "Nacionalidad"))))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Nacionalidad))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No fue posible completar nacionalidad desde AppMovilRegistro local.");
                return new List<LocalNationalityInfo>();
            }
        }

        private static bool LocalNationalityMatchesFolio(LocalNationalityInfo info, params string?[] folios)
        {
            var infoFolios = new[] { info.FolioApp, info.FolioOriginal, info.FolioPos }
                .Select(NormalizeRelationToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (infoFolios.Count == 0)
                return false;

            return folios
                .Select(NormalizeRelationToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Any(infoFolios.Contains);
        }

        private static bool LocalNationalityMatches(LocalNationalityInfo info, string? taxista, string? gafete, string? unidad)
        {
            var rowName = Normalize(taxista);
            var infoName = Normalize(info.Taxista);
            if (string.IsNullOrWhiteSpace(rowName) || !string.Equals(rowName, infoName, StringComparison.OrdinalIgnoreCase))
                return false;

            var rowBadges = SplitCatalogTokens(gafete).Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var infoBadges = SplitCatalogTokens(info.Gafete).Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (rowBadges.Count > 0 && infoBadges.Count > 0)
                return rowBadges.Overlaps(infoBadges);

            var rowUnit = Normalize(unidad);
            var infoUnit = Normalize(info.Unidad);
            return !string.IsNullOrWhiteSpace(rowUnit)
                && !string.IsNullOrWhiteSpace(infoUnit)
                && string.Equals(rowUnit, infoUnit, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record LocalNationalityInfo(string FolioApp, string FolioOriginal, string FolioPos, string Taxista, string Gafete, string Unidad, string Nacionalidad);

        private async Task<string> ResolveCatalogNationalityAsync(Dictionary<string, string> cache, string? taxistaId, string? taxistaNombre, string? gafete)
        {
            foreach (var query in BuildCatalogNationalityQueries(taxistaId, taxistaNombre, gafete))
            {
                if (cache.TryGetValue(query, out var cached))
                    return cached;

                var options = await _appTaxiApi.SearchCatalogTaxistasAsync(query, 20);
                var match = options.FirstOrDefault(option => CatalogTaxistaMatches(option, taxistaId, taxistaNombre, gafete));
                var nationality = CleanNationality(match?.Nacionalidad);
                cache[query] = nationality;
                if (!string.IsNullOrWhiteSpace(nationality))
                    return nationality;
            }

            return string.Empty;
        }

        private static IEnumerable<string> BuildCatalogNationalityQueries(string? taxistaId, string? taxistaNombre, string? gafete)
        {
            var values = new[]
            {
                Normalize(taxistaId),
                SplitCatalogTokens(gafete).FirstOrDefault(),
                Normalize(taxistaNombre)
            };

            return values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)!;
        }

        private static bool CatalogTaxistaMatches(PosRegistroAppTaxistaOption option, string? taxistaId, string? taxistaNombre, string? gafete)
        {
            var id = Normalize(taxistaId);
            if (!string.IsNullOrWhiteSpace(id) && string.Equals(Normalize(option.Clave), id, StringComparison.OrdinalIgnoreCase))
                return true;

            var optionBadges = SplitCatalogTokens(option.Gafete).Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rowBadges = SplitCatalogTokens(gafete).Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (optionBadges.Count > 0 && rowBadges.Count > 0 && optionBadges.Overlaps(rowBadges))
                return true;

            var optionName = Normalize(option.Nombre);
            var rowName = Normalize(taxistaNombre);
            return !string.IsNullOrWhiteSpace(optionName)
                && !string.IsNullOrWhiteSpace(rowName)
                && string.Equals(optionName, rowName, StringComparison.OrdinalIgnoreCase);
        }

        private static string CleanNationality(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
                return string.Empty;

            var normalized = Normalize(text);
            return normalized is "NO CAPTURADA" or "SIN CAPTURAR" or "SIN NACIONALIDAD" or "-"
                ? string.Empty
                : text;
        }

        private static string NormalizeRelationToken(string? value)
        {
            var text = Normalize(value);
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : text;
        }

        private static string JoinDistinctRelationValues(IEnumerable<string?> values)
        {
            var result = values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .SelectMany(x => (x ?? string.Empty)
                    .Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join(", ", result);
        }

        private async Task<List<PosRelacionRowViewModel>> GetRelacionesRowsFromDejadasAsync(string? normalized, DateTime? fechaInicio = null, DateTime? fechaFin = null, bool exactReport = false)
        {
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();
                var dejadasNacionalidadSql = await PosColumnExistsAsync(connection, "dejadas", "nacionalidad")
                    ? "COALESCE(CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(d.nacionalidad, '')))) IN ('', 'NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-') THEN NULL ELSE d.nacionalidad END, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(a.nacionalidad, '')))) IN ('', 'NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-') THEN NULL ELSE a.nacionalidad END, '')"
                    : "COALESCE(CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(a.nacionalidad, '')))) IN ('', 'NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-') THEN NULL ELSE a.nacionalidad END, '')";
                var topClause = exactReport ? string.Empty : "TOP (2000)";
                var innerTopClause = exactReport ? "TOP (2147483647)" : "TOP (2000)";

                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT {topClause}
    COALESCE(c.FolioControl, r.FolioApp, d.codigorecepcion, CONVERT(nvarchar(60), d.folioregistro), d.folioregistrostr, '') AS FolioControl,
      COALESCE(r.FolioApp, '') AS FolioApp,
      CASE WHEN NULLIF(r.FolioApp, '') IS NULL THEN 'SISTEMA/POS' ELSE 'APP MOVIL + SISTEMA/POS' END AS Fuente,
      COALESCE(NULLIF(r.Usuario, ''), 'POS') AS UsuarioOrigen,
      COALESCE(NULLIF(r.FolioOperacion, ''), CONVERT(nvarchar(60), d.folioregistro), d.folioregistrostr, '') AS FolioOperacion,
      COALESCE(NULLIF(r.FolioPos, ''), '') AS FolioPos,
      COALESCE(NULLIF(r.Vendedor, ''), '') AS VendedorAsignado,
      COALESCE(a.fecha_operacion, d.fecha) AS Fecha,
    COALESCE(NULLIF(CONVERT(nvarchar(40), d.hora), ''), '') AS HoraTexto,
    COALESCE(d.nombrevendedor, d.nombrestaff, '') AS Vendedor,
    COALESCE(NULLIF(a.hotel, ''), NULLIF(d.hotel, ''), d.nombrealmacen, '') AS Hotel,
    COALESCE(NULLIF(a.sitio, ''), '') AS Sitio,
    COALESCE(NULLIF(a.origen, ''), '') AS Origen,
    COALESCE(d.pax, 0) AS Pax,
    COALESCE(NULLIF(a.folio_gafete, ''), NULLIF(d.gafete, ''), NULLIF(r.Gafete, ''), '') AS Gafete,
    COALESCE(NULLIF(d.idtaxi, 0), NULLIF(a.id_catalogo, 0), r.TaxistaId, 0) AS TaxistaId,
    COALESCE(NULLIF(d.nombrestaff, ''), NULLIF(r.TaxistaNombre, ''), NULLIF(a.vendedor_nombre, ''), NULLIF(d.nombrevendedor, ''), '') AS TaxistaNombre,
    COALESCE(NULLIF(r.TransporteTipo, ''), d.tipotransporte, '') AS TransporteTipo,
    {dejadasNacionalidadSql} AS Nacionalidad,
    COALESCE(NULLIF(d.unidad, ''), NULLIF(a.unidad, ''), '') AS Unidad,
    COALESCE(NULLIF(d.telefono, ''), NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
    COALESCE(NULLIF(a.placas, ''), '') AS Placas,
    COALESCE(NULLIF(a.destino, ''), '') AS Destino,
    COALESCE(NULLIF(d.totalventa, 0), NULLIF(AppStoreVenta.StoreVenta, 0), 0) AS Venta,
    COALESCE(NULLIF(AppStoreVenta.StoreTicket, ''), '') AS VentaTicket,
      COALESCE(d.total, NULLIF(r.Dejada, 0), 0) AS Dejada,
    COALESCE(d.pago, 0) AS DejadaPagada,
    COALESCE(d.comision, 0) AS Comision,
    COALESCE(d.pago, 0) AS Pago,
    d.fechapago AS FechaPagoDejada,
    COALESCE(d.codigorecepcion, '') AS TicketPagoDejada,
    COALESCE(NULLIF(r.Observaciones, ''), '') AS Observaciones,
    COALESCE(NULLIF(CONVERT(nvarchar(60), d.idstaff), ''), '') AS IdStaff
FROM
(
    SELECT {innerTopClause} d0.*
    FROM {PosTable("dejadas")} d0
    WHERE (@fechaInicio IS NULL OR d0.fecha >= @fechaInicio)
      AND (@fechaFinExclusiva IS NULL OR d0.fecha < @fechaFinExclusiva)
      AND (
            @q IS NULL
         OR CONVERT(nvarchar(60), d0.folioregistro) LIKE '%' + @q + '%'
         OR d0.folioregistrostr LIKE '%' + @q + '%'
         OR d0.codigorecepcion LIKE '%' + @q + '%'
         OR d0.gafete LIKE '%' + @q + '%'
         OR d0.nombrevendedor LIKE '%' + @q + '%'
         OR d0.nombrestaff LIKE '%' + @q + '%'
         OR d0.hotel LIKE '%' + @q + '%'
         OR d0.unidad LIKE '%' + @q + '%'
         OR d0.telefono LIKE '%' + @q + '%'
         OR CONVERT(nvarchar(60), d0.idtaxi) LIKE '%' + @q + '%'
         OR (
                @q NOT LIKE '%[^0-9]%'
            AND LEN(@q) BETWEEN 1 AND 18
            AND ISNUMERIC(CONVERT(nvarchar(60), d0.folioregistro)) = 1
            AND CONVERT(bigint, d0.folioregistro) = CONVERT(bigint, @q)
            )
      )
    ORDER BY d0.fecha DESC, d0.folioregistro DESC
) d
OUTER APPLY
(
    SELECT TOP (1) r0.*
    FROM {PosTable("RelacionTicketTaxista")} r0
    LEFT JOIN {PosTable("AppMovilRegistro")} ar
        ON ar.folio_app = r0.FolioApp OR ar.folio_app_original = r0.FolioApp
    WHERE (
            r0.FolioOperacion = CONVERT(nvarchar(60), d.folioregistro)
         OR r0.FolioOperacion = d.folioregistrostr
         OR (
                r0.FolioOperacion NOT LIKE '%[^0-9]%'
            AND LEN(r0.FolioOperacion) BETWEEN 1 AND 18
            AND CONVERT(bigint, r0.FolioOperacion) = CONVERT(bigint, d.folioregistro)
            )
         OR (
                r0.FolioApp NOT LIKE '%[^0-9]%'
            AND LEN(r0.FolioApp) BETWEEN 1 AND 18
            AND CONVERT(bigint, r0.FolioApp) = CONVERT(bigint, d.folioregistro)
            )
         OR r0.FolioOperacion = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
         OR r0.FolioApp = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
    )
      AND (ar.fecha_operacion IS NULL OR CAST(ar.fecha_operacion AS date) = CAST(d.fecha AS date))
    ORDER BY
        CASE WHEN ar.fecha_operacion IS NOT NULL AND CAST(ar.fecha_operacion AS date) = CAST(d.fecha AS date) THEN 0 ELSE 1 END,
        r0.FechaActualizacion DESC
) r
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioAppOriginal = r.FolioApp
    OR c.FolioControl = d.codigorecepcion
    OR c.FolioControl = d.folioregistrostr
    OR c.FolioAppOriginal = d.codigorecepcion
    OR c.FolioAppOriginal = d.folioregistrostr
LEFT JOIN {PosTable("AppMovilRegistro")} a
    ON (
        CAST(a.fecha_operacion AS date) = CAST(d.fecha AS date)
        AND (
            a.folio_app = r.FolioApp
            OR a.folio_app_original = r.FolioApp
        )
    )
    OR (
        CAST(a.fecha_operacion AS date) = CAST(d.fecha AS date)
        AND (
            a.folio_app = d.codigorecepcion
            OR a.folio_app_original = d.codigorecepcion
            OR a.folio_pos = d.codigorecepcion
            OR a.folio_app = d.folioregistrostr
            OR a.folio_app_original = d.folioregistrostr
            OR a.folio_pos = d.folioregistrostr
            OR (ISNUMERIC(a.folio_app) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, d.folioregistro))
            OR (ISNUMERIC(a.folio_app_original) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, d.folioregistro))
            OR a.folio_app = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
            OR a.folio_app_original = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
        )
    )
OUTER APPLY
(
    SELECT TOP (1) StoreVenta, StoreTicket
    FROM
    (
        SELECT
            COALESCE(comp.total, 0) AS StoreVenta,
            CONVERT(nvarchar(80), comp.folio_remision) AS StoreTicket,
            comp.fecha AS Fecha
        FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] comp
        WHERE LTRIM(RTRIM(COALESCE(a.folio_app,''))) NOT LIKE '%[^0-9]%' AND LEN(LTRIM(RTRIM(COALESCE(a.folio_app,'')))) > 0
          AND comp.folioregistro = CONVERT(int, LTRIM(RTRIM(a.folio_app)))
          AND UPPER(LTRIM(RTRIM(COALESCE(comp.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
        UNION ALL
        SELECT
            COALESCE(joy.total, 0) AS StoreVenta,
            COALESCE(NULLIF(CONVERT(nvarchar(80), joy.folio_pedido), ''), CONVERT(nvarchar(80), joy.folio_factura)) AS StoreTicket,
            joy.fecha AS Fecha
        FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] joy
        WHERE LTRIM(RTRIM(COALESCE(a.folio_app,''))) NOT LIKE '%[^0-9]%' AND LEN(LTRIM(RTRIM(COALESCE(a.folio_app,'')))) > 0
          AND joy.folio_registro = CONVERT(int, LTRIM(RTRIM(a.folio_app)))
          AND UPPER(LTRIM(RTRIM(COALESCE(joy.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
    ) _StoreRows
    ORDER BY _StoreRows.StoreVenta DESC, _StoreRows.Fecha DESC
) AppStoreVenta
ORDER BY d.fecha DESC, d.folioregistro DESC",
                    ("@q", normalized),
                    ("@fechaInicio", fechaInicio?.Date),
                    ("@fechaFinExclusiva", fechaFin?.Date.AddDays(1)));

                var foliosConVentaPendiente = rows
                    .Where(row => ToDecimal(PickObject(row, "Venta")) <= 0m)
                    .Select(row => PickText(row, "FolioOperacion"))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var storeVentas = await LoadStoreVentasByFolioRegistroAsync(
                    foliosConVentaPendiente,
                    fechaInicio,
                    fechaFin,
                    exactReport);

                var result = rows.Select(row =>
                {
                    var dejada = ToDecimal(PickObject(row, "Dejada"));
                    var pagada = ToDecimal(PickObject(row, "DejadaPagada"));
                    var folioOperacion = PickText(row, "FolioOperacion");
                    storeVentas.TryGetValue(folioOperacion, out var storeVenta);
                    var venta = ToDecimal(PickObject(row, "Venta"));
                    var comision = ToDecimal(PickObject(row, "Comision"));
                    return new PosRelacionRowViewModel
                    {
                        FolioControl = PickText(row, "FolioControl"),
                        FolioApp = PickText(row, "FolioApp"),
                        Fuente = PickText(row, "Fuente"),
                        UsuarioOrigen = PickText(row, "UsuarioOrigen"),
                        FolioOperacion = folioOperacion,
                        FolioPos = FirstText(PickText(row, "FolioPos"), PickText(row, "VentaTicket"), storeVenta?.Ticket),
                        Fecha = ResolveRelacionDateDisplay(row, "Fecha", "HoraTexto"),
                        Vendedor = PickText(row, "Vendedor"),
                        Hotel = PickText(row, "Hotel"),
                        Sitio = PickText(row, "Sitio"),
                        Origen = PickText(row, "Origen"),
                        Pax = PickInt(row, "Pax"),
                        Gafete = PickText(row, "Gafete"),
                        Nacionalidad = PickText(row, "Nacionalidad"),
                        Unidad = PickText(row, "Unidad"),
                        Telefono = PickText(row, "Telefono"),
                        Placas = PickText(row, "Placas"),
                        Destino = PickText(row, "Destino"),
                        TaxistaId = Convert.ToInt64(ToDecimal(PickObject(row, "TaxistaId"))),
                        TaxistaNombre = FirstText(PickText(row, "TaxistaNombre"), PickText(row, "Vendedor")),
                        TransporteTipo = PickText(row, "TransporteTipo"),
                        Venta = venta,
                        Dejada = dejada,
                        DejadaPagada = pagada,
                        Comision = comision,
                        Pago = ToDecimal(PickObject(row, "Pago")),
                        EstatusDejada = pagada > 0m ? "pagado" : "pendiente",
                        FechaPagoDejada = PickDate(row, "FechaPagoDejada"),
                        TicketPagoDejada = PickText(row, "TicketPagoDejada"),
                        Observaciones = PickText(row, "Observaciones"),
                        PuedePagarDejada = dejada > 0m && pagada <= 0m,
                        Estatus = pagada > 0m ? "DEJADA PAGADA" : "DEJADA PENDIENTE"
                    };
                }).ToList();

                return exactReport ? result : result.Take(120).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar dejadas desde dbo.dejadas.");
                return new List<PosRelacionRowViewModel>();
            }
        }

        private static bool IsWithinDateRange(string? fechaText, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (!fechaInicio.HasValue && !fechaFin.HasValue)
                return true;

            if (!DateTime.TryParse(fechaText, CultureInfo.CurrentCulture, DateTimeStyles.None, out var fecha)
                && !DateTime.TryParse(fechaText, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
                return false;

            var date = fecha.Date;
            if (fechaInicio.HasValue && date < fechaInicio.Value.Date)
                return false;
            if (fechaFin.HasValue && date > fechaFin.Value.Date)
                return false;
            return true;
        }

        private async Task<Dictionary<string, StoreRelacionVentaInfo>> LoadStoreVentasByFolioRegistroAsync(
            IReadOnlyCollection<string> folios,
            DateTime? fechaInicio,
            DateTime? fechaFin,
            bool exactReport = false)
        {
            var normalizedFolios = folios
                .Select(Normalize)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x!.All(char.IsDigit))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedFolios.Count == 0)
                return new Dictionary<string, StoreRelacionVentaInfo>(StringComparer.OrdinalIgnoreCase);

            var parameters = new List<(string Name, object? Value)>();
            var folioParams = new List<string>();
            for (var i = 0; i < normalizedFolios.Count; i++)
            {
                var name = $"@folio{i}";
                folioParams.Add(name);
                parameters.Add((name, normalizedFolios[i]));
            }

            var dateFilterComp = string.Empty;
            var dateFilterJoy = string.Empty;
            if (fechaInicio.HasValue)
            {
                dateFilterComp += " AND comp.fecha >= @fechaInicio";
                dateFilterJoy += " AND joy.fecha >= @fechaInicio";
                parameters.Add(("@fechaInicio", fechaInicio.Value.Date));
            }
            if (fechaFin.HasValue)
            {
                dateFilterComp += " AND comp.fecha < @fechaFinExclusiva";
                dateFilterJoy += " AND joy.fecha < @fechaFinExclusiva";
                parameters.Add(("@fechaFinExclusiva", fechaFin.Value.Date.AddDays(1)));
            }

            var inSql = string.Join(",", folioParams);
            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT
    FolioRegistro,
    Ticket,
    TotalVenta,
    Fecha
FROM
(
      SELECT
        CONVERT(nvarchar(60), comp.folioregistro) AS FolioRegistro,
        CONVERT(nvarchar(80), comp.folio_remision) AS Ticket,
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(comp.estatus, '')))) = 'C' THEN 0 ELSE COALESCE(comp.total, 0) END AS TotalVenta,
        comp.fecha AS Fecha,
        COALESCE(comp.estatus, '') AS Estatus
      FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] comp
    WHERE CONVERT(nvarchar(60), comp.folioregistro) IN ({inSql})
      {dateFilterComp}

    UNION ALL

      SELECT
        CONVERT(nvarchar(60), joy.folio_registro) AS FolioRegistro,
        COALESCE(NULLIF(CONVERT(nvarchar(80), joy.folio_pedido), ''), NULLIF(CONVERT(nvarchar(80), joy.folio_factura), '')) AS Ticket,
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(joy.estatus, '')))) = 'C' THEN 0 ELSE COALESCE(joy.total, 0) END AS TotalVenta,
        joy.fecha AS Fecha,
        COALESCE(joy.estatus, '') AS Estatus
      FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM] joy
    WHERE CONVERT(nvarchar(60), joy.folio_registro) IN ({inSql})
      {dateFilterJoy}
) ventas
  ORDER BY CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(Estatus, '')))) = 'C' THEN 0 ELSE 1 END, Fecha DESC", parameters.ToArray());

            return rows
                .Select(row => new
                {
                    Folio = PickText(row, "FolioRegistro"),
                    Venta = new StoreRelacionVentaInfo(PickText(row, "Ticket"), ToDecimal(PickObject(row, "TotalVenta")), ToDate(PickObject(row, "Fecha")))
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Folio))
                .GroupBy(x => x.Folio, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => new StoreRelacionVentaInfo(
                        string.Join(", ", x.Select(item => item.Venta.Ticket).Where(ticket => !string.IsNullOrWhiteSpace(ticket)).Distinct(StringComparer.OrdinalIgnoreCase)),
                        x.Sum(item => item.Venta.TotalVenta),
                        x.Max(item => item.Venta.Fecha)),
                    StringComparer.OrdinalIgnoreCase);
        }

        private async Task<List<string>> LoadRelationVendorOptionsAsync(DbConnection connection)
        {
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT Nombre
FROM
(
    SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(160), Nombre))), '') AS Nombre
    FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[vendedor]

    UNION

    SELECT NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(160), Nombre))), '') AS Nombre
    FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[vendedor]
) vendors
WHERE Nombre IS NOT NULL
ORDER BY Nombre");

            return rows
                .Select(row => PickText(row, "Nombre"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToList();
        }

        private sealed record StoreRelacionVentaInfo(string Ticket, decimal TotalVenta, DateTime? Fecha);

        private static DateTime? TryParseRelacionDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                ? parsed
                : null;
        }

        private static string ResolveRelacionDateDisplay(IReadOnlyDictionary<string, object?> row, string dateField, string timeField)
        {
            var dateValue = PickObject(row, dateField);
            var dateText = PickDate(row, dateField);
            var timeText = PickText(row, timeField);

            if (dateValue is DateTime dateTimeValue)
            {
                if (dateTimeValue.TimeOfDay != TimeSpan.Zero)
                    return dateTimeValue.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

                var combined = CombineDateAndTime(dateTimeValue, timeText);
                if (!string.IsNullOrWhiteSpace(combined))
                    return combined;
            }

            return !string.IsNullOrWhiteSpace(dateText)
                ? dateText
                : CombineDateAndTime(ToDate(dateValue), timeText);
        }

        private static string CombineDateAndTime(DateTime? dateValue, string? timeText)
        {
            if (!dateValue.HasValue)
                return string.Empty;

            if (string.IsNullOrWhiteSpace(timeText))
                return dateValue.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

            if (DateTime.TryParse(timeText, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsedTime)
                || DateTime.TryParse(timeText, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime))
            {
                var combined = dateValue.Value.Date.Add(parsedTime.TimeOfDay);
                return combined.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            }

            return $"{dateValue.Value:dd/MM/yyyy} {timeText.Trim()}";
        }

        private static async Task EnsureAppMovilPayoutColumnsAsync(DbConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF OBJECT_ID('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_status') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_status NVARCHAR(30) NOT NULL CONSTRAINT DF_AppMovilRegistro_PayoutStatus DEFAULT 'pendiente';
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_date') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_date DATETIME2 NULL;
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_user') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_user NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilRegistro_PayoutUser DEFAULT '';
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_ticket') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_ticket NVARCHAR(40) NOT NULL CONSTRAINT DF_AppMovilRegistro_PayoutTicket DEFAULT '';
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'estado_pago_dejada') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD estado_pago_dejada NVARCHAR(20) NOT NULL CONSTRAINT DF_AppMovilRegistro_EstadoPagoDejada DEFAULT 'pendiente';
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'fecha_pago_dejada') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD fecha_pago_dejada DATETIME2 NULL;
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'usuario_pago_dejada') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD usuario_pago_dejada NVARCHAR(180) NOT NULL CONSTRAINT DF_AppMovilRegistro_UsuarioPagoDejada DEFAULT '';
    IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'ticket_pago_dejada') IS NULL
        ALTER TABLE {PosTable("AppMovilRegistro")} ADD ticket_pago_dejada NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilRegistro_TicketPagoDejada DEFAULT '';
END";
            await ExecuteNonQueryAsync(command);
        }

        public async Task<PosDejadaTicketViewModel?> PayDejadaAsync(string? folioControl, string? folioApp, string? folioOperacion, string? folioPos, string usuario)
        {
            var control = Normalize(folioControl);
            var app = Normalize(folioApp);
            var operacion = Normalize(folioOperacion);
            var pos = Normalize(folioPos);
            if (control == null && app == null && operacion == null && pos == null)
                return null;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsureAppMovilPayoutColumnsAsync(connection);
            var ticket = $"DJ{DateTime.Now:yyyyMMddHHmmss}";

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1)
    COALESCE(c.FolioControl, a.folio_app, '') AS FolioControl,
    a.folio_app AS FolioApp,
    COALESCE(a.folio_pos, '') AS FolioPos,
    a.fecha_operacion AS FechaViaje,
    COALESCE(a.vendedor_nombre, '') AS Taxista,
    COALESCE(a.folio_gafete, '') AS Gafete,
    COALESCE(a.nacionalidad, '') AS Nacionalidad,
    COALESCE(a.tipo_operacion, '') AS Transporte,
    COALESCE(a.hotel, '') AS Hotel,
    COALESCE(a.unidad, '') AS Unidad,
    COALESCE(a.placas, '') AS Placas,
    COALESCE(a.destino, '') AS Destino,
    COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
    COALESCE(a.pax, 0) AS Pax,
    COALESCE(a.total, 0) AS Importe,
    COALESCE(NULLIF(a.payout_status, ''), NULLIF(a.estado_pago_dejada, ''), 'pendiente') AS Estatus,
    COALESCE(a.payout_date, a.fecha_pago_dejada) AS FechaPago,
    COALESCE(NULLIF(a.payout_user, ''), NULLIF(a.usuario_pago_dejada, ''), '') AS Usuario,
    COALESCE(NULLIF(a.payout_ticket, ''), NULLIF(a.ticket_pago_dejada, ''), '') AS Ticket
FROM {PosTable("AppMovilRegistro")} a
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioControl = a.folio_app OR c.FolioAppOriginal = a.folio_app_original OR c.FolioAppOriginal = a.folio_app
WHERE (@folioApp <> '' AND (UPPER(a.folio_app) = UPPER(@folioApp) OR UPPER(a.folio_app_original) = UPPER(@folioApp)))
   OR (@folioControl <> '' AND UPPER(c.FolioControl) = UPPER(@folioControl))
   OR (@folioOperacion <> '' AND UPPER(a.folio_pos) = UPPER(@folioOperacion))
   OR (@folioPos <> '' AND UPPER(a.folio_pos) = UPPER(@folioPos))
ORDER BY
    CASE
        WHEN @folioApp <> '' AND UPPER(a.folio_app) = UPPER(@folioApp) THEN 0
        WHEN @folioApp <> '' AND UPPER(a.folio_app_original) = UPPER(@folioApp) THEN 1
        WHEN @folioControl <> '' AND UPPER(c.FolioControl) = UPPER(@folioControl) THEN 2
        WHEN @folioPos <> '' AND UPPER(a.folio_pos) = UPPER(@folioPos) THEN 3
        ELSE 9
    END,
    a.fecha_operacion DESC", ("@folioControl", control ?? string.Empty), ("@folioApp", app ?? string.Empty), ("@folioOperacion", operacion ?? string.Empty), ("@folioPos", pos ?? string.Empty));

            var row = rows.FirstOrDefault();
            if (row == null)
            {
                var exactCandidates = new[] { control, app, operacion, pos }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var candidate in exactCandidates)
                {
                    var mktTicket = await PayDejadaFromMktAsync(connection, candidate!, usuario, ticket);
                    if (mktTicket != null && TicketMatchesRequestedRow(mktTicket, control, app, operacion, pos))
                        return mktTicket;
                }

                var apiFolio = app ?? control;
                if (!string.IsNullOrWhiteSpace(apiFolio))
                {
                    var apiTicket = await PayDejadaFromApiAsync(connection, apiFolio, usuario, ticket);
                    return apiTicket != null && TicketMatchesRequestedRow(apiTicket, control, app, operacion, pos)
                        ? apiTicket
                        : null;
                }

                return null;
            }

            if (!string.IsNullOrWhiteSpace(control)
                && !string.Equals(PickText(row, "FolioControl"), control, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(PickText(row, "FolioApp"), app, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var importe = ToDecimal(PickObject(row, "Importe"));
            if (importe <= 0)
            {
                var fallbackCandidates = new[] { control, app, operacion, pos, PickText(row, "FolioControl"), PickText(row, "FolioApp"), PickText(row, "FolioPos") }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (var candidate in fallbackCandidates)
                {
                    var mktTicket = await PayDejadaFromMktAsync(connection, candidate!, usuario, ticket);
                    if (mktTicket != null && TicketMatchesRequestedRow(mktTicket, control, app, operacion, pos))
                        return mktTicket;
                }

                return null;
            }

            var estatus = PickText(row, "Estatus");
            var finalTicket = FirstText(PickText(row, "Ticket"), ticket);
            var finalUsuario = FirstText(PickText(row, "Usuario"), usuario);
            var fechaPago = PickDate(row, "FechaPago");

            if (!string.Equals(estatus, "pagado", StringComparison.OrdinalIgnoreCase))
            {
                var paidAt = DateTime.Now;

                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $@"
UPDATE {PosTable("AppMovilRegistro")}
SET payout_status = 'pagado',
    payout_date = GETDATE(),
    payout_user = @usuario,
    payout_ticket = @ticket,
    estado_pago_dejada = 'pagado',
    fecha_pago_dejada = GETDATE(),
    usuario_pago_dejada = @usuario,
    ticket_pago_dejada = @ticket
WHERE (
        @folioAppResolved <> ''
    AND (
            UPPER(folio_app) = UPPER(@folioAppResolved)
         OR UPPER(folio_app_original) = UPPER(@folioAppResolved)
        )
      )
   OR (
        @folioAppResolved = ''
    AND @folioControlResolved <> ''
    AND (
            UPPER(folio_app) = UPPER(@folioControlResolved)
         OR UPPER(folio_app_original) = UPPER(@folioControlResolved)
        )
      )
   OR (
        @folioAppResolved = ''
    AND @folioControlResolved = ''
    AND @folioPosResolved <> ''
    AND UPPER(folio_pos) = UPPER(@folioPosResolved)
      );";
                AddParameter(command, "@usuario", SafeText(usuario, 80));
                AddParameter(command, "@ticket", finalTicket);
                AddParameter(command, "@folioAppResolved", PickText(row, "FolioApp"));
                AddParameter(command, "@folioControlResolved", PickText(row, "FolioControl"));
                AddParameter(command, "@folioPosResolved", PickText(row, "FolioPos"));
                var affected = await ExecuteNonQueryAsync(command);
                if (affected <= 0)
                {
                    await transaction.RollbackAsync();
                    var fallbackCandidates = new[] { control, app, operacion, pos, PickText(row, "FolioControl"), PickText(row, "FolioApp"), PickText(row, "FolioPos") }
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    foreach (var candidate in fallbackCandidates)
                    {
                        var mktTicket = await PayDejadaFromMktAsync(connection, candidate!, usuario, ticket);
                        if (mktTicket != null && TicketMatchesRequestedRow(mktTicket, control, app, operacion, pos))
                        {
                            return mktTicket;
                        }
                    }

                    return null;
                }

                await using var dejadaUpdate = connection.CreateCommand();
                dejadaUpdate.Transaction = transaction;
                dejadaUpdate.CommandText = $@"
UPDATE d
SET pago = CASE WHEN COALESCE(d.pago, 0) > 0 THEN d.pago ELSE COALESCE(d.total, @importe) END,
    fechapago = COALESCE(d.fechapago, GETDATE())
FROM {PosTable("dejadas")} d
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioOperacion = CONVERT(nvarchar(60), d.folioregistro)
    OR r.FolioOperacion = d.folioregistrostr
    OR (ISNUMERIC(r.FolioOperacion) = 1 AND CONVERT(bigint, r.FolioOperacion) = CONVERT(bigint, d.folioregistro))
    OR (ISNUMERIC(r.FolioApp) = 1 AND CONVERT(bigint, r.FolioApp) = CONVERT(bigint, d.folioregistro))
    OR r.FolioOperacion = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
    OR r.FolioApp = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioAppOriginal = r.FolioApp
WHERE UPPER(CONVERT(nvarchar(60), d.folioregistro)) IN (UPPER(@folio), UPPER(@folioControl), UPPER(@resolvedFolio))
   OR UPPER(d.folioregistrostr) IN (UPPER(@folio), UPPER(@folioControl), UPPER(@resolvedFolio))
   OR UPPER(d.codigorecepcion) IN (UPPER(@folio), UPPER(@folioControl), UPPER(@resolvedFolio))
   OR UPPER(r.FolioApp) IN (UPPER(@folio), UPPER(@folioControl), UPPER(@resolvedFolio))
   OR UPPER(c.FolioControl) IN (UPPER(@folio), UPPER(@folioControl), UPPER(@resolvedFolio));";
                AddParameter(dejadaUpdate, "@folio", PickText(row, "FolioApp"));
                AddParameter(dejadaUpdate, "@folioControl", PickText(row, "FolioControl"));
                AddParameter(dejadaUpdate, "@resolvedFolio", FirstText(operacion, pos, PickText(row, "FolioApp")));
                AddParameter(dejadaUpdate, "@importe", importe);
                await ExecuteNonQueryAsync(dejadaUpdate);

                await InsertPosAuditoriaAsync(connection, transaction, usuario, "Dejadas", "Pagar", PickText(row, "FolioControl"), "Dejada pagada",
                    BuildAuditJson(("FolioControl", PickText(row, "FolioControl")), ("FolioApp", PickText(row, "FolioApp")), ("Ticket", finalTicket), ("Importe", importe)));
                await transaction.CommitAsync();

                try
                {
                    await _appTaxiApi.MarkTripPayoutPaidAsync(PickText(row, "FolioApp"), usuario, finalTicket, paidAt);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "La dejada se pago en SQL local, pero no se pudo reflejar en la API movil.");
                }

                estatus = "pagado";
                finalUsuario = usuario;
                fechaPago = paidAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            }

            return new PosDejadaTicketViewModel
            {
                Ticket = finalTicket,
                FolioApp = PickText(row, "FolioApp"),
                FolioControl = PickText(row, "FolioControl"),
                FolioOperacion = string.Empty,
                FolioPos = PickText(row, "FolioPos"),
                FechaViaje = ResolveRelacionDateDisplay(row, "FechaViaje", "HoraTexto"),
                FechaPago = fechaPago,
                Taxista = PickText(row, "Taxista"),
                Vendedor = PickText(row, "Vendedor"),
                Gafete = PickText(row, "Gafete"),
                Unidad = PickText(row, "Unidad"),
                Placas = PickText(row, "Placas"),
                Destino = PickText(row, "Destino"),
                Telefono = PickText(row, "Telefono"),
                Nacionalidad = PickText(row, "Nacionalidad"),
                Transporte = PickText(row, "Transporte"),
                Hotel = PickText(row, "Hotel"),
                Pax = PickInt(row, "Pax"),
                Importe = importe,
                Usuario = finalUsuario,
                Estatus = estatus
            };
        }

        private static bool TicketMatchesRequestedRow(PosDejadaTicketViewModel ticket, string? folioControl, string? folioApp, string? folioOperacion, string? folioPos)
        {
            var requested = new[] { folioControl, folioApp, folioOperacion, folioPos }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToList();
            if (requested.Count == 0)
                return false;

            var actual = new[] { ticket.FolioControl, ticket.FolioApp, ticket.FolioOperacion, ticket.FolioPos, ticket.Ticket }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToList();

            return actual.Any(a => requested.Any(r =>
                string.Equals(a, r, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeRelationToken(a), NormalizeRelationToken(r), StringComparison.OrdinalIgnoreCase)));
        }

        private async Task<PosDejadaTicketViewModel?> PayDejadaFromMktAsync(DbConnection connection, string folio, string usuario, string ticket)
        {
            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1)
    COALESCE(c.FolioControl, r.FolioApp, d.codigorecepcion, CONVERT(nvarchar(60), d.folioregistro), d.folioregistrostr, '') AS FolioControl,
    COALESCE(r.FolioApp, '') AS FolioApp,
    COALESCE(NULLIF(r.FolioOperacion, ''), CONVERT(nvarchar(60), d.folioregistro), d.folioregistrostr, '') AS FolioOperacion,
    COALESCE(NULLIF(r.FolioPos, ''), '') AS FolioPos,
    COALESCE(a.fecha_operacion, d.fecha) AS FechaViaje,
    COALESCE(NULLIF(CONVERT(nvarchar(40), d.hora), ''), '') AS HoraTexto,
    COALESCE(d.nombrevendedor, d.nombrestaff, '') AS Taxista,
    COALESCE(NULLIF(r.Vendedor, ''), '') AS Vendedor,
    COALESCE(d.gafete, '') AS Gafete,
    COALESCE(a.nacionalidad, '') AS Nacionalidad,
    COALESCE(d.tipotransporte, '') AS Transporte,
    COALESCE(a.unidad, d.unidad, '') AS Unidad,
    COALESCE(a.placas, '') AS Placas,
    COALESCE(a.destino, '') AS Destino,
    COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
    COALESCE(d.hotel, d.nombrealmacen, '') AS Hotel,
    COALESCE(d.pax, 0) AS Pax,
    COALESCE(d.total, 0) AS Importe,
    COALESCE(d.pago, 0) AS Pago,
    d.fechapago AS FechaPago,
    COALESCE(d.codigorecepcion, '') AS CodigoRecepcion,
    d.folioregistro AS FolioRegistro,
    d.folioregistrostr AS FolioRegistroStr
FROM {PosTable("dejadas")} d
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioOperacion = CONVERT(nvarchar(60), d.folioregistro)
    OR r.FolioOperacion = d.folioregistrostr
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioAppOriginal = r.FolioApp
LEFT JOIN {PosTable("AppMovilRegistro")} a
    ON a.folio_app = r.FolioApp OR a.folio_app_original = r.FolioApp
WHERE UPPER(CONVERT(nvarchar(60), d.folioregistro)) = UPPER(@folio)
   OR UPPER(d.folioregistrostr) = UPPER(@folio)
   OR UPPER(d.codigorecepcion) = UPPER(@folio)
   OR UPPER(r.FolioApp) = UPPER(@folio)
   OR UPPER(c.FolioControl) = UPPER(@folio)
   OR (ISNUMERIC(CONVERT(nvarchar(60), d.folioregistro)) = 1 AND ISNUMERIC(@folio) = 1 AND CONVERT(bigint, d.folioregistro) = CONVERT(bigint, @folio))
   OR (ISNUMERIC(d.folioregistrostr) = 1 AND ISNUMERIC(@folio) = 1 AND CONVERT(bigint, d.folioregistrostr) = CONVERT(bigint, @folio))
ORDER BY
    CASE
        WHEN UPPER(d.codigorecepcion) = UPPER(@folio) THEN 0
        WHEN UPPER(d.folioregistrostr) = UPPER(@folio) THEN 1
        WHEN UPPER(CONVERT(nvarchar(60), d.folioregistro)) = UPPER(@folio) THEN 2
        WHEN UPPER(r.FolioApp) = UPPER(@folio) THEN 3
        WHEN UPPER(c.FolioControl) = UPPER(@folio) THEN 4
        ELSE 9
    END,
    d.fecha DESC", ("@folio", folio));

            var row = rows.FirstOrDefault();
            if (row == null)
                return null;

            var importe = ToDecimal(PickObject(row, "Importe"));
            if (importe <= 0m)
                return null;

            var pago = ToDecimal(PickObject(row, "Pago"));
            var finalTicket = FirstText(PickText(row, "CodigoRecepcion"), ticket);
            var paidAt = DateTime.Now;
            if (pago <= 0m)
            {
                await using var transaction = await connection.BeginTransactionAsync();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $@"
UPDATE {PosTable("dejadas")}
SET pago = COALESCE(total, 0),
    fechapago = GETDATE()
WHERE CONVERT(nvarchar(60), folioregistro) = @folioOperacion
   OR folioregistrostr = @folioOperacion
   OR codigorecepcion = @folioOperacion
   OR (ISNUMERIC(CONVERT(nvarchar(60), folioregistro)) = 1 AND ISNUMERIC(@folioOperacion) = 1 AND CONVERT(bigint, folioregistro) = CONVERT(bigint, @folioOperacion))
   OR (ISNUMERIC(folioregistrostr) = 1 AND ISNUMERIC(@folioOperacion) = 1 AND CONVERT(bigint, folioregistrostr) = CONVERT(bigint, @folioOperacion));";
                AddParameter(command, "@folioOperacion", PickText(row, "FolioOperacion"));
                var affected = await ExecuteNonQueryAsync(command);

                var folioApp = PickText(row, "FolioApp");
                if (!string.IsNullOrWhiteSpace(folioApp))
                {
                    await using var appUpdate = connection.CreateCommand();
                    appUpdate.Transaction = transaction;
                    appUpdate.CommandText = $@"
UPDATE {PosTable("AppMovilRegistro")}
SET payout_status = 'pagado',
    payout_date = GETDATE(),
    payout_user = @usuario,
    payout_ticket = @ticket,
    estado_pago_dejada = 'pagado',
    fecha_pago_dejada = GETDATE(),
    usuario_pago_dejada = @usuario,
    ticket_pago_dejada = @ticket
WHERE folio_app = @folioApp
   OR folio_app_original = @folioApp;";
                    AddParameter(appUpdate, "@usuario", SafeText(usuario, 80));
                    AddParameter(appUpdate, "@ticket", finalTicket);
                    AddParameter(appUpdate, "@folioApp", folioApp);
                    await ExecuteNonQueryAsync(appUpdate);
                }

                if (affected > 0)
                    await InsertPosAuditoriaAsync(connection, transaction, usuario, "Dejadas", "Pagar", PickText(row, "FolioOperacion"), "Dejada pagada en dbo.dejadas",
                        BuildAuditJson(("FolioOperacion", PickText(row, "FolioOperacion")), ("FolioApp", folioApp), ("Ticket", finalTicket), ("Importe", importe)));
                await transaction.CommitAsync();

                if (!string.IsNullOrWhiteSpace(folioApp))
                {
                    try
                    {
                        await _appTaxiApi.MarkTripPayoutPaidAsync(folioApp, usuario, finalTicket, paidAt);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "La dejada se pago en SQL local, pero no se pudo reflejar en la API movil.");
                    }
                }
            }

            return new PosDejadaTicketViewModel
            {
                Ticket = finalTicket,
                FolioApp = PickText(row, "FolioApp"),
                FolioControl = PickText(row, "FolioControl"),
                FolioOperacion = PickText(row, "FolioOperacion"),
                FolioPos = PickText(row, "FolioPos"),
                FechaViaje = ResolveRelacionDateDisplay(row, "FechaViaje", "HoraTexto"),
                FechaPago = pago > 0m ? PickDate(row, "FechaPago") : paidAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                Taxista = PickText(row, "Taxista"),
                Vendedor = PickText(row, "Vendedor"),
                Gafete = PickText(row, "Gafete"),
                Nacionalidad = PickText(row, "Nacionalidad"),
                Transporte = PickText(row, "Transporte"),
                Hotel = PickText(row, "Hotel"),
                Unidad = PickText(row, "Unidad"),
                Placas = PickText(row, "Placas"),
                Destino = PickText(row, "Destino"),
                Telefono = PickText(row, "Telefono"),
                Pax = PickInt(row, "Pax"),
                Importe = importe,
                Usuario = usuario,
                Estatus = "pagado"
            };
        }

        private async Task<PosDejadaTicketViewModel?> PayDejadaFromApiAsync(DbConnection connection, string folioApp, string usuario, string ticket)
        {
            var rows = await _appTaxiApi.GetTripRecordsAsync(folioApp, null, null);
            var row = rows.FirstOrDefault(x => string.Equals(x.FolioControl, folioApp, StringComparison.OrdinalIgnoreCase));
            if (row == null || row.Total <= 0)
                return null;

            var alreadyPaid = string.Equals(row.PayoutStatus, "pagado", StringComparison.OrdinalIgnoreCase);
            var paidAt = DateTime.Now;
            var finalTicket = FirstText(row.PayoutTicket, ticket);
            if (!alreadyPaid)
            {
                try
                {
                    await _appTaxiApi.MarkTripPayoutPaidAsync(row.FolioControl, usuario, finalTicket, paidAt);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo reflejar pago de dejada en Hostinger, se conserva pago local.");
                }

                row.PayoutStatus = "pagado";
                row.PayoutDate = paidAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                row.PayoutUser = usuario;
                row.PayoutTicket = finalTicket;
            }

            await EnsureAppMovilMirrorSchemaAsync(connection);
            await UpsertAppMovilRegistroAsync(connection, row);
            var localSnapshot = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1)
    fecha_operacion AS FechaViaje,
    COALESCE(payout_date, fecha_pago_dejada) AS FechaPago,
    COALESCE(vendedor_nombre, '') AS Taxista,
    COALESCE(folio_gafete, '') AS Gafete,
    COALESCE(unidad, '') AS Unidad,
    COALESCE(placas, '') AS Placas,
    COALESCE(destino, '') AS Destino,
    COALESCE(NULLIF(telefono_taxista, ''), NULLIF(telefono_contacto, ''), '') AS Telefono,
    COALESCE(nacionalidad, '') AS Nacionalidad,
    COALESCE(tipo_operacion, '') AS Transporte,
    COALESCE(hotel, '') AS Hotel,
    COALESCE(pax, 0) AS Pax,
    COALESCE(total, 0) AS Importe,
    COALESCE(payout_user, usuario_pago_dejada, '') AS UsuarioPago
FROM {PosTable("AppMovilRegistro")}
WHERE folio_app = @folioApp
   OR folio_app_original = @folioApp
ORDER BY COALESCE(payout_date, fecha_pago_dejada, fecha_operacion) DESC",
                ("@folioApp", folioApp));
            var localRow = localSnapshot.FirstOrDefault();

            return new PosDejadaTicketViewModel
            {
                Ticket = finalTicket,
                FolioApp = row.FolioControl,
                FolioControl = row.FolioControl,
                FolioPos = row.Ticket,
                FechaViaje = localRow != null ? PickDate(localRow, "FechaViaje") : row.Hora,
                FechaPago = localRow != null
                    ? PickDate(localRow, "FechaPago")
                    : alreadyPaid
                        ? row.PayoutDate
                        : paidAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                Taxista = localRow != null ? PickText(localRow, "Taxista") : row.Vendedor,
                Vendedor = string.Empty,
                Gafete = localRow != null ? PickText(localRow, "Gafete") : row.Gafete,
                Unidad = localRow != null ? PickText(localRow, "Unidad") : row.Unidad,
                Placas = localRow != null ? PickText(localRow, "Placas") : row.Placas,
                Destino = localRow != null ? PickText(localRow, "Destino") : row.Destino,
                Telefono = localRow != null ? PickText(localRow, "Telefono") : row.Telefono,
                Nacionalidad = localRow != null ? PickText(localRow, "Nacionalidad") : row.Nacionalidad,
                Transporte = localRow != null ? PickText(localRow, "Transporte") : row.TipoOperacion,
                Hotel = localRow != null ? PickText(localRow, "Hotel") : row.Hotel,
                Pax = localRow != null ? PickInt(localRow, "Pax") : row.Pax,
                Importe = localRow != null ? ToDecimal(PickObject(localRow, "Importe")) : row.Total,
                Usuario = localRow != null
                    ? FirstText(PickText(localRow, "UsuarioPago"), alreadyPaid ? row.PayoutUser : usuario)
                    : alreadyPaid
                        ? row.PayoutUser
                        : usuario,
                Estatus = alreadyPaid ? row.PayoutStatus : "pagado"
            };
        }

        public async Task<bool> SaveRelacionAsync(PosRelacionesViewModel model, string usuario)
        {
            var folioApp = Normalize(model.FolioApp);
            var folioOperacion = Normalize(model.FolioOperacion);
            var vendedor = SafeText(model.Vendedor, 150);
            var fechaRelacion = TryParseRelacionDate(model.Fecha)?.Date;
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsureRelacionesTicketTaxistaTableAsync(connection);
            folioApp = await ResolveFolioAppOriginalAsync(connection, folioApp ?? model.FolioControl);
            if (folioApp == null && !string.IsNullOrWhiteSpace(folioOperacion))
                folioApp = folioOperacion;
            if (folioApp == null || (string.IsNullOrWhiteSpace(folioOperacion) && model.TaxistaId <= 0))
                return false;
            folioOperacion ??= string.Empty;
            if (!string.IsNullOrWhiteSpace(folioOperacion))
                await EnsurePosOperacionBeneficiariosTableAsync(connection);

            var hasAppMovilRow = false;
            await using (var appExists = connection.CreateCommand())
            {
                appExists.CommandText = $@"
SELECT TOP (1) 1
FROM {PosTable("AppMovilRegistro")}
WHERE folio_app = @folioApp
   OR folio_app_original = @folioApp;";
                AddParameter(appExists, "@folioApp", folioApp);
                var value = await ExecuteScalarAsync(appExists);
                hasAppMovilRow = value != null && value != DBNull.Value;
            }

            var originalGafetes = string.Empty;
            await using (var existingRelation = connection.CreateCommand())
            {
                existingRelation.CommandText = $@"
SELECT TOP (1) COALESCE(Gafete, '')
FROM {PosTable("RelacionTicketTaxista")}
WHERE FolioApp = @folioApp;";
                AddParameter(existingRelation, "@folioApp", folioApp);
                var value = await ExecuteScalarAsync(existingRelation);
                originalGafetes = value == null || value == DBNull.Value ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            if (string.IsNullOrWhiteSpace(originalGafetes) && !string.IsNullOrWhiteSpace(folioOperacion))
            {
                await using var existingGafetes = connection.CreateCommand();
                existingGafetes.CommandText = $@"
SELECT STUFF((
    SELECT ', ' + CONVERT(nvarchar(50), g2.gafete)
    FROM {PosTable("gafete")} g2
    WHERE (
            CONVERT(nvarchar(50), g2.folioperacion) = @folio
         OR (
                ISNUMERIC(CONVERT(nvarchar(50), g2.folioperacion)) = 1
            AND ISNUMERIC(@folio) = 1
            AND CONVERT(bigint, g2.folioperacion) = CONVERT(bigint, @folio)
            )
          )
      AND UPPER(COALESCE(g2.venta, '')) = 'A'
    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '')
FROM {PosTable("gafete")}
WHERE (
        CONVERT(nvarchar(50), folioperacion) = @folio
     OR (
            ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
        AND ISNUMERIC(@folio) = 1
        AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folio)
        )
      )
  AND UPPER(COALESCE(venta, '')) = 'A';";
                AddParameter(existingGafetes, "@folio", folioOperacion);
                var value = await ExecuteScalarAsync(existingGafetes);
                originalGafetes = value == null || value == DBNull.Value ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            var dejadasNacionalidadSet = await PosColumnExistsAsync(connection, "dejadas", "nacionalidad")
                ? ", nacionalidad = CASE WHEN NULLIF(@nacionalidad, '') IS NULL THEN nacionalidad ELSE @nacionalidad END"
                : string.Empty;

            await using var transaction = await connection.BeginTransactionAsync();
            long? folioOperacionNumero = null;
            if (long.TryParse(folioOperacion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFolioOperacion))
                folioOperacionNumero = parsedFolioOperacion;
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $@"
IF EXISTS (
    SELECT 1
    FROM {PosTable("RelacionTicketTaxista")}
    WHERE FolioApp = @folioApp
)
BEGIN
    UPDATE {PosTable("RelacionTicketTaxista")}
    SET FolioOperacion = CASE WHEN NULLIF(FolioOperacion, '') IS NULL THEN @folioOperacion ELSE FolioOperacion END,
        FolioPos = CASE WHEN NULLIF(FolioPos, '') IS NULL THEN @folioPos ELSE FolioPos END,
        Gafete = @gafete,
        TaxistaId = @taxistaId,
        TaxistaNombre = @taxistaNombre,
        Vendedor = @vendedor,
        TransporteTipo = @transporte,
        Dejada = @dejada,
        Observaciones = @observaciones,
        Usuario = @usuario,
        FechaActualizacion = SYSUTCDATETIME()
    WHERE FolioApp = @folioApp;
END
ELSE
BEGIN
    INSERT INTO {PosTable("RelacionTicketTaxista")}
        (FolioApp, FolioOperacion, FolioPos, Gafete, TaxistaId, TaxistaNombre, Vendedor, TransporteTipo, Dejada, Observaciones, Usuario)
    VALUES
        (@folioApp, @folioOperacion, @folioPos, @gafete, @taxistaId, @taxistaNombre, @vendedor, @transporte, @dejada, @observaciones, @usuario);
END";
                AddParameter(command, "@folioApp", folioApp);
                AddParameter(command, "@folioOperacion", folioOperacion);
                AddParameter(command, "@folioPos", SafeText(model.FolioPos, 120));
                AddParameter(command, "@gafete", SafeText(model.Gafete, 300));
                AddParameter(command, "@taxistaId", model.TaxistaId);
                AddParameter(command, "@taxistaNombre", SafeText(model.TaxistaNombre, 150));
                AddParameter(command, "@vendedor", vendedor);
                AddParameter(command, "@transporte", SafeText(model.TransporteTipo, 20));
                AddParameter(command, "@dejada", model.Dejada);
                AddParameter(command, "@observaciones", SafeText(model.Observaciones, 300));
                AddParameter(command, "@usuario", SafeText(usuario, 50));
                await ExecuteNonQueryAsync(command);
            }

            if (!string.IsNullOrWhiteSpace(folioOperacion))
            {
                await using var beneficiary = connection.CreateCommand();
                beneficiary.Transaction = transaction;
                beneficiary.CommandText = $@"
IF EXISTS (SELECT 1 FROM {AppTable("PosOperacionBeneficiarios")} WHERE FolioOperacion = @folio)
BEGIN
    UPDATE {AppTable("PosOperacionBeneficiarios")}
    SET TransporteTipo = @transporte,
        TaxistaId = @taxistaId,
        TaxistaNombre = @taxistaNombre,
        Usuario = @usuario,
        Fecha = SYSUTCDATETIME()
    WHERE FolioOperacion = @folio;
END
ELSE
BEGIN
    INSERT INTO {AppTable("PosOperacionBeneficiarios")}
        (FolioOperacion, TransporteTipo, TaxistaId, TaxistaNombre, Usuario)
    VALUES
        (@folio, @transporte, @taxistaId, @taxistaNombre, @usuario);
END";
                AddParameter(beneficiary, "@folio", folioOperacion);
                AddParameter(beneficiary, "@transporte", SafeText(model.TransporteTipo, 10));
                AddParameter(beneficiary, "@taxistaId", model.TaxistaId);
                AddParameter(beneficiary, "@taxistaNombre", SafeText(model.TaxistaNombre, 80));
                AddParameter(beneficiary, "@usuario", SafeText(usuario, 50));
                await ExecuteNonQueryAsync(beneficiary);

                await using var dejada = connection.CreateCommand();
                dejada.Transaction = transaction;
                dejada.CommandText = $@"
UPDATE m
SET dejada = COALESCE(@dejada, CASE WHEN ISNUMERIC(v.costo_viaje) = 1 THEN CAST(v.costo_viaje AS decimal(18,2)) ELSE COALESCE(m.dejada, 0) END),
    transportetipo = CASE WHEN @transporte = '' THEN transportetipo ELSE @transporte END
FROM {PosTable("mov_operacion")} m
INNER JOIN {PosTable("AppMovilRegistro")} a
    ON a.folio_app = @folioApp OR a.folio_app_original = @folioApp
INNER JOIN {PosTable("vw_AppMovilRegistrosViajes")} v
    ON v.id_registro = a.folio_app
WHERE CONVERT(nvarchar(60), m.folioperacion) = @folio;";
                AddParameter(dejada, "@folio", folioOperacion);
                AddParameter(dejada, "@folioApp", folioApp);
                AddParameter(dejada, "@transporte", SafeText(model.TransporteTipo, 20));
                AddParameter(dejada, "@dejada", model.Dejada);
                await ExecuteNonQueryAsync(dejada);

                await using (var dejadasDirect = connection.CreateCommand())
                {
                    dejadasDirect.Transaction = transaction;
                    dejadasDirect.CommandText = $@"
UPDATE {PosTable("dejadas")}
SET gafete = CASE WHEN NULLIF(@gafete, '') IS NULL THEN gafete ELSE @gafete END,
    total = COALESCE(@dejada, total),
    idtaxi = CASE WHEN @taxistaId > 0 THEN CONVERT(int, @taxistaId) ELSE idtaxi END,
    nombrevendedor = CASE WHEN NULLIF(@taxistaNombre, '') IS NULL THEN nombrevendedor ELSE @taxistaNombre END,
    nombrestaff = CASE WHEN NULLIF(@vendedor, '') IS NULL THEN nombrestaff ELSE @vendedor END,
    tipotransporte = CASE WHEN NULLIF(@transporte, '') IS NULL THEN tipotransporte ELSE @transporte END
    {dejadasNacionalidadSet}
WHERE (
        CONVERT(nvarchar(60), folioregistro) = @folio
     OR folioregistrostr = @folio
     OR codigorecepcion = @folio
     OR (ISNUMERIC(CONVERT(nvarchar(60), folioregistro)) = 1 AND ISNUMERIC(@folio) = 1 AND CONVERT(bigint, folioregistro) = CONVERT(bigint, @folio))
      )
  AND (@fechaRelacion IS NULL OR CAST(fecha AS date) = @fechaRelacion);";
                    AddParameter(dejadasDirect, "@folio", folioOperacion);
                    AddParameter(dejadasDirect, "@fechaRelacion", fechaRelacion);
                    AddParameter(dejadasDirect, "@dejada", model.Dejada);
                    AddParameter(dejadasDirect, "@gafete", SafeText(model.Gafete, 10));
                    AddParameter(dejadasDirect, "@taxistaId", model.TaxistaId);
                    AddParameter(dejadasDirect, "@taxistaNombre", SafeText(model.TaxistaNombre, 100));
                    AddParameter(dejadasDirect, "@vendedor", vendedor);
                    AddParameter(dejadasDirect, "@transporte", SafeText(model.TransporteTipo, 10));
                    AddParameter(dejadasDirect, "@nacionalidad", SafeText(model.Nacionalidad, 120));
                    await ExecuteNonQueryAsync(dejadasDirect);
                }

                var gafeteNormalizado = SafeText(model.Gafete, 300);
                if (hasAppMovilRow)
                {
                    await using var appMovil = connection.CreateCommand();
                    appMovil.Transaction = transaction;
                    appMovil.CommandText = $@"
UPDATE {PosTable("AppMovilRegistro")}
SET folio_gafete = @gafete
   , total = COALESCE(@dejada, total)
   , nacionalidad = CASE WHEN NULLIF(@nacionalidad, '') IS NULL THEN nacionalidad ELSE @nacionalidad END
WHERE folio_app = @folioApp
   OR folio_app_original = @folioApp;";
                    AddParameter(appMovil, "@folioApp", folioApp);
                    AddParameter(appMovil, "@gafete", gafeteNormalizado);
                    AddParameter(appMovil, "@dejada", model.Dejada);
                    AddParameter(appMovil, "@nacionalidad", SafeText(model.Nacionalidad, 120));
                    await ExecuteNonQueryAsync(appMovil);
                }

                if (hasAppMovilRow && !string.IsNullOrWhiteSpace(gafeteNormalizado))
                {
                    await using var appMovilGafetesDelete = connection.CreateCommand();
                    appMovilGafetesDelete.Transaction = transaction;
                    appMovilGafetesDelete.CommandText = $@"
DELETE FROM {PosTable("AppMovilRegistroGafetes")}
WHERE FolioApp = @folioApp;";
                    AddParameter(appMovilGafetesDelete, "@folioApp", folioApp);
                    await ExecuteNonQueryAsync(appMovilGafetesDelete);

                    foreach (var badge in SplitGafeteNumbers(gafeteNormalizado)
                                 .Select(NormalizeBadgeToken)
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        await using var appMovilGafetesInsert = connection.CreateCommand();
                        appMovilGafetesInsert.Transaction = transaction;
                        appMovilGafetesInsert.CommandText = $@"
INSERT INTO {PosTable("AppMovilRegistroGafetes")} (FolioApp, IdCatalogo, FolioGafete)
VALUES (@folioApp, @catalogId, @gafete);";
                        AddParameter(appMovilGafetesInsert, "@folioApp", folioApp);
                        AddParameter(appMovilGafetesInsert, "@catalogId", model.TaxistaId > 0 ? model.TaxistaId : (object?)null);
                        AddParameter(appMovilGafetesInsert, "@gafete", badge);
                        await ExecuteNonQueryAsync(appMovilGafetesInsert);
                    }
                }

                if (!string.IsNullOrWhiteSpace(gafeteNormalizado) && folioOperacionNumero.HasValue && model.TaxistaId > 0)
                {
                    var nuevosGafetes = SplitGafeteNumbers(gafeteNormalizado)
                        .Select(NormalizeBadgeToken)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var gafetesAnteriores = SplitGafeteNumbers(originalGafetes)
                        .Select(NormalizeBadgeToken)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var oldBadge in gafetesAnteriores.Except(nuevosGafetes, StringComparer.OrdinalIgnoreCase))
                    {
                        await using var suspendOld = connection.CreateCommand();
                        suspendOld.Transaction = transaction;
                        suspendOld.CommandText = $@"
UPDATE {PosTable("gafete")}
SET venta = 'S',
    fecha = CONVERT(smalldatetime, GETDATE()),
    hora = GETDATE()
WHERE (
        CONVERT(nvarchar(50), folioperacion) = @folio
     OR (
            ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
        AND ISNUMERIC(@folio) = 1
        AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folio)
        )
      )
  AND CONVERT(nvarchar(50), gafete) = @gafete
  AND UPPER(COALESCE(venta, '')) = 'A';";
                        AddParameter(suspendOld, "@folio", folioOperacion);
                        AddParameter(suspendOld, "@gafete", oldBadge);
                        await ExecuteNonQueryAsync(suspendOld);
                    }

                    foreach (var newBadge in nuevosGafetes)
                    {
                        await using var insertNew = connection.CreateCommand();
                        insertNew.Transaction = transaction;
                        insertNew.CommandText = $@"
INSERT INTO {PosTable("gafete")} (matricula, gafete, fecha, venta, hora, folioperacion)
SELECT @matricula,
       @gafete,
       CONVERT(smalldatetime, COALESCE(@fechaRelacion, CONVERT(date, GETDATE()))),
       'A',
       COALESCE(@fechaRelacion, GETDATE()),
       @folioNumero
WHERE NOT EXISTS (
    SELECT 1
    FROM {PosTable("gafete")}
    WHERE (
            CONVERT(nvarchar(50), folioperacion) = @folio
         OR (
                ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
            AND ISNUMERIC(@folio) = 1
            AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folio)
            )
          )
      AND CONVERT(nvarchar(50), gafete) = @gafete
      AND UPPER(COALESCE(venta, '')) = 'A'
);";
                        AddParameter(insertNew, "@matricula", model.TaxistaId);
                        AddParameter(insertNew, "@gafete", newBadge);
                        AddParameter(insertNew, "@folio", folioOperacion);
                        AddParameter(insertNew, "@folioNumero", folioOperacionNumero.Value);
                        AddParameter(insertNew, "@fechaRelacion", fechaRelacion);
                        await ExecuteNonQueryAsync(insertNew);
                    }
                }
            }

            await InsertPosAuditoriaAsync(connection, transaction, usuario, "Relaciones", "Guardar", folioApp, "Relacion app movil / ticket / taxista guardada",
                BuildAuditJson(("FolioApp", folioApp), ("FolioOperacion", folioOperacion), ("FolioPos", model.FolioPos), ("Gafete", model.Gafete), ("TaxistaId", model.TaxistaId), ("TaxistaNombre", model.TaxistaNombre), ("TransporteTipo", model.TransporteTipo)));
            await transaction.CommitAsync();

            if (model.TaxistaId > 0 && !string.IsNullOrWhiteSpace(model.Gafete))
            {
                try
                {
                    var taxistaIdInt = checked((int)model.TaxistaId);
                    await UpdateGafeteAsync(taxistaIdInt, originalGafetes, null, taxistaIdInt, model.Gafete, folioOperacionNumero, "A", usuario);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo actualizar el gafete espejo para folio {FolioOperacion}.", folioOperacion);
                }
            }

            if (folioOperacionNumero.HasValue)
            {
                try
                {
                    await SyncGafetesOperacionAsync(folioOperacionNumero.Value, usuario);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo reflejar la actualizacion de gafete hacia la app movil para folio {FolioOperacion}.", folioOperacion);
                }
            }

            if (hasAppMovilRow && !string.IsNullOrWhiteSpace(model.Gafete))
            {
                try
                {
                    var badges = SplitGafeteNumbers(model.Gafete)
                        .Select(NormalizeBadgeToken)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (badges.Count > 0)
                        await _appTaxiApi.UpdateTripBadgesAsync(folioApp, badges);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudieron reflejar los gafetes hacia Hostinger para folio {FolioApp}.", folioApp);
                }
            }

            if (hasAppMovilRow && model.Dejada.HasValue)
            {
                try
                {
                    await _appTaxiApi.UpdateTripAmountAsync(folioApp, model.Dejada.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo reflejar el importe de dejada hacia Hostinger para folio {FolioApp}.", folioApp);
                }
            }

            return true;
        }

        private static async Task<string?> ResolveFolioAppOriginalAsync(DbConnection connection, string? folio)
        {
            var normalized = Normalize(folio);
            if (normalized == null)
                return null;

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT TOP (1) FolioAppOriginal
FROM {PosTable("AppMovilFolioControl")}
WHERE UPPER(FolioControl) = UPPER(@folio)
   OR UPPER(FolioAppOriginal) = UPPER(@folio)";
            AddParameter(command, "@folio", normalized);
            var result = await ExecuteScalarAsync(command);
            return result == null || result == DBNull.Value
                ? normalized
                : Convert.ToString(result, CultureInfo.InvariantCulture);
        }

        private static async Task<string?> ResolveFolioOperacionFromRelacionAsync(DbConnection connection, string? folio)
        {
            var normalized = Normalize(folio);
            if (normalized == null)
                return null;

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT TOP (1) r.FolioOperacion
FROM {PosTable("RelacionTicketTaxista")} r
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioAppOriginal = r.FolioApp
LEFT JOIN {PosTable("vw_AppMovilRegistrosViajes")} v
    ON v.id_registro = r.FolioApp
WHERE NULLIF(r.FolioOperacion, '') IS NOT NULL
  AND (
        CONVERT(nvarchar(60), r.FolioOperacion) = @folio
     OR UPPER(r.FolioPos) = UPPER(@folio)
     OR UPPER(r.FolioApp) = UPPER(@folio)
     OR UPPER(c.FolioControl) = UPPER(@folio)
     OR r.TaxistaNombre LIKE '%' + @folio + '%'
     OR v.nombre_taxista LIKE '%' + @folio + '%'
     OR v.hotel LIKE '%' + @folio + '%'
     OR v.folio_gafete LIKE '%' + @folio + '%'
  )
ORDER BY r.FechaActualizacion DESC";
            AddParameter(command, "@folio", normalized);
            var result = await ExecuteScalarAsync(command);
            return result == null || result == DBNull.Value
                ? normalized
                : Convert.ToString(result, CultureInfo.InvariantCulture);
        }

        public async Task<List<CuadreCamionesSummary>> GetCamionesResumenAsync(DateTime? fechaInicio, DateTime? fechaFin)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            var fechaFinExclusiva = fechaFin.HasValue ? fechaFin.Value.Date.AddDays(1) : (DateTime?)null;

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT
    UPPER(LTRIM(RTRIM(COALESCE(d.tipotransporte, '')))) AS Tipo,
    SUM(COALESCE(d.pax, 0)) AS Pax,
    COUNT(*) AS Entraron,
    COUNT(CASE WHEN COALESCE(d.pago, 0) > 0 THEN 1 ELSE NULL END) AS Salieron,
    COUNT(DISTINCT NULLIF(LTRIM(RTRIM(COALESCE(d.unidad, ''))), '')) AS Unidades,
    SUM(COALESCE(d.total, 0)) AS Dejada
FROM {PosTable("dejadas")} d
WHERE (@fechaInicio IS NULL OR CAST(d.fecha AS date) >= CAST(@fechaInicio AS date))
  AND (@fechaFinExclusiva IS NULL OR CAST(d.fecha AS date) < CAST(@fechaFinExclusiva AS date))
  AND (
        UPPER(LTRIM(RTRIM(d.tipotransporte))) LIKE '%AUTOCAR%'
     OR UPPER(LTRIM(RTRIM(d.tipotransporte))) LIKE '%MAYA CARIBE%'
     OR UPPER(LTRIM(RTRIM(d.tipotransporte))) LIKE '%TURICUN%'
     OR UPPER(LTRIM(RTRIM(d.tipotransporte))) LIKE '%CAMION%'
     OR UPPER(LTRIM(RTRIM(d.tipotransporte))) LIKE '%AUTOBUS%'
  )
GROUP BY UPPER(LTRIM(RTRIM(COALESCE(d.tipotransporte, ''))))
ORDER BY SUM(COALESCE(d.pax, 0)) DESC",
                ("@fechaInicio", (object?)fechaInicio ?? DBNull.Value),
                ("@fechaFinExclusiva", (object?)fechaFinExclusiva ?? DBNull.Value));

            return rows.Select(r => new CuadreCamionesSummary
            {
                Nombre = ToString(PickObject(r, "Tipo")),
                Pax = ToInt(PickObject(r, "Pax")),
                Entraron = ToInt(PickObject(r, "Entraron")),
                Salieron = ToInt(PickObject(r, "Salieron")),
                Unidades = ToInt(PickObject(r, "Unidades")),
                Dejada = ToDecimal(PickObject(r, "Dejada"))
            }).ToList();
        }

        private static int ToInt(object? val) =>
            val == null || val == DBNull.Value ? 0 : Convert.ToInt32(val, CultureInfo.InvariantCulture);

        private static string ToString(object? val) =>
            val == null || val == DBNull.Value ? string.Empty : Convert.ToString(val, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

