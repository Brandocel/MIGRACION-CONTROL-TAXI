using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed class CascoReadOnlyDataProvider
{
    private readonly BranchConfiguration _configuration;

    private const string BaseSelect = @"
            SELECT
              COALESCE(folio_app, '') AS FolioControl,
              COALESCE(folio_app_original, '') AS OriginalFolio,
              COALESCE(folio_pos, '') AS PosFolio,
              COALESCE(vendedor_nombre, '') AS DriverName,
              COALESCE(hotel, '') AS Hotel,
              COALESCE(folio_gafete, '') AS Badge,
              COALESCE(fecha_operacion, fecha_creacion, '') AS OperationDate,
              COALESCE(NULLIF(modelo_vehiculo, ''), NULLIF(tipo_operacion, ''), '') AS TransportType,
              COALESCE(total, 0) AS Total,
              COALESCE(
                  NULLIF(estado_pago_dejada, ''),
                  NULLIF(payout_status, ''),
                  CASE
                    WHEN COALESCE(fecha_pago_dejada, '') <> '' OR COALESCE(payout_date, '') <> '' THEN 'pagado'
                    ELSE 'pendiente'
                  END
              ) AS PaymentStatus,
              COALESCE(usuario_movil, '') AS Usuario,
              COALESCE(notas, '') AS Notes,
              COALESCE(origen, '') AS Origin,
              COALESCE(destino, '') AS Destination,
              COALESCE(sitio, '') AS Site,
              COALESCE(unidad, '') AS Unit,
              COALESCE(placas, '') AS Plates,
              COALESCE(nacionalidad, '') AS Nationality,
              COALESCE(NULLIF(telefono_taxista, ''), NULLIF(telefono_contacto, ''), '') AS Phone,
              COALESCE(CAST(id_catalogo AS nvarchar(60)), '') AS TaxistaId,
              COALESCE(usuario_pago_dejada, '') AS PayoutUser,
              COALESCE(CONVERT(nvarchar(30), fecha_pago_dejada, 120), CONVERT(nvarchar(30), payout_date, 120), '') AS PayoutDate,
              COALESCE(ticket_pago_dejada, '') AS PayoutTicket,
              COALESCE(efectivo, 0) AS Cash,
              COALESCE(tarjeta, 0) AS Card,
              COALESCE(pax, 0) AS Passengers,
              COALESCE(dolares, 0) AS Dollars,
              COALESCE(tipo_cambio, 0) AS ExchangeRate,
              COALESCE(comision_calculada, 0) AS CommissionCalculated,
              COALESCE(pago_comision, 0) AS CommissionPaid,
              COALESCE(detalle_json, '') AS DetailJson,
              COALESCE(pagos_json, '') AS PaymentsJson
            FROM dbo.AppMovilRegistro";

    public CascoReadOnlyDataProvider(BranchConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<LocalAppRecordRow>> GetAppRecordsAsync(string sqlPassword, DateTime? start = null, DateTime? end = null, System.Threading.CancellationToken cancellationToken = default)
    {
        var records = await GetDetailedAppRecordsAsync(sqlPassword, start, end, cancellationToken);
        return records.Select(record => new LocalAppRecordRow(
            record.FolioControl,
            record.OriginalFolio,
            record.DriverName,
            record.Hotel,
            record.Badge,
            record.OperationDate,
            record.TransportType,
            record.Total,
            record.PaymentStatus,
            record.User,
            record.Notes)).ToArray();
    }

    public async Task<IReadOnlyList<CascoAppRecordDetail>> GetDetailedAppRecordsAsync(string sqlPassword, DateTime? start = null, DateTime? end = null, System.Threading.CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(sqlPassword, cancellationToken);
        const string query = BaseSelect + @"
            WHERE sitio = @sitio
              AND (@start IS NULL OR (fecha_operacion IS NOT NULL AND CAST(fecha_operacion AS datetime2) >= @start))
              AND (@endExclusive IS NULL OR (fecha_operacion IS NOT NULL AND CAST(fecha_operacion AS datetime2) < @endExclusive))
            ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;";

        await using var command = new SqlCommand(query, connection);
        command.CommandTimeout = 90;
        command.Parameters.AddWithValue("@sitio", _configuration.SiteName);
        command.Parameters.AddWithValue("@start", start?.Date ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@endExclusive", end?.Date.AddDays(1) ?? (object)DBNull.Value);
        return await ReadDetailedRecordsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CascoAppRecordDetail>> GetDetailedRecordsByOriginalFolioAsync(string sqlPassword, string folioOriginal, System.Threading.CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(sqlPassword, cancellationToken);
        const string query = BaseSelect + @"
            WHERE sitio = @sitio
              AND folio_app_original = @folioOriginal
            ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;";

        await using var command = new SqlCommand(query, connection);
        command.CommandTimeout = 90;
        command.Parameters.AddWithValue("@sitio", _configuration.SiteName);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal?.Trim() ?? string.Empty);
        return await ReadDetailedRecordsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CascoAppRecordDetail>> GetDetailedRecordsByBadgeAsync(string sqlPassword, string badge, System.Threading.CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(sqlPassword, cancellationToken);
        const string query = BaseSelect + @"
            WHERE sitio = @sitio
              AND (
                    LTRIM(RTRIM(COALESCE(folio_gafete, ''))) = @badge
                 OR ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
                    LIKE '%,' + @badge + ',%'
                  )
            ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;";

        await using var command = new SqlCommand(query, connection);
        command.CommandTimeout = 90;
        command.Parameters.AddWithValue("@sitio", _configuration.SiteName);
        command.Parameters.AddWithValue("@badge", badge?.Trim() ?? string.Empty);
        return await ReadDetailedRecordsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<CascoBadgeControlState>> GetBadgeControlStatesAsync(string sqlPassword, System.Threading.CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(sqlPassword, cancellationToken);
        const string query = """
            WITH ranked AS
            (
                SELECT
                    CONVERT(nvarchar(50), g.gafete) AS Badge,
                    CONVERT(nvarchar(50), g.folioperacion) AS OperationFolio,
                    UPPER(COALESCE(g.venta, '')) AS RawStatus,
                    COALESCE(g.hora, g.fecha) AS ActivityDate,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY CONVERT(nvarchar(50), g.gafete), CONVERT(nvarchar(50), g.folioperacion)
                        ORDER BY COALESCE(g.hora, g.fecha) DESC
                    ) AS rn
                FROM dbo.gafete g
                WHERE COALESCE(g.gafete, '') <> ''
            )
            SELECT
                COALESCE(Badge, '') AS Badge,
                COALESCE(OperationFolio, '') AS OperationFolio,
                COALESCE(RawStatus, '') AS RawStatus,
                COALESCE(CONVERT(nvarchar(30), ActivityDate, 120), '') AS ActivityDate
            FROM ranked
            WHERE rn = 1;
            """;

        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<CascoBadgeControlState>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CascoBadgeControlState(
                reader.IsDBNull(0) ? string.Empty : Text(reader, 0),
                reader.IsDBNull(1) ? string.Empty : Text(reader, 1),
                reader.IsDBNull(2) ? string.Empty : Text(reader, 2),
                reader.IsDBNull(3) ? string.Empty : Text(reader, 3)));
        }

        return rows;
    }

    public async Task<SqlConnection> OpenConnectionAsync(string sqlPassword, System.Threading.CancellationToken cancellationToken = default)
    {
        if (!_configuration.IsReadOnly)
            throw new InvalidOperationException("El proveedor Casco solo debe usarse para sucursales de solo lectura.");

        if (string.IsNullOrWhiteSpace(sqlPassword))
            throw new InvalidOperationException("La contrasena SQL debe proporcionarse mediante la variable de entorno CASCO_SQL_PASSWORD.");

        var connection = new SqlConnection(BuildConnectionString(sqlPassword));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private string BuildConnectionString(string sqlPassword)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _configuration.SqlServer,
            InitialCatalog = _configuration.Database,
            UserID = CascoSqlIdentity.ResolveUser(_configuration),
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 30
        };

        return builder.ConnectionString;
    }

    private static async Task<IReadOnlyList<CascoAppRecordDetail>> ReadDetailedRecordsAsync(SqlCommand command, System.Threading.CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CascoAppRecordDetail>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var operationDate = reader.IsDBNull(6)
                ? string.Empty
                : reader.GetValue(6) switch
                {
                    string s => s,
                    DateTime dt => dt.ToString("s", CultureInfo.InvariantCulture),
                    DateTimeOffset dto => dto.ToString("s", CultureInfo.InvariantCulture),
                    _ => reader.GetValue(6)?.ToString() ?? string.Empty
                };

            result.Add(new CascoAppRecordDetail(
                reader.IsDBNull(0) ? string.Empty : Text(reader, 0),
                reader.IsDBNull(1) ? string.Empty : Text(reader, 1),
                reader.IsDBNull(2) ? string.Empty : Text(reader, 2),
                reader.IsDBNull(3) ? string.Empty : Text(reader, 3),
                reader.IsDBNull(4) ? string.Empty : Text(reader, 4),
                reader.IsDBNull(5) ? string.Empty : Text(reader, 5),
                operationDate,
                reader.IsDBNull(7) ? string.Empty : Text(reader, 7),
                reader.IsDBNull(8) ? 0m : Decimal(reader, 8),
                reader.IsDBNull(9) ? string.Empty : Text(reader, 9),
                reader.IsDBNull(10) ? string.Empty : Text(reader, 10),
                reader.IsDBNull(11) ? string.Empty : Text(reader, 11),
                reader.IsDBNull(12) ? string.Empty : Text(reader, 12),
                reader.IsDBNull(13) ? string.Empty : Text(reader, 13),
                reader.IsDBNull(14) ? string.Empty : Text(reader, 14),
                reader.IsDBNull(15) ? string.Empty : Text(reader, 15),
                reader.IsDBNull(16) ? string.Empty : Text(reader, 16),
                reader.IsDBNull(17) ? string.Empty : Text(reader, 17),
                reader.IsDBNull(18) ? string.Empty : Text(reader, 18),
                reader.IsDBNull(19) ? string.Empty : Text(reader, 19),
                reader.IsDBNull(20) ? string.Empty : Text(reader, 20),
                reader.IsDBNull(21) ? string.Empty : Text(reader, 21),
                reader.IsDBNull(22) ? string.Empty : Text(reader, 22),
                reader.IsDBNull(23) ? 0m : Decimal(reader, 23),
                reader.IsDBNull(24) ? 0m : Decimal(reader, 24),
                reader.IsDBNull(25) ? 0 : Convert.ToInt32(reader.GetValue(25), CultureInfo.InvariantCulture),
                reader.IsDBNull(26) ? 0m : Decimal(reader, 26),
                reader.IsDBNull(27) ? 0m : Decimal(reader, 27),
                reader.IsDBNull(28) ? 0m : Decimal(reader, 28),
                reader.IsDBNull(29) ? 0m : Decimal(reader, 29),
                reader.IsDBNull(30) ? string.Empty : Text(reader, 30),
                reader.IsDBNull(31) ? string.Empty : Text(reader, 31)));
        }

        return result;
    }

    private static string Text(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;

    private static decimal Decimal(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}

public sealed record CascoAppRecordDetail(
    string FolioControl,
    string OriginalFolio,
    string PosFolio,
    string DriverName,
    string Hotel,
    string Badge,
    string OperationDate,
    string TransportType,
    decimal Total,
    string PaymentStatus,
    string User,
    string Notes,
    string Origin,
    string Destination,
    string Site,
    string Unit,
    string Plates,
    string Nationality,
    string Phone,
    string TaxistaId,
    string PayoutUser,
    string PayoutDate,
    string PayoutTicket,
    decimal Cash,
    decimal Card,
    int Passengers,
    decimal Dollars = 0m,
    decimal ExchangeRate = 0m,
    decimal CommissionCalculated = 0m,
    decimal CommissionPaid = 0m,
    string DetailJson = "",
    string PaymentsJson = "");

public sealed record CascoBadgeControlState(
    string Badge,
    string OperationFolio,
    string RawStatus,
    string ActivityDate)
{
    public string Status => RawStatus switch
    {
        "A" => "ASIGNADO",
        "S" => "SUSPENDIDO",
        "R" => "LIBRE",
        _ => RawStatus
    };
}
