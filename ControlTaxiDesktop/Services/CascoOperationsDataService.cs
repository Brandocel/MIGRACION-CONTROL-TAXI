using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ControlTaxiDesktop.Models;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed record CascoOperationsLoadResult(
    string BranchCode,
    string Provider,
    string QuerySource,
    IReadOnlyList<LocalAppRecordRow> SourceRows,
    IReadOnlyList<AppRecordGridRow> AppGridRows,
    IReadOnlyList<LocalRegistroDiarioRow> RegistroRows);

public sealed record CascoPayoutPreview(
    string BranchCode,
    string Provider,
    string QuerySource,
    string FolioOriginal,
    string FolioLocal,
    string Taxista,
    string Gafete,
    string Sitio,
    decimal Dejada,
    string PayoutStatus,
    string PayoutDate,
    string PayoutUser,
    string PayoutTicket,
    int MatchingRecords,
    bool RecordFound,
    bool CanPay,
    string ProposedTicket,
    string PayoutSource,
    bool HasManualRelation,
    bool HasDejadaRow,
    string ValidationSql,
    string UpdateSql,
    IReadOnlyList<string> ColumnsToChange,
    LocalRelation RelationRow);

public sealed record CascoPayoutResult(
    string BranchCode,
    string Provider,
    string QuerySource,
    string FolioOriginal,
    string FolioLocal,
    string Taxista,
    string Gafete,
    string Sitio,
    decimal Dejada,
    string PayoutStatus,
    string PayoutDate,
    string PayoutUser,
    string PayoutTicket,
    LocalRelation RelationRow);

public sealed record CascoReportCenterLoadResult(
    string BranchCode,
    string Provider,
    string QuerySource,
    DateTime StartDate,
    DateTime EndDate,
    IReadOnlyList<LocalOperationsPreviewRow> OperationRows,
    IReadOnlyList<LocalCommissionPaymentPreviewRow> PaymentRows,
    int MovementCount,
    int TotalPax,
    decimal TotalAmount,
    IReadOnlyList<string> OriginalFolios,
    IReadOnlyList<string> LocalFolios,
    IReadOnlyList<string> Taxistas,
    IReadOnlyList<string> Gafetes,
    IReadOnlyList<string> Sitios);

public sealed record CascoBadgeLoadResult(
    string BranchCode,
    string Provider,
    string QuerySource,
    IReadOnlyList<LocalBadge> Badges);

public sealed record CascoRelationCalculationDiagnostic(
    string BranchCode,
    string Provider,
    string QuerySource,
    string FolioOriginal,
    string FolioLocal,
    string Taxista,
    string Gafete,
    decimal Total,
    decimal Efectivo,
    decimal Tarjeta,
    decimal Dolares,
    decimal TipoCambio,
    string PaymentMethodRemoto,
    string PagosJson,
    decimal VentaActual,
    decimal DejadaActual,
    decimal ComisionCalculadaActual,
    decimal PagoComisionActual,
    string FormaPagoActual,
    string MonedaActual,
    string PayoutStatus,
    IReadOnlyList<(string Campo, string Valor, string Fuente, string ReglaPlaza28, string Decision)> ComparisonRows);

public sealed record CascoRelationSavePreview(
    string BranchCode,
    string Provider,
    string QuerySource,
    string FolioOriginal,
    string FolioLocal,
    string PosFolio,
    string Taxista,
    string Gafete,
    string Sitio,
    string Unidad,
    string Hotel,
    string Origen,
    string Destino,
    string Telefono,
    string TipoTransporte,
    string TaxistaId,
    DateTime FechaOperacion,
    decimal ProposedSale,
    decimal ProposedPayout,
    string PaymentMethod,
    string Currency,
    bool RelationExists,
    bool DejadaExists,
    int MatchingCvRows,
    int MatchingPlaza28Rows,
    int RelationRowsToAffect,
    int DejadaRowsToAffect,
    bool CanSave,
    string ValidationSql,
    string RelationUpsertSql,
    string DejadaSelectSql,
    string DejadaInsertSql,
    string DejadaUpdateSql);

public sealed record CascoRelationSaveResult(
    CascoRelationSavePreview Preview,
    bool RelationSaved,
    bool DejadaSaved);

public static class CascoOperationsDataService
{
    private const string QuerySource = "REYNA.mktCasco.dbo.AppMovilRegistro";
    private static readonly string[] PaymentColumns =
    [
        "estado_pago_dejada",
        "fecha_pago_dejada",
        "usuario_pago_dejada",
        "ticket_pago_dejada",
        "payout_status",
        "payout_date",
        "payout_user",
        "payout_ticket"
    ];

    private sealed record ParameterLog(string Name, object? Value, int? Length, string Destination);

    private static readonly IReadOnlyDictionary<string, string> RelationParameterDestinationInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["@folioOriginal"] = "RelacionTicketTaxista.FolioApp nvarchar(60)",
        ["@folioLocal"] = "RelacionTicketTaxista.FolioOperacion nvarchar(60)",
        ["@folioPos"] = "RelacionTicketTaxista.FolioPos nvarchar(120)",
        ["@gafete"] = "RelacionTicketTaxista.Gafete nvarchar(300)",
        ["@taxistaId"] = "RelacionTicketTaxista.TaxistaId bigint",
        ["@taxistaNombre"] = "RelacionTicketTaxista.TaxistaNombre nvarchar(150)",
        ["@vendedor"] = "RelacionTicketTaxista.Vendedor nvarchar(150)",
        ["@transporteTipo"] = "RelacionTicketTaxista.TransporteTipo nvarchar(20)",
        ["@dejada"] = "RelacionTicketTaxista.Dejada decimal(18,2)",
        ["@observaciones"] = "RelacionTicketTaxista.Observaciones nvarchar(300)",
        ["@usuario"] = "RelacionTicketTaxista.Usuario nvarchar(50)"
    };

    private static readonly IReadOnlyDictionary<string, string> DejadaParameterDestinationInfo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["@idstaff"] = "dejadas.idstaff int",
        ["@nombrestaff"] = "dejadas.nombrestaff nvarchar(100)",
        ["@sitio"] = "dejadas.nombrealmacen nvarchar(100)",
        ["@idalmacen"] = "dejadas.idalmacen int",
        ["@fechaDateTime"] = "dejadas.fecha smalldatetime",
        ["@fecha"] = "dejadas.fecha date",
        ["@horaTexto"] = "dejadas.hora nvarchar(50)",
        ["@usuario"] = "dejadas.nombrecajero nvarchar(100)",
        ["@dejada"] = "dejadas.total real",
        ["@codigoRecepcion"] = "dejadas.codigorecepcion nvarchar(100)",
        ["@folioRegistro"] = "dejadas.folioregistro bigint",
        ["@folioOriginal"] = "dejadas.folioregistrostr nvarchar(50)",
        ["@unidad"] = "dejadas.unidad nvarchar(20)",
        ["@pax"] = "dejadas.pax int",
        ["@hotel"] = "dejadas.hotel nvarchar(100)",
        ["@taxistaNombre"] = "dejadas.nombrevendedor nvarchar(100)",
        ["@transporteTipo"] = "dejadas.tipotransporte nvarchar(10)",
        ["@telefono"] = "dejadas.telefono nvarchar(12)",
        ["@venta"] = "dejadas.totalventa real",
        ["@totalEfectivo"] = "dejadas.totalefectivo real",
        ["@totalTarjeta"] = "dejadas.totaltarjeta real",
        ["@gafete"] = "dejadas.gafete nvarchar(10)"
    };

    public static async Task<CascoOperationsLoadResult> LoadAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        string? search = null,
        DateTime? start = null,
        DateTime? end = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveCascoBranch(currentBranch, branchCode);
        if (resolvedBranch is null)
        {
            return new CascoOperationsLoadResult(
                currentBranch?.Code ?? branchCode,
                "N/A",
                "N/A",
                Array.Empty<LocalAppRecordRow>(),
                Array.Empty<AppRecordGridRow>(),
                Array.Empty<LocalRegistroDiarioRow>());
        }

        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        var detailedRows = await provider.GetDetailedAppRecordsAsync(sqlPassword, start, end, cancellationToken);
        var filteredDetailedRows = ApplyDetailedSearchFilter(detailedRows, resolvedBranch.SiteName, search);
        var filteredRows = filteredDetailedRows.Select(ToLocalAppRecordRow).ToArray();
        var appGridRows = MapAppGridRows(filteredDetailedRows, resolvedBranch.SiteName);
        var registroRows = MapRegistroRows(filteredDetailedRows, resolvedBranch.SiteName);

        return new CascoOperationsLoadResult(
            resolvedBranch.Code,
            nameof(CascoReadOnlyDataProvider),
            QuerySource,
            filteredRows,
            appGridRows,
            registroRows);
    }

    public static async Task<IReadOnlyList<LocalRelation>> LoadRelationsAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        string? search = null,
        DateTime? start = null,
        DateTime? end = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveCascoBranch(currentBranch, branchCode);
        if (resolvedBranch is null)
            return Array.Empty<LocalRelation>();

        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        var rows = await LoadCascoRelationRowsAsync(connection, resolvedBranch.SiteName, search, start, end, cancellationToken);
        rows = await EnrichCascoRelationSalesAsync(rows, resolvedBranch, sqlPassword, cancellationToken);
        rows = await EnrichCascoRelationCommissionsAsync(rows, resolvedBranch, sqlPassword, cancellationToken);
        return rows
            .OrderByDescending(row => ParseOperationDate(row.DateText) ?? DateTime.MinValue)
            .ThenByDescending(row => row.OperationFolio, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<CascoRelationSavePreview> GetRelationSavePreviewAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        LocalRelation relation,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        ValidateManualRelationInput(relation);
        var folioOriginal = RequireValue(relation.OperationFolio, "El folio original");
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);

        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        var appRows = await LoadCascoSourceRowsAsync(connection, transaction: null, resolvedBranch.SiteName, folioOriginal, lockTarget: false, cancellationToken);
        var plaza28Rows = CountMatchingPlaza28Rows(appRows, resolvedBranch.SiteName);
        var sourceRow = appRows.Count == 1 ? appRows[0] : null;
        var relationExists = sourceRow is not null && await RelationExistsAsync(connection, transaction: null, folioOriginal, cancellationToken);
        var dejadaMatch = sourceRow is not null
            ? await GetDejadaMatchInfoAsync(connection, transaction: null, folioOriginal, resolvedBranch.SiteName, sourceRow.OperationDate.Date, sourceRow.Badge, cancellationToken)
            : new CascoDejadaMatchInfo(0, false);
        var effectiveRelation = NormalizeCascoRelationForSave(relation, relationExists, dejadaMatch.Exists);
        var canSave = sourceRow is not null
            && appRows.Count == 1
            && plaza28Rows == 0
            && dejadaMatch.Count <= 1;

        return new CascoRelationSavePreview(
            resolvedBranch.Code,
            nameof(CascoReadOnlyDataProvider),
            QuerySource,
            folioOriginal,
            sourceRow?.FolioLocal ?? string.Empty,
            FirstFilled(relation.PosFolio, sourceRow?.PosFolio ?? string.Empty),
            FirstFilled(relation.Driver, sourceRow?.DriverName ?? string.Empty),
            FirstFilled(relation.Badge, sourceRow?.Badge ?? string.Empty),
            resolvedBranch.SiteName,
            sourceRow?.Unit ?? string.Empty,
            sourceRow?.Hotel ?? string.Empty,
            sourceRow?.Origin ?? string.Empty,
            sourceRow?.Destination ?? string.Empty,
            sourceRow?.Phone ?? string.Empty,
            FirstFilled(relation.TransportType, sourceRow?.TransportType ?? string.Empty),
            FirstFilled(relation.TaxistaId, sourceRow?.TaxistaId ?? string.Empty),
            sourceRow?.OperationDate ?? DateTime.MinValue,
            effectiveRelation.Sale,
            effectiveRelation.Payout ?? 0m,
            FirstFilled(effectiveRelation.PaymentMethod, sourceRow is null ? string.Empty : InferPaymentMethod(sourceRow)),
            FirstFilled(effectiveRelation.Currency, sourceRow is null ? string.Empty : InferCurrency(sourceRow)),
            relationExists,
            dejadaMatch.Exists,
            appRows.Count,
            plaza28Rows,
            relationExists ? 1 : 1,
            dejadaMatch.Exists ? 1 : 1,
            canSave,
            GetRelationTargetSelectSql(),
            GetRelationUpsertSql(),
            GetDejadaSelectSql(),
            GetDejadaInsertSql(),
            GetDejadaUpdateSql());
    }

    public static async Task<CascoRelationSaveResult> SaveRelationAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        LocalRelation relation,
        string user,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        ValidateManualRelationInput(relation);
        var folioOriginal = RequireValue(relation.OperationFolio, "El folio original");
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        var normalizedUser = NormalizePayoutUser(user);

        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var appRows = await LoadCascoSourceRowsAsync(connection, transaction, resolvedBranch.SiteName, folioOriginal, lockTarget: true, cancellationToken);
            var plaza28Rows = CountMatchingPlaza28Rows(appRows, resolvedBranch.SiteName);
            if (appRows.Count == 0)
            {
                throw new InvalidOperationException($"No se encontro el folio original {folioOriginal} en Casco Viejo.");
            }

            if (plaza28Rows != 0)
                throw new InvalidOperationException("La validacion detecto filas de Plaza 28. La operacion se cancela.");

            var sourceRows = ExpandSourceRowsByBadge(appRows, relation.Badge).ToArray();
            if (sourceRows.Length == 0)
                throw new InvalidOperationException($"No se encontraron gafetes validos para el folio original {folioOriginal}.");

            var sourceRow = BuildAggregateSourceRow(sourceRows, relation.Badge);
            var relationExists = await RelationExistsAsync(connection, transaction, folioOriginal, cancellationToken);
            var dejadaExistsByBadge = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var physicalSourceRow in sourceRows)
            {
                var dejadaMatch = await GetDejadaMatchInfoAsync(connection, transaction, folioOriginal, resolvedBranch.SiteName, physicalSourceRow.OperationDate.Date, physicalSourceRow.Badge, cancellationToken);
                if (dejadaMatch.Count > 1)
                    throw new InvalidOperationException($"Se encontraron multiples filas en dbo.dejadas para el folio {folioOriginal} y gafete {physicalSourceRow.Badge}.");
                dejadaExistsByBadge[physicalSourceRow.Badge] = dejadaMatch.Exists;
            }

            var anyDejadaExists = dejadaExistsByBadge.Values.Any(exists => exists);
            var effectiveRelation = NormalizeCascoRelationForSave(relation, relationExists, anyDejadaExists);
            await LogSaveRelationParametersAsync(
                "ExecuteRelationUpsertAsync",
                sourceRow.FolioOriginal,
                sourceRow.FolioLocal,
                normalizedUser,
                BuildRelationUpsertParameterLogs(sourceRow, effectiveRelation, normalizedUser));
            try
            {
                await ExecuteRelationUpsertAsync(connection, transaction, sourceRow, effectiveRelation, normalizedUser, cancellationToken);
                await PersistSourceEditSnapshotAsync(connection, transaction, sourceRow, effectiveRelation, cancellationToken);
                await ReconcileCascoPhysicalBadgeRowsAsync(connection, transaction, sourceRow, effectiveRelation, normalizedUser, cancellationToken);
            }
            catch (SqlException exception)
            {
                await LogSaveRelationErrorAsync(
                    "ExecuteRelationUpsertAsync",
                    sourceRow.FolioOriginal,
                    sourceRow.FolioLocal,
                    normalizedUser,
                    BuildRelationUpsertParameterLogs(sourceRow, effectiveRelation, normalizedUser),
                    exception);
                throw;
            }

            foreach (var physicalSourceRow in sourceRows)
            {
                var physicalRelation = effectiveRelation with { Badge = physicalSourceRow.Badge };
                if (dejadaExistsByBadge.TryGetValue(physicalSourceRow.Badge, out var dejadaExists) && dejadaExists)
                {
                    await LogSaveRelationParametersAsync(
                        "ExecuteDejadaUpdateAsync",
                        physicalSourceRow.FolioOriginal,
                        physicalSourceRow.FolioLocal,
                        normalizedUser,
                        BuildDejadaParameterLogs(physicalSourceRow, physicalRelation, normalizedUser));
                    try
                    {
                        await ExecuteDejadaUpdateAsync(connection, transaction, physicalSourceRow, physicalRelation, normalizedUser, cancellationToken);
                    }
                    catch (SqlException exception)
                    {
                        await LogSaveRelationErrorAsync(
                            "ExecuteDejadaUpdateAsync",
                            physicalSourceRow.FolioOriginal,
                            physicalSourceRow.FolioLocal,
                            normalizedUser,
                            BuildDejadaParameterLogs(physicalSourceRow, physicalRelation, normalizedUser),
                            exception);
                        throw;
                    }
                }
                else
                {
                    await LogSaveRelationParametersAsync(
                        "ExecuteDejadaInsertAsync",
                        physicalSourceRow.FolioOriginal,
                        physicalSourceRow.FolioLocal,
                        normalizedUser,
                        BuildDejadaParameterLogs(physicalSourceRow, physicalRelation, normalizedUser));
                    try
                    {
                        await ExecuteDejadaInsertAsync(connection, transaction, physicalSourceRow, physicalRelation, normalizedUser, cancellationToken);
                    }
                    catch (SqlException exception)
                    {
                        await LogSaveRelationErrorAsync(
                            "ExecuteDejadaInsertAsync",
                            physicalSourceRow.FolioOriginal,
                            physicalSourceRow.FolioLocal,
                            normalizedUser,
                            BuildDejadaParameterLogs(physicalSourceRow, physicalRelation, normalizedUser),
                            exception);
                        throw;
                    }
                }
            }

            await PersistSourcePosFolioAsync(
                connection,
                transaction,
                sourceRow.FolioOriginal,
                sourceRow.Site,
                FirstFilled(effectiveRelation.PosFolio, sourceRow.PosFolio),
                cancellationToken);
            await ValidateSavedRelationAsync(connection, transaction, folioOriginal, cancellationToken);
            foreach (var physicalSourceRow in sourceRows)
                await ValidateSavedDejadaAsync(connection, transaction, folioOriginal, resolvedBranch.SiteName, physicalSourceRow.OperationDate.Date, physicalSourceRow.Badge, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var preview = await GetRelationSavePreviewAsync(
                resolvedBranch,
                resolvedBranch.Code,
                sqlPassword,
                effectiveRelation with
                {
                    AppFolio = sourceRow.FolioLocal,
                    OperationFolio = sourceRow.FolioOriginal,
                    PosFolio = FirstFilled(effectiveRelation.PosFolio, sourceRow.PosFolio),
                    Badge = FirstFilled(effectiveRelation.Badge, sourceRow.Badge),
                    Driver = FirstFilled(effectiveRelation.Driver, sourceRow.DriverName),
                    Vendor = FirstFilled(effectiveRelation.Vendor, sourceRow.DriverName),
                    TaxistaId = FirstFilled(effectiveRelation.TaxistaId, sourceRow.TaxistaId),
                    TransportType = FirstFilled(effectiveRelation.TransportType, sourceRow.TransportType),
                    PaymentMethod = FirstFilled(effectiveRelation.PaymentMethod, InferPaymentMethod(sourceRow)),
                    Currency = FirstFilled(effectiveRelation.Currency, InferCurrency(sourceRow))
                },
                cancellationToken);

            return new CascoRelationSaveResult(preview, true, true);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static IReadOnlyList<ParameterLog> BuildRelationUpsertParameterLogs(CascoSourceRow sourceRow, LocalRelation relation, string user)
    {
        return new List<ParameterLog>
        {
            CreateParameterLog("@folioOriginal", sourceRow.FolioOriginal),
            CreateParameterLog("@folioLocal", sourceRow.FolioLocal),
            CreateParameterLog("@folioPos", FirstFilled(relation.PosFolio, sourceRow.PosFolio)),
            CreateParameterLog("@gafete", FirstFilled(relation.Badge, sourceRow.Badge)),
            CreateParameterLog("@taxistaId", ParseBigIntOrZero(FirstFilled(relation.TaxistaId, sourceRow.TaxistaId))),
            CreateParameterLog("@taxistaNombre", FirstFilled(relation.Driver, sourceRow.DriverName)),
            CreateParameterLog("@vendedor", FirstFilled(relation.Vendor, sourceRow.DriverName)),
            CreateParameterLog("@transporteTipo", FirstFilled(relation.TransportType, sourceRow.TransportType)),
            CreateParameterLog("@dejada", relation.Payout ?? 0m),
            CreateParameterLog("@observaciones", relation.Notes ?? string.Empty),
            CreateParameterLog("@usuario", user)
        };
    }

    private static IReadOnlyList<ParameterLog> BuildDejadaParameterLogs(CascoSourceRow sourceRow, LocalRelation relation, string user)
    {
        var paymentMethod = FirstFilled(relation.PaymentMethod, InferPaymentMethod(sourceRow));
        var isCard = string.Equals(paymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase);
        var isMixed = string.Equals(paymentMethod, "Mixto", StringComparison.OrdinalIgnoreCase);
        var sale = relation.Sale;
        var cash = isMixed ? sourceRow.Cash : isCard ? 0m : sale;
        var card = isMixed ? sourceRow.Card : isCard ? sale : 0m;

        return new List<ParameterLog>
        {
            CreateParameterLog("@idstaff", ParseBigIntNullable(sourceRow.FolioOriginal) ?? 0L),
            CreateParameterLog("@nombrestaff", NormalizeForSqlLength(sourceRow.DriverName, 100)),
            CreateParameterLog("@sitio", NormalizeForSqlLength(sourceRow.Site, 100)),
            CreateParameterLog("@idalmacen", 0),
            CreateParameterLog("@fechaDateTime", sourceRow.OperationDate),
            CreateParameterLog("@fecha", sourceRow.OperationDate.Date),
            CreateParameterLog("@horaTexto", sourceRow.OperationDate == DateTime.MinValue ? string.Empty : sourceRow.OperationDate.ToString("HH:mm", CultureInfo.InvariantCulture)),
            CreateParameterLog("@usuario", NormalizeForSqlLength(user, 100)),
            CreateParameterLog("@dejada", relation.Payout ?? 0m),
            CreateParameterLog("@codigoRecepcion", NormalizeForSqlLength(sourceRow.FolioOriginal, 100)),
            CreateParameterLog("@folioRegistro", ParseBigIntNullable(sourceRow.FolioLocal) ?? 0L),
            CreateParameterLog("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 50)),
            CreateParameterLog("@unidad", NormalizeForSqlLength(sourceRow.Unit, 20)),
            CreateParameterLog("@pax", ResolveRelationPassengers(sourceRow, relation)),
            CreateParameterLog("@hotel", NormalizeForSqlLength(sourceRow.Hotel, 100)),
            CreateParameterLog("@taxistaNombre", NormalizeForSqlLength(sourceRow.DriverName, 100)),
            CreateParameterLog("@transporteTipo", NormalizeForSqlLength(sourceRow.TransportType, 10)),
            CreateParameterLog("@telefono", NormalizeForSqlLength(sourceRow.Phone, 12)),
            CreateParameterLog("@venta", sale),
            CreateParameterLog("@totalEfectivo", cash),
            CreateParameterLog("@totalTarjeta", card),
            CreateParameterLog("@gafete", NormalizeForSqlLength(sourceRow.Badge, 10))
        };
    }

    private static ParameterLog CreateParameterLog(string name, object? value)
    {
        var textValue = value is string stringValue ? stringValue : value is DateTime dateValue ? dateValue.ToString("o", CultureInfo.InvariantCulture) : value?.ToString() ?? "NULL";
        int? length = value is string stringValueLength ? stringValueLength.Length : null;
        var destination = RelationParameterDestinationInfo.TryGetValue(name, out var relationDestination)
            ? relationDestination
            : DejadaParameterDestinationInfo.TryGetValue(name, out var dejadaDestination)
                ? dejadaDestination
                : "unknown";

        return new ParameterLog(name, textValue, length, destination);
    }

    private static async Task LogSaveRelationParametersAsync(string methodName, string folioOriginal, string folioApp, string usuario, IReadOnlyList<ParameterLog> parameters)
    {
        var logPath = GetSaveRelationLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? string.Empty);
        var builder = new StringBuilder();
        builder.AppendLine("=== CASCO SAVE PARAMS ===");
        builder.AppendLine($"Time: {DateTime.Now:O}");
        builder.AppendLine($"Method: {methodName}");
        builder.AppendLine($"folio_app_original: {folioOriginal}");
        builder.AppendLine($"folio_app: {folioApp}");
        builder.AppendLine($"usuario: {usuario}");
        builder.AppendLine("Parameters:");
        foreach (var parameter in parameters)
        {
            builder.AppendLine($"  {parameter.Name}: {parameter.Value}; LEN={(parameter.Length?.ToString() ?? "n/a")}; Destination={parameter.Destination}");
        }
        builder.AppendLine(new string('-', 120));
        await File.AppendAllTextAsync(logPath, builder.ToString(), Encoding.UTF8);
    }

    private static async Task LogSaveRelationErrorAsync(string methodName, string folioOriginal, string folioApp, string usuario, IReadOnlyList<ParameterLog> parameters, SqlException exception)
    {
        var logPath = GetSaveRelationLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? string.Empty);
        var builder = new StringBuilder();
        builder.AppendLine("=== CASCO SAVE SQL ERROR ===");
        builder.AppendLine($"Time: {DateTime.Now:O}");
        builder.AppendLine($"Method: {methodName}");
        builder.AppendLine($"ErrorNumber: {exception.Number}");
        builder.AppendLine($"ErrorMessage: {exception.Message}");
        builder.AppendLine($"StackTrace: {exception.StackTrace}");
        builder.AppendLine($"folio_app_original: {folioOriginal}");
        builder.AppendLine($"folio_app: {folioApp}");
        builder.AppendLine($"usuario: {usuario}");
        builder.AppendLine("Parameters:");
        foreach (var parameter in parameters)
        {
            builder.AppendLine($"  {parameter.Name}: {parameter.Value}; LEN={(parameter.Length?.ToString() ?? "n/a")}; Destination={parameter.Destination}");
        }
        builder.AppendLine(new string('=', 120));
        await File.AppendAllTextAsync(logPath, builder.ToString(), Encoding.UTF8);
    }

    private static string GetSaveRelationLogPath()
    {
        return Path.Combine(Environment.CurrentDirectory, "Logs", "casco-save.log");
    }

    public static async Task GenerateSaveLogDiagnostic(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        LocalRelation relation,
        string user,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        ValidateManualRelationInput(relation);
        var folioOriginal = RequireValue(relation.OperationFolio, "El folio original");
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);

        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        var appRows = await LoadCascoSourceRowsAsync(connection, transaction: null, resolvedBranch.SiteName, folioOriginal, lockTarget: false, cancellationToken);
        if (appRows.Count != 1)
            throw new InvalidOperationException(appRows.Count == 0
                ? $"No se encontro el folio original {folioOriginal} en {resolvedBranch.SiteName}."
                : $"Se encontraron {appRows.Count} filas para {folioOriginal} en {resolvedBranch.SiteName}.");

        var sourceRow = appRows[0];

        var relationParams = BuildRelationUpsertParameterLogs(sourceRow, relation, user);
        var dejadaParams = BuildDejadaParameterLogs(sourceRow, relation, user);

        // Log relation upsert parameters
        await LogDiagnosticParametersAsync("ExecuteRelationUpsertAsync", folioOriginal, sourceRow.FolioLocal, user, relationParams);

        // Log dejada parameters (simulate insert)
        await LogDiagnosticParametersAsync("ExecuteDejadaInsertAsync", folioOriginal, sourceRow.FolioLocal, user, dejadaParams);
    }

    private static async Task LogDiagnosticParametersAsync(string methodName, string folioOriginal, string folioApp, string usuario, IReadOnlyList<ParameterLog> parameters)
    {
        var logPath = GetSaveRelationLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? string.Empty);
        var builder = new StringBuilder();
        builder.AppendLine("=== CASCO SAVE DIAGNOSTIC ===");
        builder.AppendLine($"Time: {DateTime.Now:O}");
        builder.AppendLine($"Method: {methodName}");
        builder.AppendLine($"folio_app_original: {folioOriginal}");
        builder.AppendLine($"folio_app: {folioApp}");
        builder.AppendLine($"usuario: {usuario}");
        builder.AppendLine("Parameters:");
        foreach (var parameter in parameters)
        {
            int? maxSize = ParseMaxSizeFromDestination(parameter.Destination);
            var lenText = parameter.Length?.ToString() ?? "n/a";
            var maxText = maxSize.HasValue ? (maxSize.Value == -1 ? "MAX" : maxSize.Value.ToString()) : "n/a";
            var fits = true;
            if (parameter.Length.HasValue && maxSize.HasValue && maxSize.Value > 0)
                fits = parameter.Length.Value <= maxSize.Value;

            builder.AppendLine($"  {parameter.Name}; Destination={parameter.Destination}; Value={parameter.Value}; LEN={lenText}; MaxSize={maxText}; Fits={fits}");
        }
        builder.AppendLine(new string('=', 120));
        await File.AppendAllTextAsync(logPath, builder.ToString(), Encoding.UTF8);
    }

    private static int? ParseMaxSizeFromDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return null;

        // look for patterns like nvarchar(100) or varchar(50) or nvarchar(MAX)
        var m = Regex.Match(destination, @"(n?varchar|char|varchar)\s*\((MAX|\d+)\)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var sizeGroup = m.Groups[2].Value;
            if (string.Equals(sizeGroup, "MAX", StringComparison.OrdinalIgnoreCase))
                return -1;
            if (int.TryParse(sizeGroup, out var parsed))
                return parsed;
        }

        // decimal or numeric precision not applicable for string length
        return null;
    }

    public static async Task<CascoPayoutPreview> GetPayoutPreviewAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        string folioOriginal,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        var normalizedFolio = RequireValue(folioOriginal, "El folio original");
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        var matches = await provider.GetDetailedRecordsByOriginalFolioAsync(sqlPassword, normalizedFolio, cancellationToken);
        var (relation, payoutSource, hasManualRelation, hasDejadaRow) = matches.Count >= 1
            ? await LoadCascoPayoutRowAsync(resolvedBranch, sqlPassword, normalizedFolio, cancellationToken)
            : (EmptyRelation(normalizedFolio, resolvedBranch.SiteName), "none", false, false);
        var proposedTicket = BuildPayoutTicket(normalizedFolio);
        var currentStatus = relation.PayoutStatus;
        var canPay = matches.Count >= 1 && !IsPaidStatus(currentStatus) &&
            (relation.Payout > 0m || hasManualRelation);

        return new CascoPayoutPreview(
            resolvedBranch.Code,
            nameof(CascoReadOnlyDataProvider),
            QuerySource,
            relation.OperationFolio,
            relation.AppFolio,
            relation.Driver,
            relation.Badge,
            relation.Site,
            relation.Payout ?? 0m,
            relation.PayoutStatus,
            relation.PayoutDate,
            relation.PayoutUser,
            relation.PayoutTicket,
            matches.Count,
            matches.Count >= 1,
            canPay,
            proposedTicket,
            payoutSource,
            hasManualRelation,
            hasDejadaRow,
            GetPendingValidationSql(),
            GetPaymentUpdateSql(),
            PaymentColumns,
            relation);
    }

    public static async Task<CascoPayoutResult> PayPayoutAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        string folioOriginal,
        string user,
        System.Threading.CancellationToken cancellationToken = default,
        string? selectedBadge = null)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        var normalizedFolio = RequireValue(folioOriginal, "El folio original");
        var normalizedUser = NormalizePayoutUser(user);
        var proposedTicket = BuildPayoutTicket(normalizedFolio);
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);

        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var normalizedBadge = NormalizeBadgeList(selectedBadge);
            var beforeRows = await LoadLockedTargetRowsAsync(connection, transaction, resolvedBranch.SiteName, normalizedFolio, normalizedBadge, cancellationToken);
            if (beforeRows.Count == 0)
                throw new InvalidOperationException(beforeRows.Count == 0
                    ? $"No se encontro el viaje {normalizedFolio} para pagar la dejada."
                    : $"Se encontraron {beforeRows.Count} registros para el folio original {normalizedFolio} en {resolvedBranch.SiteName}. Selecciona una fila con gafete unico.");

            var before = MergeRelationRowsForDisplay(beforeRows);
            if (beforeRows.Any(row => IsPaidStatus(row.PayoutStatus)))
                throw new InvalidOperationException("La dejada de este viaje ya esta pagada.");

            await ValidatePendingRowsAsync(connection, transaction, resolvedBranch.SiteName, normalizedFolio, normalizedBadge, beforeRows.Count, cancellationToken);
            await ExecutePaymentUpdateAsync(connection, transaction, resolvedBranch.SiteName, normalizedFolio, normalizedBadge, beforeRows.Count, normalizedUser, proposedTicket, cancellationToken);

            var afterRows = await LoadLockedTargetRowsAsync(connection, transaction, resolvedBranch.SiteName, normalizedFolio, normalizedBadge, cancellationToken);
            if (afterRows.Count != beforeRows.Count)
                throw new InvalidOperationException("No se pudo validar el registro actualizado despues del pago.");

            var after = MergeRelationRowsForDisplay(afterRows);
            if (afterRows.Any(row => !IsPaidStatus(row.PayoutStatus)) || afterRows.Any(row => !string.Equals(row.PayoutTicket, proposedTicket, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("La validacion posterior al pago no confirmo el estado PAGADO.");

            await transaction.CommitAsync(cancellationToken);

            return new CascoPayoutResult(
                resolvedBranch.Code,
                nameof(CascoReadOnlyDataProvider),
                QuerySource,
                after.OperationFolio,
                after.AppFolio,
                after.Driver,
                after.Badge,
                after.Site,
                after.Payout ?? 0m,
                after.PayoutStatus,
                after.PayoutDate,
                after.PayoutUser,
                after.PayoutTicket,
                after);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public static async Task<string> BuildDejadaTicketTextAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        LocalRelation relation,
        string user,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var lines = await GetDejadaTicketLinesAsync(currentBranch, branchCode, sqlPassword, relation.OperationFolio, cancellationToken);
        return BuildTicketText(lines, string.IsNullOrWhiteSpace(user) ? relation.PayoutUser : user);
    }

    public static async Task<IReadOnlyList<LocalTicketLine>> GetDejadaTicketLinesAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        string folioOriginal,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        var normalizedFolio = RequireValue(folioOriginal, "El folio original");
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT TOP (1)
              COALESCE(a.folio_app_original, a.folio_app, '') AS Folio,
              COALESCE(a.folio_app, '') AS FolioApp,
              COALESCE(a.folio_app_original, a.folio_app, '') AS Operacion,
              COALESCE(COALESCE(a.ticket_pago_dejada, a.payout_ticket), '') AS Ticket,
              COALESCE(CONVERT(nvarchar(30), a.fecha_operacion, 120), CONVERT(nvarchar(30), a.fecha_creacion, 120), '') AS FechaViaje,
              COALESCE(CONVERT(nvarchar(30), a.fecha_pago_dejada, 120), CONVERT(nvarchar(30), a.payout_date, 120), '') AS FechaPago,
              COALESCE(a.vendedor_nombre, '') AS Taxista,
              CAST(N'' AS nvarchar(200)) AS Vendedor,
              COALESCE(a.folio_gafete, '') AS Gafete,
              COALESCE(a.unidad, '') AS Unidad,
              COALESCE(a.placas, '') AS Placas,
              COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
              COALESCE(a.nacionalidad, '') AS Nacionalidad,
              COALESCE(NULLIF(a.modelo_vehiculo, ''), NULLIF(a.tipo_operacion, ''), '') AS Transporte,
              COALESCE(a.hotel, '') AS Hotel,
              COALESCE(a.origen, '') AS Origen,
              COALESCE(a.destino, '') AS Destino,
              COALESCE(a.pax, 0) AS Pax,
              0 AS Venta,
              COALESCE(a.total, 0) AS Dejada,
              CASE
                WHEN COALESCE(a.dolares, 0) > 0 AND COALESCE(a.efectivo, 0) = 0 AND COALESCE(a.tarjeta, 0) = 0 THEN 'Dolares'
                WHEN COALESCE(a.dolares, 0) > 0 AND (COALESCE(a.efectivo, 0) > 0 OR COALESCE(a.tarjeta, 0) > 0) THEN 'Mixto'
                WHEN COALESCE(a.tarjeta, 0) > 0 THEN 'Tarjeta'
                WHEN COALESCE(a.efectivo, 0) > 0 THEN 'Efectivo'
                ELSE ''
              END AS FormaPago,
              CASE WHEN COALESCE(a.dolares, 0) > 0 THEN 'USD' ELSE 'MXN' END AS Moneda,
              COALESCE(a.usuario_pago_dejada, a.payout_user, '') AS UsuarioPago,
              COALESCE(COALESCE(a.ticket_pago_dejada, a.payout_ticket), '') AS TicketPago,
              COALESCE(NULLIF(a.estado_pago_dejada, ''), NULLIF(a.payout_status, ''), 'pendiente') AS Estatus,
              COALESCE(a.sitio, '') AS Sitio
            FROM dbo.AppMovilRegistro a
            OUTER APPLY
            (
              SELECT TOP (1) FolioApp, Dejada, TicketPagoDejada = payout_ticket
              FROM dbo.RelacionTicketTaxista
              WHERE FolioApp = a.folio_app_original
              ORDER BY FechaActualizacion DESC, Id DESC
            ) t
            OUTER APPLY
            (
              SELECT TOP (1) total, totalventa
              FROM dbo.dejadas
              WHERE folioregistrostr = a.folio_app_original
                AND nombrealmacen = @sitio
                AND CAST(fecha AS date) = CAST(COALESCE(a.fecha_operacion, a.fecha_creacion) AS date)
                AND COALESCE(gafete, '') = COALESCE(a.folio_gafete, '')
              ORDER BY fecha DESC
            ) d
            WHERE a.folio_app_original = @folioOriginal
              AND a.sitio = @sitio
            ORDER BY COALESCE(a.fecha_operacion, a.fecha_creacion) DESC;
            """,
            connection);
        command.Parameters.AddWithValue("@folioOriginal", normalizedFolio);
        command.Parameters.AddWithValue("@sitio", resolvedBranch.SiteName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"No se encontro el viaje {normalizedFolio} para imprimir la dejada.");

        // Compute Venta by summing remisiones from Casco remision stores (compuadmoCasco + joyeriaCasco)
        decimal ventaDecimal = 0m;
        try
        {
            var salesProvider = new CascoSalesDataProvider(resolvedBranch);
            var rem = await salesProvider.GetRemisionesTotalByOperacionAsync(sqlPassword, normalizedFolio);
            ventaDecimal = rem.Compuadmo + rem.Joyeria;
        }
        catch
        {
            // swallow - diagnostics read-only
        }

        var venta = ventaDecimal.ToString("C2", CultureInfo.CurrentCulture);
        var dejada = Convert.ToDecimal(reader.GetValue(19), CultureInfo.InvariantCulture).ToString("C2", CultureInfo.CurrentCulture);
        var paidBy = reader.IsDBNull(22) ? string.Empty : reader.GetString(22);
        return
        [
            new LocalTicketLine("Ticket", reader.IsDBNull(3) ? string.Empty : reader.GetString(3)),
            new LocalTicketLine("Folio", reader.IsDBNull(0) ? string.Empty : reader.GetString(0)),
            new LocalTicketLine("Folio app", reader.IsDBNull(1) ? string.Empty : reader.GetString(1)),
            new LocalTicketLine("Operacion", reader.IsDBNull(2) ? string.Empty : reader.GetString(2)),
            new LocalTicketLine("Fecha viaje", reader.IsDBNull(4) ? string.Empty : reader.GetString(4)),
            new LocalTicketLine("Fecha pago", reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
            new LocalTicketLine("Taxista", reader.IsDBNull(6) ? string.Empty : reader.GetString(6)),
            new LocalTicketLine("Gafete", reader.IsDBNull(8) ? string.Empty : reader.GetString(8)),
            new LocalTicketLine("Unidad", reader.IsDBNull(9) ? string.Empty : reader.GetString(9)),
            new LocalTicketLine("Placas", reader.IsDBNull(10) ? string.Empty : reader.GetString(10)),
            new LocalTicketLine("Telefono", reader.IsDBNull(11) ? string.Empty : reader.GetString(11)),
            new LocalTicketLine("Nacionalidad", reader.IsDBNull(12) ? string.Empty : reader.GetString(12)),
            new LocalTicketLine("Transporte", reader.IsDBNull(13) ? string.Empty : reader.GetString(13)),
            new LocalTicketLine("Hotel", reader.IsDBNull(14) ? string.Empty : reader.GetString(14)),
            new LocalTicketLine("Destino", reader.IsDBNull(16) ? string.Empty : reader.GetString(16)),
            new LocalTicketLine("Pax", Convert.ToString(reader.GetValue(17), CultureInfo.InvariantCulture) ?? "0"),
            new LocalTicketLine("Dejada", dejada),
            new LocalTicketLine("Usuario", paidBy),
            new LocalTicketLine("Estatus", NormalizePendingStatus(reader.IsDBNull(24) ? string.Empty : reader.GetString(24), reader.IsDBNull(5) ? string.Empty : reader.GetString(5))),
            new LocalTicketLine("Sucursal", reader.IsDBNull(25) ? string.Empty : reader.GetString(25))
        ];
    }

    private static async Task<(LocalRelation Relation, string PayoutSource, bool HasManualRelation, bool HasDejadaRow)> LoadCascoPayoutRowAsync(
        BranchConfiguration resolvedBranch,
        string sqlPassword,
        string folioOriginal,
        System.Threading.CancellationToken cancellationToken)
    {
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT TOP (1)
              COALESCE(a.folio_app, '') AS FolioControl,
              COALESCE(a.folio_app_original, '') AS OriginalFolio,
              COALESCE(a.folio_pos, '') AS PosFolio,
              COALESCE(a.vendedor_nombre, '') AS DriverName,
              COALESCE(a.hotel, '') AS Hotel,
              COALESCE(a.folio_gafete, '') AS Badge,
              COALESCE(CONVERT(nvarchar(30), a.fecha_operacion, 120), CONVERT(nvarchar(30), a.fecha_creacion, 120), '') AS OperationDate,
              COALESCE(NULLIF(a.modelo_vehiculo, ''), NULLIF(a.tipo_operacion, ''), '') AS TransportType,
              COALESCE(a.total, 0) AS Total,
              COALESCE(NULLIF(a.estado_pago_dejada, ''), NULLIF(a.payout_status, ''), 'pendiente') AS PaymentStatus,
              COALESCE(a.usuario_movil, '') AS Usuario,
              COALESCE(a.notas, '') AS Notes,
              COALESCE(a.origen, '') AS Origin,
              COALESCE(a.destino, '') AS Destination,
              COALESCE(a.sitio, '') AS Site,
              COALESCE(a.unidad, '') AS Unit,
              COALESCE(a.placas, '') AS Plates,
              COALESCE(a.nacionalidad, '') AS Nationality,
              COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Phone,
              COALESCE(CAST(a.id_catalogo AS nvarchar(60)), '') AS TaxistaId,
              COALESCE(a.usuario_pago_dejada, a.payout_user, '') AS PayoutUser,
              COALESCE(CONVERT(nvarchar(30), a.fecha_pago_dejada, 120), CONVERT(nvarchar(30), a.payout_date, 120), '') AS PayoutDate,
              COALESCE(a.ticket_pago_dejada, a.payout_ticket, '') AS PayoutTicket,
              COALESCE(a.efectivo, 0) AS Cash,
              COALESCE(a.tarjeta, 0) AS Card,
              COALESCE(a.pax, 0) AS Passengers,
              COALESCE(a.dolares, 0) AS Dollars,
              COALESCE(a.tipo_cambio, 0) AS ExchangeRate,
              COALESCE(a.comision_calculada, 0) AS CommissionCalculated,
              COALESCE(a.pago_comision, 0) AS CommissionPaid,
              COALESCE(a.detalle_json, '') AS DetailJson,
              COALESCE(a.pagos_json, '') AS PaymentsJson,
              t.Dejada AS RelationDejada,
              d.total AS DejadaTotal
            FROM dbo.AppMovilRegistro a
            OUTER APPLY
            (
              SELECT TOP (1) Dejada
              FROM dbo.RelacionTicketTaxista rr
              WHERE rr.FolioApp = a.folio_app_original
              ORDER BY FechaActualizacion DESC, Id DESC
            ) t
            OUTER APPLY
            (
              SELECT TOP (1) total
              FROM dbo.dejadas dd
              WHERE dd.folioregistrostr = a.folio_app_original
                AND dd.nombrealmacen = @sitio
                AND CAST(dd.fecha AS date) = CAST(COALESCE(a.fecha_operacion, a.fecha_creacion) AS date)
                AND COALESCE(dd.gafete, '') = COALESCE(a.folio_gafete, '')
              ORDER BY dd.fecha DESC
            ) d
            WHERE a.folio_app_original = @folioOriginal
              AND a.sitio = @sitio
            ORDER BY COALESCE(a.fecha_operacion, a.fecha_creacion) DESC;
            """,
            connection);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        command.Parameters.AddWithValue("@sitio", resolvedBranch.SiteName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"No se encontro el viaje {folioOriginal} para obtener el preview.");

        var baseRelation = MapRelationRow(new CascoAppRecordDetail(
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            reader.IsDBNull(8) ? 0m : Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
            reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
            reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
            reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
            reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
            reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
            reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
            reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
            reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
            reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
            reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
            reader.IsDBNull(21) ? string.Empty : reader.GetString(21),
            reader.IsDBNull(22) ? string.Empty : reader.GetString(22),
            reader.IsDBNull(23) ? 0m : Convert.ToDecimal(reader.GetValue(23), CultureInfo.InvariantCulture),
            reader.IsDBNull(24) ? 0m : Convert.ToDecimal(reader.GetValue(24), CultureInfo.InvariantCulture),
            reader.IsDBNull(25) ? 0 : Convert.ToInt32(reader.GetValue(25), CultureInfo.InvariantCulture),
            reader.IsDBNull(26) ? 0m : Convert.ToDecimal(reader.GetValue(26), CultureInfo.InvariantCulture),
            reader.IsDBNull(27) ? 0m : Convert.ToDecimal(reader.GetValue(27), CultureInfo.InvariantCulture),
            reader.IsDBNull(28) ? 0m : Convert.ToDecimal(reader.GetValue(28), CultureInfo.InvariantCulture),
            reader.IsDBNull(29) ? 0m : Convert.ToDecimal(reader.GetValue(29), CultureInfo.InvariantCulture),
            reader.IsDBNull(30) ? string.Empty : reader.GetString(30),
            reader.IsDBNull(31) ? string.Empty : reader.GetString(31)));

        var hasManualRelation = !reader.IsDBNull(32);
        var manualDejada = hasManualRelation ? Convert.ToDecimal(reader.GetValue(32), CultureInfo.InvariantCulture) : 0m;
        var hasDejadaRow = !reader.IsDBNull(33);
        var dejadaTotal = hasDejadaRow ? Convert.ToDecimal(reader.GetValue(33), CultureInfo.InvariantCulture) : 0m;
        var payout = hasManualRelation ? manualDejada : hasDejadaRow ? dejadaTotal : 0m;
        var payoutSource = hasManualRelation ? "RelacionTicketTaxista.Dejada" : hasDejadaRow ? "dbo.dejadas.total" : "none";
        var relation = baseRelation with { Payout = payout };
        return (relation, payoutSource, hasManualRelation, hasDejadaRow);
    }

    public static async Task<IReadOnlyList<LocalOperationsPreviewRow>> GetOperationsReportPreviewAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        DateTime? start,
        DateTime? end,
        System.Threading.CancellationToken cancellationToken = default)
    {
        return (await LoadReportAsync(currentBranch, branchCode, sqlPassword, start, end, cancellationToken)).OperationRows;
    }

    public static async Task<IReadOnlyList<LocalCommissionPaymentPreviewRow>> GetPayoutReportPreviewAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        DateTime? start,
        DateTime? end,
        System.Threading.CancellationToken cancellationToken = default)
    {
        return (await LoadReportAsync(currentBranch, branchCode, sqlPassword, start, end, cancellationToken)).PaymentRows;
    }

    public static async Task<CascoReportCenterLoadResult> LoadReportAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        DateTime? start,
        DateTime? end,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        var appliedStart = start?.Date ?? DateTime.Today;
        var appliedEnd = end?.Date ?? appliedStart;
        var rows = await LoadRelationsAsync(
            resolvedBranch,
            resolvedBranch.Code,
            sqlPassword,
            start: appliedStart,
            end: appliedEnd,
            cancellationToken: cancellationToken);
        var operationRows = rows.Select(MapOperationsPreviewRow).ToArray();
        var paymentRows = rows.Select(MapCommissionPaymentPreviewRow).ToArray();
        return new CascoReportCenterLoadResult(
            resolvedBranch.Code,
            nameof(CascoReadOnlyDataProvider),
            QuerySource,
            appliedStart,
            appliedEnd,
            operationRows,
            paymentRows,
            operationRows.Length,
            operationRows.Sum(x => x.Pax),
            operationRows.Sum(x => x.Importe),
            DistinctNonEmpty(operationRows.Select(x => x.FolioOriginal)),
            DistinctNonEmpty(operationRows.Select(x => x.FolioLocal)),
            DistinctNonEmpty(operationRows.Select(x => x.Taxista)),
            DistinctNonEmpty(operationRows.Select(x => x.Gafete)),
            DistinctNonEmpty(operationRows.Select(x => x.Sitio)));
    }

    public static async Task<CascoReportCenterLoadResult> LoadActiveReportAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        Func<DateTime?, DateTime?, string?, System.Threading.CancellationToken, Task<IReadOnlyList<LocalOperationsPreviewRow>>> plaza28OperationsLoader,
        Func<DateTime?, DateTime?, string?, System.Threading.CancellationToken, Task<IReadOnlyList<LocalCommissionPaymentPreviewRow>>> plaza28PaymentsLoader,
        DateTime? start,
        DateTime? end,
        string? siteName,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var normalizedBranchCode = string.IsNullOrWhiteSpace(branchCode) ? currentBranch?.Code ?? "P28" : branchCode.Trim().ToUpperInvariant();
        if (string.Equals(normalizedBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            return await LoadReportAsync(currentBranch, normalizedBranchCode, sqlPassword, start, end, cancellationToken);

        var appliedStart = start?.Date ?? DateTime.Today;
        var appliedEnd = end?.Date ?? appliedStart;
        var operationRows = (await plaza28OperationsLoader(appliedStart, appliedEnd, siteName, cancellationToken)).ToArray();
        var paymentRows = (await plaza28PaymentsLoader(appliedStart, appliedEnd, siteName, cancellationToken)).ToArray();
        return new CascoReportCenterLoadResult(
            normalizedBranchCode,
            "LocalOperationsRepository",
            "DatosLocal/SQLite",
            appliedStart,
            appliedEnd,
            operationRows,
            paymentRows,
            operationRows.Length,
            operationRows.Sum(x => x.Pax),
            operationRows.Sum(x => x.Importe),
            DistinctNonEmpty(operationRows.Select(x => x.FolioOriginal)),
            DistinctNonEmpty(operationRows.Select(x => x.FolioLocal)),
            DistinctNonEmpty(operationRows.Select(x => x.Taxista)),
            DistinctNonEmpty(operationRows.Select(x => x.Gafete)),
            DistinctNonEmpty(operationRows.Select(x => x.Sitio)));
    }

    public static async Task<CascoBadgeLoadResult> LoadBadgesAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        DateTime? start,
        DateTime? end,
        string? staffSearch = null,
        string? badgeSearch = null,
        string? operationSearch = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        var rows = await provider.GetDetailedAppRecordsAsync(sqlPassword, start, end, cancellationToken);
        var controlStates = await provider.GetBadgeControlStatesAsync(sqlPassword, cancellationToken);
        var badges = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Badge))
            .Where(row => string.Equals(row.Site, resolvedBranch.SiteName, StringComparison.OrdinalIgnoreCase))
            .Where(row => Matches(row.DriverName, staffSearch) || Matches(row.Unit, staffSearch))
            .Where(row => Matches(row.Badge, badgeSearch))
            .Where(row => Matches(row.OriginalFolio, operationSearch) || Matches(row.FolioControl, operationSearch))
            .SelectMany(row =>
            {
                var operationFolio = string.IsNullOrWhiteSpace(row.OriginalFolio) ? row.FolioControl : row.OriginalFolio;
                var rowBadges = SplitBadgeValues(row.Badge);
                return rowBadges.Select(badge =>
                {
                    var controlState = FindBadgeControlState(controlStates, badge, operationFolio, row.FolioControl);
                    return new LocalBadge(
                        0,
                        badge,
                        controlState?.Status ?? "ASIGNADO",
                        long.TryParse(row.TaxistaId, out var driverId) ? driverId : null,
                        NormalizeDateText(row.OperationDate),
                        string.Equals(controlState?.RawStatus, "R", StringComparison.OrdinalIgnoreCase) ? controlState?.ActivityDate ?? string.Empty : string.Empty,
                        row.DriverName,
                        operationFolio,
                        row.Unit,
                        row.Phone,
                        row.Nationality,
                        row.FolioControl);
                });
            })
            .OrderByDescending(row => ParseOperationDate(row.AssignedAt) ?? DateTime.MinValue)
            .ThenByDescending(row => row.Number, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => row with { Id = index + 1 })
            .ToArray();

        return new CascoBadgeLoadResult(
            resolvedBranch.Code,
            nameof(CascoReadOnlyDataProvider),
            QuerySource,
            badges);
    }

    public static async Task<CascoRelationCalculationDiagnostic> GetRelationCalculationDiagnosticAsync(
        BranchConfiguration? currentBranch,
        string branchCode,
        string sqlPassword,
        string folioOriginal,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var resolvedBranch = ResolveRequiredCascoBranch(currentBranch, branchCode);
        var provider = new CascoReadOnlyDataProvider(resolvedBranch);
        var rows = await provider.GetDetailedRecordsByOriginalFolioAsync(sqlPassword, folioOriginal, cancellationToken);
        var row = rows.FirstOrDefault()
            ?? throw new InvalidOperationException($"No se encontro el folio {folioOriginal} en {resolvedBranch.SiteName}.");
        var payment = NormalizeCascoPayment(row);
        var comparisonRows = new List<(string Campo, string Valor, string Fuente, string ReglaPlaza28, string Decision)>
        {
            ("Venta", row.Total.ToString("0.00", CultureInfo.InvariantCulture), "dbo.AppMovilRegistro.total", "Venta real de tickets/tiendas", "Se usa total como venta actual de CV"),
            ("Dejada", row.Total.ToString("0.00", CultureInfo.InvariantCulture), "dbo.AppMovilRegistro.total", "Deduccion real separada", "Inconsistencia pendiente: no existe campo separado confirmado"),
            ("Comision", row.CommissionCalculated.ToString("0.00", CultureInfo.InvariantCulture), "dbo.AppMovilRegistro.comision_calculada", "Formula Plaza 28 con descuentos y deducciones", "Solo lectura: se respeta valor almacenado si existe"),
            ("Pago comision", row.CommissionPaid.ToString("0.00", CultureInfo.InvariantCulture), "dbo.AppMovilRegistro.pago_comision", "Pago real acumulado", "Se muestra valor almacenado"),
            ("Forma de pago", payment.FormaPago, string.IsNullOrWhiteSpace(payment.Fuente) ? "Sin fuente" : payment.Fuente, "Texto normalizado sin mezclar moneda", "Corregido en lectura"),
            ("Moneda", payment.Moneda, payment.FuenteMoneda, "Moneda separada de forma de pago", "Corregido en lectura")
        };

        return new CascoRelationCalculationDiagnostic(
            resolvedBranch.Code,
            nameof(CascoReadOnlyDataProvider),
            QuerySource,
            string.IsNullOrWhiteSpace(row.OriginalFolio) ? row.FolioControl : row.OriginalFolio,
            row.FolioControl,
            row.DriverName,
            row.Badge,
            row.Total,
            row.Cash,
            row.Card,
            row.Dollars,
            row.ExchangeRate,
            payment.PaymentMethodRemoto,
            row.PaymentsJson,
            row.Total,
            row.Total,
            row.CommissionCalculated,
            row.CommissionPaid,
            payment.FormaPago,
            payment.Moneda,
            NormalizePendingStatus(row.PaymentStatus, row.PayoutDate),
            comparisonRows);
    }

    public static async Task<CascoCommissionPreview> GetLocalCommissionPreviewAsync(
        string sqlPassword,
        string folioOriginal,
        decimal? saleOverride = null,
        string? transportOverride = null,
        string? paymentMethodOverride = null,
        decimal? payoutOverride = null,
        decimal? gastoOverride = null,
        decimal? degustacionOverride = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        var localBranch = CascoCommissionRuleService.BuildLocalBranch();
        var service = new CascoCommissionRuleService();
        return await service.PreviewAsync(
            localBranch,
            sqlPassword,
            folioOriginal,
            saleOverride,
            transportOverride,
            paymentMethodOverride,
            payoutOverride,
            gastoOverride,
            degustacionOverride,
            cancellationToken);
    }

    private static IReadOnlyList<string> DistinctNonEmpty(IEnumerable<string> values) =>
        values.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static LocalCommissionPaymentPreviewRow MapCommissionPaymentPreviewRow(LocalRelation row)
        => new(
            row.OperationFolio,
            row.PayoutDate,
            row.Driver,
            row.TransportType,
            row.Commission,
            row.PayoutPaid,
            row.PayoutStatus,
            row.Badge,
            row.Payout ?? 0m,
            row.PayoutUser,
            row.PayoutTicket,
            row.Site,
            row.DisplayLocalFolio,
            row.Sale);

    public static string GetPendingValidationSql() =>
        """
        SELECT COUNT(*)
        FROM dbo.AppMovilRegistro
        WHERE folio_app_original = @folioOriginal
          AND sitio = @sitio
          AND (
                NULLIF(@gafeteList, '') IS NULL
             OR CHARINDEX(',' + REPLACE(COALESCE(folio_gafete, ''), ' ', '') + ',', ',' + REPLACE(@gafeteList, ' ', '') + ',') > 0
          )
          AND UPPER(COALESCE(NULLIF(estado_pago_dejada, ''), NULLIF(payout_status, ''), 'pendiente')) NOT IN ('PAGADO','PAGADA');
        """;

    public static string GetPaymentUpdateSql() =>
        """
        UPDATE dbo.AppMovilRegistro
        SET estado_pago_dejada = @paidStatus,
            fecha_pago_dejada = @paidAt,
            usuario_pago_dejada = @paidBy,
            ticket_pago_dejada = @ticket,
            payout_status = @paidStatus,
            payout_date = @paidAt,
            payout_user = @paidBy,
            payout_ticket = @ticket
        WHERE folio_app_original = @folioOriginal
          AND sitio = @sitio
          AND (
                NULLIF(@gafeteList, '') IS NULL
             OR CHARINDEX(',' + REPLACE(COALESCE(folio_gafete, ''), ' ', '') + ',', ',' + REPLACE(@gafeteList, ' ', '') + ',') > 0
          )
          AND UPPER(COALESCE(NULLIF(estado_pago_dejada, ''), NULLIF(payout_status, ''), 'pendiente')) NOT IN ('PAGADO','PAGADA');
        """;

        public static string GetDejadaPaymentUpdateSql() =>
                """
                UPDATE dbo.dejadas
                SET pago = total,
                        fechapago = @paidAt
                WHERE folioregistrostr = @folioOriginal
                    AND nombrealmacen = @sitio
                    AND (pago IS NULL OR pago = 0);
                """;

    public static string GetRelationTargetSelectSql() =>
        """
        SELECT
          folio_app,
          folio_app_original,
          folio_pos,
          fecha_operacion,
          vendedor_nombre,
          folio_gafete,
          id_catalogo,
          tipo_operacion,
          hotel,
          origen,
          destino,
          unidad,
          placas,
          telefono_taxista,
          telefono_contacto,
          nacionalidad,
          sitio,
          pax,
          efectivo,
          tarjeta,
          dolares,
          tipo_cambio,
          notas,
          detalle_json
        FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
        WHERE folio_app_original = @folioOriginal
          AND sitio = @sitio;
        """;

    public static string GetRelationUpsertSql() =>
        """
        IF EXISTS (SELECT 1 FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folioOriginal)
        BEGIN
            UPDATE dbo.RelacionTicketTaxista
            SET FolioOperacion = @folioLocal,
                FolioPos = @folioPos,
                Gafete = @gafete,
                TaxistaId = @taxistaId,
                TaxistaNombre = @taxistaNombre,
                Vendedor = @vendedor,
                TransporteTipo = @transporteTipo,
                Dejada = @dejada,
                Observaciones = @observaciones,
                Usuario = @usuario,
                FechaActualizacion = SYSUTCDATETIME()
            WHERE FolioApp = @folioOriginal;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.RelacionTicketTaxista
                (FolioApp, FolioOperacion, FolioPos, Gafete, TaxistaId, TaxistaNombre, Vendedor, TransporteTipo, Dejada, Observaciones, Usuario)
            VALUES
                (@folioOriginal, @folioLocal, @folioPos, @gafete, @taxistaId, @taxistaNombre, @vendedor, @transporteTipo, @dejada, @observaciones, @usuario);
        END
        """;

    public static string GetDejadaSelectSql() =>
        """
        SELECT COUNT(*)
        FROM dbo.dejadas
        WHERE folioregistrostr = @folioOriginal
          AND nombrealmacen = @sitio
          AND CAST(fecha AS date) = @fecha
          AND COALESCE(gafete, '') = @gafete;
        """;

    public static string GetDejadaInsertSql() =>
        """
        INSERT INTO dbo.dejadas
            (idstaff, nombrestaff, nombrealmacen, idalmacen, fecha, hora, idcajero, nombrecajero, total,
             codigorecepcion, folioregistro, folioregistrostr, unidad, pax, hotel, nombrevendedor,
             tipotransporte, telefono, horaentrada, horasalida, totalventa, comision, pago,
             totalefectivo, totaltarjeta, totalgastos, gafete)
        VALUES
            (@idstaff, @nombrestaff, @sitio, @idalmacen, @fechaDateTime, @horaTexto, 0, @usuario, @dejada,
             @codigoRecepcion, @folioRegistro, @folioOriginal, @unidad, @pax, @hotel, @vendedorNombre,
             @transporteTipo, @telefono, @horaTexto, @horaTexto, @venta, 0, 0,
             @totalEfectivo, @totalTarjeta, 0, @gafete);
        """;

    public static string GetDejadaUpdateSql() =>
        """
        UPDATE dbo.dejadas
        SET idstaff = @idstaff,
            nombrestaff = @nombrestaff,
            codigorecepcion = @codigoRecepcion,
            folioregistro = @folioRegistro,
            unidad = @unidad,
            pax = @pax,
            hotel = @hotel,
            nombrevendedor = @vendedorNombre,
            tipotransporte = @transporteTipo,
            telefono = @telefono,
            totalventa = @venta,
            total = @dejada,
            totalefectivo = @totalEfectivo,
            totaltarjeta = @totalTarjeta,
            comision = 0,
            pago = 0,
            gafete = @gafete
        WHERE folioregistrostr = @folioOriginal
          AND nombrealmacen = @sitio
          AND CAST(fecha AS date) = @fecha
          AND COALESCE(gafete, '') = @gafete;
        """;

    private static async Task<List<LocalRelation>> LoadCascoRelationRowsAsync(
        SqlConnection connection,
        string siteName,
        string? search,
        DateTime? start,
        DateTime? end,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            WITH filtered_app AS
            (
              SELECT
                a.folio_app,
                a.folio_app_original,
                a.folio_pos,
                a.folio_gafete,
                a.vendedor_nombre,
                a.notas,
                a.usuario_movil,
                a.fecha_operacion,
                a.fecha_creacion,
                COALESCE(a.fecha_operacion, a.fecha_creacion) AS EffectiveDate,
                a.hotel,
                a.origen,
                a.sitio,
                a.destino,
                a.unidad,
                a.placas,
                a.telefono_taxista,
                a.telefono_contacto,
                a.nacionalidad,
                a.modelo_vehiculo,
                a.tipo_operacion,
                a.comision_calculada,
                a.pago_comision,
                a.estado_pago_dejada,
                a.payout_status,
                a.ticket_pago_dejada,
                a.id_catalogo,
                a.usuario_pago_dejada,
                a.fecha_pago_dejada,
                a.pax,
                a.efectivo,
                a.tarjeta,
                a.dolares,
                a.tipo_cambio,
                a.detalle_json,
                a.pagos_json,
                a.total
              FROM dbo.AppMovilRegistro a
              WHERE a.sitio = @sitio
                AND (@start IS NULL OR COALESCE(a.fecha_operacion, a.fecha_creacion) >= @start)
                AND (@endExclusive IS NULL OR COALESCE(a.fecha_operacion, a.fecha_creacion) < @endExclusive)
                AND (
                      @q IS NULL
                   OR a.folio_app LIKE '%' + @q + '%'
                   OR a.folio_app_original LIKE '%' + @q + '%'
                   OR COALESCE(a.folio_pos, '') LIKE '%' + @q + '%'
                   OR COALESCE(a.folio_gafete, '') LIKE '%' + @q + '%'
                   OR COALESCE(a.vendedor_nombre, '') LIKE '%' + @q + '%'
                   OR COALESCE(a.hotel, '') LIKE '%' + @q + '%'
                   OR COALESCE(a.nacionalidad, '') LIKE '%' + @q + '%'
                   OR COALESCE(a.modelo_vehiculo, '') LIKE '%' + @q + '%'
                   OR COALESCE(a.tipo_operacion, '') LIKE '%' + @q + '%'
                )
            ),
            latest_relation AS
            (
              SELECT
                rr.FolioApp,
                rr.FolioPos,
                rr.Gafete,
                rr.TaxistaNombre,
                rr.Vendedor,
                rr.TransporteTipo,
                rr.Dejada,
                ROW_NUMBER() OVER
                (
                  PARTITION BY rr.FolioApp, COALESCE(NULLIF(rr.Gafete, ''), '')
                  ORDER BY rr.FechaActualizacion DESC, rr.Id DESC
                ) AS rn
              FROM dbo.RelacionTicketTaxista rr
              INNER JOIN filtered_app a
                ON a.folio_app_original = rr.FolioApp
            ),
            latest_dejada AS
            (
              SELECT
                a.folio_app_original AS FolioOriginal,
                COALESCE(a.folio_gafete, '') AS Gafete,
                dd.total,
                dd.totalventa,
                ROW_NUMBER() OVER
                (
                  PARTITION BY a.folio_app_original, COALESCE(a.folio_gafete, '')
                  ORDER BY dd.fecha DESC, dd.folioregistro DESC
                ) AS rn
              FROM filtered_app a
              INNER JOIN dbo.dejadas dd
                ON dd.folioregistrostr = a.folio_app_original
               AND dd.nombrealmacen = @sitio
               AND dd.fecha >= DATEADD(day, DATEDIFF(day, 0, a.EffectiveDate), 0)
               AND dd.fecha < DATEADD(day, DATEDIFF(day, 0, a.EffectiveDate) + 1, 0)
               AND COALESCE(dd.gafete, '') = COALESCE(a.folio_gafete, '')
            )
            SELECT
              COALESCE(a.folio_app, '') AS AppFolio,
              COALESCE(a.folio_app_original, '') AS OperationFolio,
              COALESCE(NULLIF(a.folio_pos, ''), NULLIF(r.FolioPos, ''), '') AS PosFolio,
              COALESCE(NULLIF(a.folio_gafete, ''), NULLIF(r.Gafete, ''), '') AS Badge,
              COALESCE(NULLIF(a.vendedor_nombre, ''), NULLIF(r.TaxistaNombre, ''), '') AS DriverName,
              COALESCE(NULLIF(r.Vendedor, ''), '') AS VendorName,
              COALESCE(r.Dejada, d.total, a.total, 0) AS Payout,
              COALESCE(a.notas, '') AS Notes,
              COALESCE(a.usuario_movil, '') AS SourceUser,
              COALESCE(CONVERT(nvarchar(30), a.EffectiveDate, 120), '') AS DateText,
              COALESCE(a.hotel, '') AS Hotel,
              COALESCE(a.origen, '') AS Origen,
              COALESCE(a.sitio, '') AS Sitio,
              COALESCE(a.destino, '') AS Destino,
              COALESCE(a.unidad, '') AS Unidad,
              COALESCE(a.placas, '') AS Placas,
              COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
              COALESCE(a.nacionalidad, '') AS Nacionalidad,
              COALESCE(NULLIF(a.modelo_vehiculo, ''), NULLIF(a.tipo_operacion, ''), NULLIF(r.TransporteTipo, ''), '') AS TransporteTipo,
              COALESCE(a.comision_calculada, 0) AS Commission,
              COALESCE(a.pago_comision, 0) AS CommissionPaid,
              CAST(0 AS decimal(18,2)) AS Sale,
              COALESCE(NULLIF(a.estado_pago_dejada, ''), NULLIF(a.payout_status, ''), '') AS PayoutStatus,
              COALESCE(a.ticket_pago_dejada, '') AS PayoutTicket,
              COALESCE(CAST(a.id_catalogo AS nvarchar(60)), '') AS TaxistaId,
              COALESCE(a.usuario_pago_dejada, '') AS PayoutUser,
              COALESCE(CONVERT(nvarchar(30), a.fecha_pago_dejada, 120), '') AS PayoutDate,
              COALESCE(a.pax, 0) AS Passengers,
              COALESCE(a.efectivo, 0) AS Cash,
              COALESCE(a.tarjeta, 0) AS Card,
              COALESCE(a.dolares, 0) AS Dollars,
              COALESCE(a.tipo_cambio, 0) AS ExchangeRate,
              COALESCE(a.detalle_json, '') AS DetailJson,
              COALESCE(a.pagos_json, '') AS PaymentsJson
            FROM filtered_app a
            LEFT JOIN latest_relation r
              ON r.FolioApp = a.folio_app_original
             AND COALESCE(NULLIF(r.Gafete, ''), '') = COALESCE(a.folio_gafete, '')
             AND r.rn = 1
            LEFT JOIN latest_dejada d
              ON d.FolioOriginal = a.folio_app_original
             AND d.Gafete = COALESCE(a.folio_gafete, '')
             AND d.rn = 1
            ORDER BY a.EffectiveDate DESC, a.folio_app_original DESC;
            """,
            connection);
        command.CommandTimeout = 120;
        command.Parameters.AddWithValue("@sitio", siteName);
        command.Parameters.AddWithValue("@start", start?.Date ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@endExclusive", end?.Date.AddDays(1) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@q", string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<LocalRelation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var sourceRow = new CascoSourceRow(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                ParseOperationDate(reader.IsDBNull(9) ? string.Empty : reader.GetString(9)) ?? DateTime.MinValue,
                reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                reader.IsDBNull(24) ? string.Empty : reader.GetString(24),
                reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                reader.IsDBNull(28) ? 0m : Convert.ToDecimal(reader.GetValue(28), CultureInfo.InvariantCulture),
                reader.IsDBNull(29) ? 0m : Convert.ToDecimal(reader.GetValue(29), CultureInfo.InvariantCulture),
                reader.IsDBNull(30) ? 0m : Convert.ToDecimal(reader.GetValue(30), CultureInfo.InvariantCulture),
                reader.IsDBNull(31) ? 0m : Convert.ToDecimal(reader.GetValue(31), CultureInfo.InvariantCulture),
                reader.IsDBNull(32) ? string.Empty : reader.GetString(32),
                reader.IsDBNull(27) ? 0 : Convert.ToInt32(reader.GetValue(27), CultureInfo.InvariantCulture));
            var paymentMethod = InferPaymentMethod(sourceRow);
            var currency = InferCurrency(sourceRow);
            var commission = reader.IsDBNull(19) ? 0m : Convert.ToDecimal(reader.GetValue(19), CultureInfo.InvariantCulture);
            var commissionPaid = reader.IsDBNull(20) ? 0m : Convert.ToDecimal(reader.GetValue(20), CultureInfo.InvariantCulture);
            var remoteDriverName = ExtractRemoteText(sourceRow.DetailJson, "driverName");
            var remoteSellerName = ExtractRemoteText(sourceRow.DetailJson, "sellerName");
            var driverName = FirstFilled(remoteDriverName, sourceRow.DriverName);
            var vendorName = FirstFilled(reader.IsDBNull(5) ? string.Empty : reader.GetString(5), remoteSellerName);
            if (string.Equals(vendorName.Trim(), driverName.Trim(), StringComparison.OrdinalIgnoreCase))
                vendorName = string.Empty;
            var adultCount = ExtractRemoteInt(sourceRow.DetailJson, "adultCount");
            var youthCount = ExtractRemoteInt(sourceRow.DetailJson, "youthCount");
            var minorCount = ExtractRemoteInt(sourceRow.DetailJson, "minorCount");
            result.Add(new LocalRelation(
                0,
                sourceRow.FolioLocal,
                sourceRow.FolioOriginal,
                sourceRow.PosFolio,
                sourceRow.Badge,
                driverName,
                vendorName,
                reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                "APP MOVIL",
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                sourceRow.Hotel,
                sourceRow.Origin,
                sourceRow.Site,
                sourceRow.Destination,
                sourceRow.Unit,
                sourceRow.Plates,
                sourceRow.Phone,
                sourceRow.Nationality,
                sourceRow.TransportType,
                reader.IsDBNull(21) ? 0m : Convert.ToDecimal(reader.GetValue(21), CultureInfo.InvariantCulture),
                commission,
                commissionPaid,
                paymentMethod,
                NormalizePendingStatus(reader.IsDBNull(22) ? string.Empty : reader.GetString(22), reader.IsDBNull(26) ? string.Empty : reader.GetString(26)),
                ResolveCommissionStatus(commission, commissionPaid),
                reader.IsDBNull(23) ? string.Empty : reader.GetString(23),
                sourceRow.TaxistaId,
                reader.IsDBNull(25) ? string.Empty : reader.GetString(25),
                reader.IsDBNull(26) ? string.Empty : reader.GetString(26),
                0m,
                reader.IsDBNull(27) ? 0 : Convert.ToInt32(reader.GetValue(27), CultureInfo.InvariantCulture),
                string.Empty,
                currency,
                paymentMethod,
                reader.IsDBNull(33) ? string.Empty : reader.GetString(33),
                reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                sourceRow.Cash,
                sourceRow.Card,
                sourceRow.Dollars,
                sourceRow.ExchangeRate,
                AdultPassengers: adultCount,
                YouthPassengers: youthCount,
                ChildPassengers: minorCount));
        }

        return MergeRelationRowsByOperation(result);
    }

    private static List<LocalRelation> MergeRelationRowsByOperation(IReadOnlyList<LocalRelation> rows)
    {
        if (rows.Count <= 1)
            return rows.ToList();

        return rows
            .GroupBy(row => $"{row.OperationFolio}|{row.Site}|{row.DateText}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToArray();
                if (items.Length == 1)
                    return items[0];

                var first = items[0];
                var badgeList = NormalizeBadgeList(string.Join(", ", items.Select(item => item.Badge)));
                var payoutStatus = items.All(item => IsPaidStatus(item.PayoutStatus))
                    ? FirstNonEmpty(items.Select(item => item.PayoutStatus))
                    : "pendiente";

                return first with
                {
                    Badge = string.IsNullOrWhiteSpace(badgeList) ? first.Badge : badgeList,
                    PosFolio = FirstNonEmpty(items.Select(item => item.PosFolio)),
                    Payout = items.Select(item => item.Payout ?? 0m).DefaultIfEmpty(0m).Max(),
                    PayoutStatus = payoutStatus,
                    PayoutTicket = FirstNonEmpty(items.Select(item => item.PayoutTicket)),
                    PayoutUser = FirstNonEmpty(items.Select(item => item.PayoutUser)),
                    PayoutDate = FirstNonEmpty(items.Select(item => item.PayoutDate)),
                    Passengers = items.Select(item => item.Passengers).DefaultIfEmpty(first.Passengers).Max(),
                    AdultPassengers = items.Select(item => item.AdultPassengers).DefaultIfEmpty(first.AdultPassengers).Max(),
                    YouthPassengers = items.Select(item => item.YouthPassengers).DefaultIfEmpty(first.YouthPassengers).Max(),
                    ChildPassengers = items.Select(item => item.ChildPassengers).DefaultIfEmpty(first.ChildPassengers).Max(),
                    TotalAmount = items.Select(item => item.TotalAmount).DefaultIfEmpty(first.TotalAmount).Max(),
                    CashAmount = items.Select(item => item.CashAmount).DefaultIfEmpty(first.CashAmount).Max(),
                    CardAmount = items.Select(item => item.CardAmount).DefaultIfEmpty(first.CardAmount).Max(),
                    DollarsAmount = items.Select(item => item.DollarsAmount).DefaultIfEmpty(first.DollarsAmount).Max()
                };
            })
            .OrderByDescending(row => ParseOperationDate(row.DateText) ?? DateTime.MinValue)
            .ThenByDescending(row => row.OperationFolio, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<List<CascoSourceRow>> LoadCascoSourceRowsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string siteName,
        string folioOriginal,
        bool lockTarget,
        System.Threading.CancellationToken cancellationToken)
    {
        var sql = lockTarget ? GetRelationTargetSelectSql() : GetRelationTargetSelectSql().Replace(" WITH (UPDLOCK, HOLDLOCK)", string.Empty, StringComparison.Ordinal);
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        command.Parameters.AddWithValue("@sitio", siteName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<CascoSourceRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CascoSourceRow(
                reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(4) ? string.Empty : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(5) ? string.Empty : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(3) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(3), CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? string.Empty : Convert.ToString(reader.GetValue(8), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(9) ? string.Empty : Convert.ToString(reader.GetValue(9), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(10) ? string.Empty : Convert.ToString(reader.GetValue(10), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(11) ? string.Empty : Convert.ToString(reader.GetValue(11), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(12) ? string.Empty : Convert.ToString(reader.GetValue(12), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(13)
                    ? reader.IsDBNull(14) ? string.Empty : Convert.ToString(reader.GetValue(14), CultureInfo.InvariantCulture) ?? string.Empty
                    : Convert.ToString(reader.GetValue(13), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(15) ? string.Empty : Convert.ToString(reader.GetValue(15), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(7) ? string.Empty : Convert.ToString(reader.GetValue(7), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(6) ? string.Empty : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(16) ? string.Empty : Convert.ToString(reader.GetValue(16), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(22) ? string.Empty : Convert.ToString(reader.GetValue(22), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(18) ? 0m : Convert.ToDecimal(reader.GetValue(18), CultureInfo.InvariantCulture),
                reader.IsDBNull(19) ? 0m : Convert.ToDecimal(reader.GetValue(19), CultureInfo.InvariantCulture),
                reader.IsDBNull(20) ? 0m : Convert.ToDecimal(reader.GetValue(20), CultureInfo.InvariantCulture),
                reader.IsDBNull(21) ? 0m : Convert.ToDecimal(reader.GetValue(21), CultureInfo.InvariantCulture),
                reader.IsDBNull(23) ? string.Empty : Convert.ToString(reader.GetValue(23), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(17) ? 0 : Convert.ToInt32(reader.GetValue(17), CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    private static int CountMatchingPlaza28Rows(IReadOnlyList<CascoSourceRow> rows, string expectedSiteName) =>
        rows.Count(row => !string.Equals(row.Site, expectedSiteName, StringComparison.OrdinalIgnoreCase));

    private static async Task<List<LocalRelation>> EnrichCascoRelationSalesAsync(
        IReadOnlyList<LocalRelation> rows,
        BranchConfiguration branch,
        string sqlPassword,
        System.Threading.CancellationToken cancellationToken)
    {
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(sqlPassword))
            return rows.ToList();

        var salesProvider = new CascoSalesDataProvider(branch);
        var operationFolios = rows
            .Select(row => row.OperationFolio?.Trim())
            .Where(folio => !string.IsNullOrWhiteSpace(folio))
            .Select(folio => folio!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var ticketsByFolio = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.OperationFolio))
            .GroupBy(row => row.OperationFolio.Trim().TrimStart('0'), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => row.PosFolio)
                    .Where(ticket => !string.IsNullOrWhiteSpace(ticket))
                    .Select(ticket => ticket!)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var summariesByFolio = await salesProvider.GetOperationSaleSummariesAsync(sqlPassword, operationFolios, ticketsByFolio);

        var result = new List<LocalRelation>(rows.Count);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = (row.OperationFolio?.Trim() ?? string.Empty).TrimStart('0');
            if (!summariesByFolio.TryGetValue(key, out var summary))
            {
                result.Add(row);
                continue;
            }

            var remisionSale = summary.Compuadmo + summary.Joyeria;
            var saleDetailParts = new List<string>();
            if (summary.Compuadmo > 0m)
                saleDetailParts.Add($"COMP {summary.Compuadmo:C2}");
            if (summary.Joyeria > 0m)
                saleDetailParts.Add($"JOY {summary.Joyeria:C2}");
            if (summary.Tickets.Count > 0)
                saleDetailParts.Add($"TK {string.Join(", ", summary.Tickets)}");

            var effectivePaymentMethod = !string.IsNullOrWhiteSpace(summary.PaymentDescription)
                ? summary.PaymentDescription
                : row.PaymentMethod;
            var effectivePosFolio = summary.Tickets.Count > 0
                ? string.Join(", ", summary.Tickets)
                : row.PosFolio;
            var effectiveVendor = !string.IsNullOrWhiteSpace(summary.VendorName)
                ? summary.VendorName
                : row.Vendor;

            if (remisionSale <= 0m)
            {
                result.Add(row with
                {
                    PosFolio = effectivePosFolio,
                    Vendor = effectiveVendor,
                    PaymentMethod = effectivePaymentMethod,
                    SaleDetail = saleDetailParts.Count == 0 ? row.SaleDetail : string.Join(" | ", saleDetailParts)
                });
                continue;
            }

            result.Add(row with
            {
                PosFolio = effectivePosFolio,
                Vendor = effectiveVendor,
                PaymentMethod = effectivePaymentMethod,
                Sale = remisionSale,
                SaleDetail = saleDetailParts.Count == 0 ? row.SaleDetail : string.Join(" | ", saleDetailParts),
                TotalAmount = remisionSale
            });
        }

        return result;
    }

    private static async Task<List<LocalRelation>> EnrichCascoRelationCommissionsAsync(
        IReadOnlyList<LocalRelation> rows,
        BranchConfiguration branch,
        string sqlPassword,
        System.Threading.CancellationToken cancellationToken)
    {
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(sqlPassword))
            return rows.ToList();

        if (!CascoCommissionRuleService.IsCascoCommissionEnvironment(branch, branch.Code))
            return rows.ToList();

        var service = new CascoCommissionRuleService();
        var generated = await LoadGeneratedCommissionsAsync(branch, sqlPassword, rows, cancellationToken);
        var rules = await service.LoadActiveRulesAsync(branch, sqlPassword, cancellationToken);
        if (rules.Count == 0)
        {
            return rows.Select(row =>
            {
                if (TryGetGeneratedCommission(generated, row, out var generatedAmount, out var generatedPaid, out var generatedStatus))
                    return row with { Commission = generatedAmount, CommissionPaid = generatedPaid, CommissionStatus = generatedStatus, OrigenComision = "Calculada" };

                var venta = row.Sale;
                if (venta <= 0m)
                    return row with { Commission = 0m, CommissionStatus = "SIN COMISION", OrigenComision = "Respaldo" };

                var amount = CascoCommissionRuleService.CalculateDefaultCommission(venta);
                return row with
                {
                    Commission = amount,
                    CommissionStatus = ResolveCommissionStatus(amount, row.CommissionPaid),
                    OrigenComision = "Respaldo"
                };
            }).ToList();
        }

        return rows.Select(row =>
        {
            if (TryGetGeneratedCommission(generated, row, out var generatedAmount, out var generatedPaid, out var generatedStatus))
                return row with { Commission = generatedAmount, CommissionPaid = generatedPaid, CommissionStatus = generatedStatus, OrigenComision = "Calculada" };

            var venta = row.Sale;
            if (venta <= 0m)
                return row with { Commission = 0m, CommissionStatus = "SIN COMISION", OrigenComision = "Respaldo" };

            var proveedor = CascoCommissionRuleService.NormalizeProvider(row.TransportType);
            var conTarjeta = CascoCommissionRuleService.IsCardLikePayment(row.PaymentMethod, row.CardAmount);
            var match = service.ResolveRule(rules, proveedor, row.TransportType, conTarjeta, venta);
            if (match.IsAmbiguous)
            {
                return row with { Commission = 0m, CommissionStatus = "REGLA AMBIGUA", OrigenComision = "Respaldo" };
            }

            var amount = CascoCommissionRuleService.CalculateAmounts(match.Rule, venta).CommissionAmount;

            return row with
            {
                Commission = amount,
                CommissionStatus = amount > 0m ? ResolveCommissionStatus(amount, row.CommissionPaid) : "SIN COMISION",
                OrigenComision = "Respaldo"
            };
        }).ToList();
    }

    private static async Task<Dictionary<string, GeneratedCommissionRow>> LoadGeneratedCommissionsAsync(
        BranchConfiguration branch,
        string sqlPassword,
        IReadOnlyList<LocalRelation> rows,
        System.Threading.CancellationToken cancellationToken)
    {
        var folios = rows
            .Select(row => FirstFilled(row.OperationFolio, row.AppFolio).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (folios.Length == 0)
            return new Dictionary<string, GeneratedCommissionRow>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var provider = new CascoReadOnlyDataProvider(branch);
            await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
            await using (var exists = new SqlCommand("SELECT OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas', N'U');", connection))
            {
                var objectId = await exists.ExecuteScalarAsync(cancellationToken);
                if (objectId is null || objectId == DBNull.Value)
                    return new Dictionary<string, GeneratedCommissionRow>(StringComparer.OrdinalIgnoreCase);
            }

            await using (var ensurePaymentColumns = connection.CreateCommand())
            {
                ensurePaymentColumns.CommandText = """
                    IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'PagoComision') IS NULL
                        ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD PagoComision DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_PagoComision_Read_Add DEFAULT ((0));
                    IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FechaPagoComision') IS NULL
                        ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FechaPagoComision DATETIME2(0) NULL;
                    IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'UsuarioPagoComision') IS NULL
                        ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD UsuarioPagoComision NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_UsuarioPagoComision_Read_Add DEFAULT (N'');
                    IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'TicketPagoComision') IS NULL
                        ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD TicketPagoComision NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_TicketPagoComision_Read_Add DEFAULT (N'');
                    """;
                await ensurePaymentColumns.ExecuteNonQueryAsync(cancellationToken);
            }

            var parameters = new List<string>();
            await using var command = connection.CreateCommand();
            for (var i = 0; i < folios.Length; i++)
            {
                var parameter = "@folio" + i.ToString(CultureInfo.InvariantCulture);
                command.Parameters.Add(parameter, SqlDbType.NVarChar, 120).Value = folios[i];
                parameters.Add(parameter);
            }

            command.CommandText = $"""
                SELECT FolioOriginal, ComisionCalculada, PagoComision, Estatus
                FROM
                (
                    SELECT
                        FolioOriginal,
                        ComisionCalculada,
                        COALESCE(PagoComision, 0) AS PagoComision,
                        Estatus,
                        ROW_NUMBER() OVER
                        (
                            PARTITION BY FolioOriginal
                            ORDER BY FechaCalculo DESC, Id DESC
                        ) AS rn
                    FROM dbo.ControlTaxiComisionesGeneradas
                    WHERE BranchCode = N'CV'
                      AND FolioOriginal IN ({string.Join(",", parameters)})
                      AND Estatus <> N'CANCELADA'
                ) x
                WHERE rn = 1;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var result = new Dictionary<string, GeneratedCommissionRow>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken))
            {
                var folio = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(folio))
                    continue;
                result[folio] = new GeneratedCommissionRow(
                    reader.IsDBNull(1) ? 0m : Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture),
                    reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
                    reader.IsDBNull(3) ? "PENDIENTE" : reader.GetString(3));
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, GeneratedCommissionRow>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool TryGetGeneratedCommission(
        IReadOnlyDictionary<string, GeneratedCommissionRow> generated,
        LocalRelation row,
        out decimal amount,
        out decimal paid,
        out string status)
    {
        var folio = FirstFilled(row.OperationFolio, row.AppFolio);
        if (generated.TryGetValue(folio, out var found) && found.Amount > 0m)
        {
            amount = found.Amount;
            paid = found.Paid;
            status = string.IsNullOrWhiteSpace(found.Status) ? "PENDIENTE" : found.Status;
            return true;
        }

        amount = 0m;
        paid = 0m;
        status = string.Empty;
        return false;
    }

    private static async Task<bool> RelationExistsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string folioOriginal,
        System.Threading.CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(*) FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folioOriginal;";
        await using var command = transaction is null
            ? new SqlCommand(sql, connection)
            : new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (count > 1)
            throw new InvalidOperationException("Se encontraron multiples relaciones para el mismo folio original.");

        return count == 1;
    }

    private static async Task<CascoDejadaMatchInfo> GetDejadaMatchInfoAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string folioOriginal,
        string siteName,
        DateTime operationDate,
        string badge,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = transaction is null
            ? new SqlCommand(GetDejadaSelectSql(), connection)
            : new SqlCommand(GetDejadaSelectSql(), connection, transaction);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        command.Parameters.AddWithValue("@sitio", siteName);
        command.Parameters.AddWithValue("@fecha", operationDate.Date);
        command.Parameters.AddWithValue("@gafete", badge ?? string.Empty);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return new CascoDejadaMatchInfo(count, count == 1);
    }

    private static async Task ExecuteRelationUpsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        LocalRelation relation,
        string user,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(GetRelationUpsertSql(), connection, transaction);
        command.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 60));
        command.Parameters.AddWithValue("@folioLocal", NormalizeForSqlLength(sourceRow.FolioLocal, 60));
        command.Parameters.AddWithValue("@folioPos", NormalizeForSqlLength(FirstFilled(relation.PosFolio, sourceRow.PosFolio), 120));
        command.Parameters.AddWithValue("@gafete", NormalizeForSqlLength(FirstFilled(relation.Badge, sourceRow.Badge), 300));
        command.Parameters.AddWithValue("@taxistaId", ParseBigIntOrZero(FirstFilled(relation.TaxistaId, sourceRow.TaxistaId)));
        command.Parameters.AddWithValue("@taxistaNombre", NormalizeForSqlLength(FirstFilled(relation.Driver, sourceRow.DriverName), 150));
        command.Parameters.AddWithValue("@vendedor", NormalizeForSqlLength(relation.Vendor?.Trim() ?? string.Empty, 150));
        command.Parameters.AddWithValue("@transporteTipo", NormalizeForSqlLength(FirstFilled(relation.TransportType, sourceRow.TransportType), 20));
        command.Parameters.AddWithValue("@dejada", relation.Payout ?? 0m);
        command.Parameters.AddWithValue("@observaciones", NormalizeForSqlLength(relation.Notes ?? string.Empty, 300));
        command.Parameters.AddWithValue("@usuario", NormalizeForSqlLength(user, 50));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task PersistSourceEditSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        LocalRelation relation,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE dbo.AppMovilRegistro
            SET folio_gafete = @gafete,
                pax = @pax,
                tipo_operacion = @transporteTipo,
                modelo_vehiculo = @transporteTipo,
                nacionalidad = @nacionalidad,
                total = @dejada,
                notas = @notas,
                detalle_json = @detailJson
            WHERE folio_app_original = @folioOriginal
              AND sitio = @sitio;
            """;
        command.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 60));
        command.Parameters.AddWithValue("@sitio", NormalizeForSqlLength(sourceRow.Site, 300));
        command.Parameters.AddWithValue("@gafete", NormalizeForSqlLength(FirstFilled(relation.Badge, sourceRow.Badge), 300));
        command.Parameters.AddWithValue("@pax", ResolveRelationPassengers(sourceRow, relation));
        command.Parameters.AddWithValue("@transporteTipo", NormalizeForSqlLength(FirstFilled(relation.TransportType, sourceRow.TransportType), 100));
        command.Parameters.AddWithValue("@nacionalidad", NormalizeForSqlLength(FirstFilled(relation.Nationality, sourceRow.Nationality), 240));
        command.Parameters.AddWithValue("@dejada", relation.Payout ?? 0m);
        command.Parameters.AddWithValue("@notas", NormalizeForSqlLength(relation.Notes ?? string.Empty, 1000));
        command.Parameters.AddWithValue("@detailJson", BuildUpdatedSourceDetailJson(sourceRow, relation));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReconcileCascoPhysicalBadgeRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        LocalRelation relation,
        string user,
        System.Threading.CancellationToken cancellationToken)
    {
        var badges = SplitBadgeValues(FirstFilled(relation.Badge, sourceRow.Badge));
        if (badges.Count == 0)
            return;

        await ReconcileAppMovilRegistroGafetesAsync(connection, transaction, sourceRow, badges, cancellationToken);
        await DeleteStaleDejadaBadgeRowsAsync(connection, transaction, sourceRow, badges, cancellationToken);
        await ReconcileGafeteRowsAsync(connection, transaction, sourceRow, badges, user, cancellationToken);
    }

    private static async Task ReconcileAppMovilRegistroGafetesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        IReadOnlyList<string> badges,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        AddBadgeListParameters(deleteCommand, badges, "@badge");
        deleteCommand.CommandText =
            $"""
            IF OBJECT_ID(N'dbo.AppMovilRegistroGafetes', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.AppMovilRegistroGafetes
                WHERE FolioApp = @folioOriginal
                  AND UPPER(LTRIM(RTRIM(FolioGafete))) NOT IN ({BuildInList(badges.Count, "@badge")});
            END;
            """;
        deleteCommand.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 120));
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var badge in badges)
        {
            await using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText =
                """
                IF OBJECT_ID(N'dbo.AppMovilRegistroGafetes', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM dbo.AppMovilRegistroGafetes WITH (UPDLOCK, HOLDLOCK)
                        WHERE FolioApp = @folioOriginal
                          AND UPPER(LTRIM(RTRIM(FolioGafete))) = UPPER(LTRIM(RTRIM(@gafete)))
                    )
                    BEGIN
                        UPDATE dbo.AppMovilRegistroGafetes
                        SET IdCatalogo = CASE WHEN @idCatalogo > 0 THEN @idCatalogo ELSE IdCatalogo END
                        WHERE FolioApp = @folioOriginal
                          AND UPPER(LTRIM(RTRIM(FolioGafete))) = UPPER(LTRIM(RTRIM(@gafete)));
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.AppMovilRegistroGafetes (FolioApp, IdCatalogo, FolioGafete)
                        VALUES (@folioOriginal, NULLIF(@idCatalogo, 0), @gafete);
                    END
                END;
                """;
            upsertCommand.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 120));
            upsertCommand.Parameters.AddWithValue("@idCatalogo", ParseIntOrZero(sourceRow.TaxistaId));
            upsertCommand.Parameters.AddWithValue("@gafete", NormalizeForSqlLength(badge, 40));
            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task DeleteStaleDejadaBadgeRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        IReadOnlyList<string> badges,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        AddBadgeListParameters(command, badges, "@badge");
        command.CommandText =
            $"""
            DELETE FROM dbo.dejadas
            WHERE folioregistrostr = @folioOriginal
              AND nombrealmacen = @sitio
              AND CAST(fecha AS date) = @fecha
              AND
              (
                    gafete IS NULL
                 OR LTRIM(RTRIM(gafete)) = N''
                 OR UPPER(LTRIM(RTRIM(gafete))) NOT IN ({BuildInList(badges.Count, "@badge")})
              );
            """;
        command.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 150));
        command.Parameters.AddWithValue("@sitio", NormalizeForSqlLength(sourceRow.Site, 300));
        command.Parameters.AddWithValue("@fecha", sourceRow.OperationDate.Date);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReconcileGafeteRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        IReadOnlyList<string> badges,
        string user,
        System.Threading.CancellationToken cancellationToken)
    {
        var numericBadges = badges
            .Select(badge => int.TryParse(badge, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (numericBadges.Length == 0)
            return;

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        AddNumberListParameters(deleteCommand, numericBadges, "@gafete");
        deleteCommand.CommandText =
            $"""
            IF OBJECT_ID(N'dbo.gafete', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.gafete
                WHERE
                (
                       COALESCE(CONVERT(NVARCHAR(50), matricula), N'') = @folioOriginal
                    OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), N'') = @folioOriginal
                    OR
                    (
                        ISNUMERIC(CONVERT(NVARCHAR(50), folioperacion)) = 1
                        AND CONVERT(BIGINT, folioperacion) = @folioOperacionNumero
                    )
                )
                  AND CAST(fecha AS date) = @fecha
                  AND (gafete IS NULL OR gafete NOT IN ({BuildInList(numericBadges.Length, "@gafete")}));
            END;
            """;
        deleteCommand.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 50));
        deleteCommand.Parameters.AddWithValue("@folioOperacionNumero", ParseBigIntNullable(sourceRow.FolioOriginal) ?? 0L);
        deleteCommand.Parameters.AddWithValue("@fecha", sourceRow.OperationDate.Date);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var badge in numericBadges)
        {
            await using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText =
                """
                IF OBJECT_ID(N'dbo.gafete', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM dbo.gafete WITH (UPDLOCK, HOLDLOCK)
                        WHERE
                        (
                               COALESCE(CONVERT(NVARCHAR(50), matricula), N'') = @folioOriginal
                            OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), N'') = @folioOriginal
                            OR
                            (
                                ISNUMERIC(CONVERT(NVARCHAR(50), folioperacion)) = 1
                                AND CONVERT(BIGINT, folioperacion) = @folioOperacionNumero
                            )
                        )
                          AND gafete = @gafete
                          AND CAST(fecha AS date) = @fecha
                    )
                    BEGIN
                        UPDATE dbo.gafete
                        SET matricula = @folioOriginal,
                            fecha = @fechaDateTime,
                            venta = 'A',
                            hora = @fechaDateTime,
                            folioperacion = @folioOperacionNumero,
                            movimiento = @movimiento,
                            usuario = @usuario
                        WHERE
                        (
                               COALESCE(CONVERT(NVARCHAR(50), matricula), N'') = @folioOriginal
                            OR COALESCE(CONVERT(NVARCHAR(50), folioperacion), N'') = @folioOriginal
                            OR
                            (
                                ISNUMERIC(CONVERT(NVARCHAR(50), folioperacion)) = 1
                                AND CONVERT(BIGINT, folioperacion) = @folioOperacionNumero
                            )
                        )
                          AND gafete = @gafete
                          AND CAST(fecha AS date) = @fecha;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.gafete (matricula, gafete, fecha, venta, hora, folioperacion, movimiento, usuario)
                        VALUES (@folioOriginal, @gafete, @fechaDateTime, 'A', @fechaDateTime, @folioOperacionNumero, @movimiento, @usuario);
                    END
                END;
                """;
            upsertCommand.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 50));
            upsertCommand.Parameters.AddWithValue("@folioOperacionNumero", ParseBigIntNullable(sourceRow.FolioOriginal) ?? 0L);
            upsertCommand.Parameters.AddWithValue("@gafete", badge);
            upsertCommand.Parameters.AddWithValue("@fecha", sourceRow.OperationDate.Date);
            upsertCommand.Parameters.AddWithValue("@fechaDateTime", sourceRow.OperationDate);
            upsertCommand.Parameters.AddWithValue("@movimiento", NormalizeForSqlLength("APP MOVIL", 100));
            upsertCommand.Parameters.AddWithValue("@usuario", NormalizeForSqlLength(user, 100));
            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string BuildUpdatedSourceDetailJson(CascoSourceRow sourceRow, LocalRelation relation)
    {
        JsonObject root;
        try
        {
            root = string.IsNullOrWhiteSpace(sourceRow.DetailJson)
                ? new JsonObject()
                : JsonNode.Parse(sourceRow.DetailJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        var badge = FirstFilled(relation.Badge, sourceRow.Badge);
        var driver = FirstFilled(relation.Driver, sourceRow.DriverName);
        var seller = relation.Vendor?.Trim() ?? string.Empty;
        var transport = FirstFilled(relation.TransportType, sourceRow.TransportType);
        var nationality = FirstFilled(relation.Nationality, sourceRow.Nationality);
        var adult = Math.Max(0, relation.AdultPassengers);
        var youth = Math.Max(0, relation.YouthPassengers);
        var minor = Math.Max(0, relation.ChildPassengers);
        var pax = ResolveRelationPassengers(sourceRow, relation);

        root["recordId"] = sourceRow.FolioOriginal;
        root["badgeId"] = badge;
        root["driverName"] = driver;
        root["sellerName"] = seller;
        root["nationality"] = nationality;
        root["serviceType"] = transport;
        root["tripCost"] = relation.Payout ?? 0m;
        root["paymentMethod"] = relation.PaymentMethod ?? string.Empty;
        root["notes"] = relation.Notes ?? string.Empty;
        root["adultCount"] = adult;
        root["youthCount"] = youth;
        root["minorCount"] = minor;
        root["passengerCount"] = pax;

        return root.ToJsonString();
    }

    private static async Task PersistSourcePosFolioAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string folioOriginal,
        string site,
        string posFolio,
        System.Threading.CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(posFolio))
            return;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE dbo.AppMovilRegistro
            SET folio_pos = @folioPos
            WHERE folio_app_original = @folioOriginal
              AND sitio = @sitio
              AND COALESCE(folio_pos, '') = '';
            """;
        command.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(folioOriginal, 60));
        command.Parameters.AddWithValue("@sitio", NormalizeForSqlLength(site, 300));
        command.Parameters.AddWithValue("@folioPos", NormalizeForSqlLength(posFolio, 100));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteDejadaInsertAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        LocalRelation relation,
        string user,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(GetDejadaInsertSql(), connection, transaction);
        AddDejadaParameters(command, sourceRow, relation, user);
        var inserted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (inserted != 1)
            throw new InvalidOperationException($"Se esperaba insertar una sola fila en dbo.dejadas y se insertaron {inserted}.");
    }

    private static async Task ExecuteDejadaUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoSourceRow sourceRow,
        LocalRelation relation,
        string user,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(GetDejadaUpdateSql(), connection, transaction);
        AddDejadaParameters(command, sourceRow, relation, user);
        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated != 1)
            throw new InvalidOperationException($"Se esperaba actualizar una sola fila en dbo.dejadas y se actualizaron {updated}.");
    }

    private static void AddDejadaParameters(SqlCommand command, CascoSourceRow sourceRow, LocalRelation relation, string user)
    {
        var paymentMethod = FirstFilled(relation.PaymentMethod, InferPaymentMethod(sourceRow));
        var isCard = string.Equals(paymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase);
        var isMixed = string.Equals(paymentMethod, "Mixto", StringComparison.OrdinalIgnoreCase);
        var sale = relation.Sale;
        var cash = isMixed ? sourceRow.Cash : isCard ? 0m : sale;
        var card = isMixed ? sourceRow.Card : isCard ? sale : 0m;
        var driverName = FirstFilled(relation.Driver, sourceRow.DriverName);
        var vendorName = relation.Vendor?.Trim() ?? string.Empty;
        var transportType = FirstFilled(relation.TransportType, sourceRow.TransportType);
        command.Parameters.AddWithValue("@idstaff", NormalizeForSqlLength(sourceRow.FolioOriginal, 50));
        command.Parameters.AddWithValue("@nombrestaff", NormalizeForSqlLength(driverName, 100));
        command.Parameters.AddWithValue("@sitio", NormalizeForSqlLength(sourceRow.Site, 100));
        command.Parameters.AddWithValue("@idalmacen", 0);
        command.Parameters.AddWithValue("@fechaDateTime", sourceRow.OperationDate);
        command.Parameters.AddWithValue("@fecha", sourceRow.OperationDate.Date);
        command.Parameters.AddWithValue("@horaTexto", sourceRow.OperationDate == DateTime.MinValue
            ? string.Empty
            : sourceRow.OperationDate.ToString("HH:mm", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@usuario", NormalizeForSqlLength(user, 100));
        command.Parameters.AddWithValue("@dejada", relation.Payout ?? 0m);
        command.Parameters.AddWithValue("@codigoRecepcion", NormalizeForSqlLength(sourceRow.FolioOriginal, 100));
        command.Parameters.AddWithValue("@folioRegistro", ParseBigIntNullable(sourceRow.FolioLocal) ?? 0L);
        command.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(sourceRow.FolioOriginal, 50));
        command.Parameters.AddWithValue("@unidad", NormalizeForSqlLength(sourceRow.Unit, 20));
        command.Parameters.AddWithValue("@pax", ResolveRelationPassengers(sourceRow, relation));
        command.Parameters.AddWithValue("@hotel", NormalizeForSqlLength(sourceRow.Hotel, 100));
        command.Parameters.AddWithValue("@taxistaNombre", NormalizeForSqlLength(driverName, 100));
        command.Parameters.AddWithValue("@vendedorNombre", NormalizeForSqlLength(vendorName, 100));
        command.Parameters.AddWithValue("@transporteTipo", NormalizeForSqlLength(transportType, 10));
        command.Parameters.AddWithValue("@telefono", NormalizeForSqlLength(sourceRow.Phone, 12));
        command.Parameters.AddWithValue("@venta", sale);
        command.Parameters.AddWithValue("@totalEfectivo", cash);
        command.Parameters.AddWithValue("@totalTarjeta", card);
        command.Parameters.AddWithValue("@gafete", NormalizeForSqlLength(sourceRow.Badge, 10));

    }

    private static async Task ValidateSavedRelationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string folioOriginal,
        System.Threading.CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(*) FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folioOriginal;";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (count != 1)
            throw new InvalidOperationException("La validacion final de dbo.RelacionTicketTaxista no confirmo una sola fila.");
    }

    private static async Task ValidateSavedDejadaAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string folioOriginal,
        string siteName,
        DateTime operationDate,
        string badge,
        System.Threading.CancellationToken cancellationToken)
    {
        var match = await GetDejadaMatchInfoAsync(connection, transaction, folioOriginal, siteName, operationDate, badge, cancellationToken);
        if (match.Count != 1)
            throw new InvalidOperationException("La validacion final de dbo.dejadas no confirmo una sola fila.");
    }

    private static void ValidateManualRelationInput(LocalRelation relation)
    {
        if (relation.Sale < 0m)
            throw new InvalidOperationException("La venta manual es obligatoria y no puede ser negativa.");

        if (!relation.Payout.HasValue || relation.Payout.Value < 0m)
            throw new InvalidOperationException("La dejada manual es obligatoria y no puede ser negativa.");
    }

    private static LocalRelation NormalizeCascoRelationForSave(LocalRelation relation, bool relationExists, bool dejadaExists)
    {
        var payout = relation.Payout ?? 0m;
        if (payout > 0m || relation.Sale <= 0m || relationExists || dejadaExists)
            return relation;

        return relation with { Payout = relation.Sale };
    }

    private static string InferPaymentMethod(CascoSourceRow row)
    {
        var hasCash = row.Cash > 0m;
        var hasCard = row.Card > 0m;
        var hasDollars = row.Dollars > 0m;
        if (hasDollars && (hasCash || hasCard))
            return "Mixto";
        if (hasDollars)
            return "Dolares";
        if (hasCash && hasCard)
            return "Mixto";
        if (hasCard)
            return "Tarjeta";
        if (hasCash)
            return "Efectivo";
        return string.Empty;
    }

    private static string InferCurrency(CascoSourceRow row) => row.Dollars > 0m ? "USD" : "MXN";

    private static string FirstFilled(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) ? first.Trim() : second.Trim();

    private static long ParseBigIntOrZero(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0L;

    private static long? ParseBigIntNullable(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static int ParseIntOrZero(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static string NormalizeSingleBadge(string? value)
    {
        var badges = SplitBadgeValues(value);
        return badges.Count > 0 ? badges[0] : string.Empty;
    }

    private static string NormalizeBadgeList(string? value) =>
        string.Join(", ", SplitBadgeValues(value));

    private static IReadOnlyList<string> SplitBadgeValues(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return Array.Empty<string>();

        return normalized
            .Split(new[] { ',', ';', '|', '/', '\\', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<CascoSourceRow> ExpandSourceRowsByBadge(IEnumerable<CascoSourceRow> rows, string? preferredBadgeList)
    {
        var preferredBadges = SplitBadgeValues(preferredBadgeList);
        foreach (var row in rows)
        {
            var rowBadges = SplitBadgeValues(row.Badge);
            var badges = preferredBadges.Count > 0 ? preferredBadges : rowBadges;
            if (badges.Count == 0)
            {
                yield return row;
                continue;
            }

            foreach (var badge in badges)
                yield return row with { Badge = badge };
        }
    }

    private static string BuildInList(int count, string prefix) =>
        string.Join(", ", Enumerable.Range(0, count).Select(index => $"{prefix}{index.ToString(CultureInfo.InvariantCulture)}"));

    private static void AddBadgeListParameters(SqlCommand command, IReadOnlyList<string> badges, string prefix)
    {
        for (var index = 0; index < badges.Count; index++)
            command.Parameters.AddWithValue(
                $"{prefix}{index.ToString(CultureInfo.InvariantCulture)}",
                NormalizeForSqlLength(badges[index].ToUpperInvariant(), 50));
    }

    private static void AddNumberListParameters(SqlCommand command, IReadOnlyList<int> values, string prefix)
    {
        for (var index = 0; index < values.Count; index++)
            command.Parameters.AddWithValue($"{prefix}{index.ToString(CultureInfo.InvariantCulture)}", values[index]);
    }

    private static CascoSourceRow BuildAggregateSourceRow(IReadOnlyList<CascoSourceRow> rows, string? preferredBadgeList)
    {
        var first = rows[0];
        var badgeList = NormalizeBadgeList(preferredBadgeList);
        if (string.IsNullOrWhiteSpace(badgeList))
            badgeList = NormalizeBadgeList(string.Join(", ", rows.Select(row => row.Badge)));

        return first with { Badge = badgeList };
    }

    private static LocalRelation MergeRelationRowsForDisplay(IReadOnlyList<LocalRelation> rows)
    {
        var first = rows[0];
        var badgeList = NormalizeBadgeList(string.Join(", ", rows.Select(row => row.Badge)));
        var totalPayout = rows.Select(row => row.Payout ?? 0m).DefaultIfEmpty(0m).Max();
        return first with
        {
            Badge = string.IsNullOrWhiteSpace(badgeList) ? first.Badge : badgeList,
            Payout = totalPayout,
            PayoutStatus = rows.All(row => IsPaidStatus(row.PayoutStatus)) ? first.PayoutStatus : "pendiente",
            PayoutTicket = FirstNonEmpty(rows.Select(row => row.PayoutTicket)),
            PayoutUser = FirstNonEmpty(rows.Select(row => row.PayoutUser)),
            PayoutDate = FirstNonEmpty(rows.Select(row => row.PayoutDate))
        };
    }

    private static string FirstNonEmpty(IEnumerable<string> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static int ResolveRelationPassengers(CascoSourceRow sourceRow, LocalRelation relation) =>
        relation.Passengers > 0 ? relation.Passengers : sourceRow.Passengers;

    private static async Task<List<LocalRelation>> LoadLockedTargetRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string siteName,
        string folioOriginal,
        string? selectedBadge,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
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
              COALESCE(NULLIF(estado_pago_dejada, ''), NULLIF(payout_status, ''), 'pendiente') AS PaymentStatus,
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
              COALESCE(usuario_pago_dejada, payout_user, '') AS PayoutUser,
              COALESCE(CONVERT(nvarchar(30), fecha_pago_dejada, 120), CONVERT(nvarchar(30), payout_date, 120), '') AS PayoutDate,
              COALESCE(ticket_pago_dejada, payout_ticket, '') AS PayoutTicket,
              COALESCE(efectivo, 0) AS Cash,
              COALESCE(tarjeta, 0) AS Card,
              COALESCE(pax, 0) AS Passengers
            FROM dbo.AppMovilRegistro WITH (UPDLOCK, ROWLOCK)
            WHERE folio_app_original = @folioOriginal
              AND sitio = @sitio
              AND (
                    NULLIF(@gafeteList, '') IS NULL
                 OR CHARINDEX(',' + REPLACE(COALESCE(folio_gafete, ''), ' ', '') + ',', ',' + REPLACE(@gafeteList, ' ', '') + ',') > 0
              )
            ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        command.Parameters.AddWithValue("@sitio", siteName);
        command.Parameters.AddWithValue("@gafeteList", NormalizeBadgeList(selectedBadge));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<LocalRelation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(MapRelationRow(new CascoAppRecordDetail(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                reader.IsDBNull(8) ? 0m : Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
                reader.IsDBNull(21) ? string.Empty : reader.GetString(21),
                reader.IsDBNull(22) ? string.Empty : reader.GetString(22),
                reader.IsDBNull(23) ? 0m : Convert.ToDecimal(reader.GetValue(23), CultureInfo.InvariantCulture),
                reader.IsDBNull(24) ? 0m : Convert.ToDecimal(reader.GetValue(24), CultureInfo.InvariantCulture),
                reader.IsDBNull(25) ? 0 : Convert.ToInt32(reader.GetValue(25), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    private static async Task ValidatePendingRowsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string siteName,
        string folioOriginal,
        string? selectedBadge,
        int expectedCount,
        System.Threading.CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(GetPendingValidationSql(), connection, transaction);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        command.Parameters.AddWithValue("@sitio", siteName);
        command.Parameters.AddWithValue("@gafeteList", NormalizeBadgeList(selectedBadge));
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result != expectedCount)
            throw new InvalidOperationException(result == 0
                ? "La dejada ya estaba pagada o el registro no esta disponible para pago."
                : $"La validacion encontro {result} registros pendientes, pero se esperaban {expectedCount}.");
    }

    private static async Task ExecutePaymentUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string siteName,
        string folioOriginal,
        string? selectedBadge,
        int expectedCount,
        string user,
        string ticket,
        System.Threading.CancellationToken cancellationToken)
    {
        // 1) Update AppMovilRegistro payment fields
        await using var command = new SqlCommand(GetPaymentUpdateSql(), connection, transaction);
        var paidAt = DateTime.Now;
        command.Parameters.AddWithValue("@paidStatus", NormalizeForSqlLength("pagado", 20));
        command.Parameters.AddWithValue("@paidAt", paidAt);
        var normalizedUser = NormalizePayoutUser(user);
        command.Parameters.AddWithValue("@paidBy", NormalizeForSqlLength(normalizedUser, 100));
        command.Parameters.AddWithValue("@ticket", NormalizeForSqlLength(ticket, 20));
        command.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(folioOriginal, 120));
        command.Parameters.AddWithValue("@sitio", NormalizeForSqlLength(siteName, 300));
        command.Parameters.AddWithValue("@gafeteList", NormalizeBadgeList(selectedBadge));
        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated != expectedCount)
            throw new InvalidOperationException($"Se esperaba actualizar {expectedCount} fila(s) en AppMovilRegistro y se actualizaron {updated}.");

        // 2) Determine if a dejadas row exists for the natural key
        await using var checkCmd = new SqlCommand(
            """
            SELECT folio_app, folio_pos, folio_gafete, vendedor_nombre, unidad, hotel, total, efectivo, tarjeta, COALESCE(fecha_operacion, fecha_creacion)
            FROM dbo.AppMovilRegistro WITH (UPDLOCK, ROWLOCK)
            WHERE folio_app_original = @folioOriginal
              AND sitio = @sitio
              AND (
                    NULLIF(@gafeteList, '') IS NULL
                 OR CHARINDEX(',' + REPLACE(COALESCE(folio_gafete, ''), ' ', '') + ',', ',' + REPLACE(@gafeteList, ' ', '') + ',') > 0
              )
            ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;
            """,
            connection,
            transaction);
        checkCmd.Parameters.AddWithValue("@folioOriginal", folioOriginal);
        checkCmd.Parameters.AddWithValue("@sitio", siteName);
        checkCmd.Parameters.AddWithValue("@gafeteList", NormalizeBadgeList(selectedBadge));

        var paymentRows = new List<CascoPaymentSourceRow>();
        await using (var reader = await checkCmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                paymentRows.Add(new CascoPaymentSourceRow(
                    reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                    reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                    reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
                    reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty,
                    reader.IsDBNull(4) ? string.Empty : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture) ?? string.Empty,
                    reader.IsDBNull(5) ? string.Empty : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty,
                    reader.IsDBNull(6) ? 0m : Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                    reader.IsDBNull(7) ? 0m : Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
                    reader.IsDBNull(8) ? 0m : Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
                    reader.IsDBNull(9) ? DateTime.Now : Convert.ToDateTime(reader.GetValue(9), CultureInfo.InvariantCulture)));
            }
        }

        if (paymentRows.Count != expectedCount)
            throw new InvalidOperationException($"No se pudieron recuperar las {expectedCount} fila(s) actualizadas de AppMovilRegistro.");

        foreach (var paymentRow in paymentRows)
        {
            var paymentBadges = SplitBadgeValues(paymentRow.Gafete);
            if (paymentBadges.Count == 0)
                paymentBadges = new[] { paymentRow.Gafete };

            foreach (var paymentBadge in paymentBadges)
            {
            await using var existCmd = new SqlCommand(
                "SELECT COUNT(*) FROM dbo.dejadas WHERE folioregistrostr = @folioOriginal AND nombrealmacen = @sitio AND COALESCE(gafete, '') = @gafete AND CAST(fecha AS date) = CAST(@operationDate AS date);",
                connection,
                transaction);
            existCmd.Parameters.AddWithValue("@folioOriginal", folioOriginal);
            existCmd.Parameters.AddWithValue("@sitio", siteName);
            existCmd.Parameters.AddWithValue("@gafete", paymentBadge);
            existCmd.Parameters.AddWithValue("@operationDate", paymentRow.OperationDate.Date);
            var dejadasCount = Convert.ToInt32(await existCmd.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            if (dejadasCount == 1)
            {
                await using var dejadaUpdate = new SqlCommand(
                    "UPDATE dbo.dejadas SET pago = total, fechapago = @paidAt WHERE folioregistrostr = @folioOriginal AND nombrealmacen = @sitio AND COALESCE(gafete, '') = @gafete AND CAST(fecha AS date) = CAST(@operationDate AS date);",
                    connection,
                    transaction);
                dejadaUpdate.Parameters.AddWithValue("@paidAt", paidAt);
                dejadaUpdate.Parameters.AddWithValue("@folioOriginal", folioOriginal);
                dejadaUpdate.Parameters.AddWithValue("@sitio", siteName);
                dejadaUpdate.Parameters.AddWithValue("@gafete", paymentBadge);
                dejadaUpdate.Parameters.AddWithValue("@operationDate", paymentRow.OperationDate.Date);
                var updatedDejada = await dejadaUpdate.ExecuteNonQueryAsync(cancellationToken);
                if (updatedDejada != 1)
                    throw new InvalidOperationException($"Se esperaba actualizar una sola fila en dbo.dejadas para el gafete {paymentBadge} y se actualizaron {updatedDejada}.");
            }
            else if (dejadasCount == 0)
            {
                await using var insertCmd = new SqlCommand(GetDejadaInsertSql(), connection, transaction);
                insertCmd.Parameters.AddWithValue("@idstaff", NormalizeForSqlLength(folioOriginal, 50));
                insertCmd.Parameters.AddWithValue("@nombrestaff", NormalizeForSqlLength(paymentRow.VendedorNombre, 100));
                insertCmd.Parameters.AddWithValue("@sitio", NormalizeForSqlLength(siteName, 100));
                insertCmd.Parameters.AddWithValue("@idalmacen", 0);
                insertCmd.Parameters.AddWithValue("@fechaDateTime", paidAt);
                insertCmd.Parameters.AddWithValue("@fecha", paidAt.Date);
                insertCmd.Parameters.AddWithValue("@horaTexto", paidAt.ToString("HH:mm", CultureInfo.InvariantCulture));
                insertCmd.Parameters.AddWithValue("@usuario", NormalizeForSqlLength(normalizedUser, 100));
                insertCmd.Parameters.AddWithValue("@dejada", paymentRow.Total);
                insertCmd.Parameters.AddWithValue("@codigoRecepcion", NormalizeForSqlLength(folioOriginal, 100));
                insertCmd.Parameters.AddWithValue("@folioRegistro", ParseBigIntNullable(paymentRow.FolioApp) ?? 0L);
                insertCmd.Parameters.AddWithValue("@folioOriginal", NormalizeForSqlLength(folioOriginal, 50));
                insertCmd.Parameters.AddWithValue("@unidad", NormalizeForSqlLength(paymentRow.Unidad, 20));
                insertCmd.Parameters.AddWithValue("@pax", 0);
                insertCmd.Parameters.AddWithValue("@hotel", NormalizeForSqlLength(paymentRow.Hotel, 100));
                insertCmd.Parameters.AddWithValue("@taxistaNombre", NormalizeForSqlLength(paymentRow.VendedorNombre, 100));
                insertCmd.Parameters.AddWithValue("@transporteTipo", NormalizeForSqlLength(string.Empty, 10));
                insertCmd.Parameters.AddWithValue("@telefono", string.Empty);
                insertCmd.Parameters.AddWithValue("@venta", paymentRow.Total);
                insertCmd.Parameters.AddWithValue("@totalEfectivo", paymentRow.Efectivo);
                insertCmd.Parameters.AddWithValue("@totalTarjeta", paymentRow.Tarjeta);
                insertCmd.Parameters.AddWithValue("@gafete", NormalizeForSqlLength(paymentBadge, 10));

                var inserted = await insertCmd.ExecuteNonQueryAsync(cancellationToken);
                if (inserted != 1)
                    throw new InvalidOperationException($"Se esperaba insertar una sola fila en dbo.dejadas para el gafete {paymentBadge} y se insertaron {inserted}.");
            }
            else
            {
                throw new InvalidOperationException($"Se encontraron multiples filas en dbo.dejadas para el folio {folioOriginal} y gafete {paymentBadge}.");
            }

            await using var finalCheck = new SqlCommand("SELECT COUNT(*) FROM dbo.dejadas WHERE folioregistrostr = @folioOriginal AND nombrealmacen = @sitio AND COALESCE(gafete, '') = @gafete AND CAST(fecha AS date) = CAST(@operationDate AS date);", connection, transaction);
            finalCheck.Parameters.AddWithValue("@folioOriginal", folioOriginal);
            finalCheck.Parameters.AddWithValue("@sitio", siteName);
            finalCheck.Parameters.AddWithValue("@gafete", paymentBadge);
            finalCheck.Parameters.AddWithValue("@operationDate", paymentRow.OperationDate.Date);
            var finalCount = Convert.ToInt32(await finalCheck.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (finalCount != 1)
                throw new InvalidOperationException($"La validacion final no confirmo exactamente una fila en dbo.dejadas para el gafete {paymentBadge}.");
            }
        }
    }

    private static LocalAppRecordRow ToLocalAppRecordRow(CascoAppRecordDetail row) =>
        new(
            row.FolioControl,
            row.OriginalFolio,
            row.DriverName,
            row.Hotel,
            row.Badge,
            row.OperationDate,
            row.TransportType,
            row.Total,
            NormalizePendingStatus(row.PaymentStatus, row.PayoutDate),
            row.User,
            row.Notes);

    private static IReadOnlyList<LocalAppRecordRow> ApplySearchFilter(
        IReadOnlyList<LocalAppRecordRow> rows,
        string siteName,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return rows;

        var token = search.Trim();
        return rows
            .Where(row =>
                Contains(row.FolioControl, token)
                || Contains(row.OriginalFolio, token)
                || Contains(row.DriverName, token)
                || Contains(row.Hotel, token)
                || Contains(row.Badge, token)
                || Contains(row.TransportType, token)
                || Contains(row.OperationDate, token)
                || Contains(siteName, token))
            .ToArray();
    }

    private static IReadOnlyList<CascoAppRecordDetail> ApplyDetailedSearchFilter(
        IReadOnlyList<CascoAppRecordDetail> rows,
        string siteName,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return rows;

        var token = search.Trim();
        return rows.Where(row =>
                Contains(row.FolioControl, token)
                || Contains(row.OriginalFolio, token)
                || Contains(row.PosFolio, token)
                || Contains(row.DriverName, token)
                || Contains(row.Hotel, token)
                || Contains(row.Badge, token)
                || Contains(row.TransportType, token)
                || Contains(row.Unit, token)
                || Contains(row.Plates, token)
                || Contains(row.Origin, token)
                || Contains(row.Destination, token)
                || Contains(row.OperationDate, token)
                || Contains(row.Notes, token)
                || Contains(siteName, token))
            .ToArray();
    }

    private static IReadOnlyList<AppRecordGridRow> MapAppGridRows(
        IReadOnlyList<CascoAppRecordDetail> rows,
        string siteName) =>
        rows
            .Select(row => new AppRecordGridRow
            {
                FolioOriginal = row.OriginalFolio,
                FolioLocal = row.FolioControl,
                Taxista = row.DriverName,
                Fecha = NormalizeDateText(row.OperationDate),
                Hotel = row.Hotel,
                Origen = row.Origin,
                Destino = row.Destination,
                Nacionalidad = row.Nationality,
                Unidad = row.Unit,
                Sitio = siteName,
                Total = row.Total,
                Efectivo = row.Cash,
                Tarjeta = row.Card,
                PayoutStatus = NormalizePendingStatus(row.PaymentStatus, row.PayoutDate)
            })
            .OrderBy(row => row.FolioOriginal, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.FolioLocal, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<LocalRegistroDiarioRow> MapRegistroRows(
        IReadOnlyList<CascoAppRecordDetail> rows,
        string siteName) =>
        rows
            .Select(row =>
            {
                var operationDate = ParseOperationDate(row.OperationDate);
                return new LocalRegistroDiarioRow(
                    string.IsNullOrWhiteSpace(row.OriginalFolio) ? row.FolioControl : row.OriginalFolio,
                    row.FolioControl,
                    "APP MOVIL",
                    row.User,
                    string.Empty,
                    row.Badge,
                    string.IsNullOrWhiteSpace(row.PosFolio) ? 0 : 1,
                    row.Hotel,
                    siteName,
                    operationDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty,
                    operationDate?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.Passengers,
                    row.DriverName,
                    row.Nationality,
                    row.TransportType,
                    row.Origin,
                    siteName,
                    row.Destination,
                    row.Unit,
                    row.Plates,
                    row.Phone,
                    row.Notes,
                    row.Total,
                    row.Cash,
                    row.Card);
            })
            .OrderBy(row => row.FolioOperacion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.FolioControl, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static LocalRelation MapRelationRow(CascoAppRecordDetail row)
    {
        var payment = NormalizeCascoPayment(row);
        var payoutStatus = NormalizePendingStatus(row.PaymentStatus, row.PayoutDate);

        return new LocalRelation(
            0,
            row.FolioControl,
            string.IsNullOrWhiteSpace(row.OriginalFolio) ? row.FolioControl : row.OriginalFolio,
            row.PosFolio,
            row.Badge,
            row.DriverName,
            row.DriverName,
            row.Total,
            row.Notes,
            "APP MOVIL",
            row.User,
            NormalizeDateText(row.OperationDate),
            row.Hotel,
            row.Origin,
            row.Site,
            row.Destination,
            row.Unit,
            row.Plates,
            row.Phone,
            row.Nationality,
            row.TransportType,
            row.Total,
            row.CommissionCalculated,
            row.CommissionPaid,
            payment.FormaPago,
            payoutStatus,
            ResolveCommissionStatus(row.CommissionCalculated, row.CommissionPaid),
            row.PayoutTicket,
            row.TaxistaId,
            row.PayoutUser,
            row.PayoutDate,
            string.IsNullOrWhiteSpace(row.PayoutDate) ? 0m : row.Total,
            row.Passengers,
            string.Empty,
            payment.Moneda,
            payment.PaymentMethodRemoto,
            row.PaymentsJson,
            row.Total,
            row.Cash,
            row.Card,
            row.Dollars,
            row.ExchangeRate);
    }

    private static bool Matches(string value, string? search) =>
        string.IsNullOrWhiteSpace(search)
        || value.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);

    private static CascoPaymentNormalization NormalizeCascoPayment(CascoAppRecordDetail row)
    {
        var remoteMethod = ExtractRemotePaymentMethod(row.DetailJson);
        var normalizedRemote = NormalizeRemotePaymentMethod(remoteMethod);
        if (!string.IsNullOrWhiteSpace(normalizedRemote))
        {
            return new CascoPaymentNormalization(
                normalizedRemote,
                row.Dollars > 0m ? "USD" : "MXN",
                remoteMethod,
                "detalle_json.paymentMethod",
                row.Dollars > 0m ? "dolares > 0" : "dolares = 0");
        }

        var hasCash = row.Cash > 0m;
        var hasCard = row.Card > 0m;
        var hasDollars = row.Dollars > 0m;
        var form = hasDollars
            ? hasCash || hasCard ? "Mixto" : "Dolares"
            : hasCash && hasCard ? "Mixto"
            : hasCard ? "Tarjeta"
            : hasCash ? "Efectivo"
            : string.Empty;
        var currency = hasDollars ? "USD" : "MXN";
        var source = hasDollars ? "dbo.AppMovilRegistro.dolares/efectivo/tarjeta" : "dbo.AppMovilRegistro.efectivo/tarjeta";
        var currencySource = hasDollars ? "dbo.AppMovilRegistro.dolares" : "dbo.AppMovilRegistro.moneda local";
        return new CascoPaymentNormalization(form, currency, remoteMethod, source, currencySource);
    }

    private static string ExtractRemotePaymentMethod(string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(detailJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("PaymentMethod", out var paymentMethod))
                return paymentMethod.ToString();
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    private static string NormalizeRemotePaymentMethod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();
        if (text.Contains("efect", StringComparison.OrdinalIgnoreCase))
            return "Efectivo";
        if (text.Contains("tarj", StringComparison.OrdinalIgnoreCase) || text.Contains("card", StringComparison.OrdinalIgnoreCase))
            return "Tarjeta";
        if (text.Contains("dolar", StringComparison.OrdinalIgnoreCase) || text.Contains("usd", StringComparison.OrdinalIgnoreCase))
            return "Dolares";
        if (text.Contains("mixt", StringComparison.OrdinalIgnoreCase))
            return "Mixto";
        return text;
    }

    private static string ResolveCommissionStatus(decimal amount, decimal paid) =>
        amount <= 0m ? "SIN COMISION" : paid >= amount ? "PAGADA" : paid > 0m ? "PARCIAL" : "PENDIENTE";

    private static LocalOperationsPreviewRow MapOperationsPreviewRow(LocalRelation row) =>
        new(
            string.IsNullOrWhiteSpace(row.Driver) ? row.Vendor : row.Driver,
            row.DateText,
            row.Passengers,
            row.Hotel,
            row.Payout ?? 0m,
            row.Payout ?? 0m,
            row.Commission,
            row.CommissionPaid,
            row.OperationFolio,
            row.DisplayLocalFolio,
            row.Badge,
            row.PayoutStatus,
            row.PayoutDate,
            row.PayoutUser,
            row.PayoutTicket,
            row.Site,
            row.Origin,
            row.Destination,
            row.Unit,
            row.Plates,
            row.TransportType,
            row.Notes);

    private static string BuildTicketText(IReadOnlyList<LocalTicketLine> lines, string user)
    {
        static string Value(IReadOnlyList<LocalTicketLine> rows, string campo) => rows.FirstOrDefault(x => x.Campo == campo)?.Valor ?? string.Empty;
        const int width = 42;
        static string Line(char value = '-') => new(value, width);
        static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        static string Center(string value)
        {
            value = Clean(value);
            if (value.Length >= width)
                return value[..width];

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
            Pair("Sucursal", Value(lines, "Sucursal")),
            Pair("Dejada", Value(lines, "Dejada")),
            Pair("Usuario", string.IsNullOrWhiteSpace(Value(lines, "Usuario")) ? user : Value(lines, "Usuario")),
            Pair("Estatus", Value(lines, "Estatus")),
            Line(),
            Center("CONSERVE ESTE COMPROBANTE"),
            "________________________",
            Center("FIRMA DEL TAXISTA")
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static LocalRelation EmptyRelation(string folioOriginal, string siteName) =>
        new(
            0,
            string.Empty,
            folioOriginal,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0m,
            string.Empty,
            "APP MOVIL",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            siteName,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0m,
            0m,
            0m,
            string.Empty,
            "pendiente",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0m,
            0,
            string.Empty);

    private static BranchConfiguration? ResolveCascoBranch(BranchConfiguration? currentBranch, string branchCode)
    {
        var resolvedBranch = currentBranch ?? new BranchConfigurationService().GetBranch(branchCode);
        return string.Equals(resolvedBranch.Code, "CV", StringComparison.OrdinalIgnoreCase) ? resolvedBranch : null;
    }

    private static BranchConfiguration ResolveRequiredCascoBranch(BranchConfiguration? currentBranch, string branchCode) =>
        ResolveCascoBranch(currentBranch, branchCode)
        ?? throw new InvalidOperationException("La operacion solicitada solo esta disponible para Casco Viejo.");

    private static string RequireValue(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{fieldName} es obligatorio.") : value.Trim();

    private static string BuildPayoutTicket(string folioOriginal)
    {
        var normalizedFolio = string.IsNullOrWhiteSpace(folioOriginal) ? "0000" : folioOriginal.Trim();
        var safeFolio = NormalizeForSqlLength(normalizedFolio, 4);
        var compactTicket = "TK" + safeFolio + DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture);
        return NormalizeForSqlLength(compactTicket, 16);
    }

    private static string NormalizePayoutUser(string? user)
    {
        var normalized = string.IsNullOrWhiteSpace(user) ? "desktop" : user.Trim();
        return normalized.Length <= 50 ? normalized : normalized[..50];
    }

    private static string NormalizeForSqlLength(string? value, int maxLength)
    {
        if (maxLength <= 0)
            return string.Empty;

        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static bool IsPaidStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status)
        && status.Contains("PAGAD", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePendingStatus(string? status, string? payoutDate)
    {
        if (IsPaidStatus(status) || !string.IsNullOrWhiteSpace(payoutDate))
            return "pagado";

        return "pendiente";
    }

    private static DateTime? ParseOperationDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var formats = new[]
        {
            "M/d/yyyy h:mm:ss tt",
            "M/d/yyyy h:mm tt",
            "M/d/yyyy HH:mm:ss",
            "M/d/yyyy HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fffffff",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFF"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date))
                return date;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private static string NormalizeDateText(string? value)
    {
        var date = ParseOperationDate(value);
        return date?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? value ?? string.Empty;
    }

    private static bool Contains(string? value, string token) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static string ExtractRemoteText(string? detailJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(detailJson))
            return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(detailJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return string.Empty;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    return property.Value.ToString().Trim();
            }

            return string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static int ExtractRemoteInt(string? detailJson, string propertyName)
    {
        var value = ExtractRemoteText(detailJson, propertyName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;
    }

    private static CascoBadgeControlState? FindBadgeControlState(
        IReadOnlyList<CascoBadgeControlState> states,
        string badge,
        string? operationFolio,
        string? localFolio)
    {
        var normalizedBadge = RequireValue(badge, "Gafete");
        return states.FirstOrDefault(state =>
            string.Equals(state.Badge, normalizedBadge, StringComparison.OrdinalIgnoreCase)
            && (
                MatchNumericFolio(state.OperationFolio, operationFolio)
                || MatchNumericFolio(state.OperationFolio, localFolio)
            ))
            ?? states.FirstOrDefault(state =>
                string.Equals(state.Badge, normalizedBadge, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchNumericFolio(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        var normalizedLeft = left.Trim();
        var normalizedRight = right.Trim();
        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase))
            return true;

        return long.TryParse(normalizedLeft, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftNumber)
            && long.TryParse(normalizedRight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightNumber)
            && leftNumber == rightNumber;
    }

    private sealed record CascoPaymentNormalization(
        string FormaPago,
        string Moneda,
        string PaymentMethodRemoto,
        string Fuente,
        string FuenteMoneda);

    private sealed record GeneratedCommissionRow(decimal Amount, decimal Paid, string Status);

    private sealed record CascoSourceRow(
        string FolioLocal,
        string FolioOriginal,
        string PosFolio,
        string DriverName,
        string Badge,
        DateTime OperationDate,
        string Hotel,
        string Origin,
        string Destination,
        string Unit,
        string Plates,
        string Phone,
        string Nationality,
        string TransportType,
        string TaxistaId,
        string Site,
        string Notes,
        decimal Cash,
        decimal Card,
        decimal Dollars,
        decimal ExchangeRate,
        string DetailJson,
        int Passengers = 0);

    private sealed record CascoPaymentSourceRow(
        string FolioApp,
        string FolioPos,
        string Gafete,
        string VendedorNombre,
        string Unidad,
        string Hotel,
        decimal Total,
        decimal Efectivo,
        decimal Tarjeta,
        DateTime OperationDate);

    private sealed record CascoDejadaMatchInfo(int Count, bool Exists);
}




