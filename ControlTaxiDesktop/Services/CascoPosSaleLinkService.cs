using System;
using System.Data;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed record CascoPosSaleLinkPreview(
    string BranchCode,
    string SqlServer,
    string MktDatabase,
    string CompuadmoDatabase,
    string JoyeriaDatabase,
    string FolioOriginal,
    int OperationNumber,
    bool AppRecordFound,
    string Driver,
    string Badge,
    int ExistingCompuadmoRows,
    int ExistingJoyeriaRows,
    int ExistingMovOperacionRows,
    bool IsSafeForLocalCasco,
    string Message);

public sealed record CascoPosSaleLinkResult(
    CascoPosSaleLinkPreview Preview,
    string Store,
    string Ticket,
    int RemisioRowsAffected,
    int MovOperacionRowsAffected,
    bool Completed);

public sealed class CascoPosSaleLinkService
{
    public static bool IsLocalCasco(BranchConfiguration branch) =>
        CascoCommissionRuleService.IsLocalCommissionEnvironment(branch, branch.Code);

    public static bool IsSupportedCasco(BranchConfiguration branch) =>
        CascoCommissionRuleService.IsCascoCommissionEnvironment(branch, branch.Code);

    public async Task<CascoPosSaleLinkPreview> PreviewAsync(
        BranchConfiguration branch,
        string sqlPassword,
        string folioOriginal,
        CancellationToken cancellationToken = default)
    {
        ValidateLocalCasco(branch, sqlPassword);
        var operationNumber = ParseOperationNumber(folioOriginal);
        var normalizedFolio = folioOriginal.Trim();

        await using var mkt = await OpenAsync(branch.SqlServer, branch.Database, sqlPassword, cancellationToken);
        var app = await ReadAppRecordAsync(mkt, normalizedFolio, branch.SiteName, cancellationToken);
        var compuRows = await CountRowsAsync(branch.SqlServer, branch.CompuadmoDatabase, sqlPassword, "folio_operacion", operationNumber, cancellationToken);
        var joyRows = await CountRowsAsync(branch.SqlServer, branch.JoyeriaDatabase, sqlPassword, "folio_operacion", operationNumber, cancellationToken);
        var movRows = await CountMovOperacionAsync(mkt, operationNumber, cancellationToken);

        return new CascoPosSaleLinkPreview(
            branch.Code,
            branch.SqlServer,
            branch.Database,
            branch.CompuadmoDatabase,
            branch.JoyeriaDatabase,
            normalizedFolio,
            operationNumber,
            app is not null,
            app?.Driver ?? string.Empty,
            app?.Badge ?? string.Empty,
            compuRows,
            joyRows,
            movRows,
            true,
            app is null
                ? "No existe AppMovilRegistro para ese folio original en Casco Viejo."
                : "Preview valido. No se realizaron escrituras.");
    }

    public async Task<CascoPosSaleLinkResult> LinkSelectedRemissionAsync(
        BranchConfiguration branch,
        string sqlPassword,
        string folioOriginal,
        LocalSalesBrowserRow sale,
        string user,
        CancellationToken cancellationToken = default)
    {
        ValidateLocalCasco(branch, sqlPassword);
        var preview = await PreviewAsync(branch, sqlPassword, folioOriginal, cancellationToken);
        if (!preview.AppRecordFound)
            throw new InvalidOperationException("No se puede enlazar la venta: no existe AppMovilRegistro con folio_app_original " + preview.FolioOriginal + ".");

        var store = NormalizeStore(sale.OrigenVenta);
        if (store is not "COMP" and not "JOY")
            throw new InvalidOperationException("Selecciona una remision real de compuadmoCasco o joyeriaCasco antes de cobrar.");

        var ticket = FirstFilled(sale.Folio, sale.Factura, sale.FolioRegistro);
        if (string.IsNullOrWhiteSpace(ticket))
            throw new InvalidOperationException("La remision seleccionada no tiene ticket identificable.");

        await using var storeConnection = await OpenAsync(
            branch.SqlServer,
            store == "JOY" ? branch.JoyeriaDatabase : branch.CompuadmoDatabase,
            sqlPassword,
            cancellationToken);
        await using var mktConnection = await OpenAsync(branch.SqlServer, branch.Database, sqlPassword, cancellationToken);

        await using var storeTransaction = (SqlTransaction)await storeConnection.BeginTransactionAsync(cancellationToken);
        await using var mktTransaction = (SqlTransaction)await mktConnection.BeginTransactionAsync(cancellationToken);
        try
        {
            var header = await ReadRemisionHeaderAsync(storeConnection, storeTransaction, store, ticket, cancellationToken)
                ?? throw new InvalidOperationException("No se encontro la remision seleccionada en " + (store == "JOY" ? branch.JoyeriaDatabase : branch.CompuadmoDatabase) + ".");

            if (header.FolioOperacion is not null && header.FolioOperacion != 0 && header.FolioOperacion != preview.OperationNumber)
                throw new InvalidOperationException($"La remision ya esta enlazada a folio_operacion {header.FolioOperacion}. No se modifica.");

            var remisioRows = await UpdateRemisionHeaderAsync(storeConnection, storeTransaction, store, ticket, preview.OperationNumber, cancellationToken);
            var movRows = await UpsertMovOperacionAsync(mktConnection, mktTransaction, preview, sale, store, user, cancellationToken);

            await storeTransaction.CommitAsync(cancellationToken);
            await mktTransaction.CommitAsync(cancellationToken);

            return new CascoPosSaleLinkResult(preview, store, ticket, remisioRows, movRows, true);
        }
        catch
        {
            await storeTransaction.RollbackAsync(cancellationToken);
            await mktTransaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void ValidateLocalCasco(BranchConfiguration branch, string sqlPassword)
    {
        if (!IsSupportedCasco(branch))
            throw new InvalidOperationException("El enlace POS de Casco requiere BranchCode CV con bases Casco validas: local mktCasco/compuadmoCasco/joyeriaCasco o produccion mkt/compuadmo/joyeria.");

        if (string.IsNullOrWhiteSpace(sqlPassword))
            throw new InvalidOperationException("Se requiere la contrasena SQL protegida de Casco para conectar.");
    }

    private static int ParseOperationNumber(string folioOriginal)
    {
        var value = (folioOriginal ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("El folio_app_original es obligatorio para enlazar la venta POS.");

        var normalized = value.TrimStart('0');
        if (string.IsNullOrWhiteSpace(normalized)
            || !int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || number <= 0)
        {
            throw new InvalidOperationException("El folio_app_original debe convertirse a entero mayor que cero. Valor recibido: " + value);
        }

        return number;
    }

    private static async Task<SqlConnection> OpenAsync(string server, string database, string password, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            UserID = CascoSqlIdentity.ResolveUser(),
            Password = password,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 10
        };

        var connection = new SqlConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<AppRecord?> ReadAppRecordAsync(SqlConnection connection, string folioOriginal, string siteName, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1)
                COALESCE(NULLIF(vendedor_nombre, ''), '') AS Driver,
                COALESCE(NULLIF(folio_gafete, ''), '') AS Badge
            FROM dbo.AppMovilRegistro
            WHERE folio_app_original = @folioOriginal
              AND (@site = '' OR sitio = @site)
            ORDER BY fecha_operacion DESC, id_app_movil_registro DESC;
            """;
        command.Parameters.Add("@folioOriginal", SqlDbType.NVarChar, 120).Value = folioOriginal;
        command.Parameters.Add("@site", SqlDbType.NVarChar, 300).Value = string.IsNullOrWhiteSpace(siteName) ? string.Empty : siteName;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new AppRecord(reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static async Task<int> CountRowsAsync(string server, string database, string password, string column, int operationNumber, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(server, database, password, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM dbo.RemisioM WHERE {column} = @folio;";
        command.Parameters.Add("@folio", SqlDbType.Int).Value = operationNumber;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<int> CountMovOperacionAsync(SqlConnection connection, int operationNumber, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM dbo.mov_operacion WHERE folioperacion = @folio;";
        command.Parameters.Add("@folio", SqlDbType.Int).Value = operationNumber;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task<RemisionHeader?> ReadRemisionHeaderAsync(SqlConnection connection, SqlTransaction transaction, string store, string ticket, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = store == "JOY"
            ? """
              SELECT TOP (1)
                  TRY_CONVERT(int, folio_operacion) AS FolioOperacion,
                  TRY_CONVERT(bigint, folio_registro) AS FolioRegistro
              FROM dbo.RemisioM WITH (UPDLOCK, HOLDLOCK)
              WHERE CONVERT(nvarchar(80), COALESCE(folio_pedido, folio_factura)) = @ticket
                 OR CONVERT(nvarchar(80), folio_factura) = @ticket
                 OR CONVERT(nvarchar(80), folio_registro) = @ticket;
              """
            : """
              SELECT TOP (1)
                  TRY_CONVERT(int, folio_operacion) AS FolioOperacion,
                  TRY_CONVERT(bigint, folioregistro) AS FolioRegistro
              FROM dbo.RemisioM WITH (UPDLOCK, HOLDLOCK)
              WHERE CONVERT(nvarchar(80), folio_remision) = @ticket
                 OR CONVERT(nvarchar(80), folio_factura) = @ticket
                 OR CONVERT(nvarchar(80), folioregistro) = @ticket;
              """;
        command.Parameters.Add("@ticket", SqlDbType.NVarChar, 80).Value = ticket.Trim();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new RemisionHeader(
                reader.IsDBNull(0) ? null : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? null : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture))
            : null;
    }

    private static async Task<int> UpdateRemisionHeaderAsync(SqlConnection connection, SqlTransaction transaction, string store, string ticket, int operationNumber, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = store == "JOY"
            ? """
              UPDATE dbo.RemisioM
              SET folio_operacion = @folio,
                  folio_registro = @folio
              WHERE (CONVERT(nvarchar(80), COALESCE(folio_pedido, folio_factura)) = @ticket
                  OR CONVERT(nvarchar(80), folio_factura) = @ticket
                  OR CONVERT(nvarchar(80), folio_registro) = @ticket)
                AND (folio_operacion IS NULL OR folio_operacion = 0 OR folio_operacion = @folio);
              """
            : """
              UPDATE dbo.RemisioM
              SET folio_operacion = @folio,
                  folioregistro = @folio
              WHERE (CONVERT(nvarchar(80), folio_remision) = @ticket
                  OR CONVERT(nvarchar(80), folio_factura) = @ticket
                  OR CONVERT(nvarchar(80), folioregistro) = @ticket)
                AND (folio_operacion IS NULL OR folio_operacion = 0 OR folio_operacion = @folio);
              """;
        command.Parameters.Add("@folio", SqlDbType.Int).Value = operationNumber;
        command.Parameters.Add("@ticket", SqlDbType.NVarChar, 80).Value = ticket.Trim();
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> UpsertMovOperacionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoPosSaleLinkPreview preview,
        LocalSalesBrowserRow sale,
        string store,
        string user,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF EXISTS (SELECT 1 FROM dbo.mov_operacion WHERE folioperacion = @folio)
            BEGIN
                UPDATE dbo.mov_operacion
                   SET totaljoyeria = CASE WHEN @store = N'JOY' THEN @total ELSE totaljoyeria END,
                       totalcompra = CASE WHEN @store = N'COMP' THEN @total ELSE totalcompra END,
                       totalefectivo = @cash,
                       totaltarjeta = @card,
                       transportetipo = @transport
                 WHERE folioperacion = @folio;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.mov_operacion
                    (foliosoluone, fecha, folioperacion, totaljoyeria, totalcompra, totalefectivo, totaltarjeta, tipo, impuestos, dejada, comision, transportetipo, porimpuestot, porimpuestoe, descuentotarjeta, descuentoefectivo, pago, fechapago, totalgastos, totalartesania, totallicor, totalfarmacia)
                VALUES
                    (@reference, @date, @folio, CASE WHEN @store = N'JOY' THEN @total ELSE 0 END, CASE WHEN @store = N'COMP' THEN @total ELSE 0 END, @cash, @card, 'V', @tax, 0, 0, @transport, 0, 0, 0, 0, 0, NULL, 0, 0, 0, 0);
            END;
            """;
        command.Parameters.Add("@folio", SqlDbType.Int).Value = preview.OperationNumber;
        command.Parameters.Add("@reference", SqlDbType.NVarChar, 20).Value = SafeText(FirstFilled(sale.Folio, sale.Factura, preview.FolioOriginal), 20);
        command.Parameters.Add("@date", SqlDbType.SmallDateTime).Value = sale.Fecha == DateTime.MinValue ? DateTime.Now : sale.Fecha;
        command.Parameters.Add("@store", SqlDbType.NVarChar, 10).Value = store;
        command.Parameters.Add("@total", SqlDbType.Decimal).Value = sale.Total;
        command.Parameters.Add("@cash", SqlDbType.Decimal).Value = sale.Efectivo;
        command.Parameters.Add("@card", SqlDbType.Decimal).Value = sale.Tarjeta;
        command.Parameters.Add("@tax", SqlDbType.Decimal).Value = sale.Iva;
        command.Parameters.Add("@transport", SqlDbType.NVarChar, 10).Value = SafeText(FirstFilled(sale.Transporte, sale.OrigenVenta, user), 10);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizeStore(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Contains("JOY", StringComparison.OrdinalIgnoreCase) ? "JOY"
            : normalized.Contains("COMP", StringComparison.OrdinalIgnoreCase) ? "COMP"
            : normalized;
    }

    private static string FirstFilled(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static string SafeText(string value, int maxLength)
    {
        var clean = value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private sealed record AppRecord(string Driver, string Badge);
    private sealed record RemisionHeader(int? FolioOperacion, long? FolioRegistro);
}
