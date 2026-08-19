using System.Globalization;
using ControlTaxiDesktop.Models;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Services;

public sealed class LocalPortalRepository(LocalDatabase database)
{
    public async Task<LocalPortalMetrics> GetDashboardAsync(LocalPortalDatabase source, DateTime date)
    {
        await using var connection = database.Open();
        var table = RemisionesTable(source);
        if (!await HasTableAsync(connection, table)) return new LocalPortalMetrics(0, 0, 0m, 0m, 0m, 0m);

        var columns = await GetColumnsAsync(connection, table);
        var day = date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dateExpr = TextExpr(columns, "fecha");
        var totalExpr = NumberExpr(columns, "total");
        var cashExpr = NumberExpr(columns, "efectivo");
        var cardExpr = NumberExpr(columns, "tarjeta");
        var dollarExpr = NumberExpr(columns, "dolares");
        var rateExpr = NumberExpr(columns, "cotizadolar", "cotizacion_dolar", "tipo_cambio");

        var operations = await ScalarLongAsync(connection, $"""SELECT COUNT(*) FROM "{table}" WHERE substr({dateExpr}, 1, 10) = $date;""", ("$date", day));
        var sales = await ScalarDecimalAsync(connection, $"""SELECT COALESCE(SUM({totalExpr}),0) FROM "{table}" WHERE substr({dateExpr}, 1, 10) = $date;""", ("$date", day));
        var payments = source == LocalPortalDatabase.JoyeriaPlaza
            ? 0m
            : await ScalarDecimalAsync(connection, $"""SELECT COALESCE(SUM({cashExpr}+{cardExpr}+({dollarExpr}*CASE WHEN {rateExpr} <= 0 THEN 1 ELSE {rateExpr} END)),0) FROM "{table}" WHERE substr({dateExpr}, 1, 10) = $date;""", ("$date", day));
        var expenses = await GetExpensesTotalAsync(connection, source, day);

        return new LocalPortalMetrics((int)operations, (int)operations, source == LocalPortalDatabase.CompuadmoPlaza ? sales : 0m, source == LocalPortalDatabase.JoyeriaPlaza ? sales : 0m, payments, expenses);
    }

    public async Task<IReadOnlyList<LocalPortalOperation>> GetOperationsAsync(LocalPortalDatabase source, DateTime date, string? query = null, int take = 200)
    {
        await using var connection = database.Open();
        var table = RemisionesTable(source);
        if (!await HasTableAsync(connection, table)) return [];

        var columns = await GetColumnsAsync(connection, table);
        var day = date.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var normalized = Normalize(query);
        var folioExpr = source == LocalPortalDatabase.JoyeriaPlaza ? TextExpr(columns, "folio_factura", "folio_pedido") : TextExpr(columns, "folio_remision", "folio_factura");
        var dateExpr = TextExpr(columns, "fecha");
        var sellerExpr = TextExpr(columns, "vendedor", "usuario", "cajero");
        var customerExpr = TextExpr(columns, "cliente", "observaciones");
        var paxExpr = source == LocalPortalDatabase.JoyeriaPlaza ? NumberExpr(columns, "cuantosvend", "cuantos_vend") : "0";
        var totalExpr = NumberExpr(columns, "total");
        var balanceExpr = NumberExpr(columns, "saldo");
        var cashExpr = NumberExpr(columns, "efectivo");
        var cardExpr = NumberExpr(columns, "tarjeta");
        var processedExpr = TextExpr(columns, "procesado");
        var paymentExpr = source == LocalPortalDatabase.JoyeriaPlaza
            ? $"'Procesado ' || {processedExpr}"
            : $"'Efectivo ' || ROUND({cashExpr}, 2) || ' / Tarjeta ' || ROUND({cardExpr}, 2)";

        var sql = $"""
            SELECT {folioExpr},
                   {dateExpr},
                   {sellerExpr},
                   {customerExpr},
                   {paxExpr},
                   {totalExpr},
                   {balanceExpr},
                   {paymentExpr}
            FROM "{table}"
            WHERE substr({dateExpr}, 1, 10) = $date
              AND ($query IS NULL OR {folioExpr} LIKE $like OR {sellerExpr} LIKE $like OR {customerExpr} LIKE $like)
            ORDER BY {dateExpr} DESC
            LIMIT $take;
            """;
        return await ReadAsync(connection, sql, row => new LocalPortalOperation(row.GetString(0), DateOrNull(row, 1), row.GetString(2), row.GetString(3), Convert.ToInt32(row.GetValue(4), CultureInfo.InvariantCulture), Decimal(row, 5), Decimal(row, 6), row.GetString(7)), ("$date", day), ("$query", normalized), ("$like", normalized is null ? null : "%" + normalized + "%"), ("$take", take));
    }

    public async Task<IReadOnlyList<LocalPortalCommission>> GetCommissionsAsync(LocalPortalDatabase source, string? query = null)
    {
        await using var connection = database.Open();
        var normalized = Normalize(query);
        var table = CommissionTable(source);
        if (!await HasTableAsync(connection, table))
        {
            return await ReadAsync(connection, """
                SELECT VentaFolio, Fecha, Taxista, Usuario, TotalVenta, ImporteComision
                FROM LocalComisiones
                WHERE $query IS NULL OR VentaFolio LIKE $like OR Taxista LIKE $like
                ORDER BY Fecha DESC
                LIMIT 200;
                """, row => new LocalPortalCommission(row.GetString(0), DateOrNull(row, 1), row.GetString(2), row.GetString(3), Decimal(row, 4), Decimal(row, 5)), ("$query", normalized), ("$like", normalized is null ? null : "%" + normalized + "%"));
        }

        var columns = await GetColumnsAsync(connection, table);
        var folioExpr = TextExpr(columns, "folio_remision", "folio_factura", "folio");
        var dateExpr = TextExpr(columns, "fecha", "fecha_venta");
        var nameExpr = TextExpr(columns, "nombre", "beneficiario");
        var sellerExpr = TextExpr(columns, "vendedor");
        var baseExpr = SumExpr(NumberExpr(columns, "sumafijo", "importe_fijo", "importe"), NumberExpr(columns, "sumadepor"));
        var commissionExpr = SumExpr(NumberExpr(columns, "comisionfijo", "comision_fija", "comision"), NumberExpr(columns, "comisiondepor"));

        return await ReadAsync(connection, $"""
            SELECT {folioExpr},
                   {dateExpr},
                   {nameExpr},
                   {sellerExpr},
                   {baseExpr},
                   {commissionExpr}
            FROM "{table}"
            WHERE $query IS NULL OR {folioExpr} LIKE $like OR {nameExpr} LIKE $like OR {sellerExpr} LIKE $like
            ORDER BY {dateExpr} DESC
            LIMIT 200;
            """, row => new LocalPortalCommission(row.GetString(0), DateOrNull(row, 1), row.GetString(2), row.GetString(3), Decimal(row, 4), Decimal(row, 5)), ("$query", normalized), ("$like", normalized is null ? null : "%" + normalized + "%"));
    }

    public async Task<IReadOnlyList<LocalPortalVendor>> GetVendorsAsync(LocalPortalDatabase source, string? query = null)
    {
        await using var connection = database.Open();
        var table = VendorTable(source);
        if (!await HasTableAsync(connection, table)) return [];
        var columns = await GetColumnsAsync(connection, table);
        var normalized = Normalize(query);
        var keyExpr = TextExpr(columns, "Vendedor", "vendedor", "clave", "cve_vendedor");
        var nameExpr = TextExpr(columns, "Nombre", "nombre");
        var phoneExpr = TextExpr(columns, "Telefono", "telefono", "tel");
        var commissionExpr = NumberExpr(columns, "Porc_Comis", "porc_comis", "comision", "porc_comision");

        return await ReadAsync(connection, $"""
            SELECT {keyExpr}, {nameExpr}, {phoneExpr}, {commissionExpr}
            FROM "{table}"
            WHERE $query IS NULL OR {keyExpr} LIKE $like OR {nameExpr} LIKE $like
            ORDER BY {nameExpr}
            LIMIT 200;
            """, row => new LocalPortalVendor(row.GetString(0), row.GetString(1), row.GetString(2), Decimal(row, 3)), ("$query", normalized), ("$like", normalized is null ? null : "%" + normalized + "%"));
    }

    public async Task<IReadOnlyList<LocalPortalProduct>> GetProductsAsync(LocalPortalDatabase source, string? query = null)
    {
        await using var connection = database.Open();
        var table = ProductTable(source);
        if (!await HasTableAsync(connection, table)) return [];
        var columns = await GetColumnsAsync(connection, table);
        var normalized = Normalize(query);
        var idExpr = NumberExpr(columns, "Producto", "producto", "id_producto", "idproducto", "id");
        var codeExpr = TextExpr(columns, "codigobarra", "codigo_barras", "codigo", "clave", "Producto", "producto");
        var nameExpr = TextExpr(columns, "Nombre", "nombre", "descripcion");
        var departmentExpr = TextExpr(columns, "depor", "departamento", "depto");
        var priceExpr = NumberExpr(columns, "precio1", "preciopub", "precio_publico", "precio");
        var ivaExpr = NumberExpr(columns, "iva");
        var currencyExpr = TextExpr(columns, ["moneda"], "MXN");
        var activeExpr = TextExpr(columns, ["activo"], "S");

        return await ReadAsync(connection, $"""
            SELECT {idExpr}, {codeExpr}, {nameExpr}, {departmentExpr}, {priceExpr}, {ivaExpr}, {currencyExpr}
            FROM "{table}"
            WHERE {activeExpr} <> 'N'
              AND ($query IS NULL OR {codeExpr} LIKE $like OR {nameExpr} LIKE $like)
            ORDER BY {nameExpr}
            LIMIT 200;
            """, row => new LocalPortalProduct(Convert.ToInt32(row.GetValue(0), CultureInfo.InvariantCulture), row.GetString(1), row.GetString(2), row.GetString(3), Decimal(row, 4), Decimal(row, 5), row.GetString(6)), ("$query", normalized), ("$like", normalized is null ? null : "%" + normalized + "%"));
    }

    public async Task<IReadOnlyList<LocalPortalGuide>> GetGuidesAsync(LocalPortalDatabase source)
    {
        await using var connection = database.Open();
        var table = GuideTable(source);
        if (await HasTableAsync(connection, table))
        {
            var columns = await GetColumnsAsync(connection, table);
            var keyExpr = TextExpr(columns, "guia", "codigo", "clave");
            var nameExpr = TextExpr(columns, "nombre", "Nombre");
            var companyExpr = TextExpr(columns, "empresa", "Empresa");
            var phoneExpr = TextExpr(columns, "telefono", "Telefono");
            return await ReadAsync(connection, $"""SELECT {keyExpr}, {nameExpr}, {companyExpr}, {phoneExpr} FROM "{table}" ORDER BY {nameExpr};""", row => new LocalPortalGuide(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3)));
        }

        return await ReadAsync(connection, "SELECT Clave, Nombre, '', Telefono FROM LocalGuias ORDER BY Nombre;", row => new LocalPortalGuide(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3)));
    }

    public async Task<IReadOnlyList<LocalPortalTransport>> GetTransportsAsync(LocalPortalDatabase source)
    {
        await using var connection = database.Open();
        var table = TransportTable(source);
        if (await HasTableAsync(connection, table))
        {
            var columns = await GetColumnsAsync(connection, table);
            var codeExpr = TextExpr(columns, "codigotrans", "codigo", "clave");
            var nameExpr = TextExpr(columns, "nombretransporte", "nombre", "Nombre");
            return await ReadAsync(connection, $"""SELECT {codeExpr}, {nameExpr} FROM "{table}" ORDER BY {nameExpr};""", row => new LocalPortalTransport(row.GetString(0), row.GetString(1)));
        }

        return await ReadAsync(connection, "SELECT Clave, Nombre FROM LocalTransportes ORDER BY Nombre;", row => new LocalPortalTransport(row.GetString(0), row.GetString(1)));
    }

    public async Task<string> CreateOperationAsync(LocalPortalCreateOperationInput input, string user)
    {
        if (string.IsNullOrWhiteSpace(input.SellerKey)) throw new ArgumentException("El vendedor es obligatorio.");
        if (string.IsNullOrWhiteSpace(input.Hotel)) throw new ArgumentException("El cliente/hotel es obligatorio.");
        if (input.PassengerCount is < 1 or > 100) throw new ArgumentException("Los pasajeros deben estar entre 1 y 100.");
        if (input.Subtotal < 0 || input.Tax < 0 || input.Cash < 0 || input.Card < 0 || input.Dollars < 0) throw new ArgumentException("Los importes no pueden ser negativos.");
        if (input.ExchangeRate <= 0) throw new ArgumentException("El tipo de cambio debe ser mayor a cero.");

        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var folio = $"WEB-{input.SaleDate:yyyyMMddHHmmss}";
        var total = input.Subtotal + input.Tax;
        var paid = input.Cash + input.Card + input.Dollars * input.ExchangeRate;
        var notes = BuildNotes(input);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocalVentas (Folio,Fecha,Cliente,Estatus,Subtotal,Iva,Total,Pagado,EstadoPago,Usuario)
            VALUES ($folio,$date,$customer,'Cobrada',$subtotal,$tax,$total,$paid,$status,$user)
            ON CONFLICT(Folio) DO UPDATE SET Cliente=excluded.Cliente,Subtotal=excluded.Subtotal,Iva=excluded.Iva,Total=excluded.Total,Pagado=excluded.Pagado,EstadoPago=excluded.EstadoPago,Usuario=excluded.Usuario;
            INSERT INTO LocalRegistros (Folio,Fecha,Gafete,Pax,Origen,Destino,Importe,MetodoPago,Notas,Usuario)
            VALUES ($folio,$date,'',$pax,'Portal',$hotel,$total,$method,$notes,$user)
            ON CONFLICT(Folio) DO UPDATE SET Pax=excluded.Pax,Importe=excluded.Importe,MetodoPago=excluded.MetodoPago,Notas=excluded.Notas,Usuario=excluded.Usuario;
            INSERT INTO LocalAuditoria (Fecha,Usuario,Modulo,Accion,Referencia,Descripcion,BaseDatos,Tabla,FolioOperacion,Importe,Exito,Equipo,Aplicacion,Detalles)
            VALUES ($now,$user,'Portal','Crear operacion',$folio,'Operacion portal offline','SQLite','LocalVentas',$folio,$total,1,$machine,'ControlTaxiDesktop',$notes);
            """;
        command.Parameters.AddWithValue("$folio", folio);
        command.Parameters.AddWithValue("$date", input.SaleDate.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$customer", input.Hotel.Trim());
        command.Parameters.AddWithValue("$subtotal", input.Subtotal);
        command.Parameters.AddWithValue("$tax", input.Tax);
        command.Parameters.AddWithValue("$total", total);
        command.Parameters.AddWithValue("$paid", paid);
        command.Parameters.AddWithValue("$status", paid >= total ? "Pagado" : "Parcial");
        command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(input.UserName) ? user : input.UserName.Trim());
        command.Parameters.AddWithValue("$pax", input.PassengerCount);
        command.Parameters.AddWithValue("$hotel", input.Hotel.Trim());
        command.Parameters.AddWithValue("$method", input.Card > 0 ? "Tarjeta" : "Efectivo");
        command.Parameters.AddWithValue("$notes", notes);
        command.Parameters.AddWithValue("$now", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$machine", Environment.MachineName);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return folio;
    }

    private static string RemisionesTable(LocalPortalDatabase source) => source == LocalPortalDatabase.JoyeriaPlaza ? "joyeria__dbo__remisioM" : "compuadmo__dbo__remisioM";
    private static string VendorTable(LocalPortalDatabase source) => source == LocalPortalDatabase.JoyeriaPlaza ? "joyeria__dbo__vendedor" : "compuadmo__dbo__vendedor";
    private static string ProductTable(LocalPortalDatabase source) => source == LocalPortalDatabase.JoyeriaPlaza ? "joyeria__dbo__Productos" : "compuadmo__dbo__Productos";
    private static string CommissionTable(LocalPortalDatabase source) => source == LocalPortalDatabase.JoyeriaPlaza ? "joyeria__dbo__ventascom" : "compuadmo__dbo__ventascom";
    private static string ExpenseTable(LocalPortalDatabase source) => source == LocalPortalDatabase.JoyeriaPlaza ? "joyeria__dbo__caja" : "compuadmo__dbo__caja";
    private static string TransportTable(LocalPortalDatabase source) => source == LocalPortalDatabase.JoyeriaPlaza ? "joyeria__dbo__catrans" : "compuadmo__dbo__catrans";
    private static string GuideTable(LocalPortalDatabase source) => source == LocalPortalDatabase.JoyeriaPlaza ? "joyeria__dbo__guias" : "compuadmo__dbo__guias";
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildNotes(LocalPortalCreateOperationInput input)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.Notes)) parts.Add(input.Notes.Trim());
        if (!string.IsNullOrWhiteSpace(input.GuideCode)) parts.Add($"GUIA:{input.GuideCode.Trim()}");
        if (!string.IsNullOrWhiteSpace(input.TransportCode)) parts.Add($"TRANS:{input.TransportCode.Trim()}");
        parts.Add($"PAX:{input.PassengerCount}");
        parts.Add($"TIPO:{input.OperationType.Trim()}");
        parts.Add($"VENDEDOR:{input.SellerKey.Trim()}");
        return string.Join(" | ", parts);
    }

    private static async Task<decimal> GetExpensesTotalAsync(SqliteConnection connection, LocalPortalDatabase source, string day)
    {
        var table = ExpenseTable(source);
        if (!await HasTableAsync(connection, table)) return 0m;
        var columns = await GetColumnsAsync(connection, table);
        var dateExpr = TextExpr(columns, "fecha", "fecha_gasto");
        var totalExpr = NumberExpr(columns, "egresototal", "total", "importe");
        return await ScalarDecimalAsync(connection, $"""SELECT COALESCE(SUM({totalExpr}),0) FROM "{table}" WHERE substr({dateExpr}, 1, 10) = $date;""", ("$date", day));
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""PRAGMA table_info("{table}");""";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync()) result.Add(reader.GetString(1));
        return result;
    }

    private static string TextExpr(HashSet<string> columns, params string[] candidates) => TextExpr(columns, candidates, string.Empty);

    private static string TextExpr(HashSet<string> columns, string[] candidates, string fallback)
    {
        var existing = candidates.Where(columns.Contains).Select(QuoteColumn).ToArray();
        if (existing.Length == 0) return $"'{SqlLiteral(fallback)}'";
        return $"CAST(COALESCE({string.Join(", ", existing)}, '{SqlLiteral(fallback)}') AS TEXT)";
    }

    private static string NumberExpr(HashSet<string> columns, params string[] candidates)
    {
        var existing = candidates.Where(columns.Contains).Select(QuoteColumn).ToArray();
        if (existing.Length == 0) return "0";
        return $"COALESCE({string.Join(", ", existing)}, 0)";
    }

    private static string SumExpr(params string[] expressions) => string.Join(" + ", expressions.Select(x => $"({x})"));
    private static string QuoteColumn(string column) => "\"" + column.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string SqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static async Task<IReadOnlyList<T>> ReadAsync<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync()) result.Add(map(reader));
        return result;
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
        Convert.ToInt64(await ScalarAsync(connection, sql, parameters), CultureInfo.InvariantCulture);

    private static async Task<decimal> ScalarDecimalAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters) =>
        Convert.ToDecimal(await ScalarAsync(connection, sql, parameters), CultureInfo.InvariantCulture);

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteScalarAsync();
    }

    private static decimal Decimal(SqliteDataReader row, int index) => Convert.ToDecimal(row.GetValue(index), CultureInfo.InvariantCulture);

    private static DateTime? DateOrNull(SqliteDataReader row, int index) =>
        DateTime.TryParse(Convert.ToString(row.GetValue(index), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;
}
