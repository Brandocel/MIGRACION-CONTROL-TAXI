using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length < 3 || (args[0] != "check" && args[0] != "import"))
    throw new ArgumentException("check backup.xlsx.db source.xlsx | import existing.db source.xlsx backup.db");
var source = Path.GetFullPath(args[2]);
var tariff = ReadBike(source);
var suppliedDb = Path.GetFullPath(args[1]);
var path = suppliedDb;
if (args[0] == "check")
{
    path = Path.Combine(Path.GetTempPath(), "ControlTaxiPhase2", Guid.NewGuid().ToString("N"), "ControlTaxi.db");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var original = Open(suppliedDb, true);
    using var copy = Open(path);
    original.BackupDatabase(copy);
}
else
{
    if (args.Length != 4 || !File.Exists(args[3])) throw new ArgumentException("An existing pre-migration backup is required.");
    using var original = Open(suppliedDb, true);
    using var backup = Open(args[3], true);
    var originalRules = await Snapshot(original, "CommissionSettingsRules");
    Assert(originalRules.Hash == (await Snapshot(backup, "CommissionSettingsRules", originalRules.Columns)).Hash, "Backup matches rules before import");
}
var database = new LocalDatabase(path);
var repository = new CommissionSettingsRepository(database);
using var inspection = Open(path);
var before = await Snapshot(inspection, "CommissionSettingsRules");
var historyBefore = await Snapshot(inspection, "CommissionSettingsAudit");
await repository.InitializeSchemaAsync();
Assert(before.Hash == (await Snapshot(inspection, "CommissionSettingsRules", before.Columns)).Hash, "Migration preserves rules and IDs");
Assert(historyBefore.Hash == (await Snapshot(inspection, "CommissionSettingsAudit", historyBefore.Columns)).Hash, "Migration preserves historical audit");
await repository.InitializeSchemaAsync();
Assert(before.Hash == (await Snapshot(inspection, "CommissionSettingsRules", before.Columns)).Hash, "Migration is idempotent");
Assert(await Scalar(inspection, "PRAGMA integrity_check;") == "ok", "SQLite integrity");
var date = DateTime.Today;
var draft = new CommissionSettingsRule(0, "TRANSPORTE", "BIKE CID", "BIKE CID", 0m, 0m, 0m, 0m, "", true, true, false,
    date, null, "", "FASE2_DEJADA", $"SOLO DEJADAS. Comision y retenciones pendientes de confirmar; ficha inactiva. {Path.GetFileName(source)} DEJADA!A12:C15. SHA256={tariff.Hash}")
{ Branch = "CV", CvPayoutOneToFourAdults = tariff.Small, CvPayoutFiveOrMoreAdults = tariff.Large };
var ruleCount = await Scalar(inspection, "SELECT COUNT(*) FROM CommissionSettingsRules;");
await repository.SaveCvPayoutDraftAsync(draft, "FASE2_DEJADA", "Carga de BIKE CID verificada en DEJADA!B14:C15; TAXI pendiente de correspondencia. Sin importar porcentajes ni otras hojas.");
Assert(before.Hash == (await Snapshot(inspection, "CommissionSettingsRules", before.Columns, "NOT (Category='TRANSPORTE' AND Branch='CV' AND Code='BIKE CID')")).Hash, "Import preserves P28 and all previous rules");
Assert(historyBefore.Hash == (await Snapshot(inspection, "CommissionSettingsAudit", historyBefore.Columns, "NOT (Category='TRANSPORTE' AND Branch='CV' AND Code='BIKE CID')")).Hash, "Import preserves previous history");
Assert(await Scalar(inspection, "SELECT COUNT(*) FROM CommissionSettingsRules;") == (int.Parse(ruleCount) + 1).ToString(), "Only one new draft is imported");
Assert(await Scalar(inspection, "SELECT COUNT(*) FROM CommissionSettingsRules WHERE Category='TRANSPORTE' AND Branch='CV' AND Code LIKE '%TAXI%';") == "0", "No ambiguous TAXI mapping imported");
var id = long.Parse(await Scalar(inspection, "SELECT Id FROM CommissionSettingsRules WHERE Category='TRANSPORTE' AND Branch='CV' AND Code='BIKE CID';"));
Assert(await Scalar(inspection, $"SELECT CvPayoutOneToFourAdults || '/' || CvPayoutFiveOrMoreAdults || '/' || Active FROM CommissionSettingsRules WHERE Id={id};") == "50.0/100.0/0", "Stored BIKE CID: 50 / 100, inactive");
Console.WriteLine($"STORED: {path}; rule Id={id}; CV BIKE CID 1-4=50, 5+=100; INACTIVE, commission pending.");
if (args[0] == "import") return;
try { await repository.SaveCvPayoutDraftAsync(draft, "CHECK", "Repeated import"); throw new Exception("Expected duplicate guard"); }
catch (InvalidOperationException ex) when (ex.Message.Contains("Ya existe")) { Console.WriteLine("PASS: repeated import cannot overwrite rates/history"); }

var resolver = new CommissionConfigurationResolver(repository, "CV");
var fixture = draft with { Id = id, Active = true, CommissionPercent = 10m, Notes = "TEST ONLY: commission fixture, not imported from Excel" };
await repository.UpdateRuleAsync(fixture, "CHECK", "Activate only the disposable test fixture", true);
foreach (var (adults, expectedPayout, expectedCommission) in new[] { (2, 50m, 95m), (4, 50m, 95m), (5, 100m, 90m) })
{
    var input = new CommissionSimulationInput(date, "BIKE CID", 1000m, 1000m, "Efectivo", 0m, 0m, 0m, 999m, 0m, "", adults);
    var result = await resolver.SimulateAsync(input);
    Assert(result.Configured && result.Payout == expectedPayout && result.FinalCommission == expectedCommission, $"Simulator: {adults} adults => payout {expectedPayout}, commission {expectedCommission}; ignores manual/legacy payout 999");
    var relation = new LocalRelation(1, "TEST", "TEST", "", "", "", "", 999m, "", TransportType: "BIKE CID", Sale: 1000m, PaymentMethod: "Efectivo", Passengers: adults, AdultPassengers: adults)
        { CommissionAdultCount = CascoPayoutRules.ReadAdultCount($"{{\"adultCount\":{adults}}}") };
    var actual = await resolver.SimulateAsync(CascoPayoutRules.FromRelation(relation, date));
    Assert(actual.Payout == expectedPayout && actual.FinalCommission == expectedCommission, $"Operations mapping: {adults} adults");
    var record = Record($"{{\"adultCount\":{adults}}}", 30);
    var preview = await resolver.SimulateAsync(CascoPayoutRules.FromRecords(new[] { record }, date, "BIKE CID", 1000m, "Efectivo", 999m, 0m));
    Assert(preview.Payout == expectedPayout && preview.FinalCommission == expectedCommission, $"Preview/generation mapping: {adults} adults, PAX=30 ignored");
}
var withMinors = new LocalRelation(1, "TEST", "TEST", "", "", "", "", 999m, "", TransportType: "BIKE CID", Sale: 1000m,
    PaymentMethod: "Efectivo", Passengers: 12, AdultPassengers: 2, ChildPassengers: 10)
    { CommissionAdultCount = CascoPayoutRules.ReadAdultCount("{\"adultCount\":2,\"minorCount\":10,\"youthCount\":0}") };
Assert((await resolver.SimulateAsync(CascoPayoutRules.FromRelation(withMinors, date))).Payout == 50m, "2 adults + 10 minors => 50, never the 5+ band");
foreach (var adults in new int?[] { null, 0 })
{
    var pending = await resolver.SimulateAsync(new(date, "BIKE CID", 1000m, 1000m, "Efectivo", 0m, 0m, 0m, 999m, 0m, "", adults));
    Assert(!pending.Configured && pending.FinalCommission == 0m && pending.Source == "DATOS_PENDIENTES_CV", $"Adults {adults?.ToString() ?? "absent"}: no assumed tariff or commission");
}
foreach (var json in new[] { "{}", "{\"pax\":12,\"minorCount\":10,\"youthCount\":2}", "{\"adultCount\":null}", "{\"adultCount\":2.5}", "{broken" })
    Assert(CascoPayoutRules.ReadAdultCount(json) is null, "Missing/invalid adults are not inferred from other passenger fields");
Assert(CascoPayoutRules.ReadAdultCount("{\"adultCount\":\"5\"}") == 5, "Explicit integer-string adultCount supported");
Assert(CascoPayoutRules.ConsistentAdults(new int?[] { 2, 5 }) is null && CascoPayoutRules.ConsistentAdults(new int?[] { 2, null }) is null, "Conflicting/incomplete grouped adults remain pending");
var conflictingPreview = CascoPayoutRules.FromRecords(new[] { Record("{\"adultCount\":2}", 2), Record("{\"adultCount\":5}", 5) }, date, "BIKE CID", 1000m, "Efectivo", 999m, 0m);
Assert(!(await resolver.SimulateAsync(conflictingPreview)).Configured, "Preview/generation rejects conflicting adults across operation rows");
var legacy = withMinors with { CommissionAdultCount = null, AdultPassengers = 12 };
Assert(!(await resolver.SimulateAsync(CascoPayoutRules.FromRelation(legacy, date))).Configured, "Operations cannot fall back to AdultPassengers display or PAX when provenance is absent");
await repository.UpdateRuleAsync(fixture with { CvPayoutFiveOrMoreAdults = 120m }, "CHECK", "Edit large tariff", true);
var reloaded = (await repository.GetRulesAsync("TRANSPORTE", "BIKE CID", sessionBranch: "CV")).Single();
Assert(reloaded.CvPayoutOneToFourAdults == 50m && reloaded.CvPayoutFiveOrMoreAdults == 120m, "Both editable amounts persist/reload independently");
Assert(await Scalar(inspection, "SELECT COUNT(*) FROM CommissionSettingsAudit WHERE Code='BIKE CID' AND NewValue LIKE '%Dejada5mas=120%';") == "1", "Tariff edit recorded in history");
await repository.UpdateRuleAsync(fixture with { CvPayoutFiveOrMoreAdults = null }, "CHECK", "Unset tariff", true);
Assert(!(await resolver.SimulateAsync(new(date, "BIKE CID", 1000m, 1000m, "Efectivo", 0m, 0m, 0m, 0m, 0m, "", 5))).Configured, "Missing 5+ tariff does not fall back to 1-4");
await repository.UpdateRuleAsync(fixture with { CvPayoutOneToFourAdults = 0m }, "CHECK", "Explicit free payout", true);
Assert((await resolver.SimulateAsync(new(date, "BIKE CID", 1000m, 1000m, "Efectivo", 0m, 0m, 0m, 100m, 0m, "", 2))).Payout == 0m, "Zero tariff is preserved as explicit free payout");
try { await repository.UpdateRuleAsync(fixture with { CvPayoutOneToFourAdults = -1m }, "CHECK", "Negative fixture", true); throw new Exception("Expected validation"); }
catch (InvalidOperationException ex) when (ex.Message.Contains("negativas")) { Console.WriteLine("PASS: negative tariff rejected"); }
var fixedCatalog = HardcodedTransportCatalog.AsRules().OrderBy(x => x.Code).ToArray();
Assert((await repository.GetRulesAsync("TRANSPORTE", sessionBranch: "P28")).OrderBy(x => x.Code).SequenceEqual(fixedCatalog), "P28 catalog is unchanged");
var p28Resolver = new CommissionConfigurationResolver(repository, "P28");
var p28Input = new CommissionSimulationInput(date, fixedCatalog.First().Name, 1000m, 1000m, "Efectivo", 1000m, 0m, 0m, 123m, 0m, "");
var p28 = await p28Resolver.SimulateAsync(p28Input);
var p28Adults = await p28Resolver.SimulateAsync(p28Input with { AdultCount = 5 });
Assert(p28 == p28Adults && p28.Payout == 123m, "P28 calculation still uses its supplied payout; CV adult bands have no effect");
Console.WriteLine($"ALL CHECKS PASSED on disposable copy: {path}");

static CascoAppRecordDetail Record(string json, int pax) => new("TEST", "TEST", "", "", "", "", "2026-09-24", "BIKE CID", 1000m,
    "", "", "", "", "", "", "", "", "", "", "", "", "", "", 1000m, 0m, pax, DetailJson: json);
static SqliteConnection Open(string path, bool readOnly = false)
{
    var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite }.ToString());
    if (!readOnly && !File.Exists(path)) c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path) }.ToString());
    c.Open(); return c;
}
static async Task<string> Scalar(SqliteConnection c, string sql)
{
    using var command = c.CreateCommand(); command.CommandText = sql;
    return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
}
static async Task<(string Hash, string[] Columns)> Snapshot(SqliteConnection c, string table, string[]? columns = null, string where = "1=1")
{
    if (table != "CommissionSettingsRules" && table != "CommissionSettingsAudit") throw new ArgumentException("Only commission data allowed");
    if (columns is null)
    {
        using var pragma = c.CreateCommand(); pragma.CommandText = $"PRAGMA table_info({table});";
        using var r = await pragma.ExecuteReaderAsync(); var names = new List<string>();
        while (await r.ReadAsync()) names.Add(r.GetString(1)); columns = names.ToArray();
    }
    using var command = c.CreateCommand(); command.CommandText = $"SELECT {string.Join(",", columns.Select(x => "\"" + x + "\""))} FROM {table} WHERE {where} ORDER BY Id;";
    using var reader = await command.ExecuteReaderAsync(); var rows = new List<object[]>();
    while (await reader.ReadAsync()) { var row = new object[reader.FieldCount]; reader.GetValues(row); rows.Add(row); }
    return (Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows))), columns);
}
static (decimal Small, decimal Large, string Hash) ReadBike(string path)
{
    using var zip = ZipFile.OpenRead(path);
    XDocument Xml(string name) { using var stream = zip.GetEntry(name)!.Open(); return XDocument.Load(stream); }
    XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    var sheet = Xml("xl/workbook.xml").Descendants(ns + "sheet").Single(x => (string?)x.Attribute("name") == "DEJADA");
    var target = (string)Xml("xl/_rels/workbook.xml.rels").Root!.Elements().Single(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(rel + "id")).Attribute("Target")!;
    var strings = Xml("xl/sharedStrings.xml").Descendants(ns + "si").Select(x => string.Concat(x.Descendants(ns + "t").Select(t => t.Value))).ToArray();
    var cells = Xml(target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target).Descendants(ns + "c").ToDictionary(x => (string)x.Attribute("r")!);
    string Cell(string address)
    {
        var cell = cells[address]; if (cell.Element(ns + "f") is not null) throw new InvalidOperationException("Unexpected formula; review source");
        var value = cell.Element(ns + "v")?.Value ?? string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
        return (string?)cell.Attribute("t") == "s" ? strings[int.Parse(value)] : value;
    }
    Assert(Cell("A12") == "BIKE CID" && Cell("A14") == "BIKE CID" && Cell("B13") == "ADULTOS" && Cell("B14") == "1 A 4" && Cell("B15") == "5 O MAS", "Exact Excel labels/bands verified in DEJADA only");
    var small = decimal.Parse(Cell("C14"), CultureInfo.InvariantCulture); var large = decimal.Parse(Cell("C15"), CultureInfo.InvariantCulture);
    Assert(small == 50m && large == 100m, "Excel BIKE CID amounts verified: C14=50, C15=100");
    return (small, large, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}
static void Assert(bool value, string label)
{
    if (!value) throw new InvalidOperationException("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
}
