using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed class CascoBackgroundSyncService
{
    private static readonly Lazy<CascoBackgroundSyncService> LazyInstance = new(() => new CascoBackgroundSyncService());
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _stateLock = new();
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _missingPasswordLogged;
    private CascoAutoSyncStatusSnapshot _status = CascoAutoSyncStatusSnapshot.CreateDefault();

    public static CascoBackgroundSyncService Instance => LazyInstance.Value;

    public event EventHandler<CascoRecordsChangedEventArgs>? RecordsChanged;

    public CascoAutoSyncSettings LoadSettings() => CascoAutoSyncSettings.LoadFromWorkspace();

    public CascoAutoSyncStatusSnapshot GetStatusSnapshot()
    {
        lock (_stateLock)
            return _status;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_loopTask is not null)
            return;

        var settings = LoadSettings();
        if (!settings.PasswordAvailable)
        {
            if (!_missingPasswordLogged)
            {
                _missingPasswordLogged = true;
                UpdateStatus(status => status with
                {
                    Configured = true,
                    PasswordAvailable = false,
                    IntervalSeconds = (int)settings.Interval.TotalSeconds,
                    LastError = "Sincronizador Casco desactivado: falta CASCO_SQL_PASSWORD"
                });
                WriteLog(settings, "Sincronizador Casco desactivado: falta CASCO_SQL_PASSWORD");
            }
            return;
        }

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timer = new PeriodicTimer(settings.Interval);
        UpdateStatus(status => status with
        {
            Configured = true,
            PasswordAvailable = true,
            IntervalSeconds = (int)settings.Interval.TotalSeconds
        });
        _loopTask = RunLoopAsync(settings, _loopCts.Token);
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _loopCts?.Cancel();
        _timer?.Dispose();
        if (_loopTask is not null)
            await _loopTask;
        _loopTask = null;
        _timer = null;
        _loopCts?.Dispose();
        _loopCts = null;
    }

    public static CascoAutoSyncStatusSnapshot ReadStatusFromDisk()
    {
        var settings = CascoAutoSyncSettings.LoadFromWorkspace();
        if (!File.Exists(settings.StatusFilePath))
            return CascoAutoSyncStatusSnapshot.CreateDefault() with
            {
                Configured = true,
                PasswordAvailable = settings.PasswordAvailable,
                IntervalSeconds = (int)settings.Interval.TotalSeconds
            };

        try
        {
            using var stream = new FileStream(settings.StatusFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return JsonSerializer.Deserialize<CascoAutoSyncStatusSnapshot>(reader.ReadToEnd(), SerializerOptions)
                ?? CascoAutoSyncStatusSnapshot.CreateDefault();
        }
        catch
        {
            return CascoAutoSyncStatusSnapshot.CreateDefault();
        }
    }

    public async Task<CascoAutoSyncCycleResult> RunCycleOnceAsync(CancellationToken cancellationToken = default, bool simulateHttpError = false, bool simulateSqlError = false)
    {
        var settings = LoadSettings();
        if (!settings.PasswordAvailable)
        {
            UpdateStatus(status => status with { LastError = "Sincronizador Casco desactivado: falta CASCO_SQL_PASSWORD" });
            return CascoAutoSyncCycleResult.Disabled("Sincronizador Casco desactivado: falta CASCO_SQL_PASSWORD");
        }

        if (!await _syncLock.WaitAsync(0, cancellationToken))
        {
            UpdateStatus(status => status with { LockActive = true });
            return CascoAutoSyncCycleResult.Skipped("Lock activo");
        }

        try
        {
            UpdateStatus(status => status with { LockActive = true });
            return await ExecuteCycleCoreAsync(settings, cancellationToken, simulateHttpError, simulateSqlError);
        }
        finally
        {
            _syncLock.Release();
            UpdateStatus(status => status with { LockActive = false });
        }
    }

    private async Task RunLoopAsync(CascoAutoSyncSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            while (_timer is not null && await _timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!await _syncLock.WaitAsync(0, cancellationToken))
                    continue;

                try
                {
                    UpdateStatus(status => status with { LockActive = true });
                    await ExecuteCycleCoreAsync(settings, cancellationToken, simulateHttpError: false, simulateSqlError: false);
                }
                catch (Exception ex)
                {
                    UpdateStatus(status => status with { LastError = ex.Message });
                    WriteLog(settings, $"Error en ciclo Casco: {ex.Message}");
                }
                finally
                {
                    _syncLock.Release();
                    UpdateStatus(status => status with { LockActive = false });
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<CascoAutoSyncCycleResult> ExecuteCycleCoreAsync(
        CascoAutoSyncSettings settings,
        CancellationToken cancellationToken,
        bool simulateHttpError,
        bool simulateSqlError)
    {
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        WriteLog(settings, $"[{startedAt:HH:mm:ss}] Casco sync iniciado");
        try
        {
            if (simulateHttpError)
                throw new HttpRequestException("Error HTTP simulado");

            using var httpClient = new HttpClient { Timeout = settings.HttpTimeout };
            var apiResult = await FetchRecordsAsync(httpClient, settings, cancellationToken);
            var cvRecords = apiResult.Records
                .Where(x => string.Equals(x.AssignedBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.Site, "Casco Viejo", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.RecordDate ?? DateTimeOffset.MinValue)
                .ThenBy(x => x.RecordId, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (simulateSqlError)
                throw new InvalidOperationException("Error SQL simulado");

            var result = await InsertNewRecordsAsync(settings, cvRecords, cancellationToken);
            stopwatch.Stop();
            var cycle = result with
            {
                StartedAt = startedAt,
                DurationMs = stopwatch.ElapsedMilliseconds,
                Url = settings.BuildApiUrl(),
                HttpStatus = apiResult.HttpStatusCode,
                ReceivedCount = apiResult.Records.Count,
                CvCount = cvRecords.Length
            };
            UpdateStatus(status => status with
            {
                Configured = true,
                PasswordAvailable = true,
                IntervalSeconds = (int)settings.Interval.TotalSeconds,
                LastCycleAt = startedAt,
                LastHttp = cycle.HttpStatus,
                LastReceivedCount = cycle.ReceivedCount,
                LastInsertedCount = cycle.InsertedCount,
                LastOmittedCount = cycle.OmittedCount,
                LastError = string.Empty,
                LastDurationMs = cycle.DurationMs
            });

            WriteLog(
                settings,
                $"HTTP: {cycle.HttpStatus}",
                $"CV recibidos: {cycle.CvCount}",
                $"Nuevos: {cycle.NewDetectedCount}",
                $"Insertados: {cycle.InsertedCount}",
                $"Omitidos: {cycle.OmittedCount}",
                $"Errores: {cycle.ErrorCount}",
                $"Duracion: {cycle.DurationMs} ms");

            if (cycle.InsertedCount > 0)
                RecordsChanged?.Invoke(this, new CascoRecordsChangedEventArgs(cycle.InsertedCount, cycle.InsertedFolios));

            return cycle;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdateStatus(status => status with
            {
                Configured = true,
                PasswordAvailable = true,
                IntervalSeconds = (int)settings.Interval.TotalSeconds,
                LastCycleAt = startedAt,
                LastError = ex.Message,
                LastDurationMs = stopwatch.ElapsedMilliseconds
            });
            WriteLog(settings, $"Error: {ex.Message}", $"Duracion: {stopwatch.ElapsedMilliseconds} ms");
            return CascoAutoSyncCycleResult.Failed(ex.Message, startedAt, stopwatch.ElapsedMilliseconds);
        }
    }

    private static async Task<CascoApiCallResult> FetchRecordsAsync(HttpClient httpClient, CascoAutoSyncSettings settings, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.BuildApiUrl());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString();
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var records = TryDeserializeRecords(rawContent);
        return new CascoApiCallResult(settings.BuildApiUrl(), (int)response.StatusCode, contentType, records);
    }

    private static IReadOnlyList<CascoTripRecord> TryDeserializeRecords(string rawContent)
    {
        try
        {
            using var document = JsonDocument.Parse(rawContent);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<CascoTripRecord>();

            var records = new List<CascoTripRecord>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                records.Add(new CascoTripRecord(
                    ReadText(element, "recordId"),
                    ReadText(element, "catalogId"),
                    ReadText(element, "badgeId"),
                    ReadText(element, "driverName"),
                    ReadText(element, "sellerName"),
                    ReadText(element, "driverPhone"),
                    ReadText(element, "contactPhone"),
                    ReadText(element, "nationality"),
                    ReadText(element, "plate"),
                    ReadText(element, "vehicleModel"),
                    ReadText(element, "unitNumber"),
                    ReadText(element, "hotel"),
                    ReadText(element, "origin"),
                    ReadText(element, "site"),
                    ReadText(element, "destination"),
                    ReadInt(element, "passengerCount"),
                    ReadText(element, "serviceType"),
                    ReadDecimal(element, "tripCost"),
                    ReadText(element, "notes"),
                    ReadDateTimeOffset(element, "recordDate"),
                    ReadText(element, "paymentMethod"),
                    ReadText(element, "assignedBranchCode"),
                    ReadText(element, "assignedBranch"),
                    ReadText(element, "assignedBranchName"),
                    ReadText(element, "payoutStatus"),
                    ReadText(element, "payoutDate"),
                    ReadText(element, "payoutUser"),
                    ReadText(element, "payoutTicket"),
                    ReadSellerBadges(element)));
            }

            return records;
        }
        catch (JsonException)
        {
            return Array.Empty<CascoTripRecord>();
        }
    }

    private static string ReadText(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.ToString() : string.Empty;

    /// <summary>
    /// Vendedores que atendieron la llegada, uno por gafete entregado. Viaja
    /// junto al registro para que el detalle quede guardado en detalle_json y
    /// el escritorio lo pueda mostrar sin columnas nuevas.
    /// </summary>
    private static IReadOnlyList<CascoTripRecordSeller> ReadSellerBadges(JsonElement element)
    {
        if (!element.TryGetProperty("sellerBadges", out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<CascoTripRecordSeller>();

        var sellers = new List<CascoTripRecordSeller>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var badgeId = ReadText(item, "badgeId");
            var sellerName = ReadText(item, "sellerName");
            if (badgeId.Length == 0 && sellerName.Length == 0)
                continue;

            sellers.Add(new CascoTripRecordSeller(badgeId, ReadText(item, "sellerKey"), sellerName));
        }

        return sellers;
    }

    private static int? ReadInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && int.TryParse(value.ToString(), out var parsed) ? parsed : null;

    private static decimal? ReadDecimal(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && decimal.TryParse(value.ToString(), CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && DateTimeOffset.TryParse(value.ToString(), out var parsed) ? parsed : null;

    private static async Task<CascoAutoSyncCycleResult> InsertNewRecordsAsync(CascoAutoSyncSettings settings, IReadOnlyList<CascoTripRecord> records, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.BuildSqlConnectionString());
        await connection.OpenAsync(cancellationToken);

        var insertedFolios = new List<string>();
        var newDetected = 0;
        var inserted = 0;
        var omitted = 0;
        var errors = 0;
        var omittedKeys = new List<string>();

        foreach (var record in records)
        {
            if (inserted >= settings.MaxNewPerCycle)
                break;

            if (await SyncExistingRecordFromApiAsync(connection, settings, record, cancellationToken))
            {
                omitted++;
                omittedKeys.Add($"{record.RecordId}/{record.BadgeId}");
                continue;
            }

            if (await ExistsLocalRecordAsync(connection, record.RecordId, record.Site, cancellationToken))
            {
                await EnsureExistingRecordMirrorsFromApiAsync(connection, settings, record, cancellationToken);
                omitted++;
                omittedKeys.Add($"{record.RecordId}/{record.BadgeId}");
                continue;
            }

            newDetected++;
            var folioLocal = await ResolveLocalFolioAsync(connection, record.RecordId, cancellationToken);

            try
            {
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (await ExistsLocalRecordAsync(connection, record.RecordId, record.Site, transaction, cancellationToken))
                    {
                        omitted++;
                        omittedKeys.Add($"{record.RecordId}/{record.BadgeId}");
                        await transaction.RollbackAsync(cancellationToken);
                        continue;
                    }

                    var values = BuildInsertValues(record, folioLocal);
                    await using var command = new SqlCommand(BuildInsertStatement(values.Keys), connection, transaction);
                    foreach (var parameter in CreateParameters(values))
                        command.Parameters.Add(parameter);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                    await ReconcileBadgeMirrorRowsFromApiAsync(connection, transaction, record, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    inserted++;
                    insertedFolios.Add(folioLocal);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            catch (Exception ex)
            {
                errors++;
                WriteLog(
                    settings,
                    $"ERROR insertando folio={record.RecordId} gafete={record.BadgeId} sitio={record.Site}: {ex.Message}");
            }
        }

        if (omittedKeys.Count > 0)
            WriteLog(settings, "Omitidos detalle: " + string.Join(", ", omittedKeys.Distinct(StringComparer.OrdinalIgnoreCase).Take(25)));

        return new CascoAutoSyncCycleResult(
            StartedAt: null,
            DurationMs: 0,
            Url: string.Empty,
            HttpStatus: 0,
            ReceivedCount: 0,
            CvCount: 0,
            NewDetectedCount: newDetected,
            InsertedCount: inserted,
            OmittedCount: omitted,
            ErrorCount: errors,
            InsertedFolios: insertedFolios,
            ErrorMessage: string.Empty);
    }

    private static async Task<bool> SyncExistingRecordFromApiAsync(
        SqlConnection connection,
        CascoAutoSyncSettings settings,
        CascoTripRecord record,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.RecordId) || string.IsNullOrWhiteSpace(record.Site))
            return false;

        var normalizedBadgeList = string.Join(", ", SplitBadgeValues(record.BadgeId));
        if (string.IsNullOrWhiteSpace(normalizedBadgeList))
            normalizedBadgeList = record.BadgeId?.Trim() ?? string.Empty;

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE dbo.AppMovilRegistro
            SET folio_gafete = @badgeList,
                vendedor_nombre = @driverName,
                hotel = @hotel,
                origen = @origin,
                destino = @destination,
                unidad = @unitNumber,
                modelo_vehiculo = @vehicleModel,
                tipo_operacion = @serviceType,
                telefono_taxista = @driverPhone,
                telefono_contacto = @contactPhone,
                nacionalidad = @nationality,
                total = @tripCost,
                efectivo = @cash,
                tarjeta = @card,
                notas = @notes,
                detalle_json = @detailJson
            WHERE folio_app_original = @recordId
              AND sitio = @site;
            """;
        var tripCost = record.TripCost ?? 0m;
        var isCash = string.Equals(record.PaymentMethod, "Efectivo", StringComparison.OrdinalIgnoreCase);
        var isCard = string.Equals(record.PaymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase);

        command.Parameters.AddWithValue("@recordId", record.RecordId);
        command.Parameters.AddWithValue("@site", record.Site);
        command.Parameters.AddWithValue("@badgeList", normalizedBadgeList);
        command.Parameters.AddWithValue("@driverName", record.DriverName ?? string.Empty);
        command.Parameters.AddWithValue("@sellerName", record.SellerName ?? string.Empty);
        command.Parameters.AddWithValue("@hotel", record.Hotel ?? string.Empty);
        command.Parameters.AddWithValue("@origin", record.Origin ?? string.Empty);
        command.Parameters.AddWithValue("@destination", record.Destination ?? string.Empty);
        command.Parameters.AddWithValue("@unitNumber", record.UnitNumber ?? string.Empty);
        command.Parameters.AddWithValue("@vehicleModel", record.VehicleModel ?? string.Empty);
        command.Parameters.AddWithValue("@serviceType", record.ServiceType ?? string.Empty);
        command.Parameters.AddWithValue("@driverPhone", record.DriverPhone ?? string.Empty);
        command.Parameters.AddWithValue("@contactPhone", record.ContactPhone ?? string.Empty);
        command.Parameters.AddWithValue("@nationality", record.Nationality ?? string.Empty);
        command.Parameters.AddWithValue("@tripCost", tripCost);
        command.Parameters.AddWithValue("@cash", isCash ? tripCost : 0m);
        command.Parameters.AddWithValue("@card", isCard ? tripCost : 0m);
        command.Parameters.AddWithValue("@notes", record.Notes ?? string.Empty);
        command.Parameters.AddWithValue("@detailJson", JsonSerializer.Serialize(record, SerializerOptions));

        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        if (updated > 0)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await ReconcileBadgeMirrorRowsFromApiAsync(connection, transaction, record, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        await UpdateRelationSellerFromApiAsync(connection, record, cancellationToken);
        if (updated > 0)
            WriteLog(settings, $"Folio actualizado desde Hostinger folio={record.RecordId} gafetes={normalizedBadgeList}");

        return updated > 0;
    }

    private static async Task ReconcileBadgeMirrorRowsFromApiAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoTripRecord record,
        CancellationToken cancellationToken)
    {
        var badges = SplitBadgeValues(record.BadgeId);
        if (badges.Count == 0 || string.IsNullOrWhiteSpace(record.RecordId))
            return;

        await ReconcileAppMovilRegistroGafetesFromApiAsync(connection, transaction, record, badges, cancellationToken);
        await ReconcileDejadaRowsFromApiAsync(connection, transaction, record, badges, cancellationToken);
        await ReconcileGafeteRowsFromApiAsync(connection, transaction, record, badges, cancellationToken);
    }

    private static async Task EnsureExistingRecordMirrorsFromApiAsync(
        SqlConnection connection,
        CascoAutoSyncSettings settings,
        CascoTripRecord record,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.RecordId) || string.IsNullOrWhiteSpace(record.Site))
            return;

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ReconcileBadgeMirrorRowsFromApiAsync(connection, transaction, record, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            WriteLog(settings, $"Espejos SQL verificados folio={record.RecordId} gafetes={string.Join(", ", SplitBadgeValues(record.BadgeId))}");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ReconcileAppMovilRegistroGafetesFromApiAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoTripRecord record,
        IReadOnlyList<string> badges,
        CancellationToken cancellationToken)
    {
        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        AddTextListParameters(deleteCommand, badges, "@badge");
        deleteCommand.CommandText =
            $"""
            IF OBJECT_ID(N'dbo.AppMovilRegistroGafetes', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.AppMovilRegistroGafetes
                WHERE FolioApp = @folioOriginal
                  AND UPPER(LTRIM(RTRIM(FolioGafete))) NOT IN ({BuildInList(badges.Count, "@badge")});
            END;
            """;
        deleteCommand.Parameters.AddWithValue("@folioOriginal", TrimSql(record.RecordId, 120));
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
            upsertCommand.Parameters.AddWithValue("@folioOriginal", TrimSql(record.RecordId, 120));
            upsertCommand.Parameters.AddWithValue("@idCatalogo", ParseIntOrZero(record.CatalogId));
            upsertCommand.Parameters.AddWithValue("@gafete", TrimSql(badge, 40));
            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReconcileDejadaRowsFromApiAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoTripRecord record,
        IReadOnlyList<string> badges,
        CancellationToken cancellationToken)
    {
        var operationDate = record.RecordDate?.DateTime ?? DateTime.Now;
        var operationNumber = ParseLongOrZero(record.RecordId);
        var payoutAmount = record.TripCost ?? 0m;
        var isCash = string.Equals(record.PaymentMethod, "Efectivo", StringComparison.OrdinalIgnoreCase);
        var isCard = string.Equals(record.PaymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase);

        await using var deleteCommand = connection.CreateCommand();
        deleteCommand.Transaction = transaction;
        AddTextListParameters(deleteCommand, badges, "@badge");
        deleteCommand.CommandText =
            $"""
            IF OBJECT_ID(N'dbo.dejadas', N'U') IS NOT NULL
            BEGIN
                DELETE FROM dbo.dejadas
                WHERE folioregistrostr = @folioOriginal
                  AND nombrealmacen = @site
                  AND CAST(fecha AS date) = @fecha
                  AND COALESCE(pago, 0) = 0
                  AND
                  (
                        gafete IS NULL
                     OR LTRIM(RTRIM(CONVERT(NVARCHAR(50), gafete))) = N''
                     OR UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), gafete)))) NOT IN ({BuildInList(badges.Count, "@badge")})
                  );
            END;
            """;
        deleteCommand.Parameters.AddWithValue("@folioOriginal", TrimSql(record.RecordId, 100));
        deleteCommand.Parameters.AddWithValue("@site", TrimSql(record.Site, 200));
        deleteCommand.Parameters.AddWithValue("@fecha", operationDate.Date);
        await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

        foreach (var badge in badges)
        {
            await using var upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText =
                """
                IF OBJECT_ID(N'dbo.dejadas', N'U') IS NOT NULL
                BEGIN
                    IF EXISTS
                    (
                        SELECT 1
                        FROM dbo.dejadas WITH (UPDLOCK, HOLDLOCK)
                        WHERE folioregistrostr = @folioOriginal
                          AND nombrealmacen = @site
                          AND CAST(fecha AS date) = @fecha
                          AND UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), COALESCE(gafete, ''))))) = UPPER(LTRIM(RTRIM(@gafete)))
                    )
                    BEGIN
                        UPDATE dbo.dejadas
                        SET idstaff = @idstaff,
                            nombrestaff = @driverName,
                            codigorecepcion = @folioOriginal,
                            folioregistro = @folioOperacion,
                            unidad = @unitNumber,
                            pax = @pax,
                            hotel = @hotel,
                            nombrevendedor = @sellerName,
                            tipotransporte = @serviceType,
                            telefono = @driverPhone,
                            totalventa = @payoutAmount,
                            total = @payoutAmount,
                            totalefectivo = @cash,
                            totaltarjeta = @card,
                            nacionalidad = @nationality,
                            pago = pago,
                            fechapago = fechapago
                        WHERE folioregistrostr = @folioOriginal
                          AND nombrealmacen = @site
                          AND CAST(fecha AS date) = @fecha
                          AND UPPER(LTRIM(RTRIM(CONVERT(NVARCHAR(50), COALESCE(gafete, ''))))) = UPPER(LTRIM(RTRIM(@gafete)));
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.dejadas
                            (idstaff, nombrestaff, nombrealmacen, idalmacen, fecha, hora, idcajero, nombrecajero, total,
                             codigorecepcion, folioregistro, folioregistrostr, unidad, pax, hotel, nombrevendedor,
                             tipotransporte, telefono, horaentrada, horasalida, totalventa, comision, pago,
                             fechapago, totalefectivo, totaltarjeta, totalgastos, gafete, nacionalidad)
                        VALUES
                            (@idstaff, @driverName, @site, 0, @fechaDateTime, @horaTexto, 0, @userName, @payoutAmount,
                             @folioOriginal, @folioOperacion, @folioOriginal, @unitNumber, @pax, @hotel, @sellerName,
                             @serviceType, @driverPhone, @horaTexto, @horaTexto, @payoutAmount, 0,
                             0,
                             NULL,
                             @cash, @card, 0, @gafete, @nationality);
                    END
                END;
                """;
            upsertCommand.Parameters.AddWithValue("@idstaff", TrimSql(record.RecordId, 50));
            upsertCommand.Parameters.AddWithValue("@driverName", TrimSql(record.DriverName, 200));
            upsertCommand.Parameters.AddWithValue("@sellerName", TrimSql(record.SellerName, 200));
            upsertCommand.Parameters.AddWithValue("@site", TrimSql(record.Site, 200));
            upsertCommand.Parameters.AddWithValue("@fecha", operationDate.Date);
            upsertCommand.Parameters.AddWithValue("@fechaDateTime", operationDate);
            upsertCommand.Parameters.AddWithValue("@horaTexto", operationDate.ToString("HH:mm", CultureInfo.InvariantCulture));
            upsertCommand.Parameters.AddWithValue("@userName", "HOSTINGER_SYNC");
            upsertCommand.Parameters.AddWithValue("@payoutAmount", payoutAmount);
            upsertCommand.Parameters.AddWithValue("@folioOriginal", TrimSql(record.RecordId, 100));
            upsertCommand.Parameters.AddWithValue("@folioOperacion", operationNumber);
            upsertCommand.Parameters.AddWithValue("@unitNumber", TrimSql(record.UnitNumber, 40));
            upsertCommand.Parameters.AddWithValue("@pax", record.PassengerCount ?? 0);
            upsertCommand.Parameters.AddWithValue("@hotel", TrimSql(record.Hotel, 200));
            upsertCommand.Parameters.AddWithValue("@serviceType", TrimSql(record.ServiceType, 20));
            upsertCommand.Parameters.AddWithValue("@driverPhone", TrimSql(record.DriverPhone, 24));
            upsertCommand.Parameters.AddWithValue("@cash", isCash ? payoutAmount : 0m);
            upsertCommand.Parameters.AddWithValue("@card", isCard ? payoutAmount : 0m);
            upsertCommand.Parameters.AddWithValue("@gafete", TrimSql(badge, 20));
            upsertCommand.Parameters.AddWithValue("@nationality", TrimSql(record.Nationality, 240));
            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReconcileGafeteRowsFromApiAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoTripRecord record,
        IReadOnlyList<string> badges,
        CancellationToken cancellationToken)
    {
        var numericBadges = badges
            .Select(badge => int.TryParse(badge, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (numericBadges.Length == 0)
            return;

        var operationNumber = ParseLongOrZero(record.RecordId);
        if (operationNumber <= 0)
            return;

        var operationDate = (record.RecordDate?.DateTime ?? DateTime.Now);

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
        deleteCommand.Parameters.AddWithValue("@folioOriginal", TrimSql(record.RecordId, 50));
        deleteCommand.Parameters.AddWithValue("@folioOperacionNumero", operationNumber);
        deleteCommand.Parameters.AddWithValue("@fecha", operationDate.Date);
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
                          AND UPPER(COALESCE(venta, '')) = 'R'
                    )
                    BEGIN
                        -- El regreso local manda. No revivir como asignado por un pull posterior de Hostinger.
                        SELECT 0;
                    END
                    ELSE IF EXISTS
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
            upsertCommand.Parameters.AddWithValue("@folioOriginal", TrimSql(record.RecordId, 50));
            upsertCommand.Parameters.AddWithValue("@folioOperacionNumero", operationNumber);
            upsertCommand.Parameters.AddWithValue("@gafete", badge);
            upsertCommand.Parameters.AddWithValue("@fecha", operationDate.Date);
            upsertCommand.Parameters.AddWithValue("@fechaDateTime", operationDate);
            upsertCommand.Parameters.AddWithValue("@movimiento", "APP MOVIL");
            upsertCommand.Parameters.AddWithValue("@usuario", "HOSTINGER_SYNC");
            await upsertCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task UpdateRelationSellerFromApiAsync(SqlConnection connection, CascoTripRecord record, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE dbo.RelacionTicketTaxista
            SET Vendedor = @sellerName,
                FechaActualizacion = SYSUTCDATETIME()
            WHERE FolioApp = @recordId
              AND COALESCE(LTRIM(RTRIM(Vendedor)), '') <> @sellerName;
            """;
        command.Parameters.AddWithValue("@recordId", record.RecordId ?? string.Empty);
        command.Parameters.AddWithValue("@sellerName", record.SellerName?.Trim() ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ExistsLocalRecordAsync(SqlConnection connection, string? recordId, string? site, string? badgeId, CancellationToken cancellationToken) =>
        await ExistsLocalRecordAsync(connection, recordId, site, badgeId, null, cancellationToken);

    private static async Task<bool> ExistsLocalRecordAsync(SqlConnection connection, string? recordId, string? site, CancellationToken cancellationToken) =>
        await ExistsLocalRecordAsync(connection, recordId, site, null, null, cancellationToken);

    private static async Task<bool> ExistsLocalRecordAsync(SqlConnection connection, string? recordId, string? site, SqlTransaction? transaction, CancellationToken cancellationToken)
        => await ExistsLocalRecordAsync(connection, recordId, site, null, transaction, cancellationToken);

    private static async Task<bool> ExistsLocalRecordAsync(SqlConnection connection, string? recordId, string? site, string? badgeId, SqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT TOP 1 1
            FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK)
            WHERE folio_app_original = @recordId
              AND sitio = @site
              AND (@badgeId IS NULL OR COALESCE(folio_gafete, '') = @badgeId);
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@recordId", recordId ?? string.Empty);
        command.Parameters.AddWithValue("@site", site ?? string.Empty);
        command.Parameters.AddWithValue("@badgeId", badgeId is null ? (object)DBNull.Value : badgeId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static IEnumerable<CascoTripRecord> ExpandRecordsByBadge(IEnumerable<CascoTripRecord> records)
    {
        foreach (var record in records)
        {
            var badges = SplitBadgeValues(record.BadgeId);
            if (badges.Count == 0)
            {
                yield return record;
                continue;
            }

            foreach (var badge in badges)
                yield return record with { BadgeId = badge };
        }
    }

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

    private static void AddTextListParameters(SqlCommand command, IReadOnlyList<string> values, string prefix)
    {
        for (var index = 0; index < values.Count; index++)
            command.Parameters.AddWithValue(prefix + index.ToString(CultureInfo.InvariantCulture), values[index]);
    }

    private static void AddNumberListParameters(SqlCommand command, IReadOnlyList<int> values, string prefix)
    {
        for (var index = 0; index < values.Count; index++)
            command.Parameters.AddWithValue(prefix + index.ToString(CultureInfo.InvariantCulture), values[index]);
    }

    private static string BuildInList(int count, string prefix) =>
        string.Join(", ", Enumerable.Range(0, count).Select(index => prefix + index.ToString(CultureInfo.InvariantCulture)));

    private static int ParseIntOrZero(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static long ParseLongOrZero(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;

    private static string TrimSql(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static async Task<bool> ExistsLocalFolioAsync(SqlConnection connection, string folio, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP 1 1 FROM dbo.AppMovilRegistro WITH (UPDLOCK, HOLDLOCK) WHERE folio_app = @folio;";
        command.Parameters.AddWithValue("@folio", folio);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task<string> ResolveLocalFolioAsync(SqlConnection connection, string? recordId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DECLARE @recordId NVARCHAR(60) = @recordIdParam;
            SELECT TOP (1) FolioControl
            FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK)
            WHERE FolioAppOriginal = @recordId;
            """;
        command.Parameters.AddWithValue("@recordIdParam", recordId ?? string.Empty);
        var existing = await command.ExecuteScalarAsync(cancellationToken);
        if (existing is not null && existing != DBNull.Value)
            return Convert.ToString(existing, CultureInfo.InvariantCulture) ?? string.Empty;

        await using var transactionCommand = connection.CreateCommand();
        transactionCommand.CommandText =
            """
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
            DECLARE @ultimo INT;
            SELECT @ultimo = ISNULL(MAX(CASE WHEN ISNUMERIC(FolioControl) = 1 THEN CAST(FolioControl AS INT) ELSE 0 END), 0)
            FROM dbo.AppMovilFolioControl WITH (UPDLOCK, HOLDLOCK);
            SELECT @ultimo = CASE WHEN AppMax.MaxFolio > @ultimo THEN AppMax.MaxFolio ELSE @ultimo END
            FROM (
                SELECT ISNULL(MAX(CASE WHEN ISNUMERIC(folio_app) = 1 THEN CAST(folio_app AS INT) WHEN ISNUMERIC(folio_app_original) = 1 THEN CAST(folio_app_original AS INT) ELSE 0 END), 0) AS MaxFolio
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
            """;
        transactionCommand.Parameters.AddWithValue("@recordId", recordId ?? string.Empty);
        var result = await transactionCommand.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static Dictionary<string, object?> BuildInsertValues(CascoTripRecord record, string folioLocal)
    {
        var tripCost = record.TripCost ?? 0m;
        var isCash = string.Equals(record.PaymentMethod, "Efectivo", StringComparison.OrdinalIgnoreCase);
        var isCard = string.Equals(record.PaymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase);
        var payoutStatus = string.IsNullOrWhiteSpace(record.PayoutStatus) ? "pendiente" : record.PayoutStatus.Trim();
        var normalizedBadgeList = string.Join(", ", SplitBadgeValues(record.BadgeId));
        var localSite = ResolveLocalOperationSite(record);
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["folio_app"] = folioLocal,
            ["folio_pos"] = string.Empty,
            ["fecha_operacion"] = record.RecordDate?.DateTime ?? DateTime.Now,
            ["vendedor_clave"] = string.Empty,
            ["vendedor_nombre"] = record.DriverName,
            ["hotel"] = record.Hotel,
            ["pax"] = record.PassengerCount ?? 0,
            ["tipo_operacion"] = record.ServiceType,
            ["subtotal"] = 0m,
            ["iva"] = 0m,
            ["total"] = tripCost,
            ["efectivo"] = isCash ? tripCost : 0m,
            ["tarjeta"] = isCard ? tripCost : 0m,
            ["dolares"] = 0m,
            ["tipo_cambio"] = 1m,
            ["usuario_movil"] = string.Empty,
            ["notas"] = record.Notes,
            ["detalle_json"] = JsonSerializer.Serialize(record, SerializerOptions),
            ["pagos_json"] = string.Empty,
            ["origen"] = record.Origin,
            ["estado_sync"] = "pendiente",
            ["fecha_creacion"] = DateTime.UtcNow,
            ["folio_app_original"] = record.RecordId,
            ["folio_gafete"] = string.IsNullOrWhiteSpace(normalizedBadgeList) ? record.BadgeId : normalizedBadgeList,
            ["id_catalogo"] = int.TryParse(record.CatalogId, out var catalogId) ? catalogId : null,
            ["telefono_taxista"] = record.DriverPhone,
            ["telefono_contacto"] = record.ContactPhone,
            ["placas"] = record.Plate,
            ["modelo_vehiculo"] = record.VehicleModel,
            ["unidad"] = record.UnitNumber,
            ["sitio"] = localSite,
            ["destino"] = record.Destination,
            ["nacionalidad"] = record.Nationality
        };
    }

    private static string ResolveLocalOperationSite(CascoTripRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.AssignedBranchName))
            return record.AssignedBranchName.Trim();

        if (!string.IsNullOrWhiteSpace(record.AssignedBranch))
        {
            var assignedBranch = record.AssignedBranch.Trim();
            var separatorIndex = assignedBranch.IndexOf('-', StringComparison.Ordinal);
            if (separatorIndex >= 0 && separatorIndex + 1 < assignedBranch.Length)
            {
                var withoutCode = assignedBranch[(separatorIndex + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(withoutCode))
                    return withoutCode;
            }

            return assignedBranch;
        }

        return record.Site;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    private static bool IsPaidPayoutStatus(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Equals("pagado", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("pagada", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("paid", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildInsertStatement(IEnumerable<string> columns) =>
        $"INSERT INTO dbo.AppMovilRegistro ({string.Join(", ", columns)}) VALUES ({string.Join(", ", columns.Select(x => "@" + x))});";

    private static IReadOnlyList<SqlParameter> CreateParameters(IDictionary<string, object?> values) =>
        values.Select(value => new SqlParameter("@" + value.Key, value.Value ?? DBNull.Value)).ToArray();

    private void UpdateStatus(Func<CascoAutoSyncStatusSnapshot, CascoAutoSyncStatusSnapshot> updater)
    {
        var settings = LoadSettings();
        CascoAutoSyncStatusSnapshot next;
        lock (_stateLock)
        {
            _status = updater(_status);
            next = _status;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(settings.StatusFilePath)!);
        WriteStatusSnapshot(settings.StatusFilePath, JsonSerializer.Serialize(next, SerializerOptions));
    }

    private static void WriteLog(CascoAutoSyncSettings settings, params string[] lines)
    {
        Directory.CreateDirectory(settings.LogDirectory);
        File.AppendAllText(
            settings.LogFilePath,
            $"{string.Join(Environment.NewLine, lines)}{Environment.NewLine}");
    }

    private static void WriteStatusSnapshot(string path, string content)
    {
        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.Write(content);
                return;
            }
            catch (IOException ex)
            {
                lastError = ex;
                Thread.Sleep(100);
            }
        }

        throw lastError ?? new IOException($"No se pudo escribir el estado de Casco en {path}.");
    }
}

public sealed record CascoTripRecord(
    string RecordId,
    string CatalogId,
    string BadgeId,
    string DriverName,
    string SellerName,
    string DriverPhone,
    string ContactPhone,
    string Nationality,
    string Plate,
    string VehicleModel,
    string UnitNumber,
    string Hotel,
    string Origin,
    string Site,
    string Destination,
    int? PassengerCount,
    string ServiceType,
    decimal? TripCost,
    string Notes,
    DateTimeOffset? RecordDate,
    string PaymentMethod,
    string AssignedBranchCode,
    string AssignedBranch,
    string AssignedBranchName,
    string PayoutStatus,
    string PayoutDate,
    string PayoutUser,
    string PayoutTicket,
    IReadOnlyList<CascoTripRecordSeller>? SellerBadges = null);

public sealed record CascoTripRecordSeller(string BadgeId, string SellerKey, string SellerName);

public sealed record CascoApiCallResult(string Url, int HttpStatusCode, string? ContentType, IReadOnlyList<CascoTripRecord> Records);

public sealed record CascoAutoSyncCycleResult(
    DateTimeOffset? StartedAt,
    long DurationMs,
    string Url,
    int HttpStatus,
    int ReceivedCount,
    int CvCount,
    int NewDetectedCount,
    int InsertedCount,
    int OmittedCount,
    int ErrorCount,
    IReadOnlyList<string> InsertedFolios,
    string ErrorMessage)
{
    public static CascoAutoSyncCycleResult Disabled(string message) => new(null, 0, string.Empty, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), message);
    public static CascoAutoSyncCycleResult Skipped(string message) => new(null, 0, string.Empty, 0, 0, 0, 0, 0, 0, 0, Array.Empty<string>(), message);
    public static CascoAutoSyncCycleResult Failed(string message, DateTimeOffset startedAt, long durationMs) => new(startedAt, durationMs, string.Empty, 0, 0, 0, 0, 0, 0, 1, Array.Empty<string>(), message);
}

public sealed class CascoRecordsChangedEventArgs : EventArgs
{
    public CascoRecordsChangedEventArgs(int insertedCount, IReadOnlyList<string> insertedFolios)
    {
        InsertedCount = insertedCount;
        InsertedFolios = insertedFolios;
    }

    public int InsertedCount { get; }
    public IReadOnlyList<string> InsertedFolios { get; }
}

public sealed record CascoAutoSyncStatusSnapshot(
    bool Configured,
    bool PasswordAvailable,
    int IntervalSeconds,
    DateTimeOffset? LastCycleAt,
    int LastHttp,
    int LastReceivedCount,
    int LastInsertedCount,
    int LastOmittedCount,
    string LastError,
    long LastDurationMs,
    bool LockActive)
{
    public static CascoAutoSyncStatusSnapshot CreateDefault() => new(false, false, 20, null, 0, 0, 0, 0, string.Empty, 0, false);
}

public sealed class CascoAutoSyncSettings
{
    public required string ApiBaseUrl { get; init; }
    public required string ApiEndpointPath { get; init; }
    public required string SqlServer { get; init; }
    public required string SqlDatabase { get; init; }
    public required string SqlUser { get; init; }
    public string? SqlPassword { get; init; }
    public required string LogDirectory { get; init; }
    public required string LogFilePath { get; init; }
    public required string StatusFilePath { get; init; }
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxNewPerCycle { get; init; } = 10;

    public bool PasswordAvailable => !string.IsNullOrWhiteSpace(SqlPassword);

    public string BuildApiUrl()
    {
        var baseUri = new Uri(ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, ApiEndpointPath.TrimStart('/')).ToString();
    }

    public string BuildSqlConnectionString() => new SqlConnectionStringBuilder
    {
        DataSource = SqlServer,
        InitialCatalog = SqlDatabase,
        UserID = SqlUser,
        Password = SqlPassword,
        TrustServerCertificate = true,
        Encrypt = false,
        ConnectTimeout = 15
    }.ConnectionString;

    public static CascoAutoSyncSettings LoadFromWorkspace()
    {
        var workspaceRoot = FindWorkspaceRoot();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var configPath = Path.Combine(workspaceRoot, "Config", "appsettings.json");
        var devConfigPath = Path.Combine(workspaceRoot, "Config", "appsettings.Development.json");
        var productionConfigPath = Path.Combine(workspaceRoot, "appsettings.production.json");
        if (File.Exists(configPath))
            MergeJson(values, configPath);
        if (File.Exists(devConfigPath))
            MergeJson(values, devConfigPath);
        if (File.Exists(productionConfigPath))
            MergeJson(values, productionConfigPath);

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD")))
            CascoCredentialStore.TryApplyToEnvironment(out _);

        var logDirectory = Path.Combine(workspaceRoot, "Logs", "CascoSync");
        return new CascoAutoSyncSettings
        {
            ApiBaseUrl = Read(values, "CascoSync:ApiBaseUrl") ?? "https://lightyellow-porpoise-679527.hostingersite.com/casco-api/",
            ApiEndpointPath = Read(values, "CascoSync:ApiEndpointPath") ?? "api/taxis/registros",
            SqlServer = Environment.GetEnvironmentVariable("CASCO_SQL_SERVER") ?? Read(values, "CascoSync:SqlServer") ?? "192.168.1.70,50807",
            SqlDatabase = Environment.GetEnvironmentVariable("CASCO_SQL_DATABASE") ?? Read(values, "CascoSync:SqlDatabase") ?? "mkt",
            SqlUser = Environment.GetEnvironmentVariable("CASCO_SQL_USER") ?? Read(values, "CascoSync:SqlUser") ?? "sa",
            SqlPassword = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD"),
            LogDirectory = logDirectory,
            LogFilePath = Path.Combine(logDirectory, "casco-auto-sync.log"),
            StatusFilePath = Path.Combine(logDirectory, "casco-auto-sync-status.json")
        };
    }

    private static void MergeJson(IDictionary<string, string?> values, string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Flatten(document.RootElement, string.Empty, values);
    }

    private static void Flatten(JsonElement element, string prefix, IDictionary<string, string?> values)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var next = string.IsNullOrWhiteSpace(prefix) ? property.Name : prefix + ":" + property.Name;
                Flatten(property.Value, next, values);
            }
            return;
        }

        values[prefix] = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static string? Read(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if ((string.Equals(current.Name, "Desktop", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current.Name, "Tools", StringComparison.OrdinalIgnoreCase))
                && current.Parent is not null
                && (File.Exists(Path.Combine(current.Parent.FullName, "appsettings.production.json"))
                    || File.Exists(Path.Combine(current.Parent.FullName, "SyncTaxi", "sync.casco.config.json"))))
                return current.Parent.FullName;

            if (File.Exists(Path.Combine(current.FullName, "CONTROL TAXI.sln")))
                return current.FullName;
            if (File.Exists(Path.Combine(current.FullName, "appsettings.production.json"))
                || File.Exists(Path.Combine(current.FullName, "SyncTaxi", "sync.casco.config.json"))
                || File.Exists(Path.Combine(current.FullName, "Tools", "ControlTaxiDesktop.Tools.exe"))
                || File.Exists(Path.Combine(current.FullName, "Desktop", "ControlTaxiDesktop.exe")))
                return current.FullName;
            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
