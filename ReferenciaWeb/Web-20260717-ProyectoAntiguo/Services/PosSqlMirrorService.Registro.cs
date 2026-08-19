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
public async Task<PosRegistroDiarioViewModel?> TryGetRegistroDiarioAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            try
            {
                var inicio = fechaInicio?.Date;
                var fin = fechaFin?.Date;
                var normalized = Normalize(folioOperacion);
                if (normalized != null)
                {
                    var setupConnection = _dbContext.Database.GetDbConnection();
                    if (setupConnection.State != ConnectionState.Open)
                        await setupConnection.OpenAsync();
                    await EnsureRelacionesTicketTaxistaTableAsync(setupConnection);
                    normalized = await ResolveFolioOperacionFromRelacionAsync(setupConnection, normalized);
                }
                _ = SyncAppMovilRegistrosFromApiAsync(normalized, inicio, fin);
                var dejadasBaseOperaciones = await GetRegistroRowsFromDejadasBaseSqlAsync(normalized, inicio, fin);
                if (dejadasBaseOperaciones.Count > 0)
                {
                    await EnrichRegistroRowsWithStoreTicketsAsync(dejadasBaseOperaciones);
                    await EnrichRegistroNationalitiesFromCatalogAsync(dejadasBaseOperaciones);
                    return BuildRegistroViewModel(normalized, fechaInicio, fechaFin, dejadasBaseOperaciones);
                }

                var appFirstOperaciones = await GetRegistroRowsFromAppMovilAsync(normalized, inicio, fin);
                var posRows = await GetRegistroRowsFromEfSafeAsync(normalized, inicio, fin);
                if (posRows.Count > 0)
                {
                    var posOperaciones = posRows.Select(row => new PosOperacionRowViewModel
                    {
                        FolioOperacion = row.Folio,
                        FolioControl = row.FolioControl,
                        Fuente = "SISTEMA/POS",
                        UsuarioOrigen = "POS",
                        Ticket = row.TicketApp,
                        CantidadTickets = 1,
                        Hotel = row.Hotel,
                        LlegadaSucursal = row.Hotel,
                        Hora = FormatDate(row.Fecha),
                        Pax = row.Pax,
                        Vendedor = FirstText(row.Taxista, row.Staff),
                        TipoOperacion = row.TipoOperacion,
                        Total = row.Total,
                        Efectivo = row.Efectivo,
                        Tarjeta = row.Tarjeta
                    }).ToList();
                    EnrichRegistroRowsWithAppData(posOperaciones, appFirstOperaciones);
                    posOperaciones.AddRange(appFirstOperaciones);
                    posOperaciones = posOperaciones
                        .OrderByDescending(x => TryParseRegistroDate(x.Hora) ?? DateTime.MinValue)
                        .Take(500)
                        .ToList();
                    await EnrichRegistroNationalitiesFromCatalogAsync(posOperaciones);

                    return BuildRegistroViewModel(normalized, fechaInicio, fechaFin, posOperaciones);
                }
                var appOnlyOperaciones = appFirstOperaciones;
                if (appOnlyOperaciones.Count > 0)
                {
                    await EnrichRegistroNationalitiesFromCatalogAsync(appOnlyOperaciones);
                    return BuildRegistroViewModel(normalized, fechaInicio, fechaFin, appOnlyOperaciones);
                }
                return await BuildRegistroFromRelacionesAsync(normalized, fechaInicio, fechaFin)
                    ?? new PosRegistroDiarioViewModel
                    {
                        FolioOperacion = normalized ?? string.Empty,
                        FechaInicio = fechaInicio,
                        FechaFin = fechaFin,
                        FechaTrabajo = DateTime.Today
                    };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Registro Diario desde POS.");
                return await BuildRegistroFromRelacionesAsync(Normalize(folioOperacion), fechaInicio, fechaFin)
                    ?? new PosRegistroDiarioViewModel
                    {
                        FolioOperacion = Normalize(folioOperacion) ?? string.Empty,
                        FechaInicio = fechaInicio,
                        FechaFin = fechaFin,
                        FechaTrabajo = DateTime.Today
                    };
            }
        }

        private static PosRegistroDiarioViewModel BuildRegistroViewModel(string? normalized, DateTime? fechaInicio, DateTime? fechaFin, List<PosOperacionRowViewModel> operaciones)
        {
            return new PosRegistroDiarioViewModel
            {
                FolioOperacion = normalized ?? operaciones.FirstOrDefault()?.FolioOperacion ?? string.Empty,
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                FechaTrabajo = fechaInicio?.Date ?? DateTime.Today,
                Staff = operaciones.FirstOrDefault()?.Vendedor ?? string.Empty,
                Hotel = operaciones.FirstOrDefault()?.Hotel ?? string.Empty,
                Pax = operaciones.Sum(x => x.Pax),
                TotalEfectivo = operaciones.Sum(x => x.Efectivo),
                TotalTarjeta = operaciones.Sum(x => x.Tarjeta),
                TotalGeneral = operaciones.Sum(x => x.Total),
                Operaciones = operaciones
            };
        }

        private async Task<PosRegistroDiarioViewModel?> BuildRegistroFromRelacionesAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                var relaciones = await GetRelacionesReporteDejadasAsync(fechaInicio, fechaFin, normalized);
                if (relaciones.Count == 0)
                    return null;

                var operaciones = relaciones.Select(row =>
                {
                    var fecha = TryParseRelacionDate(row.Fecha);
                    var folioVisible = FirstText(row.FolioControl, row.FolioOperacion, row.FolioApp);
                    var ticket = CleanRegistroDisplayTicket(FirstText(row.FolioPos, row.TicketPagoDejada));
                    return new PosOperacionRowViewModel
                    {
                        FolioOperacion = folioVisible,
                        FolioControl = row.FolioControl,
                        Fuente = FirstText(row.Fuente, "SISTEMA/POS"),
                        UsuarioOrigen = row.UsuarioOrigen,
                        Ticket = ticket,
                        CantidadTickets = 1,
                        Hotel = row.Hotel,
                        LlegadaSucursal = FirstText(row.Destino, row.Sitio, row.Origen, row.Hotel),
                        Gafete = row.Gafete,
                        Origen = row.Origen,
                        Sitio = row.Sitio,
                        Destino = row.Destino,
                        Unidad = row.Unidad,
                        Placas = row.Placas,
                        Telefono = row.Telefono,
                        Nacionalidad = CleanNationality(row.Nacionalidad),
                        Hora = fecha.HasValue ? fecha.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) : row.Fecha,
                        Pax = row.Pax,
                        Vendedor = FirstText(row.TaxistaNombre, row.Vendedor),
                        TipoOperacion = row.TransporteTipo,
                        Total = row.Venta > 0m ? row.Venta : row.Dejada,
                        Efectivo = row.Venta > 0m ? row.Venta : row.Dejada,
                        Tarjeta = 0m
                    };
                })
                .OrderByDescending(x => TryParseRegistroDate(x.Hora) ?? DateTime.MinValue)
                .Take(500)
                .ToList();

                return new PosRegistroDiarioViewModel
                {
                    FolioOperacion = normalized ?? operaciones.FirstOrDefault()?.FolioOperacion ?? string.Empty,
                    FechaInicio = fechaInicio,
                    FechaFin = fechaFin,
                    FechaTrabajo = fechaInicio?.Date ?? DateTime.Today,
                    Staff = operaciones.FirstOrDefault()?.Vendedor ?? string.Empty,
                    Hotel = operaciones.FirstOrDefault()?.Hotel ?? string.Empty,
                    Pax = operaciones.Sum(x => x.Pax),
                    TotalEfectivo = operaciones.Sum(x => x.Efectivo),
                    TotalTarjeta = operaciones.Sum(x => x.Tarjeta),
                    TotalGeneral = operaciones.Sum(x => x.Total),
                    Operaciones = operaciones
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible usar Relacion ticket-taxista como respaldo para Registro.");
                return null;
            }
        }

        private static string CleanRegistroDisplayTicket(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            return IsRegistroPlaceholderToken(text) ? string.Empty : text;
        }

        private static void EnrichRegistroRowsWithAppData(List<PosOperacionRowViewModel> posRows, List<PosOperacionRowViewModel> appRows)
        {
            if (posRows.Count == 0 || appRows.Count == 0)
                return;

            foreach (var pos in posRows)
            {
                var match = appRows.FirstOrDefault(app => RegistroRowsMatch(pos, app));
                if (match == null)
                    continue;

                pos.Nacionalidad = FirstText(CleanNationality(pos.Nacionalidad), CleanNationality(match.Nacionalidad));
                pos.Gafete = FirstText(pos.Gafete, match.Gafete);
                pos.Unidad = FirstText(pos.Unidad, match.Unidad);
                pos.Telefono = FirstText(pos.Telefono, match.Telefono);
                pos.TipoOperacion = FirstText(pos.TipoOperacion, match.TipoOperacion);
            }
        }

        private static bool RegistroRowsMatch(PosOperacionRowViewModel pos, PosOperacionRowViewModel app)
        {
            var posTokens = new[]
            {
                NormalizeRegistroMergeToken(pos.FolioOperacion),
                NormalizeRegistroMergeToken(pos.FolioControl),
                NormalizeRegistroMergeToken(pos.Ticket)
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var appTokens = new[]
            {
                NormalizeRegistroMergeToken(app.FolioOperacion),
                NormalizeRegistroMergeToken(app.FolioControl),
                NormalizeRegistroMergeToken(app.Ticket)
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (posTokens.Count > 0 && appTokens.Count > 0 && posTokens.Overlaps(appTokens))
                return true;

            var posDate = TryParseRegistroDate(pos.Hora);
            var appDate = TryParseRegistroDate(app.Hora);
            if (posDate.HasValue && appDate.HasValue && posDate.Value.Date != appDate.Value.Date)
                return false;

            var sameGafete = !string.IsNullOrWhiteSpace(pos.Gafete)
                && !string.IsNullOrWhiteSpace(app.Gafete)
                && SplitCatalogTokens(pos.Gafete)
                    .Select(Normalize)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Intersect(
                        SplitCatalogTokens(app.Gafete).Select(Normalize).Where(x => !string.IsNullOrWhiteSpace(x)),
                        StringComparer.OrdinalIgnoreCase)
                    .Any();
            if (!sameGafete && (!string.IsNullOrWhiteSpace(pos.Gafete) || !string.IsNullOrWhiteSpace(app.Gafete)))
                return false;

            var sameDriver = !string.IsNullOrWhiteSpace(pos.Vendedor)
                && !string.IsNullOrWhiteSpace(app.Vendedor)
                && string.Equals(Normalize(pos.Vendedor), Normalize(app.Vendedor), StringComparison.OrdinalIgnoreCase);
            var sameHotel = !string.IsNullOrWhiteSpace(pos.Hotel)
                && !string.IsNullOrWhiteSpace(app.Hotel)
                && string.Equals(Normalize(pos.Hotel), Normalize(app.Hotel), StringComparison.OrdinalIgnoreCase);
            var closeTime = !posDate.HasValue
                || !appDate.HasValue
                || Math.Abs((posDate.Value - appDate.Value).TotalMinutes) <= 2;
            var sameAmount = pos.Total <= 0m
                || app.Total <= 0m
                || Math.Abs(pos.Total - app.Total) < 0.01m;

            return sameDriver && sameHotel && closeTime && sameAmount;
        }

        private static string NormalizeRegistroToken(string? value)
        {
            var text = Normalize(value);
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : text;
        }

        private async Task<List<PosRegistroEfRow>> GetRegistroRowsFromEfSafeAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                return await GetRegistroRowsFromEfAsync(normalized, fechaInicio, fechaFin);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar registros POS/MKT; se continuara con registros de APP_TAXI.");
                return new List<PosRegistroEfRow>();
            }
        }

        private async Task<List<PosRegistroEfRow>> GetRegistroRowsFromEfAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var operaciones = await GetRegistroOperacionesBaseAsync(normalized, fechaInicio, fechaFin);
            if (operaciones.Count == 0)
                return new List<PosRegistroEfRow>();

            var folios = operaciones
                .Where(x => x.Folio.HasValue)
                .Select(x => x.Folio!.Value)
                .ToHashSet();
            var minFolio = folios.Min();
            var maxFolio = folios.Max();
            var movimientos = await _posContext.MovOperaciones
                .AsNoTracking()
                .Where(x => x.FolioOperacion.HasValue && x.FolioOperacion.Value >= minFolio && x.FolioOperacion.Value <= maxFolio)
                .ToListAsync();
            movimientos = movimientos
                .Where(x => x.FolioOperacion.HasValue && folios.Contains(x.FolioOperacion.Value))
                .ToList();
            var storeTickets = await GetStoreTicketsByOperationAsync(folios);

            var relaciones = await _posContext.RelacionesTicketTaxista.AsNoTracking().Take(2000).ToListAsync();
            var controles = await _posContext.AppMovilFolioControl.AsNoTracking().Take(2000).ToListAsync();

            return operaciones.Select(o =>
            {
                var movs = movimientos.Where(m => m.FolioOperacion == o.Folio).ToList();
                var totalMov = movs.Sum(m => ToDecimal(m.TotalJoyeria) + ToDecimal(m.TotalCompra));
                var folioText = o.Folio?.ToString() ?? string.Empty;
                var rel = relaciones.FirstOrDefault(r => TextMatches(r.FolioOperacion, folioText) || TextMatches(r.FolioPos, folioText));
                var control = !string.IsNullOrWhiteSpace(rel?.FolioApp)
                    ? controles.FirstOrDefault(c => TextMatches(c.FolioAppOriginal, rel.FolioApp))
                    : null;
                storeTickets.TryGetValue(o.Folio ?? 0, out var ticketsFromStore);
                var ticketsFromMov = JoinDistinctText(movs.Select(m => m.FolioSoluone));
                return new PosRegistroEfRow(
                    o.Folio?.ToString() ?? string.Empty,
                    FirstText(control?.FolioControl, rel?.FolioApp),
                    FirstText(rel?.FolioPos, ticketsFromMov, ticketsFromStore, rel?.FolioApp, control?.FolioControl),
                    o.Fecha,
                    o.Pax ?? 0,
                    o.Hotel ?? string.Empty,
                    o.Tipo ?? string.Empty,
                    totalMov > 0m ? totalMov : ToDecimal(o.TotalCompra) + ToDecimal(o.TotalJoyeria),
                    o.StaffNombre ?? string.Empty,
                    rel?.TaxistaNombre ?? string.Empty,
                    movs.Sum(m => ToDecimal(m.TotalEfectivo)),
                    movs.Sum(m => ToDecimal(m.TotalTarjeta)));
            }).ToList();
        }

        private async Task<Dictionary<int, string>> GetStoreTicketsByOperationAsync(IReadOnlyCollection<int> folios)
        {
            var validFolios = folios.Where(x => x > 0).Distinct().Take(200).ToList();
            if (validFolios.Count == 0)
                return new Dictionary<int, string>();

            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                var parameters = validFolios
                    .Select((folio, index) =>
                    {
                        var name = $"@folio{index}";
                        AddParameter(command, name, folio);
                        return name;
                    })
                    .ToList();
                var inClause = string.Join(", ", parameters);
                command.CommandText = $"""
                    SELECT CAST(folioregistro AS INT) AS FolioOperacion,
                           CONVERT(NVARCHAR(80), folio_remision) AS Ticket
                    FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
                    WHERE ISNUMERIC(CONVERT(NVARCHAR(50), folioregistro)) = 1
                      AND CAST(folioregistro AS INT) IN ({inClause})
                      AND NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(80), folio_remision))), '') IS NOT NULL
                    UNION ALL
                    SELECT CAST(folio_registro AS INT) AS FolioOperacion,
                           CONVERT(NVARCHAR(80), folio_factura) AS Ticket
                    FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
                    WHERE ISNUMERIC(CONVERT(NVARCHAR(50), folio_registro)) = 1
                      AND CAST(folio_registro AS INT) IN ({inClause})
                      AND NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(80), folio_factura))), '') IS NOT NULL
                    """;

                var rows = await ReadRowsAsync(command);
                return rows
                    .Select(row => new
                    {
                        Folio = Convert.ToInt32(ToDecimal(PickObject(row, "FolioOperacion")), CultureInfo.InvariantCulture),
                        Ticket = Convert.ToString(PickObject(row, "Ticket"), CultureInfo.InvariantCulture)
                    })
                    .Where(x => x.Folio > 0 && !string.IsNullOrWhiteSpace(x.Ticket))
                    .GroupBy(x => x.Folio)
                    .ToDictionary(
                        x => x.Key,
                        x => JoinDistinctText(x.Select(y => y.Ticket)),
                        EqualityComparer<int>.Default);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No fue posible cargar tickets de Compuadmo/Joyeria por folio de operacion.");
                return new Dictionary<int, string>();
            }
        }

        private static string JoinDistinctText(IEnumerable<string?> values) =>
            string.Join(", ", values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));

        private async Task<List<PosOperacionRowViewModel>> GetRegistroRowsFromDejadasBaseSqlAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync();

                var nacionalidadSql = await PosColumnExistsAsync(connection, "dejadas", "nacionalidad")
                    ? "COALESCE(CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(d.nacionalidad, '')))) IN ('', 'NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-') THEN NULL ELSE d.nacionalidad END, '')"
                    : "''";

                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (800)
    COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), '') AS FolioOperacion,
    COALESCE(NULLIF(d.idstaff, ''), NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), '') AS FolioControl,
    CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(d.nombrecajero, '')))) = 'APP MOVIL' THEN 'APP MOVIL' ELSE 'SISTEMA/POS' END AS Fuente,
    COALESCE(NULLIF(d.nombrecajero, ''), 'POS') AS UsuarioOrigen,
    COALESCE(NULLIF(d.codigorecepcion, ''), '') AS Ticket,
    COALESCE(NULLIF(d.gafete, ''), '') AS Gafete,
    d.fecha AS FechaOperacion,
    COALESCE(NULLIF(CONVERT(nvarchar(40), d.hora), ''), '') AS HoraTexto,
    COALESCE(NULLIF(d.nombrestaff, ''), NULLIF(d.nombrevendedor, ''), '') AS Taxista,
    COALESCE(d.tipotransporte, '') AS TipoOperacion,
    COALESCE(NULLIF(d.hotel, ''), NULLIF(d.nombrealmacen, ''), '') AS Hotel,
    COALESCE(d.unidad, '') AS Unidad,
    COALESCE(d.telefono, '') AS Telefono,
    {nacionalidadSql} AS Nacionalidad,
    COALESCE(d.idtaxi, 0) AS CatalogId,
    COALESCE(d.pax, 0) AS Pax,
    COALESCE(NULLIF(d.totalventa, 0), d.total, 0) AS Total,
    COALESCE(d.totalefectivo, 0) AS Efectivo,
    COALESCE(d.totaltarjeta, 0) AS Tarjeta
FROM {PosTable("dejadas")} d
WHERE (@inicio IS NULL OR d.fecha >= @inicio)
  AND (@finExclusiva IS NULL OR d.fecha < @finExclusiva)
  AND (
        @q IS NULL
     OR d.idstaff LIKE '%' + @q + '%'
     OR d.folioregistrostr LIKE '%' + @q + '%'
     OR d.codigorecepcion LIKE '%' + @q + '%'
     OR CONVERT(nvarchar(60), d.folioregistro) LIKE '%' + @q + '%'
     OR d.gafete LIKE '%' + @q + '%'
     OR d.nombrestaff LIKE '%' + @q + '%'
     OR d.nombrevendedor LIKE '%' + @q + '%'
     OR d.hotel LIKE '%' + @q + '%'
     OR d.nombrealmacen LIKE '%' + @q + '%'
     OR d.tipotransporte LIKE '%' + @q + '%'
  )
ORDER BY d.fecha DESC, d.hora DESC, d.folioregistro DESC",
                    ("@inicio", fechaInicio?.Date),
                    ("@finExclusiva", fechaFin?.Date.AddDays(1)),
                    ("@q", normalized));

                return rows.Select(row =>
                {
                    var total = ToDecimal(PickObject(row, "Total"));
                    var efectivo = ToDecimal(PickObject(row, "Efectivo"));
                    var tarjeta = ToDecimal(PickObject(row, "Tarjeta"));
                    var fecha = PickDate(row, "FechaOperacion");
                    var hora = PickText(row, "HoraTexto");
                    return new PosOperacionRowViewModel
                    {
                        FolioOperacion = PickText(row, "FolioOperacion"),
                        FolioControl = PickText(row, "FolioControl"),
                        Fuente = FirstText(PickText(row, "Fuente"), "SISTEMA/POS"),
                        UsuarioOrigen = PickText(row, "UsuarioOrigen"),
                        Ticket = CleanRegistroDisplayTicket(PickText(row, "Ticket")),
                        CantidadTickets = 1,
                        Hotel = PickText(row, "Hotel"),
                        LlegadaSucursal = PickText(row, "Hotel"),
                        Gafete = PickText(row, "Gafete"),
                        CatalogId = PickInt(row, "CatalogId"),
                        Unidad = PickText(row, "Unidad"),
                        Telefono = PickText(row, "Telefono"),
                        Nacionalidad = CleanNationality(PickText(row, "Nacionalidad")),
                        Hora = FirstText(hora, fecha),
                        Pax = PickInt(row, "Pax"),
                        Vendedor = PickText(row, "Taxista"),
                        TipoOperacion = PickText(row, "TipoOperacion"),
                        Total = total,
                        Efectivo = efectivo > 0m || tarjeta > 0m ? efectivo : total,
                        Tarjeta = tarjeta
                    };
                })
                .GroupBy(BuildRegistroMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(MergeRegistroRows)
                .OrderByDescending(x => TryParseRegistroDate(x.Hora) ?? DateTime.MinValue)
                .Take(500)
                .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Registro directo desde dbo.dejadas.");
                return new List<PosOperacionRowViewModel>();
            }
        }

        private async Task<List<PosOperacionRowViewModel>> GetRegistroRowsFromAppMovilAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var combinedRows = new List<PosOperacionRowViewModel>();

            try
            {
                var apiRows = _autoSyncAppMovilFromApi
                    ? await _appTaxiApi.GetTripRecordsAsync(normalized, fechaInicio, fechaFin)
                    : new List<PosOperacionRowViewModel>();
                if (apiRows.Count > 0)
                {
                    await EnrichAppRowsWithAssignedTicketsAsync(apiRows);
                    _logger.LogInformation("Registro cargado desde API APP_TAXI. Filas: {Count}", apiRows.Count);
                }

                combinedRows.AddRange(apiRows);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar registros desde API APP_TAXI para Registro.");
            }

            try
            {
                combinedRows.AddRange(await GetRegistroRowsFromAppMovilSqlAsync(normalized, fechaInicio, fechaFin));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar AppMovilRegistro local para Registro.");
            }

            try
            {
                combinedRows.AddRange(await GetRegistroRowsFromDejadasSqlAsync(normalized, fechaInicio, fechaFin));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar dbo.dejadas para Registro.");
            }

            combinedRows = combinedRows
                .GroupBy(BuildRegistroMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(MergeRegistroRows)
                .OrderByDescending(x => TryParseRegistroDate(x.Hora) ?? DateTime.MinValue)
                .Take(500)
                .ToList();

            await EnrichRegistroRowsWithStoreTicketsAsync(combinedRows);
            return combinedRows;
        }

        private async Task EnrichRegistroRowsWithStoreTicketsAsync(IEnumerable<PosOperacionRowViewModel> rows)
        {
            var rowsList = rows.ToList();
            var folios = rowsList
                .SelectMany(row => new[] { row.FolioOperacion, row.FolioControl })
                .Select(Normalize)
                .Where(value => !string.IsNullOrWhiteSpace(value) && value.All(char.IsDigit))
                .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folio) ? folio : 0)
                .Where(folio => folio > 0)
                .Distinct()
                .ToHashSet();
            if (folios.Count == 0)
                return;

            var storeTickets = await GetStoreTicketsByOperationAsync(folios);
            foreach (var row in rowsList)
            {
                var currentTicket = Normalize(row.Ticket);
                if (!string.IsNullOrWhiteSpace(currentTicket) && !currentTicket.Equals("SIN TICKET POS", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rowDate = TryParseRegistroDate(row.Hora)?.Date;
                var candidates = new[] { row.FolioOperacion, row.FolioControl }
                    .Select(Normalize)
                    .Where(value => !string.IsNullOrWhiteSpace(value) && value.All(char.IsDigit))
                    .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folio) ? folio : 0)
                    .Where(folio => folio > 0);

                foreach (var folio in candidates)
                {
                    var ticket = rowDate.HasValue
                        ? await GetStoreTicketByOperationAndDateAsync(folio, rowDate.Value)
                        : null;
                    if (!rowDate.HasValue && string.IsNullOrWhiteSpace(ticket) && storeTickets.TryGetValue(folio, out var ticketFromStore))
                        ticket = ticketFromStore;
                    if (!string.IsNullOrWhiteSpace(ticket))
                    {
                        row.Ticket = ticket;
                        break;
                    }
                }
            }
        }

        private async Task<string?> GetStoreTicketByOperationAndDateAsync(int folio, DateTime fecha)
        {
            try
            {
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (20) Ticket
FROM
(
    SELECT CONVERT(NVARCHAR(80), folio_remision) AS Ticket, fecha AS Fecha
    FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE (folio_operacion = @folio OR folioregistro = @folio)
      AND CAST(fecha AS date) = @fecha
      AND NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(80), folio_remision))), '') IS NOT NULL
    UNION ALL
    SELECT CONVERT(NVARCHAR(80), folio_factura) AS Ticket, fecha AS Fecha
    FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE (folio_operacion = @folio OR folio_registro = @folio)
      AND CAST(fecha AS date) = @fecha
      AND NULLIF(LTRIM(RTRIM(CONVERT(NVARCHAR(80), folio_factura))), '') IS NOT NULL
) StoreTickets
ORDER BY Fecha DESC",
                    ("@folio", folio),
                    ("@fecha", fecha.Date));

                return JoinDistinctText(rows.Select(row => Convert.ToString(PickObject(row, "Ticket"), CultureInfo.InvariantCulture)));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No fue posible cargar ticket por folio y fecha desde remisioM.");
                return null;
            }
        }

        private async Task<List<PosOperacionRowViewModel>> GetRegistroRowsFromAppMovilSqlAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (500)
    COALESCE(NULLIF(r.FolioOperacion, ''), '') AS FolioOperacion,
    COALESCE(c.FolioControl, a.folio_app, '') AS FolioControl,
    'APP MOVIL' AS Fuente,
    COALESCE(NULLIF(a.usuario_movil, ''), 'hostinger') AS UsuarioOrigen,
    COALESCE(NULLIF(r.FolioPos, ''), a.folio_pos, '') AS Ticket,
    COALESCE(NULLIF(a.folio_gafete, ''), NULLIF(r.Gafete, ''), '') AS Gafete,
    a.fecha_operacion AS FechaOperacion,
    a.id_catalogo AS CatalogId,
    COALESCE(a.vendedor_nombre, '') AS Taxista,
    COALESCE(a.tipo_operacion, '') AS TipoOperacion,
    COALESCE(a.hotel, '') AS Hotel,
    COALESCE(a.origen, '') AS Origen,
    COALESCE(a.sitio, '') AS Sitio,
    COALESCE(a.destino, '') AS Destino,
    COALESCE(a.unidad, '') AS Unidad,
    COALESCE(a.placas, '') AS Placas,
    COALESCE(a.telefono_taxista, '') AS Telefono,
    COALESCE(a.nacionalidad, '') AS Nacionalidad,
    COALESCE(a.pax, 0) AS Pax,
    COALESCE(a.total, 0) AS Total,
    COALESCE(a.efectivo, 0) AS Efectivo,
    COALESCE(a.tarjeta, 0) AS Tarjeta
FROM {PosTable("AppMovilRegistro")} a
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioControl = a.folio_app OR c.FolioAppOriginal = a.folio_app_original OR c.FolioAppOriginal = a.folio_app
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioApp = a.folio_app
WHERE (@inicio IS NULL OR a.fecha_operacion >= @inicio)
  AND (@finExclusiva IS NULL OR a.fecha_operacion < @finExclusiva)
  AND (
        @q IS NULL
     OR a.folio_app LIKE '%' + @q + '%'
     OR a.folio_pos LIKE '%' + @q + '%'
     OR a.folio_gafete LIKE '%' + @q + '%'
     OR a.vendedor_nombre LIKE '%' + @q + '%'
     OR a.hotel LIKE '%' + @q + '%'
     OR a.origen LIKE '%' + @q + '%'
     OR a.sitio LIKE '%' + @q + '%'
     OR a.destino LIKE '%' + @q + '%'
     OR a.tipo_operacion LIKE '%' + @q + '%'
     OR r.FolioOperacion LIKE '%' + @q + '%'
     OR r.FolioPos LIKE '%' + @q + '%'
  )
ORDER BY a.fecha_operacion DESC",
                ("@inicio", fechaInicio?.Date),
                ("@finExclusiva", fechaFin?.Date.AddDays(1)),
                ("@q", normalized));

            return rows.Select(row =>
            {
                var total = ToDecimal(PickObject(row, "Total"));
                var efectivo = ToDecimal(PickObject(row, "Efectivo"));
                var tarjeta = ToDecimal(PickObject(row, "Tarjeta"));
                var hotel = PickText(row, "Hotel");
                var origen = PickText(row, "Origen");
                var sitio = PickText(row, "Sitio");
                var destino = PickText(row, "Destino");
                return new PosOperacionRowViewModel
                {
                    FolioOperacion = PickText(row, "FolioOperacion"),
                    FolioControl = PickText(row, "FolioControl"),
                    Fuente = FirstText(PickText(row, "Fuente"), "APP MOVIL"),
                    UsuarioOrigen = PickText(row, "UsuarioOrigen"),
                    Ticket = PickText(row, "Ticket"),
                    CantidadTickets = 1,
                    Hotel = FirstText(hotel, origen, sitio, destino),
                    LlegadaSucursal = FirstText(destino, sitio, origen, hotel),
                    Gafete = PickText(row, "Gafete"),
                    CatalogId = PickInt(row, "CatalogId"),
                    Origen = origen,
                    Sitio = sitio,
                    Destino = destino,
                    Unidad = PickText(row, "Unidad"),
                    Placas = PickText(row, "Placas"),
                    Telefono = PickText(row, "Telefono"),
                    Nacionalidad = CleanNationality(PickText(row, "Nacionalidad")),
                    Hora = PickDate(row, "FechaOperacion"),
                    Pax = PickInt(row, "Pax"),
                    Vendedor = PickText(row, "Taxista"),
                    TipoOperacion = PickText(row, "TipoOperacion"),
                    Total = total,
                    Efectivo = efectivo > 0m || tarjeta > 0m ? efectivo : total,
                    Tarjeta = tarjeta
                };
            }).ToList();
        }

        private async Task<List<PosOperacionRowViewModel>> GetRegistroRowsFromDejadasSqlAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            var dejadasNacionalidadSql = await PosColumnExistsAsync(connection, "dejadas", "nacionalidad")
                ? "COALESCE(CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(d.nacionalidad, '')))) IN ('', 'NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-') THEN NULL ELSE d.nacionalidad END, CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(a.nacionalidad, '')))) IN ('', 'NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-') THEN NULL ELSE a.nacionalidad END, '')"
                : "COALESCE(CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(a.nacionalidad, '')))) IN ('', 'NO CAPTURADA', 'SIN CAPTURAR', 'SIN NACIONALIDAD', '-') THEN NULL ELSE a.nacionalidad END, '')";

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (500)
    COALESCE(NULLIF(d.folioregistrostr, ''), NULLIF(d.codigorecepcion, ''), CONVERT(nvarchar(60), d.folioregistro), '') AS FolioOperacion,
    COALESCE(NULLIF(d.idstaff, ''), NULLIF(d.folioregistrostr, ''), NULLIF(d.codigorecepcion, ''), CONVERT(nvarchar(60), d.folioregistro), '') AS FolioControl,
    CASE
        WHEN UPPER(LTRIM(RTRIM(COALESCE(d.nombrecajero, '')))) = 'APP MOVIL' THEN 'APP MOVIL'
        ELSE 'SISTEMA/POS'
    END AS Fuente,
    COALESCE(NULLIF(d.nombrecajero, ''), 'POS') AS UsuarioOrigen,
    COALESCE(NULLIF(a.folio_gafete, ''), NULLIF(d.gafete, ''), '') AS Gafete,
    d.fecha AS FechaOperacion,
    COALESCE(CONVERT(nvarchar(40), d.hora), '') AS HoraTexto,
    COALESCE(NULLIF(d.nombrestaff, ''), NULLIF(d.nombrevendedor, ''), '') AS Taxista,
    COALESCE(d.tipotransporte, '') AS TipoOperacion,
    COALESCE(NULLIF(d.hotel, ''), NULLIF(d.nombrealmacen, ''), '') AS Hotel,
    COALESCE(d.unidad, '') AS Unidad,
    COALESCE(d.telefono, '') AS Telefono,
    {dejadasNacionalidadSql} AS Nacionalidad,
    COALESCE(a.id_catalogo, d.idtaxi, 0) AS CatalogId,
    COALESCE(d.pax, 0) AS Pax,
    COALESCE(NULLIF(d.totalventa, 0), d.total, 0) AS Total,
    COALESCE(d.codigorecepcion, '') AS TicketVenta,
    COALESCE(d.totalefectivo, 0) AS Efectivo,
    COALESCE(d.totaltarjeta, 0) AS Tarjeta
FROM {PosTable("dejadas")} d
LEFT JOIN {PosTable("AppMovilRegistro")} a
    ON CAST(a.fecha_operacion AS date) = CAST(d.fecha AS date)
   AND (
        a.folio_app = COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), d.codigorecepcion)
     OR a.folio_app_original = COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), d.codigorecepcion)
     OR a.folio_pos = COALESCE(NULLIF(d.folioregistrostr, ''), CONVERT(nvarchar(60), d.folioregistro), d.codigorecepcion)
     OR a.folio_app = d.idstaff
     OR a.folio_app_original = d.idstaff
     OR (ISNUMERIC(a.folio_app) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, d.folioregistro))
     OR (ISNUMERIC(a.folio_app_original) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, d.folioregistro))
     OR a.folio_app = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
     OR a.folio_app_original = RIGHT('0000' + CONVERT(nvarchar(20), d.folioregistro), 4)
       )
WHERE (@inicio IS NULL OR d.fecha >= @inicio)
  AND (@finExclusiva IS NULL OR d.fecha < @finExclusiva)
  AND (
        @q IS NULL
     OR d.idstaff LIKE '%' + @q + '%'
     OR d.folioregistrostr LIKE '%' + @q + '%'
     OR d.codigorecepcion LIKE '%' + @q + '%'
     OR CONVERT(nvarchar(60), d.folioregistro) LIKE '%' + @q + '%'
     OR d.gafete LIKE '%' + @q + '%'
     OR d.nombrestaff LIKE '%' + @q + '%'
     OR d.nombrevendedor LIKE '%' + @q + '%'
     OR d.hotel LIKE '%' + @q + '%'
     OR d.nombrealmacen LIKE '%' + @q + '%'
     OR d.tipotransporte LIKE '%' + @q + '%'
  )
ORDER BY d.fecha DESC, d.hora DESC",
                ("@inicio", fechaInicio?.Date),
                ("@finExclusiva", fechaFin?.Date.AddDays(1)),
                ("@q", normalized));

            return rows.Select(row =>
            {
                var total = ToDecimal(PickObject(row, "Total"));
                var efectivo = ToDecimal(PickObject(row, "Efectivo"));
                var tarjeta = ToDecimal(PickObject(row, "Tarjeta"));
                var hotel = PickText(row, "Hotel");
                return new PosOperacionRowViewModel
                {
                    FolioOperacion = PickText(row, "FolioOperacion"),
                    FolioControl = PickText(row, "FolioControl"),
                    Fuente = FirstText(PickText(row, "Fuente"), "APP MOVIL"),
                    UsuarioOrigen = PickText(row, "UsuarioOrigen"),
                    Ticket = PickText(row, "TicketVenta"),
                    CantidadTickets = 1,
                    Hotel = hotel,
                    LlegadaSucursal = hotel,
                    Gafete = PickText(row, "Gafete"),
                    CatalogId = PickInt(row, "CatalogId"),
                    Unidad = PickText(row, "Unidad"),
                    Telefono = PickText(row, "Telefono"),
                    Nacionalidad = CleanNationality(PickText(row, "Nacionalidad")),
                    Hora = FirstText(PickText(row, "HoraTexto"), PickDate(row, "FechaOperacion")),
                    Pax = PickInt(row, "Pax"),
                    Vendedor = PickText(row, "Taxista"),
                    TipoOperacion = PickText(row, "TipoOperacion"),
                    Total = total,
                    Efectivo = efectivo > 0m || tarjeta > 0m ? efectivo : total,
                    Tarjeta = tarjeta
                };
            }).ToList();
        }

        private async Task EnrichAppRowsWithAssignedTicketsAsync(IEnumerable<PosOperacionRowViewModel> rows)
        {
            var appRows = rows.ToList();
            if (appRows.Count == 0)
                return;

            var controles = await _posContext.AppMovilFolioControl.AsNoTracking().Take(2000).ToListAsync();
            var relaciones = await _posContext.RelacionesTicketTaxista.AsNoTracking().Take(2000).ToListAsync();
            var appFolios = appRows
                .Select(x => FirstText(x.FolioControl, x.Ticket))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var appRegistroTickets = (await _posContext.AppMovilRegistros.AsNoTracking()
                .Where(x => x.FolioApp != null)
                .Select(x => new { x.FolioApp, x.FolioPos })
                .Take(2000)
                .ToListAsync())
                .Where(x => !string.IsNullOrWhiteSpace(x.FolioApp) && appFolios.Contains(x.FolioApp))
                .ToList();
            var appMatches = appRows
                .Select(row =>
                {
                    var appFolio = FirstText(row.FolioControl, row.Ticket);
                    var rel = FindRelacionForAppRow(relaciones, controles, appFolio, null);
                    return new { Row = row, AppFolio = appFolio, Rel = rel };
                })
                .ToList();
            var relatedOperacionFolios = appMatches
                .Select(x => int.TryParse(Normalize(x.Rel?.FolioOperacion), NumberStyles.Integer, CultureInfo.InvariantCulture, out var folio) ? folio : 0)
                .Where(x => x > 0)
                .ToHashSet();
            var storeTickets = await GetStoreTicketsByOperationAsync(relatedOperacionFolios);

            foreach (var match in appMatches)
            {
                var appTicket = appRegistroTickets
                    .FirstOrDefault(x => TextMatchesNonEmpty(x.FolioApp, match.AppFolio))
                    ?.FolioPos;
                var folioOperacion = int.TryParse(Normalize(match.Rel?.FolioOperacion), NumberStyles.Integer, CultureInfo.InvariantCulture, out var folio)
                    ? folio
                    : 0;
                storeTickets.TryGetValue(folioOperacion, out var ticketsFromStore);
                match.Row.FolioOperacion = match.Rel?.FolioOperacion ?? string.Empty;
                match.Row.FolioControl = match.AppFolio;
                match.Row.Ticket = FirstText(match.Rel?.FolioPos, appTicket, ticketsFromStore);
            }
        }

        private static PosRelacionTicketTaxista? FindRelacionForAppRow(
            IEnumerable<PosRelacionTicketTaxista> relaciones,
            IEnumerable<PosAppMovilFolioControl> controles,
            string? folioApp,
            string? folioPos)
        {
            var ticket = FirstText(folioApp, folioPos);
            var original = controles
                .FirstOrDefault(c => TextMatchesNonEmpty(c.FolioControl, ticket) || TextMatchesNonEmpty(c.FolioAppOriginal, ticket))
                ?.FolioAppOriginal;

            return relaciones.FirstOrDefault(r =>
                TextMatchesNonEmpty(r.FolioApp, ticket)
                || TextMatchesNonEmpty(r.FolioApp, original)
                || TextMatchesNonEmpty(r.FolioPos, folioPos));
        }

        private static bool TextMatchesNonEmpty(string? value, string? normalized) =>
            !string.IsNullOrWhiteSpace(normalized) && TextMatches(value, normalized.Trim());

        private static PosOperacionRowViewModel MergeRegistroRows(IEnumerable<PosOperacionRowViewModel> rows)
        {
            var list = rows
                .OrderByDescending(HasRegistroOperacionOrTicket)
                .ThenBy(x => x.Fuente.Contains("APP MOVIL", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(x => TryParseRegistroDate(x.Hora) ?? DateTime.MinValue)
                .ToList();
            var first = list.First();
            first.FolioOperacion = FirstText(first.FolioOperacion, list.Select(x => x.FolioOperacion).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.FolioControl = FirstText(first.FolioControl, list.Select(x => x.FolioControl).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Fuente = FirstText(first.Fuente, list.Select(x => x.Fuente).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.UsuarioOrigen = FirstText(first.UsuarioOrigen, list.Select(x => x.UsuarioOrigen).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Hora = FirstText(
                list.Select(x => x.Hora).FirstOrDefault(IsRegistroUsefulTime),
                first.Hora,
                list.Select(x => x.Hora).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Gafete = JoinDistinctGafetes(list.Select(x => x.Gafete));
            first.Ticket = FirstText(first.Ticket, JoinDistinctText(list.Select(x => x.Ticket)));
            first.Hotel = FirstText(first.Hotel, list.Select(x => x.Hotel).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.LlegadaSucursal = FirstText(first.LlegadaSucursal, list.Select(x => x.LlegadaSucursal).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Unidad = FirstText(first.Unidad, list.Select(x => x.Unidad).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Telefono = FirstText(first.Telefono, list.Select(x => x.Telefono).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Nacionalidad = FirstText(CleanNationality(first.Nacionalidad), list.Select(x => CleanNationality(x.Nacionalidad)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.Vendedor = FirstText(first.Vendedor, list.Select(x => x.Vendedor).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            first.TipoOperacion = FirstText(first.TipoOperacion, list.Select(x => x.TipoOperacion).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)));
            if (first.Total <= 0)
                first.Total = list.Select(x => x.Total).FirstOrDefault(x => x > 0);
            if (first.Efectivo <= 0)
                first.Efectivo = list.Select(x => x.Efectivo).FirstOrDefault(x => x > 0);
            if (first.Tarjeta <= 0)
                first.Tarjeta = list.Select(x => x.Tarjeta).FirstOrDefault(x => x > 0);
            return first;
        }

        private static string BuildRegistroMergeKey(PosOperacionRowViewModel row)
        {
            var operation = NormalizeRegistroMergeToken(row.FolioOperacion);
            if (!string.IsNullOrWhiteSpace(operation))
                return $"folio:{operation}";

            var control = NormalizeRegistroMergeToken(row.FolioControl);
            if (!string.IsNullOrWhiteSpace(control))
                return $"folio:{control}";

            var ticket = NormalizeRegistroMergeToken(row.Ticket);
            if (!string.IsNullOrWhiteSpace(ticket))
                return $"ticket:{ticket}";

            return string.Join("|", new[]
            {
                TryParseRegistroDate(row.Hora)?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? (row.Hora ?? string.Empty),
                Normalize(row.Hotel) ?? string.Empty,
                Normalize(row.Vendedor) ?? string.Empty,
                Normalize(row.Gafete) ?? string.Empty,
                Normalize(row.TipoOperacion) ?? string.Empty,
                row.Total.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static string NormalizeRegistroMergeToken(string? value)
        {
            var text = NormalizeRegistroToken(value);
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return IsRegistroPlaceholderToken(text) ? string.Empty : text;
        }

        private static bool IsRegistroPlaceholderToken(string value)
        {
            var normalized = Normalize(value) ?? string.Empty;
            return normalized.Length == 0
                || normalized == "-"
                || normalized == "SIN TICKET"
                || normalized == "SIN TICKET POS"
                || normalized == "SIN OPERACION"
                || normalized == "SIN OPERACION SIN TICKET POS"
                || normalized == "SIN FOLIO";
        }

        private static bool IsRegistroUsefulTime(string? value)
        {
            var date = TryParseRegistroDate(value);
            return date.HasValue && date.Value.TimeOfDay != TimeSpan.Zero;
        }

        private static bool HasRegistroOperacionOrTicket(PosOperacionRowViewModel row)
        {
            return !string.IsNullOrWhiteSpace(row.FolioOperacion)
                || !string.IsNullOrWhiteSpace(row.Ticket)
                || !string.IsNullOrWhiteSpace(row.FolioControl);
        }

        private static string JoinDistinctGafetes(IEnumerable<string?> values)
        {
            var gafetes = values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .SelectMany(x => (x ?? string.Empty)
                    .Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", gafetes);
        }

        private static bool DateInRange(DateTime? fecha, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (!fecha.HasValue)
                return !fechaInicio.HasValue && !fechaFin.HasValue;
            var date = fecha.Value.Date;
            if (fechaInicio.HasValue && date < fechaInicio.Value)
                return false;
            if (fechaFin.HasValue && date > fechaFin.Value)
                return false;
            return true;
        }

        private static DateTime? TryParseRegistroDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)
                || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                ? parsed
                : null;
        }

        private async Task<List<PosOperacion>> GetRegistroOperacionesBaseAsync(string? normalized, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (normalized == null && !fechaInicio.HasValue && !fechaFin.HasValue)
            {
                var today = DateTime.Today;
                var latestDate = await _posContext.Operaciones
                    .AsNoTracking()
                    .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == today)
                    .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date)
                    ?? await _posContext.Operaciones
                        .AsNoTracking()
                        .Where(x => x.Fecha.HasValue)
                        .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date);
                if (!latestDate.HasValue)
                    return new List<PosOperacion>();

                return await _posContext.Operaciones
                    .AsNoTracking()
                    .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == latestDate.Value)
                    .OrderByDescending(x => x.Fecha)
                    .ThenByDescending(x => x.Folio)
                    .Take(150)
                    .ToListAsync();
            }

            var operacionesQuery = _posContext.Operaciones.AsNoTracking();
            if (fechaInicio.HasValue)
                operacionesQuery = operacionesQuery.Where(x => x.Fecha.HasValue && x.Fecha.Value >= fechaInicio.Value);
            if (fechaFin.HasValue)
            {
                var finExclusive = fechaFin.Value.AddDays(1);
                operacionesQuery = operacionesQuery.Where(x => x.Fecha.HasValue && x.Fecha.Value < finExclusive);
            }

            var operaciones = await operacionesQuery
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.Folio)
                .Take(300)
                .ToListAsync();
            var relaciones = await _posContext.RelacionesTicketTaxista.AsNoTracking().Take(1000).ToListAsync();
            var controles = await _posContext.AppMovilFolioControl.AsNoTracking().Take(1000).ToListAsync();

            var normalizedSearch = normalized ?? string.Empty;
            var query =
                from o in operaciones
                join r in relaciones on (o.Folio?.ToString() ?? string.Empty) equals (r.FolioOperacion ?? string.Empty) into rels
                from r in rels.DefaultIfEmpty()
                join c in controles on (r?.FolioApp ?? string.Empty) equals c.FolioAppOriginal into ctrls
                from c in ctrls.DefaultIfEmpty()
                where TextMatches(o.Folio?.ToString(), normalizedSearch)
                   || TextMatches(o.StaffNombre, normalizedSearch)
                   || TextMatches(o.Hotel, normalizedSearch)
                   || TextMatches(r?.TaxistaNombre, normalizedSearch)
                   || TextMatches(r?.Gafete, normalizedSearch)
                   || TextMatches(r?.FolioPos, normalizedSearch)
                   || TextMatches(r?.FolioApp, normalizedSearch)
                   || TextMatches(c?.FolioControl, normalizedSearch)
                select o;

            return query.Take(150).ToList();
        }

        private sealed record PosRegistroEfRow(string Folio, string FolioControl, string TicketApp, DateTime? Fecha, int Pax, string Hotel, string TipoOperacion, decimal Total, string Staff, string Taxista, decimal Efectivo, decimal Tarjeta);



public async Task<bool> UpdateRegistroAsync(string folioOperacion, string staff, int pax, DateTime fechaTrabajo, string usuario)
        {
            var normalized = Normalize(folioOperacion);
            if (normalized == null)
                return false;
            if (await IsCorteClosedAsync(fechaTrabajo.Date))
                return false;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
UPDATE {PosTable("operacion")}
SET fecha = @fecha,
    staffnombre = CASE WHEN @staff = '' THEN staffnombre ELSE @staff END,
    pax = @pax
WHERE CONVERT(nvarchar(30), folio) = @folio";
            AddParameter(command, "@fecha", fechaTrabajo.Date);
            AddParameter(command, "@staff", staff?.Trim() ?? string.Empty);
            AddParameter(command, "@pax", Math.Max(pax, 0));
            AddParameter(command, "@folio", normalized);
            var affected = await ExecuteNonQueryAsync(command);
            if (affected > 0)
                await InsertPosAuditoriaAsync(connection, transaction, usuario, "Registro", "Guardar", normalized, "Registro diario actualizado en POS operacion",
                    BuildAuditJson(("FolioOperacion", normalized), ("Staff", staff), ("Pax", pax), ("FechaTrabajo", fechaTrabajo.Date)));
            await transaction.CommitAsync();
            return affected > 0;
        }


    }
}
