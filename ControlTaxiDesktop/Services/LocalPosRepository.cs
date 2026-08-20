using System.Globalization;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Services;

public sealed class LocalPosRepository(LocalDatabase database)
{
    private readonly LocalSqlServerSource? _sqlSource = LocalSqlServerSource.TryLoad();
    private readonly CommissionSettingsRepository _commissionSettings = new(database);
    private readonly CommissionConfigurationResolver _commissionResolver = new(new CommissionSettingsRepository(database));
    private bool _commissionSettingsLoaded;
    private bool IsPlaza28SqlMode => _sqlSource is not null;

    private async Task EnsureCommissionSettingsLoadedAsync()
    {
        if (_commissionSettingsLoaded) return;
        await _commissionResolver.InitializeAsync();
        _commissionSettingsLoaded = true;
    }

    public Task<IReadOnlyList<LocalProduct>> GetProductsAsync(string? search = null)
    {
        var sql = string.IsNullOrWhiteSpace(search)
            ? "SELECT Id,Codigo,Nombre,Precio,Iva,Existencia,Activo FROM LocalProductos ORDER BY Nombre;"
            : "SELECT Id,Codigo,Nombre,Precio,Iva,Existencia,Activo FROM LocalProductos WHERE Codigo LIKE $q OR Nombre LIKE $q ORDER BY Nombre;";
        return ReadAsync(sql, Product, string.IsNullOrWhiteSpace(search) ? [] : [("$q", "%" + search.Trim() + "%")]);
    }

    public Task<IReadOnlyList<LocalSale>> GetSalesAsync(DateTime? start = null, DateTime? end = null)
    {
        const string sql = """
            SELECT Id,Folio,Fecha,Cliente,Estatus,Subtotal,Iva,Total,Pagado,EstadoPago,Usuario
            FROM LocalVentas
            WHERE Fecha >= $start AND Fecha < $end
            ORDER BY Fecha DESC, Id DESC;
            """;
        return ReadAsync(sql, Sale, [("$start", (start ?? DateTime.Today.AddDays(-30)).Date.ToString("O", CultureInfo.InvariantCulture)), ("$end", (end ?? DateTime.Today).Date.AddDays(1).ToString("O", CultureInfo.InvariantCulture))]);
    }

    public async Task<IReadOnlyList<LocalSalesBrowserRow>> GetSalesBrowserRowsAsync(string? search = null)
    {
        if (_sqlSource is not null && !string.IsNullOrWhiteSpace(search))
        {
            try
            {
                var remote = await GetSalesBrowserRowsFromSqlAsync(search.Trim());
                if (remote.Count > 0)
                    return remote;
            }
            catch
            {
                // Si la consulta remota no esta disponible, conserva el respaldo local.
            }
        }

        var local = await GetSalesAsync();
        var normalized = search?.Trim();
        return local
            .Where(x => string.IsNullOrWhiteSpace(normalized)
                || x.Folio.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || x.Customer.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(x => new LocalSalesBrowserRow(
                x.Folio,
                string.Empty,
                x.Date,
                x.Customer,
                string.Empty,
                string.Empty,
                x.User,
                x.Folio,
                string.Empty,
                x.Folio,
                string.Empty,
                string.Empty,
                0,
                x.Subtotal,
                x.Tax,
                x.Total,
                x.PaymentStatus.Equals("Pagado", StringComparison.OrdinalIgnoreCase) ? x.Total : x.Paid,
                0m,
                0m,
                0m,
                "LOCAL"))
            .OrderByDescending(x => x.Fecha)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalSalesTicketRow>> GetSalesTicketRowsAsync(string? search = null)
    {
        var rows = await GetSalesBrowserRowsAsync(search);
        var selected = rows.FirstOrDefault();
        return selected is null ? [] : await GetSalesTicketRowsAsync(selected);
    }

    public async Task<IReadOnlyList<LocalSalesTicketRow>> GetSalesTicketRowsAsync(LocalSalesBrowserRow sale)
    {
        if (_sqlSource is not null)
        {
            try
            {
                var remote = await GetSalesTicketRowsFromSqlAsync(sale);
                if (remote.Count > 0)
                    return remote;
            }
            catch
            {
                // Si el detalle remoto no esta disponible, conserva el resumen local.
            }
        }

        return [BuildFallbackSalesTicketRow(sale)];
    }

    public Task<IReadOnlyList<LocalSaleLine>> GetSaleLinesAsync(long saleId) =>
        ReadAsync("SELECT Id,VentaId,ProductoId,Codigo,Nombre,Cantidad,PrecioUnitario,Iva,Subtotal,ImporteIva,Total FROM LocalVentaLineas WHERE VentaId=$id ORDER BY Id;", SaleLine, [("$id", saleId)]);

    public Task<IReadOnlyList<LocalPayment>> GetPaymentsAsync(string? saleFolio = null)
    {
        var sql = string.IsNullOrWhiteSpace(saleFolio)
            ? "SELECT Id,Folio,VentaFolio,Fecha,Importe,Metodo,Notas,Usuario FROM LocalPagos ORDER BY Fecha DESC, Id DESC LIMIT 500;"
            : "SELECT Id,Folio,VentaFolio,Fecha,Importe,Metodo,Notas,Usuario FROM LocalPagos WHERE VentaFolio=$folio ORDER BY Fecha DESC, Id DESC;";
        return ReadAsync(sql, Payment, string.IsNullOrWhiteSpace(saleFolio) ? [] : [("$folio", saleFolio.Trim())]);
    }

    public Task<IReadOnlyList<LocalCommission>> GetCommissionsAsync(string? folio = null)
    {
        var sql = string.IsNullOrWhiteSpace(folio)
            ? "SELECT Id,Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus FROM LocalComisiones ORDER BY Fecha DESC, Id DESC;"
            : "SELECT Id,Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus FROM LocalComisiones WHERE Folio=$folio OR VentaFolio=$folio ORDER BY Fecha DESC, Id DESC;";
        return ReadAsync(sql, Commission, string.IsNullOrWhiteSpace(folio) ? [] : [("$folio", folio.Trim())]);
    }

    public async Task<IReadOnlyList<LocalCommissionBrowserRow>> GetCommissionBrowserRowsAsync(string? search = null, DateTime? start = null, DateTime? end = null)
    {
        await EnsureCommissionSettingsLoadedAsync();
        if (_sqlSource is not null)
        {
            try
            {
                var rows = await LoadAuthoritativeCommissionRowsAsync(search, start, end);
                ApplyLargestPayoutToHighestSale(rows);
                foreach (var row in rows)
                    row.CommissionAmount = CalculateAuthoritativeCommission(row);
                ApplyStoreOnlyOperationGroupCommissions(rows);
                ApplyCommissionPayments(rows);
                return FilterCommissionBrowserRows(rows.Select(MapAuthoritativeCommissionRow), search, start, end).ToArray();
            }
            catch (Exception ex)
            {
                LogPlazaCommissionWarning(ex);
                // Plaza 28 no debe dejar la vista vacia si la lectura principal falla una vez.
                // Continua con los respaldos importados/locales para no frenar produccion.
            }
        }

        try
        {
            await using var sqlite = database.Open();
            await EnsureImportedCommissionCompatibilityAsync(sqlite);
            var hasImportedMovements = await HasTableAsync(sqlite, "mkt__dbo__mov_operacion");
            var hasImportedDejadas = await HasTableAsync(sqlite, "mkt__dbo__dejadas");
            var hasImportedApp = await HasTableAsync(sqlite, "mkt__dbo__AppMovilRegistro");
            if (hasImportedMovements || hasImportedDejadas || hasImportedApp)
            {
                var importedRows = await GetCommissionBrowserRowsFromImportedMovOperationsAsync(search, start, end);
                if (importedRows.Count > 0)
                    return importedRows;
            }
        }
        catch
        {
            // El espejo SQLite puede estar incompleto; para Comisiones debe seguir funcionando con los demas respaldos.
        }

        try
        {
            var relationRows = await GetCommissionBrowserRowsFromRelationsAsync(search, start, end);
            if (relationRows.Count > 0)
                return relationRows;
        }
        catch
        {
            // Comisiones no debe fallar por respaldos SQLite incompletos.
        }

        try
        {
            return await GetCommissionBrowserRowsFromLocalAsync(search, start, end);
        }
        catch
        {
            // Si no hay fuente de comisiones disponible, la vista queda vacia sin romper otros modulos.
            return Array.Empty<LocalCommissionBrowserRow>();
        }
    }

    public async Task<IReadOnlyList<LocalCommissionDiagnosticRow>> DiagnoseCommissionByFolioAsync(string folio)
    {
        var cleanFolio = (folio ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanFolio))
            return [];

        if (_sqlSource is not null)
        {
            try
            {
                var rows = await LoadAuthoritativeCommissionRowsAsync(cleanFolio, null, null);
            ApplyLargestPayoutToHighestSale(rows);
            foreach (var row in rows)
                row.CommissionAmount = CalculateAuthoritativeCommission(row);
            ApplyStoreOnlyOperationGroupCommissions(rows);
            return BuildCommissionDiagnostics(rows, "SQL Server");
            }
            catch (Exception ex)
            {
                LogPlazaCommissionWarning(ex);
            }
        }

        try
        {
            await using var sqlite = database.Open();
            await EnsureImportedCommissionCompatibilityAsync(sqlite);
            await using var transaction = sqlite.BeginTransaction();
            var rows = await LoadImportedAuthoritativeCommissionRowsAsync(sqlite, transaction, null, null);
            rows = rows
                .Where(row =>
                    string.Equals(row.OperationFolio, cleanFolio, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(row.Ticket, cleanFolio, StringComparison.OrdinalIgnoreCase)
                    || row.LocalFolio.Contains(cleanFolio, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ApplyLargestPayoutToHighestSale(rows);
            foreach (var row in rows)
                row.CommissionAmount = CalculateAuthoritativeCommission(row);
            ApplyStoreOnlyOperationGroupCommissions(rows);
            return BuildCommissionDiagnostics(rows, "SQLite");
        }
        catch
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<LocalCommissionIntegrityIssue>> DiagnoseCommissionIntegrityAsync(DateTime start, DateTime end)
    {
        var issues = new List<LocalCommissionIntegrityIssue>();
        if (_sqlSource is null)
            return issues;

        var from = start.Date;
        var to = end.Date <= from ? from.AddDays(1) : end.Date.AddDays(1);

        try
        {
            await using var pos = await _sqlSource.OpenPosAsync();
            await using var compu = await _sqlSource.OpenCompuadmoAsync();
            await using var joyeria = await _sqlSource.OpenJoyeriaAsync();

            await AddStoreOnlyRelationIssuesAsync(pos, compu, issues, from, to);
            await AddTequilaWithoutExpenseIssuesAsync(compu, issues, from, to);
            await AddJewelryWithoutExpenseIssuesAsync(joyeria, issues, from, to);
            await AddUnknownPaymentIssuesAsync(compu, issues, from, to);
            await AddMissingTransportPercentIssuesAsync(pos, issues);
        }
        catch (Exception ex)
        {
            issues.Add(new LocalCommissionIntegrityIssue(
                string.Empty,
                DateTime.Now,
                "Diagnostico",
                "ALTA",
                "No se pudo completar el diagnostico de integridad.",
                ex.Message,
                "Revisar conexion SQL/configuracion antes de publicar."));
        }

        return issues;
    }

    private static async Task AddStoreOnlyRelationIssuesAsync(SqlConnection pos, SqlConnection compu, List<LocalCommissionIntegrityIssue> issues, DateTime from, DateTime to)
    {
        await using var command = compu.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT TOP 100
                m.folio_remision,
                COALESCE(m.fecha, GETDATE()) AS fecha,
                LTRIM(RTRIM(COALESCE(CONVERT(nvarchar(300), m.observaciones), ''))) AS observaciones
            FROM dbo.remisioM m WITH (NOLOCK)
            WHERE m.fecha >= @from AND m.fecha < @to
              AND LTRIM(RTRIM(COALESCE(CONVERT(nvarchar(300), m.observaciones), ''))) <> ''
              AND UPPER(LTRIM(RTRIM(COALESCE(m.estatus, '')))) NOT IN ('C','CANCELADO','CANCELADA');
            """;
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string Folio, DateTime Fecha, string Observacion)>();
        while (await reader.ReadAsync())
        {
            rows.Add((
                Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToDateTime(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty));
        }

        foreach (var row in rows)
        {
            if (await HasStrongTicketRelationAsync(pos, row.Folio))
                continue;

            issues.Add(new LocalCommissionIntegrityIssue(
                row.Folio,
                row.Fecha,
                "Relacion no determinada",
                "MEDIA",
                "Venta POS valida con taxista/guia textual pero sin TaxistaId/UnidadId/GafeteId demostrable.",
                row.Observacion,
                "Guardar TaxistaId, UnidadId/GafeteId o FolioOperacion al asociar nuevas ventas."));
        }
    }

    private static async Task<bool> HasStrongTicketRelationAsync(SqlConnection pos, string ticket)
    {
        await using var command = pos.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = """
            SELECT TOP 1 1
            FROM dbo.RelacionTicketTaxista WITH (NOLOCK)
            WHERE FolioPos = @ticket
              AND (
                    TaxistaId IS NOT NULL
                 OR LTRIM(RTRIM(COALESCE(Gafete, ''))) <> ''
                 OR LTRIM(RTRIM(COALESCE(FolioOperacion, ''))) <> ''
              );
            """;
        command.Parameters.AddWithValue("@ticket", ticket);
        return await command.ExecuteScalarAsync() is not null;
    }

    private static async Task AddTequilaWithoutExpenseIssuesAsync(SqlConnection compu, List<LocalCommissionIntegrityIssue> issues, DateTime from, DateTime to)
    {
        await using var command = compu.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT TOP 100
                m.folio_remision,
                COALESCE(m.fecha, GETDATE()) AS fecha,
                CAST(m.total AS decimal(18,2)) AS total,
                MIN(CONVERT(nvarchar(300), d.descripcion_larga)) AS producto
            FROM dbo.remisioM m WITH (NOLOCK)
            INNER JOIN dbo.remisioD d WITH (NOLOCK) ON d.folio_remision = m.folio_remision
            WHERE m.fecha >= @from AND m.fecha < @to
              AND UPPER(COALESCE(CONVERT(nvarchar(max), d.descripcion_larga), '')) LIKE '%TEQUIL%'
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.egresos e WITH (NOLOCK)
                  WHERE e.folio_remision = m.folio_remision
              )
            GROUP BY m.folio_remision, m.fecha, m.total
            ORDER BY m.fecha;
            """;
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            issues.Add(new LocalCommissionIntegrityIssue(
                Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToDateTime(reader.GetValue(1), CultureInfo.InvariantCulture),
                "Tequila sin gasto",
                "MEDIA",
                "Venta con producto tequila sin egreso/gasto relacionado.",
                $"{reader.GetValue(3)} | venta {Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture):C2}",
                "Registrar egreso real de tequila cuando aplique; no inventar gasto."));
        }
    }

    private static async Task AddJewelryWithoutExpenseIssuesAsync(SqlConnection joyeria, List<LocalCommissionIntegrityIssue> issues, DateTime from, DateTime to)
    {
        await using var command = joyeria.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT TOP 100
                COALESCE(NULLIF(m.folio_pedido, ''), m.folio_factura) AS folio,
                COALESCE(m.fecha, GETDATE()) AS fecha,
                CAST(m.total AS decimal(18,2)) AS total,
                LTRIM(RTRIM(COALESCE(m.observaciones, ''))) AS observaciones
            FROM dbo.remisioM m WITH (NOLOCK)
            WHERE m.fecha >= @from AND m.fecha < @to
              AND CAST(COALESCE(m.total, 0) AS decimal(18,2)) > 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM dbo.gastos g WITH (NOLOCK)
                  WHERE g.folio_factura = COALESCE(NULLIF(m.folio_pedido, ''), m.folio_factura)
              );
            """;
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            issues.Add(new LocalCommissionIntegrityIssue(
                Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToDateTime(reader.GetValue(1), CultureInfo.InvariantCulture),
                "Joyeria sin egreso",
                "BAJA",
                "Venta de joyeria sin gasto/egreso relacionado por folio.",
                $"{Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture)} | venta {Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture):C2}",
                "Validar si la venta requiere egreso antes de pagar comision."));
        }
    }

    private static async Task AddUnknownPaymentIssuesAsync(SqlConnection compu, List<LocalCommissionIntegrityIssue> issues, DateTime from, DateTime to)
    {
        await using var command = compu.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT TOP 100
                p.folio_factura,
                COALESCE(r.fecha, GETDATE()) AS fecha,
                COALESCE(m.Nombre, '') AS forma_pago,
                CAST(p.total AS decimal(18,2)) AS total
            FROM dbo.pagosM p WITH (NOLOCK)
            INNER JOIN dbo.remisioM r WITH (NOLOCK) ON r.folio_remision = p.folio_factura
            LEFT JOIN dbo.monedas m WITH (NOLOCK) ON m.moneda = p.moneda
            WHERE r.fecha >= @from AND r.fecha < @to;
            """;
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var payment = Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty;
            if (IsKnownSafePayment(payment))
                continue;

            issues.Add(new LocalCommissionIntegrityIssue(
                Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToDateTime(reader.GetValue(1), CultureInfo.InvariantCulture),
                "Forma de pago no clasificada",
                "ALTA",
                "Forma de pago sin clasificacion explicita de retencion.",
                $"{payment} | pago {Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture):C2}",
                "Clasificar forma de pago antes de liquidar comision."));
        }
    }

    private static bool IsKnownSafePayment(string payment)
    {
        if (IsAmexPayment(payment) || IsCardPayment(payment)) return true;
        return payment.Contains("PESO", StringComparison.OrdinalIgnoreCase)
            || payment.Contains("DLS", StringComparison.OrdinalIgnoreCase)
            || payment.Contains("DOLAR", StringComparison.OrdinalIgnoreCase)
            || payment.Contains("EURO", StringComparison.OrdinalIgnoreCase)
            || payment.Contains("CANADIENSE", StringComparison.OrdinalIgnoreCase)
            || payment.Contains("LIBRA", StringComparison.OrdinalIgnoreCase)
            || payment.Contains("CORTESIA", StringComparison.OrdinalIgnoreCase)
            || payment.Contains("VALES", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AddMissingTransportPercentIssuesAsync(SqlConnection pos, List<LocalCommissionIntegrityIssue> issues)
    {
        await using var command = pos.CreateCommand();
        command.CommandTimeout = 10;
        command.CommandText = """
            SELECT TOP 100 tipo, nombre
            FROM dbo.transporte WITH (NOLOCK)
            WHERE COALESCE(comision, 0) <= 0;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            issues.Add(new LocalCommissionIntegrityIssue(
                string.Empty,
                DateTime.Today,
                "Porcentaje unidad inexistente",
                "MEDIA",
                "Transporte sin porcentaje de comision configurado; se usaria fallback general si aparece en venta.",
                $"{reader.GetValue(0)} | {reader.GetValue(1)}",
                "Configurar mkt2.transporte.comision para evitar fallback 10%."));
        }
    }

    private static void LogPlazaCommissionWarning(Exception exception)
    {
        try
        {
            var folder = System.IO.Path.Combine(AppContext.BaseDirectory, "Logs");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(folder, "plaza-commission-live.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
        }
        catch
        {
            // El log nunca debe romper la operacion principal.
        }
    }

    public async Task<bool> HasImportedCommissionSourceAsync()
    {
        if (_sqlSource is not null)
        {
            return true;
        }

        try
        {
            await using var sqlite = database.Open();
            return await HasTableAsync(sqlite, "mkt__dbo__mov_operacion")
                || await HasTableAsync(sqlite, "mkt__dbo__dejadas")
                || await HasTableAsync(sqlite, "mkt__dbo__AppMovilRegistro");
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<LocalCut>> GetCutsAsync(DateTime? day = null)
    {
        var rows = await ReadAsync("SELECT Id,Fecha,Efectivo,Tarjeta,Pagos,Gastos,Esperado,Contado,Diferencia,Estatus,Usuario,FechaCierre FROM LocalCortes ORDER BY Fecha DESC;", Cut, []);
        return day is null
            ? rows
            : rows.Where(x => x.Date.Date == day.Value.Date).ToArray();
    }

    private async Task<IReadOnlyList<LocalCommissionBrowserRow>> GetCommissionBrowserRowsFromLocalAsync(string? search, DateTime? start, DateTime? end)
    {
        const string sql = """
            SELECT
              c.Folio,
              c.VentaFolio,
              c.Fecha,
              COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(a.tipo_operacion, ''), NULLIF(d.tipotransporte, ''), '') AS Unidad,
              COALESCE(NULLIF(a.unidad, ''), NULLIF(d.unidad, ''), '') AS NumeroUnidad,
              COALESCE(NULLIF(d.nombrestaff, ''), NULLIF(c.Taxista, ''), NULLIF(a.vendedor_nombre, ''), '') AS Nombre,
              COALESCE(a.pax, d.pax, 0) AS Pax,
              COALESCE(NULLIF(a.hotel, ''), NULLIF(d.hotel, ''), '') AS Hotel,
              CAST(0 AS REAL) AS VentaArtesania,
              CAST(0 AS REAL) AS VentaFarmacia,
              CAST(0 AS REAL) AS VentaTienda,
              COALESCE(c.TotalVenta, 0) AS VentaJoyeria,
              COALESCE(c.TotalVenta, 0) AS VentaTotal,
              COALESCE(NULLIF(r.FolioPos, ''), NULLIF(a.folio_pos, ''), '') AS Ticket,
              CASE
                WHEN COALESCE(a.tarjeta, 0) > 0 AND COALESCE(a.efectivo, 0) > 0 THEN 'EFECTIVO / TARJETA'
                WHEN COALESCE(a.tarjeta, 0) > 0 THEN 'TARJETA'
                WHEN COALESCE(a.efectivo, 0) > 0 THEN 'EFECTIVO'
                ELSE ''
              END AS FormaPago,
              CAST(0 AS REAL) AS DescuentoPorcentaje,
              COALESCE(a.total, d.total, 0) AS Dejada,
              CAST(0 AS REAL) AS BebidasCajasRegalo,
              CAST(0 AS REAL) AS Reparacion,
              CAST(0 AS REAL) AS Degustacion,
              CAST(CASE
                WHEN UPPER(COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(a.tipo_operacion, ''), NULLIF(d.tipotransporte, ''), '')) LIKE '%SALMORAN%' THEN 0.20
                WHEN UPPER(COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(a.tipo_operacion, ''), NULLIF(d.tipotransporte, ''), '')) LIKE '%MAJESTIC%'
                  OR UPPER(COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(a.tipo_operacion, ''), NULLIF(d.tipotransporte, ''), '')) LIKE '%MAESTIC%'
                  OR UPPER(COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(a.tipo_operacion, ''), NULLIF(d.tipotransporte, ''), '')) LIKE '%TRAVEL EXPERIENCE%' THEN 0.08
                ELSE 0.10
              END AS REAL) AS PorcentajeComision,
              COALESCE(c.ImporteComision, 0) AS PagoComision,
              COALESCE(c.Pagado, 0) AS Pagado,
              COALESCE(c.Saldo, 0) AS Saldo,
              COALESCE(c.Estatus, '') AS Estatus,
              COALESCE(NULLIF(d.nombrevendedor, ''), '') AS Vendedor,
              COALESCE(NULLIF(a.folio_gafete, ''), NULLIF(d.gafete, ''), '') AS Gafete
            FROM LocalComisiones c
            LEFT JOIN LocalRelaciones r
              ON r.FolioOperacion = c.VentaFolio
              OR r.FolioPos = c.VentaFolio
            LEFT JOIN "mkt__dbo__AppMovilRegistro" a
              ON a.folio_app = r.FolioApp
              OR a.folio_app_original = r.FolioApp
              OR a.folio_app = c.VentaFolio
              OR a.folio_app_original = c.VentaFolio
            LEFT JOIN "mkt__dbo__dejadas" d
              ON CAST(d.folioregistro AS TEXT) = c.VentaFolio
              OR CAST(d.folioregistrostr AS TEXT) = c.VentaFolio
              OR CAST(d.codigorecepcion AS TEXT) = c.VentaFolio
              OR CAST(d.codigorecepcion AS TEXT) = a.folio_app
            ORDER BY c.Fecha DESC, c.Folio DESC;
            """;

        var rows = await ReadAsync(sql, row => new LocalCommissionBrowserRow(
            Text(row, 0),
            Text(row, 1),
            Date(row, 2),
            Text(row, 3),
            Text(row, 4),
            Text(row, 5),
            Convert.ToInt32(row.GetValue(6), CultureInfo.InvariantCulture),
            Text(row, 7),
            Decimal(row, 8),
            Decimal(row, 9),
            Decimal(row, 10),
            Decimal(row, 11),
            Decimal(row, 12),
            Text(row, 13),
            Text(row, 14),
            Decimal(row, 15),
            Decimal(row, 16),
            Decimal(row, 17),
            Decimal(row, 18),
            Decimal(row, 19),
            Decimal(row, 20),
            Decimal(row, 21),
            Decimal(row, 22),
            Decimal(row, 23),
            Text(row, 24),
            Text(row, 25),
            Text(row, 26)));

        return FilterCommissionBrowserRows(rows, search, start, end).ToArray();
    }

    private async Task<IReadOnlyList<LocalCommissionBrowserRow>> GetCommissionBrowserRowsFromImportedMovOperationsAsync(string? search, DateTime? start, DateTime? end)
    {
        await using var connection = database.Open();
        await EnsureImportedCommissionCompatibilityAsync(connection);
        await using var transaction = connection.BeginTransaction();
        var authoritativeRows = await LoadImportedAuthoritativeCommissionRowsAsync(connection, transaction, start, end);
        if (authoritativeRows.Count > 0)
        {
            ApplyLargestPayoutToHighestSale(authoritativeRows);
            foreach (var row in authoritativeRows)
                row.CommissionAmount = CalculateAuthoritativeCommission(row);
            ApplyStoreOnlyOperationGroupCommissions(authoritativeRows);
            ApplyCommissionPayments(authoritativeRows);

            var importedAuthoritative = authoritativeRows
                .Select(row => new LocalCommissionBrowserRow(
                    row.LocalFolio,
                    row.OperationFolio,
                    row.Date,
                    row.TransportType,
                    row.UnitNumber,
                    row.DriverName,
                    row.Passengers,
                    row.Hotel,
                    0m,
                    0m,
                    row.SaleStore,
                    row.SaleJewelry,
                    row.SaleTotal,
                    row.Ticket,
                    row.Payments.Description,
                    CalculateDiscountPercent(row),
                    row.GroupPayout,
                    row.Expenses.Bebidas + row.Expenses.CajasRegalo,
                    row.Expenses.Reparacion,
                    row.Expenses.Degustacion,
                    ResolveAuthoritativePercentage(row) / 100m,
                    row.CommissionAmount,
                    row.PaidAmount,
                    Math.Max(row.CommissionAmount - row.PaidAmount, 0m),
                    ResolveCommissionStatus(row.CommissionAmount, row.PaidAmount),
                    row.Vendedor,
                    row.Badge,
                    row.PayoutStatus))
                .ToArray();
            return FilterCommissionBrowserRows(importedAuthoritative, search, start, end).ToArray();
        }

        var rows = await ReadImportedMovOperationsAsync(connection, transaction, null);
        var mapped = rows
            .Select(MapImportedMovOperationCommissionRow)
            .Where(x => x.VentaTotal > 0m || x.PagoComision > 0m || x.Pagado > 0m)
            .ToArray();
        return FilterCommissionBrowserRows(mapped, search, start, end).ToArray();
    }

    private async Task<List<AuthoritativeCommissionRow>> LoadImportedAuthoritativeCommissionRowsAsync(SqliteConnection connection, SqliteTransaction transaction, DateTime? start, DateTime? end)
    {
        var result = new List<AuthoritativeCommissionRow>();
        if (!await HasTableAsync(connection, "mkt__dbo__AppMovilRegistro"))
            return result;
        if (!await HasTableAsync(connection, "compuadmo__dbo__remisioM") && !await HasTableAsync(connection, "joyeria__dbo__remisioM"))
            return result;

        var transportCatalog = await ReadImportedTransportCatalogAsync(connection, transaction);
        var appRows = await ReadImportedAuthoritativeAppRowsAsync(connection, transaction, start, end);
        foreach (var appRow in appRows)
        {
            var storeTickets = await LoadImportedStoreTicketsForKeysAsync(connection, transaction, appRow.Keys, appRow.PosFolio);
            var commissionableTickets = storeTickets
                .Where(x => IsCommissionableStoreTicket(x.Ticket))
                .ToList();
            if (commissionableTickets.Count == 0) continue;

            var ticketNumbers = commissionableTickets.Select(x => x.Ticket).ToArray();
            var payments = await LoadImportedTicketPaymentBreakdownsAsync(connection, transaction, ticketNumbers);
            var expenses = await LoadImportedTicketExpenseBreakdownsAsync(connection, transaction, ticketNumbers);
            foreach (var ticket in commissionableTickets)
            {
                payments.TryGetValue(ticket.Ticket, out var breakdown);
                expenses.TryGetValue(ticket.Ticket, out var expense);
                result.Add(new AuthoritativeCommissionRow(
                    "C-" + appRow.OperationFolio + "-" + ticket.Ticket,
                    appRow.OperationFolio,
                    ticket.Ticket,
                    appRow.Date,
                    appRow.DriverCode,
                    appRow.DriverName,
                    appRow.TransportType,
                    ticket.Total,
                    ticket.VentaTienda,
                    ticket.VentaJoyeria,
                    appRow.Hotel,
                    appRow.Passengers,
                    appRow.UnitNumber,
                    appRow.Badge,
                    appRow.Staff,
                    appRow.DriverName,
                    appRow.Payout,
                    appRow.CommissionPaidControl,
                    appRow.CommissionPaidDate,
                    breakdown ?? new TicketPaymentBreakdown(ticket.Total, 0m, 0m, "SIN PAGO"),
                    expense,
                    ResolveTransportInfo(transportCatalog, appRow.TransportType, appRow.Date))
                {
                    // El estatus de la dejada viaja aparte del de la comision.
                    PayoutStatus = appRow.PayoutStatus
                });
            }
        }

        var appFolios = appRows.Select(x => x.OperationFolio).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.AddRange(await ReadImportedAuthoritativeMovRowsAsync(connection, transaction, appFolios, transportCatalog, start, end));
        return result;
    }

    private async Task<IReadOnlyList<ImportedTransportCatalogRow>> ReadImportedTransportCatalogAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        var rows = new List<ImportedTransportCatalogRow>();

        if (await HasTableAsync(connection, "mkt__dbo__transporte"))
        {
            rows.AddRange(await ReadInTransactionAsync(connection, transaction, """
                SELECT
                  COALESCE(tipo, '') AS Tipo,
                  COALESCE(nombre, '') AS Nombre,
                  COALESCE(efectivo, 0) AS DescEfectivo,
                  COALESCE(tarjeta, 0) AS DescTarjeta,
                  COALESCE(comision, 0) AS Comision,
                  COALESCE(minimo, 0) AS Minimo,
                  COALESCE(maximo, 0) AS Maximo,
                  COALESCE(amexco, 0) AS DescAmex
                FROM "mkt__dbo__transporte"
                WHERE COALESCE(TRIM(tipo), '') <> '' OR COALESCE(TRIM(nombre), '') <> '';
                """, row => new ImportedTransportCatalogRow(
                Text(row, 0),
                Text(row, 1),
                Decimal(row, 2),
                Decimal(row, 3),
                Decimal(row, 4),
                Decimal(row, 5),
                Decimal(row, 6),
                Decimal(row, 7))));
        }

        rows.AddRange(await ReadConfiguredTransportCatalogRowsAsync());
        return rows;
    }

    private async Task<List<AuthoritativeAppRow>> ReadImportedAuthoritativeAppRowsAsync(SqliteConnection connection, SqliteTransaction transaction, DateTime? start, DateTime? end)
    {
        var hasRelations = await HasTableAsync(connection, "mkt__dbo__RelacionTicketTaxista");
        var hasDejadas = await HasTableAsync(connection, "mkt__dbo__dejadas");
        var relationJoin = hasRelations
            ? """
            LEFT JOIN "mkt__dbo__RelacionTicketTaxista" r
              ON r.FolioApp = a.folio_app OR r.FolioApp = a.folio_app_original
            """
            : string.Empty;
        var dejadaJoin = hasDejadas
            ? """
            LEFT JOIN "mkt__dbo__dejadas" d
              ON d.codigorecepcion = a.folio_app
              OR d.codigorecepcion = a.folio_app_original
              OR CAST(d.folioregistro AS TEXT) = COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), a.folio_app, '')
              OR d.folioregistrostr = COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), a.folio_app, '')
            """
            : string.Empty;

        var rows = await ReadInTransactionAsync(connection, transaction, $"""
            SELECT
              COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), a.folio_app, '') AS FolioOperacion,
              COALESCE(NULLIF(r.FolioPos, ''), NULLIF(a.folio_pos, ''), '') AS FolioPos,
              COALESCE(a.folio_app, '') AS FolioApp,
              COALESCE(a.folio_app_original, '') AS FolioAppOriginal,
              COALESCE(a.fecha_operacion, a.fecha_creacion, '') AS Fecha,
              COALESCE(NULLIF(d.nombrestaff, ''), a.vendedor_nombre, '') AS DriverName,
              COALESCE(CAST(a.id_catalogo AS TEXT), '') AS DriverCode,
              COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(a.tipo_operacion, ''), '') AS TransportType,
              COALESCE(a.total, 0) AS Payout,
              COALESCE(a.pago_comision, 0) AS CommissionPaid,
              -- CONVERT ANTES del COALESCE, a proposito: COALESCE devuelve el tipo de mayor
              -- precedencia, asi que COALESCE(datetime2, '') convertia el '' a fecha y
              -- regresaba 1900-01-01 en vez de cadena vacia. El C# leia eso como "si hay
              -- fecha de pago" y daba TODA comision por pagada.
              COALESCE(CONVERT(nvarchar(30), a.fecha_pago_comision, 120), '') AS CommissionPaidDate,
              COALESCE(a.hotel, d.hotel, '') AS Hotel,
              COALESCE(a.pax, d.pax, 0) AS Passengers,
              COALESCE(a.unidad, d.unidad, '') AS UnitNumber,
              COALESCE(a.folio_gafete, d.gafete, '') AS Badge,
              COALESCE(d.nombrestaff, '') AS Staff
            FROM "mkt__dbo__AppMovilRegistro" a
            {relationJoin}
            {dejadaJoin}
            WHERE COALESCE(a.folio_app, '') <> ''
              AND ($start IS NULL OR substr(COALESCE(a.fecha_operacion, a.fecha_creacion, ''), 1, 10) >= $start)
              AND ($end IS NULL OR substr(COALESCE(a.fecha_operacion, a.fecha_creacion, ''), 1, 10) <= $end);
            """, row => new
            {
                OperationFolio = Text(row, 0),
                PosFolio = Text(row, 1),
                FolioApp = Text(row, 2),
                FolioOriginal = Text(row, 3),
                DateText = Text(row, 4),
                DriverName = Text(row, 5),
                DriverCode = Text(row, 6),
                TransportType = Text(row, 7),
                Payout = Decimal(row, 8),
                CommissionPaid = Decimal(row, 9),
                CommissionPaidDate = Text(row, 10),
                Hotel = Text(row, 11),
                Passengers = row.IsDBNull(12) ? 0 : Convert.ToInt32(row.GetValue(12), CultureInfo.InvariantCulture),
                UnitNumber = Text(row, 13),
                Badge = Text(row, 14),
                Staff = Text(row, 15)
            },
            ("$start", start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$end", end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        var result = new List<AuthoritativeAppRow>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.OperationFolio))
                continue;
            var keys = new[] { row.OperationFolio, row.FolioApp, row.FolioOriginal }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result.Add(new AuthoritativeAppRow(
                row.OperationFolio,
                row.PosFolio,
                row.FolioApp,
                row.FolioOriginal,
                DateTime.TryParse(row.DateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : DateTime.Today,
                row.DriverName,
                row.DriverCode,
                row.TransportType,
                row.Payout,
                row.CommissionPaid,
                row.CommissionPaidDate,
                row.Hotel,
                row.Passengers,
                row.UnitNumber,
                row.Badge,
                row.Staff,
                keys,
                !string.IsNullOrWhiteSpace(row.OperationFolio) && !string.Equals(row.OperationFolio.Trim(), "0", StringComparison.OrdinalIgnoreCase)));
        }
        return result;
    }

    private async Task<List<AuthoritativeCommissionRow>> ReadImportedAuthoritativeMovRowsAsync(SqliteConnection connection, SqliteTransaction transaction, ISet<string> appFolios, IReadOnlyList<ImportedTransportCatalogRow> transportCatalog, DateTime? start, DateTime? end)
    {
        if (!await HasTableAsync(connection, "mkt__dbo__mov_operacion"))
            return [];

        var rows = await ReadInTransactionAsync(connection, transaction, """
            SELECT
              CAST(m.folioperacion AS TEXT) AS FolioOperacion,
              COALESCE(m.fecha, '') AS Fecha,
              COALESCE(m.foliosoluone, '') AS Ticket,
              COALESCE(NULLIF(m.transportetipo, ''), NULLIF(d.tipotransporte, ''), '') AS TransportType,
              COALESCE(m.totaljoyeria, 0) + COALESCE(m.totalcompra, 0) AS SaleTotal,
              COALESCE(m.totalcompra, 0) AS SaleStore,
              COALESCE(m.totaljoyeria, 0) AS SaleJewelry,
              COALESCE(m.dejada, COALESCE(d.total, 0), 0) AS Payout,
              COALESCE(m.totalefectivo, 0) AS Cash,
              COALESCE(m.totaltarjeta, 0) AS Card,
              COALESCE(m.pago, 0) AS Paid,
              COALESCE(d.idtaxi, 0) AS DriverCode,
              COALESCE(d.nombrestaff, '') AS DriverName,
              COALESCE(d.hotel, '') AS Hotel,
              COALESCE(d.pax, 0) AS Passengers,
              COALESCE(d.unidad, '') AS UnitNumber,
              COALESCE(d.gafete, '') AS Badge,
              COALESCE(d.nombrestaff, '') AS Staff
            FROM "mkt__dbo__mov_operacion" m
            LEFT JOIN "mkt__dbo__dejadas" d
              ON CAST(d.folioregistro AS TEXT) = CAST(m.folioperacion AS TEXT)
              OR d.folioregistrostr = CAST(m.folioperacion AS TEXT)
              OR d.codigorecepcion = CAST(m.folioperacion AS TEXT)
            WHERE COALESCE(m.folioperacion, '') <> ''
              AND COALESCE(m.foliosoluone, '') <> ''
              AND (COALESCE(m.totaljoyeria, 0) + COALESCE(m.totalcompra, 0)) > 0
              AND ($start IS NULL OR substr(COALESCE(m.fecha, ''), 1, 10) >= $start)
              AND ($end IS NULL OR substr(COALESCE(m.fecha, ''), 1, 10) <= $end);
            """, row => new
            {
                OperationFolio = Text(row, 0),
                DateText = Text(row, 1),
                Ticket = Text(row, 2),
                TransportType = Text(row, 3),
                SaleTotal = Decimal(row, 4),
                SaleStore = Decimal(row, 5),
                SaleJewelry = Decimal(row, 6),
                Payout = Decimal(row, 7),
                Cash = Decimal(row, 8),
                Card = Decimal(row, 9),
                Paid = Decimal(row, 10),
                DriverCode = Text(row, 11),
                DriverName = Text(row, 12),
                Hotel = Text(row, 13),
                Passengers = row.IsDBNull(14) ? 0 : Convert.ToInt32(row.GetValue(14), CultureInfo.InvariantCulture),
                UnitNumber = Text(row, 15),
                Badge = Text(row, 16),
                Staff = Text(row, 17)
            },
            ("$start", start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$end", end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        var result = new List<AuthoritativeCommissionRow>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.OperationFolio) || appFolios.Contains(row.OperationFolio))
                continue;
            result.Add(new AuthoritativeCommissionRow(
                "C-" + row.OperationFolio + "-" + row.Ticket,
                row.OperationFolio,
                row.Ticket,
                DateTime.TryParse(row.DateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : DateTime.Today,
                row.DriverCode,
                row.DriverName,
                row.TransportType,
                row.SaleTotal,
                row.SaleStore,
                row.SaleJewelry,
                row.Hotel,
                row.Passengers,
                row.UnitNumber,
                row.Badge,
                row.Staff,
                row.DriverName,
                row.Payout,
                row.Paid,
                string.Empty,
                new TicketPaymentBreakdown(row.Cash, row.Card, 0m, row.Card > 0m ? "TARJETA" : "PESOS"),
                new StoreExpenseBreakdown(),
                ResolveTransportInfo(transportCatalog, row.TransportType, DateTime.TryParse(row.DateText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var movDate) ? movDate : DateTime.Today)));
        }
        return result;
    }

    private async Task<List<StoreTicketRow>> LoadImportedStoreTicketsForKeysAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> keys, string? exactTicket)
    {
        var result = new List<StoreTicketRow>();
        result.AddRange(await ReadImportedStoreTicketsAsync(connection, transaction, "compuadmo__dbo__remisioM", "folioregistro", "folio_remision", keys, exactTicket, false));
        result.AddRange(await ReadImportedStoreTicketsAsync(connection, transaction, "joyeria__dbo__remisioM", "folio_registro", "folio_factura", keys, exactTicket, true));
        return result
            .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StoreTicketRow(g.Key, g.Sum(x => x.Total), g.Sum(x => x.VentaTienda), g.Sum(x => x.VentaJoyeria)))
            .ToList();
    }

    private async Task<List<StoreTicketRow>> ReadImportedStoreTicketsAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string folioColumn, string ticketColumn, IReadOnlyList<string> keys, string? exactTicket, bool joyeria)
    {
        if (!await HasTableAsync(connection, table) || (keys.Count == 0 && string.IsNullOrWhiteSpace(exactTicket)))
            return [];

        var whereParts = new List<string>();
        var parameters = new List<(string Name, object? Value)>();
        if (keys.Count > 0)
        {
            var keyParameters = new List<string>();
            for (var i = 0; i < keys.Count; i++)
            {
                var parameter = "$key" + i.ToString(CultureInfo.InvariantCulture);
                keyParameters.Add(parameter);
                parameters.Add((parameter, keys[i]));
            }
            whereParts.Add($"CAST({folioColumn} AS TEXT) IN ({string.Join(",", keyParameters)})");

            var observationParameters = new List<string>();
            for (var i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var parameter = "$obsKey" + i.ToString(CultureInfo.InvariantCulture);
                observationParameters.Add($"UPPER(COALESCE(CAST(observaciones AS TEXT), '')) LIKE UPPER({parameter})");
                parameters.Add((parameter, "%" + key.Trim() + "%"));
            }

            if (observationParameters.Count > 0)
                whereParts.Add("(" + string.Join(" OR ", observationParameters) + ")");
        }
        if (!string.IsNullOrWhiteSpace(exactTicket))
        {
            parameters.Add(("$ticket", exactTicket));
            whereParts.Add($"CAST({ticketColumn} AS TEXT) = $ticket");
        }

        var statusFilter = await HasColumnAsync(connection, table, "estatus")
            ? "AND UPPER(TRIM(COALESCE(estatus, ''))) NOT IN ('C', 'CANCELADO', 'CANCELADA')"
            : string.Empty;

        var rows = await ReadInTransactionAsync(connection, transaction, $"""
            SELECT CAST({ticketColumn} AS TEXT) AS Ticket, COALESCE(total, 0) AS Total
            FROM "{table}"
            WHERE ({string.Join(" OR ", whereParts)})
              {statusFilter};
            """, row => new StoreTicketRow(
            Text(row, 0),
            Decimal(row, 1),
            joyeria ? 0m : Decimal(row, 1),
            joyeria ? Decimal(row, 1) : 0m), parameters.ToArray());
        return rows.Where(x => !string.IsNullOrWhiteSpace(x.Ticket)).ToList();
    }


    private async Task<Dictionary<string, TicketPaymentBreakdown>> LoadImportedTicketPaymentBreakdownsAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> tickets)
    {
        var result = new Dictionary<string, TicketPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in await ReadImportedTicketPaymentsAsync(connection, transaction, "compuadmo__dbo__pagosM", "compuadmo__dbo__monedas", tickets))
            result[pair.Key] = pair.Value;
        foreach (var pair in await ReadImportedTicketPaymentsAsync(connection, transaction, "joyeria__dbo__pagosM", "joyeria__dbo__Monedas", tickets))
            result[pair.Key] = pair.Value;
        return result;
    }

    private async Task<Dictionary<string, TicketPaymentBreakdown>> ReadImportedTicketPaymentsAsync(SqliteConnection connection, SqliteTransaction transaction, string paymentsTable, string currenciesTable, IReadOnlyList<string> tickets)
    {
        if (tickets.Count == 0 || !await HasTableAsync(connection, paymentsTable))
            return new Dictionary<string, TicketPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);

        var parameters = new List<(string Name, object? Value)>();
        var ticketParameters = new List<string>();
        for (var i = 0; i < tickets.Count; i++)
        {
            var parameter = "$ticket" + i.ToString(CultureInfo.InvariantCulture);
            ticketParameters.Add(parameter);
            parameters.Add((parameter, tickets[i]));
        }

        var currencyJoin = await HasTableAsync(connection, currenciesTable)
            ? $"""LEFT JOIN "{currenciesTable}" m ON CAST(m.moneda AS TEXT) = CAST(p.moneda AS TEXT)"""
            : string.Empty;

        var rows = await ReadInTransactionAsync(connection, transaction, $"""
            SELECT CAST(p.folio_factura AS TEXT) AS Ticket, COALESCE(m.Nombre, '') AS PaymentName, COALESCE(p.total, 0) AS Total
            FROM "{paymentsTable}" p
            {currencyJoin}
            WHERE CAST(p.folio_factura AS TEXT) IN ({string.Join(",", ticketParameters)});
            """, row => (Ticket: Text(row, 0), PaymentName: Text(row, 1), Total: Decimal(row, 2)), parameters.ToArray());

        return rows
            .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var nonCard = 0m;
                    var card = 0m;
                    var amex = 0m;
                    var descriptions = new List<string>();
                    foreach (var payment in g)
                    {
                        descriptions.Add($"{payment.PaymentName} {payment.Total:C2}");
                        if (IsAmexPayment(payment.PaymentName)) amex += payment.Total;
                        else if (IsCardPayment(payment.PaymentName)) card += payment.Total;
                        else nonCard += payment.Total;
                    }
                    return new TicketPaymentBreakdown(nonCard, card, amex, string.Join(" / ", descriptions.Distinct(StringComparer.OrdinalIgnoreCase)));
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, StoreExpenseBreakdown>> LoadImportedTicketExpenseBreakdownsAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<string> tickets)
    {
        var result = new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in await ReadImportedTicketExpensesAsync(connection, transaction, "compuadmo__dbo__egresos", "folio_remision", tickets))
            result[pair.Key] = pair.Value;
        foreach (var pair in await ReadImportedTicketExpensesAsync(connection, transaction, "joyeria__dbo__gastos", "folio_factura", tickets))
            result[pair.Key] = pair.Value;
        return result;
    }

    private async Task<Dictionary<string, StoreExpenseBreakdown>> ReadImportedTicketExpensesAsync(SqliteConnection connection, SqliteTransaction transaction, string table, string ticketColumn, IReadOnlyList<string> tickets)
    {
        if (tickets.Count == 0 || !await HasTableAsync(connection, table))
            return new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);

        var parameters = new List<(string Name, object? Value)>();
        var ticketParameters = new List<string>();
        for (var i = 0; i < tickets.Count; i++)
        {
            var parameter = "$ticketExpense" + i.ToString(CultureInfo.InvariantCulture);
            ticketParameters.Add(parameter);
            parameters.Add((parameter, tickets[i]));
        }

        var rows = await ReadInTransactionAsync(connection, transaction, $"""
            SELECT CAST({ticketColumn} AS TEXT) AS Ticket, UPPER(COALESCE(referencia, '')) AS Referencia, UPPER(COALESCE(concepto, '')) AS Concepto, COALESCE(total, 0) AS Total
            FROM "{table}"
            WHERE CAST({ticketColumn} AS TEXT) IN ({string.Join(",", ticketParameters)});
            """, row => (Ticket: Text(row, 0), Referencia: Text(row, 1), Concepto: Text(row, 2), Total: Decimal(row, 3)), parameters.ToArray());

        var result = new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var text = (row.Referencia + " " + row.Concepto).ToUpperInvariant();
            result.TryGetValue(row.Ticket, out var current);
            result[row.Ticket] = current with
            {
                Dejada = current.Dejada + (text.Contains("DEJADA", StringComparison.OrdinalIgnoreCase) ? row.Total : 0m),
                GastosVarios = current.GastosVarios + ((text.Contains("GASTOS VARIOS", StringComparison.OrdinalIgnoreCase) || text.EndsWith(" GV", StringComparison.OrdinalIgnoreCase) || text.Contains(" GV ", StringComparison.OrdinalIgnoreCase)) ? row.Total : 0m),
                Degustacion = current.Degustacion + (text.Contains("DEGUST", StringComparison.OrdinalIgnoreCase) ? row.Total : 0m),
                Reparacion = current.Reparacion + (text.Contains("REPARA", StringComparison.OrdinalIgnoreCase) ? row.Total : 0m),
                Bebidas = current.Bebidas + (LooksLikeBeverage(text) ? row.Total : 0m),
                CajasRegalo = current.CajasRegalo + ((text.Contains("CAJA", StringComparison.OrdinalIgnoreCase) || text.Contains("REGALO", StringComparison.OrdinalIgnoreCase)) ? row.Total : 0m)
            };
        }

        return result;
    }

    private async Task<IReadOnlyList<LocalSalesBrowserRow>> GetSalesBrowserRowsFromSqlAsync(string search)
    {
        if (_sqlSource is null) return [];
        var normalized = search.Trim();
        await using var compu = await _sqlSource.OpenCompuadmoAsync();
        await using var joy = await _sqlSource.OpenJoyeriaAsync();
        var result = new List<LocalSalesBrowserRow>();
        var tokens = SplitSearchTokens(normalized);
        if (tokens.Count == 0)
            tokens.Add(normalized);

        foreach (var token in tokens)
        {
            var numeric = token.StartsWith("M", StringComparison.OrdinalIgnoreCase) && token.Length > 1
                ? token[1..]
                : token;
            var folioNumber = long.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (long?)null;
            var prefixedTicket = folioNumber.HasValue
                ? "M" + folioNumber.Value.ToString("D8", CultureInfo.InvariantCulture)
                : token;

            var compuTask = ReadSalesFromStoreAsync(compu, false, token, numeric, prefixedTicket, folioNumber);
            var joyTask = ReadSalesFromStoreAsync(joy, true, token, numeric, prefixedTicket, folioNumber);
            await Task.WhenAll(compuTask, joyTask);
            result.AddRange(compuTask.Result);
            result.AddRange(joyTask.Result);
        }

        return result
            .GroupBy(x => $"{x.OrigenVenta}|{x.FolioRegistro}|{x.Folio}|{x.Factura}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Fecha).First())
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Folio, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalSalesTicketRow>> GetSalesTicketRowsFromSqlAsync(LocalSalesBrowserRow sale)
    {
        if (_sqlSource is null) return [];

        await using var compu = await _sqlSource.OpenCompuadmoAsync();
        await using var joy = await _sqlSource.OpenJoyeriaAsync();
        var keys = SplitSearchTokens(string.Join(",",
            new[]
            {
                sale.FolioRegistro,
                sale.Folio,
                sale.Factura,
                sale.FolioControl,
                sale.FolioApp
            }.Where(x => !string.IsNullOrWhiteSpace(x))));
        if (keys.Count == 0)
            return [];

        var compuTask = ReadSalesTicketRowsFromStoreAsync(compu, sale, false, keys);
        var joyTask = ReadSalesTicketRowsFromStoreAsync(joy, sale, true, keys);
        await Task.WhenAll(compuTask, joyTask);
        var rows = new List<LocalSalesTicketRow>();
        rows.AddRange(compuTask.Result);
        rows.AddRange(joyTask.Result);
        return rows
            .GroupBy(x => $"{x.Referencia}|{x.Producto}|{x.Importe}|{x.Departamento}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalTaxiReportRow>> GetTaxiReportAsync(DateTime? start = null, DateTime? end = null)
    {
        await using var connection = database.Open();
        if (!await HasTableAsync(connection, "mkt__dbo__mov_operacion")) return [];
        await using var transaction = connection.BeginTransaction();
        var rows = await ReadImportedMovOperationsAsync(connection, transaction, null);
        if (start.HasValue || end.HasValue)
        {
            var from = start?.Date ?? DateTime.MinValue;
            var to = (end?.Date ?? DateTime.MaxValue.Date).AddDays(1);
            rows = rows.Where(x => x.Date >= from && x.Date < to).ToArray();
        }
        return rows.Select(x => new LocalTaxiReportRow(
            x.TransportType,
            x.Folio,
            x.TransportType,
            x.Date,
            0,
            0,
            0,
            string.Empty,
            x.TotalDay,
            x.Cash,
            x.Card,
            0m,
            x.LeftAmount,
            x.Expenses,
            x.TotalDay,
            CalculateWebCommission(x),
            string.Empty,
            ResolveCommissionStatus(CalculateWebCommission(x), x.Payment))).ToList();
    }

    public async Task<IReadOnlyList<LocalSpecialReportRow>> GetSpecialReportAsync(DateTime? start = null, DateTime? end = null)
    {
        await using var connection = database.Open();
        if (!await HasTableAsync(connection, "mkt__dbo__mov_operacion")) return [];
        await using var transaction = connection.BeginTransaction();
        var rows = await ReadImportedMovOperationsAsync(connection, transaction, null);
        var from = start?.Date ?? DateTime.MinValue;
        var to = (end?.Date ?? DateTime.MaxValue.Date).AddDays(1);
        return rows.Where(x => x.Date >= from && x.Date < to)
            .Select(x =>
            {
                var commission = CalculateWebCommission(x);
                return new LocalSpecialReportRow(
                    x.Date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                    string.Empty,
                    string.Empty,
                    x.Folio,
                    x.Ticket,
                    x.TransportType,
                    string.Empty,
                    x.TransportType,
                    string.Empty,
                    0,
                    x.TotalDay,
                    x.LeftAmount,
                    x.LeftAmount > 0m && x.Payment > 0m ? x.LeftAmount : 0m,
                    commission,
                    x.Payment,
                    ResolveCommissionStatus(commission, x.Payment));
            }).ToList();
    }

    public async Task<IReadOnlyList<DesktopWorkbookSheet>> GetCuadreFinalWorkbookSheetsAsync(DateTime? start = null, DateTime? end = null)
    {
        var dejadas = await GetSpecialReportAsync(start, end);
        var taxi = await GetTaxiReportAsync(start, end);
        var comisiones = await GetCommissionsAsync();
        var cortes = (await GetCutsAsync())
            .Where(x => (!start.HasValue || x.Date.Date >= start.Value.Date)
                && (!end.HasValue || x.Date.Date < end.Value.Date.AddDays(1)))
            .Select(x => new LocalCuadreCorteRow(x.Date, x.Cash, x.Card, x.Payments, x.Expenses, x.Expected, x.Counted, x.Difference, x.Status))
            .ToList();

        var camiones = await GetCamionesResumenAsync(start, end);
        var resumen = new List<LocalCuadreResumenRow>
        {
            new("TOTAL GENERAL", dejadas.Sum(x => x.Passengers), dejadas.Count, dejadas.Count(x => x.Paid > 0m), 0, dejadas.Sum(x => x.Payout), dejadas.Sum(x => x.Sale), dejadas.Sum(x => x.Commission), taxi.Sum(x => x.Expenses), dejadas.Sum(x => x.Sale) - taxi.Sum(x => x.Expenses) - dejadas.Sum(x => x.Commission))
        };
        resumen.AddRange(camiones);

        return
        [
            DesktopWorkbookSheet.Create("CUADRE", resumen),
            DesktopWorkbookSheet.Create("CUADRE dejadas", dejadas),
            DesktopWorkbookSheet.Create("REPORTE HOTELES", dejadas.GroupBy(x => string.IsNullOrWhiteSpace(x.Hotel) ? "SIN HOTEL" : x.Hotel).Select(g => new LocalCuadreResumenRow(g.Key, g.Sum(x => x.Passengers), g.Count(), g.Count(x => x.Paid > 0m), 0, g.Sum(x => x.Payout), g.Sum(x => x.Sale), g.Sum(x => x.Commission), 0m, g.Sum(x => x.Sale) - g.Sum(x => x.Commission))).ToList()),
            DesktopWorkbookSheet.Create("comisiones", comisiones),
            DesktopWorkbookSheet.Create("CORTE FINAL", cortes)
        ];
    }

    public async Task<IReadOnlyList<LocalCuadreResumenRow>> GetCamionesResumenAsync(DateTime? start = null, DateTime? end = null)
    {
        // -----------------------------------------------------------------------
        // FUENTE OFICIAL PARA CAMIONES EN EL CUADRE DE PLAZA 28
        // -----------------------------------------------------------------------
        // Fuente: base configurada.dbo.registroscamiones → id_chofer → base configurada.dbo.choferes → empresa
        //
        // La empresa del chofer (choferes.empresa) determina la categoría:
        //   'AUTOCAR'     → ACAR  en el cuadre
        //   'MAYA CARIBE' → MC    en el cuadre
        //   'TURICUN'     → TUR   en el cuadre
        //
        // ⚠️ NO usar dejadas.tipotransporte para AUTOCAR/MAYA CARIBE/TURICUN.
        // Desde la migración de junio 2026, dejadas.tipotransporte ya no almacena
        // esos valores — todos los registros de camiones viven en registroscamiones
        // y la empresa de cada chofer está en choferes.empresa.
        //
        // SQL Server es la fuente principal, pero se completa con SQLite si una
        // empresa de camiones no viene en la respuesta remota y si la SQLite
        // sincronizada si tiene esa empresa para el mismo rango. Esto evita que
        // AUTOCAR/MAYA/TURICUN se "pierdan" por una base remota incompleta, sin
        // volver a clasificar desde dejadas.tipotransporte.
        // -----------------------------------------------------------------------
        if (_sqlSource is not null)
        {
            try
            {
                var sqlRows = await GetCamionesResumenFromSqlServerAsync(_sqlSource, start, end);
                return await CompleteCamionesResumenWithSqliteAsync(sqlRows, start, end);
            }
            catch
            {
                // SQL Server no respondió. Cae al fallback SQLite solo en este caso.
            }
        }

        // Fallback SQLite: misma fuente logica que SQL Server, espejo local.
        return await GetCamionesResumenFromSqliteAsync(start, end);
    }

    private async Task<IReadOnlyList<LocalCuadreResumenRow>> CompleteCamionesResumenWithSqliteAsync(
        IReadOnlyList<LocalCuadreResumenRow> sqlRows, DateTime? start, DateTime? end)
    {
        var sqliteRows = await GetCamionesResumenFromSqliteAsync(start, end);
        if (sqliteRows.Count == 0)
            return sqlRows;

        var merged = sqlRows
            .ToDictionary(x => x.Concepto.Trim(), x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var sqliteRow in sqliteRows)
        {
            var key = sqliteRow.Concepto.Trim();
            if (merged.ContainsKey(key))
                continue;

            if (sqliteRow.Pax == 0
                && sqliteRow.Entraron == 0
                && sqliteRow.Salieron == 0
                && sqliteRow.Unidades == 0
                && sqliteRow.Dejada == 0m)
            {
                continue;
            }

            merged[key] = sqliteRow;
        }

        return merged.Values
            .OrderByDescending(x => x.Pax)
            .ThenBy(x => x.Concepto, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalCuadreResumenRow>> GetCamionesResumenFromSqliteAsync(DateTime? start, DateTime? end)
    {
        await using var connection = database.Open();

        if (await HasTableAsync(connection, "mkt__dbo__registroscamiones")
            && await HasTableAsync(connection, "mkt__dbo__choferes"))
        {
            var from2 = start?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to2   = end?.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            const string sqlCamiones = """
                SELECT
                    UPPER(TRIM(COALESCE(ch.empresa, '')))     AS Empresa,
                    COALESCE(SUM(r.pax_valido), 0)            AS Pax,
                    MAX(0, COALESCE(SUM(r.pax_valido), 0) - COALESCE(SUM(CASE WHEN r.pax_valido > 0 THEN 1 ELSE 0 END), 0)) AS Entraron,
                    COALESCE(SUM(CASE WHEN r.pax_valido > 0 THEN 1 ELSE 0 END), 0) AS Salieron,
                    COUNT(DISTINCT r.camion)                   AS Unidades,
                    COALESCE(SUM(r.comision), 0)              AS Dejada
                FROM "mkt__dbo__registroscamiones" r
                INNER JOIN "mkt__dbo__choferes" ch
                    ON r.id_chofer = ch.Id_chofer
                WHERE ($start IS NULL OR r.fecha >= $start)
                  AND ($end   IS NULL OR r.fecha <  $end)
                  AND UPPER(TRIM(COALESCE(ch.empresa, ''))) IN ('AUTOCAR','MAYA CARIBE','TURICUN')
                GROUP BY UPPER(TRIM(COALESCE(ch.empresa, '')))
                ORDER BY Pax DESC;
                """;
            var sqliteRows = await ReadAsync(sqlCamiones, row => new LocalCuadreResumenRow(
                row.GetString(0),
                Convert.ToInt32(row.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToInt32(row.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToInt32(row.GetValue(3), CultureInfo.InvariantCulture),
                Convert.ToInt32(row.GetValue(4), CultureInfo.InvariantCulture),
                Decimal(row, 5),
                0m, 0m, 0m,
                Decimal(row, 5)), ("$start", from2), ("$end", to2));
            return sqliteRows;
        }

        // Sin espejo de registroscamiones/choferes no hay fuente confiable para AUTOCAR/MAYA/TURICUN.
        return [];
    }

    private static async Task<IReadOnlyList<LocalCuadreResumenRow>> GetCamionesResumenFromSqlServerAsync(
        LocalSqlServerSource sqlSource, DateTime? start, DateTime? end)
    {
        await using var connection = await sqlSource.OpenPosAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        // -----------------------------------------------------------------------
        // FUENTE DEFINITIVA DE CAMIONES PARA EL CUADRE DE PLAZA 28
        // -----------------------------------------------------------------------
        // Fuente: base configurada.dbo.registroscamiones + base configurada.dbo.choferes
        //
        // La empresa que determina la categoría es choferes.empresa:
        //   - 'AUTOCAR'     → ACAR en el cuadre
        //   - 'MAYA CARIBE' → MC   en el cuadre
        //   - 'TURICUN'     → TUR  en el cuadre
        //
        // NO usar dejadas.tipotransporte para este propósito.
        // Desde la migración de junio 2026, dejadas ya no almacena los tipos
        // AUTOCAR/MAYA CARIBE; todos los registros de camiones viven en
        // registroscamiones y la empresa de cada chofer está en choferes.
        //
        // Fórmulas:
        //   PAX      = SUM(pax_valido)          -- pasajeros válidos del registro de camiones
        //   SALIERON = COUNT(registros con pax_valido > 0)
        //   ENTRARON = PAX - SALIERON           -- forma usada por el corte final Plaza 28
        //   UNIDADES = COUNT(DISTINCT camion)   -- número de unidades distintas
        //   DEJADA   = SUM(comision)            -- comisión pagada al chofer (= dejada)
        // -----------------------------------------------------------------------
        command.CommandText = $"""
            SELECT
                UPPER(LTRIM(RTRIM(ch.empresa)))             AS Empresa,
                COALESCE(SUM(r.pax_valido), 0)              AS Pax,
                CASE
                    WHEN COALESCE(SUM(r.pax_valido), 0) - COALESCE(SUM(CASE WHEN r.pax_valido > 0 THEN 1 ELSE 0 END), 0) < 0 THEN 0
                    ELSE COALESCE(SUM(r.pax_valido), 0) - COALESCE(SUM(CASE WHEN r.pax_valido > 0 THEN 1 ELSE 0 END), 0)
                END                                         AS Entraron,
                COALESCE(SUM(CASE WHEN r.pax_valido > 0 THEN 1 ELSE 0 END), 0) AS Salieron,
                COUNT(DISTINCT r.camion)                     AS Unidades,
                COALESCE(SUM(r.comision), 0)                 AS Dejada
            FROM {sqlSource.PosTable("registroscamiones")} r
            INNER JOIN {sqlSource.PosTable("choferes")} ch
                ON r.id_chofer = ch.Id_chofer
            WHERE (@start IS NULL OR r.fecha >= @start)
              AND (@end   IS NULL OR r.fecha <  @end)
              AND UPPER(LTRIM(RTRIM(COALESCE(ch.empresa, '')))) IN (
                    'AUTOCAR', 'MAYA CARIBE', 'TURICUN'
              )
            GROUP BY UPPER(LTRIM(RTRIM(ch.empresa)))
            ORDER BY Pax DESC;
            """;
        command.Parameters.AddWithValue("@start", start?.Date ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@end", end?.Date.AddDays(1) ?? (object)DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<LocalCuadreResumenRow>();
        while (await reader.ReadAsync())
        {
            result.Add(new LocalCuadreResumenRow(
                reader.GetString(0),
                Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                0m,
                0m,
                0m,
                Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    public async Task<string> BuildDejadaTicketTextAsync(string folio, string user)
    {
        var lines = await GetDejadaTicketLinesAsync(folio, user);
        static string Value(IReadOnlyList<LocalTicketLine> rows, string campo) => rows.FirstOrDefault(x => x.Campo == campo)?.Valor ?? string.Empty;
        const int width = 42;
        static string Line(char value = '-') => new(value, width);
        static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        static string Center(string value)
        {
            value = Clean(value);
            if (value.Length >= width) return value[..width];
            var left = (width - value.Length) / 2;
            return new string(' ', left) + value;
        }
        static string Pair(string label, string value)
        {
            label = Clean(label).ToUpperInvariant();
            value = Clean(value);
            var prefix = $"{label}: ";
            var maxValue = Math.Max(1, width - prefix.Length);
            return prefix + (value.Length > maxValue ? value[..maxValue] : value);
        }

        return string.Join(Environment.NewLine, new[]
        {
            Center("CONTROL TAXI"),
            Center("PAGO DE DEJADA"),
            Line(),
            Pair("Ticket", Value(lines, "Ticket")),
            Pair("Folio", Value(lines, "Folio")),
            Pair("Folio app", Value(lines, "Folio app")),
            Pair("Operacion", Value(lines, "Operacion")),
            Pair("Fecha viaje", Value(lines, "Fecha viaje")),
            Pair("Fecha pago", Value(lines, "Fecha pago")),
            Line(),
            Pair("Taxista", Value(lines, "Taxista")),
            Pair("Vendedor", Value(lines, "Vendedor")),
            Pair("Gafete", Value(lines, "Gafete")),
            Pair("Unidad", Value(lines, "Unidad")),
            Pair("Placas", Value(lines, "Placas")),
            Pair("Telefono", Value(lines, "Telefono")),
            Pair("Nacionalidad", Value(lines, "Nacionalidad")),
            Pair("Transporte", Value(lines, "Transporte")),
            Pair("Hotel", Value(lines, "Hotel")),
            Pair("Destino", Value(lines, "Destino")),
            Pair("Pax", Value(lines, "Pax")),
            Line(),
            Pair("Importe", Value(lines, "Importe")),
            Pair("Usuario", user),
            Pair("Estatus", Value(lines, "Estatus")),
            Line(),
            Center("CONSERVE ESTE COMPROBANTE"),
            "________________________",
            Center("FIRMA DEL TAXISTA")
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public async Task<IReadOnlyList<LocalTicketLine>> GetDejadaTicketLinesAsync(string folio, string user)
    {
        await using var connection = database.Open();
        var value = Require(folio, "El folio");
        if (!await HasTableAsync(connection, "mkt__dbo__dejadas")) throw new InvalidOperationException("No existe la tabla importada mkt.dbo.dejadas para generar el ticket.");
        const string sql = """
            SELECT
              CAST(COALESCE(d.folioregistrostr, d.folioregistro, '') AS TEXT) AS Folio,
              CAST(COALESCE(a.folio_app, d.codigorecepcion, '') AS TEXT) AS FolioApp,
              CAST(COALESCE(r.FolioOperacion, '') AS TEXT) AS Operacion,
              CAST(COALESCE(r.FolioPos, a.folio_pos, '') AS TEXT) AS Ticket,
              COALESCE(d.fecha, '') AS FechaViaje,
              COALESCE(a.fecha_pago_dejada, '') AS FechaPago,
              COALESCE(d.nombrestaff, '') AS Taxista,
              COALESCE(d.nombrevendedor, '') AS Vendedor,
              CAST(COALESCE(d.gafete, '') AS TEXT) AS Gafete,
              COALESCE(d.unidad, '') AS Unidad,
              COALESCE(a.placas, '') AS Placas,
              COALESCE(d.telefono, a.telefono_taxista, a.telefono_contacto, '') AS Telefono,
              COALESCE(a.nacionalidad, '') AS Nacionalidad,
              COALESCE(d.tipotransporte, a.tipo_operacion, '') AS Transporte,
              COALESCE(d.hotel, a.hotel, '') AS Hotel,
              COALESCE(a.destino, '') AS Destino,
              COALESCE(d.pax, a.pax, 0) AS Pax,
              COALESCE(d.total, a.total, 0) AS Importe,
              COALESCE(a.estado_pago_dejada, CASE WHEN COALESCE(d.pago, 0) > 0 THEN 'pagado' ELSE 'pendiente' END) AS Estatus
            FROM "mkt__dbo__dejadas" d
            LEFT JOIN "mkt__dbo__AppMovilRegistro" a
              ON a.folio_app = d.codigorecepcion
              OR a.folio_app_original = d.codigorecepcion
              OR a.folio_app = d.folioregistrostr
              OR a.folio_app_original = d.folioregistrostr
              OR CAST(a.folio_app AS TEXT) = CAST(d.folioregistro AS TEXT)
              OR CAST(a.folio_app_original AS TEXT) = CAST(d.folioregistro AS TEXT)
            LEFT JOIN "mkt__dbo__RelacionTicketTaxista" r
              ON r.FolioApp = a.folio_app
              OR r.FolioApp = a.folio_app_original
              OR r.FolioApp = d.codigorecepcion
              OR r.FolioApp = d.folioregistrostr
            WHERE CAST(d.folioregistro AS TEXT) = $folio
               OR CAST(d.folioregistrostr AS TEXT) = $folio
               OR CAST(d.codigorecepcion AS TEXT) = $folio
               OR CAST(a.folio_app AS TEXT) = $folio
               OR CAST(a.folio_app_original AS TEXT) = $folio
               OR CAST(r.FolioOperacion AS TEXT) = $folio
               OR CAST(r.FolioPos AS TEXT) = $folio
            ORDER BY d.fecha DESC
            LIMIT 1;
            """;
        var rows = await ReadAsync(sql, row =>
        {
            var importe = Decimal(row, 17).ToString("C2", CultureInfo.CurrentCulture);
            return new[]
            {
                new LocalTicketLine("Ticket", row.GetString(3)),
                new LocalTicketLine("Folio", row.GetString(0)),
                new LocalTicketLine("Folio app", row.GetString(1)),
                new LocalTicketLine("Operacion", row.GetString(2)),
                new LocalTicketLine("Fecha viaje", row.GetString(4)),
                new LocalTicketLine("Fecha pago", string.IsNullOrWhiteSpace(row.GetString(5)) ? DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : row.GetString(5)),
                new LocalTicketLine("Taxista", row.GetString(6)),
                new LocalTicketLine("Vendedor", row.GetString(7)),
                new LocalTicketLine("Gafete", row.GetString(8)),
                new LocalTicketLine("Unidad", row.GetString(9)),
                new LocalTicketLine("Placas", row.GetString(10)),
                new LocalTicketLine("Telefono", row.GetString(11)),
                new LocalTicketLine("Nacionalidad", row.GetString(12)),
                new LocalTicketLine("Transporte", row.GetString(13)),
                new LocalTicketLine("Hotel", row.GetString(14)),
                new LocalTicketLine("Destino", row.GetString(15)),
                new LocalTicketLine("Pax", Convert.ToString(row.GetValue(16), CultureInfo.InvariantCulture) ?? "0"),
                new LocalTicketLine("Importe", importe),
                new LocalTicketLine("Usuario", user),
                new LocalTicketLine("Estatus", row.GetString(18))
            };
        }, ("$folio", value));
        return rows.FirstOrDefault() ?? throw new InvalidOperationException("No se encontrÃ³ la dejada/ticket con el folio indicado.");
    }

    public async Task<IReadOnlyList<LocalBadgeMovement>> GetImportedBadgeMovementsAsync(DateTime? start = null, DateTime? end = null)
    {
        await using var connection = database.Open();
        if (!await HasTableAsync(connection, "mkt__dbo__gafete")) return [];
        var from = start?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = end?.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        const string sql = """
            SELECT
              CAST(COALESCE(g.gafete, '') AS TEXT),
              CAST(COALESCE(g.matricula, '') AS TEXT),
              CAST(COALESCE(g.folioperacion, '') AS TEXT),
              COALESCE(g.fecha, ''),
              CASE
                WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 'OCUPADO'
                WHEN UPPER(COALESCE(g.venta, '')) = 'S' THEN 'SUSPENDIDO'
                WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN 'LIBRE'
                ELSE COALESCE(g.venta, '')
              END,
              CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN COALESCE(g.hora, '') ELSE '' END,
              '', '', ''
            FROM "mkt__dbo__gafete" g
            WHERE ($start IS NULL OR COALESCE(g.fecha, g.hora, '') >= $start)
              AND ($end IS NULL OR COALESCE(g.fecha, g.hora, '') < $end)
            ORDER BY COALESCE(g.hora, g.fecha, '') DESC, g.gafete DESC
            LIMIT 500;
            """;
        return await ReadAsync(sql, row => new LocalBadgeMovement(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5), row.GetString(6), row.GetString(7), row.GetString(8)), ("$start", from), ("$end", to));
    }

    public Task<IReadOnlyList<LocalAuditEntry>> GetAuditAsync(DateTime? start = null, DateTime? end = null)
    {
        const string sql = """
            SELECT Id,Fecha,Usuario,Modulo,Accion,IdRegistro,Descripcion,BaseDatos,Tabla,FolioApp,FolioOperacion,FolioPos,Taxista,Gafete,Importe,Exito,Equipo,Aplicacion,Detalles
            FROM LocalAuditoria
            WHERE Fecha >= $start AND Fecha < $end
            ORDER BY Fecha DESC, Id DESC;
            """;
        return ReadAsync(sql, Audit, [("$start", (start ?? DateTime.Today.AddDays(-7)).Date.ToString("O", CultureInfo.InvariantCulture)), ("$end", (end ?? DateTime.Today).Date.AddDays(1).ToString("O", CultureInfo.InvariantCulture))]);
    }

    public async Task<long> SaveProductAsync(LocalProduct product, string user)
    {
        if (product.Price < 0) throw new ArgumentException("El precio no puede ser negativo.");
        if (product.TaxRate < 0) throw new ArgumentException("El IVA no puede ser negativo.");
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var id = await ScalarAsync(connection, transaction, """
            INSERT INTO LocalProductos (Codigo,Nombre,Precio,Iva,Existencia,Activo)
            VALUES ($code,$name,$price,$tax,$stock,$active)
            ON CONFLICT(Codigo) DO UPDATE SET Nombre=excluded.Nombre,Precio=excluded.Precio,Iva=excluded.Iva,Existencia=excluded.Existencia,Activo=excluded.Activo
            RETURNING Id;
            """,
            ("$code", Require(product.Code, "El cÃ³digo")), ("$name", Require(product.Name, "El producto")), ("$price", product.Price), ("$tax", product.TaxRate), ("$stock", product.Stock), ("$active", product.Active ? 1 : 0));
        await AuditAsync(connection, transaction, user, "Ventas", "Guardar producto", product.Code, product.Price, product.Name);
        await transaction.CommitAsync();
        return id;
    }

    public async Task<long> CreateSaleAsync(string folio, string customer, IEnumerable<(long ProductId, int Quantity)> lines, string user)
    {
        var requested = lines.Where(x => x.Quantity > 0).ToArray();
        if (requested.Length == 0) throw new ArgumentException("La venta requiere al menos una lÃ­nea.");
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var existing = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM LocalVentas WHERE Folio=$folio;", ("$folio", Require(folio, "El folio")));
        if (existing > 0) throw new InvalidOperationException("Ya existe una venta con ese folio.");

        var saleId = await ScalarAsync(connection, transaction, """
            INSERT INTO LocalVentas (Folio,Fecha,Cliente,Estatus,Usuario)
            VALUES ($folio,$date,$customer,'Abierta',$user) RETURNING Id;
            """,
            ("$folio", folio.Trim()), ("$date", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)), ("$customer", customer.Trim()), ("$user", Require(user, "El usuario")));

        foreach (var line in requested)
        {
            var product = await GetProductForUpdateAsync(connection, transaction, line.ProductId);
            if (!product.Active) throw new InvalidOperationException($"El producto {product.Code} no estÃ¡ activo.");
            if (product.Stock < line.Quantity) throw new InvalidOperationException($"No hay existencia suficiente de {product.Name}.");
            var subtotal = decimal.Round(product.Price * line.Quantity, 2);
            var tax = decimal.Round(subtotal * product.TaxRate, 2);
            var total = subtotal + tax;
            await ExecuteAsync(connection, transaction, """
                INSERT INTO LocalVentaLineas (VentaId,ProductoId,Codigo,Nombre,Cantidad,PrecioUnitario,Iva,Subtotal,ImporteIva,Total)
                VALUES ($sale,$product,$code,$name,$qty,$price,$taxRate,$subtotal,$tax,$total);
                """,
                ("$sale", saleId), ("$product", product.Id), ("$code", product.Code), ("$name", product.Name), ("$qty", line.Quantity), ("$price", product.Price), ("$taxRate", product.TaxRate), ("$subtotal", subtotal), ("$tax", tax), ("$total", total));
            await ExecuteAsync(connection, transaction, "UPDATE LocalProductos SET Existencia=Existencia-$qty WHERE Id=$id;", ("$qty", line.Quantity), ("$id", product.Id));
        }

        await RecalculateSaleAsync(connection, transaction, saleId);
        await AuditAsync(connection, transaction, user, "Ventas", "Guardar venta", folio, null, $"Cliente: {customer}; LÃ­neas: {requested.Length}");
        await transaction.CommitAsync();
        return saleId;
    }

    public async Task DeleteLastSaleLineAsync(string saleFolio, string user)
    {
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var sale = await GetSaleForUpdateAsync(connection, transaction, saleFolio);
        if (sale.Status == "Cobrada" || sale.PaymentStatus == "Pagado") throw new InvalidOperationException("No se puede borrar una lÃ­nea de una venta pagada.");
        var lines = await ReadInTransactionAsync(connection, transaction, """
            SELECT Id,VentaId,ProductoId,Codigo,Nombre,Cantidad,PrecioUnitario,Iva,Subtotal,ImporteIva,Total
            FROM LocalVentaLineas
            WHERE VentaId=$sale
            ORDER BY Id DESC
            LIMIT 1;
            """, SaleLine, ("$sale", sale.Id));
        var line = lines.SingleOrDefault() ?? throw new InvalidOperationException("La venta no tiene lÃ­neas para borrar.");
        await ExecuteAsync(connection, transaction, "UPDATE LocalProductos SET Existencia=Existencia+$qty WHERE Id=$product;", ("$qty", line.Quantity), ("$product", line.ProductId));
        await ExecuteAsync(connection, transaction, "DELETE FROM LocalVentaLineas WHERE Id=$id;", ("$id", line.Id));
        await RecalculateSaleAsync(connection, transaction, sale.Id);
        await AuditAsync(connection, transaction, user, "Ventas", "Borrar lÃ­nea", sale.Folio, line.Total, $"Producto: {line.ProductName}; Cantidad: {line.Quantity}");
        await transaction.CommitAsync();
    }

    public async Task<string> RegisterPaymentAsync(string saleFolio, decimal amount, string method, string notes, string user)
    {
        if (amount < 0) throw new ArgumentException("El importe del pago no puede ser negativo.");
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var sale = await GetSaleForUpdateAsync(connection, transaction, saleFolio);
        if (sale.PaymentStatus == "Pagado") throw new InvalidOperationException("La venta ya estÃ¡ pagada.");
        var pending = Math.Max(sale.Total - sale.Paid, 0m);
        if (pending <= 0m) throw new InvalidOperationException("La venta ya no tiene saldo pendiente.");
        var amountToApply = amount == 0m ? pending : amount;
        if (amountToApply > pending) throw new InvalidOperationException("El pago excede el saldo pendiente.");
        var paymentFolio = "P" + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        await ScalarAsync(connection, transaction, """
            INSERT INTO LocalPagos (Folio,VentaFolio,Fecha,Importe,Metodo,Notas,Usuario)
            VALUES ($folio,$sale,$date,$amount,$method,$notes,$user) RETURNING Id;
            """,
            ("$folio", paymentFolio), ("$sale", sale.Folio), ("$date", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)), ("$amount", amountToApply), ("$method", Require(method, "El mÃ©todo")), ("$notes", notes.Trim()), ("$user", Require(user, "El usuario")));
        await RecalculateSalePaymentsAsync(connection, transaction, sale.Folio);
        await AuditAsync(connection, transaction, user, "Pagos", "Registrar pago", sale.Folio, amountToApply, method + (amount == 0m ? " | AUTO-PENDIENTE" : ""));
        await transaction.CommitAsync();
        // Estado resultante: si el pago cubrió todo el saldo pendiente, queda PAGADO.
        return amountToApply >= pending ? "Pagado" : "Parcial";
    }

    public async Task<int> RecalculateCommissionsAsync(string user)
    {
        await EnsureCommissionSettingsLoadedAsync();
        await using var connection = database.Open();
        if (await HasTableAsync(connection, "mkt__dbo__mov_operacion"))
            return await RecalculateImportedMovOperationCommissionsAsync(connection, user);

        if (_sqlSource is not null)
        {
            var authoritative = await TryRecalculateAuthoritativeCommissionsAsync(user);
            if (authoritative > 0) return authoritative;
        }

        var relationBased = await TryRecalculateRelationCommissionsAsync(user);
        if (relationBased > 0) return relationBased;

        var defaultCommissionPercent = (await _commissionSettings.GetRulesAsync("GENERAL", "DEFAULT_COMMISSION", true, DateTime.Today))
            .FirstOrDefault()?.CommissionPercent ?? 10m;
        await using var transaction = connection.BeginTransaction();
        var sales = await ReadInTransactionAsync(connection, transaction, """
            SELECT Id,Folio,Fecha,Cliente,Estatus,Subtotal,Iva,Total,Pagado,EstadoPago,Usuario
            FROM LocalVentas WHERE Estatus <> 'Cancelada';
            """, Sale);
        var affected = 0;
        foreach (var sale in sales)
        {
            var commission = decimal.Round(sale.Total * (CommissionPaymentRules.NormalizePercent(defaultCommissionPercent) / 100m), 2);
            var existingPaid = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(Pagado,0) FROM LocalComisiones WHERE VentaFolio=$folio;", ("$folio", sale.Folio));
            var balance = Math.Max(commission - existingPaid, 0);
            var status = balance <= 0 ? "Pagada" : existingPaid > 0 ? "Parcial" : "Pendiente";
            await ExecuteAsync(connection, transaction, """
                INSERT INTO LocalComisiones (Folio,VentaFolio,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus)
                VALUES ($folio,$sale,$date,$total,$commission,$paid,$balance,$status)
                ON CONFLICT(Folio) DO UPDATE SET TotalVenta=excluded.TotalVenta,ImporteComision=excluded.ImporteComision,Pagado=excluded.Pagado,Saldo=excluded.Saldo,Estatus=excluded.Estatus;
                """,
                ("$folio", "C-" + sale.Folio), ("$sale", sale.Folio), ("$date", sale.Date.ToString("O", CultureInfo.InvariantCulture)), ("$total", sale.Total), ("$commission", commission), ("$paid", existingPaid), ("$balance", balance), ("$status", status));
            affected++;
        }
        await AuditAsync(connection, transaction, user, "Comisiones", "Recalcular comisiones", "", affected, $"Ventas procesadas: {affected}");
        await transaction.CommitAsync();
        return affected;
    }

    public async Task EnsureCommissionSnapshotAsync(LocalCommissionBrowserRow row, string user)
    {
        if (row.PagoComision <= 0m)
            throw new InvalidOperationException("No hay comision pendiente con importe mayor a cero.");

        var saleFolio = Require(string.IsNullOrWhiteSpace(row.SaleFolio) ? row.Folio : row.SaleFolio, "El folio");
        var commissionFolio = NormalizeCommissionFolio(row.Folio, saleFolio);
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();

        var existing = (await ReadInTransactionAsync(
            connection,
            transaction,
            "SELECT Id,Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus FROM LocalComisiones WHERE Folio=$folio OR VentaFolio=$sale;",
            Commission,
            ("$folio", commissionFolio),
            ("$sale", saleFolio))).FirstOrDefault();

        var paid = existing?.PaidAmount ?? Math.Max(row.Pagado, 0m);
        paid = Math.Min(paid, row.PagoComision);
        var balance = Math.Max(row.PagoComision - paid, 0m);
        var status = balance <= 0m ? "Pagada" : paid > 0m ? "Parcial" : "Pendiente";
        var date = row.Fecha == default ? DateTime.Today : row.Fecha;

        await ExecuteAsync(connection, transaction, """
            INSERT INTO LocalComisiones (Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus)
            VALUES ($folio,$sale,$code,$taxista,$date,$total,$commission,$paid,$balance,$status)
            ON CONFLICT(Folio) DO UPDATE SET
              VentaFolio=excluded.VentaFolio,
              ClaveTaxista=excluded.ClaveTaxista,
              Taxista=excluded.Taxista,
              Fecha=excluded.Fecha,
              TotalVenta=excluded.TotalVenta,
              ImporteComision=excluded.ImporteComision,
              Pagado=excluded.Pagado,
              Saldo=excluded.Saldo,
              Estatus=excluded.Estatus;
            """,
            ("$folio", commissionFolio),
            ("$sale", saleFolio),
            ("$code", row.Gafete ?? string.Empty),
            ("$taxista", row.Nombre ?? string.Empty),
            ("$date", date.ToString("O", CultureInfo.InvariantCulture)),
            ("$total", row.VentaTotal),
            ("$commission", row.PagoComision),
            ("$paid", paid),
            ("$balance", balance),
            ("$status", status));

        await AuditAsync(connection, transaction, user, "Comisiones", "Preparar comision para pago", saleFolio, row.PagoComision, status);
        await transaction.CommitAsync();
    }

    private static string NormalizeCommissionFolio(string folio, string saleFolio)
    {
        var value = string.IsNullOrWhiteSpace(folio) ? saleFolio : folio.Trim();
        return value.StartsWith("C-", StringComparison.OrdinalIgnoreCase) ? value : "C-" + value;
    }

    /// <summary>
    /// Registra el pago de la COMISION en SQL Server (AppMovilRegistro.pago_comision /
    /// fecha_pago_comision), que es la fuente que lee la pantalla de Comisiones.
    ///
    /// Sin esto, PayCommissionAsync solo escribia el snapshot local de SQLite y la comision
    /// se quedaba en PENDIENTE para siempre: se cobraba, pero la fuente autoritativa nunca se
    /// enteraba. Es el equivalente, del lado de comision, a lo que PayPayoutAsync hace del
    /// lado de la dejada. No toca ninguna columna de dejada.
    /// </summary>
    public async Task MarkCommissionPaidInPosAsync(string operationFolio, decimal paidAmount, string user)
    {
        if (_sqlSource is null) return;
        var folio = operationFolio?.Trim();
        if (string.IsNullOrWhiteSpace(folio)) return;

        await using var connection = await _sqlSource.OpenPosAsync();

        // Si la instalacion todavia no tiene estas columnas, no se puede registrar el pago:
        // se avisa en vez de fallar en silencio y dejar la comision cobrada sin rastro.
        if (!await HasSqlColumnAsync(connection, "AppMovilRegistro", "pago_comision")
            || !await HasSqlColumnAsync(connection, "AppMovilRegistro", "fecha_pago_comision"))
        {
            throw new InvalidOperationException(
                "La tabla AppMovilRegistro no tiene las columnas pago_comision / fecha_pago_comision, "
                + "asi que el pago de la comision no se puede registrar en SQL Server.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {_sqlSource.PosTable("AppMovilRegistro")}
            SET pago_comision = @paid,
                fecha_pago_comision = SYSDATETIME()
            WHERE folio_app = @folio OR folio_app_original = @folio;
            """;
        command.Parameters.AddWithValue("@paid", paidAmount);
        command.Parameters.AddWithValue("@folio", folio);
        await command.ExecuteNonQueryAsync();
        _ = user;
    }

    public async Task PayCommissionAsync(string folio, decimal amount, string user)
    {
        if (amount < 0) throw new ArgumentException("El abono no puede ser negativo.");
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var commissions = await ReadInTransactionAsync(connection, transaction, "SELECT Id,Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus FROM LocalComisiones WHERE Folio=$folio OR VentaFolio=$folio;", Commission, ("$folio", Require(folio, "El folio")));
        var items = commissions.ToList();
        if (items.Count == 0) throw new InvalidOperationException("Comision no encontrada.");
        var pending = items.Sum(x => x.Balance);
        if (pending <= 0m) throw new InvalidOperationException("La comision ya esta pagada.");
        var amountToApply = amount == 0m ? pending : amount;
        if (amountToApply > pending) throw new InvalidOperationException("El abono excede el saldo de la comision.");

        var remaining = amountToApply;
        foreach (var commission in items.OrderByDescending(x => x.Balance))
        {
            if (remaining <= 0m) break;
            var currentApply = Math.Min(commission.Balance, remaining);
            var paid = commission.PaidAmount + currentApply;
            var balance = commission.CommissionAmount - paid;
            var status = balance <= 0 ? "Pagada" : "Parcial";
            await ExecuteAsync(connection, transaction, "UPDATE LocalComisiones SET Pagado=$paid,Saldo=$balance,Estatus=$status WHERE Folio=$folio;", ("$paid", paid), ("$balance", balance), ("$status", status), ("$folio", commission.Folio));
            remaining -= currentApply;
        }

        var finalStatus = amountToApply >= pending ? "Pagada" : "Parcial";
        await AuditAsync(connection, transaction, user, "Comisiones", "Abonar comision", folio, amountToApply, finalStatus + (amount == 0m ? " | AUTO-SALDO" : ""));
        await transaction.CommitAsync();
    }

    public async Task<LocalCut> CalculateCutAsync(DateTime date, decimal counted, string user)
    {
        await using var connection = database.Open();
        if (await HasTableAsync(connection, "mkt__dbo__mov_operacion"))
            return await CalculateImportedMovOperationCutAsync(connection, date, user);

        await using var transaction = connection.BeginTransaction();
        var day = date.Date;
        var start = day.ToString("O", CultureInfo.InvariantCulture);
        var end = day.AddDays(1).ToString("O", CultureInfo.InvariantCulture);
        var cash = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(Importe),0) FROM LocalPagos WHERE Fecha >= $start AND Fecha < $end AND upper(Metodo) <> 'TARJETA';", ("$start", start), ("$end", end));
        var card = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(Importe),0) FROM LocalPagos WHERE Fecha >= $start AND Fecha < $end AND upper(Metodo) = 'TARJETA';", ("$start", start), ("$end", end));
        var payments = cash + card;
        var expenses = 0m;
        var expected = cash - expenses;
        var difference = counted - expected;
        await ExecuteAsync(connection, transaction, """
            INSERT INTO LocalCortes (Fecha,Efectivo,Tarjeta,Pagos,Gastos,Esperado,Contado,Diferencia,Estatus,Usuario)
            VALUES ($date,$cash,$card,$payments,$expenses,$expected,$counted,$difference,'Abierto',$user)
            ON CONFLICT(Fecha) DO UPDATE SET Efectivo=excluded.Efectivo,Tarjeta=excluded.Tarjeta,Pagos=excluded.Pagos,Gastos=excluded.Gastos,Esperado=excluded.Esperado,Contado=excluded.Contado,Diferencia=excluded.Diferencia,Usuario=excluded.Usuario;
            """,
            ("$date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$cash", cash), ("$card", card), ("$payments", payments), ("$expenses", expenses), ("$expected", expected), ("$counted", counted), ("$difference", difference), ("$user", Require(user, "El usuario")));
        await AuditAsync(connection, transaction, user, "Cortes", "Calcular corte", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), difference, "");
        await transaction.CommitAsync();
        return (await GetCutsAsync()).First(x => x.Date.Date == day);
    }

    private async Task<int> RecalculateImportedMovOperationCommissionsAsync(SqliteConnection connection, string user)
    {
        await using var transaction = connection.BeginTransaction();
        var rows = await ReadImportedMovOperationsAsync(connection, transaction, null);
        var affected = 0;
        foreach (var row in rows)
        {
            var amount = CalculateWebCommission(row);
            var paid = Math.Max(row.Payment, 0m);
            var balance = Math.Max(amount - paid, 0m);
            var status = ResolveCommissionStatus(amount, paid);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO LocalComisiones (Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus)
                VALUES ($folio,$sale,'','',$date,$total,$commission,$paid,$balance,$status)
                ON CONFLICT(Folio) DO UPDATE SET
                  VentaFolio=excluded.VentaFolio,
                  Fecha=excluded.Fecha,
                  TotalVenta=excluded.TotalVenta,
                  ImporteComision=excluded.ImporteComision,
                  Pagado=excluded.Pagado,
                  Saldo=excluded.Saldo,
                  Estatus=excluded.Estatus;
                """,
                ("$folio", "C-" + row.Folio),
                ("$sale", row.Folio),
                ("$date", row.Date.ToString("O", CultureInfo.InvariantCulture)),
                ("$total", row.TotalSale),
                ("$commission", amount),
                ("$paid", paid),
                ("$balance", balance),
                ("$status", status));
            affected++;
        }

        await AuditAsync(connection, transaction, user, "Comisiones", "Recalcular", "", affected, $"FÃ³rmula Web aplicada desde mkt.mov_operacion. Filas: {affected}");
        await transaction.CommitAsync();
        return affected;
    }

    private async Task<LocalCut> CalculateImportedMovOperationCutAsync(SqliteConnection connection, DateTime date, string user)
    {
        await using var transaction = connection.BeginTransaction();
        var day = date.Date;
        var rows = await ReadImportedMovOperationsAsync(connection, transaction, day);
        if (rows.Count == 0) throw new InvalidOperationException("No hay movimientos importados para la fecha seleccionada.");

        var cash = rows.Sum(x => x.Cash);
        var card = rows.Sum(x => x.Card);
        var expenses = rows.Sum(x => x.Expenses);
        var commissions = rows.Sum(CalculateWebCommission);
        var totalDay = rows.Sum(x => x.TotalDay);
        var paidAmount = cash + card;
        var difference = totalDay - paidAmount;
        var closed = await GetImportedCutClosedStatusAsync(connection, transaction, day);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO LocalCortes (Fecha,Efectivo,Tarjeta,Pagos,Gastos,Esperado,Contado,Diferencia,Estatus,Usuario,FechaCierre)
            VALUES ($date,$cash,$card,$payments,$expenses,$expected,$counted,$difference,$status,$user,$closed)
            ON CONFLICT(Fecha) DO UPDATE SET
              Efectivo=excluded.Efectivo,
              Tarjeta=excluded.Tarjeta,
              Pagos=excluded.Pagos,
              Gastos=excluded.Gastos,
              Esperado=excluded.Esperado,
              Contado=excluded.Contado,
              Diferencia=excluded.Diferencia,
              Estatus=excluded.Estatus,
              Usuario=excluded.Usuario,
              FechaCierre=excluded.FechaCierre;
            """,
            ("$date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$cash", cash),
            ("$card", card),
            ("$payments", paidAmount),
            ("$expenses", expenses),
            ("$expected", totalDay),
            ("$counted", paidAmount),
            ("$difference", difference),
            ("$status", closed is null ? "Abierto" : "Cerrado"),
            ("$user", Require(user, "El usuario")),
            ("$closed", closed));
        await AuditAsync(connection, transaction, user, "Cortes", "Calcular", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), difference, $"Corte Web: movimientos={rows.Count}; comisiones={commissions:N2}");
        await transaction.CommitAsync();
        return (await GetCutsAsync()).First(x => x.Date.Date == day);
    }

    private static decimal CalculateWebCommission(ImportedMovOperation row)
    {
        var transportText = row.TransportType ?? string.Empty;
        var majestic = IsMajesticTransport(transportText);
        var salmoranTuribus = IsSalmoranOrTuribusTransport(transportText);
        var percentValue = row.CommissionPercent > 0m
            ? row.CommissionPercent
            : majestic ? 8m : salmoranTuribus ? 20m : row.CommissionPercent;
        var fixedCommission = percentValue > 100m
            ? percentValue
            : percentValue <= 0m && row.Maximum is >= 1m and <= 1000m
                ? row.Maximum
                : percentValue <= 0m && row.Minimum is >= 1m and <= 1000m
                    ? row.Minimum
                    : 0m;
        if (fixedCommission > 0m) return Math.Max(0m, decimal.Truncate(fixedCommission));

        var discountPercent = row.Amex > 0m
            ? CommissionPaymentRules.AmexRetentionPercent
            : row.Card > 0m
                ? CommissionPaymentRules.CardRetentionPercent
                : CommissionPaymentRules.CashRetentionPercent;
        var saleTotal = row.TotalSale;
        var net = discountPercent > 0m ? saleTotal - (saleTotal * (discountPercent / 100m)) : saleTotal;
        var deductions = CalculateExcelRuleDeductions(row);
        return Math.Max(0m, decimal.Truncate(Math.Max(0m, net - deductions) * (NormalizePercentValue(percentValue) / 100m)));
    }

    private static decimal ResolveImportedCommissionPercentage(ImportedMovOperation row)
    {
        var transportText = row.TransportType ?? string.Empty;
        if (row.CommissionPercent > 0m) return row.CommissionPercent;
        if (IsMajesticTransport(transportText)) return 8m;
        if (IsSalmoranOrTuribusTransport(transportText)) return 20m;
        return row.CommissionPercent;
    }

    private static void ApplyLargestSaleDejadaRule(IReadOnlyList<ImportedMovOperation> rows)
    {
        foreach (var group in rows
            .Where(x => x.LeftAmount > 0m && !IsMajesticTransport(x.TransportType))
            .GroupBy(x => !string.IsNullOrWhiteSpace(x.Folio) ? "FOLIO|" + NormalizeKey(x.Folio) : "ROW|" + x.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "|" + NormalizeKey(x.TransportType), StringComparer.OrdinalIgnoreCase))
        {
            var items = group
                .OrderByDescending(x => x.TotalSale)
                .ThenByDescending(x => x.TotalDay)
                .ThenBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (items.Count <= 1) continue;
            var dejada = items.Max(x => x.LeftAmount);
            for (var i = 0; i < items.Count; i++)
                items[i].EffectiveLeftAmount = i == 0 ? dejada : 0m;
        }
    }

    private static decimal CalculateExcelRuleDeductions(ImportedMovOperation row)
    {
        var deductions = 0m;
        if (!IsMajesticTransport(row.TransportType)) deductions += row.EffectiveLeftAmount;
        deductions += row.Expenses;
        deductions += row.Repair;
        deductions += row.Beverages;
        deductions += row.GiftBoxes;
        return deductions;
    }

    private static decimal NormalizePercentValue(decimal value) => Math.Clamp(value > 0m && value <= 1m ? value * 100m : value, 0m, 100m);

    private static bool IsMajesticTransport(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MAESTIC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSalmoranOrTuribusTransport(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains("SALMORAN", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private async Task<IReadOnlyList<ImportedMovOperation>> ReadImportedMovOperationsAsync(SqliteConnection connection, SqliteTransaction transaction, DateTime? date)
    {
        var dejadaLookup = await LoadImportedDejadaLookupAsync(connection, transaction);
        const string sql = """
            SELECT
              CAST(COALESCE(m.folioperacion, '') AS TEXT),
              COALESCE(m.fecha, ''),
              COALESCE(m.foliosoluone, ''),
              COALESCE(m.transportetipo, ''),
              COALESCE(m.totalefectivo, 0),
              COALESCE(m.totaltarjeta, 0),
              COALESCE(m.pago, 0),
              COALESCE(m.totalgastos, 0),
              COALESCE(m.totaljoyeria, 0),
              COALESCE(m.totalcompra, 0),
              COALESCE(m.totalartesania, 0),
              COALESCE(m.totallicor, 0),
              COALESCE(m.totalfarmacia, 0),
              COALESCE(m.dejada, 0)
            FROM "mkt__dbo__mov_operacion" m
            WHERE $date IS NULL OR substr(COALESCE(m.fecha, ''), 1, 10) = $date
            ORDER BY m.fecha DESC, m.folioperacion DESC;
            """;
        var result = await ReadInTransactionAsync(connection, transaction, sql, row =>
        {
            var parsedDate = DateTime.TryParse(Convert.ToString(row.GetValue(1), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value) ? value : DateTime.Today;
            var folio = row.GetString(0);
            var transportType = row.GetString(3);
            var dejada = dejadaLookup.GetValueOrDefault(NormalizeKey(folio), ImportedDejadaInfo.Empty);
            return new ImportedMovOperation(
                folio,
                parsedDate,
                row.GetString(2),
                transportType,
                Decimal(row, 4),
                Decimal(row, 5),
                Decimal(row, 6),
                Decimal(row, 7),
                Decimal(row, 8),
                Decimal(row, 9),
                Decimal(row, 10),
                Decimal(row, 11),
                Decimal(row, 12),
                Decimal(row, 13),
                0m,
                0m,
                0m,
                0m,
                0m,
                dejada.DriverName,
                dejada.Hotel,
                dejada.Passengers,
                dejada.UnitNumber,
                dejada.Badge);
        }, ("$date", date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        ApplyLargestSaleDejadaRule(result);
        return result;
    }

    private async Task<IReadOnlyDictionary<string, ImportedDejadaInfo>> LoadImportedDejadaLookupAsync(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!await HasTableAsync(connection, "mkt__dbo__dejadas"))
            return new Dictionary<string, ImportedDejadaInfo>(StringComparer.OrdinalIgnoreCase);

        var rows = await ReadInTransactionAsync(connection, transaction, """
            SELECT
              CAST(COALESCE(folioregistro, '') AS TEXT),
              COALESCE(folioregistrostr, ''),
              COALESCE(codigorecepcion, ''),
              COALESCE(nombrevendedor, ''),
              COALESCE(hotel, ''),
              COALESCE(pax, 0),
              COALESCE(unidad, ''),
              COALESCE(gafete, '')
            FROM "mkt__dbo__dejadas";
            """, row => new ImportedDejadaCatalogRow(
            Text(row, 0),
            Text(row, 1),
            Text(row, 2),
            Text(row, 3),
            Text(row, 4),
            row.IsDBNull(5) ? 0 : Convert.ToInt32(row.GetValue(5), CultureInfo.InvariantCulture),
            Text(row, 6),
            Text(row, 7)));

        var lookup = new Dictionary<string, ImportedDejadaInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var info = new ImportedDejadaInfo(row.DriverName, row.Hotel, row.Passengers, row.UnitNumber, row.Badge);
            foreach (var key in new[] { NormalizeKey(row.FolioRegistro), NormalizeKey(row.FolioRegistroString), NormalizeKey(row.CodigoRecepcion) })
            {
                if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
                    lookup[key] = info;
            }
        }

        return lookup;
    }

    private static async Task<string?> GetImportedCutClosedStatusAsync(SqliteConnection connection, SqliteTransaction transaction, DateTime day)
    {
        if (!await HasTableAsync(connection, "ControlTaxis__dbo__Cortes")) return null;
        var values = await ReadInTransactionAsync(connection, transaction, """
            SELECT COALESCE(FechaCierre, '')
            FROM "ControlTaxis__dbo__Cortes"
            WHERE substr(COALESCE(Fecha, ''), 1, 10) = $date
              AND UPPER(COALESCE(Estatus, '')) = 'CERRADO'
            LIMIT 1;
            """, row => row.GetString(0), ("$date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        return values.FirstOrDefault();
    }

    private static string ResolveCommissionStatus(decimal amount, decimal paid) =>
        amount <= 0m ? "SIN COMISION" : paid >= amount ? "PAGADA" : paid > 0m ? "PARCIAL" : "PENDIENTE";

    public async Task CloseCutAsync(DateTime date, string user)
    {
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var affected = await ExecuteAsync(connection, transaction, "UPDATE LocalCortes SET Estatus='Cerrado', Usuario=$user, FechaCierre=$closed WHERE Fecha=$date AND Estatus <> 'Cerrado';", ("$user", Require(user, "El usuario")), ("$closed", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)), ("$date", date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        if (affected != 1) throw new InvalidOperationException("El corte no existe o ya estaba cerrado.");
        await AuditAsync(connection, transaction, user, "Cortes", "Cerrar corte", date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, "");
        await transaction.CommitAsync();
    }

    private async Task RecalculateSaleAsync(SqliteConnection connection, SqliteTransaction transaction, long saleId)
    {
        var subtotal = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(Subtotal),0) FROM LocalVentaLineas WHERE VentaId=$id;", ("$id", saleId));
        var tax = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(ImporteIva),0) FROM LocalVentaLineas WHERE VentaId=$id;", ("$id", saleId));
        var total = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(Total),0) FROM LocalVentaLineas WHERE VentaId=$id;", ("$id", saleId));
        await ExecuteAsync(connection, transaction, "UPDATE LocalVentas SET Subtotal=$subtotal,Iva=$tax,Total=$total WHERE Id=$id;", ("$subtotal", subtotal), ("$tax", tax), ("$total", total), ("$id", saleId));
    }

    private async Task RecalculateSalePaymentsAsync(SqliteConnection connection, SqliteTransaction transaction, string folio)
    {
        var paid = await ScalarDecimalAsync(connection, transaction, "SELECT COALESCE(SUM(Importe),0) FROM LocalPagos WHERE VentaFolio=$folio;", ("$folio", folio));
        var total = await ScalarDecimalAsync(connection, transaction, "SELECT Total FROM LocalVentas WHERE Folio=$folio;", ("$folio", folio));
        var status = paid >= total ? "Pagado" : paid > 0 ? "Parcial" : "Pendiente";
        var saleStatus = paid >= total ? "Cobrada" : "Abierta";
        await ExecuteAsync(connection, transaction, "UPDATE LocalVentas SET Pagado=$paid,EstadoPago=$status,Estatus=$saleStatus WHERE Folio=$folio;", ("$paid", paid), ("$status", status), ("$saleStatus", saleStatus), ("$folio", folio));
    }

    private async Task AuditAsync(SqliteConnection connection, SqliteTransaction transaction, string user, string module, string action, string reference, decimal? amount, string details) =>
        await ExecuteAsync(connection, transaction, """
            INSERT INTO LocalAuditoria
              (Fecha,Usuario,Modulo,Accion,Referencia,IdRegistro,Descripcion,BaseDatos,Tabla,FolioOperacion,FolioPos,Importe,Exito,Equipo,Aplicacion,Detalles)
            VALUES
              ($date,$user,$module,$action,$reference,$record,$description,'SQLite','Local',$operation,$pos,$amount,1,$machine,'ControlTaxiDesktop',$details);
            """,
            ("$date", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)),
            ("$user", Require(user, "El usuario")),
            ("$module", module),
            ("$action", action),
            ("$reference", reference),
            ("$record", reference),
            ("$description", details),
            ("$operation", reference),
            ("$pos", reference),
            ("$amount", amount),
            ("$machine", Environment.MachineName),
            ("$details", details));

    private async Task<int> TryRecalculateAuthoritativeCommissionsAsync(string user)
    {
        if (_sqlSource is null) return 0;
        var sqlSource = _sqlSource;

        var rows = await LoadAuthoritativeCommissionRowsAsync();
        if (rows.Count == 0) return 0;

        ApplyLargestPayoutToHighestSale(rows);
        foreach (var row in rows)
            row.CommissionAmount = CalculateAuthoritativeCommission(row);
        ApplyStoreOnlyOperationGroupCommissions(rows);
        ApplyCommissionPayments(rows);

        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction, "DELETE FROM LocalComisiones WHERE Folio LIKE 'C-%';");

        foreach (var row in rows)
        {
            var status = ResolveCommissionStatus(row.CommissionAmount, row.PaidAmount);
            await ExecuteAsync(connection, transaction, """
                INSERT INTO LocalComisiones (Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus)
                VALUES ($folio,$sale,$driverCode,$driver,$date,$total,$commission,$paid,$balance,$status);
                """,
                ("$folio", row.LocalFolio),
                ("$sale", row.OperationFolio),
                ("$driverCode", row.DriverCode),
                ("$driver", row.DriverName),
                ("$date", row.Date.ToString("O", CultureInfo.InvariantCulture)),
                ("$total", row.SaleTotal),
                ("$commission", row.CommissionAmount),
                ("$paid", row.PaidAmount),
                ("$balance", Math.Max(row.CommissionAmount - row.PaidAmount, 0m)),
                ("$status", status));
        }

        await AuditAsync(connection, transaction, user, "Comisiones", "Recalcular", "", rows.Count, $"Comisiones recalculadas con tickets reales remisioM. Filas: {rows.Count}");
        await transaction.CommitAsync();
        return rows.Count;
    }

    private async Task<int> TryRecalculateRelationCommissionsAsync(string user)
    {
        var rows = await GetCommissionBrowserRowsFromRelationsAsync(null, null, null);
        if (rows.Count == 0)
            return 0;

        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction, "DELETE FROM LocalComisiones WHERE Folio LIKE 'C-%';");

        foreach (var row in rows)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO LocalComisiones (Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus)
                VALUES ($folio,$sale,$driverCode,$driver,$date,$total,$commission,$paid,$balance,$status);
                """,
                ("$folio", row.Folio),
                ("$sale", row.SaleFolio),
                ("$driverCode", row.Gafete),
                ("$driver", row.Nombre),
                ("$date", row.Fecha.ToString("O", CultureInfo.InvariantCulture)),
                ("$total", row.VentaTotal),
                ("$commission", row.PagoComision),
                ("$paid", row.Pagado),
                ("$balance", row.Saldo),
                ("$status", row.Estatus));
        }

        await AuditAsync(connection, transaction, user, "Comisiones", "Recalcular", "", rows.Count, $"Comisiones recalculadas desde relaciones reales. Filas: {rows.Count}");
        await transaction.CommitAsync();
        return rows.Count;
    }

    private async Task<IReadOnlyList<LocalCommissionBrowserRow>> GetCommissionBrowserRowsFromRelationsAsync(string? search, DateTime? start, DateTime? end)
    {
        var operations = new LocalOperationsRepository(database);
        var relations = await operations.GetRelationsAsync(search, start, end, includeFinancialDetails: true);
        var rows = relations
            .Where(x => x.Sale > 0m || x.Commission > 0m || x.CommissionPaid > 0m)
            .Select(MapRelationCommissionRow)
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Folio, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return FilterCommissionBrowserRows(rows, search, start, end).ToArray();
    }

    private async Task<List<AuthoritativeCommissionRow>> LoadAuthoritativeCommissionRowsAsync(string? search = null, DateTime? start = null, DateTime? end = null)
    {
        var result = new List<AuthoritativeCommissionRow>();
        if (_sqlSource is null) return result;
        var sqlSource = _sqlSource;

        await using var posConnection = await sqlSource.OpenPosAsync();
        await using var compuConnection = await sqlSource.OpenCompuadmoAsync();
        await using var joyeriaConnection = await sqlSource.OpenJoyeriaAsync();

        var transportCatalog = await ReadTransportCatalogAsync(posConnection);
        var appRows = await ReadAuthoritativeAppRowsAsync(posConnection, search, start, end);

        // Resuelve los tickets de tienda de cada registro (esto si depende de cada fila:
        // usa folios + nombre de taxista + fecha para encontrar el ticket correcto), pero
        // NO consulta pagos ni gastos todavia. Antes, pagos/gastos se pedian a SQL Server
        // uno por uno dentro de este mismo ciclo (hasta 2 consultas extra por registro,
        // ~80 idas y vueltas con 40 registros). Ahora se juntan todos los tickets aqui y
        // se piden en un solo viaje por tipo de dato, igual que ya hace la pantalla de
        // Relaciones. Confirmado con el usuario 2026-08-20.
        var pending = new List<(AuthoritativeAppRow AppRow, List<StoreTicketRow> CommissionableTickets)>();
        var allTicketNumbers = new List<string>();
        var storeTicketsByRow = await LoadStoreTicketsForAllRowsAsync(compuConnection, joyeriaConnection, appRows);
        for (var rowIndex = 0; rowIndex < appRows.Count; rowIndex++)
        {
            var appRow = appRows[rowIndex];
            var storeTickets = storeTicketsByRow[rowIndex];
            if (storeTickets is null)
            {
                // Sin match directo: solo aqui se usa el respaldo por observaciones, que si
                // depende del taxista y la fecha de cada registro.
                storeTickets = IsPlaza28SqlMode && !appRow.HasLinkedOperationFolio
                    ? await LoadStoreTicketsForKeysAsync(
                        compuConnection,
                        joyeriaConnection,
                        appRow.Keys,
                        appRow.PosFolio,
                        appRow.DriverName,
                        appRow.Date,
                        allowObservationFallback: true)
                    : [];
            }
            var commissionableTickets = storeTickets
                .Where(x => IsCommissionableStoreTicket(x.Ticket))
                .ToList();
            if (commissionableTickets.Count == 0) continue;

            pending.Add((appRow, commissionableTickets));
            allTicketNumbers.AddRange(commissionableTickets.Select(x => x.Ticket));
        }

        var distinctTicketNumbers = allTicketNumbers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Dictionary<string, TicketPaymentBreakdown> payments;
        try
        {
            payments = await LoadTicketPaymentBreakdownsAsync(compuConnection, joyeriaConnection, distinctTicketNumbers);
        }
        catch
        {
            payments = new Dictionary<string, TicketPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, StoreExpenseBreakdown> expenses;
        try
        {
            expenses = await LoadTicketExpenseBreakdownsAsync(compuConnection, joyeriaConnection, distinctTicketNumbers);
        }
        catch
        {
            expenses = new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var (appRow, commissionableTickets) in pending)
        {
            foreach (var ticket in commissionableTickets)
            {
                payments.TryGetValue(ticket.Ticket, out var breakdown);
                expenses.TryGetValue(ticket.Ticket, out var expense);
                result.Add(new AuthoritativeCommissionRow(
                    "C-" + appRow.OperationFolio + "-" + ticket.Ticket,
                    appRow.OperationFolio,
                    ticket.Ticket,
                    appRow.Date,
                    appRow.DriverCode,
                    appRow.DriverName,
                    appRow.TransportType,
                    ticket.Total,
                    ticket.VentaTienda,
                    ticket.VentaJoyeria,
                    appRow.Hotel,
                    appRow.Passengers,
                    appRow.UnitNumber,
                    appRow.Badge,
                    appRow.Staff,
                    appRow.DriverName,
                    appRow.Payout,
                    appRow.CommissionPaidControl,
                    appRow.CommissionPaidDate,
                    breakdown ?? new TicketPaymentBreakdown(ticket.Total, 0m, 0m, "SIN PAGO"),
                    expense,
                    ResolveTransportInfo(transportCatalog, appRow.TransportType, appRow.Date))
                {
                    // El estatus de la dejada viaja aparte del de la comision.
                    PayoutStatus = appRow.PayoutStatus
                });
            }
        }

        var appFolios = appRows.Select(x => x.OperationFolio).Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        result.AddRange(await ReadAuthoritativeMovRowsAsync(posConnection, appFolios, transportCatalog, search, start, end));
        result.AddRange(await ReadStoreOnlyObservationRowsAsync(
            compuConnection,
            joyeriaConnection,
            transportCatalog,
            result.Select(x => x.Ticket).ToHashSet(StringComparer.OrdinalIgnoreCase),
            search,
            start,
            end));
        return result;
    }

    private async Task<IReadOnlyList<ImportedTransportCatalogRow>> ReadTransportCatalogAsync(SqlConnection connection)
    {
        if (_sqlSource is null) return [];
        var sqlSource = _sqlSource;
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
              COALESCE(tipo, '') AS Tipo,
              COALESCE(nombre, '') AS Nombre,
              COALESCE(efectivo, 0) AS DescEfectivo,
              COALESCE(tarjeta, 0) AS DescTarjeta,
              COALESCE(comision, 0) AS Comision,
              COALESCE(minimo, 0) AS Minimo,
              COALESCE(maximo, 0) AS Maximo,
              COALESCE(amexco, 0) AS DescAmex
            FROM {sqlSource.PosTable("transporte")}
            WHERE COALESCE(LTRIM(RTRIM(tipo)), '') <> '' OR COALESCE(LTRIM(RTRIM(nombre)), '') <> '';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<ImportedTransportCatalogRow>();
        while (await reader.ReadAsync())
        {
            result.Add(new ImportedTransportCatalogRow(
                Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture)));
        }
        result.AddRange(await ReadConfiguredTransportCatalogRowsAsync());
        return result;
    }

    /// <summary>
    /// Indica si una columna existe en la tabla. Se usa para no romper la consulta principal en
    /// instalaciones donde AppMovilRegistro todavia no tiene los campos del pago de dejada:
    /// si la columna no existe, SQL Server tira "Invalid column name" y toda la pantalla de
    /// Comisiones se quedaba vacia por caer al respaldo.
    /// </summary>
    private static async Task<bool> HasSqlColumnAsync(SqlConnection connection, string table, string column)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = """
                SELECT TOP 1 1
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @table AND COLUMN_NAME = @column;
                """;
            command.Parameters.AddWithValue("@table", table);
            command.Parameters.AddWithValue("@column", column);
            return await command.ExecuteScalarAsync() is not null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<List<AuthoritativeAppRow>> ReadAuthoritativeAppRowsAsync(SqlConnection connection, string? search, DateTime? start, DateTime? end)
    {
        if (_sqlSource is null) return [];
        var sqlSource = _sqlSource;

        // El estatus de la dejada solo se puede calcular si existen sus columnas.
        var hasEstadoPagoDejada = await HasSqlColumnAsync(connection, "AppMovilRegistro", "estado_pago_dejada");
        var hasPayoutStatus = await HasSqlColumnAsync(connection, "AppMovilRegistro", "payout_status");
        var hasFechaPagoDejada = await HasSqlColumnAsync(connection, "AppMovilRegistro", "fecha_pago_dejada");
        var hasPayoutDate = await HasSqlColumnAsync(connection, "AppMovilRegistro", "payout_date");

        var payoutDateParts = new List<string>();
        if (hasFechaPagoDejada) payoutDateParts.Add("a.fecha_pago_dejada");
        if (hasPayoutDate) payoutDateParts.Add("a.payout_date");
        var payoutStatusParts = new List<string>();
        if (hasEstadoPagoDejada) payoutStatusParts.Add("a.estado_pago_dejada");
        if (hasPayoutStatus) payoutStatusParts.Add("a.payout_status");

        string payoutStatusExpression;
        if (payoutDateParts.Count == 0 && payoutStatusParts.Count == 0)
        {
            payoutStatusExpression = "'SIN DEJADA'";
        }
        else
        {
            var conditions = new List<string>();
            if (payoutDateParts.Count > 0)
                conditions.Add($"COALESCE({string.Join(", ", payoutDateParts)}) IS NOT NULL");
            if (payoutStatusParts.Count > 0)
                conditions.Add($"UPPER(COALESCE({string.Join(", ", payoutStatusParts)}, '')) IN ('PAGADO', 'PAGADA')");

            payoutStatusExpression = $"""
                CASE
                  WHEN {string.Join(" OR ", conditions)} THEN 'PAGADA'
                  WHEN COALESCE(a.total, 0) <= 0 THEN 'SIN DEJADA'
                  ELSE 'PENDIENTE'
                END
                """;
        }

        await using var command = connection.CreateCommand();
        var filters = new List<string> { "COALESCE(a.folio_app, '') <> ''" };
        if (start.HasValue)
        {
            command.Parameters.AddWithValue("@startDate", start.Value.Date);
            filters.Add("CAST(COALESCE(a.fecha_operacion, a.fecha_creacion) AS date) >= @startDate");
        }

        if (end.HasValue)
        {
            command.Parameters.AddWithValue("@endDate", end.Value.Date);
            filters.Add("CAST(COALESCE(a.fecha_operacion, a.fecha_creacion) AS date) <= @endDate");
        }

        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            command.Parameters.AddWithValue("@search", normalizedSearch);
            command.Parameters.AddWithValue("@searchLike", "%" + normalizedSearch + "%");
            filters.Add("""
                (
                   a.folio_app = @search
                OR a.folio_app_original = @search
                OR COALESCE(a.folio_pos, '') = @search
                OR COALESCE(a.vendedor_nombre, '') LIKE @searchLike
                OR COALESCE(a.hotel, '') LIKE @searchLike
                OR COALESCE(a.folio_gafete, '') LIKE @searchLike
                )
                """);
        }

        command.CommandText = $"""
            SELECT
              COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), a.folio_app, '') AS FolioOperacion,
              COALESCE(NULLIF(r.FolioPos, ''), NULLIF(a.folio_pos, ''), '') AS FolioPos,
              COALESCE(a.folio_app, '') AS FolioApp,
              COALESCE(a.folio_app_original, '') AS FolioAppOriginal,
              COALESCE(a.fecha_operacion, a.fecha_creacion) AS Fecha,
              COALESCE(NULLIF(d.nombrestaff, ''), a.vendedor_nombre, '') AS DriverName,
              COALESCE(CAST(a.id_catalogo AS nvarchar(60)), '') AS DriverCode,
              COALESCE(NULLIF(r.TransporteTipo, ''), NULLIF(a.tipo_operacion, ''), '') AS TransportType,
              COALESCE(a.total, 0) AS Payout,
              COALESCE(a.pago_comision, 0) AS CommissionPaid,
              -- CONVERT ANTES del COALESCE, a proposito: COALESCE devuelve el tipo de mayor
              -- precedencia, asi que COALESCE(datetime2, '') convertia el '' a fecha y
              -- regresaba 1900-01-01 en vez de cadena vacia. El C# leia eso como "si hay
              -- fecha de pago" y daba TODA comision por pagada.
              COALESCE(CONVERT(nvarchar(30), a.fecha_pago_comision, 120), '') AS CommissionPaidDate,
              COALESCE(a.hotel, d.hotel, '') AS Hotel,
              COALESCE(a.pax, d.pax, 0) AS Passengers,
              COALESCE(a.unidad, d.unidad, '') AS UnitNumber,
              COALESCE(a.folio_gafete, d.gafete, '') AS Badge,
              COALESCE(d.nombrestaff, '') AS Staff,
              CASE
                WHEN NULLIF(LTRIM(RTRIM(COALESCE(r.FolioOperacion, ''))), '') IS NOT NULL
                 AND LTRIM(RTRIM(COALESCE(r.FolioOperacion, ''))) <> '0'
                THEN CAST(1 AS bit)
                ELSE CAST(0 AS bit)
              END AS HasLinkedOperationFolio,
              -- Estatus del pago de la DEJADA (el anticipo al taxista). Es independiente del
              -- pago de la comision. Misma regla que usa el modulo de Relaciones
              -- (LocalOperationsRepository): cuenta como pagada si hay fecha de pago o si el
              -- estado dice PAGADO/PAGADA. La expresion se arma arriba segun que columnas
              -- existan realmente, para no romper la consulta donde aun no estan.
              {payoutStatusExpression} AS PayoutStatus
            FROM {sqlSource.PosTable("AppMovilRegistro")} a
            OUTER APPLY
            (
              SELECT TOP (1) rr.FolioOperacion, rr.FolioPos, rr.TransporteTipo
              FROM {sqlSource.PosTable("RelacionTicketTaxista")} rr
              WHERE rr.FolioApp = a.folio_app OR rr.FolioApp = a.folio_app_original
              ORDER BY rr.FolioOperacion DESC, rr.FolioPos DESC
            ) r
            OUTER APPLY
            (
              SELECT TOP (1) dd.hotel, dd.pax, dd.unidad, dd.gafete, dd.nombrestaff, dd.nombrevendedor
              FROM {sqlSource.PosTable("dejadas")} dd
              WHERE dd.codigorecepcion = a.folio_app
                 OR dd.codigorecepcion = a.folio_app_original
                 OR CAST(dd.folioregistro AS nvarchar(60)) = COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), a.folio_app, '')
                 OR dd.folioregistrostr = COALESCE(NULLIF(r.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), a.folio_app, '')
              ORDER BY dd.fecha DESC
            ) d
            WHERE {string.Join(" AND ", filters)};
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<AuthoritativeAppRow>();
        while (await reader.ReadAsync())
        {
            var operationFolio = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operationFolio)) continue;
            var folioApp = Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty;
            var folioOriginal = Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty;
            var keys = new[] { operationFolio, folioApp, folioOriginal }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result.Add(new AuthoritativeAppRow(
                operationFolio,
                Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                folioApp,
                folioOriginal,
                reader.IsDBNull(4) ? DateTime.Today : Convert.ToDateTime(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader.GetValue(7), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
                reader.IsDBNull(10) ? string.Empty : Convert.ToString(reader.GetValue(10), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(11) ? string.Empty : Convert.ToString(reader.GetValue(11), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(12) ? 0 : Convert.ToInt32(reader.GetValue(12), CultureInfo.InvariantCulture),
                reader.IsDBNull(13) ? string.Empty : Convert.ToString(reader.GetValue(13), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(14) ? string.Empty : Convert.ToString(reader.GetValue(14), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(15) ? string.Empty : Convert.ToString(reader.GetValue(15), CultureInfo.InvariantCulture) ?? string.Empty,
                keys,
                !reader.IsDBNull(16) && Convert.ToBoolean(reader.GetValue(16), CultureInfo.InvariantCulture),
                reader.IsDBNull(17) ? string.Empty : Convert.ToString(reader.GetValue(17), CultureInfo.InvariantCulture) ?? string.Empty));
        }
        return result;
    }

    private async Task<List<AuthoritativeCommissionRow>> ReadAuthoritativeMovRowsAsync(SqlConnection connection, ISet<string> appFolios, IReadOnlyList<ImportedTransportCatalogRow> transportCatalog, string? search, DateTime? start, DateTime? end)
    {
        if (_sqlSource is null) return [];
        var sqlSource = _sqlSource;
        await using var command = connection.CreateCommand();
        var filters = new List<string>
        {
            "COALESCE(m.folioperacion, 0) <> 0",
            "COALESCE(m.foliosoluone, '') <> ''",
            "(COALESCE(m.totaljoyeria, 0) + COALESCE(m.totalcompra, 0)) > 0"
        };

        if (start.HasValue)
        {
            command.Parameters.AddWithValue("@movStartDate", start.Value.Date);
            filters.Add("CAST(COALESCE(m.fecha, GETDATE()) AS date) >= @movStartDate");
        }

        if (end.HasValue)
        {
            command.Parameters.AddWithValue("@movEndDate", end.Value.Date);
            filters.Add("CAST(COALESCE(m.fecha, GETDATE()) AS date) <= @movEndDate");
        }

        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            command.Parameters.AddWithValue("@movSearch", normalizedSearch);
            command.Parameters.AddWithValue("@movSearchLike", "%" + normalizedSearch + "%");
            filters.Add("""
                (
                   CAST(m.folioperacion AS nvarchar(60)) = @movSearch
                OR COALESCE(m.foliosoluone, '') = @movSearch
                OR COALESCE(d.nombrestaff, '') LIKE @movSearchLike
                OR COALESCE(d.hotel, '') LIKE @movSearchLike
                OR COALESCE(d.gafete, '') LIKE @movSearchLike
                )
                """);
        }

        command.CommandText = $"""
            SELECT
              CAST(m.folioperacion AS nvarchar(60)) AS FolioOperacion,
              COALESCE(m.fecha, GETDATE()) AS Fecha,
              COALESCE(m.foliosoluone, '') AS Ticket,
              COALESCE(NULLIF(m.transportetipo, ''), NULLIF(d.tipotransporte, ''), '') AS TransportType,
              COALESCE(m.totaljoyeria, 0) + COALESCE(m.totalcompra, 0) AS SaleTotal,
              COALESCE(m.totalcompra, 0) AS SaleStore,
              COALESCE(m.totaljoyeria, 0) AS SaleJewelry,
              COALESCE(m.dejada, COALESCE(d.total, 0), 0) AS Payout,
              COALESCE(m.totalefectivo, 0) AS Cash,
              COALESCE(m.totaltarjeta, 0) AS Card,
              COALESCE(m.pago, 0) AS Paid,
              COALESCE(d.idtaxi, 0) AS DriverCode,
              COALESCE(d.nombrestaff, '') AS DriverName,
              COALESCE(d.hotel, '') AS Hotel,
              COALESCE(d.pax, 0) AS Passengers,
              COALESCE(d.unidad, '') AS UnitNumber,
              COALESCE(d.gafete, '') AS Badge,
              COALESCE(d.nombrestaff, '') AS Staff
            FROM {sqlSource.PosTable("mov_operacion")} m
            OUTER APPLY
            (
              SELECT TOP (1) dd.tipotransporte, dd.total, dd.idtaxi, dd.nombrevendedor, dd.hotel, dd.pax, dd.unidad, dd.gafete, dd.nombrestaff
              FROM {sqlSource.PosTable("dejadas")} dd
              WHERE CAST(dd.folioregistro AS nvarchar(60)) = CAST(m.folioperacion AS nvarchar(60))
                 OR dd.folioregistrostr = CAST(m.folioperacion AS nvarchar(60))
                 OR dd.codigorecepcion = CAST(m.folioperacion AS nvarchar(60))
              ORDER BY dd.fecha DESC
            ) d
            WHERE {string.Join(" AND ", filters)};
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<AuthoritativeCommissionRow>();
        while (await reader.ReadAsync())
        {
            var operationFolio = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operationFolio) || appFolios.Contains(operationFolio)) continue;
            var cash = Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture);
            var card = Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture);
            var transportType = Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty;
            rows.Add(new AuthoritativeCommissionRow(
                "C-" + operationFolio + "-" + (Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty),
                operationFolio,
                Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(1) ? DateTime.Today : Convert.ToDateTime(reader.GetValue(1), CultureInfo.InvariantCulture),
                Convert.ToString(reader.GetValue(11), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader.GetValue(12), CultureInfo.InvariantCulture) ?? string.Empty,
                transportType,
                Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                reader.IsDBNull(13) ? string.Empty : Convert.ToString(reader.GetValue(13), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(14) ? 0 : Convert.ToInt32(reader.GetValue(14), CultureInfo.InvariantCulture),
                reader.IsDBNull(15) ? string.Empty : Convert.ToString(reader.GetValue(15), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(16) ? string.Empty : Convert.ToString(reader.GetValue(16), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(17) ? string.Empty : Convert.ToString(reader.GetValue(17), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader.GetValue(12), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
                string.Empty,
                new TicketPaymentBreakdown(cash, card, 0m, card > 0m ? "TARJETA" : "PESOS"),
                new StoreExpenseBreakdown(),
                ResolveTransportInfo(transportCatalog, transportType, Convert.ToDateTime(reader.GetValue(1), CultureInfo.InvariantCulture))));
        }
        return rows;
    }

    private static async Task<List<AuthoritativeCommissionRow>> ReadStoreOnlyObservationRowsAsync(
        SqlConnection compuConnection,
        SqlConnection joyeriaConnection,
        IReadOnlyList<ImportedTransportCatalogRow> transportCatalog,
        ISet<string> existingTickets,
        string? search,
        DateTime? start,
        DateTime? end)
    {
        if (string.IsNullOrWhiteSpace(search))
            return [];

        var rows = new List<StoreOnlyTicketRow>();
        rows.AddRange(await ReadStoreOnlyObservationRowsAsync(compuConnection, "folioregistro", "folio_remision", search, start, end, false));
        rows.AddRange(await ReadStoreOnlyObservationRowsAsync(joyeriaConnection, "folio_registro", "COALESCE(folio_pedido, folio_factura)", search, start, end, true));
        rows = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Ticket) && !existingTickets.Contains(x.Ticket))
            .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Total).First())
            .ToList();

        if (rows.Count == 0)
            return [];

        var tickets = rows.Select(x => x.Ticket).ToArray();
        var payments = await LoadTicketPaymentBreakdownsAsync(compuConnection, joyeriaConnection, tickets);
        var expenses = await LoadTicketExpenseBreakdownsAsync(compuConnection, joyeriaConnection, tickets);
        var result = new List<AuthoritativeCommissionRow>();
        foreach (var row in rows)
        {
            payments.TryGetValue(row.Ticket, out var breakdown);
            expenses.TryGetValue(row.Ticket, out var expense);
            var transport = InferTransportFromObservation(row.Observation);
            result.Add(new AuthoritativeCommissionRow(
                "C-POS-" + row.OperationFolio + "-" + row.Ticket,
                row.OperationFolio,
                row.Ticket,
                row.Date,
                string.Empty,
                row.Observation,
                transport,
                row.Total,
                row.Joyeria ? 0m : row.Total,
                row.Joyeria ? row.Total : 0m,
                string.Empty,
                0,
                string.Empty,
                string.Empty,
                row.Observation,
                row.Observation,
                0m,
                0m,
                string.Empty,
                breakdown ?? new TicketPaymentBreakdown(row.Total, 0m, 0m, "SIN PAGO"),
                expense,
                ResolveTransportInfo(transportCatalog, transport, row.Date),
                row.Subtotal > 0m ? row.Subtotal : row.Total));
        }
        return result;
    }

    private static async Task<List<StoreOnlyTicketRow>> ReadStoreOnlyObservationRowsAsync(SqlConnection connection, string folioColumn, string ticketColumn, string search, DateTime? start, DateTime? end, bool joyeria)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.Parameters.AddWithValue("@search", search.Trim());
        command.Parameters.AddWithValue("@searchLike", "%" + search.Trim() + "%");
        var filters = new List<string>
        {
            $"(CAST({ticketColumn} AS nvarchar(80)) = @search OR CAST({folioColumn} AS nvarchar(60)) = @search OR UPPER(COALESCE(CONVERT(nvarchar(max), observaciones), '')) LIKE UPPER(@searchLike))",
            "UPPER(LTRIM(RTRIM(COALESCE(estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')"
        };
        if (start.HasValue)
        {
            command.Parameters.AddWithValue("@storeOnlyStart", start.Value.Date);
            filters.Add("CAST(COALESCE(fecha, GETDATE()) AS date) >= @storeOnlyStart");
        }
        if (end.HasValue)
        {
            command.Parameters.AddWithValue("@storeOnlyEnd", end.Value.Date);
            filters.Add("CAST(COALESCE(fecha, GETDATE()) AS date) <= @storeOnlyEnd");
        }

        var subtotalExpression = joyeria ? "total" : "COALESCE(stotal, total)";
        command.CommandText = $"""
            SELECT
                CAST({ticketColumn} AS nvarchar(80)) AS Ticket,
                CAST({folioColumn} AS nvarchar(60)) AS FolioOperacion,
                COALESCE(fecha, GETDATE()) AS Fecha,
                CAST(total AS decimal(18,2)) AS Total,
                CAST({subtotalExpression} AS decimal(18,2)) AS Subtotal,
                LTRIM(RTRIM(COALESCE(CONVERT(nvarchar(300), observaciones), ''))) AS Observaciones
            FROM dbo.remisioM WITH (NOLOCK)
            WHERE {string.Join(" AND ", filters)};
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<StoreOnlyTicketRow>();
        while (await reader.ReadAsync())
        {
            result.Add(new StoreOnlyTicketRow(
                Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(2) ? DateTime.Today : Convert.ToDateTime(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty,
                joyeria));
        }
        return result;
    }

    private static string InferTransportFromObservation(string? observation)
    {
        var text = observation ?? string.Empty;
        if (text.Contains("GUIA", StringComparison.OrdinalIgnoreCase) || text.Contains("QUENO", StringComparison.OrdinalIgnoreCase))
            return "GUIA";
        if (text.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase))
            return "MAJESTIC";
        if (text.Contains("SALMORAN", StringComparison.OrdinalIgnoreCase))
            return "SALMORAN";
        if (text.Contains("TURIBUS", StringComparison.OrdinalIgnoreCase))
            return "TURIBUS";
        return string.Empty;
    }

    // Version por lotes de LoadStoreTicketsForKeysAsync: en vez de 2 consultas por registro
    // (una a compuadmo y otra a joyeria), junta los folios y tickets de TODOS los registros y
    // hace unas pocas consultas. Con un rango de fechas amplio esto pasa de cientos de idas y
    // vueltas a SQL Server a un puñado. El resultado se reparte de vuelta por registro usando
    // el folio que hizo match, que ahora viene en el SELECT.
    private static async Task<List<StoreTicketRow>?[]> LoadStoreTicketsForAllRowsAsync(
        SqlConnection compuConnection,
        SqlConnection joyeriaConnection,
        IReadOnlyList<AuthoritativeAppRow> appRows)
    {
        var matches = new List<StoreTicketMatch>();
        matches.AddRange(await ReadStoreTicketMatchesAsync(compuConnection, "folioregistro", "folio_remision", appRows, false));
        matches.AddRange(await ReadStoreTicketMatchesAsync(joyeriaConnection, "folio_registro", "COALESCE(folio_pedido, folio_factura)", appRows, true));

        var byFolio = new Dictionary<string, List<StoreTicketMatch>>(StringComparer.OrdinalIgnoreCase);
        var byTicket = new Dictionary<string, List<StoreTicketMatch>>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches)
        {
            if (!string.IsNullOrWhiteSpace(match.MatchFolio))
            {
                if (!byFolio.TryGetValue(match.MatchFolio, out var folioBucket))
                    byFolio[match.MatchFolio] = folioBucket = [];
                folioBucket.Add(match);
            }
            if (!byTicket.TryGetValue(match.Ticket, out var ticketBucket))
                byTicket[match.Ticket] = ticketBucket = [];
            ticketBucket.Add(match);
        }

        // Indexado por posicion en appRows: dos registros distintos pueden compartir
        // FolioOperacion, asi que ese campo no sirve como llave.
        var result = new List<StoreTicketRow>?[appRows.Count];
        for (var i = 0; i < appRows.Count; i++)
        {
            var appRow = appRows[i];
            // Una misma fila fisica de remisioM puede llegar por folio y por ticket a la vez;
            // el Id evita sumarla dos veces (la consulta original la traia una sola vez).
            var seen = new HashSet<long>();
            var rows = new List<StoreTicketMatch>();
            foreach (var key in appRow.Keys)
            {
                if (!byFolio.TryGetValue(key, out var bucket)) continue;
                foreach (var match in bucket)
                    if (seen.Add(match.Id)) rows.Add(match);
            }
            if (!string.IsNullOrWhiteSpace(appRow.PosFolio) && byTicket.TryGetValue(appRow.PosFolio, out var ticketBucket))
            {
                foreach (var match in ticketBucket)
                    if (seen.Add(match.Id)) rows.Add(match);
            }
            if (rows.Count == 0) continue;

            result[i] = rows
                .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
                .Select(g => new StoreTicketRow(
                    g.Key,
                    g.Sum(x => x.Total),
                    g.Sum(x => x.VentaTienda),
                    g.Sum(x => x.VentaJoyeria)))
                .ToList();
        }
        return result;
    }

    private static async Task<List<StoreTicketMatch>> ReadStoreTicketMatchesAsync(
        SqlConnection connection,
        string folioColumn,
        string ticketColumn,
        IReadOnlyList<AuthoritativeAppRow> appRows,
        bool joyeria)
    {
        var keys = appRows.SelectMany(x => x.Keys).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var tickets = appRows.Select(x => x.PosFolio).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Se consulta por folio y por ticket en viajes distintos, asi que una misma fila fisica
        // puede volver en ambos lotes. remisioM no expone aqui una llave unica, de modo que se
        // cuentan las filas identicas por lado y se toma el MAXIMO, no la suma: eso reproduce
        // exactamente lo que devolvia la consulta original con un solo OR (cada fila una vez).
        var fromKeys = new List<StoreTicketMatch>();
        var fromTickets = new List<StoreTicketMatch>();

        // SQL Server topa en 2100 parametros por comando; se parte en bloques holgados.
        const int chunkSize = 800;
        for (var offset = 0; offset < keys.Length; offset += chunkSize)
        {
            var chunk = keys.Skip(offset).Take(chunkSize).ToArray();
            await ReadChunkAsync($"CAST({folioColumn} AS nvarchar(60))", "@key", chunk, fromKeys);
        }
        for (var offset = 0; offset < tickets.Length; offset += chunkSize)
        {
            var chunk = tickets.Skip(offset).Take(chunkSize).ToArray();
            await ReadChunkAsync($"CAST({ticketColumn} AS nvarchar(80))", "@tk", chunk, fromTickets);
        }

        var result = new List<StoreTicketMatch>();
        var nextId = 0L;
        var ticketGroups = fromTickets
            .GroupBy(BuildStoreTicketMatchKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var group in fromKeys.GroupBy(BuildStoreTicketMatchKey, StringComparer.OrdinalIgnoreCase))
        {
            ticketGroups.Remove(group.Key, out var alsoFromTickets);
            var copies = Math.Max(group.Count(), alsoFromTickets?.Count ?? 0);
            var template = group.First();
            for (var i = 0; i < copies; i++)
                result.Add(template with { Id = nextId++ });
        }
        foreach (var group in ticketGroups.Values)
        {
            foreach (var match in group)
                result.Add(match with { Id = nextId++ });
        }
        return result;

        async Task ReadChunkAsync(string matchExpression, string parameterPrefix, IReadOnlyList<string> values, List<StoreTicketMatch> sink)
        {
            if (values.Count == 0) return;
            await using var command = connection.CreateCommand();
            var parameters = new List<string>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var parameter = parameterPrefix + i.ToString(CultureInfo.InvariantCulture);
                command.Parameters.AddWithValue(parameter, values[i]);
                parameters.Add(parameter);
            }
            command.CommandText = $"""
                SELECT CAST({folioColumn} AS nvarchar(60)) AS MatchFolio,
                       CAST({ticketColumn} AS nvarchar(80)) AS Ticket,
                       CAST(total AS decimal(18,2)) AS Total
                FROM dbo.remisioM
                WHERE {matchExpression} IN ({string.Join(",", parameters)})
                  AND UPPER(LTRIM(RTRIM(COALESCE(estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA');
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var total = Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture);
                sink.Add(new StoreTicketMatch(
                    0,
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.GetString(1),
                    total,
                    joyeria ? 0m : total,
                    joyeria ? total : 0m));
            }
        }
    }

    private static string BuildStoreTicketMatchKey(StoreTicketMatch match)
        => string.Create(CultureInfo.InvariantCulture, $"{match.MatchFolio}|{match.Ticket}|{match.Total:0.00}");

    private async Task<List<StoreTicketRow>> LoadStoreTicketsForKeysAsync(
        SqlConnection compuConnection,
        SqlConnection joyeriaConnection,
        IReadOnlyList<string> keys,
        string? exactTicket,
        string? driverName,
        DateTime operationDate,
        bool allowObservationFallback)
    {
        var result = new List<StoreTicketRow>();
        result.AddRange(await ReadStoreTicketsAsync(compuConnection, "folioregistro", "folio_remision", keys, exactTicket, driverName, operationDate, false, allowObservationFallback));
        result.AddRange(await ReadStoreTicketsAsync(joyeriaConnection, "folio_registro", "COALESCE(folio_pedido, folio_factura)", keys, exactTicket, driverName, operationDate, true, allowObservationFallback));
        return result
            .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StoreTicketRow(
                g.Key,
                g.Sum(x => x.Total),
                g.Sum(x => x.VentaTienda),
                g.Sum(x => x.VentaJoyeria)))
            .ToList();
    }

    private static async Task<List<StoreTicketRow>> ReadStoreTicketsAsync(
        SqlConnection connection,
        string folioColumn,
        string ticketColumn,
        IReadOnlyList<string> keys,
        string? exactTicket,
        string? driverName,
        DateTime operationDate,
        bool joyeria,
        bool allowObservationFallback)
    {
        if (keys.Count == 0 && string.IsNullOrWhiteSpace(exactTicket)) return [];
        var directFilters = new List<string>();
        var index = 0;
        var parameters = new List<(string Name, object Value)>();
        if (keys.Count > 0)
        {
            var keyParameters = new List<string>();
            foreach (var key in keys)
            {
                var parameter = "@key" + index++;
                keyParameters.Add(parameter);
                parameters.Add((parameter, key));
            }
            directFilters.Add($"CAST({folioColumn} AS nvarchar(60)) IN ({string.Join(",", keyParameters)})");
        }
        if (!string.IsNullOrWhiteSpace(exactTicket))
        {
            parameters.Add(("@ticket", exactTicket));
            directFilters.Add($"CAST({ticketColumn} AS nvarchar(80)) = @ticket");
        }

        var direct = await ReadStoreTicketsWithFiltersAsync(connection, ticketColumn, directFilters, parameters, joyeria);
        if (direct.Count > 0 || !allowObservationFallback || string.IsNullOrWhiteSpace(driverName))
            return direct;

        return await ReadStoreTicketsByObservationAsync(connection, ticketColumn, driverName, operationDate, joyeria);
    }

    private static string BuildStoreTicketMergeKey(StoreTicketRow row)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{row.Ticket}|{row.Total:0.00}|{row.VentaTienda:0.00}|{row.VentaJoyeria:0.00}");

    private static async Task<List<StoreTicketRow>> ReadStoreTicketsByObservationAsync(
        SqlConnection connection,
        string ticketColumn,
        string driverName,
        DateTime operationDate,
        bool joyeria)
    {
        var normalizedDriver = driverName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDriver))
            return [];

        await using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@driverLike", "%" + normalizedDriver + "%");
        command.Parameters.AddWithValue("@opDate", operationDate.Date);
        command.CommandText = $"""
            SELECT
                CAST({ticketColumn} AS nvarchar(80)) AS Ticket,
                CAST(total AS decimal(18,2)) AS Total,
                UPPER(LTRIM(RTRIM(COALESCE(CONVERT(nvarchar(max), observaciones), '')))) AS ObservationText
            FROM dbo.remisioM
            WHERE UPPER(COALESCE(CONVERT(nvarchar(max), observaciones), '')) LIKE UPPER(@driverLike)
              AND CAST(COALESCE(fecha, GETDATE()) AS date) = @opDate
              AND UPPER(LTRIM(RTRIM(COALESCE(estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA');
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var rawRows = new List<(string Ticket, decimal Total, string ObservationText)>();
        while (await reader.ReadAsync())
        {
            var total = Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture);
            rawRows.Add((
                reader.GetString(0),
                total,
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2)));
        }

        if (rawRows.Count == 0)
            return [];

        var distinctObservationGroups = rawRows
            .Select(x => x.ObservationText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctObservationGroups.Length != 1)
            return [];

        var result = new List<StoreTicketRow>(rawRows.Count);
        foreach (var row in rawRows)
        {
            result.Add(new StoreTicketRow(
                row.Ticket,
                row.Total,
                joyeria ? 0m : row.Total,
                joyeria ? row.Total : 0m));
        }

        return result;
    }

    private static async Task<List<StoreTicketRow>> ReadStoreTicketsWithFiltersAsync(SqlConnection connection, string ticketColumn, IReadOnlyList<string> filters, IReadOnlyList<(string Name, object Value)> parameters, bool joyeria)
    {
        if (filters.Count == 0) return [];
        await using var command = connection.CreateCommand();
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);

        command.CommandText = $"""
            SELECT CAST({ticketColumn} AS nvarchar(80)) AS Ticket, CAST(total AS decimal(18,2)) AS Total
            FROM dbo.remisioM
            WHERE ({string.Join(" OR ", filters)})
              AND UPPER(LTRIM(RTRIM(COALESCE(estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<StoreTicketRow>();
        while (await reader.ReadAsync())
        {
            var total = Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture);
            result.Add(new StoreTicketRow(
                reader.GetString(0),
                total,
                joyeria ? 0m : total,
                joyeria ? total : 0m));
        }
        return result;
    }

    private static bool IsCommissionableStoreTicket(string ticket) => !string.IsNullOrWhiteSpace(ticket);

    private static void ApplyLargestPayoutToHighestSale(List<AuthoritativeCommissionRow> rows)
    {
        foreach (var group in rows.GroupBy(x => x.OperationFolio, StringComparer.OrdinalIgnoreCase))
        {
            var highest = group.OrderByDescending(x => x.SaleTotal).ThenBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (highest is null) continue;
            var payout = group.Max(x => x.GroupPayout);
            foreach (var row in group) row.PayoutDeduction = 0m;
            highest.PayoutDeduction = payout;
        }
    }

    private static decimal CalculateAuthoritativeCommission(AuthoritativeCommissionRow row)
    {
        var fixedCommission = ResolveAuthoritativeFixedCommission(row);
        if (fixedCommission > 0m)
            return fixedCommission;

        var percentage = ResolveAuthoritativePercentage(row);
        if (percentage <= 0m)
            return 0m;

        var deductions = row.PayoutDeduction + CalculateExpenseDeductions(row.TransportType, row.Expenses);
        var saleTotal = row.CommissionableTotal > 0m ? row.CommissionableTotal : row.SaleTotal;
        var payments = row.Payments;
        var netAfterDiscount = CalculateAuthoritativeNetAfterDiscount(row, saleTotal, payments);

        return Math.Max(0m, decimal.Truncate(Math.Max(0m, netAfterDiscount - deductions) * (percentage / 100m)));
    }

    private static void ApplyStoreOnlyOperationGroupCommissions(List<AuthoritativeCommissionRow> rows)
    {
        foreach (var group in rows
            .Where(row => row.LocalFolio.StartsWith("C-POS-", StringComparison.OrdinalIgnoreCase))
            .GroupBy(row => row.OperationFolio, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            var items = group.ToArray();
            var percentage = ResolveAuthoritativePercentage(items[0]);
            if (percentage <= 0m) continue;

            var baseTotal = items.Sum(row =>
            {
                var saleTotal = row.CommissionableTotal > 0m ? row.CommissionableTotal : row.SaleTotal;
                var deductions = row.PayoutDeduction + CalculateExpenseDeductions(row.TransportType, row.Expenses);
                return Math.Max(0m, CalculateAuthoritativeNetAfterDiscount(row, saleTotal, row.Payments) - deductions);
            });
            var groupCommission = Math.Max(0m, decimal.Truncate(baseTotal * (percentage / 100m)));
            var target = items.OrderByDescending(row => row.SaleTotal).ThenBy(row => row.Ticket, StringComparer.OrdinalIgnoreCase).First();
            foreach (var row in items) row.CommissionAmount = 0m;
            target.CommissionAmount = groupCommission;
        }
    }

    private static IReadOnlyList<LocalCommissionDiagnosticRow> BuildCommissionDiagnostics(IEnumerable<AuthoritativeCommissionRow> rows, string source)
    {
        return rows
            .OrderBy(row => row.Date)
            .ThenBy(row => row.Ticket, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                var cardRetentionPercent = ResolveAuthoritativeDiscount(row, PaymentKind.Card);
                var amexRetentionPercent = ResolveAuthoritativeDiscount(row, PaymentKind.Amex);
                var cardRetention = row.Payments.Card * (CommissionPaymentRules.NormalizePercent(cardRetentionPercent) / 100m);
                var amexRetention = row.Payments.Amex * (CommissionPaymentRules.NormalizePercent(amexRetentionPercent) / 100m);
                var deductions = row.PayoutDeduction + CalculateExpenseDeductions(row.TransportType, row.Expenses);
                var saleTotal = row.CommissionableTotal > 0m ? row.CommissionableTotal : row.SaleTotal;
                var baseCommission = Math.Max(0m, CalculateAuthoritativeNetAfterDiscount(row, saleTotal, row.Payments) - deductions);
                return new LocalCommissionDiagnosticRow(
                    row.OperationFolio,
                    row.Ticket,
                    row.Date,
                    row.SaleTotal,
                    row.Badge,
                    row.TransportType,
                    row.DriverName,
                    row.Hotel,
                    row.Payments.Description,
                    row.Payments.NonCard,
                    row.Payments.Card,
                    row.Payments.Amex,
                    cardRetention,
                    amexRetention,
                    row.PayoutDeduction,
                    CalculateExpenseDeductions(row.TransportType, row.Expenses),
                    ResolveAuthoritativePercentage(row),
                    baseCommission,
                    row.CommissionAmount,
                    source);
            })
            .ToArray();
    }

    private static decimal CalculateAuthoritativeNetAfterDiscount(AuthoritativeCommissionRow row, decimal saleTotal, TicketPaymentBreakdown payments)
    {
        if (saleTotal <= 0m)
            return 0m;

        if (payments.Total <= 0m)
        {
            var discount = IsAmexPayment(payments.Description)
                ? ResolveAuthoritativeDiscount(row, PaymentKind.Amex)
                : IsCardPayment(payments.Description)
                    ? ResolveAuthoritativeDiscount(row, PaymentKind.Card)
                    : ResolveAuthoritativeDiscount(row, PaymentKind.Cash);
            return saleTotal - saleTotal * (discount / 100m);
        }

        // pagosM can contain only part of a ticket payment. Do not scale a partial
        // card payment to the full sale, because that invents extra card discount.
        var scale = payments.Total > saleTotal ? saleTotal / payments.Total : 1m;
        var efectivo = payments.NonCard * scale;
        var tarjeta = payments.Card * scale;
        var amex = payments.Amex * scale;
        var allocated = Math.Min(saleTotal, efectivo + tarjeta + amex);
        var unresolved = Math.Max(0m, saleTotal - allocated);

        return unresolved
            + NetAfterDiscount(efectivo, ResolveAuthoritativeDiscount(row, PaymentKind.Cash))
            + NetAfterDiscount(tarjeta, ResolveAuthoritativeDiscount(row, PaymentKind.Card))
            + NetAfterDiscount(amex, ResolveAuthoritativeDiscount(row, PaymentKind.Amex));
    }

    private static decimal NetAfterDiscount(decimal amount, decimal discountPercent) =>
        amount <= 0m ? 0m : amount - amount * (NormalizePercentValue(discountPercent) / 100m);

    private static decimal CalculateExpenseDeductions(string? transportType, StoreExpenseBreakdown expense)
    {
        var duplicateSalmoranExpense = IsSalmoranAuthoritative(transportType)
            && expense.GastosVarios > 0m
            && expense.GastosVarios == expense.Degustacion;
        var deductions = expense.Dejada;
        deductions += duplicateSalmoranExpense ? 0m : expense.GastosVarios;
        deductions += expense.Degustacion;
        deductions += expense.Reparacion;
        deductions += expense.Bebidas;
        deductions += expense.CajasRegalo;
        return deductions;
    }

    private static decimal ResolveAuthoritativeFixedCommission(AuthoritativeCommissionRow row)
    {
        var commission = NormalizePercentValue(row.TransportInfo.CommissionPercent);
        if (commission > 100m)
            return commission;
        if (commission <= 0m)
            return FirstFixed(row.TransportInfo.Maximum, row.TransportInfo.Minimum);
        return 0m;
    }

    private static decimal ResolveAuthoritativePercentage(AuthoritativeCommissionRow row)
    {
        var catalogPercent = NormalizePercentValue(row.TransportInfo.CommissionPercent);
        if (catalogPercent > 0m && catalogPercent <= 100m)
            return catalogPercent;
        if (IsMajesticAuthoritative(row.TransportType)) return 8m;
        if (IsSalmoranAuthoritative(row.TransportType)) return 20m;
        return 10m;
    }

    private async Task<IReadOnlyList<ImportedTransportCatalogRow>> ReadConfiguredTransportCatalogRowsAsync()
    {
        var rules = await _commissionSettings.GetRulesAsync("TRANSPORTE");
        return rules
            .Where(x => x.Active)
            .Select(x => new ImportedTransportCatalogRow(
                x.Code,
                x.Name,
                x.CashRetentionPercent,
                x.CardRetentionPercent,
                x.CommissionPercent,
                0m,
                0m,
                x.AmexRetentionPercent,
                x.EffectiveFrom,
                x.EffectiveTo,
                true))
            .ToArray();
    }

    private static ImportedTransportInfo ResolveTransportInfo(IReadOnlyList<ImportedTransportCatalogRow> catalog, string? transportType, DateTime operationDate)
    {
        if (catalog.Count == 0 || string.IsNullOrWhiteSpace(transportType))
            return ImportedTransportInfo.Empty;

        var key = ResolveTransportAlias(NormalizeTransportLookup(transportType));
        var effectiveCatalog = catalog
            .Where(row => IsTransportRuleEffective(row, operationDate))
            .OrderByDescending(row => row.Configured)
            .ThenByDescending(row => row.EffectiveFrom ?? DateTime.MinValue)
            .ToArray();

        var match = effectiveCatalog.FirstOrDefault(row =>
            string.Equals(NormalizeTransportLookup(row.Type), key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeTransportLookup(row.Name), key, StringComparison.OrdinalIgnoreCase));

        match ??= effectiveCatalog.FirstOrDefault(row =>
            key.Contains(NormalizeTransportLookup(row.Type), StringComparison.OrdinalIgnoreCase)
            || key.Contains(NormalizeTransportLookup(row.Name), StringComparison.OrdinalIgnoreCase)
            || NormalizeTransportLookup(row.Type).Contains(key, StringComparison.OrdinalIgnoreCase)
            || NormalizeTransportLookup(row.Name).Contains(key, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return ImportedTransportInfo.Empty;

        return new ImportedTransportInfo(
            CommissionPaymentRules.NormalizePercent(match.CashDiscount),
            CommissionPaymentRules.ResolveCardRetention(match.CardDiscount),
            CommissionPaymentRules.NormalizePercent(match.CommissionPercent),
            match.Minimum,
            match.Maximum,
            CommissionPaymentRules.ResolveAmexRetention(match.AmexDiscount));
    }

    private static bool IsTransportRuleEffective(ImportedTransportCatalogRow row, DateTime operationDate)
    {
        if (!row.Configured) return true;
        var date = operationDate.Date;
        return (row.EffectiveFrom is null || row.EffectiveFrom.Value.Date <= date)
            && (row.EffectiveTo is null || row.EffectiveTo.Value.Date >= date);
    }

    private static string NormalizeTransportLookup(string? value)
    {
        var text = value ?? string.Empty;
        return new string(text
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    // Errores de captura conocidos en el punto de venta (confirmados por el usuario 2026-08-19):
    // la unidad real llega mal escrita y sin este alias caeria al valor por defecto en vez de
    // usar la comision configurada de la unidad correcta. Claves ya normalizadas (sin espacios,
    // mayusculas) con NormalizeTransportLookup.
    private static readonly Dictionary<string, string> TransportLookupAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TAXOVERDE"] = "TAXIVERDENACIONAL",
        ["VANTRANSPOTADORA"] = "VANTRANSPORTADORAS",
        ["VANTRASNPOTADORA"] = "VANTRANSPORTADORAS",
    };

    private static string ResolveTransportAlias(string normalizedKey) =>
        TransportLookupAliases.TryGetValue(normalizedKey, out var canonical) ? canonical : normalizedKey;

    private static decimal ResolveAuthoritativeDiscount(AuthoritativeCommissionRow row, PaymentKind kind)
    {
        var catalog = kind switch
        {
            PaymentKind.Cash => row.TransportInfo.CashDiscount,
            PaymentKind.Amex => row.TransportInfo.AmexDiscount,
            _ => row.TransportInfo.CardDiscount
        };
        if (kind == PaymentKind.Amex)
            return CommissionPaymentRules.ResolveAmexRetention(catalog);
        if (catalog > 0m)
            return CommissionPaymentRules.NormalizePercent(catalog);
        if (IsSalmoranAuthoritative(row.TransportType))
        {
            return kind switch
            {
                PaymentKind.Cash => 16m,
                PaymentKind.Amex => CommissionPaymentRules.AmexRetentionPercent,
                _ => CommissionPaymentRules.CardRetentionPercent
            };
        }
        if (kind == PaymentKind.Card)
            return CommissionPaymentRules.CardRetentionPercent;
        return CommissionPaymentRules.CashRetentionPercent;
    }

    private static decimal FirstFixed(params decimal[] values) =>
        values.FirstOrDefault(value => value > 0m && value <= 1000m);

    private static bool IsMajesticAuthoritative(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TRAVEL EXPERIENCE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MAESTIC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSalmoranAuthoritative(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains("SALMORAN", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAmexPayment(string? paymentName) => CommissionPaymentRules.IsAmexPayment(paymentName);

    private static bool IsCardPayment(string? paymentName) => CommissionPaymentRules.IsCardPayment(paymentName);

    private static void ApplyCommissionPayments(List<AuthoritativeCommissionRow> rows)
    {
        foreach (var group in rows.GroupBy(x => x.OperationFolio, StringComparer.OrdinalIgnoreCase))
        {
            var totalCommission = group.Sum(x => x.CommissionAmount);
            var controlPaid = group.Max(x => x.GroupCommissionPaid);
            var hasPaidDate = group.Any(x => !string.IsNullOrWhiteSpace(x.GroupCommissionPaidDate));
            var remaining = controlPaid > 0m ? controlPaid : hasPaidDate ? totalCommission : 0m;
            foreach (var row in group.OrderByDescending(x => x.CommissionAmount))
            {
                row.PaidAmount = Math.Min(row.CommissionAmount, Math.Max(remaining, 0m));
                remaining -= row.PaidAmount;
            }
        }
    }

    private static IEnumerable<LocalCommissionBrowserRow> FilterCommissionBrowserRows(IEnumerable<LocalCommissionBrowserRow> rows, string? search, DateTime? start, DateTime? end)
    {
        var normalized = search?.Trim();
        var startDate = start?.Date;
        var endDate = end?.Date;
        return rows
            .Where(row =>
                (!startDate.HasValue || row.Fecha.Date >= startDate.Value)
                && (!endDate.HasValue || row.Fecha.Date <= endDate.Value)
                && (string.IsNullOrWhiteSpace(normalized)
                    || ContainsIgnoreCase(row.Folio, normalized)
                    || ContainsIgnoreCase(row.SaleFolio, normalized)
                    || ContainsIgnoreCase(row.Ticket, normalized)
                    || ContainsIgnoreCase(row.Nombre, normalized)
                    || ContainsIgnoreCase(row.Hotel, normalized)
                    || ContainsIgnoreCase(row.Gafete, normalized)))
            .OrderByDescending(row => row.Fecha)
            .ThenByDescending(row => row.Folio, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<LocalSalesBrowserRow>> ReadSalesFromStoreAsync(SqlConnection connection, bool joyeria, string normalized, string numeric, string prefixedTicket, long? folioNumber)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = joyeria
            ? """
                SELECT TOP (30)
                  CONVERT(nvarchar(30), COALESCE(folio_pedido, folio_factura)) AS Folio,
                  CONVERT(nvarchar(30), folio_factura) AS Factura,
                  fecha AS Fecha,
                  COALESCE(cliente, '') AS Cliente,
                  CONVERT(nvarchar(30), COALESCE(vendedor, 0)) AS Vendedor,
                  COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS Taxista,
                  COALESCE(usuario, '') AS Usuario,
                  CONVERT(nvarchar(30), folio_registro) AS FolioRegistro,
                  COALESCE(stotal, 0) AS Subtotal,
                  COALESCE(iva, 0) AS Iva,
                  COALESCE(total, 0) AS Total,
                  CAST(0 AS decimal(18,2)) AS Efectivo,
                  CAST(0 AS decimal(18,2)) AS Tarjeta,
                  CAST(0 AS decimal(18,2)) AS Dolares,
                  COALESCE(tipo_cambio, 0) AS TipoCambio
                FROM dbo.remisioM
                WHERE UPPER(LTRIM(RTRIM(COALESCE(estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
                  AND (
                       (@folioNumber IS NOT NULL AND (folio_operacion = @folioNumber OR folio_registro = @folioNumber))
                    OR UPPER(CONVERT(nvarchar(30), COALESCE(folio_pedido, folio_factura))) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
                    OR UPPER(CONVERT(nvarchar(30), folio_factura)) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
                    OR UPPER(COALESCE(cliente, '')) LIKE UPPER(@searchLike)
                    OR UPPER(COALESCE(CONVERT(nvarchar(max), observaciones), '')) LIKE UPPER(@searchLike)
                  )
                ORDER BY fecha DESC;
                """
            : """
                SELECT TOP (30)
                  CONVERT(nvarchar(30), folio_remision) AS Folio,
                  CONVERT(nvarchar(30), folio_factura) AS Factura,
                  fecha AS Fecha,
                  COALESCE(cliente, '') AS Cliente,
                  COALESCE(NULLIF(vendedor, ''), '') AS Vendedor,
                  COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS Taxista,
                  COALESCE(usuario, '') AS Usuario,
                  CONVERT(nvarchar(30), folioregistro) AS FolioRegistro,
                  COALESCE(stotal, 0) AS Subtotal,
                  COALESCE(iva, 0) AS Iva,
                  COALESCE(total, 0) AS Total,
                  COALESCE(efectivo, 0) AS Efectivo,
                  COALESCE(tarjeta, 0) AS Tarjeta,
                  COALESCE(dolares, 0) AS Dolares,
                  COALESCE(tipo_cambio, 0) AS TipoCambio
                FROM dbo.remisioM
                WHERE UPPER(LTRIM(RTRIM(COALESCE(estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
                  AND (
                       (@folioNumber IS NOT NULL AND (folio_operacion = @folioNumber OR folioregistro = @folioNumber))
                    OR UPPER(CONVERT(nvarchar(30), folio_remision)) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
                    OR UPPER(CONVERT(nvarchar(30), folio_factura)) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
                    OR UPPER(COALESCE(cliente, '')) LIKE UPPER(@searchLike)
                    OR UPPER(COALESCE(CONVERT(nvarchar(max), observaciones), '')) LIKE UPPER(@searchLike)
                  )
                ORDER BY fecha DESC;
                """;
        command.Parameters.AddWithValue("@folio", numeric);
        command.Parameters.AddWithValue("@ticket", normalized);
        command.Parameters.AddWithValue("@prefixedTicket", prefixedTicket);
        command.Parameters.AddWithValue("@folioNumber", folioNumber ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@searchLike", "%" + normalized + "%");

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<LocalSalesBrowserRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new LocalSalesBrowserRow(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? DateTime.Today : Convert.ToDateTime(reader.GetValue(2), CultureInfo.InvariantCulture),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                string.Empty,
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                string.Empty,
                string.Empty,
                0,
                Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(11), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(12), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(13), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(14), CultureInfo.InvariantCulture),
                joyeria ? "JOY" : "COMP"));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<LocalSalesTicketRow>> ReadSalesTicketRowsFromStoreAsync(SqlConnection connection, LocalSalesBrowserRow sale, bool joyeria, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return [];

        await using var command = connection.CreateCommand();
        var parameters = new List<string>();
        for (var i = 0; i < keys.Count; i++)
        {
            var parameter = "@key" + i.ToString(CultureInfo.InvariantCulture);
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, keys[i]);
        }

        var folioColumn = joyeria ? "m.folio_registro" : "m.folioregistro";
        var ticketColumn = joyeria ? "COALESCE(m.folio_pedido, m.folio_factura)" : "m.folio_remision";
        var invoiceColumn = "m.folio_factura";
        command.CommandText = $"""
            SELECT
              COALESCE(TRY_CONVERT(int, d.cantidads), 1) AS Cantidad,
              COALESCE(NULLIF(d.producto, ''), NULLIF(d.descripcion_larga, ''), 'SIN DESCRIPCION') AS Producto,
              COALESCE(NULLIF(d.deportiva, ''), NULLIF(d.categoria, ''), '') AS Departamento,
              COALESCE(d.p_unitario, 0) AS Precio,
              COALESCE(d.stotal, 0) AS Importe,
              CONVERT(nvarchar(80), {ticketColumn}) AS Referencia
            FROM dbo.remisioD d
            INNER JOIN dbo.remisioM m
              ON d.folio_registro = {folioColumn}
            WHERE (
                   CAST({folioColumn} AS nvarchar(80)) IN ({string.Join(",", parameters)})
                OR UPPER(CONVERT(nvarchar(80), {ticketColumn})) IN ({string.Join(",", parameters.Select(x => "UPPER(" + x + ")"))})
                OR UPPER(CONVERT(nvarchar(80), {invoiceColumn})) IN ({string.Join(",", parameters.Select(x => "UPPER(" + x + ")"))})
              )
              AND UPPER(LTRIM(RTRIM(COALESCE(m.estatus, '')))) NOT IN ('C', 'CANCELADO', 'CANCELADA')
            ORDER BY d.id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<LocalSalesTicketRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new LocalSalesTicketRow(
                reader.IsDBNull(0) ? 1 : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? "SIN DESCRIPCION" : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? sale.Folio : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? sale.Folio,
                sale.Factura,
                sale.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                sale.Gafete,
                sale.OrigenVenta));
        }

        return rows;
    }

    private static LocalSalesTicketRow BuildFallbackSalesTicketRow(LocalSalesBrowserRow sale) =>
        new(
            1,
            string.IsNullOrWhiteSpace(sale.Cliente) ? $"VENTA {sale.Folio}" : sale.Cliente,
            string.IsNullOrWhiteSpace(sale.OrigenVenta) ? "VENTA" : sale.OrigenVenta,
            sale.Subtotal,
            sale.Total,
            sale.Folio,
            sale.Factura,
            sale.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            sale.Gafete,
            sale.OrigenVenta);

    private static List<string> SplitSearchTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split([',', ';', '|', '/', '\\', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static LocalCommissionBrowserRow MapAuthoritativeCommissionRow(AuthoritativeCommissionRow row)
    {
        var paymentText = string.IsNullOrWhiteSpace(row.Payments.Description)
            ? (row.Payments.Card > 0m || row.Payments.Amex > 0m ? "TARJETA" : "EFECTIVO")
            : row.Payments.Description;
        var vendor = string.IsNullOrWhiteSpace(row.Vendedor)
            ? string.Empty
            : row.Vendedor.Trim();
        return new LocalCommissionBrowserRow(
            row.LocalFolio,
            row.OperationFolio,
            row.Date,
            row.TransportType,
            row.UnitNumber,
            string.IsNullOrWhiteSpace(row.Staff) ? row.DriverName : row.Staff,
            row.Passengers,
            row.Hotel,
            0m,
            0m,
            row.SaleStore,
            row.SaleJewelry,
            row.SaleTotal,
            row.Ticket,
            paymentText,
            CalculateDiscountPercent(row),
            row.PayoutDeduction,
            row.Expenses.Bebidas + row.Expenses.CajasRegalo,
            row.Expenses.Reparacion,
            row.Expenses.Degustacion,
            ResolveAuthoritativePercentage(row) / 100m,
            row.CommissionAmount,
            row.PaidAmount,
            Math.Max(row.CommissionAmount - row.PaidAmount, 0m),
            ResolveCommissionStatus(row.CommissionAmount, row.PaidAmount),
            vendor,
            row.Badge,
            // Este es el mapeo que usa la ruta principal (SQL Server). Faltaba propagar el
            // estatus de la dejada y por eso la columna salia vacia aunque el dato si venia
            // en la consulta. Las comisiones que no nacen de un registro de la app movil no
            // tienen dejada asociada: se marcan como SIN DEJADA en vez de dejarse en blanco.
            string.IsNullOrWhiteSpace(row.PayoutStatus) ? "SIN DEJADA" : row.PayoutStatus);
    }

    private static LocalCommissionBrowserRow MapRelationCommissionRow(LocalRelation relation)
    {
        var sale = relation.Sale;
        var primaryTicket = SplitSearchTokens(relation.PosFolio).FirstOrDefault() ?? string.Empty;
        var date = DateTime.TryParse(relation.DateText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : DateTime.Today;
        var name = string.IsNullOrWhiteSpace(relation.Driver) ? relation.Vendor : relation.Driver;
        var status = string.IsNullOrWhiteSpace(relation.CommissionStatus)
            ? ResolveCommissionStatus(relation.Commission, relation.CommissionPaid)
            : relation.CommissionStatus;

        return new LocalCommissionBrowserRow(
            BuildRelationCommissionFolio(relation, primaryTicket),
            relation.OperationFolio,
            date,
            relation.TransportType,
            relation.Unit,
            name,
            relation.Passengers,
            relation.Hotel,
            0m,
            0m,
            sale,
            0m,
            sale,
            primaryTicket,
            relation.PaymentMethod,
            0m,
            relation.Payout ?? 0m,
            0m,
            0m,
            0m,
            0m,
            relation.Commission,
            relation.CommissionPaid,
            Math.Max(relation.Commission - relation.CommissionPaid, 0m),
            status.ToUpperInvariant(),
            relation.Vendor,
            relation.Badge);
    }

    private static LocalCommissionBrowserRow MapImportedMovOperationCommissionRow(ImportedMovOperation row)
    {
        var saleTotal = row.TotalSale;
        var commission = CalculateWebCommission(row);
        var paid = Math.Max(row.Payment, 0m);
        var balance = Math.Max(commission - paid, 0m);
        var discountPercent = row.Amex > 0m
            ? CommissionPaymentRules.AmexRetentionPercent
            : row.Card > 0m
                ? CommissionPaymentRules.CardRetentionPercent
                : CommissionPaymentRules.CashRetentionPercent;
        var paymentMethod = row.Card > 0m && row.Cash > 0m ? "EFECTIVO / TARJETA" : row.Card > 0m ? "TARJETA" : "EFECTIVO";

        return new LocalCommissionBrowserRow(
            "C-" + row.Folio,
            row.Folio,
            row.Date,
            row.TransportType,
            row.UnitNumber,
            row.DriverName,
            row.Passengers,
            row.Hotel,
            row.Craft,
            row.Pharmacy,
            row.Purchase,
            row.Jewelry,
            saleTotal,
            row.Ticket,
            paymentMethod,
            discountPercent / 100m,
            row.EffectiveLeftAmount,
            row.Beverages + row.GiftBoxes,
            row.Repair,
            row.Tasting,
            NormalizePercentValue(ResolveImportedCommissionPercentage(row)) / 100m,
            commission,
            paid,
            balance,
            ResolveCommissionStatus(commission, paid),
            row.DriverName,
            row.Badge);
    }

    private static decimal CalculateDiscountPercent(AuthoritativeCommissionRow row)
    {
        var saleTotal = row.SaleTotal;
        if (saleTotal <= 0m)
            return 0m;

        var netAfterDiscount = CalculateAuthoritativeNetAfterDiscount(row, saleTotal, row.Payments);
        return saleTotal <= 0m
            ? 0m
            : Math.Max((saleTotal - netAfterDiscount) / saleTotal, 0m);
    }

    private static bool ContainsIgnoreCase(string? source, string value) =>
        !string.IsNullOrWhiteSpace(source)
        && source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string BuildRelationCommissionFolio(LocalRelation relation, string primaryTicket)
    {
        var baseFolio = !string.IsNullOrWhiteSpace(relation.OperationFolio)
            ? relation.OperationFolio.Trim()
            : relation.AppFolio.Trim();
        return string.IsNullOrWhiteSpace(primaryTicket)
            ? "C-" + baseFolio
            : "C-" + baseFolio + "-" + primaryTicket.Trim();
    }

    private static async Task<Dictionary<string, TicketPaymentBreakdown>> LoadTicketPaymentBreakdownsAsync(SqlConnection compuConnection, SqlConnection joyeriaConnection, IReadOnlyList<string> tickets)
    {
        var result = new Dictionary<string, TicketPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in await ReadTicketPaymentsAsync(compuConnection, tickets))
            result[pair.Key] = pair.Value;
        foreach (var pair in await ReadTicketPaymentsAsync(joyeriaConnection, tickets))
            result[pair.Key] = pair.Value;
        return result;
    }

    private static async Task<Dictionary<string, TicketPaymentBreakdown>> ReadTicketPaymentsAsync(SqlConnection connection, IReadOnlyList<string> tickets)
    {
        if (tickets.Count == 0) return new Dictionary<string, TicketPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        var parameters = new List<string>();
        for (var i = 0; i < tickets.Count; i++)
        {
            var parameter = "@ticket" + i.ToString(CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue(parameter, tickets[i]);
            parameters.Add(parameter);
        }
        command.CommandText = $"""
            SELECT p.folio_factura AS Ticket, COALESCE(m.Nombre, '') AS PaymentName, CAST(p.total AS decimal(18,2)) AS Total
            FROM dbo.pagosM p
            LEFT JOIN dbo.monedas m ON m.moneda = p.moneda
            WHERE p.folio_factura IN ({string.Join(",", parameters)});
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string Ticket, string PaymentName, decimal Total)>();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture)));
        }

        return rows
            .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var nonCard = 0m;
                    var card = 0m;
                    var amex = 0m;
                    var descriptions = new List<string>();
                    foreach (var payment in g)
                    {
                        descriptions.Add($"{payment.PaymentName} {payment.Total:C2}");
                        if (IsAmexPayment(payment.PaymentName)) amex += payment.Total;
                        else if (IsCardPayment(payment.PaymentName)) card += payment.Total;
                        else nonCard += payment.Total;
                    }
                    return new TicketPaymentBreakdown(nonCard, card, amex, string.Join(" / ", descriptions.Distinct(StringComparer.OrdinalIgnoreCase)));
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, StoreExpenseBreakdown>> LoadTicketExpenseBreakdownsAsync(SqlConnection compuConnection, SqlConnection joyeriaConnection, IReadOnlyList<string> tickets)
    {
        var result = new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in await ReadTicketExpensesAsync(compuConnection, tickets, false))
            result[pair.Key] = pair.Value;
        foreach (var pair in await ReadTicketExpensesAsync(joyeriaConnection, tickets, true))
            result[pair.Key] = pair.Value;
        return result;
    }

    private static async Task<Dictionary<string, StoreExpenseBreakdown>> ReadTicketExpensesAsync(SqlConnection connection, IReadOnlyList<string> tickets, bool joyeria)
    {
        if (tickets.Count == 0) return new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        var parameters = new List<string>();
        for (var i = 0; i < tickets.Count; i++)
        {
            var parameter = "@ticketExpense" + i.ToString(CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue(parameter, tickets[i]);
            parameters.Add(parameter);
        }

        command.CommandText = joyeria
            ? $"""
                SELECT CAST(folio_factura AS nvarchar(80)) AS Ticket, UPPER(COALESCE(referencia, '')) AS Referencia, UPPER(COALESCE(concepto, '')) AS Concepto, CAST(COALESCE(total, 0) AS decimal(18,2)) AS Total
                FROM dbo.gastos
                WHERE CAST(folio_factura AS nvarchar(80)) IN ({string.Join(",", parameters)});
                """
            : $"""
                SELECT CAST(folio_remision AS nvarchar(80)) AS Ticket, UPPER(COALESCE(referencia, '')) AS Referencia, UPPER(COALESCE(concepto, '')) AS Concepto, CAST(COALESCE(total, 0) AS decimal(18,2)) AS Total
                FROM dbo.egresos
                WHERE CAST(folio_remision AS nvarchar(80)) IN ({string.Join(",", parameters)});
                """;

        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<string, StoreExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            var ticket = reader.GetString(0);
            var text = ((reader.IsDBNull(1) ? string.Empty : reader.GetString(1)) + " " + (reader.IsDBNull(2) ? string.Empty : reader.GetString(2))).ToUpperInvariant();
            var total = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture);
            result.TryGetValue(ticket, out var current);
            var updated = current with
            {
                Dejada = current.Dejada + (text.Contains("DEJADA", StringComparison.OrdinalIgnoreCase) ? total : 0m),
                GastosVarios = current.GastosVarios + ((text.Contains("GASTOS VARIOS", StringComparison.OrdinalIgnoreCase) || text.EndsWith(" GV", StringComparison.OrdinalIgnoreCase) || text.Contains(" GV ", StringComparison.OrdinalIgnoreCase)) ? total : 0m),
                Degustacion = current.Degustacion + (text.Contains("DEGUST", StringComparison.OrdinalIgnoreCase) ? total : 0m),
                Reparacion = current.Reparacion + (text.Contains("REPARA", StringComparison.OrdinalIgnoreCase) ? total : 0m),
                Bebidas = current.Bebidas + (LooksLikeBeverage(text) ? total : 0m),
                CajasRegalo = current.CajasRegalo + ((text.Contains("CAJA", StringComparison.OrdinalIgnoreCase) || text.Contains("REGALO", StringComparison.OrdinalIgnoreCase)) ? total : 0m)
            };
            result[ticket] = updated;
        }

        return result;
    }

    private static bool LooksLikeBeverage(string text) =>
        text.Contains("CERVEZA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("CORONA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("COCA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("AGUA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("CANTARITO", StringComparison.OrdinalIgnoreCase)
        || text.Contains("REFRESCO", StringComparison.OrdinalIgnoreCase)
        || text.Contains("BEBIDA", StringComparison.OrdinalIgnoreCase);

    private sealed class AuthoritativeCommissionRow
    {
        public AuthoritativeCommissionRow(string localFolio, string operationFolio, string ticket, DateTime date, string driverCode, string driverName, string transportType, decimal saleTotal, decimal saleStore, decimal saleJewelry, string hotel, int passengers, string unitNumber, string badge, string staff, string vendedor, decimal groupPayout, decimal groupCommissionPaid, string groupCommissionPaidDate, TicketPaymentBreakdown payments, StoreExpenseBreakdown expenses, ImportedTransportInfo? transportInfo = null, decimal commissionableTotal = 0m)
        {
            LocalFolio = localFolio;
            OperationFolio = operationFolio;
            Ticket = ticket;
            Date = date;
            DriverCode = driverCode;
            DriverName = driverName;
            TransportType = transportType;
            SaleTotal = saleTotal;
            SaleStore = saleStore;
            SaleJewelry = saleJewelry;
            Hotel = hotel;
            Passengers = passengers;
            UnitNumber = unitNumber;
            Badge = badge;
            Staff = staff;
            Vendedor = vendedor;
            GroupPayout = groupPayout;
            GroupCommissionPaid = groupCommissionPaid;
            GroupCommissionPaidDate = groupCommissionPaidDate;
            Payments = payments;
            Expenses = expenses;
            TransportInfo = transportInfo ?? ImportedTransportInfo.Empty;
            CommissionableTotal = commissionableTotal > 0m ? commissionableTotal : saleTotal;
        }

        public string LocalFolio { get; }
        public string OperationFolio { get; }
        public string Ticket { get; }
        public DateTime Date { get; }
        public string DriverCode { get; }
        public string DriverName { get; }
        public string TransportType { get; }
        public decimal SaleTotal { get; }
        public decimal SaleStore { get; }
        public decimal SaleJewelry { get; }
        public string Hotel { get; }
        public int Passengers { get; }
        public string UnitNumber { get; }
        public string Badge { get; }
        public string Staff { get; }
        public string Vendedor { get; }
        public decimal GroupPayout { get; }
        public decimal GroupCommissionPaid { get; }
        public string GroupCommissionPaidDate { get; }
        public TicketPaymentBreakdown Payments { get; }
        public StoreExpenseBreakdown Expenses { get; }
        public ImportedTransportInfo TransportInfo { get; }
        public decimal CommissionableTotal { get; }
        public decimal PayoutDeduction { get; set; }
        public decimal CommissionAmount { get; set; }
        public decimal PaidAmount { get; set; }

        /// <summary>
        /// Estatus del pago de la DEJADA al taxista. No tiene nada que ver con el pago de la
        /// comision (CommissionAmount / PaidAmount): son dos pagos distintos.
        /// </summary>
        public string PayoutStatus { get; set; } = string.Empty;
    }

    private sealed record AuthoritativeAppRow(string OperationFolio, string PosFolio, string FolioApp, string FolioOriginal, DateTime Date, string DriverName, string DriverCode, string TransportType, decimal Payout, decimal CommissionPaidControl, string CommissionPaidDate, string Hotel, int Passengers, string UnitNumber, string Badge, string Staff, IReadOnlyList<string> Keys, bool HasLinkedOperationFolio, string PayoutStatus = "");
    private sealed record StoreTicketRow(string Ticket, decimal Total, decimal VentaTienda, decimal VentaJoyeria);
    private sealed record StoreTicketMatch(long Id, string MatchFolio, string Ticket, decimal Total, decimal VentaTienda, decimal VentaJoyeria);
    private sealed record StoreOnlyTicketRow(string Ticket, string OperationFolio, DateTime Date, decimal Total, decimal Subtotal, string Observation, bool Joyeria);
    private sealed record TicketPaymentBreakdown(decimal NonCard, decimal Card, decimal Amex, string Description)
    {
        public decimal Total => NonCard + Card + Amex;
    }
    private readonly record struct StoreExpenseBreakdown(decimal Dejada = 0m, decimal GastosVarios = 0m, decimal Degustacion = 0m, decimal Reparacion = 0m, decimal Bebidas = 0m, decimal CajasRegalo = 0m);

    private async Task<LocalProduct> GetProductForUpdateAsync(SqliteConnection connection, SqliteTransaction transaction, long id) =>
        (await ReadInTransactionAsync(connection, transaction, "SELECT Id,Codigo,Nombre,Precio,Iva,Existencia,Activo FROM LocalProductos WHERE Id=$id;", Product, ("$id", id))).SingleOrDefault() ?? throw new InvalidOperationException("Producto no encontrado.");

    private async Task<LocalSale> GetSaleForUpdateAsync(SqliteConnection connection, SqliteTransaction transaction, string folio) =>
        (await ReadInTransactionAsync(connection, transaction, "SELECT Id,Folio,Fecha,Cliente,Estatus,Subtotal,Iva,Total,Pagado,EstadoPago,Usuario FROM LocalVentas WHERE Folio=$folio;", Sale, ("$folio", Require(folio, "El folio")))).SingleOrDefault() ?? throw new InvalidOperationException("Venta no encontrada.");

    private async Task<LocalCommission> GetCommissionForUpdateAsync(SqliteConnection connection, SqliteTransaction transaction, string folio) =>
        (await ReadInTransactionAsync(connection, transaction, "SELECT Id,Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus FROM LocalComisiones WHERE Folio=$folio;", Commission, ("$folio", Require(folio, "El folio")))).SingleOrDefault() ?? throw new InvalidOperationException("ComisiÃ³n no encontrada.");

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        await using var connection = database.Open();
        await using var command = CreateCommand(connection, null, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync()) result.Add(map(reader));
        return result;
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\");";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task EnsureImportedCommissionCompatibilityAsync(SqliteConnection connection)
    {
        if (await HasTableAsync(connection, "mkt__dbo__AppMovilRegistro"))
        {
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "adult_count", "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "youth_count", "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "minor_count", "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "dolares", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "metodo_pago", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "pago_comision", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "fecha_pago_comision", "TEXT");
        }

        if (await HasTableAsync(connection, "mkt__dbo__dejadas"))
        {
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "folioregistro", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "folioregistrostr", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "codigorecepcion", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "tipotransporte", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "total", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "idtaxi", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "nombrestaff", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "hotel", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "pax", "INTEGER NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "unidad", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "gafete", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__dejadas", "nombrevendedor", "TEXT");
        }

        if (await HasTableAsync(connection, "mkt__dbo__mov_operacion"))
        {
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "folioperacion", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "fecha", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "foliosoluone", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "transportetipo", "TEXT");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "totaljoyeria", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "totalcompra", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "dejada", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "totalefectivo", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "totaltarjeta", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, "mkt__dbo__mov_operacion", "pago", "REAL NOT NULL DEFAULT 0");
        }

        foreach (var table in new[] { "compuadmo__dbo__remisioM", "joyeria__dbo__remisioM" })
        {
            if (!await HasTableAsync(connection, table))
                continue;

            await EnsureSqliteColumnAsync(connection, table, "folioregistro", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "folio_registro", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "folio_remision", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "folio_factura", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "observaciones", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "total", "REAL NOT NULL DEFAULT 0");
            await EnsureSqliteColumnAsync(connection, table, "estatus", "TEXT");
        }

        foreach (var table in new[] { "compuadmo__dbo__pagosM", "joyeria__dbo__pagosM" })
        {
            if (!await HasTableAsync(connection, table))
                continue;

            await EnsureSqliteColumnAsync(connection, table, "folio_factura", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "moneda", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "total", "REAL NOT NULL DEFAULT 0");
        }

        foreach (var table in new[] { "compuadmo__dbo__monedas", "joyeria__dbo__Monedas" })
        {
            if (!await HasTableAsync(connection, table))
                continue;

            await EnsureSqliteColumnAsync(connection, table, "moneda", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "Nombre", "TEXT");
        }

        foreach (var table in new[] { "compuadmo__dbo__egresos", "joyeria__dbo__gastos" })
        {
            if (!await HasTableAsync(connection, table))
                continue;

            await EnsureSqliteColumnAsync(connection, table, "folio_remision", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "folio_factura", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "referencia", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "concepto", "TEXT");
            await EnsureSqliteColumnAsync(connection, table, "total", "REAL NOT NULL DEFAULT 0");
        }
    }

    private static async Task EnsureSqliteColumnAsync(SqliteConnection connection, string table, string column, string definition)
    {
        if (await HasColumnAsync(connection, table, column))
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER TABLE \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\" ADD COLUMN \"{column.Replace("\"", "\"\"", StringComparison.Ordinal)}\" {definition};";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<T>> ReadInTransactionAsync<T>(SqliteConnection connection, SqliteTransaction transaction, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync()) result.Add(map(reader));
        return result;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters) =>
        Convert.ToInt64(await CreateCommand(connection, transaction, sql, parameters).ExecuteScalarAsync(), CultureInfo.InvariantCulture);

    private static async Task<decimal> ScalarDecimalAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters) =>
        Convert.ToDecimal(await CreateCommand(connection, transaction, sql, parameters).ExecuteScalarAsync(), CultureInfo.InvariantCulture);

    private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters) =>
        await CreateCommand(connection, transaction, sql, parameters).ExecuteNonQueryAsync();

    private static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (transaction is not null) command.Transaction = transaction;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }

    private static LocalProduct Product(SqliteDataReader row) => new(row.GetInt64(0), row.GetString(1), row.GetString(2), Decimal(row, 3), Decimal(row, 4), row.GetInt32(5), row.GetInt64(6) == 1);
    private static LocalSale Sale(SqliteDataReader row) => new(row.GetInt64(0), row.GetString(1), Date(row, 2), row.GetString(3), row.GetString(4), Decimal(row, 5), Decimal(row, 6), Decimal(row, 7), Decimal(row, 8), row.GetString(9), row.GetString(10));
    private static LocalSaleLine SaleLine(SqliteDataReader row) => new(row.GetInt64(0), row.GetInt64(1), row.GetInt64(2), row.GetString(3), row.GetString(4), row.GetInt32(5), Decimal(row, 6), Decimal(row, 7), Decimal(row, 8), Decimal(row, 9), Decimal(row, 10));
    private static LocalPayment Payment(SqliteDataReader row) => new(row.GetInt64(0), row.GetString(1), row.GetString(2), Date(row, 3), Decimal(row, 4), row.GetString(5), row.GetString(6), row.GetString(7));
    private static LocalCommission Commission(SqliteDataReader row) => new(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), Date(row, 5), Decimal(row, 6), Decimal(row, 7), Decimal(row, 8), Decimal(row, 9), row.GetString(10));
    private static LocalCut Cut(SqliteDataReader row) => new(row.GetInt64(0), DateOnly(row, 1), Decimal(row, 2), Decimal(row, 3), Decimal(row, 4), Decimal(row, 5), Decimal(row, 6), Decimal(row, 7), Decimal(row, 8), row.GetString(9), row.GetString(10), row.IsDBNull(11) ? null : row.GetString(11));
    private static LocalAuditEntry Audit(SqliteDataReader row) => new(row.GetInt64(0), Date(row, 1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5), row.GetString(6), row.GetString(7), row.GetString(8), row.GetString(9), row.GetString(10), row.GetString(11), row.GetString(12), row.GetString(13), row.IsDBNull(14) ? null : Decimal(row, 14), row.GetInt64(15) == 1, row.GetString(16), row.GetString(17), row.GetString(18));
    private static string Text(SqliteDataReader row, int index) => row.IsDBNull(index) ? string.Empty : Convert.ToString(row.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
    private static decimal Decimal(SqliteDataReader row, int index) => Convert.ToDecimal(row.GetValue(index), CultureInfo.InvariantCulture);
    private static DateTime Date(SqliteDataReader row, int index) => DateTime.Parse(row.GetString(index), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static DateTime DateOnly(SqliteDataReader row, int index) => DateTime.ParseExact(row.GetString(index), "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Require(string value, string label) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"{label} es obligatorio.");

    private sealed record ImportedTransportCatalogRow(
        string Type,
        string Name,
        decimal CashDiscount,
        decimal CardDiscount,
        decimal CommissionPercent,
        decimal Minimum,
        decimal Maximum,
        decimal AmexDiscount,
        DateTime? EffectiveFrom = null,
        DateTime? EffectiveTo = null,
        bool Configured = false);
    private enum PaymentKind
    {
        Cash,
        Card,
        Amex
    }

    private sealed record ImportedTransportInfo(decimal CashDiscount, decimal CardDiscount, decimal CommissionPercent, decimal Minimum, decimal Maximum, decimal AmexDiscount = 0m)
    {
        public static ImportedTransportInfo Empty { get; } = new(0m, 0m, 0m, 0m, 0m);
    }

    private sealed record ImportedDejadaCatalogRow(string FolioRegistro, string FolioRegistroString, string CodigoRecepcion, string DriverName, string Hotel, int Passengers, string UnitNumber, string Badge);
    private sealed record ImportedDejadaInfo(string DriverName, string Hotel, int Passengers, string UnitNumber, string Badge)
    {
        public static ImportedDejadaInfo Empty { get; } = new(string.Empty, string.Empty, 0, string.Empty, string.Empty);
    }

    private sealed record ImportedMovOperation(
        string Folio,
        DateTime Date,
        string Ticket,
        string TransportType,
        decimal Cash,
        decimal Card,
        decimal Payment,
        decimal Expenses,
        decimal Jewelry,
        decimal Purchase,
        decimal Craft,
        decimal Beverages,
        decimal Pharmacy,
        decimal LeftAmount,
        decimal CashDiscount,
        decimal CardDiscount,
        decimal CommissionPercent,
        decimal Minimum,
        decimal Maximum,
        string DriverName,
        string Hotel,
        int Passengers,
        string UnitNumber,
        string Badge)
    {
        public decimal EffectiveLeftAmount { get; set; } = LeftAmount;
        public decimal TotalSale => Jewelry + Purchase;
        public decimal TotalDay => Jewelry + Purchase + Craft + Beverages + Pharmacy;
        public decimal Amex => 0m;
        public decimal Repair => 0m;
        public decimal Tasting => Expenses;
        public decimal GiftBoxes => 0m;
    }
}
