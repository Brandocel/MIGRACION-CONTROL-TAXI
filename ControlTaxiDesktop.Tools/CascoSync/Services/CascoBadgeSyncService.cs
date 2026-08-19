using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ControlTaxiDesktop.Tools.CascoSync.Configuration;
using ControlTaxiDesktop.Tools.CascoSync.State;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

public sealed class CascoBadgeSyncService
{
    private readonly CascoBadgeSyncConfiguration _configuration;
    private readonly CascoBadgeSyncStateStore _stateStore;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public CascoBadgeSyncService(CascoBadgeSyncConfiguration configuration)
    {
        _configuration = configuration;
        _stateStore = new CascoBadgeSyncStateStore(configuration.StatusFilePath);
    }

    public CascoBadgeSyncConfiguration Configuration => _configuration;
    public string StatePath => _stateStore.Path;
    public string GetPayloadPath(string badgeId) => Path.Combine(Path.GetDirectoryName(_configuration.LogFilePath)!, $"casco-badge-sync-payload-{badgeId}.json");

    public async Task<CascoBadgeSyncDiagnostic> BuildDiagnosticAsync(string badgeId, CancellationToken cancellationToken)
    {
        var preview = await LoadPreviewAsync(badgeId, cancellationToken);
        var payloadPath = SavePayload(preview.Row.BadgeId, preview.PayloadJson);
        return new CascoBadgeSyncDiagnostic(
            _configuration.ConfigPath,
            _configuration.BranchCode,
            _configuration.ApiBaseUrl,
            $"{_configuration.SqlServer}/{_configuration.MktDatabase}",
            preview.Row,
            preview.PayloadJson,
            payloadPath,
            _configuration.BuildPushUrl(),
            _configuration.LockFilePath,
            _configuration.LogFilePath,
            preview.IsPending,
            false,
            false,
            preview.Safety);
    }

    public async Task<CascoBadgeSyncPreview> LoadPreviewAsync(string badgeId, CancellationToken cancellationToken)
    {
        var normalizedBadgeId = badgeId.Trim();
        var row = await LoadRowAsync(normalizedBadgeId, cancellationToken);
        var state = _stateStore.Load();
        var entry = state.Entries.TryGetValue(normalizedBadgeId, out var currentEntry) ? currentEntry : null;
        var fingerprint = BuildFingerprint(row);
        var now = DateTimeOffset.Now;
        var isPending = entry is null
            || !string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)
            || entry.Synced == false && (!entry.NextRetryAt.HasValue || entry.NextRetryAt.Value <= now);

        var payload = BuildPayload(row);
        var payloadJson = JsonSerializer.Serialize(payload, _jsonOptions);

        return new CascoBadgeSyncPreview(
            row,
            payloadJson,
            isPending,
            entry,
            EvaluateSafety(row),
            ValidatePayloadJson(payloadJson));
    }

    public async Task<CascoBadgeSyncCycleResult> RunWatchCycleAsync(CancellationToken cancellationToken, bool dryRun)
    {
        var stage = "configuracion";
        try
        {
            WriteLog("INFO", stage, "Inicio de ciclo de sincronización de gafetes Casco.");
            using var lockHandle = AcquireLock();
            stage = "lectura SQL";
            var rows = await LoadRowsAsync(cancellationToken);
            var state = _stateStore.Load();
            var updatedEntries = new Dictionary<string, CascoBadgeSyncEntryState>(state.Entries, StringComparer.OrdinalIgnoreCase);
            var prepared = 0;
            var sent = 0;
            var omitted = 0;
            var errors = 0;
            var lastHttp = (int?)null;
            var now = DateTimeOffset.Now;

            foreach (var row in rows)
            {
                stage = "construccion de payload";
                var safety = EvaluateSafety(row);
                var fingerprint = BuildFingerprint(row);
                var previous = updatedEntries.TryGetValue(row.BadgeId, out var existing) ? existing : null;
                var shouldRetry = previous is not null
                    && !previous.Synced
                    && (!previous.NextRetryAt.HasValue || previous.NextRetryAt.Value <= now);
                var isPending = dryRun
                    ? previous is null || !previous.Synced || !string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal)
                    : previous is null
                      || !string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal)
                      || shouldRetry;

                if (!isPending)
                {
                    omitted++;
                    continue;
                }

                if (!safety.CanPost)
                {
                    errors++;
                    var error = BuildSyntheticError("PrePostGuardError", "La validación de seguridad previa al POST no corresponde a Casco.", stage, row.BadgeId, null);
                    updatedEntries[row.BadgeId] = new CascoBadgeSyncEntryState(
                        row.BadgeId,
                        fingerprint,
                        row.Status,
                        row.CreatedAt,
                        now,
                        null,
                        false,
                        error,
                        now.AddSeconds(_configuration.IntervalSeconds));
                    WriteLog("ERROR", stage, error);
                    continue;
                }

                stage = "serializacion";
                var preview = await LoadPreviewAsync(row.BadgeId, cancellationToken);
                var payloadPath = SavePayload(row.BadgeId, preview.PayloadJson);
                var payloadValid = preview.PayloadValid;
                if (!payloadValid)
                {
                    errors++;
                    var error = BuildSyntheticError("PayloadValidationError", "El payload JSON no pasó la validación de System.Text.Json.", stage, row.BadgeId, null);
                    updatedEntries[row.BadgeId] = new CascoBadgeSyncEntryState(
                        row.BadgeId,
                        fingerprint,
                        row.Status,
                        row.CreatedAt,
                        now,
                        null,
                        false,
                        error,
                        now.AddSeconds(_configuration.IntervalSeconds));
                    WriteLog("ERROR", stage, error);
                    continue;
                }

                stage = "estado";
                prepared++;
                if (dryRun)
                {
                    updatedEntries[row.BadgeId] = new CascoBadgeSyncEntryState(
                        row.BadgeId,
                        fingerprint,
                        row.Status,
                        row.CreatedAt,
                        now,
                        null,
                        false,
                        null,
                        null);
                    WriteLog("INFO", stage, $"Payload preparado para badgeId={row.BadgeId}. payloadPath={payloadPath}. dryRun={dryRun.ToString().ToLowerInvariant()}");
                    continue;
                }

                stage = "http";
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                using var request = new HttpRequestMessage(HttpMethod.Post, _configuration.BuildPushUrl())
                {
                    Content = new StringContent(preview.PayloadJson, Encoding.UTF8, "application/json")
                };
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                using var response = await httpClient.SendAsync(request, cancellationToken);
                stopwatch.Stop();
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                lastHttp = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    sent++;
                    updatedEntries[row.BadgeId] = new CascoBadgeSyncEntryState(
                        row.BadgeId,
                        fingerprint,
                        row.Status,
                        row.CreatedAt,
                        now,
                        lastHttp,
                        true,
                        null,
                        null);
                    WriteLog("INFO", stage, $"POST exitoso para badgeId={row.BadgeId}. http={lastHttp}. durationMs={stopwatch.ElapsedMilliseconds}. lastSuccessAt={now:O}");
                }
                else
                {
                    errors++;
                    updatedEntries[row.BadgeId] = new CascoBadgeSyncEntryState(
                        row.BadgeId,
                        fingerprint,
                        row.Status,
                        row.CreatedAt,
                        now,
                        lastHttp,
                        false,
                        responseBody,
                        now.AddSeconds(_configuration.IntervalSeconds));
                    WriteLog("ERROR", stage, $"POST fallido para badgeId={row.BadgeId}. http={lastHttp}. durationMs={stopwatch.ElapsedMilliseconds}. body={responseBody}");
                }
            }

            _stateStore.Save(new CascoBadgeSyncState(now, lastHttp, updatedEntries));
            WriteLog("INFO", "estado", $"Ciclo completado. scanned={rows.Count}; prepared={prepared}; sent={sent}; omitted={omitted}; errors={errors}; dryRun={dryRun.ToString().ToLowerInvariant()}");
            return new CascoBadgeSyncCycleResult(rows.Count, prepared, sent, omitted, errors, lastHttp, _configuration.LogFilePath, _configuration.LockFilePath, _configuration.StatusFilePath, dryRun, true, !dryRun && (sent > 0 || errors > 0), null);
        }
        catch (Exception exception)
        {
            var error = BuildSyntheticError(exception.GetType().FullName ?? exception.GetType().Name, exception.Message, stage, null, exception);
            WriteLog("ERROR", stage, error);
            return new CascoBadgeSyncCycleResult(0, 0, 0, 0, 1, null, _configuration.LogFilePath, _configuration.LockFilePath, _configuration.StatusFilePath, dryRun, false, false, error);
        }
    }

    public async Task RunWatchLoopAsync(CancellationToken cancellationToken, bool dryRun)
    {
        WriteLog("INFO", "inicio", $"Proceso de sincronizacion Casco iniciado. dryRun={dryRun.ToString().ToLowerInvariant()} intervalSeconds={_configuration.IntervalSeconds} branchCode={_configuration.BranchCode} database={_configuration.MktDatabase}");
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await RunWatchCycleAsync(cancellationToken, dryRun);
            WriteLog("INFO", "resumen", $"Resumen ciclo. scanned={result.Scanned}; prepared={result.Prepared}; sent={result.Sent}; omitted={result.Omitted}; errors={result.Errors}; lastHttp={(result.LastHttpStatus.HasValue ? result.LastHttpStatus.Value.ToString(CultureInfo.InvariantCulture) : "sin POST")}");
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_configuration.IntervalSeconds), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        WriteLog("INFO", "fin", "Proceso de sincronizacion Casco finalizado.");
    }

    public async Task<CascoBadgeSyncPostResult> PostSingleAsync(string badgeId, CancellationToken cancellationToken)
    {
        const string stage = "http";
        var startedAt = DateTimeOffset.Now;
        using var lockHandle = AcquireLock();
        var preview = await LoadPreviewAsync(badgeId, cancellationToken);
        var payloadPath = SavePayload(preview.Row.BadgeId, preview.PayloadJson);
        var fingerprint = BuildFingerprint(preview.Row);
        var state = _stateStore.Load();
        var updatedEntries = new Dictionary<string, CascoBadgeSyncEntryState>(state.Entries, StringComparer.OrdinalIgnoreCase);

        if (!preview.Safety.CanPost)
            throw new InvalidOperationException("La validación de seguridad no corresponde a Casco.");

        if (!string.Equals(preview.Row.Status, "R", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"El status '{preview.Row.Status}' no es elegible para envío.");

        if (!preview.PayloadValid)
            throw new InvalidOperationException("El payload JSON no pasó la validación previa.");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var request = new HttpRequestMessage(HttpMethod.Post, _configuration.BuildPushUrl())
        {
            Content = new StringContent(preview.PayloadJson, Encoding.UTF8, "application/json")
        };

        WriteLog("INFO", stage, $"Enviando POST único para badgeId={preview.Row.BadgeId}. endpoint={_configuration.BuildPushUrl()}");
        var startedStopwatch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await httpClient.SendAsync(request, cancellationToken);
        startedStopwatch.Stop();
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var now = DateTimeOffset.Now;
        var httpStatus = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            updatedEntries[preview.Row.BadgeId] = new CascoBadgeSyncEntryState(
                preview.Row.BadgeId,
                fingerprint,
                preview.Row.Status,
                preview.Row.CreatedAt,
                now,
                httpStatus,
                true,
                null,
                null);
            _stateStore.Save(new CascoBadgeSyncState(now, httpStatus, updatedEntries));
            WriteLog("INFO", stage, $"POST exitoso para badgeId={preview.Row.BadgeId}. http={httpStatus}. durationMs={startedStopwatch.ElapsedMilliseconds}");
        }
        else
        {
            updatedEntries[preview.Row.BadgeId] = new CascoBadgeSyncEntryState(
                preview.Row.BadgeId,
                fingerprint,
                preview.Row.Status,
                preview.Row.CreatedAt,
                now,
                httpStatus,
                false,
                responseBody,
                now.AddSeconds(_configuration.IntervalSeconds));
            _stateStore.Save(new CascoBadgeSyncState(now, httpStatus, updatedEntries));
            WriteLog("ERROR", stage, $"POST fallido para badgeId={preview.Row.BadgeId}. http={httpStatus}. durationMs={startedStopwatch.ElapsedMilliseconds}. body={responseBody}");
        }

        return new CascoBadgeSyncPostResult(
            preview.Row.BadgeId,
            _configuration.BuildPushUrl(),
            preview.PayloadJson,
            payloadPath,
            httpStatus,
            responseBody,
            startedStopwatch.ElapsedMilliseconds,
            1,
            response.IsSuccessStatusCode,
            startedAt,
            now);
    }

    public async Task<IReadOnlyList<CascoBadgeSyncRow>> LoadRowsAsync(CancellationToken cancellationToken)
    {
        var rows = new List<CascoBadgeSyncRow>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH latest AS
            (
                SELECT
                    CONVERT(nvarchar(80), g.gafete) AS BadgeId,
                    UPPER(COALESCE(g.venta, '')) AS StatusCode,
                    COALESCE(g.hora, g.fecha, GETDATE()) AS CreatedAt,
                    CONVERT(nvarchar(80), g.matricula) AS TaxistaIdRaw,
                    CONVERT(nvarchar(80), g.folioperacion) AS FolioOperacion,
                    ROW_NUMBER() OVER (
                        PARTITION BY CONVERT(nvarchar(80), g.gafete)
                        ORDER BY COALESCE(g.hora, g.fecha, GETDATE()) DESC, CONVERT(nvarchar(80), g.folioperacion) DESC
                    ) AS rn
                FROM dbo.gafete g
                WHERE g.gafete IS NOT NULL
                  AND UPPER(COALESCE(g.venta, '')) IN ('R', 'S')
            )
            SELECT
                l.BadgeId,
                l.StatusCode,
                l.CreatedAt,
                l.TaxistaIdRaw,
                COALESCE(NULLIF(a.vendedor_nombre, ''), '') AS TaxistaName,
                COALESCE(NULLIF(a.folio_app_original, ''), '') AS FolioOriginal,
                COALESCE(NULLIF(a.folio_app, ''), '') AS FolioLocal,
                COALESCE(NULLIF(a.sitio, ''), '') AS Sitio
            FROM latest l
            OUTER APPLY
            (
                SELECT TOP (1)
                    a.vendedor_nombre,
                    a.folio_app_original,
                    a.folio_app,
                    a.sitio
                FROM dbo.AppMovilRegistro a
                WHERE COALESCE(a.sitio, '') = @sitio
                  AND (
                        UPPER(COALESCE(a.folio_app, '')) = UPPER(l.FolioOperacion)
                     OR UPPER(COALESCE(a.folio_app_original, '')) = UPPER(l.FolioOperacion)
                     OR ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(a.folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
                        LIKE '%,' + l.BadgeId + ',%'
                  )
                ORDER BY a.fecha_operacion DESC
            ) a
            WHERE l.rn = 1
            ORDER BY l.CreatedAt DESC;
            """;
        command.Parameters.AddWithValue("@sitio", "Casco Viejo");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim().ToUpperInvariant();
            var createdAt = reader.IsDBNull(2)
                ? DateTimeOffset.Now
                : new DateTimeOffset(Convert.ToDateTime(reader.GetValue(2), CultureInfo.InvariantCulture));
            rows.Add(new CascoBadgeSyncRow(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim(),
                status,
                1,
                null,
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim(),
                createdAt,
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5).Trim(),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6).Trim(),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim(),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7).Trim(),
                "dbo.gafete"));
        }

        return rows;
    }

    private async Task<CascoBadgeSyncRow> LoadRowAsync(string badgeId, CancellationToken cancellationToken)
    {
        var row = (await LoadRowsAsync(cancellationToken)).FirstOrDefault(item => string.Equals(item.BadgeId, badgeId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            throw new InvalidOperationException($"No se encontró fila elegible para el gafete {badgeId} en dbo.gafete con estado R/S.");
        return row;
    }

    private CascoBadgeSyncSafety EvaluateSafety(CascoBadgeSyncRow row)
    {
        var branchOk = string.Equals(_configuration.BranchCode, "CV", StringComparison.OrdinalIgnoreCase);
        var urlOk = _configuration.ApiBaseUrl.Contains("/casco-api", StringComparison.OrdinalIgnoreCase);
        var databaseOk = string.Equals(_configuration.MktDatabase, "mkt", StringComparison.OrdinalIgnoreCase);
        var tableOk = string.Equals(row.SourceTable, "dbo.gafete", StringComparison.OrdinalIgnoreCase);
        var badgeOk = !string.IsNullOrWhiteSpace(row.BadgeId);
        var plaza28 = string.Equals(_configuration.BranchCode, "28", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_configuration.MktDatabase, "mkt2", StringComparison.OrdinalIgnoreCase)
            || !_configuration.ApiBaseUrl.Contains("/casco-api", StringComparison.OrdinalIgnoreCase);
        return new CascoBadgeSyncSafety(branchOk, urlOk, databaseOk, tableOk, badgeOk, plaza28);
    }

    private object BuildPayload(CascoBadgeSyncRow row) => new
    {
        branchCode = _configuration.BranchCode,
        gafetes = new[]
        {
            new
            {
                badgeId = row.BadgeId,
                barcode = row.Barcode,
                status = row.Status,
                cycle = row.Cycle,
                taxistaId = row.TaxistaId,
                taxistaName = row.TaxistaName,
                createdAt = row.CreatedAt.ToString("s", CultureInfo.InvariantCulture)
            }
        }
    };

    private string BuildFingerprint(CascoBadgeSyncRow row)
    {
        var source = $"{row.BadgeId}|{row.Status}|{row.CreatedAt:O}|{row.FolioOriginal}|{row.FolioLocal}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes);
    }

    private FileStream AcquireLock()
    {
        var directory = Path.GetDirectoryName(_configuration.LockFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        return new FileStream(_configuration.LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private string SavePayload(string badgeId, string payloadJson)
    {
        var path = GetPayloadPath(badgeId);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, payloadJson, Encoding.UTF8);
        return path;
    }

    private bool ValidatePayloadJson(string payloadJson)
    {
        using var _ = JsonDocument.Parse(payloadJson);
        return true;
    }

    private void WriteLog(string level, string stage, string message)
    {
        var directory = Path.GetDirectoryName(_configuration.LogFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var line = $"[{DateTimeOffset.Now:O}] level={level} stage={stage} {message}";
        File.AppendAllText(_configuration.LogFilePath, line + Environment.NewLine, Encoding.UTF8);
    }

    private static string BuildSyntheticError(string exceptionType, string message, string stage, string? badgeId, Exception? exception)
    {
        var inner = exception?.InnerException?.ToString() ?? "<null>";
        var stack = exception?.StackTrace ?? "<null>";
        return $"ExceptionType={exceptionType}{Environment.NewLine}Message={message}{Environment.NewLine}InnerException={inner}{Environment.NewLine}StackTrace={stack}{Environment.NewLine}Metodo={exception?.TargetSite?.Name ?? "RunWatchCycleAsync"}{Environment.NewLine}Archivo=ControlTaxiDesktop.Tools/CascoSync/Services/CascoBadgeSyncService.cs{Environment.NewLine}Linea=<manual>{Environment.NewLine}BadgeId={badgeId ?? "<null>"}{Environment.NewLine}Etapa={stage}";
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var password = ResolveSqlPassword();
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("No se encontro la credencial SQL cifrada de Casco para sincronizar gafetes.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _configuration.SqlServer,
            InitialCatalog = _configuration.MktDatabase,
            UserID = _configuration.SqlUser,
            Password = password,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15
        };

        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private string ResolveSqlPassword()
    {
        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD");
        if (!string.IsNullOrWhiteSpace(password))
            return password;

        try
        {
            var credentialPath = ResolveCredentialPath();
            if (credentialPath is null)
                return string.Empty;

            var encrypted = File.ReadAllBytes(credentialPath);
            var entropy = Encoding.UTF8.GetBytes("ControlTaxiDesktop.Casco.Credentials.v1");
            var jsonBytes = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.LocalMachine);
            using var document = JsonDocument.Parse(jsonBytes);
            if (document.RootElement.TryGetProperty("SqlPassword", out var value)
                && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? string.Empty;
        }
        catch (Exception exception)
        {
            WriteLog("ERROR", "credencial", $"No se pudo leer la credencial cifrada de Casco. error={exception.Message}");
        }

        return string.Empty;
    }

    private string? ResolveCredentialPath()
    {
        var configDirectory = Path.GetDirectoryName(_configuration.ConfigPath);
        var workspaceRoot = configDirectory is null ? Environment.CurrentDirectory : Directory.GetParent(configDirectory)?.FullName;
        var candidates = new[]
        {
            workspaceRoot is null ? null : Path.Combine(workspaceRoot, "Config", "casco.credentials.dat"),
            Path.Combine(Environment.CurrentDirectory, "Config", "casco.credentials.dat"),
            Path.Combine(AppContext.BaseDirectory, "Config", "casco.credentials.dat"),
            Directory.GetParent(AppContext.BaseDirectory) is null
                ? null
                : Path.Combine(Directory.GetParent(AppContext.BaseDirectory)!.FullName, "Config", "casco.credentials.dat")
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }
}

public sealed record CascoBadgeSyncRow(
    string BadgeId,
    string Barcode,
    string Status,
    int Cycle,
    int? TaxistaId,
    string TaxistaName,
    DateTimeOffset CreatedAt,
    string FolioOriginal,
    string FolioLocal,
    string Taxista,
    string Sitio,
    string SourceTable);

public sealed record CascoBadgeSyncSafety(
    bool BranchCodeIsCv,
    bool UrlContainsCascoApi,
    bool DatabaseIsMktCasco,
    bool SourceTableIsGafete,
    bool BadgeMatches,
    bool Plaza28)
{
    public bool CanPost => BranchCodeIsCv && UrlContainsCascoApi && DatabaseIsMktCasco && SourceTableIsGafete && BadgeMatches && !Plaza28;
}

public sealed record CascoBadgeSyncDiagnostic(
    string ConfigPath,
    string BranchCode,
    string ApiBaseUrl,
    string LocalDatabase,
    CascoBadgeSyncRow Row,
    string PayloadJson,
    string PayloadPath,
    string EndpointFinal,
    string LockPath,
    string LogPath,
    bool PendingState,
    bool PostPerformed,
    bool Plaza28,
    CascoBadgeSyncSafety Safety);

public sealed record CascoBadgeSyncPreview(
    CascoBadgeSyncRow Row,
    string PayloadJson,
    bool IsPending,
    CascoBadgeSyncEntryState? PreviousState,
    CascoBadgeSyncSafety Safety,
    bool PayloadValid);

public sealed record CascoBadgeSyncCycleResult(
    int Scanned,
    int Prepared,
    int Sent,
    int Omitted,
    int Errors,
    int? LastHttpStatus,
    string LogPath,
    string LockPath,
    string StatusPath,
    bool DryRun,
    bool PayloadValid,
    bool PostPerformed,
    string? ErrorDetail);

public sealed record CascoBadgeSyncPostResult(
    string BadgeId,
    string Endpoint,
    string PayloadJson,
    string PayloadPath,
    int HttpStatus,
    string ResponseBody,
    long DurationMs,
    int AttemptNumber,
    bool Success,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);
