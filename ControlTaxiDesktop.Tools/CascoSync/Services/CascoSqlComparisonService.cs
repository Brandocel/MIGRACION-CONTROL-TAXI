using System.Globalization;
using System.Data;
using System.Linq;
using System.Text.Json;
using ControlTaxiDesktop.Tools.CascoSync.Configuration;
using ControlTaxiDesktop.Tools.CascoSync.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

public sealed class CascoSqlComparisonService
{
    private readonly CascoSyncOptions _options;

    public CascoSqlComparisonService(CascoSyncOptions options)
    {
        _options = options;
    }

    public async Task<CascoDryRunResult> CompareAsync(IReadOnlyList<CascoTripRecord> records, CascoSchemaSnapshot schema, CancellationToken cancellationToken)
    {
        var connectionString = BuildConnectionString();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var localRows = new List<CascoLocalRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT TOP (@limit)
    COALESCE(folio_app_original, '') AS FolioAppOriginal,
    COALESCE(folio_app, '') AS FolioApp,
    COALESCE(sitio, '') AS Sitio,
    COALESCE(id_catalogo, 0) AS IdCatalogo,
    COALESCE(folio_gafete, '') AS FolioGafete,
    COALESCE(vendedor_nombre, '') AS VendedorNombre,
    COALESCE(telefono_taxista, '') AS TelefonoTaxista,
    COALESCE(telefono_contacto, '') AS TelefonoContacto,
    COALESCE(nacionalidad, '') AS Nacionalidad,
    COALESCE(placas, '') AS Placas,
    COALESCE(modelo_vehiculo, '') AS ModeloVehiculo,
    COALESCE(unidad, '') AS Unidad,
    COALESCE(hotel, '') AS Hotel,
    COALESCE(origen, '') AS Origen,
    COALESCE(destino, '') AS Destino,
    COALESCE(pax, 0) AS Pax,
    COALESCE(tipo_operacion, '') AS TipoOperacion,
    COALESCE(subtotal, 0) AS Subtotal,
    COALESCE(iva, 0) AS Iva,
    COALESCE(total, 0) AS Total,
    COALESCE(efectivo, 0) AS Efectivo,
    COALESCE(tarjeta, 0) AS Tarjeta,
    COALESCE(estado_pago_dejada, '') AS EstadoPagoDejada,
    fecha_pago_dejada AS FechaPagoDejada,
    COALESCE(usuario_pago_dejada, '') AS UsuarioPagoDejada,
    COALESCE(ticket_pago_dejada, '') AS TicketPagoDejada,
    COALESCE(payout_status, '') AS PayoutStatus,
    payout_date AS PayoutDate,
    COALESCE(payout_user, '') AS PayoutUser,
    COALESCE(payout_ticket, '') AS PayoutTicket,
    COALESCE(detalle_json, '') AS DetalleJson,
    COALESCE(pagos_json, '') AS PagosJson,
    COALESCE(estado_sync, '') AS EstadoSync,
    fecha_creacion AS FechaCreacion,
    COALESCE(notas, '') AS Notas,
    fecha_operacion AS FechaOperacion
FROM dbo.AppMovilRegistro
WHERE sitio = @site
ORDER BY folio_app_original;";
            command.Parameters.AddWithValue("@site", "Casco Viejo");
            command.Parameters.AddWithValue("@limit", Math.Max(1, _options.RecordLimit));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                localRows.Add(new CascoLocalRow(
                    GetNullableString(reader, "FolioAppOriginal") ?? string.Empty,
                    GetNullableString(reader, "Sitio") ?? string.Empty,
                    GetNullableString(reader, "FolioApp") ?? string.Empty,
                    GetNullableInt(reader, "IdCatalogo")?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                    GetNullableString(reader, "FolioGafete") ?? string.Empty,
                    GetNullableString(reader, "VendedorNombre") ?? string.Empty,
                    GetNullableString(reader, "TelefonoTaxista") ?? string.Empty,
                    GetNullableString(reader, "TelefonoContacto") ?? string.Empty,
                    GetNullableString(reader, "Nacionalidad") ?? string.Empty,
                    GetNullableString(reader, "Placas") ?? string.Empty,
                    GetNullableString(reader, "ModeloVehiculo") ?? string.Empty,
                    GetNullableString(reader, "Unidad") ?? string.Empty,
                    GetNullableString(reader, "Hotel") ?? string.Empty,
                    GetNullableString(reader, "Origen") ?? string.Empty,
                    GetNullableString(reader, "Destino") ?? string.Empty,
                    GetNullableInt(reader, "Pax") ?? 0,
                    GetNullableString(reader, "TipoOperacion") ?? string.Empty,
                    GetNullableDecimal(reader, "Subtotal") ?? 0m,
                    GetNullableDecimal(reader, "Iva") ?? 0m,
                    GetNullableDecimal(reader, "Total") ?? 0m,
                    GetNullableDecimal(reader, "Efectivo") ?? 0m,
                    GetNullableDecimal(reader, "Tarjeta") ?? 0m,
                    GetNullableString(reader, "EstadoPagoDejada") ?? string.Empty,
                    GetNullableDateTime(reader, "FechaPagoDejada")?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
                    GetNullableString(reader, "UsuarioPagoDejada") ?? string.Empty,
                    GetNullableString(reader, "TicketPagoDejada") ?? string.Empty,
                    GetNullableString(reader, "PayoutStatus") ?? string.Empty,
                    GetNullableDateTime(reader, "PayoutDate")?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
                    GetNullableString(reader, "PayoutUser") ?? string.Empty,
                    GetNullableString(reader, "PayoutTicket") ?? string.Empty,
                    GetNullableString(reader, "DetalleJson") ?? string.Empty,
                    GetNullableString(reader, "PagosJson") ?? string.Empty,
                    GetNullableString(reader, "EstadoSync") ?? string.Empty,
                    GetNullableDateTime(reader, "FechaCreacion")?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
                    GetNullableString(reader, "Notas") ?? string.Empty,
                    GetNullableDateTime(reader, "FechaOperacion")?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty));
            }
        }

        var localLookup = new Dictionary<(string RecordId, string Site), CascoLocalRow>(new CascoKeyComparer());
        foreach (var row in localRows)
            localLookup[(row.RecordId, row.Site)] = row;
        var discrepancies = 0;
        var rows = new List<CascoDryRunRow>();
        var insertProposedCount = 0;
        var updateProposedCount = 0;
        var sinCambiosCount = 0;
        var realDiscrepanciesCount = 0;
        var errorCount = 0;
        foreach (var record in records)
        {
            var key = (record.RecordId ?? string.Empty, "Casco Viejo");
            var localRow = localLookup.TryGetValue(key, out var matched) ? matched : null;
            var action = localRow is null ? "INSERT" : "SIN CAMBIOS";
            var fieldDiscrepancies = new List<CascoDryRunFieldDiscrepancy>();
            if (localRow is not null)
            {
                var normalizedRemote = NormalizeRecord(record);
                var normalizedLocal = NormalizeLocalRow(localRow);
                foreach (var field in normalizedRemote.Keys.Intersect(normalizedLocal.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    var remoteValue = normalizedRemote[field];
                    var localValue = normalizedLocal[field];
                    var remoteType = GetRemoteFieldType(field, record);
                    var sqlType = GetSqlFieldType(field);
                    var (areEqual, reason) = CompareField(field, remoteValue, localValue, remoteType, sqlType);
                    if (!areEqual)
                    {
                        fieldDiscrepancies.Add(new CascoDryRunFieldDiscrepancy(
                            field,
                            remoteValue,
                            localValue,
                            remoteType,
                            sqlType,
                            reason,
                            IsIgnoredField(field)));
                    }
                }
            }

            if (fieldDiscrepancies.Any(x => !x.Ignored))
            {
                action = "UPDATE";
            }

            discrepancies += fieldDiscrepancies.Count(x => !x.Ignored);
            switch (action)
            {
                case "INSERT":
                    insertProposedCount++;
                    break;
                case "UPDATE":
                    updateProposedCount++;
                    realDiscrepanciesCount++;
                    break;
                case "SIN CAMBIOS":
                    sinCambiosCount++;
                    break;
                default:
                    errorCount++;
                    break;
            }

            rows.Add(new CascoDryRunRow(
                record.RecordId ?? string.Empty,
                record.DriverName ?? string.Empty,
                record.Site ?? string.Empty,
                record.RecordDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                action,
                fieldDiscrepancies.Select(x => x.FieldName).ToList(),
                fieldDiscrepancies));
        }

        return new CascoDryRunResult
        {
            Server = _options.SqlServer,
            Database = _options.SqlDatabase,
            LocalRowsFound = localRows.Count,
            LocalRowsMissing = Math.Max(0, records.Count - localRows.Count),
            InsertProposedCount = insertProposedCount,
            UpdateProposedCount = updateProposedCount,
            SinCambiosCount = sinCambiosCount,
            RealDiscrepanciesCount = realDiscrepanciesCount,
            ErrorCount = errorCount,
            RecordsWithDiscrepancies = rows.Count(x => x.Action != "SIN CAMBIOS" && x.Action != "ERROR"),
            TotalFieldsWithDiscrepancies = discrepancies,
            Rows = rows,
            Schema = schema
        };
    }

    private string BuildConnectionString() =>
        new SqlConnectionStringBuilder
        {
            DataSource = _options.SqlServer,
            InitialCatalog = _options.SqlDatabase,
            UserID = _options.SqlUser,
            Password = _options.SqlPassword ?? string.Empty,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15
        }.ConnectionString;

    private static string? GetNullableString(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;

        return Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTime? GetNullableDateTime(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;

        var value = reader.GetValue(ordinal);
        return value switch
        {
            DateTime date => date,
            DateTimeOffset offset => offset.DateTime,
            _ when DateTime.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed) => parsed,
            _ => null
        };
    }

    private static decimal? GetNullableDecimal(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;

        var value = reader.GetValue(ordinal);
        return value switch
        {
            decimal dec => dec,
            double dbl => Convert.ToDecimal(dbl, CultureInfo.InvariantCulture),
            float flt => Convert.ToDecimal(flt, CultureInfo.InvariantCulture),
            int i => Convert.ToDecimal(i, CultureInfo.InvariantCulture),
            long l => Convert.ToDecimal(l, CultureInfo.InvariantCulture),
            string text when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static int? GetNullableInt(SqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return null;

        var value = reader.GetValue(ordinal);
        return value switch
        {
            int i => i,
            long l when l >= int.MinValue && l <= int.MaxValue => Convert.ToInt32(l, CultureInfo.InvariantCulture),
            string text when int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static string NormalizeText(string? input) => input?.Trim() ?? string.Empty;

    private static string NormalizePaymentStatus(string? input) => NormalizeText(input).ToUpperInvariant();

    private static string NormalizeDecimal(decimal? value) => value.HasValue ? value.Value.ToString("G29", CultureInfo.InvariantCulture) : string.Empty;

    private static string NormalizeJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
            return string.Empty;

        if (!TryParseJsonDocument(rawJson, out var document) || document is null)
            return rawJson.Trim();

        using (document)
        {
            return JsonSerializer.Serialize(document.RootElement);
        }
    }

    private static string NormalizeLocalDateTime(DateTime? value)
    {
        if (!value.HasValue)
            return string.Empty;
        return value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static decimal DeriveCash(string? paymentMethod, decimal? tripCost)
    {
        if (string.Equals(paymentMethod?.Trim(), "Efectivo", StringComparison.OrdinalIgnoreCase))
            return tripCost ?? 0m;
        return 0m;
    }

    private static decimal DeriveCard(string? paymentMethod, decimal? tripCost)
    {
        if (string.Equals(paymentMethod?.Trim(), "Tarjeta", StringComparison.OrdinalIgnoreCase))
            return tripCost ?? 0m;
        return 0m;
    }

    private static bool TryParseJsonDocument(string rawJson, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        try
        {
            document = JsonDocument.Parse(rawJson);
            return true;
        }
        catch
        {
            document?.Dispose();
            return false;
        }
    }

    private static bool IsIgnoredField(string field)
    {
        return field switch
        {
            "folio_app" => true,
            "fecha_creacion" => true,
            "estado_sync" => true,
            _ => false
        };
    }

    private static string GetRemoteFieldType(string field, CascoTripRecord record)
    {
        return field switch
        {
            "recordId" => "string",
            "catalogId" => "string",
            "badgeId" => "string",
            "driverName" => "string",
            "driverPhone" => "string",
            "contactPhone" => "string",
            "nationality" => "string",
            "plate" => "string",
            "vehicleModel" => "string",
            "unitNumber" => "string",
            "hotel" => "string",
            "origin" => "string",
            "site" => "string",
            "destination" => "string",
            "passengerCount" => "int",
            "serviceType" => "string",
            "subtotal" => "decimal",
            "iva" => "decimal",
            "tripCost" => "decimal",
            "efectivo" => "decimal",
            "tarjeta" => "decimal",
            "notes" => "string",
            "recordDate" => "datetime",
            "estado_pago_dejada" => "string",
            "fecha_pago_dejada" => "datetime",
            "usuario_pago_dejada" => "string",
            "ticket_pago_dejada" => "string",
            "payoutStatus" => "string",
            "payoutDate" => "datetime",
            "payoutUser" => "string",
            "payoutTicket" => "string",
            _ => "string"
        };
    }

    private static string GetSqlFieldType(string field)
    {
        return field switch
        {
            "recordId" => "NVARCHAR",
            "catalogId" => "NVARCHAR",
            "badgeId" => "NVARCHAR",
            "driverName" => "NVARCHAR",
            "driverPhone" => "NVARCHAR",
            "contactPhone" => "NVARCHAR",
            "nationality" => "NVARCHAR",
            "plate" => "NVARCHAR",
            "vehicleModel" => "NVARCHAR",
            "unitNumber" => "NVARCHAR",
            "hotel" => "NVARCHAR",
            "origin" => "NVARCHAR",
            "site" => "NVARCHAR",
            "destination" => "NVARCHAR",
            "passengerCount" => "INT",
            "serviceType" => "NVARCHAR",
            "subtotal" => "DECIMAL(18,2)",
            "iva" => "DECIMAL(18,2)",
            "tripCost" => "DECIMAL(18,2)",
            "efectivo" => "DECIMAL(18,2)",
            "tarjeta" => "DECIMAL(18,2)",
            "notes" => "NVARCHAR",
            "recordDate" => "DATETIME2",
            "estado_pago_dejada" => "NVARCHAR",
            "fecha_pago_dejada" => "DATETIME2",
            "usuario_pago_dejada" => "NVARCHAR",
            "ticket_pago_dejada" => "NVARCHAR",
            "payoutStatus" => "NVARCHAR",
            "payoutDate" => "DATETIME2",
            "payoutUser" => "NVARCHAR",
            "payoutTicket" => "NVARCHAR",
            _ => "NVARCHAR"
        };
    }

    private static (bool equal, string reason) CompareField(string field, string remoteValue, string localValue, string remoteType, string sqlType)
    {
        if (IsIgnoredField(field))
            return (true, "Campo técnico ignorado");

        if (field is "site" or "payoutStatus" or "paymentMethod" or "estado_pago_dejada")
        {
            if (string.Equals(remoteValue, localValue, StringComparison.OrdinalIgnoreCase))
                return (true, "Coinciden ignorando mayúsculas/minúsculas");
            return (false, "Diferencia en texto case-insensitive");
        }

        if (field is "tripCost" or "subtotal" or "iva" or "efectivo" or "tarjeta")
        {
            if (TryParseDecimal(remoteValue, out var remoteDecimal) && TryParseDecimal(localValue, out var localDecimal))
            {
                if (decimal.Round(remoteDecimal, 2) == decimal.Round(localDecimal, 2))
                    return (true, "Coinciden como decimales con escala de SQL");
                return (false, "Diferencia decimal");
            }

            return (!string.IsNullOrWhiteSpace(remoteValue) || !string.IsNullOrWhiteSpace(localValue)) ? (false, "No se pudieron comparar como decimales") : (true, "Ambos nulos/vacíos");
        }

        if (field is "recordDate" or "payoutDate")
        {
            if (TryParseDateTime(remoteValue, out var remoteDate) && TryParseDateTime(localValue, out var localDate))
            {
                if (Math.Abs((remoteDate - localDate).TotalSeconds) < 1)
                    return (true, "Coinciden dentro de un segundo sin convertir a UTC");
                return (false, "Diferencia horaria con segundos");
            }

            return (!string.IsNullOrWhiteSpace(remoteValue) || !string.IsNullOrWhiteSpace(localValue)) ? (false, "No se pudieron comparar como fechas") : (true, "Ambos nulos/vacíos");
        }

        if (field is "detalle_json" or "pagos_json")
        {
            var remoteParsed = TryParseJsonDocument(remoteValue, out var remoteDoc);
            var localParsed = TryParseJsonDocument(localValue, out var localDoc);
            using (remoteDoc)
            using (localDoc)
            {
                if (!remoteParsed || !localParsed)
                {
                    return (!remoteParsed && !localParsed) ? (true, "Ambos JSON inválidos o vacíos") : (false, "JSON inválido en uno de los lados");
                }

                if (JsonDocumentsEqual(remoteDoc!, localDoc!))
                    return (true, "JSON semánticamente equivalente");
                return (false, "JSON diferente semánticamente");
            }
        }

        if (field is "notes" or "driverName" or "origin" or "destination" or "hotel" or "plate" or "vehicleModel" or "unitNumber" or "nationality")
        {
            if (string.Equals(remoteValue.Trim(), localValue.Trim(), StringComparison.OrdinalIgnoreCase))
                return (true, "Coinciden tras trim y comparación case-insensitive");
            return (false, "Diferencia textual");
        }

        if (string.Equals(remoteValue.Trim(), localValue.Trim(), StringComparison.OrdinalIgnoreCase))
            return (true, "Coinciden exacto tras trim");

        return (false, "Valor distinto");
    }

    private static bool TryParseDecimal(string input, out decimal value)
    {
        return decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDateTime(string input, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        return DateTime.TryParseExact(input, new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "o", "s", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static bool JsonDocumentsEqual(JsonDocument left, JsonDocument right)
    {
        return JsonElementDeepEquals(left.RootElement, right.RootElement);
    }

    private static bool JsonElementDeepEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftProperties = left.EnumerateObject().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var rightProperties = right.EnumerateObject().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (leftProperties.Count != rightProperties.Count)
                    return false;
                for (var i = 0; i < leftProperties.Count; i++)
                {
                    if (!string.Equals(leftProperties[i].Name, rightProperties[i].Name, StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (!JsonElementDeepEquals(leftProperties[i].Value, rightProperties[i].Value))
                        return false;
                }
                return true;
            case JsonValueKind.Array:
                if (left.GetArrayLength() != right.GetArrayLength())
                    return false;
                for (var i = 0; i < left.GetArrayLength(); i++)
                {
                    if (!JsonElementDeepEquals(left[i], right[i]))
                        return false;
                }
                return true;
            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.OrdinalIgnoreCase);
            case JsonValueKind.Number:
                return left.GetDecimal() == right.GetDecimal();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return left.GetBoolean() == right.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return true;
            default:
                return left.ToString() == right.ToString();
        }
    }

    private static DateTime? ParseSqlLocalClock(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
            return dto.DateTime;

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);

        return null;
    }

    private static Dictionary<string, string> NormalizeRecord(CascoTripRecord record)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["recordId"] = record.RecordId ?? string.Empty,
            ["catalogId"] = record.CatalogId ?? string.Empty,
            ["badgeId"] = record.BadgeId ?? string.Empty,
            ["driverName"] = NormalizeText(record.DriverName),
            ["driverPhone"] = NormalizeText(record.DriverPhone),
            ["contactPhone"] = NormalizeText(record.ContactPhone),
            ["nationality"] = NormalizeText(record.Nationality),
            ["plate"] = NormalizeText(record.Plate),
            ["vehicleModel"] = NormalizeText(record.VehicleModel),
            ["unitNumber"] = NormalizeText(record.UnitNumber),
            ["hotel"] = NormalizeText(record.Hotel),
            ["origin"] = NormalizeText(record.Origin),
            ["site"] = NormalizeText(record.Site),
            ["destination"] = NormalizeText(record.Destination),
            ["passengerCount"] = record.PassengerCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["serviceType"] = NormalizeText(record.ServiceType),
            ["subtotal"] = NormalizeDecimal(0m),
            ["iva"] = NormalizeDecimal(0m),
            ["tripCost"] = NormalizeDecimal(record.TripCost),
            ["efectivo"] = NormalizeDecimal(DeriveCash(record.PaymentMethod, record.TripCost)),
            ["tarjeta"] = NormalizeDecimal(DeriveCard(record.PaymentMethod, record.TripCost)),
            ["notes"] = NormalizeText(record.Notes),
            ["recordDate"] = NormalizeLocalDateTime(record.RecordDate?.DateTime),
            ["estado_pago_dejada"] = NormalizePaymentStatus(record.PayoutStatus),
            ["fecha_pago_dejada"] = NormalizeLocalDateTime(ParseSqlLocalClock(record.PayoutDate)),
            ["usuario_pago_dejada"] = NormalizeText(record.PayoutUser),
            ["ticket_pago_dejada"] = NormalizeText(record.PayoutTicket),
            ["payoutStatus"] = NormalizePaymentStatus(record.PayoutStatus),
            ["payoutDate"] = NormalizeLocalDateTime(ParseSqlLocalClock(record.PayoutDate)),
            ["payoutUser"] = NormalizeText(record.PayoutUser),
            ["payoutTicket"] = NormalizeText(record.PayoutTicket),
            ["detalle_json"] = NormalizeJson(JsonSerializer.Serialize(record)),
            ["pagos_json"] = string.Empty,
            ["estado_sync"] = "pendiente",
            ["fecha_creacion"] = string.Empty
        };
        return values;
    }

    private static Dictionary<string, string> NormalizeLocalRow(CascoLocalRow row)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["recordId"] = row.RecordId,
            ["catalogId"] = row.CatalogId,
            ["badgeId"] = row.FolioGafete,
            ["driverName"] = NormalizeText(row.VendedorNombre),
            ["driverPhone"] = NormalizeText(row.TelefonoTaxista),
            ["contactPhone"] = NormalizeText(row.TelefonoContacto),
            ["nationality"] = NormalizeText(row.Nacionalidad),
            ["plate"] = NormalizeText(row.Placas),
            ["vehicleModel"] = NormalizeText(row.ModeloVehiculo),
            ["unitNumber"] = NormalizeText(row.Unidad),
            ["hotel"] = NormalizeText(row.Hotel),
            ["origin"] = NormalizeText(row.Origen),
            ["site"] = NormalizeText(row.Site),
            ["destination"] = NormalizeText(row.Destino),
            ["passengerCount"] = row.Pax.ToString(CultureInfo.InvariantCulture),
            ["serviceType"] = NormalizeText(row.TipoOperacion),
            ["subtotal"] = NormalizeDecimal(row.Subtotal),
            ["iva"] = NormalizeDecimal(row.Iva),
            ["tripCost"] = NormalizeDecimal(row.Total),
            ["efectivo"] = NormalizeDecimal(row.Efectivo),
            ["tarjeta"] = NormalizeDecimal(row.Tarjeta),
            ["notes"] = NormalizeText(row.Notas),
            ["recordDate"] = NormalizeLocalDateTime(ParseSqlLocalClock(row.FechaOperacion)),
            ["estado_pago_dejada"] = NormalizePaymentStatus(row.EstadoPagoDejada),
            ["fecha_pago_dejada"] = NormalizeLocalDateTime(ParseSqlLocalClock(row.FechaPagoDejada)),
            ["usuario_pago_dejada"] = NormalizeText(row.UsuarioPagoDejada),
            ["ticket_pago_dejada"] = NormalizeText(row.TicketPagoDejada),
            ["payoutStatus"] = NormalizePaymentStatus(row.PayoutStatus),
            ["payoutDate"] = NormalizeLocalDateTime(ParseSqlLocalClock(row.PayoutDate)),
            ["payoutUser"] = NormalizeText(row.PayoutUser),
            ["payoutTicket"] = NormalizeText(row.PayoutTicket),
            ["detalle_json"] = NormalizeText(row.DetalleJson),
            ["pagos_json"] = NormalizeText(row.PagosJson),
            ["estado_sync"] = NormalizeText(row.EstadoSync),
            ["fecha_creacion"] = NormalizeLocalDateTime(ParseSqlLocalClock(row.FechaCreacion))
        };
    }
}

public sealed record CascoLocalRow(
    string RecordId,
    string Site,
    string FolioApp,
    string CatalogId,
    string FolioGafete,
    string VendedorNombre,
    string TelefonoTaxista,
    string TelefonoContacto,
    string Nacionalidad,
    string Placas,
    string ModeloVehiculo,
    string Unidad,
    string Hotel,
    string Origen,
    string Destino,
    int Pax,
    string TipoOperacion,
    decimal Subtotal,
    decimal Iva,
    decimal Total,
    decimal Efectivo,
    decimal Tarjeta,
    string EstadoPagoDejada,
    string FechaPagoDejada,
    string UsuarioPagoDejada,
    string TicketPagoDejada,
    string PayoutStatus,
    string PayoutDate,
    string PayoutUser,
    string PayoutTicket,
    string DetalleJson,
    string PagosJson,
    string EstadoSync,
    string FechaCreacion,
    string Notas,
    string FechaOperacion);

public sealed class CascoKeyComparer : IEqualityComparer<(string RecordId, string Site)>
{
    public bool Equals((string RecordId, string Site) x, (string RecordId, string Site) y) =>
        string.Equals(x.RecordId, y.RecordId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.Site, y.Site, StringComparison.OrdinalIgnoreCase);

    public int GetHashCode((string RecordId, string Site) obj) =>
        HashCode.Combine(obj.RecordId?.ToUpperInvariant(), obj.Site?.ToUpperInvariant());
}
