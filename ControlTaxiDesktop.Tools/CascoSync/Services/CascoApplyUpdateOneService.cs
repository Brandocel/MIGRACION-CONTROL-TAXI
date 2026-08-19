using System.Data;
using System.Globalization;
using System.Text;
using ControlTaxiDesktop.Tools.CascoSync.Configuration;
using ControlTaxiDesktop.Tools.CascoSync.Logging;
using ControlTaxiDesktop.Tools.CascoSync.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

public sealed class CascoApplyUpdateOneService
{
    private static readonly string[] AllowedColumns =
    [
        "id_catalogo",
        "folio_gafete",
        "vendedor_nombre",
        "telefono_taxista",
        "telefono_contacto",
        "nacionalidad",
        "placas",
        "modelo_vehiculo",
        "unidad",
        "hotel",
        "origen",
        "sitio",
        "destino",
        "pax",
        "tipo_operacion",
        "subtotal",
        "iva",
        "total",
        "efectivo",
        "tarjeta",
        "notas",
        "detalle_json",
        "fecha_operacion",
        "estado_pago_dejada",
        "fecha_pago_dejada",
        "usuario_pago_dejada",
        "ticket_pago_dejada",
        "payout_status",
        "payout_date",
        "payout_user",
        "payout_ticket"
    ];

    public async Task<CascoApplyUpdateOneResult> ApplyUpdateOneAsync(
        CascoSyncOptions options,
        IReadOnlyList<CascoTripRecord> records,
        CascoSchemaSnapshot schema,
        CascoSyncLogger logger,
        bool simulateUpdate,
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

        var dryRunRecords = orderedCandidates.Take(options.RecordLimit).ToList();
        if (simulateUpdate)
        {
            var targetRecord = dryRunRecords.FirstOrDefault(x => string.Equals(x.RecordId, "0002", StringComparison.OrdinalIgnoreCase));
            if (targetRecord is not null)
            {
                var simulatedRecord = CloneTripRecord(targetRecord);
                simulatedRecord.Notes = "PRUEBA UPDATE SIMULADO";
                var index = dryRunRecords.FindIndex(x => string.Equals(x.RecordId, targetRecord.RecordId, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    dryRunRecords[index] = simulatedRecord;
            }
        }

        var comparisonService = new CascoSqlComparisonService(options);
        var dryRunResult = await comparisonService.CompareAsync(dryRunRecords, schema, cancellationToken);

        var targetRow = dryRunResult.Rows.FirstOrDefault(x =>
            string.Equals(x.Action, "UPDATE", StringComparison.OrdinalIgnoreCase)
            && x.FieldDiscrepancies.Any(y => !y.Ignored));

        if (targetRow is null)
        {
            Console.WriteLine("No hay registros con cambios para actualizar.");
            return new CascoApplyUpdateOneResult { Applied = false, Confirmed = false, RecordId = null, ProposedFolio = null };
        }

        var remoteRecord = dryRunRecords.FirstOrDefault(x => string.Equals(x.RecordId, targetRow.RecordId, StringComparison.OrdinalIgnoreCase));
        if (remoteRecord is null)
        {
            Console.WriteLine("No hay registros con cambios para actualizar.");
            return new CascoApplyUpdateOneResult { Applied = false, Confirmed = false, RecordId = targetRow.RecordId, ProposedFolio = null };
        }

        var localRecord = await LoadExistingLocalRecordAsync(connection, remoteRecord.RecordId, remoteRecord.Site, cancellationToken);
        if (localRecord is null)
        {
            Console.WriteLine("No hay registros con cambios para actualizar.");
            return new CascoApplyUpdateOneResult { Applied = false, Confirmed = false, RecordId = remoteRecord.RecordId, ProposedFolio = null };
        }

        var discrepancies = targetRow.FieldDiscrepancies
            .Where(x => !x.Ignored)
            .Where(x => TryGetSqlColumnName(x.FieldName, out _))
            .ToList();

        if (discrepancies.Count == 0)
        {
            Console.WriteLine("No hay registros con cambios para actualizar.");
            return new CascoApplyUpdateOneResult { Applied = false, Confirmed = false, RecordId = remoteRecord.RecordId, ProposedFolio = localRecord.FolioApp };
        }

        Console.WriteLine("Servidor: REYNA");
        Console.WriteLine($"Base: {options.SqlDatabase}");
        Console.WriteLine("Sucursal: CV / Casco Viejo");
        Console.WriteLine($"RecordId: {remoteRecord.RecordId}");
        Console.WriteLine($"Folio local: {localRecord.FolioApp}");
        Console.WriteLine($"Conductor: {remoteRecord.DriverName}");
        Console.WriteLine($"Campos a actualizar: {discrepancies.Count}");

        foreach (var discrepancy in discrepancies)
        {
            if (!TryGetSqlColumnName(discrepancy.FieldName, out var sqlColumnName))
                continue;

            Console.WriteLine($"columna = {sqlColumnName}");
            Console.WriteLine($"valor local = {discrepancy.LocalValue}");
            Console.WriteLine($"valor remoto = {discrepancy.RemoteValue}");
        }

        Console.WriteLine("Escriba ACTUALIZAR para continuar.");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "ACTUALIZAR", StringComparison.Ordinal))
        {
            Console.WriteLine("Cancelado. No se realizó ningún cambio.");
            return new CascoApplyUpdateOneResult { Applied = false, Confirmed = false, RecordId = remoteRecord.RecordId, ProposedFolio = localRecord.FolioApp };
        }

        if (simulateUpdate)
        {
            Console.WriteLine("No se permite aplicar actualizaciones simuladas.");
            return new CascoApplyUpdateOneResult { Applied = false, Confirmed = false, RecordId = remoteRecord.RecordId, ProposedFolio = localRecord.FolioApp };
        }

        var updateColumns = discrepancies
            .Select(x => new { Discrepancy = x, SqlColumn = TryGetSqlColumnName(x.FieldName, out var columnName) ? columnName : null })
            .Where(x => !string.IsNullOrWhiteSpace(x.SqlColumn))
            .Select(x => new { x.Discrepancy, SqlColumn = x.SqlColumn! })
            .ToList();

        if (updateColumns.Count == 0)
        {
            Console.WriteLine("No hay columnas sincronizables disponibles para actualizar.");
            return new CascoApplyUpdateOneResult { Applied = false, Confirmed = false, RecordId = remoteRecord.RecordId, ProposedFolio = localRecord.FolioApp };
        }

        var updateParameters = new List<SqlParameter>();
        var statementBuilder = new StringBuilder();
        statementBuilder.AppendLine("UPDATE dbo.AppMovilRegistro");
        statementBuilder.AppendLine("SET");
        for (var index = 0; index < updateColumns.Count; index++)
        {
            var updateColumn = updateColumns[index];
            var parameterName = $"@{updateColumn.SqlColumn}_{index}";
            var parameter = CreateParameter(parameterName, updateColumn.SqlColumn, updateColumn.Discrepancy.RemoteValue);
            updateParameters.Add(parameter);
            statementBuilder.Append($"  {updateColumn.SqlColumn} = {parameterName}");
            if (index < updateColumns.Count - 1)
                statementBuilder.AppendLine(",");
            else
                statementBuilder.AppendLine();
        }

        statementBuilder.AppendLine("WHERE folio_app_original = @folio_app_original");
        statementBuilder.AppendLine("  AND sitio = @sitio;");

        updateParameters.Add(new SqlParameter("@folio_app_original", SqlDbType.NVarChar, 200) { Value = remoteRecord.RecordId ?? string.Empty });
        updateParameters.Add(new SqlParameter("@sitio", SqlDbType.NVarChar, 200) { Value = remoteRecord.Site ?? string.Empty });

        Console.WriteLine("SQL UPDATE parametrizado:");
        Console.WriteLine(statementBuilder.ToString());
        Console.WriteLine("Parámetros:");
        foreach (var parameter in updateParameters)
            Console.WriteLine($"  {parameter.ParameterName} [{parameter.SqlDbType}] = {FormatParameterValue(parameter.Value)}");

        Console.WriteLine("Columnas permitidas:");
        foreach (var column in AllowedColumns)
            Console.WriteLine($"- {column}");

        Console.WriteLine("Validación posterior:");
        Console.WriteLine("SELECT TOP 1 folio_app, folio_app_original, vendedor_nombre, sitio, total FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK) WHERE folio_app_original = @folio_app_original AND sitio = @sitio;");
        Console.WriteLine("Comparar nuevamente: si sigue habiendo discrepancias, rollback; si queda SIN CAMBIOS, commit.");
        Console.WriteLine("No se ejecuta actualización en esta fase.");

        logger.Log($"apply-update-one: previsualización preparada para {remoteRecord.RecordId} con folio {localRecord.FolioApp}");
        return new CascoApplyUpdateOneResult
        {
            Applied = false,
            Confirmed = true,
            RecordId = remoteRecord.RecordId,
            ProposedFolio = localRecord.FolioApp
        };
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

    private static async Task<CascoLocalRecordSnapshot?> LoadExistingLocalRecordAsync(
        SqlConnection connection,
        string? recordId,
        string? site,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT TOP 1
    COALESCE(folio_app, '') AS FolioApp,
    COALESCE(vendedor_nombre, '') AS VendedorNombre,
    COALESCE(sitio, '') AS Sitio
FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
WHERE folio_app_original = @folio_app_original
  AND sitio = @sitio;";
        command.Parameters.AddWithValue("@folio_app_original", recordId ?? string.Empty);
        command.Parameters.AddWithValue("@sitio", site ?? string.Empty);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new CascoLocalRecordSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static bool TryGetSqlColumnName(string fieldName, out string? sqlColumnName)
    {
        sqlColumnName = fieldName switch
        {
            "catalogId" => "id_catalogo",
            "badgeId" => "folio_gafete",
            "driverName" => "vendedor_nombre",
            "driverPhone" => "telefono_taxista",
            "contactPhone" => "telefono_contacto",
            "nationality" => "nacionalidad",
            "plate" => "placas",
            "vehicleModel" => "modelo_vehiculo",
            "unitNumber" => "unidad",
            "hotel" => "hotel",
            "origin" => "origen",
            "site" => "sitio",
            "destination" => "destino",
            "passengerCount" => "pax",
            "serviceType" => "tipo_operacion",
            "subtotal" => "subtotal",
            "iva" => "iva",
            "tripCost" => "total",
            "efectivo" => "efectivo",
            "tarjeta" => "tarjeta",
            "notes" => "notas",
            "recordDate" => "fecha_operacion",
            "estado_pago_dejada" => "estado_pago_dejada",
            "fecha_pago_dejada" => "fecha_pago_dejada",
            "usuario_pago_dejada" => "usuario_pago_dejada",
            "ticket_pago_dejada" => "ticket_pago_dejada",
            "payoutStatus" => "payout_status",
            "payoutDate" => "payout_date",
            "payoutUser" => "payout_user",
            "payoutTicket" => "payout_ticket",
            "detalle_json" => "detalle_json",
            _ => null
        };

        if (sqlColumnName is null)
            return false;

        return AllowedColumns.Contains(sqlColumnName, StringComparer.OrdinalIgnoreCase);
    }

    private static SqlParameter CreateParameter(string parameterName, string columnName, string rawValue)
    {
        if (string.Equals(columnName, "subtotal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnName, "iva", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnName, "total", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnName, "efectivo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnName, "tarjeta", StringComparison.OrdinalIgnoreCase))
        {
            return new SqlParameter(parameterName, SqlDbType.Decimal) { Value = decimal.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericValue) ? numericValue : 0m };
        }

        if (string.Equals(columnName, "pax", StringComparison.OrdinalIgnoreCase))
            return new SqlParameter(parameterName, SqlDbType.Int) { Value = int.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var integerValue) ? integerValue : 0 };

        if (string.Equals(columnName, "fecha_operacion", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnName, "fecha_pago_dejada", StringComparison.OrdinalIgnoreCase)
            || string.Equals(columnName, "payout_date", StringComparison.OrdinalIgnoreCase))
        {
            return new SqlParameter(parameterName, SqlDbType.DateTime2) { Value = DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue) ? dateValue : DBNull.Value };
        }

        return new SqlParameter(parameterName, SqlDbType.NVarChar, 4000) { Value = string.IsNullOrWhiteSpace(rawValue) ? string.Empty : rawValue };
    }

    private static string FormatParameterValue(object? value)
    {
        if (value is DBNull or null)
            return "<null>";

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static CascoTripRecord CloneTripRecord(CascoTripRecord source)
    {
        return new CascoTripRecord
        {
            RecordId = source.RecordId,
            CatalogId = source.CatalogId,
            BadgeId = source.BadgeId,
            DriverName = source.DriverName,
            DriverPhone = source.DriverPhone,
            ContactPhone = source.ContactPhone,
            Nationality = source.Nationality,
            Plate = source.Plate,
            VehicleModel = source.VehicleModel,
            UnitNumber = source.UnitNumber,
            Hotel = source.Hotel,
            Origin = source.Origin,
            Site = source.Site,
            Destination = source.Destination,
            PassengerCount = source.PassengerCount,
            ServiceType = source.ServiceType,
            TripCost = source.TripCost,
            Notes = source.Notes,
            RecordDate = source.RecordDate,
            PaymentMethod = source.PaymentMethod,
            AssignedBranchCode = source.AssignedBranchCode,
            PayoutStatus = source.PayoutStatus,
            PayoutDate = source.PayoutDate,
            PayoutUser = source.PayoutUser,
            PayoutTicket = source.PayoutTicket
        };
    }
}

public sealed record CascoApplyUpdateOneResult
{
    public bool Applied { get; init; }
    public bool Confirmed { get; init; }
    public string? RecordId { get; init; }
    public string? ProposedFolio { get; init; }
}

public sealed record CascoLocalRecordSnapshot(string FolioApp, string VendorName, string Site);
