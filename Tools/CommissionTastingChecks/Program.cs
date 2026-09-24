using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length == 0)
    throw new ArgumentException("Indica al menos una base SQLite existente. Cada una se copia antes de migrarla.");

foreach (var suppliedPath in args)
    await CheckAsync(Path.GetFullPath(suppliedPath));

static async Task CheckAsync(string sourcePath)
{
    if (!File.Exists(sourcePath)) throw new FileNotFoundException("No existe la base SQLite indicada.", sourcePath);

    var label = sourcePath.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        ? "Release"
        : sourcePath.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Debug"
            : "SQLite";
    var copiedPath = Path.Combine(Path.GetTempPath(), "ControlTaxiTasting", $"{label}-{Guid.NewGuid():N}", "ControlTaxi.db");
    Directory.CreateDirectory(Path.GetDirectoryName(copiedPath)!);
    await using (var source = Open(sourcePath, readOnly: true))
    await using (var copy = Open(copiedPath))
        source.BackupDatabase(copy);
    Console.WriteLine($"COPY: {label} -> {copiedPath}");

    await using var inspection = Open(copiedPath);
    var beforeRules = await SnapshotAsync(inspection, "CommissionSettingsRules");
    var beforeAudit = await SnapshotAsync(inspection, "CommissionSettingsAudit");
    var maxRuleId = await ScalarLongAsync(inspection, "SELECT COALESCE(MAX(Id),0) FROM CommissionSettingsRules;");
    var maxAuditId = await ScalarLongAsync(inspection, "SELECT COALESCE(MAX(Id),0) FROM CommissionSettingsAudit;");
    var payoutBefore = await SnapshotSelectedAsync(inspection,
        "SELECT Id,CvPayoutOneToFourAdults,CvPayoutFiveOrMoreAdults FROM CommissionSettingsRules ORDER BY Id;",
        allowMissingPayoutColumns: true);

    var repository = new CommissionSettingsRepository(new LocalDatabase(copiedPath));
    await repository.InitializeSchemaAsync();
    Assert(await HasColumnAsync(inspection, "CommissionSettingsRules", "CvTastingPercent"), $"{label}: migration adds CvTastingPercent");
    Assert(beforeRules.Hash == (await SnapshotAsync(inspection, "CommissionSettingsRules", beforeRules.Columns)).Hash,
        $"{label}: migration preserves every existing rule value and Id");
    Assert(beforeAudit.Hash == (await SnapshotAsync(inspection, "CommissionSettingsAudit", beforeAudit.Columns)).Hash,
        $"{label}: migration preserves audit history");
    if (payoutBefore != "MISSING")
        Assert(payoutBefore == await SnapshotSelectedAsync(inspection,
                "SELECT Id,CvPayoutOneToFourAdults,CvPayoutFiveOrMoreAdults FROM CommissionSettingsRules ORDER BY Id;"),
            $"{label}: existing CV payout bands remain intact");
    Assert(await ScalarLongAsync(inspection, $"SELECT COUNT(*) FROM CommissionSettingsRules WHERE Id <= {maxRuleId} AND CvTastingPercent IS NOT NULL;") == 0,
        $"{label}: existing rules migrate as tasting not configured");

    await repository.InitializeSchemaAsync();
    Assert(beforeRules.Hash == (await SnapshotAsync(inspection, "CommissionSettingsRules", beforeRules.Columns)).Hash,
        $"{label}: migration is idempotent");
    Assert(await ScalarTextAsync(inspection, "PRAGMA integrity_check;") == "ok", $"{label}: SQLite integrity check");

    var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    var code = "DEGUSTACION CHECK " + suffix;
    var fixture = new CommissionSettingsRule(
        0, "TRANSPORTE", code, code, 0m, 0m, 0m, 0m, string.Empty,
        true, true, false, DateTime.Today, null, string.Empty, "CHECK_DEGUSTACION",
        "Regla desechable creada únicamente en una copia temporal.")
    {
        Branch = "CV",
        CvPayoutOneToFourAdults = 50m,
        CvPayoutFiveOrMoreAdults = 100m,
        CvTastingPercent = null
    };

    await repository.SaveRuleAsync(fixture, "CHECK_DEGUSTACION", "Prueba de Degustación sin configurar", true);
    var stored = await ReloadAsync(repository, code);
    Assert(stored.CvTastingPercent is null && stored.CvTastingDisplay == "Pendiente",
        $"{label}: null persists and displays as Pendiente");
    Assert(stored.CvPayoutOneToFourAdults == 50m && stored.CvPayoutFiveOrMoreAdults == 100m,
        $"{label}: creating tasting fixture preserves both CV payout values");
    Assert(await AuditContainsAsync(inspection, code, "Degustacion=PENDIENTE"),
        $"{label}: audit records tasting as pending on create");

    await repository.UpdateRuleAsync(stored with { CvTastingPercent = 0m }, "CHECK_DEGUSTACION", "Prueba Degustación 0%", true);
    stored = await ReloadAsync(repository, code);
    Assert(stored.CvTastingPercent == 0m && stored.CvTastingDisplay == "0%",
        $"{label}: explicit 0% persists separately from null");
    Assert(await AuditContainsAsync(inspection, code, "Degustacion=0"), $"{label}: audit records explicit 0%");

    await repository.UpdateRuleAsync(stored with { CvTastingPercent = 8m }, "CHECK_DEGUSTACION", "Prueba Degustación 8%", true);
    stored = await ReloadAsync(repository, code);
    Assert(stored.CvTastingPercent == 8m && stored.CvTastingDisplay == "8%",
        $"{label}: 8% persists and reloads");
    Assert(await AuditContainsAsync(inspection, code, "Degustacion=8"), $"{label}: audit records 8%");

    await repository.UpdateRuleAsync(stored with { CvTastingPercent = 12.5m }, "CHECK_DEGUSTACION", "Edición de Degustación", true);
    stored = await ReloadAsync(repository, code);
    Assert(stored.CvTastingPercent == 12.5m && stored.CvTastingDisplay == "12.5%",
        $"{label}: edited percentage persists");
    Assert(stored.CvPayoutOneToFourAdults == 50m && stored.CvPayoutFiveOrMoreAdults == 100m,
        $"{label}: tasting edits do not alter CV payout bands");
    Assert(await AuditContainsAsync(inspection, code, "Degustacion=12.5"), $"{label}: audit records percentage edit");

    var dollarFixture = stored with { CvPayoutOneToFourAdults = 50m, CvPayoutFiveOrMoreAdults = 100m };
    Assert(dollarFixture.CvPayoutOneToFourDisplay.StartsWith('$')
           && dollarFixture.CvPayoutOneToFourDisplay.Contains("50", StringComparison.Ordinal)
           && dollarFixture.CvPayoutFiveOrMoreDisplay.StartsWith('$')
           && dollarFixture.CvPayoutFiveOrMoreDisplay.Contains("100", StringComparison.Ordinal),
        $"{label}: explicit dollar display remains intact");

    var p28 = (await repository.GetRulesAsync("TRANSPORTE", sessionBranch: "P28")).OrderBy(x => x.Code).ToArray();
    var expectedP28 = HardcodedTransportCatalog.AsRules().OrderBy(x => x.Code).ToArray();
    Assert(p28.SequenceEqual(expectedP28), $"{label}: Plaza 28 catalog remains unchanged");
    Assert(beforeRules.Hash == (await SnapshotAsync(inspection, "CommissionSettingsRules", beforeRules.Columns, $"Id <= {maxRuleId}")).Hash,
        $"{label}: all rules that existed before the check remain intact");
    Assert(beforeAudit.Hash == (await SnapshotAsync(inspection, "CommissionSettingsAudit", beforeAudit.Columns, $"Id <= {maxAuditId}")).Hash,
        $"{label}: all prior audit rows remain intact");
    Assert(await ScalarTextAsync(inspection, "PRAGMA integrity_check;") == "ok", $"{label}: final SQLite integrity check");
    Console.WriteLine($"PASS: {label} completed on disposable copy only; source was not opened for writing.");
}

static async Task<CommissionSettingsRule> ReloadAsync(CommissionSettingsRepository repository, string code) =>
    (await repository.GetRulesAsync("TRANSPORTE", code, sessionBranch: "CV")).Single(x => x.Code == code);

static async Task<bool> AuditContainsAsync(SqliteConnection connection, string code, string text)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM CommissionSettingsAudit WHERE Code=$code AND NewValue LIKE $value;";
    command.Parameters.AddWithValue("$code", code);
    command.Parameters.AddWithValue("$value", "%" + text + "%");
    return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
}

static async Task<bool> HasColumnAsync(SqliteConnection connection, string table, string column)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name=$column;";
    command.Parameters.AddWithValue("$column", column);
    return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
}

static async Task<(string Hash, string[] Columns)> SnapshotAsync(
    SqliteConnection connection,
    string table,
    string[]? columns = null,
    string where = "1=1")
{
    if (table is not ("CommissionSettingsRules" or "CommissionSettingsAudit"))
        throw new ArgumentException("Only commission data is allowed.", nameof(table));
    if (columns is null)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await pragma.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync()) names.Add(reader.GetString(1));
        columns = names.ToArray();
    }
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT {string.Join(',', columns.Select(x => $"\"{x}\""))} FROM {table} WHERE {where} ORDER BY Id;";
    await using var rows = await command.ExecuteReaderAsync();
    var values = new List<object[]>();
    while (await rows.ReadAsync())
    {
        var row = new object[rows.FieldCount];
        rows.GetValues(row);
        values.Add(row);
    }
    return (Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values))), columns);
}

static async Task<string> SnapshotSelectedAsync(SqliteConnection connection, string sql, bool allowMissingPayoutColumns = false)
{
    if (allowMissingPayoutColumns
        && (!await HasColumnAsync(connection, "CommissionSettingsRules", "CvPayoutOneToFourAdults")
            || !await HasColumnAsync(connection, "CommissionSettingsRules", "CvPayoutFiveOrMoreAdults")))
        return "MISSING";
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await using var reader = await command.ExecuteReaderAsync();
    var values = new List<object[]>();
    while (await reader.ReadAsync())
    {
        var row = new object[reader.FieldCount];
        reader.GetValues(row);
        values.Add(row);
    }
    return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
}

static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql) =>
    Convert.ToInt64(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture);

static async Task<string> ScalarTextAsync(SqliteConnection connection, string sql) =>
    Convert.ToString(await ScalarAsync(connection, sql), CultureInfo.InvariantCulture) ?? string.Empty;

static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return await command.ExecuteScalarAsync();
}

static SqliteConnection Open(string path, bool readOnly = false)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
    }.ToString());
    connection.Open();
    return connection;
}

static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}
