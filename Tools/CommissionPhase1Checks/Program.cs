using Microsoft.Data.Sqlite;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

if (args.Length == 3 && args[0] == "backup")
{
    Backup(args[1], args[2]);
    Console.WriteLine($"SQLite backup completed: {Path.GetFullPath(args[2])}");
    return;
}
if (args.Length != 2 || args[0] != "check") throw new ArgumentException("Usage: backup source destination | check backup");
var backupPath = Path.GetFullPath(args[1]);
var backupHash = SHA256.HashData(File.ReadAllBytes(backupPath));
var database = new LocalDatabase(true);
Backup(backupPath, database.TestPath);
// Invoke only the commission migration on the disposable copy; never query user/credential tables.
using (var connection = Open(database.TestPath))
{
    var before = await Snapshot(connection);
    var indexes = await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE tbl_name='CommissionSettingsRules' AND type='index';");
    var migrate = typeof(CommissionSettingsRepository).GetMethod("MigrateAddBranchAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
    await (Task)migrate.Invoke(null, new object[] { connection })!;
    Assert(before == await Snapshot(connection), "Migration preserves rule values and IDs");
    Assert(indexes == await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE tbl_name='CommissionSettingsRules' AND type='index';"), "Migration preserves indexes");
    await (Task)migrate.Invoke(null, new object[] { connection })!;
    Assert(before == await Snapshot(connection), "Migration is idempotent");
    Assert(await Scalar(connection, "PRAGMA integrity_check;") == "ok", "SQLite integrity");
    using var partial = new SqliteConnection("Data Source=:memory:");
    partial.Open();
    var schema = await Scalar(connection, "SELECT sql FROM sqlite_master WHERE name='CommissionSettingsRules' AND type='table';");
    schema = System.Text.RegularExpressions.Regex.Replace(schema,
        @"UNIQUE\s*\(Category,\s*Code,\s*EffectiveFrom,\s*Branch\)", "UNIQUE(Category, Code, EffectiveFrom)");
    await Scalar(partial, schema);
    await Scalar(partial, "INSERT INTO CommissionSettingsRules(Category,Code,Name,EffectiveFrom,UpdatedAt,Branch) VALUES('TRANSPORTE','PARTIAL','PARTIAL','2026-01-01','2026-01-01','CV'); CREATE INDEX fixture_index ON CommissionSettingsRules(Code); CREATE TABLE CommissionSettingsRules_new(Id INTEGER);");
    var partialBefore = await Snapshot(partial);
    try { await (Task)migrate.Invoke(null, new object[] { partial })!; throw new Exception("Expected migration failure"); }
    catch (SqliteException) { }
    Assert(partialBefore == await Snapshot(partial), "Failed migration leaves original rules intact");
    await Scalar(partial, "DROP TABLE CommissionSettingsRules_new;");
    await (Task)migrate.Invoke(null, new object[] { partial })!;
    Assert(partialBefore == await Snapshot(partial) && await Scalar(partial, "SELECT Branch FROM CommissionSettingsRules;") == "CV", "Partial migration preserves existing CV branch");
    await Scalar(partial, "INSERT INTO CommissionSettingsRules(Category,Code,Name,EffectiveFrom,UpdatedAt,Branch) VALUES('TRANSPORTE','PARTIAL','PARTIAL','2026-01-01','2026-01-01','');");
    Assert(await Scalar(partial, "SELECT COUNT(*) FROM CommissionSettingsRules;") == "2", "Uniqueness is scoped by branch");
    using var legacy = new SqliteConnection("Data Source=:memory:");
    legacy.Open();
    await Scalar(legacy, schema.Replace("Branch TEXT NOT NULL DEFAULT '',", ""));
    await Scalar(legacy, "INSERT INTO CommissionSettingsRules(Category,Code,Name,EffectiveFrom,UpdatedAt) VALUES('TRANSPORTE','LEGACY','LEGACY','2026-01-01','2026-01-01');");
    var legacyBefore = await Snapshot(legacy);
    await (Task)migrate.Invoke(null, new object[] { legacy })!;
    Assert(legacyBefore == await Snapshot(legacy) && await Scalar(legacy, "SELECT Branch FROM CommissionSettingsRules;") == "", "Legacy schema gains Branch without claiming CV ownership");
}
await database.InitializeAsync();
var settings = new CommissionSettingsRepository(database);
await settings.InitializeAsync();
// Remove only rules in the disposable fixture, so real CV data cannot affect assertions.
using (var connection = database.Open())
{
    await Scalar(connection, "DELETE FROM CommissionSettingsRules WHERE Branch='CV';");
}
Assert((await settings.GetRulesAsync("TRANSPORTE", sessionBranch: "CV")).Count == 0, "CV never inherits legacy/fixed transports");
Assert((await settings.GetSummaryAsync("CV")).TransportRules == 0, "CV counter excludes legacy transports");
Assert((await settings.GetRulesAsync("FORMA_PAGO", sessionBranch: "CV")).Count > 0, "Shared payment catalog remains available");
var fixedRules = HardcodedTransportCatalog.AsRules().OrderBy(r => r.Code).ToArray();
var p28 = (await settings.GetRulesAsync("TRANSPORTE", sessionBranch: "P28")).OrderBy(r => r.Code).ToArray();
Assert(p28.SequenceEqual(fixedRules), "P28 fixed catalog unchanged, without SQLite duplicates");
var date = new DateTime(2026, 9, 24);
var input = new CommissionSimulationInput(date, "PHASE1-CV", 1000m, 1000m, "Efectivo", 0m, 0m, 0m, 100m, 20m, "", AdultCount: 2);
var missing = await settings.SimulateAsync(input, "CV");
Assert(!missing.Configured && missing.FinalCommission == 0 && missing.Source == "SIN_CONFIGURACION", "Missing CV rule produces no fallback");
var resolver = new CommissionConfigurationResolver(settings, "CV");
var fallback = await resolver.ResolveTransportAsync("MAJESTIC", date, 99m, 99m, 99m, 99m);
Assert(!fallback.Configured && fallback.CommissionPercent == 0 && fallback.CardRetentionPercent == 0, "CV ignores supplied legacy percentages");
var rule = new CommissionSettingsRule(0, "TRANSPORTE", "PHASE1-CV", "PHASE1-CV", 13m, 2m, 7m, 11m, "", true, true, true,
    date.AddDays(-1), date.AddDays(1), "", "CHECK", "isolated fixture") { Branch = "CV", CvPayoutOneToFourAdults = 100m, CvPayoutFiveOrMoreAdults = 100m };
await settings.SaveRuleAsync(rule, "CHECK", "Isolated CV fixture", true);
rule = (await settings.GetRulesAsync("TRANSPORTE", "PHASE1-CV", sessionBranch: "CV")).Single();
Assert(rule.Branch == "CV", "Create persists CV branch");
Assert((await settings.GetSummaryAsync("CV")).TransportRules == 1, "CV counter agrees with list");
var cash = await settings.SimulateAsync(input, "CV");
Assert(cash.Configured && cash.FinalCommission == 111m && cash.RetentionPercent == 2m, "CV cash uses SQLite commission and retention");
var card = await settings.SimulateAsync(input with { PaymentMethod = "Tarjeta" }, "CV");
Assert(card.FinalCommission == 105m && card.RetentionPercent == 7m, "CV card uses SQLite retention");
var amex = await settings.SimulateAsync(input with { PaymentMethod = "AMEX" }, "CV");
Assert(amex.FinalCommission == 100m && amex.RetentionPercent == 11m, "CV AMEX uses SQLite retention");
Assert(!(await settings.SimulateAsync(input with { TransportCodeOrName = "PHASE1" }, "CV")).Configured, "Substring is not a CV rule match");
Assert(!(await settings.SimulateAsync(input with { Date = date.AddDays(2) }, "CV")).Configured, "Expired CV rule cannot calculate");
await settings.UpdateRuleAsync(rule with { AppliesPayout = false, AppliesExpense = false }, "CHECK", "Flags fixture", true);
Assert((await settings.SimulateAsync(input, "CV")).FinalCommission == 127m, "CV deduction flags respected");
await settings.UpdateRuleAsync(rule with { Active = false }, "CHECK", "Inactive fixture", true);
Assert(!(await settings.SimulateAsync(input, "CV")).Configured, "Inactive CV rule cannot calculate");
await settings.UpdateRuleAsync(rule with { CommissionPercent = 0m }, "CHECK", "Zero fixture", true);
var zero = await settings.SimulateAsync(input, "CV");
Assert(zero.Configured && zero.FinalCommission == 0m, "Explicit zero percent is valid and never replaced");
await settings.SaveRuleAsync(rule with { Id = 0, Code = "PHASE1-AMBIGUOUS" }, "CHECK", "Ambiguous fixture", true);
Assert(!(await settings.SimulateAsync(input, "CV")).Configured, "Ambiguous exact matches cannot calculate");
try { await settings.UpdateRuleAsync(rule with { Branch = "P28" }, "CHECK", "Cross branch fixture", true); throw new Exception("Expected rejection"); }
catch (UnauthorizedAccessException) { Console.WriteLine("PASS: cross-branch transport edit rejected"); }
Assert((await settings.GetRulesAsync("TRANSPORTE", sessionBranch: "P28")).OrderBy(r => r.Code).SequenceEqual(fixedRules), "P28 remains unchanged after CV edits");
Assert(backupHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backupPath))), "Original backup unchanged by checks");
Console.WriteLine($"PASS: disposable migrated database: {database.TestPath}");

static SqliteConnection Open(string path, bool readOnly = false)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate }.ToString());
    connection.Open();
    return connection;
}
static void Backup(string source, string destination)
{
    if (File.Exists(destination)) throw new IOException("Destination already exists.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
    using var original = Open(source, true);
    using var copy = Open(destination);
    original.BackupDatabase(copy);
}
static async Task<string> Scalar(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
}
static async Task<string> Snapshot(SqliteConnection connection)
{
    using var command = connection.CreateCommand();
    command.CommandText = "SELECT Id,Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,PaymentKind,AppliesPayout,PayoutAmount,PaxKind,MonedaId,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes FROM CommissionSettingsRules ORDER BY Id;";
    using var reader = await command.ExecuteReaderAsync();
    var rows = new List<object[]>();
    while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(values); }
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(rows))));
}
static void Assert(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}
