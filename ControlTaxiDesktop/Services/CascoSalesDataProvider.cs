using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed class CascoSalesDataProvider
{
    public sealed record CascoOperationSaleSummary(
        decimal Compuadmo,
        decimal Joyeria,
        IReadOnlyList<string> Tickets,
        string PaymentDescription,
        string VendorName);

    private readonly string _sqlServer;
    private readonly string _compuadmoDatabase;
    private readonly string _joyeriaDatabase;

    public CascoSalesDataProvider(string sqlServer)
    {
        _sqlServer = sqlServer ?? throw new ArgumentNullException(nameof(sqlServer));
        _compuadmoDatabase = "compuadmoCasco";
        _joyeriaDatabase = "joyeriaCasco";
    }

    public CascoSalesDataProvider(BranchConfiguration branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        _sqlServer = branch.SqlServer ?? throw new ArgumentNullException(nameof(branch.SqlServer));
        _compuadmoDatabase = string.IsNullOrWhiteSpace(branch.CompuadmoDatabase) ? "compuadmoCasco" : branch.CompuadmoDatabase.Trim();
        _joyeriaDatabase = string.IsNullOrWhiteSpace(branch.JoyeriaDatabase) ? "joyeriaCasco" : branch.JoyeriaDatabase.Trim();
    }

    public async Task<IReadOnlyList<LocalSalesBrowserRow>> GetSalesBrowserRowsAsync(string sqlPassword, string? search)
    {
        if (string.IsNullOrWhiteSpace(sqlPassword)) throw new ArgumentException("sql password required", nameof(sqlPassword));
        var normalized = search?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<LocalSalesBrowserRow>();

        await using var compu = await OpenStoreAsync(sqlPassword, _compuadmoDatabase);
        await using var joy = await OpenStoreAsync(sqlPassword, _joyeriaDatabase);
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

        var rows = result
            .GroupBy(x => string.Join("|",
                x.OrigenVenta,
                x.FolioRegistro,
                x.Folio,
                x.Factura,
                x.Fecha.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                x.Total.ToString("0.00", CultureInfo.InvariantCulture)), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Fecha).First())
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Folio, StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToArray();

        return NormalizePaymentDistribution(rows);
    }

    public async Task<IReadOnlyList<string>> GetVendorNamesAsync(string sqlPassword)
    {
        if (string.IsNullOrWhiteSpace(sqlPassword)) throw new ArgumentException("sql password required", nameof(sqlPassword));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var compu = await OpenStoreAsync(sqlPassword, _compuadmoDatabase);
        await using var joy = await OpenStoreAsync(sqlPassword, _joyeriaDatabase);

        foreach (var name in await ReadVendorNamesAsync(compu))
            names.Add(name);
        foreach (var name in await ReadVendorNamesAsync(joy))
            names.Add(name);

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<LocalSalesTicketRow>> GetSalesTicketRowsAsync(string sqlPassword, LocalSalesBrowserRow sale)
    {
        if (string.IsNullOrWhiteSpace(sqlPassword)) throw new ArgumentException("sql password required", nameof(sqlPassword));

        await using var compu = await OpenStoreAsync(sqlPassword, _compuadmoDatabase);
        await using var joy = await OpenStoreAsync(sqlPassword, _joyeriaDatabase);
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
            return Array.Empty<LocalSalesTicketRow>();

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

    public async Task<(decimal Compuadmo, decimal Joyeria)> GetRemisionesTotalByOperacionAsync(string sqlPassword, string folioOperacion)
    {
        if (string.IsNullOrWhiteSpace(sqlPassword)) throw new ArgumentException("sql password required", nameof(sqlPassword));
        if (string.IsNullOrWhiteSpace(folioOperacion)) return (0m, 0m);
        var normalizedOperation = folioOperacion.Trim().TrimStart('0');
        if (!int.TryParse(string.IsNullOrWhiteSpace(normalizedOperation) ? "0" : normalizedOperation, NumberStyles.Integer, CultureInfo.InvariantCulture, out var operationNumber))
            return (0m, 0m);

        var ventaCompu = 0m;
        var ventaJoy = 0m;
        try
        {
            var cBuilder = new SqlConnectionStringBuilder
            {
                DataSource = _sqlServer,
                InitialCatalog = _compuadmoDatabase,
                UserID = CascoSqlIdentity.ResolveUser(),
                Password = sqlPassword,
                TrustServerCertificate = true,
                Encrypt = false
            };

            await using var cConn = new SqlConnection(cBuilder.ToString());
            await cConn.OpenAsync();
            await using (var cmd = cConn.CreateCommand())
            {
                cmd.CommandTimeout = 90;
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(CAST(COALESCE(total, 0) AS decimal(18,2))), 0)
                    FROM dbo.remisioM
                    WHERE (folio_operacion = @folioOp
                       OR folioregistro = @folioOp)
                      AND COALESCE(NULLIF(UPPER(LTRIM(RTRIM(CONVERT(nvarchar(20), estatus)))), N''), N'A') <> N'C'";
                cmd.Parameters.Add("@folioOp", SqlDbType.Int).Value = operationNumber;
                ventaCompu = Convert.ToDecimal(await cmd.ExecuteScalarAsync() ?? 0m, CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // swallow - diagnostic provider must not throw
        }

        try
        {
            var jBuilder = new SqlConnectionStringBuilder
            {
                DataSource = _sqlServer,
                InitialCatalog = _joyeriaDatabase,
                UserID = CascoSqlIdentity.ResolveUser(),
                Password = sqlPassword,
                TrustServerCertificate = true,
                Encrypt = false
            };

            await using var jConn = new SqlConnection(jBuilder.ToString());
            await jConn.OpenAsync();
            await using (var cmd = jConn.CreateCommand())
            {
                cmd.CommandTimeout = 90;
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(CAST(COALESCE(total, 0) AS decimal(18,2))), 0)
                    FROM dbo.remisioM
                    WHERE (folio_operacion = @folioOp
                       OR folio_registro = @folioOp)
                      AND COALESCE(NULLIF(UPPER(LTRIM(RTRIM(CONVERT(nvarchar(20), estatus)))), N''), N'A') <> N'C'";
                cmd.Parameters.Add("@folioOp", SqlDbType.Int).Value = operationNumber;
                ventaJoy = Convert.ToDecimal(await cmd.ExecuteScalarAsync() ?? 0m, CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // swallow
        }

        return (ventaCompu, ventaJoy);
    }

    public async Task<IReadOnlyDictionary<string, CascoOperationSaleSummary>> GetOperationSaleSummariesAsync(
        string sqlPassword,
        IReadOnlyList<string> foliosOperacion,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? ticketsByFolioOperacion = null)
    {
        if (string.IsNullOrWhiteSpace(sqlPassword) || foliosOperacion.Count == 0)
            return new Dictionary<string, CascoOperationSaleSummary>(StringComparer.OrdinalIgnoreCase);

        var normalizedFolios = foliosOperacion
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.TrimStart('0'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedFolios.Length == 0)
            return new Dictionary<string, CascoOperationSaleSummary>(StringComparer.OrdinalIgnoreCase);

        await using var compu = await OpenStoreAsync(sqlPassword, _compuadmoDatabase);
        await using var joy = await OpenStoreAsync(sqlPassword, _joyeriaDatabase);

        var normalizedTicketsByFolio = NormalizeTicketsByOperation(ticketsByFolioOperacion);
        var operationFoliosToQuery = normalizedFolios;

        var compuSummaryByTicket = await SafeReadTicketSummariesAsync(compu, normalizedTicketsByFolio, joyeria: false);
        var joySummaryByTicket = await SafeReadTicketSummariesAsync(joy, normalizedTicketsByFolio, joyeria: true);
        var compuSummaryByOperation = await SafeReadOperationSummariesAsync(compu, operationFoliosToQuery, joyeria: false);
        var joySummaryByOperation = await SafeReadOperationSummariesAsync(joy, operationFoliosToQuery, joyeria: true);

        var result = new Dictionary<string, CascoOperationSaleSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var folio in normalizedFolios)
        {
            compuSummaryByOperation.TryGetValue(folio, out var compuSummary);
            joySummaryByOperation.TryGetValue(folio, out var joySummary);
            compuSummary ??= new OperationSummaryAccumulator();
            joySummary ??= new OperationSummaryAccumulator();
            if (compuSummaryByTicket.TryGetValue(folio, out var compuTicketSummary))
                compuSummary.Merge(compuTicketSummary);
            if (joySummaryByTicket.TryGetValue(folio, out var joyTicketSummary))
                joySummary.Merge(joyTicketSummary);

            var tickets = compuSummary.Tickets
                .Concat(joySummary.Tickets)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var paymentParts = compuSummary.PaymentLines
                .Concat(joySummary.PaymentLines)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            result[folio] = new CascoOperationSaleSummary(
                compuSummary.Total,
                joySummary.Total,
                tickets,
                string.Join(" / ", paymentParts),
                FirstFilled(compuSummary.VendorNames.Concat(joySummary.VendorNames).ToArray()));
        }

        return result;
    }

    private async Task<SqlConnection> OpenStoreAsync(string sqlPassword, string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _sqlServer,
            InitialCatalog = database,
            UserID = CascoSqlIdentity.ResolveUser(),
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false
        };

        var connection = new SqlConnection(builder.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<IReadOnlyList<string>> ReadVendorNamesAsync(SqlConnection connection)
    {
        if (!await TableExistsAsync(connection, "vendedor") || !await ColumnExistsAsync(connection, "vendedor", "Nombre"))
            return Array.Empty<string>();

        var hasLastName = await ColumnExistsAsync(connection, "vendedor", "Apellidos");
        var nameExpression = hasLastName
            ? """
              NULLIF(LTRIM(RTRIM(
                CASE
                  WHEN NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200), Apellidos))), N'') IS NULL
                    THEN CONVERT(nvarchar(200), Nombre)
                  ELSE CONCAT(CONVERT(nvarchar(200), Nombre), N' ', CONVERT(nvarchar(200), Apellidos))
                END)), N'')
              """
            : "NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200), Nombre))), N'')";

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 45;
        command.CommandText = $"""
            SELECT DISTINCT {nameExpression} AS NombreVendedor
            FROM dbo.vendedor
            WHERE {nameExpression} IS NOT NULL
            ORDER BY NombreVendedor;
            """;

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
                result.Add(name.Trim());
        }

        return result;
    }

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@tableName, N'U') IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 256).Value = "dbo." + table;
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(SqlConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 256).Value = "dbo." + table;
        command.Parameters.Add("@columnName", SqlDbType.NVarChar, 128).Value = column;
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<Dictionary<string, OperationSummaryAccumulator>> ReadOperationSummariesAsync(
        SqlConnection connection,
        IReadOnlyList<string> foliosOperacion,
        bool joyeria)
    {
        var result = new Dictionary<string, OperationSummaryAccumulator>(StringComparer.OrdinalIgnoreCase);
        if (foliosOperacion.Count == 0)
            return result;

        var numericFolios = foliosOperacion
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.TrimStart('0'))
            .Select(value => int.TryParse(string.IsNullOrWhiteSpace(value) ? "0" : value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? (int?)parsed : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();

        if (numericFolios.Length == 0)
            return result;

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 90;
        var parameters = new List<string>();
        for (var i = 0; i < numericFolios.Length; i++)
        {
            var parameter = "@folioOp" + i.ToString(CultureInfo.InvariantCulture);
            command.Parameters.Add(parameter, SqlDbType.Int).Value = numericFolios[i];
            parameters.Add(parameter);
        }

        var ticketColumn = joyeria
            ? "COALESCE(CONVERT(nvarchar(50), r.folio_pedido), CONVERT(nvarchar(50), r.folio_factura))"
            : "CONVERT(nvarchar(50), r.folio_remision)";
        var registryColumn = joyeria ? "r.folio_registro" : "r.folioregistro";
        var operationKeyColumn = $"CASE WHEN COALESCE(r.folio_operacion, 0) <> 0 THEN CONVERT(bigint, r.folio_operacion) ELSE COALESCE({registryColumn}, 0) END";
        var vendorNameExpression = joyeria
            ? "COALESCE(NULLIF(v.Nombre, ''), NULLIF(r.usuario, ''), '')"
            : """
              COALESCE(NULLIF(LTRIM(RTRIM(
                CASE
                  WHEN v.Apellidos IS NULL THEN v.Nombre
                  ELSE CONCAT(v.Nombre, ' ', v.Apellidos)
                END)), ''), NULLIF(r.usuario, ''), '')
              """;

        command.CommandText = $"""
            SELECT
              CONVERT(nvarchar(50), {operationKeyColumn}) AS FolioOperacion,
              {ticketColumn} AS Ticket,
              CONVERT(nvarchar(50), r.folio_factura) AS Factura,
              CAST(COALESCE(r.total, 0) AS decimal(18,2)) AS Venta,
              COALESCE(pay.PaymentName, N'') AS PaymentName,
              COALESCE(pay.PaymentTotal, 0) AS PaymentTotal,
              COALESCE(pay.CurrencyName, N'') AS CurrencyName,
              {vendorNameExpression} AS VendorName
            FROM dbo.remisioM r
            LEFT JOIN dbo.vendedor v
              ON CONVERT(nvarchar(50), v.Vendedor) = CONVERT(nvarchar(50), r.vendedor)
            OUTER APPLY
            (
                SELECT
                    SUM(CAST(COALESCE(p.total, 0) AS decimal(18,2))) AS PaymentTotal,
                    MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(120), p.tipo_pago))), N'')) AS PaymentName,
                    MAX(CASE
                        WHEN COALESCE(CONVERT(nvarchar(40), p.moneda), N'') IN (N'0', N'00') THEN N'PESOS'
                        ELSE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(80), p.moneda))), N'')
                    END) AS CurrencyName
                FROM dbo.pagosM p
                WHERE UPPER(CONVERT(nvarchar(80), p.folio_factura)) IN
                    (UPPER({ticketColumn}), UPPER(CONVERT(nvarchar(80), r.folio_factura)))
            ) pay
            WHERE (r.folio_operacion IN ({string.Join(",", parameters)})
               OR {registryColumn} IN ({string.Join(",", parameters)}))
              AND COALESCE(NULLIF(UPPER(LTRIM(RTRIM(CONVERT(nvarchar(20), r.estatus)))), N''), N'A') <> N'C';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var folio = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(folio))
                continue;

            if (!result.TryGetValue(folio, out var summary))
            {
                summary = new OperationSummaryAccumulator();
                result[folio] = summary;
            }

            var ticket = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty;
            var factura = reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty;
            var sale = reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture);
            summary.AddSale(BuildSaleKey(joyeria, folio, ticket, factura, sale), sale);
            if (!string.IsNullOrWhiteSpace(ticket))
                summary.Tickets.Add(ticket.Trim());
            else if (!string.IsNullOrWhiteSpace(factura))
                summary.Tickets.Add(factura.Trim());

            var paymentName = reader.IsDBNull(4) ? string.Empty : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture) ?? string.Empty;
            var paymentTotal = reader.IsDBNull(5) ? 0m : Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture);
            var currencyName = reader.IsDBNull(6) ? string.Empty : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty;
            if (paymentTotal > 0m)
                summary.AddPayment(BuildPaymentLine(paymentName, currencyName, paymentTotal), paymentTotal);

            var vendorName = reader.IsDBNull(7) ? string.Empty : Convert.ToString(reader.GetValue(7), CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(vendorName))
                summary.VendorNames.Add(vendorName.Trim());
        }

        return result;
    }

    private static async Task<Dictionary<string, OperationSummaryAccumulator>> SafeReadOperationSummariesAsync(
        SqlConnection connection,
        IReadOnlyList<string> foliosOperacion,
        bool joyeria)
    {
        try
        {
            return await ReadOperationSummariesAsync(connection, foliosOperacion, joyeria);
        }
        catch
        {
            return new Dictionary<string, OperationSummaryAccumulator>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<Dictionary<string, OperationSummaryAccumulator>> SafeReadTicketSummariesAsync(
        SqlConnection connection,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ticketsByFolioOperacion,
        bool joyeria)
    {
        try
        {
            return await ReadTicketSummariesAsync(connection, ticketsByFolioOperacion, joyeria);
        }
        catch
        {
            return new Dictionary<string, OperationSummaryAccumulator>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static async Task<Dictionary<string, OperationSummaryAccumulator>> ReadTicketSummariesAsync(
        SqlConnection connection,
        IReadOnlyDictionary<string, IReadOnlyList<string>> ticketsByFolioOperacion,
        bool joyeria)
    {
        var result = new Dictionary<string, OperationSummaryAccumulator>(StringComparer.OrdinalIgnoreCase);
        var ticketToOperation = ticketsByFolioOperacion
            .SelectMany(pair => pair.Value.Select(ticket => (Operation: pair.Key, Ticket: ticket)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Ticket))
            .GroupBy(pair => pair.Ticket.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Operation, StringComparer.OrdinalIgnoreCase);

        if (ticketToOperation.Count == 0)
            return result;

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 90;
        var parameters = new List<string>();
        var tickets = ticketToOperation.Keys.ToArray();
        for (var i = 0; i < tickets.Length; i++)
        {
            var parameter = "@ticket" + i.ToString(CultureInfo.InvariantCulture);
            command.Parameters.Add(parameter, SqlDbType.NVarChar, 80).Value = tickets[i];
            parameters.Add(parameter);
        }

        var ticketColumn = joyeria
            ? "COALESCE(CONVERT(nvarchar(80), r.folio_pedido), CONVERT(nvarchar(80), r.folio_factura))"
            : "CONVERT(nvarchar(80), r.folio_remision)";
        var invoiceColumn = "CONVERT(nvarchar(80), r.folio_factura)";
        var vendorNameExpression = joyeria
            ? "COALESCE(NULLIF(v.Nombre, ''), NULLIF(r.usuario, ''), '')"
            : """
              COALESCE(NULLIF(LTRIM(RTRIM(
                CASE
                  WHEN v.Apellidos IS NULL THEN v.Nombre
                  ELSE CONCAT(v.Nombre, ' ', v.Apellidos)
                END)), ''), NULLIF(r.usuario, ''), '')
              """;

        command.CommandText = $"""
            SELECT
              {ticketColumn} AS Ticket,
              {invoiceColumn} AS Factura,
              CAST(COALESCE(r.total, 0) AS decimal(18,2)) AS Venta,
              COALESCE(pay.PaymentName, N'') AS PaymentName,
              COALESCE(pay.PaymentTotal, 0) AS PaymentTotal,
              COALESCE(pay.CurrencyName, N'') AS CurrencyName,
              {vendorNameExpression} AS VendorName
            FROM dbo.remisioM r
            LEFT JOIN dbo.vendedor v
              ON CONVERT(nvarchar(50), v.Vendedor) = CONVERT(nvarchar(50), r.vendedor)
            OUTER APPLY
            (
                SELECT
                    SUM(CAST(COALESCE(p.total, 0) AS decimal(18,2))) AS PaymentTotal,
                    MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(120), p.tipo_pago))), N'')) AS PaymentName,
                    MAX(CASE
                        WHEN COALESCE(CONVERT(nvarchar(40), p.moneda), N'') IN (N'0', N'00') THEN N'PESOS'
                        ELSE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(80), p.moneda))), N'')
                    END) AS CurrencyName
                FROM dbo.pagosM p
                WHERE UPPER(CONVERT(nvarchar(80), p.folio_factura)) IN
                    (UPPER({ticketColumn}), UPPER({invoiceColumn}))
            ) pay
            WHERE (UPPER({ticketColumn}) IN ({string.Join(",", parameters.Select(x => "UPPER(" + x + ")"))})
               OR UPPER({invoiceColumn}) IN ({string.Join(",", parameters.Select(x => "UPPER(" + x + ")"))}))
              AND COALESCE(NULLIF(UPPER(LTRIM(RTRIM(CONVERT(nvarchar(20), r.estatus)))), N''), N'A') <> N'C';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var ticket = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
            var factura = reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty;
            var operation = string.Empty;
            if (!string.IsNullOrWhiteSpace(ticket))
                ticketToOperation.TryGetValue(ticket.Trim(), out operation);
            if (string.IsNullOrWhiteSpace(operation) && !string.IsNullOrWhiteSpace(factura))
                ticketToOperation.TryGetValue(factura.Trim(), out operation);
            if (string.IsNullOrWhiteSpace(operation))
                continue;

            if (!result.TryGetValue(operation, out var summary))
            {
                summary = new OperationSummaryAccumulator();
                result[operation] = summary;
            }

            var sale = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture);
            summary.AddSale(BuildSaleKey(joyeria, operation, ticket, factura, sale), sale);
            if (!string.IsNullOrWhiteSpace(ticket))
                summary.Tickets.Add(ticket.Trim());
            else if (!string.IsNullOrWhiteSpace(factura))
                summary.Tickets.Add(factura.Trim());

            var paymentName = reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty;
            var paymentTotal = reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture);
            var currencyName = reader.IsDBNull(5) ? string.Empty : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty;
            if (paymentTotal > 0m)
                summary.AddPayment(BuildPaymentLine(paymentName, currencyName, paymentTotal), paymentTotal);

            var vendorName = reader.IsDBNull(6) ? string.Empty : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(vendorName))
                summary.VendorNames.Add(vendorName.Trim());
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NormalizeTicketsByOperation(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? ticketsByFolioOperacion)
    {
        if (ticketsByFolioOperacion is null || ticketsByFolioOperacion.Count == 0)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        return ticketsByFolioOperacion
            .Select(pair => (
                Operation: (pair.Key ?? string.Empty).Trim().TrimStart('0'),
                Tickets: pair.Value
                    .SelectMany(SplitSearchTokens)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Operation) && pair.Tickets.Length > 0)
            .ToDictionary(pair => pair.Operation, pair => (IReadOnlyList<string>)pair.Tickets, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<LocalSalesBrowserRow>> ReadSalesFromStoreAsync(SqlConnection connection, bool joyeria, string normalized, string numeric, string prefixedTicket, long? folioNumber)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 90;
        var includeTextSearch = !folioNumber.HasValue && normalized.Length >= 3;
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
                  COALESCE(tipo_cambio, 0) AS TipoCambio,
                  COALESCE(pay.PaymentTotal, 0) AS TotalPagos,
                  COALESCE(pay.PaymentName, N'') AS FormaPagoReal,
                  COALESCE(pay.CurrencyName, N'') AS MonedaReal
                FROM dbo.remisioM r
                OUTER APPLY
                (
                    SELECT
                        SUM(CAST(COALESCE(p.total, 0) AS decimal(18,2))) AS PaymentTotal,
                        MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(120), p.tipo_pago))), N'')) AS PaymentName,
                        MAX(CASE
                            WHEN COALESCE(CONVERT(nvarchar(40), p.moneda), N'') IN (N'0', N'00') THEN N'PESOS'
                            ELSE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(80), p.moneda))), N'')
                        END) AS CurrencyName
                    FROM dbo.pagosM p
                    WHERE UPPER(CONVERT(nvarchar(80), p.folio_factura)) IN
                        (UPPER(CONVERT(nvarchar(80), COALESCE(r.folio_pedido, r.folio_factura))), UPPER(CONVERT(nvarchar(80), r.folio_factura)))
                ) pay
                WHERE ((@folioNumber IS NOT NULL AND (folio_operacion = @folioNumber OR folio_registro = @folioNumber))
                   OR (@folioNumber IS NULL AND UPPER(CONVERT(nvarchar(30), COALESCE(folio_pedido, folio_factura))) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket)))
                   OR (@folioNumber IS NULL AND UPPER(CONVERT(nvarchar(30), folio_factura)) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket)))
                   OR (@folioNumber IS NULL AND @includeTextSearch = 1 AND UPPER(COALESCE(cliente, '')) LIKE UPPER(@searchLike))
                   OR (@folioNumber IS NULL AND @includeTextSearch = 1 AND UPPER(COALESCE(CONVERT(nvarchar(max), observaciones), '')) LIKE UPPER(@searchLike)))
                  AND COALESCE(NULLIF(UPPER(LTRIM(RTRIM(CONVERT(nvarchar(20), estatus)))), N''), N'A') <> N'C'
                ORDER BY fecha DESC
                OPTION (RECOMPILE);
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
                  COALESCE(tipo_cambio, 0) AS TipoCambio,
                  COALESCE(pay.PaymentTotal, 0) AS TotalPagos,
                  COALESCE(pay.PaymentName, N'') AS FormaPagoReal,
                  COALESCE(pay.CurrencyName, N'') AS MonedaReal
                FROM dbo.remisioM r
                OUTER APPLY
                (
                    SELECT
                        SUM(CAST(COALESCE(p.total, 0) AS decimal(18,2))) AS PaymentTotal,
                        MAX(NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(120), p.tipo_pago))), N'')) AS PaymentName,
                        MAX(CASE
                            WHEN COALESCE(CONVERT(nvarchar(40), p.moneda), N'') IN (N'0', N'00') THEN N'PESOS'
                            ELSE NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(80), p.moneda))), N'')
                        END) AS CurrencyName
                    FROM dbo.pagosM p
                    WHERE UPPER(CONVERT(nvarchar(80), p.folio_factura)) IN
                        (UPPER(CONVERT(nvarchar(80), r.folio_remision)), UPPER(CONVERT(nvarchar(80), r.folio_factura)))
                ) pay
                WHERE ((@folioNumber IS NOT NULL AND (folio_operacion = @folioNumber OR folioregistro = @folioNumber))
                   OR (@folioNumber IS NULL AND UPPER(CONVERT(nvarchar(30), folio_remision)) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket)))
                   OR (@folioNumber IS NULL AND UPPER(CONVERT(nvarchar(30), folio_factura)) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket)))
                   OR (@folioNumber IS NULL AND @includeTextSearch = 1 AND UPPER(COALESCE(cliente, '')) LIKE UPPER(@searchLike))
                   OR (@folioNumber IS NULL AND @includeTextSearch = 1 AND UPPER(COALESCE(CONVERT(nvarchar(max), observaciones), '')) LIKE UPPER(@searchLike)))
                  AND COALESCE(NULLIF(UPPER(LTRIM(RTRIM(CONVERT(nvarchar(20), estatus)))), N''), N'A') <> N'C'
                ORDER BY fecha DESC
                OPTION (RECOMPILE);
                """;
        command.Parameters.AddWithValue("@folio", numeric);
        command.Parameters.AddWithValue("@ticket", normalized);
        command.Parameters.AddWithValue("@prefixedTicket", prefixedTicket);
        command.Parameters.AddWithValue("@folioNumber", folioNumber ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@searchLike", "%" + normalized + "%");
        command.Parameters.Add("@includeTextSearch", SqlDbType.Bit).Value = includeTextSearch ? 1 : 0;

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<LocalSalesBrowserRow>();
        while (await reader.ReadAsync())
        {
            var total = Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture);
            var totalPagos = Convert.ToDecimal(reader.GetValue(15), CultureInfo.InvariantCulture);
            var formaPagoReal = reader.IsDBNull(16) ? string.Empty : Convert.ToString(reader.GetValue(16), CultureInfo.InvariantCulture) ?? string.Empty;
            var monedaReal = reader.IsDBNull(17) ? string.Empty : Convert.ToString(reader.GetValue(17), CultureInfo.InvariantCulture) ?? string.Empty;
            var formaPagoDetalle = NormalizePaymentMethod(formaPagoReal);
            var monedaDetalle = NormalizeCurrency(monedaReal);
            var diferenciaPago = decimal.Round(total - totalPagos, 2, MidpointRounding.AwayFromZero);

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
                total,
                Convert.ToDecimal(reader.GetValue(11), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(12), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(13), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(14), CultureInfo.InvariantCulture),
                joyeria ? "JOY" : "COMP",
                totalPagos,
                diferenciaPago,
                formaPagoDetalle,
                monedaDetalle,
                BuildPaymentLine(formaPagoDetalle, monedaDetalle, totalPagos),
                diferenciaPago < 0m));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<LocalSalesTicketRow>> ReadSalesTicketRowsFromStoreAsync(SqlConnection connection, LocalSalesBrowserRow sale, bool joyeria, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0)
            return Array.Empty<LocalSalesTicketRow>();

        await using var command = connection.CreateCommand();
        var parameters = new List<string>();
        for (var i = 0; i < keys.Count; i++)
        {
            var parameter = "@key" + i.ToString(CultureInfo.InvariantCulture);
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, keys[i]);
        }

        var folioColumn = joyeria ? "m.folio_registro" : "m.folioregistro";
        var detailJoin = joyeria
            ? "CONVERT(nvarchar(80), d.folio_factura) = CONVERT(nvarchar(80), m.folio_factura)"
            : "CONVERT(nvarchar(80), d.folio_remision) = CONVERT(nvarchar(80), m.folio_remision)";
        var ticketColumn = joyeria ? "COALESCE(m.folio_pedido, m.folio_factura)" : "m.folio_remision";
        var invoiceColumn = "m.folio_factura";
        var productExpression = joyeria
            ? "COALESCE(NULLIF(CONVERT(nvarchar(200), d.producto), ''), NULLIF(CONVERT(nvarchar(200), d.descripcion_larga), ''), 'SIN DESCRIPCION')"
            : "COALESCE(NULLIF(CONVERT(nvarchar(200), d.producto), ''), NULLIF(CONVERT(nvarchar(200), d.descripcion_larga), ''), 'SIN DESCRIPCION')";
        var departmentExpression = joyeria
            ? "COALESCE(NULLIF(CONVERT(nvarchar(120), d.deportiva), ''), NULLIF(CONVERT(nvarchar(120), d.categoria), ''), '')"
            : "COALESCE(NULLIF(CONVERT(nvarchar(120), d.deportiva), ''), '')";
        var orderExpression = joyeria ? "d.fecha" : "d.cons";
        command.CommandText = $"""
            SELECT
              CASE WHEN ISNUMERIC(d.cantidads) = 1 THEN CONVERT(int, d.cantidads) ELSE 1 END AS Cantidad,
              {productExpression} AS Producto,
              {departmentExpression} AS Departamento,
              COALESCE(d.p_unitario, 0) AS Precio,
              COALESCE(d.stotal, 0) AS Importe,
              CONVERT(nvarchar(80), {ticketColumn}) AS Referencia
            FROM dbo.remisioD d
            INNER JOIN dbo.remisioM m
              ON {detailJoin}
            WHERE CAST({folioColumn} AS nvarchar(80)) IN ({string.Join(",", parameters)})
               OR UPPER(CONVERT(nvarchar(80), {ticketColumn})) IN ({string.Join(",", parameters.Select(x => "UPPER(" + x + ")"))})
               OR UPPER(CONVERT(nvarchar(80), {invoiceColumn})) IN ({string.Join(",", parameters.Select(x => "UPPER(" + x + ")"))})
            ORDER BY {orderExpression};
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

    private sealed class OperationSummaryAccumulator
    {
        public decimal Total { get; set; }
        public HashSet<string> SaleKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Tickets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PaymentLines { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> VendorNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddSale(string key, decimal amount)
        {
            if (SaleKeys.Add(key))
            {
                SalesByKey[key] = amount;
                Total += amount;
            }
        }

        public void AddPayment(string line, decimal amount)
        {
            if (!string.IsNullOrWhiteSpace(line) && amount > 0m)
                PaymentLines.Add(line);
        }

        public void Merge(OperationSummaryAccumulator other)
        {
            foreach (var key in other.SaleKeys)
            {
                if (SaleKeys.Add(key))
                    Total += other.SalesByKey.TryGetValue(key, out var amount) ? amount : 0m;
            }

            foreach (var ticket in other.Tickets)
                Tickets.Add(ticket);
            foreach (var paymentLine in other.PaymentLines)
                PaymentLines.Add(paymentLine);
            foreach (var vendorName in other.VendorNames)
                VendorNames.Add(vendorName);
        }

        private Dictionary<string, decimal> SalesByKey { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildSaleKey(bool joyeria, string operation, string ticket, string factura, decimal sale) =>
        string.Join("|", joyeria ? "JOY" : "COMP", operation.Trim(), ticket.Trim(), factura.Trim(), sale.ToString("0.00", CultureInfo.InvariantCulture));

    private static string NormalizePaymentMethod(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(text) ? "NO ESPECIFICADA" : text;
    }

    private static string NormalizeCurrency(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text is "0" or "00" ? "PESOS" : text;
    }

    private static string BuildPaymentLine(string? paymentName, string? currencyName, decimal paymentTotal)
    {
        if (paymentTotal <= 0m)
            return string.Empty;

        var amount = paymentTotal.ToString("C2", CultureInfo.CurrentCulture);
        var currency = NormalizeCurrency(currencyName);
        var method = NormalizePaymentMethod(paymentName);
        return string.IsNullOrWhiteSpace(currency)
            ? $"{amount} | {method}"
            : $"{amount} | {currency} | {method}";
    }

    private static IReadOnlyList<LocalSalesBrowserRow> NormalizePaymentDistribution(IReadOnlyList<LocalSalesBrowserRow> rows)
    {
        if (rows.Count <= 1)
            return rows;

        return rows
            .GroupBy(row => string.Join("|", row.OrigenVenta, row.Folio, row.Factura), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var items = group.ToList();
                if (items.Count <= 1)
                    return items;

                var totalPayment = items.Max(row => row.TotalPagos);
                if (totalPayment <= 0m)
                    return items;

                var paymentSource = items.First(row => row.TotalPagos == totalPayment);
                var target = items.FirstOrDefault(row => row.Total == totalPayment) ?? paymentSource;
                var assigned = false;
                return items.Select(row =>
                {
                    if (!assigned && ReferenceEquals(row, target))
                    {
                        assigned = true;
                        return row with
                        {
                            TotalPagos = totalPayment,
                            DiferenciaPago = decimal.Round(row.Total - totalPayment, 2, MidpointRounding.AwayFromZero),
                            FormaPagoDetalle = paymentSource.FormaPagoDetalle,
                            MonedaDetalle = paymentSource.MonedaDetalle,
                            PagoDetalle = paymentSource.PagoDetalle,
                            PagoInconsistente = row.Total - totalPayment < 0m
                        };
                    }

                    return row with
                    {
                        TotalPagos = 0m,
                        DiferenciaPago = row.Total,
                        FormaPagoDetalle = "NO ESPECIFICADA",
                        MonedaDetalle = string.Empty,
                        PagoDetalle = string.Empty,
                        PagoInconsistente = false
                    };
                });
            })
            .OrderByDescending(row => row.Fecha)
            .ThenBy(row => row.TotalPagos > 0m ? 0 : 1)
            .ThenByDescending(row => row.Total)
            .ToArray();
    }

    private static string FirstFilled(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
