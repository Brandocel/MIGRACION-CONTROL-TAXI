using System.Data;
using System.Globalization;
using System.Text.Json;
using ControlTaxiDesktop.Tools.CascoSync.Configuration;
using ControlTaxiDesktop.Tools.CascoSync.Logging;
using ControlTaxiDesktop.Tools.CascoSync.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

public sealed class CascoApplyOneService
{
    public async Task<CascoApplyOneResult> ApplyOneAsync(
        CascoSyncOptions options,
        IReadOnlyList<CascoTripRecord> records,
        CascoSchemaSnapshot schema,
        CascoSyncLogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.SqlServer))
            throw new InvalidOperationException("El servidor destino para Casco no puede quedar vacio.");

        if (!string.Equals(options.SqlDatabase, "mkt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La base destino debe ser exactamente mkt.");

        if (!string.Equals(options.BranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El branchCode debe ser exactamente CV.");

        var connectionString = BuildConnectionString(options);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var orderedCandidates = records
            .Where(x => string.Equals(x.AssignedBranchCode, options.BranchCode, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.Equals(x.Site, "Casco Viejo", StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x.Site, "Plaza 28", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RecordDate ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.RecordId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CascoTripRecord? remoteRecord = null;
        string? proposedFolio = null;
        foreach (var candidate in orderedCandidates)
        {
            var existingLocal = await ExistsLocalRecordAsync(connection, candidate.RecordId, candidate.Site, cancellationToken);
            if (existingLocal)
                continue;

            proposedFolio = await ResolveLocalFolioAsync(connection, candidate.RecordId, cancellationToken);
            var folioExists = await ExistsLocalFolioAsync(connection, proposedFolio, cancellationToken);
            if (folioExists)
                continue;

            if (!string.Equals(candidate.Site, "Casco Viejo", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El sitio remoto debe ser exactamente Casco Viejo.");

            remoteRecord = candidate;
            break;
        }

        if (remoteRecord is null)
        {
            Console.WriteLine("No hay registros nuevos para insertar.");
            return new CascoApplyOneResult { Applied = false, Confirmed = false };
        }

        if (string.IsNullOrWhiteSpace(options.SqlServer))
            throw new InvalidOperationException("El servidor destino para Casco no puede quedar vacio.");

        if (!string.Equals(options.SqlDatabase, "mkt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La base destino debe ser exactamente mkt.");

        if (!string.Equals(remoteRecord.Site, "Casco Viejo", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El sitio remoto debe ser exactamente Casco Viejo.");

        if (string.IsNullOrWhiteSpace(proposedFolio))
            throw new InvalidOperationException("No se pudo resolver un folio local propuesto.");

        var folioConflict = await ExistsLocalFolioAsync(connection, proposedFolio, cancellationToken);
        if (folioConflict)
            throw new InvalidOperationException($"El folio local propuesto {proposedFolio} ya existe.");

        var columnMetadata = await LoadColumnMetadataAsync(connection, cancellationToken);
        var requiredColumns = columnMetadata
            .Where(x => string.Equals(x.IsNullable, "NO", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(x.ColumnDefault))
            .Select(x => x.ColumnName)
            .Where(x => !string.Equals(x, "id_app_movil_registro", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var insertValues = BuildInsertValues(remoteRecord, columnMetadata, proposedFolio);
        var missingRequiredColumns = requiredColumns
            .Where(column => !insertValues.ContainsKey(column))
            .ToList();
        if (missingRequiredColumns.Count > 0)
            throw new InvalidOperationException($"Faltan columnas obligatorias sin default: {string.Join(", ", missingRequiredColumns)}");

        foreach (var column in requiredColumns)
        {
            var value = insertValues[column];
            if (value is null || value is DBNull || (value is string stringValue && string.IsNullOrWhiteSpace(stringValue)))
                throw new InvalidOperationException($"La columna obligatoria {column} no puede quedar vacía.");
        }

        var compatibleColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "folio_app",
            "folio_pos",
            "fecha_operacion",
            "vendedor_clave",
            "vendedor_nombre",
            "hotel",
            "pax",
            "tipo_operacion",
            "subtotal",
            "iva",
            "total",
            "efectivo",
            "tarjeta",
            "dolares",
            "tipo_cambio",
            "usuario_movil",
            "notas",
            "detalle_json",
            "pagos_json",
            "origen",
            "estado_sync",
            "fecha_creacion",
            "folio_app_original",
            "folio_gafete",
            "id_catalogo",
            "telefono_taxista",
            "telefono_contacto",
            "placas",
            "modelo_vehiculo",
            "unidad",
            "sitio",
            "destino",
            "nacionalidad",
            "estado_pago_dejada",
            "fecha_pago_dejada",
            "usuario_pago_dejada",
            "ticket_pago_dejada",
            "payout_status",
            "payout_date",
            "payout_user",
            "payout_ticket"
        };

        foreach (var column in insertValues.Keys)
        {
            if (!compatibleColumns.Contains(column))
                continue;

            var metadata = columnMetadata.FirstOrDefault(x => string.Equals(x.ColumnName, column, StringComparison.OrdinalIgnoreCase));
            if (metadata is null)
                throw new InvalidOperationException($"La columna {column} no existe en dbo.AppMovilRegistro.");

            if (!IsCompatible(metadata.DataType, insertValues[column]))
                throw new InvalidOperationException($"La columna {column} no es compatible con el valor propuesto.");
        }

        var previewValues = BuildInsertValues(remoteRecord, columnMetadata, proposedFolio);
        var previewParameters = CreateParameters(previewValues);

        Console.WriteLine($"Servidor: {options.SqlServer}");
        Console.WriteLine($"Base: {options.SqlDatabase}");
        Console.WriteLine("Sucursal: CV / Casco Viejo");
        Console.WriteLine($"RecordId remoto: {remoteRecord.RecordId}");
        Console.WriteLine($"Folio local propuesto: {proposedFolio}");
        Console.WriteLine($"Conductor: {remoteRecord.DriverName}");
        Console.WriteLine($"Fecha: {remoteRecord.RecordDate:yyyy-MM-dd HH:mm}");
        Console.WriteLine($"Total: {remoteRecord.TripCost?.ToString(CultureInfo.InvariantCulture) ?? "0"}");
        Console.WriteLine("Registros a insertar: 1");
        Console.WriteLine("SQL INSERT parametrizado:");
        Console.WriteLine(BuildInsertStatement(previewValues, includeParameters: true));
        Console.WriteLine("Parámetros:");
        foreach (var parameter in previewParameters)
        {
            Console.WriteLine($"  {parameter.ParameterName} [{parameter.SqlDbType}] = {FormatParameterValue(parameter.Value)}");
        }
        Console.WriteLine("Consulta de validación:");
        Console.WriteLine("SELECT TOP 1 folio_app, folio_app_original, vendedor_nombre, sitio, total FROM dbo.AppMovilRegistro WHERE folio_app = @folio_app AND folio_app_original = @folio_app_original;");
        Console.WriteLine("Escriba INSERTAR para continuar.");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "INSERTAR", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelado. No se realizó ningún cambio.");
            return new CascoApplyOneResult { Applied = false, Confirmed = false, RecordId = remoteRecord.RecordId, ProposedFolio = proposedFolio };
        }

        using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var preCheckCommand = connection.CreateCommand();
            preCheckCommand.Transaction = transaction;
            preCheckCommand.CommandText = @"
SELECT TOP 1 1
FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
WHERE folio_app_original = @recordId AND sitio = @site;";
            preCheckCommand.Parameters.AddWithValue("@recordId", remoteRecord.RecordId ?? string.Empty);
            preCheckCommand.Parameters.AddWithValue("@site", remoteRecord.Site ?? string.Empty);
            var alreadyExists = await preCheckCommand.ExecuteScalarAsync(cancellationToken) is not null;
            if (alreadyExists)
                throw new InvalidOperationException("El registro ya existe localmente durante la verificación final.");

            await using var folioCommand = connection.CreateCommand();
            folioCommand.Transaction = transaction;
            folioCommand.CommandText = @"
SELECT TOP 1 1
FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
WHERE folio_app = @folio_app;";
            folioCommand.Parameters.AddWithValue("@folio_app", proposedFolio);
            var folioTaken = await folioCommand.ExecuteScalarAsync(cancellationToken) is not null;
            if (folioTaken)
                throw new InvalidOperationException("El folio local ya fue tomado por otro registro.");

            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = BuildInsertStatement(previewValues, includeParameters: true);
            foreach (var parameter in CreateParameters(previewValues))
                insertCommand.Parameters.Add(parameter);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);

            await using var validationCommand = connection.CreateCommand();
            validationCommand.Transaction = transaction;
            validationCommand.CommandText = @"
SELECT TOP 1 CONCAT(CAST(folio_app AS NVARCHAR(60)), '|', CAST(folio_app_original AS NVARCHAR(60)), '|', CAST(vendedor_nombre AS NVARCHAR(4000)), '|', CAST(sitio AS NVARCHAR(4000)), '|', CAST(CAST(total AS NVARCHAR(50)) AS NVARCHAR(50)))
FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
WHERE folio_app = @folio_app AND folio_app_original = @folio_app_original;";
            validationCommand.Parameters.AddWithValue("@folio_app", proposedFolio);
            validationCommand.Parameters.AddWithValue("@folio_app_original", remoteRecord.RecordId ?? string.Empty);
            var validationValue = await validationCommand.ExecuteScalarAsync(cancellationToken);
            if (validationValue is null || validationValue is DBNull)
                throw new InvalidOperationException("La validación del INSERT no encontró el registro insertado.");

            var validationParts = Convert.ToString(validationValue)?.Split('|', 5) ?? Array.Empty<string>();
            if (validationParts.Length < 5)
                throw new InvalidOperationException("La validación del INSERT devolvió un resultado incompleto.");

            var validatedFolio = validationParts[0];
            var validatedOriginal = validationParts[1];
            var validatedVendor = validationParts[2];
            var validatedSite = validationParts[3];
            var validatedTotal = decimal.Parse(validationParts[4], CultureInfo.InvariantCulture);

            await transaction.CommitAsync(cancellationToken);
            logger.Log($"INSERT completado para {remoteRecord.RecordId} con folio {validatedFolio}");
            return new CascoApplyOneResult
            {
                Applied = true,
                Confirmed = true,
                RecordId = remoteRecord.RecordId,
                ProposedFolio = validatedFolio,
                VendorName = validatedVendor,
                Site = validatedSite,
                Total = validatedTotal
            };

        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string BuildConnectionString(CascoSyncOptions options) =>
        new SqlConnectionStringBuilder
        {
            DataSource = options.SqlServer,
            InitialCatalog = options.SqlDatabase,
            UserID = options.SqlUser,
            Password = options.SqlPassword ?? string.Empty,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15
        }.ConnectionString;

    private static async Task<bool> ExistsLocalRecordAsync(SqlConnection connection, string? recordId, string? site, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP 1 1
FROM dbo.AppMovilRegistro WITH (NOLOCK)
WHERE folio_app_original = @recordId AND sitio = @site;";
        command.Parameters.AddWithValue("@recordId", recordId ?? string.Empty);
        command.Parameters.AddWithValue("@site", site ?? string.Empty);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task<bool> ExistsLocalFolioAsync(SqlConnection connection, string folio, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP 1 1
FROM dbo.AppMovilRegistro WITH (NOLOCK)
WHERE folio_app = @folio;";
        command.Parameters.AddWithValue("@folio", folio);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task<string> ResolveLocalFolioAsync(SqlConnection connection, string? recordId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
DECLARE @recordId NVARCHAR(60) = @recordIdParam;
SELECT TOP (1) FolioControl
FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK)
WHERE FolioAppOriginal = @recordId;
";
        command.Parameters.AddWithValue("@recordIdParam", recordId ?? string.Empty);
        var existing = await command.ExecuteScalarAsync(cancellationToken);
        if (existing is not null && existing != DBNull.Value)
            return Convert.ToString(existing) ?? string.Empty;

        await using var transactionCommand = connection.CreateCommand();
        transactionCommand.CommandText = @"
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRANSACTION;

DECLARE @existente NVARCHAR(60);
SELECT TOP (1) @existente = FolioControl
FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK)
WHERE FolioAppOriginal = @recordId;

IF NULLIF(LTRIM(RTRIM(@existente)), '') IS NOT NULL
BEGIN
    COMMIT TRANSACTION;
    SELECT @existente;
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK) WHERE FolioControl = @recordId OR FolioAppOriginal = @recordId)
   AND NOT EXISTS (SELECT 1 FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK) WHERE folio_app = @recordId OR folio_app_original = @recordId)
BEGIN
    INSERT INTO dbo.AppMovilFolioControl (FolioAppOriginal, FolioControl)
    VALUES (@recordId, @recordId);

    COMMIT TRANSACTION;
    SELECT @recordId;
    RETURN;
END;

DECLARE @ultimo INT;
SELECT @ultimo = ISNULL(MAX(
    CASE
        WHEN ISNUMERIC(FolioControl) = 1 THEN CAST(FolioControl AS INT)
        WHEN FolioControl LIKE 'AP%' AND ISNUMERIC(SUBSTRING(FolioControl, 3, 20)) = 1 THEN CAST(SUBSTRING(FolioControl, 3, 20) AS INT)
        ELSE 0
    END), 0)
FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK);

SELECT @ultimo = CASE WHEN AppMax.MaxFolio > @ultimo THEN AppMax.MaxFolio ELSE @ultimo END
FROM (
    SELECT ISNULL(MAX(
        CASE
            WHEN ISNUMERIC(folio_app) = 1 THEN CAST(folio_app AS INT)
            WHEN ISNUMERIC(folio_app_original) = 1 THEN CAST(folio_app_original AS INT)
            ELSE 0
        END), 0) AS MaxFolio
    FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
) AppMax;

DECLARE @folioControl NVARCHAR(60) = RIGHT('0000' + CONVERT(NVARCHAR(20), @ultimo + 1), 4);
WHILE EXISTS (SELECT 1 FROM dbo.AppMovilFolioControl WHERE FolioControl = @folioControl OR FolioAppOriginal = @folioControl)
   OR EXISTS (SELECT 1 FROM dbo.AppMovilRegistro WHERE folio_app = @folioControl OR folio_app_original = @folioControl)
BEGIN
    SET @ultimo = @ultimo + 1;
    SET @folioControl = RIGHT('0000' + CONVERT(NVARCHAR(20), @ultimo + 1), 4);
END;

INSERT INTO dbo.AppMovilFolioControl (FolioAppOriginal, FolioControl)
VALUES (@recordId, @folioControl);

COMMIT TRANSACTION;
SELECT @folioControl;
";
        transactionCommand.Parameters.AddWithValue("@recordId", recordId ?? string.Empty);
        var result = await transactionCommand.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(result) ?? string.Empty;
    }

    private static async Task<IReadOnlyList<ColumnMetadata>> LoadColumnMetadataAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'AppMovilRegistro'
ORDER BY ORDINAL_POSITION;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var metadata = new List<ColumnMetadata>();
        while (await reader.ReadAsync(cancellationToken))
        {
            metadata.Add(new ColumnMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return metadata;
    }

    private static Dictionary<string, object?> BuildInsertValues(CascoTripRecord remoteRecord, IReadOnlyList<ColumnMetadata> metadata, string? proposedFolio = null)
    {
        var tripCost = remoteRecord.TripCost ?? 0m;
        var paymentMethod = (remoteRecord.PaymentMethod ?? string.Empty).Trim();
        decimal cash = 0m;
        decimal card = 0m;
        if (string.Equals(paymentMethod, "Efectivo", StringComparison.OrdinalIgnoreCase))
        {
            cash = tripCost;
        }
        else if (string.Equals(paymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase))
        {
            card = tripCost;
        }

        var payoutStatus = string.IsNullOrWhiteSpace(remoteRecord.PayoutStatus)
            ? "pendiente"
            : remoteRecord.PayoutStatus.Trim();

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["folio_app"] = proposedFolio ?? string.Empty,
            ["folio_pos"] = string.Empty,
            ["fecha_operacion"] = ToSqlLocalClock(remoteRecord.RecordDate) ?? DateTime.UtcNow,
            ["vendedor_clave"] = string.Empty,
            ["vendedor_nombre"] = remoteRecord.DriverName ?? string.Empty,
            ["hotel"] = remoteRecord.Hotel ?? string.Empty,
            ["pax"] = remoteRecord.PassengerCount ?? 0,
            ["tipo_operacion"] = remoteRecord.ServiceType ?? string.Empty,
            ["subtotal"] = 0m,
            ["iva"] = 0m,
            ["total"] = tripCost,
            ["efectivo"] = cash,
            ["tarjeta"] = card,
            ["dolares"] = 0m,
            ["tipo_cambio"] = 1m,
            ["usuario_movil"] = string.Empty,
            ["notas"] = remoteRecord.Notes ?? string.Empty,
            ["detalle_json"] = JsonSerializer.Serialize(remoteRecord),
            ["pagos_json"] = string.Empty,
            ["origen"] = remoteRecord.Origin ?? string.Empty,
            ["estado_sync"] = "pendiente",
            ["fecha_creacion"] = DateTime.UtcNow,
            ["folio_app_original"] = remoteRecord.RecordId ?? string.Empty,
            ["folio_gafete"] = remoteRecord.BadgeId ?? string.Empty,
            ["id_catalogo"] = TryParseInt(remoteRecord.CatalogId),
            ["telefono_taxista"] = remoteRecord.DriverPhone ?? string.Empty,
            ["telefono_contacto"] = remoteRecord.ContactPhone ?? string.Empty,
            ["placas"] = remoteRecord.Plate ?? string.Empty,
            ["modelo_vehiculo"] = remoteRecord.VehicleModel ?? string.Empty,
            ["unidad"] = remoteRecord.UnitNumber ?? string.Empty,
            ["sitio"] = remoteRecord.Site ?? string.Empty,
            ["destino"] = remoteRecord.Destination ?? string.Empty,
            ["nacionalidad"] = remoteRecord.Nationality ?? string.Empty,
            ["estado_pago_dejada"] = payoutStatus,
            ["fecha_pago_dejada"] = ParseSqlLocalClock(remoteRecord.PayoutDate),
            ["usuario_pago_dejada"] = remoteRecord.PayoutUser ?? string.Empty,
            ["ticket_pago_dejada"] = remoteRecord.PayoutTicket ?? string.Empty,
            ["payout_status"] = payoutStatus,
            ["payout_date"] = ParseSqlLocalClock(remoteRecord.PayoutDate),
            ["payout_user"] = remoteRecord.PayoutUser ?? string.Empty,
            ["payout_ticket"] = remoteRecord.PayoutTicket ?? string.Empty
        };

        return values;
    }

    private static IReadOnlyList<SqlParameter> CreateParameters(IDictionary<string, object?> values)
    {
        return values.Select(x => CreateParameter(x.Key, x.Value)).ToList();
    }

    private static SqlParameter CreateParameter(string name, object? value)
    {
        var parameter = new SqlParameter($"@{name}", value ?? DBNull.Value);
        if (value is int)
            parameter.SqlDbType = SqlDbType.Int;
        else if (value is decimal)
            parameter.SqlDbType = SqlDbType.Decimal;
        else if (value is DateTime)
            parameter.SqlDbType = SqlDbType.DateTime2;
        else if (value is DateTimeOffset)
            parameter.SqlDbType = SqlDbType.DateTime2;
        else if (value is string)
            parameter.SqlDbType = SqlDbType.NVarChar;
        else if (value is DBNull)
            parameter.SqlDbType = InferNullParameterType(parameter.ParameterName);
        else
            parameter.SqlDbType = SqlDbType.NVarChar;
        return parameter;
    }

    private static SqlDbType InferNullParameterType(string parameterName)
    {
        var normalized = parameterName.TrimStart('@').ToLowerInvariant();
        var dateTime2Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fecha_operacion",
            "payout_date",
            "fecha_pago_dejada",
            "fecha_creacion"
        };

        if (dateTime2Names.Contains(normalized) || normalized.EndsWith("_date") || normalized.Contains("fecha_"))
            return SqlDbType.DateTime2;

        return SqlDbType.NVarChar;
    }

    private static string BuildInsertStatement(IDictionary<string, object?> values, bool includeParameters = false)
    {
        var columns = values.Keys.ToList();
        var parameters = columns.Select(x => includeParameters ? $"@{x}" : x).ToList();
        return $"INSERT INTO dbo.AppMovilRegistro ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)});";
    }

    private static object? FormatParameterValue(object? value)
    {
        if (value is null || value is DBNull)
            return "NULL";
        if (value is string text)
            return $"'{text.Replace("'", "''")}'";
        if (value is DateTime dateTime)
            return $"'{dateTime:yyyy-MM-dd HH:mm:ss.fffffff}'";
        if (value is DateTimeOffset dateTimeOffset)
            return $"'{dateTimeOffset:yyyy-MM-dd HH:mm:ss.fffffff zzz}'";
        return value;
    }

    private static bool IsCompatible(string dataType, object? value)
    {
        var normalized = (dataType ?? string.Empty).ToLowerInvariant();
        if (value is null || value is DBNull)
            return true;

        return normalized switch
        {
            "nvarchar" or "varchar" or "char" or "text" => value is string,
            "int" or "smallint" or "bigint" => value is int or long or short or byte,
            "decimal" or "numeric" or "money" or "smallmoney" => value is decimal or double or float or int or long or short,
            "datetime2" or "datetime" or "date" or "time" => value is DateTime or DateTimeOffset,
            _ => true
        };
    }

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateTime? ToSqlLocalClock(DateTimeOffset? value)
    {
        return value?.DateTime;
    }

    private static DateTime? ParseSqlLocalClock(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.DateTime;
        }

        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        }

        return null;
    }
}

public sealed record CascoApplyOneResult
{
    public bool Applied { get; init; }
    public bool Confirmed { get; init; }
    public string? RecordId { get; init; }
    public string? ProposedFolio { get; init; }
    public string? VendorName { get; init; }
    public string? Site { get; init; }
    public decimal? Total { get; init; }
}
