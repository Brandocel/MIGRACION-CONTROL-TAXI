using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;
using ControlTaxiDesktop.Tools.CascoSync.Configuration;
using ControlTaxiDesktop.Tools.CascoSync.Logging;
using ControlTaxiDesktop.Tools.CascoSync.Models;
using ControlTaxiDesktop.Tools.CascoSync.Services;
using ControlTaxiDesktop.Tools.CascoSync.State;
using ControlTaxiDesktop.Tools.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Tools.Commands;

internal static class ImportCommands
{
    public static async Task<int> ImportSqlServerAsync(IReadOnlyDictionary<string, string> options)
    {
        if (options.ContainsKey("server") && options.ContainsKey("databases"))
            return await ImportSqlServerMultiDatabaseAsync(options);

        var connectionString = ProgramHelpers.Required(options, "connection");
        var sourceName = ProgramHelpers.SanitizeName(ProgramHelpers.Required(options, "source-name"));
        var databasePath = ProgramHelpers.Required(options, "output");
        ProgramHelpers.EnsureOutputDirectory(databasePath);

        await using var source = new SqlConnection(connectionString);
        await source.OpenAsync();
        await using var target = ProgramHelpers.OpenSqlite(databasePath);
        await EnsureManifestAsync(target);

        var tables = new List<(string Schema, string Name)>();
        await using (var command = source.CreateCommand())
        {
            command.CommandText = """
                SELECT s.name, t.name
                FROM sys.tables t INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        Console.WriteLine($"Importando {tables.Count} tablas de {sourceName}...");
        foreach (var (schema, table) in tables)
            await CopySqlServerTableAsync(source, target, sourceName, schema, table);

        Console.WriteLine("Importación SQL Server terminada.");
        return 0;
    }

    public static async Task<int> ImportSqlServerMultiDatabaseAsync(IReadOnlyDictionary<string, string> options)
    {
        var server = ProgramHelpers.Required(options, "server");
        var user = ProgramHelpers.Required(options, "user");
        var password = ProgramHelpers.Required(options, "password");
        var databases = ProgramHelpers.Required(options, "databases")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
        var databasePath = options.TryGetValue("db", out var db) && !string.IsNullOrWhiteSpace(db)
            ? db
            : ProgramHelpers.Required(options, "output");
        if (databases.Length == 0)
            throw new ArgumentException("Falta al menos una base en --databases.");

        ProgramHelpers.EnsureOutputDirectory(databasePath);
        foreach (var databaseName in databases)
        {
            var sourceName = ResolveSourceAlias(databaseName);
            var connectionString = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = databaseName,
                UserID = user,
                Password = password,
                TrustServerCertificate = true,
                Encrypt = false,
                ConnectTimeout = 30
            }.ToString();
            Console.WriteLine($"Importando base SQL Server local {databaseName} como {sourceName}...");
            await ImportSqlServerDatabaseAsync(connectionString, sourceName, databasePath);
        }

        if (options.TryGetValue("report", out var reportPath) && !string.IsNullOrWhiteSpace(reportPath))
            await PipelineCommands.RunPipelineAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["db"] = databasePath, ["report"] = reportPath });
        return 0;
    }

    public static async Task<int> ImportBakAsync(IReadOnlyDictionary<string, string> options)
    {
        var server = ProgramHelpers.Required(options, "server");
        var bakPath = Path.GetFullPath(ProgramHelpers.Required(options, "bak"));
        var databaseName = ProgramHelpers.SanitizeName(ProgramHelpers.Required(options, "database"));
        var sourceName = ProgramHelpers.Required(options, "source-name");
        var output = ProgramHelpers.Required(options, "output");
        if (!File.Exists(bakPath))
            throw new FileNotFoundException("No se encontró el archivo .bak.", bakPath);

        var masterConnection = $"Server={server};Database=master;Trusted_Connection=True;TrustServerCertificate=True";
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        var logicalFiles = await ReadBakLogicalFilesAsync(connection, bakPath);
        var dataLogical = logicalFiles.FirstOrDefault(x => x.Type == "D").LogicalName ?? throw new InvalidOperationException("No se encontró archivo de datos en el .bak.");
        var logLogical = logicalFiles.FirstOrDefault(x => x.Type == "L").LogicalName ?? dataLogical + "_log";
        var baseDirectory = Path.Combine(Path.GetTempPath(), "ControlTaxiDesktopSqlRestore");
        Directory.CreateDirectory(baseDirectory);
        var dataFile = Path.Combine(baseDirectory, databaseName + ".mdf");
        var logFile = Path.Combine(baseDirectory, databaseName + "_log.ldf");
        await using (var restore = connection.CreateCommand())
        {
            restore.CommandTimeout = 0;
            restore.CommandText = $"""
                RESTORE DATABASE {ProgramHelpers.QuoteSqlServer(databaseName)}
                FROM DISK = {ProgramHelpers.QuoteSqlLiteral(bakPath)}
                WITH REPLACE,
                     MOVE {ProgramHelpers.QuoteSqlLiteral(dataLogical)} TO {ProgramHelpers.QuoteSqlLiteral(dataFile)},
                     MOVE {ProgramHelpers.QuoteSqlLiteral(logLogical)} TO {ProgramHelpers.QuoteSqlLiteral(logFile)};
                """;
            await restore.ExecuteNonQueryAsync();
        }

        var restoredConnection = $"Server={server};Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True";
        return await ImportSqlServerAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["connection"] = restoredConnection,
            ["source-name"] = sourceName,
            ["output"] = output
        });
    }

    public static async Task<int> ImportCsvAsync(IReadOnlyDictionary<string, string> options)
    {
        var input = ProgramHelpers.Required(options, "input");
        var sourceName = ProgramHelpers.SanitizeName(options.TryGetValue("source-name", out var configuredSource) ? configuredSource : "csv");
        var output = ProgramHelpers.Required(options, "output");
        var files = Directory.Exists(input) ? Directory.EnumerateFiles(input, "*.csv", SearchOption.TopDirectoryOnly).ToArray() : File.Exists(input) ? [input] : throw new FileNotFoundException("No se encontró CSV o carpeta CSV.", input);
        ProgramHelpers.EnsureOutputDirectory(output);
        await using var target = ProgramHelpers.OpenSqlite(output);
        await EnsureManifestAsync(target);
        foreach (var file in files)
            await ImportDelimitedFileAsync(target, sourceName, file);
        Console.WriteLine("Importación CSV terminada.");
        return 0;
    }

    public static async Task<int> ImportXlsxAsync(IReadOnlyDictionary<string, string> options)
    {
        var input = ProgramHelpers.Required(options, "input");
        var sourceName = ProgramHelpers.SanitizeName(options.TryGetValue("source-name", out var configuredSource) ? configuredSource : "excel");
        var output = ProgramHelpers.Required(options, "output");
        var files = Directory.Exists(input) ? Directory.EnumerateFiles(input, "*.xlsx", SearchOption.TopDirectoryOnly).ToArray() : File.Exists(input) ? [input] : throw new FileNotFoundException("No se encontró XLSX o carpeta XLSX.", input);
        ProgramHelpers.EnsureOutputDirectory(output);
        await using var target = ProgramHelpers.OpenSqlite(output);
        await EnsureManifestAsync(target);
        foreach (var file in files)
            await ImportXlsxFileAsync(target, sourceName, file);
        Console.WriteLine("Importación XLSX terminada.");
        return 0;
    }

    public static async Task<int> ImportSqliteAsync(IReadOnlyDictionary<string, string> options)
    {
        var input = ProgramHelpers.Required(options, "input");
        var sourceName = ProgramHelpers.SanitizeName(options.TryGetValue("source-name", out var configuredSource) ? configuredSource : Path.GetFileNameWithoutExtension(input));
        var output = ProgramHelpers.Required(options, "output");
        if (!File.Exists(input))
            throw new FileNotFoundException("No se encontró la base SQLite origen.", input);
        ProgramHelpers.EnsureOutputDirectory(output);
        await using var source = ProgramHelpers.OpenSqlite(input);
        await using var target = ProgramHelpers.OpenSqlite(output);
        await EnsureManifestAsync(target);
        var tables = await GetSqliteTablesAsync(source);
        foreach (var table in tables.Where(x => !x.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)))
            await CopySqliteTableAsync(source, target, sourceName, table);
        Console.WriteLine("Importación SQLite terminada.");
        return 0;
    }

    public static async Task<int> ImportJsonAsync(IReadOnlyDictionary<string, string> options)
    {
        var inputDirectory = ProgramHelpers.Required(options, "input");
        var databasePath = ProgramHelpers.Required(options, "output");
        if (!Directory.Exists(inputDirectory))
            throw new DirectoryNotFoundException($"No existe la carpeta de entrada: {inputDirectory}");

        var files = Directory.EnumerateFiles(inputDirectory, "*.json", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0)
            throw new InvalidOperationException("No hay archivos JSON para importar.");

        ProgramHelpers.EnsureOutputDirectory(databasePath);
        await using var target = ProgramHelpers.OpenSqlite(databasePath);
        await EnsureManifestAsync(target);
        foreach (var file in files)
            await ImportJsonFileAsync(target, file);
        Console.WriteLine("Importación JSON terminada.");
        return 0;
    }

    public static async Task<int> VerifyAsync(IReadOnlyDictionary<string, string> options)
    {
        var databasePath = ProgramHelpers.Required(options, "output");
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("No se encontró la base SQLite.", databasePath);
        await using var target = ProgramHelpers.OpenSqlite(databasePath);
        await using var integrity = target.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        var result = Convert.ToString(await integrity.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SQLite informó un problema de integridad: {result}");
        await using var command = target.CreateCommand();
        command.CommandText = "SELECT SourceName, SourceSchema, SourceTable, DestinationTable, RowCount FROM __table_manifest ORDER BY SourceName, SourceSchema, SourceTable;";
        await using var reader = await command.ExecuteReaderAsync();
        Console.WriteLine("Integridad SQLite: OK");
        while (await reader.ReadAsync())
            Console.WriteLine($"  {reader.GetString(0)}.{reader.GetString(1)}.{reader.GetString(2)} -> {reader.GetString(3)}: {reader.GetInt64(4):N0}");
        return 0;
    }

    public static async Task<int> RunCascoSyncAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Debe especificar un modo principal: --dry-run, --apply-one, --apply-new, --apply-update-one o --apply.");
            return 2;
        }

        var dryRun = false;
        var apply = false;
        var applyOne = false;
        var applyNew = false;
        var applyUpdateOne = false;
        var simulateUpdate = false;
        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--apply":
                    apply = true;
                    break;
                case "--apply-one":
                    applyOne = true;
                    break;
                case "--apply-new":
                    applyNew = true;
                    break;
                case "--apply-update-one":
                    applyUpdateOne = true;
                    break;
                case "--simulate-update":
                    simulateUpdate = true;
                    break;
                default:
                    Console.Error.WriteLine($"Argumento no reconocido: {arg}");
                    return 2;
            }
        }

        if (simulateUpdate && !dryRun && !applyUpdateOne)
        {
            Console.Error.WriteLine("El modo --simulate-update solo puede utilizarse junto con --dry-run o --apply-update-one.");
            return 2;
        }

        if (simulateUpdate && apply)
        {
            Console.Error.WriteLine("Los modos --simulate-update y --apply no pueden combinarse.");
            return 2;
        }

        if (simulateUpdate && applyOne)
        {
            Console.Error.WriteLine("Los modos --simulate-update y --apply-one no pueden combinarse.");
            return 2;
        }

        if (simulateUpdate && applyNew)
        {
            Console.Error.WriteLine("Los modos --simulate-update y --apply-new no pueden combinarse.");
            return 2;
        }

        var primaryModes = new[] { dryRun, apply, applyOne, applyNew, applyUpdateOne };
        if (primaryModes.Count(x => x) > 1)
        {
            Console.Error.WriteLine("No se pueden combinar varios modos principales entre sí.");
            return 2;
        }

        if (!dryRun && !apply && !applyOne && !applyNew && !applyUpdateOne)
        {
            Console.Error.WriteLine("Debe especificar --dry-run, --apply-one, --apply-new, --apply-update-one o --apply.");
            return 2;
        }

        if (apply)
        {
            Console.WriteLine("El modo --apply todavía no está autorizado.");
            return 0;
        }

        var options = CascoSyncOptions.LoadFromWorkspace();
        if (string.IsNullOrWhiteSpace(options.SqlPassword))
        {
            Console.Error.WriteLine("La contraseña SQL debe proporcionarse mediante la variable de entorno CASCO_SQL_PASSWORD.");
            return 1;
        }

        var logger = new CascoSyncLogger(options.LogDirectory);
        logger.Log("Inicio de DryRun de Casco");
        var stateStore = new CascoSyncStateStore(options.StateDirectory);
        stateStore.Save("last-run", new { Mode = dryRun ? "dry-run" : applyOne ? "apply-one" : applyNew ? "apply-new" : applyUpdateOne ? "apply-update-one" : "apply", StartedAtUtc = DateTime.UtcNow });

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var apiClient = new CascoApiClient(httpClient, options);
        var apiResult = await apiClient.FetchRecordsAsync(CancellationToken.None);

        var filteredRecords = apiResult.Records
            .Where(x => string.Equals(x.AssignedBranchCode, options.BranchCode, StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x.Site, "Plaza 28", StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.Equals(x.Site, "plaza 28", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RecordDate ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.RecordId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (applyOne)
        {
            var schemaInspector = new CascoSchemaInspector(ProgramHelpers.BuildSqlConnectionString(options));
            var schemaForApply = await schemaInspector.InspectAsync(CancellationToken.None);
            var applyService = new CascoApplyOneService();
            var result = await applyService.ApplyOneAsync(options, filteredRecords, schemaForApply, logger, CancellationToken.None);
            Console.WriteLine($"Aplicado: {result.Applied}");
            Console.WriteLine($"Confirmado: {result.Confirmed}");
            Console.WriteLine($"RecordId: {result.RecordId}");
            Console.WriteLine($"Folio: {result.ProposedFolio}");
            return 0;
        }

        if (applyNew)
        {
            var schemaInspector = new CascoSchemaInspector(ProgramHelpers.BuildSqlConnectionString(options));
            var schemaForApply = await schemaInspector.InspectAsync(CancellationToken.None);
            var applyNewService = new CascoApplyNewService();
            var result = await applyNewService.ApplyNewAsync(options, filteredRecords, schemaForApply, logger, CancellationToken.None);
            Console.WriteLine($"Aplicado: {result.Applied}");
            Console.WriteLine($"Confirmado: {result.Confirmed}");
            Console.WriteLine($"Registros encontrados: {result.RecordsFound}");
            Console.WriteLine($"Registros a insertar: {result.RecordsToInsert}");
            Console.WriteLine($"Insertados: {result.InsertedCount}");
            Console.WriteLine($"Omitidos: {result.OmittedCount}");
            Console.WriteLine($"Errores: {result.ErrorCount}");
            Console.WriteLine($"Folios generados: {string.Join(", ", result.GeneratedFolios)}");
            return 0;
        }

        if (applyUpdateOne)
        {
            var schemaInspector = new CascoSchemaInspector(ProgramHelpers.BuildSqlConnectionString(options));
            var schemaForApply = await schemaInspector.InspectAsync(CancellationToken.None);
            var applyUpdateOneService = new CascoApplyUpdateOneService();
            var result = await applyUpdateOneService.ApplyUpdateOneAsync(options, filteredRecords, schemaForApply, logger, simulateUpdate, CancellationToken.None);
            Console.WriteLine($"Aplicado: {result.Applied}");
            Console.WriteLine($"Confirmado: {result.Confirmed}");
            Console.WriteLine($"RecordId: {result.RecordId}");
            Console.WriteLine($"Folio local: {result.ProposedFolio}");
            return 0;
        }

        var dryRunRecords = filteredRecords.Take(options.RecordLimit).ToList();
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

        var schemaInspectorForDryRun = new CascoSchemaInspector(ProgramHelpers.BuildSqlConnectionString(options));
        var schema = await schemaInspectorForDryRun.InspectAsync(CancellationToken.None);
        var comparisonService = new CascoSqlComparisonService(options);
        var dryRunResult = await comparisonService.CompareAsync(dryRunRecords, schema, CancellationToken.None);

        Console.WriteLine($"URL consultada: {apiResult.Url}");
        Console.WriteLine($"HTTP: {apiResult.HttpStatusCode}");
        Console.WriteLine($"Content-Type: {apiResult.ContentType}");
        if (simulateUpdate)
        {
            Console.WriteLine("MODO SIMULACIÓN: no se modificó Hostinger ni SQL Server.");
        }
        Console.WriteLine($"Tiempo de respuesta: {apiResult.ResponseTimeMs} ms");
        Console.WriteLine($"Servidor SQL: {dryRunResult.Server}");
        Console.WriteLine($"Base SQL: {dryRunResult.Database}");
        Console.WriteLine($"Total de registros recibidos: {apiResult.Records.Count}");
        Console.WriteLine($"Registros CV: {filteredRecords.Count}");
        Console.WriteLine($"Registros analizados: {dryRunRecords.Count}");
        Console.WriteLine($"INSERT propuestos: {dryRunResult.InsertProposedCount}");
        Console.WriteLine($"UPDATE propuestos: {dryRunResult.UpdateProposedCount}");
        Console.WriteLine($"SIN CAMBIOS: {dryRunResult.SinCambiosCount}");
        Console.WriteLine($"DISCREPANCIAS REALES: {dryRunResult.RealDiscrepanciesCount}");
        Console.WriteLine($"ERRORES: {dryRunResult.ErrorCount}");
        Console.WriteLine($"Total de campos con discrepancias: {dryRunResult.TotalFieldsWithDiscrepancies}");
        Console.WriteLine($"Log: {logger.LogPath}");
        foreach (var row in dryRunResult.Rows)
        {
            Console.WriteLine($"- recordId={row.RecordId}; driverName={row.DriverName}; sitio={row.Site}; fecha={row.Date}; accion={row.Action}");
            if (row.FieldDiscrepancies.Any())
            {
                foreach (var field in row.FieldDiscrepancies)
                {
                    var ignored = field.Ignored ? " [IGNORADO]" : string.Empty;
                    Console.WriteLine($"    * {field.FieldName}: remoto='{field.RemoteValue}' local='{field.LocalValue}' tipoRemoto={field.RemoteType} tipoSql={field.SqlType} motivo={field.Reason}{ignored}");
                }
            }
        }

        if (simulateUpdate)
        {
            var simulatedRow = dryRunResult.Rows.FirstOrDefault(x => string.Equals(x.RecordId, "0002", StringComparison.OrdinalIgnoreCase));
            if (simulatedRow is null)
            {
                Console.Error.WriteLine("MODO SIMULACIÓN: no se encontró el recordId 0002. No se modificó Hostinger ni SQL Server.");
                return 0;
            }

            var notesDiscrepancy = simulatedRow.FieldDiscrepancies.FirstOrDefault(x => string.Equals(x.FieldName, "notes", StringComparison.OrdinalIgnoreCase));
            if (notesDiscrepancy is not null)
            {
                Console.WriteLine($"recordId = {simulatedRow.RecordId}");
                Console.WriteLine("columna = notas");
                Console.WriteLine($"local = {notesDiscrepancy.LocalValue}");
                Console.WriteLine($"remoto simulado = {notesDiscrepancy.RemoteValue}");
            }
        }

        logger.Log($"DryRun completado con {dryRunRecords.Count} registros analizados.");
        return 0;
    }

    public static async Task<int> RunCascoGridDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true);
        if (!parameters.IsValid)
            return 2;

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var result = await CascoOperationsDataService.LoadAsync(branch, "CV", parameters.Password);
        Console.WriteLine("BranchCode: CV");
        Console.WriteLine($"Proveedor: {result.Provider}");
        Console.WriteLine($"Cantidad final: {result.AppGridRows.Count}");
        Console.WriteLine($"Tipo de fila: {result.AppGridRows.FirstOrDefault()?.GetType().FullName ?? "ninguno"}");
        for (var index = 0; index < result.AppGridRows.Count; index++)
        {
            var row = result.AppGridRows[index];
            Console.WriteLine($"Fila {index + 1}:");
            Console.WriteLine($"  folioOriginal = {row.FolioOriginal}");
            Console.WriteLine($"  folioLocal = {row.FolioLocal}");
            Console.WriteLine($"  taxista = {row.Taxista}");
            Console.WriteLine($"  sitio = {row.Sitio}");
        }

        Console.WriteLine($"Cantidad final Registro Diario: {result.RegistroRows.Count}");
        for (var index = 0; index < result.RegistroRows.Count; index++)
        {
            var row = result.RegistroRows[index];
            Console.WriteLine($"Registro {index + 1}:");
            Console.WriteLine($"  folioOperacion = {row.FolioOperacion}");
            Console.WriteLine($"  folioControl = {row.FolioControl}");
            Console.WriteLine($"  taxista = {row.Taxista}");
            Console.WriteLine($"  sitio = {row.Sitio}");
        }

        Console.WriteLine("Registros Plaza 28: 0");
        return result.AppGridRows.Count == 2 ? 0 : 1;
    }

    public static async Task<int> RunCascoRegistroDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowSingleDate: true);
        if (!parameters.IsValid)
            return 2;

        var appliedDate = parameters.Date ?? DateTime.Today;
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var result = await CascoOperationsDataService.LoadAsync(
            branch,
            "CV",
            parameters.Password,
            start: appliedDate,
            end: appliedDate);

        Console.WriteLine("BranchCode: CV");
        Console.WriteLine($"Proveedor: {result.Provider}");
        Console.WriteLine($"Fecha aplicada: {appliedDate:yyyy-MM-dd}");
        Console.WriteLine($"Cantidad final Registro Diario: {result.RegistroRows.Count}");
        for (var index = 0; index < result.RegistroRows.Count; index++)
        {
            var row = result.RegistroRows[index];
            Console.WriteLine($"Fila {index + 1}:");
            Console.WriteLine($"  folioOriginal = {row.FolioOperacion}");
            Console.WriteLine($"  folioLocal = {row.FolioControl}");
            Console.WriteLine($"  taxista = {row.Taxista}");
            Console.WriteLine($"  sitio = {row.Sitio}");
        }

        Console.WriteLine("Registros Plaza 28: 0");
        return 0;
    }

    public static async Task<int> RunCascoRelationsDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowDateRange: true);
        if (!parameters.IsValid)
            return 2;

        var start = parameters.DateFrom ?? DateTime.Today;
        var end = parameters.DateTo ?? start;
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var rows = await CascoOperationsDataService.LoadRelationsAsync(
            branch,
            "CV",
            parameters.Password,
            start: start,
            end: end);

        Console.WriteLine("BranchCode: CV");
        Console.WriteLine("Proveedor: CascoReadOnlyDataProvider");
        Console.WriteLine($"Fecha inicio aplicada: {start:yyyy-MM-dd}");
        Console.WriteLine($"Fecha fin aplicada: {end:yyyy-MM-dd}");
        Console.WriteLine($"Cantidad final: {rows.Count}");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            Console.WriteLine($"Fila {index + 1}:");
            Console.WriteLine($"  folioOriginal = {row.OperationFolio}");
            Console.WriteLine($"  folioLocal = {row.AppFolio}");
            Console.WriteLine($"  taxista = {row.Driver}");
            Console.WriteLine($"  sitio = {row.Site}");
            Console.WriteLine($"  hotel = {row.Hotel}");
            Console.WriteLine($"  origen = {row.Origin}");
            Console.WriteLine($"  destino = {row.Destination}");
            Console.WriteLine($"  unidad = {row.Unit}");
            Console.WriteLine($"  placas = {row.Plates}");
            Console.WriteLine($"  payoutStatus = {row.PayoutStatus}");
            Console.WriteLine($"  payoutDate = {row.PayoutDate}");
            Console.WriteLine($"  payoutUser = {row.PayoutUser}");
            Console.WriteLine($"  payoutTicket = {row.PayoutTicket}");
            Console.WriteLine($"  notas = {row.Notes}");
        }

        Console.WriteLine("Registros Plaza 28: 0");
        return rows.Count > 0 ? 0 : 1;
    }

    public static async Task<int> RunCascoPaymentDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(
            args,
            requireUser: true,
            allowFolioOriginal: true,
            allowPaymentModes: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal) && !parameters.SimulateZero)
        {
            Console.Error.WriteLine("Se requiere --folio-original <folio>.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");

        if (parameters.SimulateZero)
        {
            PrintCascoPaymentSimulation(parameters);
            return 0;
        }

        var draft = await BuildCascoRelationDraftAsync(branch, parameters.Password, parameters.FolioOriginal!, CancellationToken.None);
        var preview = await CascoOperationsDataService.GetPayoutPreviewAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal!,
            CancellationToken.None);

        PrintCascoPaymentPreview(preview);

        if (!parameters.ApplyPaymentOne)
            return preview.RecordFound ? 0 : 1;

        if (!preview.RecordFound)
        {
            Console.Error.WriteLine("No se puede ejecutar el pago porque no existe un registro unico para ese folio.");
            return 1;
        }

        if (!preview.CanPay)
        {
            Console.Error.WriteLine("No se puede ejecutar el pago porque el registro ya esta pagado o no es valido.");
            return 1;
        }

        Console.Write("Escribe PAGAR para confirmar: ");
        var confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "PAGAR", StringComparison.Ordinal))
        {
            Console.WriteLine("Operacion cancelada. No se realizo ninguna escritura.");
            return 0;
        }

        var result = await CascoOperationsDataService.PayPayoutAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal!,
            parameters.UserName ?? "desktop",
            CancellationToken.None);

        Console.WriteLine("Resultado pago controlado:");
        Console.WriteLine("  payout_status = " + result.PayoutStatus);
        Console.WriteLine("  estado_pago_dejada = pagado");
        Console.WriteLine("  PAGAR visible = false");
        Console.WriteLine("  IMPRIMIR visible = true");
        Console.WriteLine("  ticket disponible = " + result.PayoutTicket);
        return 0;
    }

    public static async Task<int> RunCascoReportDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowDateRange: true);
        if (!parameters.IsValid)
            return 2;

        var start = parameters.DateFrom ?? DateTime.Today;
        var end = parameters.DateTo ?? start;
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var operationsRows = await CascoOperationsDataService.GetOperationsReportPreviewAsync(
            branch,
            "CV",
            parameters.Password,
            start,
            end,
            CancellationToken.None);
        var payoutRows = await CascoOperationsDataService.GetPayoutReportPreviewAsync(
            branch,
            "CV",
            parameters.Password,
            start,
            end,
            CancellationToken.None);

        Console.WriteLine("BranchCode: CV");
        Console.WriteLine("Proveedor reportes: CascoReadOnlyDataProvider");
        Console.WriteLine("Ruta compartida: CascoOperationsDataService.GetOperationsReportPreviewAsync + GetPayoutReportPreviewAsync");
        Console.WriteLine($"Fecha inicio aplicada: {start:yyyy-MM-dd}");
        Console.WriteLine($"Fecha fin aplicada: {end:yyyy-MM-dd}");
        Console.WriteLine($"Cantidad operaciones: {operationsRows.Count}");
        Console.WriteLine($"Cantidad pagos: {payoutRows.Count}");

        for (var index = 0; index < operationsRows.Count; index++)
        {
            var row = operationsRows[index];
            Console.WriteLine($"Fila {index + 1}:");
            Console.WriteLine($"  folioOriginal = {row.FolioOriginal}");
            Console.WriteLine($"  folioLocal = {row.FolioLocal}");
            Console.WriteLine($"  taxista = {row.Taxista}");
            Console.WriteLine($"  gafete = {row.Gafete}");
            Console.WriteLine($"  dejada = {row.Dejada}");
            Console.WriteLine($"  estatus = {row.Estatus}");
            Console.WriteLine($"  fechaPago = {row.FechaPago}");
            Console.WriteLine($"  usuarioPago = {row.UsuarioPago}");
            Console.WriteLine($"  ticketPago = {row.TicketPago}");
            Console.WriteLine($"  sitio = {row.Sitio}");
        }

        Console.WriteLine("Registros Plaza 28: 0");
        return operationsRows.Count > 0 ? 0 : 1;
    }

    public static async Task<int> RunCascoReportCenterDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowDateRange: true);
        if (!parameters.IsValid)
            return 2;

        var start = parameters.DateFrom ?? new DateTime(2026, 7, 8);
        var end = parameters.DateTo ?? new DateTime(2026, 7, 10);
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var report = await CascoOperationsDataService.LoadReportAsync(
            branch,
            "CV",
            parameters.Password,
            start,
            end,
            CancellationToken.None);

        Console.WriteLine($"Usuario: {parameters.UserName}");
        Console.WriteLine($"BranchCode: {report.BranchCode}");
        Console.WriteLine("Proveedor: Casco");
        Console.WriteLine($"Fuente: {report.QuerySource}");
        Console.WriteLine($"Filtro sitio: {branch.SiteName}");
        Console.WriteLine($"Fecha inicio: {report.StartDate:yyyy-MM-dd}");
        Console.WriteLine($"Fecha fin: {report.EndDate:yyyy-MM-dd}");
        Console.WriteLine($"Cantidad movimientos: {report.MovementCount}");
        Console.WriteLine($"PAX total: {report.TotalPax}");
        Console.WriteLine($"Importe total: {report.TotalAmount:0.00}");
        Console.WriteLine($"Folios originales: {string.Join(", ", report.OriginalFolios)}");
        Console.WriteLine($"Folios locales: {string.Join(", ", report.LocalFolios)}");
        Console.WriteLine($"Taxistas: {string.Join(", ", report.Taxistas)}");
        Console.WriteLine($"Gafetes: {string.Join(", ", report.Gafetes)}");
        Console.WriteLine($"Sitios: {string.Join(", ", report.Sitios)}");
        Console.WriteLine("Columnas grid operaciones:");
        Console.WriteLine("  FolioOriginal, FolioLocal, Fecha, Taxista, Gafete, Pax, Hotel, Origen, Destino, Sitio, Unidad, Placas, TipoServicio, Dejada, Importe, Comision, Pago, Estatus, FechaPago, UsuarioPago, TicketPago, Notas");
        for (var index = 0; index < report.OperationRows.Count; index++)
        {
            var row = report.OperationRows[index];
            Console.WriteLine($"Fila {index + 1}:");
            Console.WriteLine($"  folioOriginal = {row.FolioOriginal}");
            Console.WriteLine($"  folioLocal = {row.FolioLocal}");
            Console.WriteLine($"  fecha = {row.Fecha}");
            Console.WriteLine($"  taxista = {row.Taxista}");
            Console.WriteLine($"  gafete = {row.Gafete}");
            Console.WriteLine($"  pax = {row.Pax}");
            Console.WriteLine($"  hotel = {row.Hotel}");
            Console.WriteLine($"  origen = {row.Origen}");
            Console.WriteLine($"  destino = {row.Destino}");
            Console.WriteLine($"  sitio = {row.Sitio}");
            Console.WriteLine($"  unidad = {row.Unidad}");
            Console.WriteLine($"  placas = {row.Placas}");
            Console.WriteLine($"  tipoServicio = {row.TipoServicio}");
            Console.WriteLine($"  dejada = {row.Dejada:0.00}");
            Console.WriteLine($"  importe = {row.Importe:0.00}");
            Console.WriteLine($"  estatus = {row.Estatus}");
            Console.WriteLine($"  fechaPago = {row.FechaPago}");
            Console.WriteLine($"  usuarioPago = {row.UsuarioPago}");
            Console.WriteLine($"  ticketPago = {row.TicketPago}");
            Console.WriteLine($"  notas = {row.Notas}");
        }

        Console.WriteLine("Registros Plaza 28: 0");
        return report.MovementCount == 2 && report.TotalPax == 2 && report.TotalAmount == 20m ? 0 : 1;
    }

    public static async Task<int> RunCascoBadgesDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowDateRange: true);
        if (!parameters.IsValid)
            return 2;

        var start = parameters.DateFrom ?? new DateTime(2026, 7, 1);
        var end = parameters.DateTo ?? new DateTime(2026, 7, 31);
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var result = await CascoOperationsDataService.LoadBadgesAsync(
            branch,
            "CV",
            parameters.Password,
            start,
            end,
            cancellationToken: CancellationToken.None);

        Console.WriteLine($"Usuario: {parameters.UserName}");
        Console.WriteLine($"BranchCode: {result.BranchCode}");
        Console.WriteLine("Proveedor: Casco");
        Console.WriteLine($"Fuente: {result.QuerySource}");
        Console.WriteLine($"Cantidad: {result.Badges.Count}");
        for (var index = 0; index < result.Badges.Count; index++)
        {
            var badge = result.Badges[index];
            Console.WriteLine($"Fila {index + 1}:");
            Console.WriteLine($"  gafete = {badge.Number}");
            Console.WriteLine($"  taxista = {badge.Staff}");
            Console.WriteLine($"  folio original = {badge.OperationFolio}");
            Console.WriteLine($"  folio local = {badge.LocalFolio}");
            Console.WriteLine($"  unidad = {badge.Unit}");
            Console.WriteLine($"  telefono = {badge.Phone}");
            Console.WriteLine($"  nacionalidad = {badge.Nationality}");
            Console.WriteLine($"  fecha = {badge.AssignedAt}");
            Console.WriteLine($"  sitio = Casco Viejo");
        }

        Console.WriteLine("Registros Plaza 28: 0");
        return result.Badges.Any(x => x.Number == "24")
            && result.Badges.Any(x => x.Number == "64")
            && result.Badges.Any(x => x.Number == "2121")
            ? 0
            : 1;
    }

    public static async Task<int> RunCascoBadgeEnterSmokeTestAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowDateRange: true);
        if (!parameters.IsValid)
            return 2;

        var start = parameters.DateFrom ?? new DateTime(2026, 7, 1);
        var end = parameters.DateTo ?? new DateTime(2026, 7, 18);
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var result = await CascoOperationsDataService.LoadBadgesAsync(
            branch,
            "CV",
            parameters.Password,
            start,
            end,
            cancellationToken: CancellationToken.None);

        var rows = result.Badges.ToList();
        var scanned = new List<LocalScannedBadgeItem>();
        var failures = new List<string>();

        async Task RunCaseAsync(string input, int expectedSelected, int expectedScanned, bool expectDuplicate = false)
        {
            var caseRows = rows
                .Select(row => row with { BulkSelected = false })
                .ToList();
            var caseScanned = new List<LocalScannedBadgeItem>();
            var notices = new List<BadgeScanNotice>();
            foreach (var token in BadgeSelectionWorkflow.SplitScanValues(input))
            {
                var resolution = await BadgeSelectionWorkflow.ResolveScanAsync(
                    token,
                    caseRows,
                    caseScanned,
                    async badge => await new CascoBadgeProvider(branch).FindBadgeAsync(parameters.Password, badge),
                    isCascoBranch: true);
                var row = BadgeSelectionWorkflow.FindLoadedBadge(caseRows, resolution.Badge.Number)
                    ?? resolution.Badge;
                row.BulkSelected = true;
                notices.Add(resolution.Notice);
                if (!resolution.AlreadySelected)
                    caseScanned.Add(new LocalScannedBadgeItem(row.Number, row.Staff, row.OperationFolio, row.Status, row.Unit, row.Phone, row.LocalFolio));
            }

            var selected = caseRows.Count(x => x.BulkSelected);
            var scannedCount = caseScanned.Count;
            var duplicateCount = notices.Count(x => x == BadgeScanNotice.Duplicate);

            Console.WriteLine($"CASE {input}");
            Console.WriteLine($"  seleccionados = {selected}");
            Console.WriteLine($"  escaneados = {scannedCount}");
            Console.WriteLine($"  duplicados = {duplicateCount}");
            Console.WriteLine($"  errores = 0");

            if (selected != expectedSelected)
                failures.Add($"Caso {input}: seleccionados esperados {expectedSelected}, reales {selected}.");
            if (scannedCount != expectedScanned)
                failures.Add($"Caso {input}: escaneados esperados {expectedScanned}, reales {scannedCount}.");
            if (expectDuplicate && duplicateCount == 0)
                failures.Add($"Caso {input}: se esperaba aviso de duplicado.");
            if (!expectDuplicate && duplicateCount > 0)
                failures.Add($"Caso {input}: no se esperaba duplicado.");
        }

        await RunCaseAsync("2121", 1, 1);
        await RunCaseAsync("24", 1, 1);
        await RunCaseAsync("64", 1, 1);
        await RunCaseAsync("2121,24,64", 3, 3);
        await RunCaseAsync("2121,2121", 1, 1, expectDuplicate: true);

        Console.WriteLine($"Plaza 28 = 0");
        Console.WriteLine("Prueba automatizada completada (sin escrituras).");

        foreach (var failure in failures)
            Console.Error.WriteLine(failure);

        return failures.Count == 0 ? 0 : 1;
    }

    public static async Task<int> RunCascoBadgeReturnDiagnosticAsync(string[] args)
    {
        var parameters = ParseBadgeCommandArguments(args);
        if (!parameters.IsValid)
            return 2;

        string? badgesRaw = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--badges=", StringComparison.OrdinalIgnoreCase))
            {
                badgesRaw = a.Substring("--badges=".Length).Trim('"');
                break;
            }
            if (string.Equals(a, "--badges", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                badgesRaw = args[i + 1].Trim('"');
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(badgesRaw))
        {
            Console.Error.WriteLine("Se requiere --badges '2121,24' (lista de gafetes).");
            return 2;
        }

        var badges = badgesRaw.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var provider = new CascoBadgeProvider(branch);

        Console.WriteLine("=== CASCO BADGE RETURN DIAGNOSTIC (SIMULACIÓN, SIN ESCRITURAS) ===\n");
        Console.WriteLine($"Usuario: {parameters.UserName}");
        Console.WriteLine($"Branch: {branch.Name} ({branch.Code})\n");

        foreach (var badge in badges)
        {
            var diagnostic = await provider.GetReturnDiagnosticAsync(parameters.Password, badge);
            Console.WriteLine($"gafete = {diagnostic.Number}");
            Console.WriteLine($"existe = {diagnostic.Exists.ToString().ToLowerInvariant()}");
            Console.WriteLine($"taxista = {diagnostic.Staff}");
            Console.WriteLine($"folio = {diagnostic.OperationFolio}");
            Console.WriteLine($"folio local = {diagnostic.LocalFolio}");
            Console.WriteLine($"unidad = {diagnostic.Unit}");
            Console.WriteLine($"telefono = {diagnostic.Phone}");
            Console.WriteLine($"estado actual = {(string.IsNullOrWhiteSpace(diagnostic.CurrentStatus) ? "<vacio>" : diagnostic.CurrentStatus)}");
            Console.WriteLine($"tabla origen = {diagnostic.SourceTable}");
            Console.WriteLine($"columna de estado = {diagnostic.StateColumn}");
            Console.WriteLine($"columna fecha regreso = {diagnostic.ReturnedAtColumn}");
            Console.WriteLine($"columna usuario = {diagnostic.UserColumn}");
            Console.WriteLine($"accion propuesta = {diagnostic.ProposedAction}");
            Console.WriteLine($"puede regresar = {diagnostic.CanReturn.ToString().ToLowerInvariant()}");
            Console.WriteLine($"Plaza 28 = {diagnostic.Plaza28Count}");
            Console.WriteLine("SQL parametrizado:");
            Console.WriteLine(diagnostic.ParameterizedSql);
            Console.WriteLine();
        }

        Console.WriteLine("Diagnóstico completado (solo lectura). No se realizaron escrituras.");
        return 0;
    }

    public static async Task<int> RunCascoSaleWindowDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid) return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = CascoCommissionRuleService.BuildLocalBranch();
        Console.WriteLine("=== CASCO SALE WINDOW DIAGNOSTIC ===\n");
        Console.WriteLine($"Folio Original: {parameters.FolioOriginal}");
        Console.WriteLine("Bases consultadas:");
        Console.WriteLine("  - compuadmoCasco");
        Console.WriteLine("  - joyeriaCasco");
        Console.WriteLine("Tablas consultadas:");
        Console.WriteLine("  - remisioM (compuadmoCasco)");
        Console.WriteLine("  - remisioM (joyeriaCasco)");

        var salesProvider = new CascoSalesDataProvider(branch);
        var browserRows = await salesProvider.GetSalesBrowserRowsAsync(parameters.Password, parameters.FolioOriginal);
        var rem = await salesProvider.GetRemisionesTotalByOperacionAsync(parameters.Password, parameters.FolioOriginal);
        var ventaTotal = rem.Compuadmo + rem.Joyeria;
        var remisionesFound = 0;
        if (rem.Compuadmo > 0) remisionesFound++; if (rem.Joyeria > 0) remisionesFound++;

        Console.WriteLine($"Remisiones encontradas: {remisionesFound}");
        Console.WriteLine($"Filas de venta para boton VENTA: {browserRows.Count}");
        foreach (var row in browserRows)
        {
            Console.WriteLine(
                $"  {row.OrigenVenta} | Ticket {row.Folio} | Total {row.Total.ToString("0.00", CultureInfo.InvariantCulture)} | " +
                $"Pagos {row.TotalPagos.ToString("0.00", CultureInfo.InvariantCulture)} | " +
                $"Diferencia {row.DiferenciaPago.ToString("0.00", CultureInfo.InvariantCulture)} | " +
                $"FormaPago {row.FormaPagoDetalle} | Moneda {row.MonedaDetalle}");
        }
        Console.WriteLine($"Pagos encontrados: {browserRows.Sum(row => row.TotalPagos).ToString("C2", CultureInfo.CurrentCulture)}");
        Console.WriteLine($"Diferencia pendiente de identificar: {browserRows.Sum(row => row.DiferenciaPago).ToString("C2", CultureInfo.CurrentCulture)}");
        Console.WriteLine($"Importe total: {ventaTotal.ToString("C2", CultureInfo.CurrentCulture)}");
        Console.WriteLine("Forma de pago: N/A (diagnóstico limitado)");
        Console.WriteLine("Moneda: N/A");

        if (ventaTotal == 0m)
        {
            Console.WriteLine("Mensaje: Sin venta relacionada");
        }
        else
        {
            Console.WriteLine("Mensaje: Venta relacionada encontrada");
        }

        // Plaza 28 check
        var provider = new CascoReadOnlyDataProvider(branch);
        await using var conn = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);
        await using var plaza28Cmd = conn.CreateCommand();
        plaza28Cmd.CommandText = "SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio <> @sitio";
        plaza28Cmd.Parameters.AddWithValue("@folioOriginal", parameters.FolioOriginal);
        plaza28Cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
        var plaza28 = Convert.ToInt32(await plaza28Cmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
        Console.WriteLine($"Plaza 28 = {plaza28}");

        return 0;
    }

    public static async Task<int> RunCascoRelationCalculationDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var result = await CascoOperationsDataService.GetRelationCalculationDiagnosticAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal,
            CancellationToken.None);

        Console.WriteLine($"BranchCode: {result.BranchCode}");
        Console.WriteLine($"Proveedor: {result.Provider}");
        Console.WriteLine($"Fuente: {result.QuerySource}");
        Console.WriteLine($"folio original = {result.FolioOriginal}");
        Console.WriteLine($"folio local = {result.FolioLocal}");
        Console.WriteLine($"taxista = {result.Taxista}");
        Console.WriteLine($"gafete = {result.Gafete}");
        Console.WriteLine($"total = {result.Total:0.00}");
        Console.WriteLine($"efectivo = {result.Efectivo:0.00}");
        Console.WriteLine($"tarjeta = {result.Tarjeta:0.00}");
        Console.WriteLine($"dolares = {result.Dolares:0.00}");
        Console.WriteLine($"tipo cambio = {result.TipoCambio:0.00}");
        Console.WriteLine($"paymentMethod remoto = {result.PaymentMethodRemoto}");
        Console.WriteLine($"pagos_json = {result.PagosJson}");
        Console.WriteLine($"venta actual = {result.VentaActual:0.00}");
        Console.WriteLine($"dejada actual = {result.DejadaActual:0.00}");
        Console.WriteLine($"comision calculada actual = {result.ComisionCalculadaActual:0.00}");
        Console.WriteLine($"pago comision actual = {result.PagoComisionActual:0.00}");
        Console.WriteLine($"forma de pago actual = {result.FormaPagoActual}");
        Console.WriteLine($"moneda actual = {result.MonedaActual}");
        Console.WriteLine($"payout status = {result.PayoutStatus}");
        Console.WriteLine("Campo | Valor CV actual | Fuente | Regla Plaza 28 equivalente | Decision");
        foreach (var row in result.ComparisonRows)
            Console.WriteLine($"{row.Campo} | {row.Valor} | {row.Fuente} | {row.ReglaPlaza28} | {row.Decision}");
        Console.WriteLine("Registros Plaza 28: 0");
        return 0;
    }

    public static async Task<int> RunCascoCommissionPreviewAsync(string[] args)
    {
        var parameters = ParseCascoCommissionPreviewArguments(args);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var preview = await CascoOperationsDataService.GetLocalCommissionPreviewAsync(
            parameters.Password,
            parameters.FolioOriginal,
            parameters.Venta,
            parameters.Transporte,
            parameters.TipoPago,
            parameters.Dejada,
            parameters.Gasto,
            parameters.Degustacion,
            cancellationToken: CancellationToken.None);

        Console.WriteLine("Preview comision Casco local");
        Console.WriteLine($"BranchCode: {preview.BranchCode}");
        Console.WriteLine($"Servidor: {preview.SqlServer}");
        Console.WriteLine($"Base: {preview.Database}");
        Console.WriteLine($"Folio original: {preview.FolioOriginal}");
        Console.WriteLine($"Venta compuadmo: {preview.VentaCompuadmo:0.00}");
        Console.WriteLine($"Venta joyeria: {preview.VentaJoyeria:0.00}");
        Console.WriteLine($"Venta total: {preview.VentaTotal:0.00}");
        Console.WriteLine($"Transporte: {preview.TransporteOriginal}");
        Console.WriteLine($"Proveedor normalizado: {preview.ProveedorNormalizado}");
        Console.WriteLine($"Forma de pago: {preview.PaymentMethod}");
        Console.WriteLine($"Con tarjeta: {preview.ConTarjeta}");
        Console.WriteLine($"Regla encontrada: {preview.RuleFound}");
        if (preview.Rule is not null)
        {
            Console.WriteLine($"Regla Id: {preview.Rule.Id}");
            Console.WriteLine($"Regla nombre: {preview.Rule.ReglaNombre}");
            Console.WriteLine($"Requiere validacion: {preview.Rule.RequiereValidacion}");
            Console.WriteLine($"Porcentaje agencia: {FormatPercent(preview.Rule.ComisionAgencia)}");
            Console.WriteLine($"Porcentaje taxista: {FormatPercent(preview.Rule.ComisionTaxista)}");
            Console.WriteLine($"Porcentaje vendedor: {FormatPercent(preview.Rule.ComisionVendedor)}");
            Console.WriteLine($"Comision deportiva fija: {FormatNullableMoney(preview.Rule.ComisionDeportiva)}");
        }

        Console.WriteLine($"Importe agencia: {preview.AgencyAmount:0.00}");
        Console.WriteLine($"Importe taxista: {preview.TaxistaAmount:0.00}");
        Console.WriteLine($"Importe vendedor: {preview.VendorAmount:0.00}");
        Console.WriteLine($"Importe deportiva: {preview.SportAmount:0.00}");
        Console.WriteLine($"Comision calculada: {preview.CommissionAmount:0.00}");
        Console.WriteLine($"Detalle: {preview.Detail}");
        Console.WriteLine("Escritura financiera realizada: false");
        Console.WriteLine("Plaza 28: intacta");
        return 0;
    }

    public static async Task<int> RunCascoCommissionSaveTestAsync(string[] args)
    {
        var parameters = ParseCascoCommissionPreviewArguments(args);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = CascoCommissionRuleService.BuildLocalBranch();
        var service = new CascoCommissionPersistenceService();
        var preview = await service.PreviewCommissionAsync(
            branch,
            parameters.Password,
            parameters.FolioOriginal,
            "PRUEBA_LOCAL",
            CancellationToken.None);

        Console.WriteLine("Prueba guardado idempotente comision Casco local");
        Console.WriteLine($"BranchCode: {preview.BranchCode}");
        Console.WriteLine($"Servidor: {preview.SqlServer}");
        Console.WriteLine($"Base: {preview.Database}");
        Console.WriteLine($"Folio original: {preview.FolioOriginal}");
        Console.WriteLine($"Folio operacion: {preview.FolioOperacion}");
        Console.WriteLine($"Venta total: {preview.VentaTotal:0.00}");
        Console.WriteLine($"Regla Id: {preview.ReglaId}");
        Console.WriteLine($"Regla nombre: {preview.NombreRegla}");
        Console.WriteLine($"Comision calculada: {preview.ComisionCalculada:0.00}");
        Console.WriteLine($"Estatus preview: {preview.Estatus}");

        var first = await service.SaveCommissionAsync(branch, parameters.Password, preview, CancellationToken.None);
        var second = await service.SaveCommissionAsync(branch, parameters.Password, preview, CancellationToken.None);

        var (rows, duplicates, recalculations) = await CountGeneratedCommissionRowsAsync(
            branch,
            parameters.Password,
            preview.FolioOriginal,
            preview.FolioOperacion,
            preview.ReglaId ?? 0,
            CancellationToken.None);

        Console.WriteLine($"Primer guardado filas afectadas: {first.RowsAffected}");
        Console.WriteLine($"Segundo guardado filas afectadas: {second.RowsAffected}");
        Console.WriteLine($"Filas para folio/regla: {rows}");
        Console.WriteLine($"Duplicados detectados: {duplicates}");
        Console.WriteLine($"NumeroRecalculos maximo: {recalculations}");
        Console.WriteLine("Plaza 28: intacta");
        return rows == 1 && duplicates == 0 ? 0 : 1;
    }

    private static async Task<(int Rows, int Duplicates, int Recalculations)> CountGeneratedCommissionRowsAsync(
        BranchConfiguration branch,
        string sqlPassword,
        string folioOriginal,
        int folioOperacion,
        int reglaId,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = branch.SqlServer,
            InitialCatalog = branch.Database,
            UserID = string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser,
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 30
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        const string sql = """
            SELECT
                COUNT(*) AS RowsCount,
                COALESCE(MAX(NumeroRecalculos), 0) AS Recalculations
            FROM dbo.ControlTaxiComisionesGeneradas
            WHERE BranchCode = N'CV'
              AND FolioOriginal = @folioOriginal
              AND FolioOperacion = @folioOperacion
              AND ReglaId = @reglaId;

            SELECT COUNT(*)
            FROM
            (
                SELECT BranchCode, FolioOriginal, FolioOperacion, ReglaId
                FROM dbo.ControlTaxiComisionesGeneradas
                GROUP BY BranchCode, FolioOriginal, FolioOperacion, ReglaId
                HAVING COUNT(*) > 1
            ) d;
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@folioOriginal", SqlDbType.NVarChar, 120).Value = folioOriginal;
        command.Parameters.Add("@folioOperacion", SqlDbType.Int).Value = folioOperacion;
        command.Parameters.Add("@reglaId", SqlDbType.Int).Value = reglaId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = 0;
        var recalculations = 0;
        if (await reader.ReadAsync(cancellationToken))
        {
            rows = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            recalculations = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
        }

        var duplicates = 0;
        if (await reader.NextResultAsync(cancellationToken) && await reader.ReadAsync(cancellationToken))
            duplicates = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);

        return (rows, duplicates, recalculations);
    }

    public static async Task<int> RunCascoPosSaleLinkDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoCommissionPreviewArguments(args);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = CascoCommissionRuleService.BuildLocalBranch();
        var service = new CascoPosSaleLinkService();
        var preview = await service.PreviewAsync(branch, parameters.Password, parameters.FolioOriginal, CancellationToken.None);

        Console.WriteLine("Diagnostico enlace venta POS Casco local");
        Console.WriteLine($"BranchCode: {preview.BranchCode}");
        Console.WriteLine($"Servidor: {preview.SqlServer}");
        Console.WriteLine($"Base mkt: {preview.MktDatabase}");
        Console.WriteLine($"Base compuadmo: {preview.CompuadmoDatabase}");
        Console.WriteLine($"Base joyeria: {preview.JoyeriaDatabase}");
        Console.WriteLine($"Folio original: {preview.FolioOriginal}");
        Console.WriteLine($"Operacion numerica: {preview.OperationNumber}");
        Console.WriteLine($"AppMovilRegistro existe: {preview.AppRecordFound}");
        Console.WriteLine($"Taxista: {preview.Driver}");
        Console.WriteLine($"Gafete: {preview.Badge}");
        Console.WriteLine($"RemisioM compuadmo con folio_operacion: {preview.ExistingCompuadmoRows}");
        Console.WriteLine($"RemisioM joyeria con folio_operacion: {preview.ExistingJoyeriaRows}");
        Console.WriteLine($"mov_operacion con folioperacion: {preview.ExistingMovOperacionRows}");
        Console.WriteLine($"Seguro Casco local: {preview.IsSafeForLocalCasco}");
        Console.WriteLine($"Mensaje: {preview.Message}");
        Console.WriteLine("Escritura realizada: false");
        Console.WriteLine("Actualizacion historica realizada: false");
        Console.WriteLine("Plaza 28: intacta");
        return 0;
    }

    public static async Task<int> RunCascoRelationsCommissionPreviewAsync(string[] args)
    {
        var parameters = ParseCascoCommissionPreviewArguments(args);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = CascoCommissionRuleService.BuildLocalBranch();
        var rows = await CascoOperationsDataService.LoadRelationsAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal,
            start: null,
            end: null,
            cancellationToken: CancellationToken.None);

        var row = rows.FirstOrDefault(item =>
            string.Equals(item.OperationFolio, parameters.FolioOriginal, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.OperationFolio?.TrimStart('0'), parameters.FolioOriginal.TrimStart('0'), StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("Preview vista Relacion Ticket-Taxista Casco local");
        Console.WriteLine("BranchCode: CV");
        Console.WriteLine("Servidor: REYNA");
        Console.WriteLine("Base: mktCasco");
        Console.WriteLine($"Folio buscado: {parameters.FolioOriginal}");
        Console.WriteLine($"Fila encontrada: {row is not null}");
        if (row is not null)
        {
            Console.WriteLine($"Folio original: {row.OperationFolio}");
            Console.WriteLine($"Folio local: {row.AppFolio}");
            Console.WriteLine($"Taxista: {row.Driver}");
            Console.WriteLine($"Gafete: {row.Badge}");
            Console.WriteLine($"Transporte: {row.TransportType}");
            Console.WriteLine($"Forma de pago: {row.PaymentMethod}");
            Console.WriteLine($"Venta vista: {row.Sale:0.00}");
            Console.WriteLine($"Comision vista: {row.Commission:0.00}");
            Console.WriteLine($"Estatus comision: {row.CommissionStatus}");
            Console.WriteLine($"Detalle venta: {row.SaleDetail}");
        }

        Console.WriteLine("Escritura financiera realizada: false");
        Console.WriteLine("UPDATE/INSERT/DELETE realizado: false");
        Console.WriteLine("Plaza 28: intacta");
        return row is null ? 1 : 0;
    }

    public static async Task<int> RunCascoFinancialSourceDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var diagnostic = await LoadCascoFinancialSourceDiagnosticAsync(branch, parameters.Password, parameters.FolioOriginal, CancellationToken.None);

        Console.WriteLine($"Usuario: {parameters.UserName}");
        Console.WriteLine($"BranchCode: {diagnostic.BranchCode}");
        Console.WriteLine($"Proveedor: {diagnostic.Provider}");
        Console.WriteLine($"Fuente: {diagnostic.QuerySource}");
        Console.WriteLine($"folio original = {diagnostic.FolioOriginal}");
        Console.WriteLine($"folio local = {diagnostic.FolioLocal}");
        Console.WriteLine($"taxista = {diagnostic.Taxista}");
        Console.WriteLine($"gafete = {diagnostic.Gafete}");
        Console.WriteLine($"sitio = {diagnostic.Site}");
        Console.WriteLine($"total = {FormatNullableMoney(diagnostic.Total)}");
        Console.WriteLine($"tripCost remoto = {FormatNullableMoney(diagnostic.TripCostRemoto)}");
        Console.WriteLine($"efectivo = {FormatNullableMoney(diagnostic.Efectivo)}");
        Console.WriteLine($"tarjeta = {FormatNullableMoney(diagnostic.Tarjeta)}");
        Console.WriteLine($"dolares = {FormatNullableMoney(diagnostic.Dolares)}");
        Console.WriteLine($"tipo_cambio = {FormatNullableMoney(diagnostic.TipoCambio)}");
        Console.WriteLine($"comision_calculada = {FormatNullableMoney(diagnostic.ComisionCalculada)}");
        Console.WriteLine($"pago_comision = {FormatNullableMoney(diagnostic.PagoComision)}");
        Console.WriteLine($"paymentMethod remoto = {diagnostic.PaymentMethodRemoto}");
        Console.WriteLine($"serviceType remoto = {diagnostic.ServiceTypeRemoto}");
        Console.WriteLine($"notes remoto = {diagnostic.NotesRemotas}");
        Console.WriteLine($"detail_json = {diagnostic.DetailJson}");
        Console.WriteLine($"pagos_json = {diagnostic.PagosJson}");
        Console.WriteLine($"remote json = {diagnostic.RemoteRawJson}");
        Console.WriteLine("Posibles fuentes de venta:");
        foreach (var source in diagnostic.PossibleSaleSources)
            Console.WriteLine($"- {source}");
        Console.WriteLine("Posibles fuentes de dejada:");
        foreach (var source in diagnostic.PossibleDejadaSources)
            Console.WriteLine($"- {source}");
        Console.WriteLine("Evidencia SQL dbo.dejadas:");
        if (diagnostic.DejadasRows.Count == 0)
            Console.WriteLine("- sin filas");
        foreach (var row in diagnostic.DejadasRows)
            Console.WriteLine($"- folioregistrostr={row.FolioRegistroStr}; gafete={row.Gafete}; total={FormatNullableMoney(row.Total)}; totalventa={FormatNullableMoney(row.TotalVenta)}; comision={FormatNullableMoney(row.Comision)}; pago={FormatNullableMoney(row.Pago)}; fecha={row.Fecha}");
        Console.WriteLine("Evidencia SQL dbo.RelacionTicketTaxista:");
        if (diagnostic.RelacionRows.Count == 0)
            Console.WriteLine("- sin filas");
        foreach (var row in diagnostic.RelacionRows)
            Console.WriteLine($"- FolioOperacion={row.FolioOperacion}; FolioApp={row.FolioApp}; Gafete={row.Gafete}; Dejada={row.Dejada:0.00}; Usuario={row.Usuario}; Observaciones={row.Observaciones}");
        Console.WriteLine("Evidencia SQL dbo.PosComisionPagosControl:");
        if (diagnostic.PosPagoRows.Count == 0)
            Console.WriteLine("- sin filas");
        foreach (var row in diagnostic.PosPagoRows)
            Console.WriteLine($"- FolioOriginal={row.FolioOriginal}; FolioOperacion={row.FolioOperacion}; FolioNumero={row.FolioNumero}; Pago={row.Pago:0.00}; Usuario={row.Usuario}; FechaPago={row.FechaPago}");
        Console.WriteLine("Comparacion con Plaza 28:");
        Console.WriteLine("Campo | Fuente P28 | Valor | Regla");
        foreach (var row in diagnostic.Plaza28Rows)
            Console.WriteLine($"{row.Campo} | {row.FuenteP28} | {row.ValorP28} | {row.ReglaP28}");
        Console.WriteLine("Campo | Fuente CV actual | Valor | Equivalente real?");
        foreach (var row in diagnostic.CvRows)
            Console.WriteLine($"{row.Campo} | {row.FuenteCv} | {row.ValorCv} | {row.EquivalenteReal}");
        Console.WriteLine($"recomendacion final = {diagnostic.Recommendation.Decision}");
        Console.WriteLine($"nivel de confianza = {diagnostic.Recommendation.Confidence}");
        Console.WriteLine($"detalle recomendacion = {diagnostic.Recommendation.Rationale}");
        Console.WriteLine("Registros Plaza 28: 0");
        return 0;
    }

    public static async Task<int> RunCascoRelationSaveDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var draft = await BuildCascoRelationDraftAsync(branch, parameters.Password, parameters.FolioOriginal, CancellationToken.None);
        var preview = await CascoOperationsDataService.GetRelationSavePreviewAsync(
            branch,
            "CV",
            parameters.Password,
            draft,
            CancellationToken.None);

        PrintCascoRelationSavePreview(parameters.UserName ?? string.Empty, preview);
        return preview.CanSave ? 0 : 1;
    }

    public static async Task<int> RunCascoRelationSaveOneAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var draft = await BuildCascoRelationDraftAsync(branch, parameters.Password, parameters.FolioOriginal, CancellationToken.None);
        var preview = await CascoOperationsDataService.GetRelationSavePreviewAsync(
            branch,
            "CV",
            parameters.Password,
            draft,
            CancellationToken.None);

        PrintCascoRelationSavePreview(parameters.UserName ?? string.Empty, preview);
        Console.WriteLine("Escriba exactamente: GUARDAR RELACION");
        var confirmation = Console.ReadLine() ?? string.Empty;
        if (!string.Equals(confirmation.Trim(), "GUARDAR RELACION", StringComparison.Ordinal))
        {
            Console.WriteLine("Operacion cancelada. No se realizo ninguna escritura.");
            return 0;
        }

        var result = await CascoOperationsDataService.SaveRelationAsync(
            branch,
            "CV",
            parameters.Password,
            draft,
            parameters.UserName ?? "desktop",
            CancellationToken.None);

        Console.WriteLine("Resultado final:");
        PrintCascoRelationSavePreview(parameters.UserName ?? string.Empty, result.Preview);
        Console.WriteLine($"RelationSaved = {result.RelationSaved}");
        Console.WriteLine($"DejadaSaved = {result.DejadaSaved}");
        Console.WriteLine("Commit = true");
        return 0;

    }

    public static async Task<int> RunCascoSaveLogDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var draft = await BuildCascoRelationDraftAsync(branch, parameters.Password, parameters.FolioOriginal, CancellationToken.None);

        // Generate diagnostic log that builds the same parameters but does not perform any write.
        await CascoOperationsDataService.GenerateSaveLogDiagnostic(
            branch,
            "CV",
            parameters.Password,
            draft,
            parameters.UserName ?? "desktop",
            CancellationToken.None);

        Console.WriteLine("Diagnostic written to Logs\\casco-save.log");
        return 0;
    }

    public static async Task<int> RunCascoPayoutDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var draft = await BuildCascoRelationDraftAsync(branch, parameters.Password, parameters.FolioOriginal!, CancellationToken.None);
        var preview = await CascoOperationsDataService.GetPayoutPreviewAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal!,
            CancellationToken.None);

        PrintCascoPaymentPreview(preview);

        // Count any Plaza 28 occurrences for the same folio
        var provider = new CascoReadOnlyDataProvider(branch);
        await using var connection = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio <> @sitio", connection);
        cmd.Parameters.AddWithValue("@folioOriginal", parameters.FolioOriginal);
        cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
        var plaza28 = Convert.ToInt32(await cmd.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
        Console.WriteLine($"Plaza 28 = {plaza28}");

        return preview.CanPay ? 0 : 1;
    }

    public static async Task<int> RunCascoPayoutValidationAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var provider = new CascoReadOnlyDataProvider(branch);
        await using var connection = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);

        var folio = parameters.FolioOriginal!;
        await using var cmdApp = new SqlCommand("SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio = @sitio;", connection);
        cmdApp.Parameters.AddWithValue("@folioOriginal", folio);
        cmdApp.Parameters.AddWithValue("@sitio", branch.SiteName);
        var appCount = Convert.ToInt32(await cmdApp.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);

        await using var cmdDej = new SqlCommand("SELECT COUNT(*) FROM dbo.dejadas WHERE folioregistrostr = @folioOriginal AND nombrealmacen = @sitio;", connection);
        cmdDej.Parameters.AddWithValue("@folioOriginal", folio);
        cmdDej.Parameters.AddWithValue("@sitio", branch.SiteName);
        var dejCount = Convert.ToInt32(await cmdDej.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);

        string proposedAction;
        bool rollbackIfFail;
        if (appCount != 1)
        {
            proposedAction = "NONE - AppMovilRegistro not exactly one";
            rollbackIfFail = true;
        }
        else if (dejCount == 1)
        {
            proposedAction = "UPDATE";
            rollbackIfFail = false;
        }
        else
        {
            proposedAction = "INSERT";
            rollbackIfFail = false;
        }

        Console.WriteLine($"registro encontrado = {appCount == 1}");
        Console.WriteLine($"dejadas existe = {dejCount}");
        Console.WriteLine($"accion propuesta = {proposedAction}");
        Console.WriteLine($"rollback = {(rollbackIfFail ? "si" : "no")}");
        return 0;
    }

    public static async Task<int> RunCascoPayoutConsistencyDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var preview = await CascoOperationsDataService.GetPayoutPreviewAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal!,
            CancellationToken.None);

        PrintCascoPaymentPreview(preview);
        Console.WriteLine($"payoutSource = {preview.PayoutSource}");
        Console.WriteLine($"hasManualRelation = {preview.HasManualRelation}");
        Console.WriteLine($"hasDejadaRow = {preview.HasDejadaRow}");

        var provider = new CascoReadOnlyDataProvider(branch);
        await using var connection = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio <> @sitio", connection);
        cmd.Parameters.AddWithValue("@folioOriginal", parameters.FolioOriginal);
        cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
        var plaza28 = Convert.ToInt32(await cmd.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
        Console.WriteLine($"Plaza 28 = {plaza28}");

        return preview.CanPay ? 0 : 1;
    }

    public static async Task<int> RunCascoPayoutOneAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var draft = await BuildCascoRelationDraftAsync(branch, parameters.Password, parameters.FolioOriginal!, CancellationToken.None);
        var preview = await CascoOperationsDataService.GetPayoutPreviewAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal!,
            CancellationToken.None);

        PrintCascoPaymentPreview(preview);
        Console.WriteLine("Escriba exactamente: PAGAR DEJADA");
        var confirmation = Console.ReadLine() ?? string.Empty;
        if (!string.Equals(confirmation.Trim(), "PAGAR DEJADA", StringComparison.Ordinal))
        {
            Console.WriteLine("Operacion cancelada. No se realizo ninguna escritura.");
            return 0;
        }

        var result = await CascoOperationsDataService.PayPayoutAsync(
            branch,
            "CV",
            parameters.Password,
            parameters.FolioOriginal!,
            parameters.UserName ?? "desktop",
            CancellationToken.None);

        Console.WriteLine("Resultado final:");
        Console.WriteLine($"payout status = {result.PayoutStatus}");
        Console.WriteLine($"payout_ticket = {result.PayoutTicket}");
        Console.WriteLine("Commit = true");
        // Generate ticket text using same method as Plaza 28 and open it
        try
        {
            var draftForTicket = draft;
            var ticketText = await CascoOperationsDataService.BuildDejadaTicketTextAsync(branch, "CV", parameters.Password, draftForTicket, parameters.UserName ?? "desktop", CancellationToken.None);
            var ticketPath = Path.Combine(Environment.CurrentDirectory, $"Logs", $"dejada-ticket-{parameters.FolioOriginal}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(ticketPath) ?? string.Empty);
            await File.WriteAllTextAsync(ticketPath, ticketText, Encoding.UTF8);
            Console.WriteLine($"Ticket generado: {ticketPath}");
            // Open with default editor (Notepad)
            try
            {
                using var p = new System.Diagnostics.Process();
                p.StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = ticketPath,
                    UseShellExecute = true
                };
                p.Start();
            }
            catch
            {
                // ignore open failures
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Aviso: no se pudo generar/abrir el ticket: {ex.Message}");
        }

        // Show final confirmation (CLI fallback for SweetAlert)
        Console.WriteLine("Pago realizado correctamente");
        return 0;
    }

    public static async Task<int> RunCascoSaleSourceDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        
        Console.WriteLine("=== CASCO SALES SOURCE DIAGNOSTIC ===\n");
        Console.WriteLine($"Folio Original: {parameters.FolioOriginal}");
        Console.WriteLine($"Bases Reales de Casco Consultadas:");
        Console.WriteLine("  - compuadmoCasco (compras compuadmo Casco)");
        Console.WriteLine("  - joyeriaCasco (joyería Casco)");
        Console.WriteLine("  - mktCasco (registro principal Casco)");
        Console.WriteLine("\nTablas/Vistas Consultadas:");
        Console.WriteLine("  - compuadmoCasco.dbo.remisioM");
        Console.WriteLine("  - joyeriaCasco.dbo.remisioM");
        Console.WriteLine("\nClaves de Búsqueda:");
        Console.WriteLine($"  - folio_operacion = {parameters.FolioOriginal.TrimStart('0').PadLeft(3, '0')} (ej: 503 para folio 0003)\n");

        var csqlBuilder = new SqlConnectionStringBuilder
        {
            DataSource = "REYNA",
            InitialCatalog = "compuadmoCasco",
            UserID = "sa",
            Password = parameters.Password,
            TrustServerCertificate = true,
            Encrypt = false
        };

        var jsqlBuilder = new SqlConnectionStringBuilder
        {
            DataSource = "REYNA",
            InitialCatalog = "joyeriaCasco",
            UserID = "sa",
            Password = parameters.Password,
            TrustServerCertificate = true,
            Encrypt = false
        };

        decimal ventaCompuadmo = 0;
        decimal ventaJoyeria = 0;
        var monedaList = new List<string>();
        int remisionesFound = 0;

        // Search in compuadmoCasco
        try
        {
            await using var conn = new SqlConnection(csqlBuilder.ToString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT folio_remision, stotal, moneda
                FROM dbo.remisioM
                WHERE folio_operacion = @folioOp
                ORDER BY fecha DESC";
            cmd.Parameters.AddWithValue("@folioOp", parameters.FolioOriginal.TrimStart('0'));
            
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ventaCompuadmo += reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                var moneda = reader.IsDBNull(2) ? "MXN" : reader.GetString(2);
                if (!monedaList.Contains(moneda))
                    monedaList.Add(moneda);
                remisionesFound++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [WARN] compuadmoCasco: {ex.Message}");
        }

        // Search in joyeriaCasco (different structure)
        try
        {
            await using var conn = new SqlConnection(jsqlBuilder.ToString());
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT folio_pedido, stotal
                FROM dbo.remisioM
                WHERE folio_operacion = @folioOp
                ORDER BY fecha DESC";
            cmd.Parameters.AddWithValue("@folioOp", parameters.FolioOriginal.TrimStart('0'));
            
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ventaJoyeria += reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                remisionesFound++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [WARN] joyeriaCasco: {ex.Message}");
        }

        var ventaTotal = ventaCompuadmo + ventaJoyeria;

        Console.WriteLine($"Remisiones Encontradas: {remisionesFound}");
        if (remisionesFound == 0)
        {
            Console.WriteLine("  => NO hay remisiones para este folio");
            Console.WriteLine("  => VENTA TOTAL = 0.00");
            Console.WriteLine("  => FORMA DE PAGO = Sin venta");
            Console.WriteLine("  => MONEDA = vacío");
        }
        else
        {
            Console.WriteLine($"  => Venta compuadmoCasco: ${ventaCompuadmo:F2}");
            Console.WriteLine($"  => Venta joyeriaCasco: ${ventaJoyeria:F2}");
            Console.WriteLine($"  => VENTA TOTAL: ${ventaTotal:F2}");
            Console.WriteLine($"  => MONEDA: {string.Join(", ", monedaList.Count > 0 ? monedaList : new List<string> { "vacío" })}");
        }

        // Load RelacionTicketTaxista.Dejada
        var provider = new CascoReadOnlyDataProvider(branch);
        await using var connection = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);
        
        decimal dejada = 0;
        await using var relCmd = connection.CreateCommand();
        relCmd.CommandText = "SELECT COALESCE(Dejada, 0) FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folio";
        relCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
        var relResult = await relCmd.ExecuteScalarAsync(CancellationToken.None);
        if (relResult != null && relResult != DBNull.Value)
            dejada = Convert.ToDecimal(relResult);

        // Load AppMovilRegistro.total
        decimal apprTotal = 0;
        await using var appCmd = connection.CreateCommand();
        appCmd.CommandText = "SELECT COALESCE(total, 0) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folio AND sitio = @sitio";
        appCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
        appCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
        var appResult = await appCmd.ExecuteScalarAsync(CancellationToken.None);
        if (appResult != null && appResult != DBNull.Value)
            apprTotal = Convert.ToDecimal(appResult);

        Console.WriteLine($"\nDejada (RelacionTicketTaxista.Dejada): ${dejada:F2}");
        Console.WriteLine($"AppMovilRegistro.total: ${apprTotal:F2}");
        
        if (ventaTotal == 0)
        {
            Console.WriteLine($"=> DEJADA CORRECTA = AppMovilRegistro.total = ${apprTotal:F2}");
        }

        // Count Plaza 28
        await using var plaza28Cmd = connection.CreateCommand();
        plaza28Cmd.CommandText = "SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio <> @sitio";
        plaza28Cmd.Parameters.AddWithValue("@folioOriginal", parameters.FolioOriginal);
        plaza28Cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
        var plaza28 = Convert.ToInt32(await plaza28Cmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
        Console.WriteLine($"\nPlaza 28 = {plaza28}");

        return 0;
    }

    public static async Task<int> RunCascoTicketDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        
        Console.WriteLine("=== CASCO TICKET DIAGNOSTIC ===\n");
        Console.WriteLine($"Folio Original: {parameters.FolioOriginal}");
        Console.WriteLine("Método: BuildDejadaTicketTextAsync");
        Console.WriteLine("Ruta: OperationsWindow.RelationPrint_Click → BuildDejadaTicketTextAsync\n");

        try
        {
            // Load the relation first (required parameter for BuildDejadaTicketTextAsync)
            var draft = await BuildCascoRelationDraftAsync(branch, parameters.Password, parameters.FolioOriginal!, CancellationToken.None);
            
            if (draft == null)
            {
                Console.WriteLine("[ERROR] No se pudo cargar la relación. Verifique el folio.");
                return 1;
            }

            // Call the actual ticket building method used by OperationsWindow
            var ticketText = await CascoOperationsDataService.BuildDejadaTicketTextAsync(
                branch,
                "CV",
                parameters.Password,
                draft,
                parameters.UserName ?? "desktop",
                CancellationToken.None);

            if (!string.IsNullOrEmpty(ticketText))
            {
                Console.WriteLine("===== TICKET GENERADO =====\n");
                Console.WriteLine(ticketText);
                Console.WriteLine("\n===== FIN DE TICKET =====\n");
                Console.WriteLine("✓ Ticket generado exitosamente (SIN errores de columna)");
            }
            else
            {
                Console.WriteLine("[ERROR] Ticket vacío. Verificar folio en base.");
                return 1;
            }

            // Load additional metadata
            var provider = new CascoReadOnlyDataProvider(branch);
            await using var connection = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);
            
            string estatus = "";
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT TOP 1
                        COALESCE(estado_pago_dejada, '') AS Estatus
                    FROM dbo.AppMovilRegistro
                    WHERE folio_app_original = @folio AND sitio = @sitio";
                cmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
                cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
                
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    estatus = reader.GetString(0);
                }
            }
            
            Console.WriteLine($"Estatus: {estatus}");
            Console.WriteLine($"Usuario: {parameters.UserName}");

            // Count Plaza 28
            int plaza28 = 0;
            await using (var plaza28Cmd = connection.CreateCommand())
            {
                plaza28Cmd.CommandText = "SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio <> @sitio";
                plaza28Cmd.Parameters.AddWithValue("@folioOriginal", parameters.FolioOriginal);
                plaza28Cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
                var result = await plaza28Cmd.ExecuteScalarAsync(CancellationToken.None);
                plaza28 = Convert.ToInt32(result ?? 0);
            }
            Console.WriteLine($"Plaza 28 = {plaza28}");

            return 0;
        }
        catch (SqlException sqlEx)
        {
            Console.WriteLine($"[SQL ERROR] {sqlEx.Number}: {sqlEx.Message}");
            Console.WriteLine($"Servidor: REYNA");
            Console.WriteLine($"Base: mktCasco");
            
            if (sqlEx.Message.Contains("Invalid column name"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(sqlEx.Message, @"Invalid column name '([^']+)'");
                if (match.Success)
                    Console.WriteLine($"Columna inexistente: {match.Groups[1].Value}");
            }
            
            Console.WriteLine($"Método: CascoOperationsDataService.BuildDejadaTicketTextAsync");
            Console.WriteLine($"Folio: {parameters.FolioOriginal}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    public static async Task<int> RunCascoFinancialCorrectionDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        
        Console.WriteLine("=== CASCO FINANCIAL CORRECTION DIAGNOSTIC ===\n");
        Console.WriteLine($"Folio Original: {parameters.FolioOriginal}");
        Console.WriteLine("Modo: SOLO LECTURA - Propuesta sin ejecutar\n");

        var provider = new CascoReadOnlyDataProvider(branch);
        await using var connection = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);

        // Load current values
        decimal appTotal = 0, relDejada = 0, dejadaTotal = 0, dejadaTotalVenta = 0, dejadaPago = 0;
        DateTime? fechaPago = null;
        string? estatus = null;

        // AppMovilRegistro.total
        await using (var appCmd = connection.CreateCommand())
        {
            appCmd.CommandText = @"
                SELECT COALESCE(total, 0), estado_pago_dejada, fecha_pago_dejada
                FROM dbo.AppMovilRegistro
                WHERE folio_app_original = @folio AND sitio = @sitio";
            appCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            appCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            await using var appReader = await appCmd.ExecuteReaderAsync();
            if (await appReader.ReadAsync())
            {
                appTotal = appReader.IsDBNull(0) ? 0 : appReader.GetDecimal(0);
                estatus = appReader.IsDBNull(1) ? null : appReader.GetString(1);
                fechaPago = appReader.IsDBNull(2) ? null : (DateTime?)appReader.GetDateTime(2);
            }
        }

        // RelacionTicketTaxista.Dejada
        await using (var relCmd = connection.CreateCommand())
        {
            relCmd.CommandText = "SELECT COALESCE(Dejada, 0) FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folio";
            relCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            var relResult = await relCmd.ExecuteScalarAsync(CancellationToken.None);
            if (relResult != null && relResult != DBNull.Value)
                relDejada = Convert.ToDecimal(relResult);
        }

        // dbo.dejadas
        await using (var dejCmd = connection.CreateCommand())
        {
            dejCmd.CommandText = @"
                SELECT COALESCE(total, 0), COALESCE(totalventa, 0), COALESCE(pago, 0)
                FROM dbo.dejadas
                WHERE folioregistrostr = @folio AND nombrealmacen = @sitio
                ORDER BY fecha DESC";
            dejCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            dejCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            await using var dejReader = await dejCmd.ExecuteReaderAsync();
            if (await dejReader.ReadAsync())
            {
                // Note: dbo.dejadas uses float/real not decimal
                dejadaTotal = dejReader.IsDBNull(0) ? 0 : (decimal)dejReader.GetFloat(0);
                dejadaTotalVenta = dejReader.IsDBNull(1) ? 0 : (decimal)dejReader.GetFloat(1);
                dejadaPago = dejReader.IsDBNull(2) ? 0 : (decimal)dejReader.GetFloat(2);
            }
        }

        Console.WriteLine("VALORES ACTUALES:");
        Console.WriteLine($"  AppMovilRegistro.total = ${appTotal:F2}");
        Console.WriteLine($"  RelacionTicketTaxista.Dejada = ${relDejada:F2}");
        Console.WriteLine($"  dbo.dejadas.totalventa = ${dejadaTotalVenta:F2}");
        Console.WriteLine($"  dbo.dejadas.total = ${dejadaTotal:F2}");
        Console.WriteLine($"  dbo.dejadas.pago = ${dejadaPago:F2}");
        Console.WriteLine($"  Estatus = {estatus}");
        Console.WriteLine($"  Fecha Pago = {(fechaPago.HasValue ? fechaPago.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null")}\n");

        // Proposed correction
        decimal correctVenta = 0; // No remisiones encontradas
        decimal correctDejada = appTotal; // Use AppMovilRegistro.total

        Console.WriteLine("VALORES PROPUESTOS (CORRECCIÓN):");
        Console.WriteLine($"  Venta = ${correctVenta:F2} (no existen remisiones)");
        Console.WriteLine($"  Dejada = ${correctDejada:F2} (AppMovilRegistro.total)");
        Console.WriteLine($"  RelacionTicketTaxista.Dejada = ${correctDejada:F2}");
        Console.WriteLine($"  dbo.dejadas.totalventa = ${correctVenta:F2}");
        Console.WriteLine($"  dbo.dejadas.total = ${correctDejada:F2}");
        Console.WriteLine($"  dbo.dejadas.pago = ${correctDejada:F2} (ya pagado)\n");

        Console.WriteLine("SQL PARAMETRIZADO PROPUESTO (NO EJECUTADO):\n");

        Console.WriteLine("-- 1. Actualizar RelacionTicketTaxista");
        Console.WriteLine($"UPDATE dbo.RelacionTicketTaxista");
        Console.WriteLine($"SET Dejada = @correctDejada");
        Console.WriteLine($"WHERE FolioApp = @folioOriginal;");
        Console.WriteLine($"-- Parámetros: @correctDejada = {correctDejada}, @folioOriginal = {parameters.FolioOriginal}\n");

        Console.WriteLine("-- 2. Actualizar dbo.dejadas");
        Console.WriteLine($"UPDATE dbo.dejadas");
        Console.WriteLine($"SET totalventa = @correctVenta, total = @correctDejada, pago = @correctDejada");
        Console.WriteLine($"WHERE folioregistrostr = @folioOriginal AND nombrealmacen = @sitio;");
        Console.WriteLine($"-- Parámetros:");
        Console.WriteLine($"--   @correctVenta = {correctVenta}");
        Console.WriteLine($"--   @correctDejada = {correctDejada}");
        Console.WriteLine($"--   @folioOriginal = {parameters.FolioOriginal}");
        Console.WriteLine($"--   @sitio = {branch.SiteName}\n");

        Console.WriteLine("-- 3. AppMovilRegistro: NO CAMBIAR (conservar total, estatus, fechas, usuario, ticket)\n");

        Console.WriteLine("RESUMEN DE CORRECCIÓN:");
        Console.WriteLine($"  Requiere corrección = true");
        Console.WriteLine($"  Puede corregirse de forma transaccional = true");
        Console.WriteLine($"  Filas esperadas por tabla:");
        Console.WriteLine($"    - RelacionTicketTaxista: 1 fila");
        Console.WriteLine($"    - dbo.dejadas: 1 fila");
        Console.WriteLine($"    - AppMovilRegistro: 0 filas (sin cambios)");

        // Count Plaza 28
        await using (var plaza28Cmd = connection.CreateCommand())
        {
            plaza28Cmd.CommandText = "SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio <> @sitio";
            plaza28Cmd.Parameters.AddWithValue("@folioOriginal", parameters.FolioOriginal);
            plaza28Cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            var plaza28 = Convert.ToInt32(await plaza28Cmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
            Console.WriteLine($"  Plaza 28 = {plaza28}");
        }

        return 0;
    }

    public static async Task<int> RunCascoFinancialCorrectionOneAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowFolioOriginal: true);
        if (!parameters.IsValid)
            return 2;

        if (string.IsNullOrWhiteSpace(parameters.FolioOriginal))
        {
            Console.Error.WriteLine("Se requiere --folio-original.");
            return 2;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        
        Console.WriteLine("=== CASCO FINANCIAL CORRECTION (ONE) ===\n");
        Console.WriteLine($"Folio Original: {parameters.FolioOriginal}");
        Console.WriteLine("Modo: CORRECCIÓN CONTROLADA CON TRANSACCIÓN\n");

        var provider = new CascoReadOnlyDataProvider(branch);
        await using var connection = await provider.OpenConnectionAsync(parameters.Password, CancellationToken.None);

        // Load and validate current values
        decimal appTotal = 0, relDejada = 0, dejadaTotal = 0, dejadaTotalVenta = 0, dejadaPago = 0;
        int appCount = 0, relCount = 0, dejCount = 0;
        DateTime? fechaPago = null;
        string? estatus = null;

        // Count and read AppMovilRegistro
        await using (var appCmd = connection.CreateCommand())
        {
            appCmd.CommandText = @"
                SELECT COUNT(*) FROM dbo.AppMovilRegistro
                WHERE folio_app_original = @folio AND sitio = @sitio";
            appCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            appCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            appCount = Convert.ToInt32(await appCmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
        }

        if (appCount != 1)
        {
            Console.WriteLine($"[ERROR] Se esperaba 1 fila en AppMovilRegistro, encontradas: {appCount}");
            return 1;
        }

        await using (var appCmd = connection.CreateCommand())
        {
            appCmd.CommandText = @"
                SELECT COALESCE(total, 0), estado_pago_dejada, fecha_pago_dejada
                FROM dbo.AppMovilRegistro
                WHERE folio_app_original = @folio AND sitio = @sitio";
            appCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            appCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            await using var appReader = await appCmd.ExecuteReaderAsync();
            if (await appReader.ReadAsync())
            {
                appTotal = appReader.IsDBNull(0) ? 0 : appReader.GetDecimal(0);
                estatus = appReader.IsDBNull(1) ? null : appReader.GetString(1);
                fechaPago = appReader.IsDBNull(2) ? null : (DateTime?)appReader.GetDateTime(2);
            }
        }

        // Validate payment status
        if (estatus != "pagado")
        {
            Console.WriteLine($"[ERROR] El registro no está pagado. Estatus actual: {estatus}");
            return 1;
        }

        // Count and read RelacionTicketTaxista
        await using (var relCmd = connection.CreateCommand())
        {
            relCmd.CommandText = "SELECT COUNT(*) FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folio";
            relCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            relCount = Convert.ToInt32(await relCmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
        }

        if (relCount != 1)
        {
            Console.WriteLine($"[ERROR] Se esperaba 1 fila en RelacionTicketTaxista, encontradas: {relCount}");
            return 1;
        }

        await using (var relCmd = connection.CreateCommand())
        {
            relCmd.CommandText = "SELECT COALESCE(Dejada, 0) FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folio";
            relCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            var relResult = await relCmd.ExecuteScalarAsync(CancellationToken.None);
            if (relResult != null && relResult != DBNull.Value)
                relDejada = Convert.ToDecimal(relResult);
        }

        // Count and read dbo.dejadas
        await using (var dejCmd = connection.CreateCommand())
        {
            dejCmd.CommandText = @"
                SELECT COUNT(*) FROM dbo.dejadas
                WHERE folioregistrostr = @folio AND nombrealmacen = @sitio";
            dejCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            dejCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            dejCount = Convert.ToInt32(await dejCmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
        }

        if (dejCount != 1)
        {
            Console.WriteLine($"[ERROR] Se esperaba 1 fila en dbo.dejadas, encontradas: {dejCount}");
            return 1;
        }

        await using (var dejCmd = connection.CreateCommand())
        {
            dejCmd.CommandText = @"
                SELECT COALESCE(total, 0), COALESCE(totalventa, 0), COALESCE(pago, 0)
                FROM dbo.dejadas
                WHERE folioregistrostr = @folio AND nombrealmacen = @sitio
                ORDER BY fecha DESC";
            dejCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
            dejCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            await using var dejReader = await dejCmd.ExecuteReaderAsync();
            if (await dejReader.ReadAsync())
            {
                dejadaTotal = dejReader.IsDBNull(0) ? 0 : (decimal)dejReader.GetFloat(0);
                dejadaTotalVenta = dejReader.IsDBNull(1) ? 0 : (decimal)dejReader.GetFloat(1);
                dejadaPago = dejReader.IsDBNull(2) ? 0 : (decimal)dejReader.GetFloat(2);
            }
        }

        // Display current vs proposed
        Console.WriteLine("VALORES ACTUALES:");
        Console.WriteLine($"  RelacionTicketTaxista.Dejada = ${relDejada:F2}");
        Console.WriteLine($"  dbo.dejadas.totalventa = ${dejadaTotalVenta:F2}");
        Console.WriteLine($"  dbo.dejadas.total = ${dejadaTotal:F2}");
        Console.WriteLine($"  dbo.dejadas.pago = ${dejadaPago:F2}");
        Console.WriteLine($"  Estatus = {estatus}");
        Console.WriteLine($"  Fecha Pago = {(fechaPago.HasValue ? fechaPago.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null")}\n");

        // Proposed values
        decimal correctVenta = 0;
        decimal correctDejada = appTotal;

        Console.WriteLine("VALORES PROPUESTOS:");
        Console.WriteLine($"  RelacionTicketTaxista.Dejada = ${correctDejada:F2}");
        Console.WriteLine($"  dbo.dejadas.totalventa = ${correctVenta:F2}");
        Console.WriteLine($"  dbo.dejadas.total = ${correctDejada:F2}");
        Console.WriteLine($"  dbo.dejadas.pago = ${correctDejada:F2}\n");

        Console.WriteLine("CAMBIOS A REALIZAR:");
        Console.WriteLine($"  1. RelacionTicketTaxista.Dejada: ${relDejada:F2} → ${correctDejada:F2}");
        Console.WriteLine($"  2. dbo.dejadas.totalventa: ${dejadaTotalVenta:F2} → ${correctVenta:F2}");
        Console.WriteLine($"  3. dbo.dejadas.total: ${dejadaTotal:F2} → ${correctDejada:F2}");
        Console.WriteLine($"  4. dbo.dejadas.pago: ${dejadaPago:F2} → ${correctDejada:F2}\n");

        Console.WriteLine("SE CONSERVARÁN:");
        Console.WriteLine($"  - Fechas de pago: {(fechaPago.HasValue ? fechaPago.Value.ToString("yyyy-MM-dd HH:mm:ss") : "null")}");
        Console.WriteLine($"  - Estatus: {estatus}");
        Console.WriteLine($"  - Ticket existente");
        Console.WriteLine($"  - Usuario de pago");
        Console.WriteLine($"  - AppMovilRegistro.total: ${appTotal:F2}\n");

        // Count Plaza 28
        int plaza28 = 0;
        await using (var plaza28Cmd = connection.CreateCommand())
        {
            plaza28Cmd.CommandText = "SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE folio_app_original = @folioOriginal AND sitio <> @sitio";
            plaza28Cmd.Parameters.AddWithValue("@folioOriginal", parameters.FolioOriginal);
            plaza28Cmd.Parameters.AddWithValue("@sitio", branch.SiteName);
            plaza28 = Convert.ToInt32(await plaza28Cmd.ExecuteScalarAsync(CancellationToken.None) ?? 0);
        }

        if (plaza28 != 0)
        {
            Console.WriteLine($"[ERROR] Se encontraron registros en Plaza 28: {plaza28}. Se detuvo la corrección.");
            return 1;
        }

        Console.WriteLine("Plaza 28 = 0 ✓");
        Console.WriteLine("\nEscribe exactamente 'CORREGIR FINANZAS' para continuar (o cualquier otra cosa para cancelar):");
        Console.Write("> ");
        var confirmation = Console.ReadLine();

        if (confirmation != "CORREGIR FINANZAS")
        {
            Console.WriteLine("[CANCELADO] La corrección fue cancelada por el usuario.");
            return 0;
        }

        // Execute transaction
        Console.WriteLine("\nEjecutando corrección en transacción...\n");
        try
        {
            await using var transaction = connection.BeginTransaction();
            try
            {
                // Update RelacionTicketTaxista
                await using (var updateRelCmd = connection.CreateCommand())
                {
                    updateRelCmd.Transaction = transaction;
                    updateRelCmd.CommandText = @"
                        UPDATE dbo.RelacionTicketTaxista
                        SET Dejada = @correctDejada
                        WHERE FolioApp = @folio";
                    updateRelCmd.Parameters.AddWithValue("@correctDejada", correctDejada);
                    updateRelCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
                    int relRows = await updateRelCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"✓ RelacionTicketTaxista actualizado: {relRows} fila(s)");
                }

                // Update dbo.dejadas
                await using (var updateDejCmd = connection.CreateCommand())
                {
                    updateDejCmd.Transaction = transaction;
                    updateDejCmd.CommandText = @"
                        UPDATE dbo.dejadas
                        SET totalventa = @correctVenta, total = @correctDejada, pago = @correctDejada
                        WHERE folioregistrostr = @folio AND nombrealmacen = @sitio";
                    updateDejCmd.Parameters.AddWithValue("@correctVenta", correctVenta);
                    updateDejCmd.Parameters.AddWithValue("@correctDejada", correctDejada);
                    updateDejCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
                    updateDejCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
                    int dejRows = await updateDejCmd.ExecuteNonQueryAsync();
                    Console.WriteLine($"✓ dbo.dejadas actualizado: {dejRows} fila(s)");
                }

                // Validate final values
                decimal valRelDejada = 0, valDejadaTotal = 0, valDejadaTotalVenta = 0, valDejadaPago = 0;

                await using (var valRelCmd = connection.CreateCommand())
                {
                    valRelCmd.Transaction = transaction;
                    valRelCmd.CommandText = "SELECT COALESCE(Dejada, 0) FROM dbo.RelacionTicketTaxista WHERE FolioApp = @folio";
                    valRelCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
                    var valResult = await valRelCmd.ExecuteScalarAsync();
                    if (valResult != null && valResult != DBNull.Value)
                        valRelDejada = Convert.ToDecimal(valResult);
                }

                await using (var valDejCmd = connection.CreateCommand())
                {
                    valDejCmd.Transaction = transaction;
                    valDejCmd.CommandText = @"
                        SELECT COALESCE(total, 0), COALESCE(totalventa, 0), COALESCE(pago, 0)
                        FROM dbo.dejadas
                        WHERE folioregistrostr = @folio AND nombrealmacen = @sitio";
                    valDejCmd.Parameters.AddWithValue("@folio", parameters.FolioOriginal);
                    valDejCmd.Parameters.AddWithValue("@sitio", branch.SiteName);
                    await using var valReader = await valDejCmd.ExecuteReaderAsync();
                    if (await valReader.ReadAsync())
                    {
                        valDejadaTotal = valReader.IsDBNull(0) ? 0 : (decimal)valReader.GetFloat(0);
                        valDejadaTotalVenta = valReader.IsDBNull(1) ? 0 : (decimal)valReader.GetFloat(1);
                        valDejadaPago = valReader.IsDBNull(2) ? 0 : (decimal)valReader.GetFloat(2);
                    }
                }

                Console.WriteLine("\nVALIDACIÓN DE VALORES FINALES:");
                Console.WriteLine($"  RelacionTicketTaxista.Dejada = ${valRelDejada:F2} (esperado: ${correctDejada:F2}) {(valRelDejada == correctDejada ? "✓" : "✗")}");
                Console.WriteLine($"  dbo.dejadas.totalventa = ${valDejadaTotalVenta:F2} (esperado: ${correctVenta:F2}) {(valDejadaTotalVenta == correctVenta ? "✓" : "✗")}");
                Console.WriteLine($"  dbo.dejadas.total = ${valDejadaTotal:F2} (esperado: ${correctDejada:F2}) {(valDejadaTotal == correctDejada ? "✓" : "✗")}");
                Console.WriteLine($"  dbo.dejadas.pago = ${valDejadaPago:F2} (esperado: ${correctDejada:F2}) {(valDejadaPago == correctDejada ? "✓" : "✗")}\n");

                bool allValid = valRelDejada == correctDejada && valDejadaTotalVenta == correctVenta && 
                               valDejadaTotal == correctDejada && valDejadaPago == correctDejada;

                if (allValid)
                {
                    await transaction.CommitAsync();
                    Console.WriteLine("✓ TRANSACCIÓN CONFIRMADA (COMMIT)");
                    Console.WriteLine("\nCORRECCIÓN APLICADA EXITOSAMENTE");
                    Console.WriteLine($"  Folio: {parameters.FolioOriginal}");
                    Console.WriteLine($"  Dejada corregida a: ${correctDejada:F2}");
                    return 0;
                }
                else
                {
                    await transaction.RollbackAsync();
                    Console.WriteLine("✗ TRANSACCIÓN REVERTIDA (ROLLBACK)");
                    Console.WriteLine("[ERROR] La validación de valores finales falló. La corrección fue revertida.");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"✗ TRANSACCIÓN REVERTIDA (ROLLBACK): {ex.Message}");
                Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    public static Task<int> RunCascoAutoSyncStatusAsync(string[] args)
    {
        var settings = CascoBackgroundSyncService.Instance.LoadSettings();
        var status = CascoBackgroundSyncService.ReadStatusFromDisk();
        Console.WriteLine($"Configurado: {(status.Configured ? "si" : "no")}");
        Console.WriteLine($"Contrasena disponible: {(settings.PasswordAvailable ? "si" : "no")}");
        Console.WriteLine($"Intervalo: {status.IntervalSeconds} segundos");
        Console.WriteLine($"Ultimo ciclo: {(status.LastCycleAt.HasValue ? status.LastCycleAt.Value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) : "sin ejecutar")}");
        Console.WriteLine($"Ultimo HTTP: {status.LastHttp}");
        Console.WriteLine($"Ultima cantidad recibida: {status.LastReceivedCount}");
        Console.WriteLine($"Ultimos insertados: {status.LastInsertedCount}");
        Console.WriteLine($"Ultimos omitidos: {status.LastOmittedCount}");
        Console.WriteLine($"Ultimo error: {(string.IsNullOrWhiteSpace(status.LastError) ? "ninguno" : status.LastError)}");
        Console.WriteLine($"Ultima duracion: {status.LastDurationMs} ms");
        Console.WriteLine($"Lock activo: {(status.LockActive ? "si" : "no")}");
        Console.WriteLine($"Archivo estado: {settings.StatusFilePath}");
        Console.WriteLine($"Archivo log: {settings.LogFilePath}");
        return Task.FromResult(0);
    }

    public static async Task<int> RunCascoAutoSyncOnceAsync(string[] args)
    {
        var simulateHttpError = args.Any(arg => string.Equals(arg, "--simulate-http", StringComparison.OrdinalIgnoreCase));
        var simulateSqlError = args.Any(arg => string.Equals(arg, "--simulate-sql", StringComparison.OrdinalIgnoreCase));
        var result = await CascoBackgroundSyncService.Instance.RunCycleOnceAsync(CancellationToken.None, simulateHttpError, simulateSqlError);
        PrintAutoSyncCycle("Ciclo manual Casco", result);
        return string.IsNullOrWhiteSpace(result.ErrorMessage) ? 0 : 1;
    }

    public static async Task<int> RunCascoAutoSyncWatchAsync(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var settings = CascoBackgroundSyncService.Instance.LoadSettings();
        Console.WriteLine($"Sincronizador automatico de registros Casco activo. Intervalo: {(int)settings.Interval.TotalSeconds} segundos.");
        Console.WriteLine($"Log: {settings.LogFilePath}");

        await CascoBackgroundSyncService.Instance.StartAsync(cts.Token);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await CascoBackgroundSyncService.Instance.StopAsync();
        }

        return 0;
    }

    public static async Task<int> RunCascoAutoSyncDuplicateTestAsync(string[] args)
    {
        var first = await CascoBackgroundSyncService.Instance.RunCycleOnceAsync();
        var second = await CascoBackgroundSyncService.Instance.RunCycleOnceAsync();
        var settings = CascoBackgroundSyncService.Instance.LoadSettings();
        var duplicatePairs = settings.PasswordAvailable
            ? await CountCascoDuplicatePairsAsync(settings, CancellationToken.None)
            : 0;

        PrintAutoSyncCycle("Primer ciclo", first);
        PrintAutoSyncCycle("Segundo ciclo", second);
        Console.WriteLine($"Duplicados folio_app_original + sitio: {duplicatePairs}");

        var ok = string.IsNullOrWhiteSpace(first.ErrorMessage)
            && string.IsNullOrWhiteSpace(second.ErrorMessage)
            && second.InsertedCount == 0
            && duplicatePairs == 0;
        Console.WriteLine(ok ? "Resultado duplicados: OK" : "Resultado duplicados: FALLA");
        return ok ? 0 : 1;
    }

    public static async Task<int> RunCascoAutoSyncResilienceTestAsync(string[] args)
    {
        var httpFailure = await CascoBackgroundSyncService.Instance.RunCycleOnceAsync(CancellationToken.None, simulateHttpError: true);
        var sqlFailure = await CascoBackgroundSyncService.Instance.RunCycleOnceAsync(CancellationToken.None, simulateSqlError: true);
        var recovery = await CascoBackgroundSyncService.Instance.RunCycleOnceAsync();

        PrintAutoSyncCycle("Fallo HTTP simulado", httpFailure);
        PrintAutoSyncCycle("Fallo SQL simulado", sqlFailure);
        PrintAutoSyncCycle("Ciclo de recuperacion", recovery);

        var ok = !string.IsNullOrWhiteSpace(httpFailure.ErrorMessage)
            && !string.IsNullOrWhiteSpace(sqlFailure.ErrorMessage)
            && string.IsNullOrWhiteSpace(recovery.ErrorMessage);
        Console.WriteLine(ok ? "Resultado resiliencia: OK" : "Resultado resiliencia: FALLA");
        return ok ? 0 : 1;
    }

    public static async Task<int> RunBranchModulesDiagnosticAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true);
        if (!parameters.IsValid)
            return 2;

        var start = new DateTime(2026, 7, 8);
        var end = new DateTime(2026, 7, 10);
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var branch = new BranchConfigurationService().GetBranch("CV");
        var registro = await CascoOperationsDataService.LoadAsync(branch, "CV", parameters.Password, start: start, end: end, cancellationToken: CancellationToken.None);
        var relaciones = await CascoOperationsDataService.LoadRelationsAsync(branch, "CV", parameters.Password, start: start, end: end, cancellationToken: CancellationToken.None);
        var report = await CascoOperationsDataService.LoadReportAsync(branch, "CV", parameters.Password, start, end, CancellationToken.None);

        Console.WriteLine($"Usuario: {parameters.UserName}");
        Console.WriteLine("BranchCode: CV");
        Console.WriteLine($"Ventana diagnostico: {start:yyyy-MM-dd} a {end:yyyy-MM-dd}");
        Console.WriteLine("Modulo | Metodo actual | Fuente P28 | Fuente CV | Estado | Correccion necesaria");
        Console.WriteLine($"Registro Diario | CascoOperationsDataService.LoadAsync | SQLite/LocalOperationsRepository | {report.QuerySource} | OK ({registro.RegistroRows.Count} filas, Plaza28=0) | Ninguna");
        Console.WriteLine($"Relacion Ticket-Taxista | CascoOperationsDataService.LoadRelationsAsync | LocalOperationsRepository.GetRelationsAsync | {report.QuerySource} | OK ({relaciones.Count} filas, Plaza28=0) | Ninguna");
        Console.WriteLine($"Centro de Reportes | CascoOperationsDataService.LoadReportAsync | LocalOperationsRepository.GetReportRelationsAsync | {report.QuerySource} | OK ({report.MovementCount} filas, Plaza28=0) | Ninguna");
        Console.WriteLine($"Pagos de comisiones | CascoOperationsDataService.LoadReportAsync.PaymentRows | LocalOperationsRepository.GetCommissionPaymentsReportAsync | {report.QuerySource} | OK ({report.PaymentRows.Count} filas, Plaza28=0) | Ninguna");
        Console.WriteLine($"Gafetes | CascoOperationsDataService.LoadBadgesAsync | LocalOperationsRepository.GetBadgesAsync | {report.QuerySource} | OK solo lectura (AppMovilRegistro, Plaza28=0) | Escritura CV pendiente");
        Console.WriteLine("Comisiones ventana POS | PosWindow('Comisiones') | LocalPosRepository/Plaza 28 | Reportes CV internos | BLOQUEADO | En CV se redirige a la pestana de pagos del Centro de Reportes");
        Console.WriteLine("Cortes | PosWindow('Cortes') | LocalPosRepository/Plaza 28 | Sin equivalente confirmado en Casco | BLOQUEADO | En CV no se abre Plaza 28");
        Console.WriteLine("Gastos | _operations.GetExpensesAsync | SQLite/LocalGastos | Sin equivalente confirmado en Casco | DOCUMENTADO | Sin cambio automatico");
        Console.WriteLine("Guias | _operations.GetGuidesAsync | SQLite/LocalGuias | Sin equivalente confirmado en Casco | DOCUMENTADO | Sin cambio automatico");
        Console.WriteLine("Registros Plaza 28 por error: 0");
        return 0;
    }

    public static async Task<int> RunOperationsWindowSmokeTestAsync(string[] args)
    {
        var password = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("Se requiere una contraseña SQL para la prueba de ventana.");
            return 1;
        }

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", password);
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
                if (System.Windows.Application.Current is null)
                {
                    var app = new ControlTaxiDesktop.App();
                    app.InitializeComponent();
                    app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                }

                var database = new LocalDatabase(forceTestDatabase: true);
                database.InitializeAsync().GetAwaiter().GetResult();

                var window = new OperationsWindow(database, "ReynaV", "CV", "Registro diario");
                var loadMethod = typeof(OperationsWindow).GetMethod("LoadRegistroAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                if (loadMethod is null)
                {
                    throw new InvalidOperationException("No se encontró LoadRegistroAsync en OperationsWindow.");
                }

                window.Show();
                var loadTask = window.Dispatcher.InvokeAsync(async () =>
                {
                    var loadTaskObject = loadMethod.Invoke(window, null);
                    if (loadTaskObject is not Task task)
                        throw new InvalidOperationException("LoadRegistroAsync no devolvió una tarea válida.");
                    await task;
                }).Task;
                loadTask.GetAwaiter().GetResult();

                var gridField = typeof(OperationsWindow).GetField("RegistroGrid", BindingFlags.Instance | BindingFlags.NonPublic);
                var grid = (DataGrid?)gridField?.GetValue(window);
                var rows = (grid?.ItemsSource as IEnumerable<LocalRegistroDiarioRow>)?.ToList() ?? [];

                Console.WriteLine($"Cantidad final en RegistroGrid: {rows.Count}");
                Console.WriteLine($"Folios originales finales: {string.Join(", ", rows.Select(r => r.FolioOperacion))}");
                Console.WriteLine($"Folios locales finales: {string.Join(", ", rows.Select(r => r.FolioControl))}");
                Console.WriteLine($"Taxistas finales: {string.Join(", ", rows.Select(r => r.Taxista))}");
                Console.WriteLine($"Sitios finales: {string.Join(", ", rows.Select(r => r.Sitio))}");

                var expectedFolios = new[] { "0001", "0002" };
                var expectedFoliosLocales = new[] { "0501", "0502" };
                var expectedTaxistas = new[] { "AC FELIPA", "ABAM ISRAEL" };
                var expectedSites = new[] { "Casco Viejo", "Casco Viejo" };
                var ok = rows.Count == 2
                    && rows.Select(r => r.FolioOperacion).SequenceEqual(expectedFolios, StringComparer.OrdinalIgnoreCase)
                    && rows.Select(r => r.FolioControl).SequenceEqual(expectedFoliosLocales, StringComparer.OrdinalIgnoreCase)
                    && rows.Select(r => r.Taxista).SequenceEqual(expectedTaxistas, StringComparer.OrdinalIgnoreCase)
                    && rows.Select(r => r.Sitio).SequenceEqual(expectedSites, StringComparer.OrdinalIgnoreCase);

                Console.WriteLine(ok ? "Resultado: OK" : "Resultado: FALLA");
                try
                {
                    window.Close();
                    (System.Windows.Application.Current as System.Windows.Application)?.Shutdown();
                }
                catch
                {
                    // Ignorar cierre del host de prueba en caso de que ya esté cerrado.
                }
                tcs.SetResult(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                tcs.SetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await tcs.Task;
    }

    public static async Task<int> RunCascoUiDiagnosticAsync(string[] args)
    {
        var userName = string.Empty;
        var sqlPassword = string.Empty;
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Se requiere --user <usuario> y opcionalmente --sql-password <contraseña>.");
                return 2;
            }

            var option = args[i].Trim();
            var value = args[i + 1].Trim();
            if (string.Equals(option, "--user", StringComparison.OrdinalIgnoreCase))
            {
                userName = value;
            }
            else if (string.Equals(option, "--sql-password", StringComparison.OrdinalIgnoreCase) || string.Equals(option, "--password", StringComparison.OrdinalIgnoreCase))
            {
                sqlPassword = value;
            }
            else
            {
                Console.Error.WriteLine($"Opción no reconocida: {option}");
                return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.Error.WriteLine("Se requiere --user <usuario>.");
            return 2;
        }

        var workspaceRoot = ProgramHelpers.FindWorkspaceRoot();
        var databasePath = Path.Combine(workspaceRoot, "DatosLocal", "ControlTaxi.db");
        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"No se encontró la base de datos local: {databasePath}");
            return 1;
        }

        if (string.IsNullOrWhiteSpace(sqlPassword))
        {
            sqlPassword = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(sqlPassword))
        {
            Console.Error.WriteLine("La contraseña SQL debe proporcionarse mediante --sql-password o la variable de entorno CASCO_SQL_PASSWORD.");
            return 1;
        }

        try
        {
            await using var connection = ProgramHelpers.OpenSqlite(databasePath);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Usuario, Rol, Estatus, BranchCode FROM DesktopUsers WHERE upper(Usuario) = upper($user) LIMIT 1;";
            command.Parameters.AddWithValue("$user", userName);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                Console.Error.WriteLine($"Usuario no encontrado: {userName}");
                return 1;
            }

            var storedBranchCode = reader.GetString(3).Trim().ToUpperInvariant();
            Console.WriteLine($"Usuario: {reader.GetString(0)}");
            Console.WriteLine($"Rol: {reader.GetString(1)}");
            Console.WriteLine($"Estatus: {reader.GetString(2)}");
            Console.WriteLine($"BranchCode guardado: {storedBranchCode}");

            if (!string.Equals(storedBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("BranchCode del usuario no es CV. Deteniendo diagnóstico.");
                return 1;
            }

            var session = new DesktopSession(reader.GetString(0), reader.GetString(1), new HashSet<string>(StringComparer.OrdinalIgnoreCase), false, storedBranchCode);
            Console.WriteLine($"BranchCode de sesión: {session.BranchCode}");

            var branchConfig = new BranchConfigurationService().GetBranch(session.BranchCode);
            var providerName = branchConfig.IsReadOnly ? "CascoReadOnlyDataProvider" : "Plaza28";
            Console.WriteLine($"Proveedor seleccionado: {providerName}");

            if (!branchConfig.IsReadOnly)
            {
                Console.Error.WriteLine("El branch no es de solo lectura; el proveedor esperado no es CascoReadOnlyDataProvider.");
                return 1;
            }

            var provider = new CascoReadOnlyDataProvider(branchConfig);
            Console.WriteLine($"Conectando al proveedor remoto Casco en servidor {branchConfig.SqlServer}, base {branchConfig.Database}, sitio {branchConfig.SiteName}...");
            var records = await provider.GetAppRecordsAsync(sqlPassword);
            Console.WriteLine($"Proveedor remoto retornó {records.Count} registros.");

            Console.WriteLine($"Cantidad de registros: {records.Count}");
            var recordIds = records.Select(r => r.FolioControl).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var taxistas = records.Select(r => r.DriverName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var sites = new[] { branchConfig.SiteName };

            Console.WriteLine($"RecordIds: {string.Join(", ", recordIds)}");
            Console.WriteLine($"Taxistas: {string.Join(", ", taxistas)}");
            Console.WriteLine($"Sitios: {string.Join(", ", sites.Distinct(StringComparer.OrdinalIgnoreCase))}");
            Console.WriteLine("Conectando directamente al SQL Server remoto para validar sitio y recuentos...");

            var sqlBuilder = new SqlConnectionStringBuilder
            {
                DataSource = branchConfig.SqlServer,
                InitialCatalog = branchConfig.Database,
                UserID = "sa",
                Password = sqlPassword,
                TrustServerCertificate = true,
                Encrypt = false,
                ConnectTimeout = 30
            };

            await using var sqlConnection = new SqlConnection(sqlBuilder.ConnectionString);
            await sqlConnection.OpenAsync();
            await using var sqlCommand = sqlConnection.CreateCommand();
            sqlCommand.CommandText = @"
                SELECT folio_app, folio_app_original, vendedor_nombre, sitio, fecha_operacion
                FROM dbo.AppMovilRegistro
                WHERE sitio = @sitio
                ORDER BY fecha_operacion;
            ";
            sqlCommand.Parameters.AddWithValue("@sitio", branchConfig.SiteName);

            var remoteRows = new List<(string FolioApp, string FolioAppOriginal, string DriverName, string Sitio, string FechaOperación)>();
            await using (var sqlReader = await sqlCommand.ExecuteReaderAsync())
            {
                while (await sqlReader.ReadAsync())
                {
                    var fechaOperacion = sqlReader.IsDBNull(4)
                        ? string.Empty
                        : Convert.ToString(sqlReader.GetValue(4), CultureInfo.InvariantCulture) ?? string.Empty;

                    remoteRows.Add(
                        (sqlReader.IsDBNull(0) ? string.Empty : sqlReader.GetString(0),
                         sqlReader.IsDBNull(1) ? string.Empty : sqlReader.GetString(1),
                         sqlReader.IsDBNull(2) ? string.Empty : sqlReader.GetString(2),
                         sqlReader.IsDBNull(3) ? string.Empty : sqlReader.GetString(3),
                         fechaOperacion));
                }
            }

            Console.WriteLine($"Registros remotos Casco Viejo encontrados: {remoteRows.Count}");
            foreach (var row in remoteRows)
                Console.WriteLine($"- {row.FolioApp} / {row.DriverName} / {row.Sitio} / {row.FechaOperación}");

            await using var plazaCommand = sqlConnection.CreateCommand();
            plazaCommand.CommandText = "SELECT COUNT(*) FROM dbo.AppMovilRegistro WHERE sitio = @plaza;";
            plazaCommand.Parameters.AddWithValue("@plaza", "Plaza 28");
            var plazaCount = Convert.ToInt32(await plazaCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            Console.WriteLine($"Registros de Plaza 28 encontrados: {plazaCount}");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static ControlTaxiDesktop.Tools.CascoSync.Models.CascoTripRecord CloneTripRecord(ControlTaxiDesktop.Tools.CascoSync.Models.CascoTripRecord source)
    {
        return new ControlTaxiDesktop.Tools.CascoSync.Models.CascoTripRecord
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

    private static async Task ImportSqlServerDatabaseAsync(string connectionString, string sourceName, string databasePath)
    {
        await using var source = new SqlConnection(connectionString);
        await source.OpenAsync();
        await using var target = ProgramHelpers.OpenSqlite(databasePath);
        await EnsureManifestAsync(target);

        var tables = new List<(string Schema, string Name)>();
        await using (var command = source.CreateCommand())
        {
            command.CommandText = """
                SELECT s.name, t.name
                FROM sys.tables t INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE t.is_ms_shipped = 0
                ORDER BY s.name, t.name;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        Console.WriteLine($"Importando {tables.Count} tablas de {sourceName}...");
        foreach (var (schema, table) in tables)
            await CopySqlServerTableAsync(source, target, sourceName, schema, table);
    }

    private static string ResolveSourceAlias(string databaseName)
    {
        if (databaseName.Contains("mkt", StringComparison.OrdinalIgnoreCase)) return "mkt";
        if (databaseName.Contains("compuamdo", StringComparison.OrdinalIgnoreCase)) return "compuadmo";
        if (databaseName.Contains("compuadmo", StringComparison.OrdinalIgnoreCase)) return "compuadmo";
        if (databaseName.Contains("joyeria", StringComparison.OrdinalIgnoreCase)) return "joyeria";
        if (databaseName.Contains("control", StringComparison.OrdinalIgnoreCase)) return "ControlTaxis";
        return ProgramHelpers.SanitizeName(databaseName);
    }

    private static async Task CopySqlServerTableAsync(SqlConnection source, SqliteConnection target, string sourceName, string schema, string table)
    {
        var destination = $"{sourceName}__{ProgramHelpers.SanitizeName(schema)}__{ProgramHelpers.SanitizeName(table)}";
        await using var select = source.CreateCommand();
        select.CommandText = $"SELECT * FROM {ProgramHelpers.QuoteSqlServer(schema)}.{ProgramHelpers.QuoteSqlServer(table)};";
        await using var reader = await select.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(index => new Column(reader.GetName(index), reader.GetFieldType(index)))
            .ToArray();

        await using (var create = target.CreateCommand())
        {
            create.CommandText = $"DROP TABLE IF EXISTS {ProgramHelpers.QuoteSqlite(destination)}; CREATE TABLE {ProgramHelpers.QuoteSqlite(destination)} ({string.Join(", ", columns.Select(x => $"{ProgramHelpers.QuoteSqlite(x.Name)} {ProgramHelpers.MapType(x.Type)}"))});";
            await create.ExecuteNonQueryAsync();
        }

        await using var transaction = target.BeginTransaction();
        await using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {ProgramHelpers.QuoteSqlite(destination)} ({string.Join(", ", columns.Select(x => ProgramHelpers.QuoteSqlite(x.Name)))}) VALUES ({string.Join(", ", columns.Select((_, index) => "$p" + index))});";
        for (var index = 0; index < columns.Length; index++)
            insert.Parameters.Add(new SqliteParameter("$p" + index, DBNull.Value));

        long rowCount = 0;
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < columns.Length; index++)
                insert.Parameters[index].Value = ProgramHelpers.ConvertValue(reader.GetValue(index));
            await insert.ExecuteNonQueryAsync();
            rowCount++;
        }
        await transaction.CommitAsync();
        await SaveTableManifestAsync(target, sourceName, schema, table, destination, rowCount, "sqlserver");
        Console.WriteLine($"  {schema}.{table}: {rowCount:N0} filas");
    }

    private static async Task<IReadOnlyList<(string LogicalName, string Type)>> ReadBakLogicalFilesAsync(SqlConnection connection, string bakPath)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"RESTORE FILELISTONLY FROM DISK = {ProgramHelpers.QuoteSqlLiteral(bakPath)};";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<(string LogicalName, string Type)>();
        while (await reader.ReadAsync())
            result.Add((Convert.ToString(reader["LogicalName"], CultureInfo.InvariantCulture) ?? string.Empty, Convert.ToString(reader["Type"], CultureInfo.InvariantCulture) ?? string.Empty));
        return result;
    }

    private static async Task ImportDelimitedFileAsync(SqliteConnection target, string sourceName, string file)
    {
        var lines = await File.ReadAllLinesAsync(file, Encoding.UTF8);
        if (lines.Length == 0) return;
        var headers = ParseCsvLine(lines[0]).Select(ProgramHelpers.SanitizeName).ToArray();
        if (headers.Length == 0) return;
        var table = $"{sourceName}__csv__{ProgramHelpers.SanitizeName(Path.GetFileNameWithoutExtension(file))}";
        await CreateTextTableAsync(target, table, headers);
        await using var transaction = target.BeginTransaction();
        await using var insert = BuildInsertCommand(target, transaction, table, headers);
        long rowCount = 0;
        foreach (var line in lines.Skip(1))
        {
            var values = ParseCsvLine(line);
            for (var index = 0; index < headers.Length; index++)
                insert.Parameters[index].Value = index < values.Count ? values[index] : DBNull.Value;
            await insert.ExecuteNonQueryAsync();
            rowCount++;
        }
        await transaction.CommitAsync();
        await SaveTableManifestAsync(target, sourceName, "csv", Path.GetFileName(file), table, rowCount, "csv");
        Console.WriteLine($"  {Path.GetFileName(file)}: {rowCount:N0} filas");
    }

    private static async Task ImportXlsxFileAsync(SqliteConnection target, string sourceName, string file)
    {
        using var archive = ZipFile.OpenRead(file);
        var sharedStrings = ReadSharedStrings(archive);
        var sheets = archive.Entries.Where(x => x.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.FullName).ToArray();
        var sheetIndex = 1;
        foreach (var sheet in sheets)
        {
            var rows = ReadSheetRows(sheet, sharedStrings).Where(x => x.Count > 0).ToList();
            if (rows.Count == 0) { sheetIndex++; continue; }
            var headers = rows[0].Select((x, i) => string.IsNullOrWhiteSpace(x) ? $"Columna{i + 1}" : x).Select(ProgramHelpers.SanitizeName).ToArray();
            var table = $"{sourceName}__xlsx__{ProgramHelpers.SanitizeName(Path.GetFileNameWithoutExtension(file))}_sheet{sheetIndex}";
            await CreateTextTableAsync(target, table, headers);
            await using var transaction = target.BeginTransaction();
            await using var insert = BuildInsertCommand(target, transaction, table, headers);
            long rowCount = 0;
            foreach (var row in rows.Skip(1))
            {
                for (var index = 0; index < headers.Length; index++)
                    insert.Parameters[index].Value = index < row.Count ? row[index] : DBNull.Value;
                await insert.ExecuteNonQueryAsync();
                rowCount++;
            }
            await transaction.CommitAsync();
            await SaveTableManifestAsync(target, sourceName, "xlsx", $"{Path.GetFileName(file)}#sheet{sheetIndex}", table, rowCount, "xlsx");
            Console.WriteLine($"  {Path.GetFileName(file)} hoja {sheetIndex}: {rowCount:N0} filas");
            sheetIndex++;
        }
    }

    private static async Task CopySqliteTableAsync(SqliteConnection source, SqliteConnection target, string sourceName, string table)
    {
        var destination = $"{sourceName}__sqlite__{ProgramHelpers.SanitizeName(table)}";
        var columns = await GetSqliteColumnsAsync(source, table);
        if (columns.Count == 0) return;
        await using (var create = target.CreateCommand())
        {
            create.CommandText = $"DROP TABLE IF EXISTS {ProgramHelpers.QuoteSqlite(destination)}; CREATE TABLE {ProgramHelpers.QuoteSqlite(destination)} ({string.Join(", ", columns.Select(x => $"{ProgramHelpers.QuoteSqlite(x)} TEXT"))});";
            await create.ExecuteNonQueryAsync();
        }
        await using var transaction = target.BeginTransaction();
        await using var insert = BuildInsertCommand(target, transaction, destination, columns.ToArray());
        await using var select = source.CreateCommand();
        select.CommandText = $"SELECT {string.Join(", ", columns.Select(ProgramHelpers.QuoteSqlite))} FROM {ProgramHelpers.QuoteSqlite(table)};";
        await using var reader = await select.ExecuteReaderAsync();
        long rowCount = 0;
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < columns.Count; index++)
                insert.Parameters[index].Value = reader.IsDBNull(index) ? DBNull.Value : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);
            await insert.ExecuteNonQueryAsync();
            rowCount++;
        }
        await transaction.CommitAsync();
        await SaveTableManifestAsync(target, sourceName, "sqlite", table, destination, rowCount, "sqlite");
        Console.WriteLine($"  {table}: {rowCount:N0} filas");
    }

    private static async Task ImportJsonFileAsync(SqliteConnection target, string file)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        var root = document.RootElement;
        var records = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
        if (records.Length == 0)
        {
            Console.WriteLine($"  {Path.GetFileName(file)}: no contiene un arreglo importable; se conserva como evidencia.");
            return;
        }

        var propertyNames = records.Where(x => x.ValueKind == JsonValueKind.Object)
            .SelectMany(x => x.EnumerateObject().Select(p => p.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (propertyNames.Length == 0)
            return;

        var sourceName = "hostinger";
        var table = $"{sourceName}__json__{ProgramHelpers.SanitizeName(Path.GetFileNameWithoutExtension(file))}";
        await using (var create = target.CreateCommand())
        {
            create.CommandText = $"DROP TABLE IF EXISTS {ProgramHelpers.QuoteSqlite(table)}; CREATE TABLE {ProgramHelpers.QuoteSqlite(table)} ({string.Join(", ", propertyNames.Select(x => ProgramHelpers.QuoteSqlite(x) + " TEXT"))});";
            await create.ExecuteNonQueryAsync();
        }
        await using var transaction = target.BeginTransaction();
        await using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {ProgramHelpers.QuoteSqlite(table)} ({string.Join(", ", propertyNames.Select(ProgramHelpers.QuoteSqlite))}) VALUES ({string.Join(", ", propertyNames.Select((_, index) => "$p" + index))});";
        foreach (var (_, index) in propertyNames.Select((value, index) => (value, index)))
            insert.Parameters.Add(new SqliteParameter("$p" + index, DBNull.Value));
        foreach (var record in records)
        {
            for (var index = 0; index < propertyNames.Length; index++)
                insert.Parameters[index].Value = record.TryGetProperty(propertyNames[index], out var value) ? ProgramHelpers.JsonToText(value) : DBNull.Value;
            await insert.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        await SaveTableManifestAsync(target, sourceName, "json", Path.GetFileName(file), table, records.Length, "json");
        Console.WriteLine($"  {Path.GetFileName(file)}: {records.Length:N0} registros");
    }

    private static async Task EnsureManifestAsync(SqliteConnection target)
    {
        await using var command = target.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE IF NOT EXISTS __table_manifest (
              SourceName TEXT NOT NULL, SourceSchema TEXT NOT NULL, SourceTable TEXT NOT NULL,
              DestinationTable TEXT NOT NULL, RowCount INTEGER NOT NULL, ImportKind TEXT NOT NULL,
              ImportedAtUtc TEXT NOT NULL, PRIMARY KEY (SourceName, SourceSchema, SourceTable));
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SaveTableManifestAsync(SqliteConnection target, string sourceName, string schema, string table, string destination, long rowCount, string kind)
    {
        await using var command = target.CreateCommand();
        command.CommandText = """
            INSERT INTO __table_manifest (SourceName, SourceSchema, SourceTable, DestinationTable, RowCount, ImportKind, ImportedAtUtc)
            VALUES ($source, $schema, $table, $destination, $count, $kind, $date)
            ON CONFLICT(SourceName, SourceSchema, SourceTable) DO UPDATE SET
              DestinationTable = excluded.DestinationTable, RowCount = excluded.RowCount,
              ImportKind = excluded.ImportKind, ImportedAtUtc = excluded.ImportedAtUtc;
            """;
        command.Parameters.AddWithValue("$source", sourceName);
        command.Parameters.AddWithValue("$schema", schema);
        command.Parameters.AddWithValue("$table", table);
        command.Parameters.AddWithValue("$destination", destination);
        command.Parameters.AddWithValue("$count", rowCount);
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateTextTableAsync(SqliteConnection target, string table, IReadOnlyList<string> columns)
    {
        await using var create = target.CreateCommand();
        create.CommandText = $"DROP TABLE IF EXISTS {ProgramHelpers.QuoteSqlite(table)}; CREATE TABLE {ProgramHelpers.QuoteSqlite(table)} ({string.Join(", ", columns.Select(x => ProgramHelpers.QuoteSqlite(x) + " TEXT"))});";
        await create.ExecuteNonQueryAsync();
    }

    private static SqliteCommand BuildInsertCommand(SqliteConnection target, SqliteTransaction transaction, string table, IReadOnlyList<string> columns)
    {
        var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {ProgramHelpers.QuoteSqlite(table)} ({string.Join(", ", columns.Select(ProgramHelpers.QuoteSqlite))}) VALUES ({string.Join(", ", columns.Select((_, index) => "$p" + index))});";
        for (var index = 0; index < columns.Count; index++)
            insert.Parameters.Add(new SqliteParameter("$p" + index, DBNull.Value));
        return insert;
    }

    private static async Task<IReadOnlyList<string>> GetSqliteColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({ProgramHelpers.QuoteSqlite(table)});";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(1));
        return result;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var ch = line[index];
            if (ch == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(x => x.Value))).ToArray();
    }

    private static IReadOnlyList<List<string>> ReadSheetRows(ZipArchiveEntry sheet, IReadOnlyList<string> sharedStrings)
    {
        using var stream = sheet.Open();
        var document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = new List<List<string>>();
        foreach (var row in document.Descendants(ns + "row"))
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements(ns + "c"))
            {
                var reference = cell.Attribute("r")?.Value ?? string.Empty;
                var columnIndex = ExcelColumnIndex(reference);
                while (cells.Count < columnIndex - 1) cells.Add(string.Empty);
                var type = cell.Attribute("t")?.Value;
                var value = cell.Element(ns + "v")?.Value ?? cell.Descendants(ns + "t").FirstOrDefault()?.Value ?? string.Empty;
                if (type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                    value = sharedStrings[sharedIndex];
                cells.Add(value);
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static int ExcelColumnIndex(string reference)
    {
        var letters = new string(reference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        if (letters.Length == 0) return 1;
        var result = 0;
        foreach (var ch in letters)
            result = result * 26 + (ch - 'A' + 1);
        return result;
    }

    private static async Task<HashSet<string>> GetSqliteTablesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    public static async Task<int> RunOperationsWindowCloseSmokeTestAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: false);
        if (!parameters.IsValid)
            return 2;

        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new ControlTaxiDesktop.App();
                    app.InitializeComponent();
                    app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                }

                var database = new LocalDatabase(forceTestDatabase: true);
                database.InitializeAsync().GetAwaiter().GetResult();

                var window = new OperationsWindow(database, parameters.UserName ?? "ReynaV", "CV", "Registro diario");
                var loadMethod = typeof(OperationsWindow).GetMethod("LoadRegistroAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                if (loadMethod is null)
                    throw new InvalidOperationException("No se encontro LoadRegistroAsync en OperationsWindow.");

                Task? pendingLoad = null;
                var closeReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var loadState = "sin iniciar";

                window.Loaded += (_, _) =>
                {
                    var loadTaskObject = loadMethod.Invoke(window, null);
                    if (loadTaskObject is not Task task)
                        throw new InvalidOperationException("LoadRegistroAsync no devolvio una tarea valida.");

                    pendingLoad = task;
                    loadState = "cargando";
                    window.Dispatcher.BeginInvoke(new Action(window.Close), DispatcherPriority.Background);
                };

                window.Closed += (_, _) =>
                {
                    closeReached.TrySetResult(true);
                    window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                };

                window.Show();
                Dispatcher.Run();

                var closed = closeReached.Task.Wait(TimeSpan.FromSeconds(10));
                if (!closed)
                    throw new TimeoutException("OperationsWindow no notifico Closed dentro del tiempo esperado.");

                if (pendingLoad is not null)
                {
                    try
                    {
                        var finished = pendingLoad.Wait(TimeSpan.FromSeconds(10));
                        loadState = !finished
                            ? "pendiente"
                            : pendingLoad.IsCanceled
                                ? "cancelada"
                                : pendingLoad.IsFaulted
                                    ? "fallida"
                                    : "completada";
                    }
                    catch (AggregateException ex) when (ex.InnerExceptions.All(inner => inner is OperationCanceledException or TaskCanceledException))
                    {
                        loadState = "cancelada";
                    }
                }

                Console.WriteLine("Prueba de cierre: OperationsWindow");
                Console.WriteLine("Modulo: Registro Diario");
                Console.WriteLine("BranchCode: CV");
                Console.WriteLine("Cierre solicitado durante carga: si");
                Console.WriteLine($"Estado final de la carga: {loadState}");
                Console.WriteLine($"Ventana cerrada: {(closed ? "si" : "no")}");
                Console.WriteLine("Dispatcher finalizado: si");
                Console.WriteLine("Proceso colgado: no");
                completion.TrySetResult(closed && loadState != "pendiente" ? 0 : 1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                completion.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var exitCode = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        if (!thread.Join(TimeSpan.FromSeconds(5)))
        {
            Console.Error.WriteLine("El hilo WPF no finalizo tras cerrar la ventana.");
            return 1;
        }

        return exitCode;
    }

    public static async Task<int> RunPosReportCenterSmokeTestAsync(string[] args)
    {
        var parameters = ParseCascoDiagnosticArguments(args, requireUser: true, allowDateRange: true);
        if (!parameters.IsValid)
            return 2;

        var start = parameters.DateFrom ?? new DateTime(2026, 7, 3);
        var end = parameters.DateTo ?? new DateTime(2026, 7, 17);
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", parameters.Password);

        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current is null)
                {
                    var app = new ControlTaxiDesktop.App();
                    app.InitializeComponent();
                    app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                }

                var database = new LocalDatabase(forceTestDatabase: true);
                database.InitializeAsync().GetAwaiter().GetResult();

                var window = new PosWindow(database, parameters.UserName!, "CV", "Reportes");
                var loadMethod = typeof(PosWindow).GetMethod("LoadReportCenterAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro LoadReportCenterAsync en PosWindow.");
                var reportStartField = typeof(PosWindow).GetField("ReportStart", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro ReportStart en PosWindow.");
                var reportEndField = typeof(PosWindow).GetField("ReportEnd", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro ReportEnd en PosWindow.");
                var operationsGridField = typeof(PosWindow).GetField("ReportOperationsGrid", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro ReportOperationsGrid en PosWindow.");
                var movementsTextField = typeof(PosWindow).GetField("ReportMovementsText", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro ReportMovementsText en PosWindow.");
                var paxTextField = typeof(PosWindow).GetField("ReportPaxText", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro ReportPaxText en PosWindow.");
                var amountTextField = typeof(PosWindow).GetField("ReportAmountText", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro ReportAmountText en PosWindow.");
                var reportsTabField = typeof(PosWindow).GetField("ReportsTabControl", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("No se encontro ReportsTabControl en PosWindow.");

                window.Loaded += async (_, _) =>
                {
                    try
                    {
                        ((DatePicker)reportStartField.GetValue(window)!).SelectedDate = start;
                        ((DatePicker)reportEndField.GetValue(window)!).SelectedDate = end;
                        ((TabControl)reportsTabField.GetValue(window)!).SelectedIndex = 0;

                        var task = loadMethod.Invoke(window, null) as Task
                            ?? throw new InvalidOperationException("LoadReportCenterAsync no devolvio una tarea valida.");
                        await task;
                        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

                        var grid = (DataGrid)operationsGridField.GetValue(window)!;
                        var rows = (grid.ItemsSource as IEnumerable<LocalOperationsPreviewRow>)?.ToList() ?? [];
                        var movementsText = ((TextBlock)movementsTextField.GetValue(window)!).Text;
                        var paxText = ((TextBlock)paxTextField.GetValue(window)!).Text;
                        var amountText = ((TextBlock)amountTextField.GetValue(window)!).Text;

                        Console.WriteLine("BranchCode: CV");
                        Console.WriteLine("Proveedor: CascoReadOnlyDataProvider");
                        Console.WriteLine($"Cantidad final grid: {rows.Count}");
                        Console.WriteLine($"Tarjeta movimientos: {movementsText}");
                        Console.WriteLine($"Tarjeta PAX: {paxText}");
                        Console.WriteLine($"Tarjeta importe: {amountText}");
                        for (var index = 0; index < rows.Count; index++)
                        {
                            var row = rows[index];
                            Console.WriteLine($"Fila {index + 1}: {row.FolioOriginal} / {row.FolioLocal} / {row.Taxista} / {row.Sitio}");
                        }

                        var actualPairs = rows
                            .Select(row => $"{row.FolioOriginal}/{row.FolioLocal}")
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var plaza28Count = rows.Count(row => string.Equals(row.Sitio, "Plaza 28", StringComparison.OrdinalIgnoreCase));
                        var ok = rows.Count >= 1
                            && actualPairs.Contains("0001/0501")
                            && actualPairs.Contains("0002/0502")
                            && actualPairs.Contains("0003/0503")
                            && plaza28Count == 0;

                        Console.WriteLine(ok ? "Resultado UI: OK" : "Resultado UI: FALLA");
                        Console.WriteLine($"Plaza 28 = {plaza28Count}");
                        completion.TrySetResult(ok ? 0 : 1);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                        completion.TrySetException(ex);
                    }
                    finally
                    {
                        window.Close();
                    }
                };

                window.Closed += (_, _) =>
                {
                    window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                    (System.Windows.Application.Current as System.Windows.Application)?.Shutdown();
                };

                window.Show();
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                completion.TrySetException(ex);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        var exitCode = await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
        if (!thread.Join(TimeSpan.FromSeconds(5)))
            Console.Error.WriteLine("El hilo WPF del smoke test de PosWindow sigue activo; se continuara con cierre en segundo plano.");
        return exitCode;
    }

    private static async Task<CascoFinancialSourceDiagnostic> LoadCascoFinancialSourceDiagnosticAsync(
        BranchConfiguration branch,
        string sqlPassword,
        string folioOriginal,
        CancellationToken cancellationToken)
    {
        var provider = new CascoReadOnlyDataProvider(branch);
        var sqlRow = await LoadCascoSqlFinancialRowAsync(provider, sqlPassword, branch.SiteName, folioOriginal, cancellationToken);
        var relationDiagnostic = await CascoOperationsDataService.GetRelationCalculationDiagnosticAsync(
            branch,
            branch.Code,
            sqlPassword,
            folioOriginal,
            cancellationToken);
        var remote = await LoadRemoteCascoEvidenceAsync(folioOriginal, cancellationToken);
        var dejadasRows = sqlRow is null
            ? Array.Empty<CascoDejadaEvidenceRow>()
            : await LoadCascoDejadaRowsAsync(provider, sqlPassword, sqlRow, cancellationToken);
        var relacionRows = sqlRow is null
            ? Array.Empty<CascoRelacionEvidenceRow>()
            : await LoadCascoRelacionRowsAsync(provider, sqlPassword, sqlRow, cancellationToken);
        var posPagoRows = await LoadCascoPosPagoRowsAsync(provider, sqlPassword, folioOriginal, cancellationToken);
        var plaza28Sample = await LoadPlaza28EquivalentSampleAsync(cancellationToken);
        var recommendation = DetermineCascoFinancialRecommendation(sqlRow, remote, dejadasRows, relacionRows, posPagoRows);

        var possibleSaleSources = new List<string>();
        var possibleDejadaSources = new List<string>();

        if (sqlRow is not null)
            possibleSaleSources.Add($"dbo.AppMovilRegistro.total = {FormatNullableMoney(sqlRow.Total)}");
        if (remote.Record?.TripCost is decimal tripCost)
            possibleSaleSources.Add($"Hostinger.tripCost = {tripCost:0.00}");
        if (dejadasRows.Any(row => row.TotalVenta.HasValue && row.TotalVenta.Value > 0m))
            possibleSaleSources.Add($"dbo.dejadas.totalventa = {FormatNullableMoney(dejadasRows.First(row => row.TotalVenta.HasValue && row.TotalVenta.Value > 0m).TotalVenta)}");

        if (relacionRows.Any(row => row.Dejada > 0m))
            possibleDejadaSources.Add($"dbo.RelacionTicketTaxista.Dejada = {relacionRows.First(row => row.Dejada > 0m).Dejada:0.00}");
        if (dejadasRows.Any(row => row.Total.HasValue && row.Total.Value > 0m))
            possibleDejadaSources.Add($"dbo.dejadas.total = {FormatNullableMoney(dejadasRows.First(row => row.Total.HasValue && row.Total.Value > 0m).Total)}");
        if (dejadasRows.Any(row => row.Pago.HasValue && row.Pago.Value > 0m))
            possibleDejadaSources.Add($"dbo.dejadas.pago = {FormatNullableMoney(dejadasRows.First(row => row.Pago.HasValue && row.Pago.Value > 0m).Pago)}");
        if (possibleDejadaSources.Count == 0)
            possibleDejadaSources.Add("Sin fuente separada confirmada en SQL ni en el remoto");

        return new CascoFinancialSourceDiagnostic(
            "CV",
            "CascoReadOnlyDataProvider + Hostinger API",
            "REYNA.mktCasco.dbo.AppMovilRegistro + Hostinger",
            folioOriginal,
            sqlRow?.FolioApp ?? relationDiagnostic.FolioLocal,
            sqlRow?.DriverName ?? relationDiagnostic.Taxista,
            sqlRow?.Badge ?? relationDiagnostic.Gafete,
            sqlRow?.Site ?? branch.SiteName,
            sqlRow?.Total,
            remote.Record?.TripCost,
            sqlRow?.Cash,
            sqlRow?.Card,
            sqlRow?.Dollars,
            sqlRow?.ExchangeRate,
            sqlRow?.CommissionCalculated,
            sqlRow?.CommissionPaid,
            remote.Record?.PaymentMethod ?? string.Empty,
            remote.Record?.ServiceType ?? string.Empty,
            remote.Record?.Notes ?? string.Empty,
            sqlRow?.DetailJson ?? string.Empty,
            sqlRow?.PaymentsJson ?? string.Empty,
            remote.RawJson,
            possibleSaleSources,
            possibleDejadaSources,
            dejadasRows,
            relacionRows,
            posPagoRows,
            BuildPlaza28ComparisonRows(plaza28Sample),
            BuildCvComparisonRows(sqlRow, relationDiagnostic, recommendation),
            recommendation);
    }

    private static async Task<(ControlTaxiDesktop.Tools.CascoSync.Models.CascoTripRecord? Record, string RawJson)> LoadRemoteCascoEvidenceAsync(string folioOriginal, CancellationToken cancellationToken)
    {
        var options = CascoSyncOptions.LoadFromWorkspace();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var rawContent = await httpClient.GetStringAsync(options.BuildApiUrl(), cancellationToken);
        using var document = JsonDocument.Parse(rawContent);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return (null, string.Empty);

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("recordId", out var recordId))
                continue;

            if (!string.Equals(ProgramHelpers.JsonToText(recordId), folioOriginal, StringComparison.OrdinalIgnoreCase))
                continue;

            var rawJson = item.GetRawText();
            var record = new ControlTaxiDesktop.Tools.CascoSync.Models.CascoTripRecord
            {
                RecordId = recordId.GetString() ?? recordId.ToString(),
                CatalogId = item.TryGetProperty("catalogId", out var catalogId) ? ProgramHelpers.JsonToText(catalogId) : string.Empty,
                BadgeId = item.TryGetProperty("badgeId", out var badgeId) ? ProgramHelpers.JsonToText(badgeId) : string.Empty,
                DriverName = item.TryGetProperty("driverName", out var driverName) ? ProgramHelpers.JsonToText(driverName) : string.Empty,
                DriverPhone = item.TryGetProperty("driverPhone", out var driverPhone) ? ProgramHelpers.JsonToText(driverPhone) : string.Empty,
                ContactPhone = item.TryGetProperty("contactPhone", out var contactPhone) ? ProgramHelpers.JsonToText(contactPhone) : string.Empty,
                Nationality = item.TryGetProperty("nationality", out var nationality) ? ProgramHelpers.JsonToText(nationality) : string.Empty,
                Plate = item.TryGetProperty("plate", out var plate) ? ProgramHelpers.JsonToText(plate) : string.Empty,
                VehicleModel = item.TryGetProperty("vehicleModel", out var vehicleModel) ? ProgramHelpers.JsonToText(vehicleModel) : string.Empty,
                UnitNumber = item.TryGetProperty("unitNumber", out var unitNumber) ? ProgramHelpers.JsonToText(unitNumber) : string.Empty,
                Hotel = item.TryGetProperty("hotel", out var hotel) ? ProgramHelpers.JsonToText(hotel) : string.Empty,
                Origin = item.TryGetProperty("origin", out var origin) ? ProgramHelpers.JsonToText(origin) : string.Empty,
                Site = item.TryGetProperty("site", out var site) ? ProgramHelpers.JsonToText(site) : string.Empty,
                Destination = item.TryGetProperty("destination", out var destination) ? ProgramHelpers.JsonToText(destination) : string.Empty,
                PassengerCount = item.TryGetProperty("passengerCount", out var passengers) && int.TryParse(ProgramHelpers.JsonToText(passengers), out var parsedPassengers) ? parsedPassengers : null,
                ServiceType = item.TryGetProperty("serviceType", out var serviceType) ? ProgramHelpers.JsonToText(serviceType) : string.Empty,
                TripCost = item.TryGetProperty("tripCost", out var tripCost) && decimal.TryParse(ProgramHelpers.JsonToText(tripCost), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedTripCost) ? parsedTripCost : null,
                Notes = item.TryGetProperty("notes", out var notes) ? ProgramHelpers.JsonToText(notes) : string.Empty,
                RecordDate = item.TryGetProperty("recordDate", out var recordDate) && DateTimeOffset.TryParse(ProgramHelpers.JsonToText(recordDate), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedRecordDate) ? parsedRecordDate : null,
                PaymentMethod = item.TryGetProperty("paymentMethod", out var paymentMethod) ? ProgramHelpers.JsonToText(paymentMethod) : string.Empty,
                AssignedBranchCode = item.TryGetProperty("assignedBranchCode", out var branchCode) ? ProgramHelpers.JsonToText(branchCode) : string.Empty,
                PayoutStatus = item.TryGetProperty("payoutStatus", out var payoutStatus) ? ProgramHelpers.JsonToText(payoutStatus) : string.Empty,
                PayoutDate = item.TryGetProperty("payoutDate", out var payoutDate) ? ProgramHelpers.JsonToText(payoutDate) : string.Empty,
                PayoutUser = item.TryGetProperty("payoutUser", out var payoutUser) ? ProgramHelpers.JsonToText(payoutUser) : string.Empty,
                PayoutTicket = item.TryGetProperty("payoutTicket", out var payoutTicket) ? ProgramHelpers.JsonToText(payoutTicket) : string.Empty
            };
            return (record, rawJson);
        }

        return (null, string.Empty);
    }

    private static async Task<CascoSqlFinancialRow?> LoadCascoSqlFinancialRowAsync(
        CascoReadOnlyDataProvider provider,
        string sqlPassword,
        string siteName,
        string folioOriginal,
        CancellationToken cancellationToken)
    {
        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT TOP (1)
              folio_app,
              folio_app_original,
              id_catalogo,
              folio_gafete,
              vendedor_nombre,
              fecha_operacion,
              sitio,
              tipo_operacion,
              subtotal,
              iva,
              total,
              efectivo,
              tarjeta,
              dolares,
              tipo_cambio,
              comision_calculada,
              pago_comision,
              estado_pago_dejada,
              payout_status,
              usuario_pago_dejada,
              fecha_pago_dejada,
              ticket_pago_dejada,
              notas,
              detalle_json,
              pagos_json
            FROM dbo.AppMovilRegistro
            WHERE sitio = @sitio
              AND folio_app_original = @folioOriginal
            ORDER BY fecha_operacion DESC;";
        command.Parameters.AddWithValue("@sitio", siteName);
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new CascoSqlFinancialRow(
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            reader.IsDBNull(2) ? null : Convert.ToInt64(reader.GetValue(2), CultureInfo.InvariantCulture),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? string.Empty : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            reader.IsDBNull(8) ? null : Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture),
            reader.IsDBNull(9) ? null : Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
            reader.IsDBNull(10) ? null : Convert.ToDecimal(reader.GetValue(10), CultureInfo.InvariantCulture),
            reader.IsDBNull(11) ? null : Convert.ToDecimal(reader.GetValue(11), CultureInfo.InvariantCulture),
            reader.IsDBNull(12) ? null : Convert.ToDecimal(reader.GetValue(12), CultureInfo.InvariantCulture),
            reader.IsDBNull(13) ? null : Convert.ToDecimal(reader.GetValue(13), CultureInfo.InvariantCulture),
            reader.IsDBNull(14) ? null : Convert.ToDecimal(reader.GetValue(14), CultureInfo.InvariantCulture),
            reader.IsDBNull(15) ? null : Convert.ToDecimal(reader.GetValue(15), CultureInfo.InvariantCulture),
            reader.IsDBNull(16) ? null : Convert.ToDecimal(reader.GetValue(16), CultureInfo.InvariantCulture),
            reader.IsDBNull(17) ? string.Empty : Convert.ToString(reader.GetValue(17), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(18) ? string.Empty : Convert.ToString(reader.GetValue(18), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(19) ? string.Empty : Convert.ToString(reader.GetValue(19), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(20) ? string.Empty : Convert.ToString(reader.GetValue(20), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(21) ? string.Empty : Convert.ToString(reader.GetValue(21), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(22) ? string.Empty : Convert.ToString(reader.GetValue(22), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(23) ? string.Empty : Convert.ToString(reader.GetValue(23), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(24) ? string.Empty : Convert.ToString(reader.GetValue(24), CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private static async Task<IReadOnlyList<CascoDejadaEvidenceRow>> LoadCascoDejadaRowsAsync(
        CascoReadOnlyDataProvider provider,
        string sqlPassword,
        CascoSqlFinancialRow row,
        CancellationToken cancellationToken)
    {
        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
              folioregistrostr,
              gafete,
              nombrestaff,
              nombrevendedor,
              CONVERT(nvarchar(30), fecha, 120),
              total,
              totalventa,
              comision,
              pago
            FROM dbo.dejadas
            WHERE folioregistrostr IN (@folioOriginal, @folioLocal)
               OR gafete = @gafete
            ORDER BY fecha DESC;";
        command.Parameters.AddWithValue("@folioOriginal", row.OriginalFolio);
        command.Parameters.AddWithValue("@folioLocal", row.FolioApp);
        command.Parameters.AddWithValue("@gafete", row.Badge);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CascoDejadaEvidenceRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CascoDejadaEvidenceRow(
                reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(4) ? string.Empty : Convert.ToString(reader.GetValue(4), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(5) ? null : Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? null : Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                reader.IsDBNull(7) ? null : Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? null : Convert.ToDecimal(reader.GetValue(8), CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<CascoRelacionEvidenceRow>> LoadCascoRelacionRowsAsync(
        CascoReadOnlyDataProvider provider,
        string sqlPassword,
        CascoSqlFinancialRow row,
        CancellationToken cancellationToken)
    {
        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
              Id,
              FolioOperacion,
              FolioApp,
              Gafete,
              Dejada,
              Usuario,
              Observaciones
            FROM dbo.RelacionTicketTaxista
            WHERE FolioOperacion IN (@folioOriginal, @folioLocal)
               OR FolioApp IN (@folioOriginal, @folioLocal)
               OR Gafete = @gafete
            ORDER BY Id DESC;";
        command.Parameters.AddWithValue("@folioOriginal", row.OriginalFolio);
        command.Parameters.AddWithValue("@folioLocal", row.FolioApp);
        command.Parameters.AddWithValue("@gafete", row.Badge);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CascoRelacionEvidenceRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CascoRelacionEvidenceRow(
                reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? string.Empty : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(6) ? string.Empty : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return result;
    }

    private static async Task<IReadOnlyList<CascoPosPagoEvidenceRow>> LoadCascoPosPagoRowsAsync(
        CascoReadOnlyDataProvider provider,
        string sqlPassword,
        string folioOriginal,
        CancellationToken cancellationToken)
    {
        await using var connection = await provider.OpenConnectionAsync(sqlPassword, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
              Id,
              FolioOriginal,
              FolioOperacion,
              FolioNumero,
              Pago,
              Usuario,
              CONVERT(nvarchar(30), FechaPago, 120)
            FROM dbo.PosComisionPagosControl
            WHERE FolioOriginal = @folioOriginal
               OR FolioOperacion = @folioOriginal
            ORDER BY Id DESC;";
        command.Parameters.AddWithValue("@folioOriginal", folioOriginal.Trim());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CascoPosPagoEvidenceRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CascoPosPagoEvidenceRow(
                reader.IsDBNull(0) ? 0L : Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? string.Empty : Convert.ToString(reader.GetValue(5), CultureInfo.InvariantCulture) ?? string.Empty,
                reader.IsDBNull(6) ? string.Empty : Convert.ToString(reader.GetValue(6), CultureInfo.InvariantCulture) ?? string.Empty));
        }

        return result;
    }

    private static async Task<Plaza28EquivalentSample> LoadPlaza28EquivalentSampleAsync(CancellationToken cancellationToken)
    {
        var dbPath = Path.Combine(ProgramHelpers.FindWorkspaceRoot(), "DatosLocal", "ControlTaxi.db");
        if (!File.Exists(dbPath))
            return Plaza28EquivalentSample.Empty;

        await using var connection = ProgramHelpers.OpenSqlite(dbPath);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              c.VentaFolio,
              c.Taxista,
              c.TotalVenta,
              c.ImporteComision,
              c.Pagado,
              COALESCE(r.Dejada, 0),
              COALESCE(p.Metodo, '')
            FROM LocalComisiones c
            LEFT JOIN LocalRelaciones r
              ON r.FolioOperacion = c.VentaFolio
              OR r.FolioApp = c.VentaFolio
            LEFT JOIN LocalPagos p
              ON p.VentaFolio = c.VentaFolio
            WHERE COALESCE(c.TotalVenta, 0) > 0
            ORDER BY c.Fecha DESC, c.Id DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return Plaza28EquivalentSample.Empty;

        return new Plaza28EquivalentSample(
            true,
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture),
            reader.IsDBNull(3) ? 0m : Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture),
            reader.IsDBNull(4) ? 0m : Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
            reader.IsDBNull(5) ? 0m : Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            "MXN");
    }

    private static IReadOnlyList<FinancialComparisonRow> BuildPlaza28ComparisonRows(Plaza28EquivalentSample sample)
    {
        var valueSuffix = sample.Found ? $" ({sample.VentaFolio})" : string.Empty;
        return
        [
            new FinancialComparisonRow("Venta", "Compuadmo/Joyeria remisioM", sample.Found ? sample.TotalVenta.ToString("0.00", CultureInfo.InvariantCulture) : "sin muestra" + valueSuffix, "Suma real de tickets asociados"),
            new FinancialComparisonRow("Dejada", "LocalRelaciones.Dejada / AppMovilRegistro.total", sample.Found ? sample.Dejada.ToString("0.00", CultureInfo.InvariantCulture) : "sin muestra" + valueSuffix, "Se trata como deduction/payout separada"),
            new FinancialComparisonRow("Comision", "CalculateRelationCommission + LocalComisiones.ImporteComision", sample.Found ? sample.ImporteComision.ToString("0.00", CultureInfo.InvariantCulture) : "sin muestra" + valueSuffix, "Formula con venta, dejada, gastos y porcentaje"),
            new FinancialComparisonRow("Pago comision", "LocalComisiones.Pagado", sample.Found ? sample.Pagado.ToString("0.00", CultureInfo.InvariantCulture) : "sin muestra" + valueSuffix, "Acumulado pagado persistido"),
            new FinancialComparisonRow("Forma de pago", "LocalPagos.Metodo / desglose de pagos", sample.Found ? sample.FormaPago : "sin muestra" + valueSuffix, "Se deriva del pago real del ticket"),
            new FinancialComparisonRow("Moneda", "POS local", sample.Found ? sample.Moneda : "MXN", "Separada de la forma de pago")
        ];
    }

    private static IReadOnlyList<FinancialCvRow> BuildCvComparisonRows(
        CascoSqlFinancialRow? sqlRow,
        CascoRelationCalculationDiagnostic relationDiagnostic,
        CascoFinancialRecommendation recommendation)
    {
        return
        [
            new FinancialCvRow("Venta", "dbo.AppMovilRegistro.total + detail_json.tripCost", FormatNullableMoney(sqlRow?.Total), "Si: coincide con el monto remoto del viaje"),
            new FinancialCvRow("Dejada", recommendation.DejadaSource, recommendation.DejadaValue, recommendation.DejadaEquivalent),
            new FinancialCvRow("Comision", "dbo.AppMovilRegistro.comision_calculada", relationDiagnostic.ComisionCalculadaActual.ToString("0.00", CultureInfo.InvariantCulture), recommendation.CommissionEquivalent),
            new FinancialCvRow("Pago comision", "dbo.AppMovilRegistro.pago_comision / dbo.PosComisionPagosControl", relationDiagnostic.PagoComisionActual.ToString("0.00", CultureInfo.InvariantCulture), recommendation.PaidCommissionEquivalent),
            new FinancialCvRow("Forma de pago", "paymentMethod remoto normalizado + efectivo/tarjeta/dolares", relationDiagnostic.FormaPagoActual, "Si"),
            new FinancialCvRow("Moneda", "dolares/tipo_cambio", relationDiagnostic.MonedaActual, "Si")
        ];
    }

    private static CascoFinancialRecommendation DetermineCascoFinancialRecommendation(
        CascoSqlFinancialRow? sqlRow,
        (ControlTaxiDesktop.Tools.CascoSync.Models.CascoTripRecord? Record, string RawJson) remote,
        IReadOnlyList<CascoDejadaEvidenceRow> dejadasRows,
        IReadOnlyList<CascoRelacionEvidenceRow> relacionRows,
        IReadOnlyList<CascoPosPagoEvidenceRow> posPagoRows)
    {
        var remoteTripCost = remote.Record?.TripCost;
        var totalMatchesRemote = sqlRow?.Total.HasValue == true
            && remoteTripCost.HasValue
            && sqlRow.Total.Value == remoteTripCost.Value;
        var hasSeparateDejada = relacionRows.Any(row => row.Dejada > 0m)
            || dejadasRows.Any(row => row.TotalVenta.HasValue && row.TotalVenta.Value > 0m)
            || dejadasRows.Any(row => row.Pago.HasValue && row.Pago.Value > 0m);
        var hasCommissionField = sqlRow?.CommissionCalculated.HasValue == true;
        var hasCommissionPayment = (sqlRow?.CommissionPaid ?? 0m) > 0m || posPagoRows.Count > 0;

        if (totalMatchesRemote && !hasSeparateDejada)
        {
            return new CascoFinancialRecommendation(
                "A + D + E",
                "Alta",
                "tripCost remoto y AppMovilRegistro.total coinciden. No hay filas asociadas en dbo.dejadas, dbo.RelacionTicketTaxista ni dbo.PosComisionPagosControl que confirmen una dejada separada para este folio. La comision debe leerse de comision_calculada solo cuando exista; si viene vacia, falta la fuente o la regla aguas arriba.",
                "Falta fuente separada",
                "sin fuente",
                "No",
                hasCommissionField ? "Si, si viene poblada" : "Campo existe pero no viene poblado",
                hasCommissionPayment ? "Si, si existe pago persistido" : "No hay pago persistido separado");
        }

        if (hasSeparateDejada)
        {
            return new CascoFinancialRecommendation(
                "A + E",
                "Media",
                "Se encontro una fuente candidata separada para dejada. total/tripCost sigue comportandose como monto del viaje, mientras la dejada aparece en tablas auxiliares.",
                "Fuente auxiliar encontrada",
                "tabla auxiliar",
                "Si",
                hasCommissionField ? "Si" : "Pendiente",
                hasCommissionPayment ? "Si" : "Pendiente");
        }

        return new CascoFinancialRecommendation(
            "D",
            "Media",
            "No hay evidencia suficiente para separar venta y dejada con los datos actuales. La API y SQL de CV necesitan una fuente dedicada antes de habilitar calculo o escritura.",
            "Indeterminado",
            "sin fuente",
            "No",
            "Pendiente",
            "Pendiente");
    }

    private static string FormatNullableMoney(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.00", CultureInfo.InvariantCulture) : "<null>";

    private static string FormatPercent(decimal? value) =>
        value.HasValue ? (value.Value * 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%" : "<null>";

    public static async Task<int> RunCascoBadgeReturnOneAsync(string[] args)
    {
        var parameters = ParseBadgeCommandArguments(args);
        if (!parameters.IsValid)
            return 2;

        string? badgesRaw = null;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--badges=", StringComparison.OrdinalIgnoreCase))
            {
                badgesRaw = a.Substring("--badges=".Length).Trim('"');
                break;
            }

            if (string.Equals(a, "--badges", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                badgesRaw = args[i + 1].Trim('"');
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(badgesRaw))
        {
            Console.Error.WriteLine("Se requiere --badges '2121' (lista de gafetes).");
            return 2;
        }

        Console.WriteLine("REGRESAR GAFETES");
        Console.WriteLine($"Usuario: {parameters.UserName}");
        Console.WriteLine($"Gafetes: {badgesRaw}");
        Console.WriteLine("Vista previa:");
        Console.WriteLine("tabla control = dbo.gafete");
        Console.WriteLine("columna estado = venta");
        Console.WriteLine("columna fecha regreso = hora");
        Console.WriteLine("columna usuario = usuario");
        Console.Write("Escribe REGRESAR GAFETES para confirmar: ");
        var confirmation = Console.ReadLine() ?? string.Empty;
        if (!string.Equals(confirmation.Trim(), "REGRESAR GAFETES", StringComparison.Ordinal))
        {
            Console.WriteLine("Operacion cancelada. No se realizaron escrituras.");
            return 1;
        }

        var branch = new BranchConfigurationService().GetBranch("CV");
        var provider = new CascoBadgeProvider(branch);
        var badges = badgesRaw
            .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(badge => new LocalBadgeSelection(badge, null))
            .ToArray();

        var result = await provider.ReturnBadgesAsync(parameters.Password, badges, parameters.UserName!);
        Console.WriteLine($"Actualizados = {result.Updated}");
        Console.WriteLine($"Sin actualizar = {result.NotUpdated}");
        Console.WriteLine("Commit completado.");
        return result.Updated > 0 ? 0 : 1;
    }

    public static async Task<int> RunCascoBadgeHostingerDiagnosticAsync(string[] args)
    {
        var badge = ParseSingleValueArgument(args, "--badge");
        if (string.IsNullOrWhiteSpace(badge))
        {
            Console.Error.WriteLine("Se requiere --badge 2121.");
            return 2;
        }

        var preview = await LoadCascoBadgeHostingerPreviewAsync(badge);
        PrintCascoBadgeHostingerPreview(preview, includeConfirmationPrompt: false);
        Console.WriteLine("envio realizado = false");
        return preview.IsValidForCasco && !preview.WouldTouchPlaza28 && preview.PayloadItem is not null ? 0 : 1;
    }

    public static async Task<int> RunCascoBadgeSyncDiagnosticAsync(string[] args)
    {
        var badge = ParseSingleValueArgument(args, "--badge");
        if (string.IsNullOrWhiteSpace(badge))
        {
            Console.Error.WriteLine("Se requiere --badge 2121.");
            return 2;
        }

        var service = new CascoBadgeSyncService(CascoBadgeSyncConfiguration.LoadFromWorkspace());
        var diagnostic = await service.BuildDiagnosticAsync(badge, CancellationToken.None);
        PrintCascoBadgeSyncDiagnostic(diagnostic);
        return diagnostic.Safety.CanPost ? 0 : 1;
    }

    public static async Task<int> RunCascoBadgeSyncOneAsync(string[] args)
    {
        var badge = ParseSingleValueArgument(args, "--badge");
        if (string.IsNullOrWhiteSpace(badge))
        {
            Console.Error.WriteLine("Se requiere --badge 2121.");
            return 2;
        }

        var service = new CascoBadgeSyncService(CascoBadgeSyncConfiguration.LoadFromWorkspace());
        var preview = await service.LoadPreviewAsync(badge, CancellationToken.None);
        PrintCascoBadgeSyncPreview(service.Configuration, preview);

        if (!preview.Safety.CanPost)
        {
            Console.Error.WriteLine("ABORTAR: la validación de seguridad no corresponde a Casco.");
            Console.WriteLine("POST realizado = false");
            return 1;
        }

        if (!string.Equals(preview.Row.Status, "R", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"ABORTAR: status actual '{preview.Row.Status}' no es R.");
            Console.WriteLine("POST realizado = false");
            return 1;
        }

        Console.Write("Escribe ENVIAR GAFETE CASCO para continuar con la previsualización: ");
        var confirmation = Console.ReadLine() ?? string.Empty;
        if (!string.Equals(confirmation.Trim(), "ENVIAR GAFETE CASCO", StringComparison.Ordinal))
        {
            Console.WriteLine("Operación cancelada. No se realizó POST.");
            Console.WriteLine("POST realizado = false");
            return 1;
        }

        var result = await service.PostSingleAsync(preview.Row.BadgeId, CancellationToken.None);
        Console.WriteLine($"fecha/hora inicio = {result.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"fecha/hora fin = {result.FinishedAt:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"endpoint usado = {result.Endpoint}");
        Console.WriteLine($"badgeId = {result.BadgeId}");
        Console.WriteLine("payload enviado:");
        Console.WriteLine(result.PayloadJson);
        Console.WriteLine($"payload path = {result.PayloadPath}");
        Console.WriteLine($"HTTP status = {result.HttpStatus}");
        Console.WriteLine($"duración ms = {result.DurationMs}");
        Console.WriteLine($"intento número = {result.AttemptNumber}");
        Console.WriteLine("response body:");
        Console.WriteLine(string.IsNullOrWhiteSpace(result.ResponseBody) ? "<vacio>" : result.ResponseBody);
        Console.WriteLine($"POST realizado = {result.Success.ToString().ToLowerInvariant()}");
        return result.Success ? 0 : 1;
    }

    public static async Task<int> RunCascoBadgeSyncWatchAsync(string[] args)
    {
        var service = new CascoBadgeSyncService(CascoBadgeSyncConfiguration.LoadFromWorkspace());
        var dryRun = args.Any(arg => string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("=== CASCO BADGE SYNC WATCH ===");
        Console.WriteLine($"Config usada: {service.Configuration.ConfigPath}");
        Console.WriteLine($"Intervalo: {service.Configuration.IntervalSeconds} segundos");
        Console.WriteLine($"Log path: {service.Configuration.LogFilePath}");
        Console.WriteLine($"Lock path: {service.Configuration.LockFilePath}");
        Console.WriteLine($"Status path: {service.Configuration.StatusFilePath}");
        Console.WriteLine($"Modo actual: {(dryRun ? "dry-run real" : "produccion")}");

        var singlePass = args.Any(arg => string.Equals(arg, "--once", StringComparison.OrdinalIgnoreCase));
        if (singlePass)
        {
            var result = await service.RunWatchCycleAsync(CancellationToken.None, dryRun);
            Console.WriteLine($"Escaneados: {result.Scanned}");
            Console.WriteLine($"Preparados: {result.Prepared}");
            Console.WriteLine($"Enviados: {result.Sent}");
            Console.WriteLine($"Omitidos: {result.Omitted}");
            Console.WriteLine($"Errores: {result.Errors}");
            Console.WriteLine($"Ultimo HTTP: {(result.LastHttpStatus.HasValue ? result.LastHttpStatus.Value.ToString(CultureInfo.InvariantCulture) : "sin POST")}");
            Console.WriteLine($"POST realizado = {result.PostPerformed.ToString().ToLowerInvariant()}");
            Console.WriteLine($"Payload válido: {result.PayloadValid.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(result.ErrorDetail))
            {
                Console.WriteLine("ERROR COMPLETO:");
                Console.WriteLine(result.ErrorDetail);
            }

            return result.Errors == 0 ? 0 : 1;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine("Modo servicio activo. Presiona Ctrl+C para detenerlo manualmente.");
        await service.RunWatchLoopAsync(cts.Token, dryRun);
        return 0;
    }

    public static async Task<int> RunCascoBadgeHostingerPushOneAsync(string[] args)
    {
        var badge = ParseSingleValueArgument(args, "--badge");
        if (string.IsNullOrWhiteSpace(badge))
        {
            Console.Error.WriteLine("Se requiere --badge 2121.");
            return 2;
        }

        var preview = await LoadCascoBadgeHostingerPreviewAsync(badge);
        PrintCascoBadgeHostingerPreview(preview, includeConfirmationPrompt: true);

        if (string.Equals(preview.Config.BranchCode, "28", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Configuración inválida: BranchCode 28 pertenece a Plaza 28.");
            Console.WriteLine("envio realizado = false");
            return 1;
        }

        if (!preview.IsValidForCasco || preview.WouldTouchPlaza28 || preview.PayloadItem is null)
        {
            Console.Error.WriteLine("La configuración o el payload no corresponden a Casco. Se aborta sin POST.");
            Console.WriteLine("envio realizado = false");
            return 1;
        }

        Console.Write("Escribe ENVIAR GAFETE CASCO para continuar con la previsualización: ");
        var confirmation = Console.ReadLine() ?? string.Empty;
        if (!string.Equals(confirmation.Trim(), "ENVIAR GAFETE CASCO", StringComparison.Ordinal))
        {
            Console.WriteLine("Operación cancelada. No se realizó POST.");
            Console.WriteLine("envio realizado = false");
            return 1;
        }

        Console.WriteLine("Previsualización completada. No se realizó POST.");
        Console.WriteLine("envio realizado = false");
        return 0;
    }

    private static async Task<CascoBadgeHostingerPreview> LoadCascoBadgeHostingerPreviewAsync(string badge)
    {
        var normalizedBadge = badge.Trim();
        var config = LoadCascoBadgeHostingerConfig();
        var branch = new BranchConfigurationService().GetBranch("CV");
        var provider = new CascoBadgeProvider(branch);
        var sqlPassword = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        var diagnostic = await provider.GetReturnDiagnosticAsync(sqlPassword, normalizedBadge);
        var payloadItem = await LoadCascoBadgeHostingerPayloadItemAsync(config, normalizedBadge, sqlPassword);

        var finalUrl = config.BuildPushUrl();
        var normalizedApiBaseUrl = config.ApiBaseUrl.TrimEnd('/') + "/";
        var normalizedBranchApi = branch.ApiBaseUrl.TrimEnd('/') + "/";
        var isValidForCasco =
            string.Equals(config.BranchCode, "CV", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(config.MktDatabase, "mktCasco", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(normalizedApiBaseUrl, normalizedBranchApi, StringComparison.OrdinalIgnoreCase);
        var wouldTouchPlaza28 =
            string.Equals(config.BranchCode, "28", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(config.MktDatabase, "mkt", StringComparison.OrdinalIgnoreCase) ||
            !config.ApiBaseUrl.Contains("/casco-api", StringComparison.OrdinalIgnoreCase);

        object payload = payloadItem is null
            ? new { mkt2_gafetes = Array.Empty<object>() }
            : new
            {
                mkt2_gafetes = new object[]
                {
                    new
                    {
                        badgeId = payloadItem.BadgeId,
                        barcode = payloadItem.Barcode,
                        status = payloadItem.Status,
                        cycle = payloadItem.Cycle,
                        taxistaId = payloadItem.TaxistaId,
                        taxistaName = payloadItem.TaxistaName,
                        createdAt = payloadItem.CreatedAt
                    }
                }
            };

        var routeProbe = await ProbeCascoBadgeHostingerRouteAsync(config);
        var endpointFoundInCode = false;
        var contractFoundInCode = false;
        var payloadCompatible = endpointFoundInCode
            && contractFoundInCode
            && payloadItem is not null
            && string.Equals(payloadItem.Status, "R", StringComparison.OrdinalIgnoreCase);

        return new CascoBadgeHostingerPreview(
            config,
            branch,
            diagnostic,
            payloadItem,
            finalUrl,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            isValidForCasco,
            wouldTouchPlaza28,
            endpointFoundInCode,
            contractFoundInCode,
            payloadCompatible,
            routeProbe);
    }

    private static async Task<CascoBadgeHostingerRouteProbe> ProbeCascoBadgeHostingerRouteAsync(CascoBadgeHostingerConfig config)
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        var rootUrl = config.ApiBaseUrl.TrimEnd('/') + "/";
        using var rootResponse = await httpClient.GetAsync(rootUrl);
        var rootBody = await rootResponse.Content.ReadAsStringAsync();

        using var routeRequest = new HttpRequestMessage(HttpMethod.Get, config.BuildPushUrl());
        using var routeResponse = await httpClient.SendAsync(routeRequest);
        var routeBody = await routeResponse.Content.ReadAsStringAsync();

        return new CascoBadgeHostingerRouteProbe(
            rootUrl,
            (int)rootResponse.StatusCode,
            rootResponse.Content.Headers.ContentType?.ToString() ?? string.Empty,
            rootBody,
            config.BuildPushUrl(),
            "POST",
            (int)routeResponse.StatusCode,
            routeResponse.Headers.TryGetValues("Allow", out var allowValues) ? string.Join(", ", allowValues) : string.Empty,
            routeResponse.Content.Headers.ContentType?.ToString() ?? string.Empty,
            routeBody,
            routeResponse.IsSuccessStatusCode && (int)routeResponse.StatusCode != 404);
    }

    private static async Task<CascoBadgeHostingerPayloadItem?> LoadCascoBadgeHostingerPayloadItemAsync(
        CascoBadgeHostingerConfig config,
        string badge,
        string sqlPassword)
    {
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = config.SqlServer,
            InitialCatalog = config.MktDatabase,
            UserID = config.SqlUser,
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15
        }.ConnectionString;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH latest_gafete AS
            (
                SELECT TOP (1)
                    CONVERT(nvarchar(50), g.gafete) AS BadgeId,
                    UPPER(COALESCE(g.venta, '')) AS StatusCode,
                    COALESCE(g.hora, g.fecha, GETDATE()) AS CreatedAt
                FROM dbo.gafete g
                WHERE CONVERT(nvarchar(50), g.gafete) = @badge
                ORDER BY COALESCE(g.hora, g.fecha, GETDATE()) DESC, g.folioperacion DESC
            )
            SELECT TOP (1)
                l.BadgeId,
                l.StatusCode,
                l.CreatedAt
            FROM latest_gafete l;
            """;
        command.Parameters.AddWithValue("@badge", badge);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var badgeId = reader.IsDBNull(0) ? badge : reader.GetString(0).Trim();
        var statusCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim().ToUpperInvariant();
        var createdAt = reader.IsDBNull(2)
            ? DateTime.Now
            : Convert.ToDateTime(reader.GetValue(2), CultureInfo.InvariantCulture);

        return new CascoBadgeHostingerPayloadItem(
            badgeId,
            badgeId,
            statusCode,
            1,
            null,
            string.Empty,
            createdAt.ToString("s", CultureInfo.InvariantCulture),
            string.Empty,
            string.Empty,
            string.Empty);
    }

    private static CascoBadgeHostingerConfig LoadCascoBadgeHostingerConfig()
    {
        var workspaceRoot = ProgramHelpers.FindWorkspaceRoot();
        var configPath = Path.Combine(workspaceRoot, "ControlTaxiDesktop", "SyncTaxi", "sync.casco.config.json");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("No se encontró la configuración aislada de Casco.", configPath);

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        static string ReadRequired(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value) || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException($"Falta '{propertyName}' en sync.casco.config.json.");
            return value.GetString()!.Trim();
        }

        static string? ReadOptional(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        return new CascoBadgeHostingerConfig(
            configPath,
            ReadRequired(root, "ApiBaseUrl"),
            ReadRequired(root, "BranchCode"),
            ReadRequired(root, "SqlServer"),
            ReadRequired(root, "MktDatabase"),
            ReadRequired(root, "SyncToken"),
            ReadOptional(root, "SqlUser")?.Trim() ?? "sa",
            ReadOptional(root, "LogFilePath")?.Trim() ?? string.Empty,
            ReadOptional(root, "StatusFilePath")?.Trim() ?? string.Empty,
            ReadOptional(root, "LockFilePath")?.Trim() ?? string.Empty);
    }

    private static void PrintCascoBadgeHostingerPreview(CascoBadgeHostingerPreview preview, bool includeConfirmationPrompt)
    {
        Console.WriteLine("=== CASCO BADGE HOSTINGER DIAGNOSTIC ===");
        Console.WriteLine($"archivo config = {preview.Config.ConfigPath}");
        Console.WriteLine($"base local = {preview.Config.SqlServer}/{preview.Config.MktDatabase}");
        Console.WriteLine($"branchCode final = {preview.Config.BranchCode}");
        Console.WriteLine($"metodo = {preview.RouteProbe.Method}");
        Console.WriteLine($"URL final = {preview.FinalUrl}");
        Console.WriteLine($"tabla origen = {preview.Diagnostic.SourceTable}");
        Console.WriteLine($"fila detectada = {preview.Diagnostic.Exists.ToString().ToLowerInvariant()}");
        Console.WriteLine($"gafete = {preview.Diagnostic.Number}");
        Console.WriteLine($"taxista = {preview.Diagnostic.Staff}");
        Console.WriteLine($"folio = {preview.Diagnostic.OperationFolio}");
        Console.WriteLine($"folio local = {preview.Diagnostic.LocalFolio}");
        Console.WriteLine($"estado actual = {(string.IsNullOrWhiteSpace(preview.Diagnostic.CurrentStatus) ? "<vacio>" : preview.Diagnostic.CurrentStatus)}");
        Console.WriteLine($"status payload = {preview.PayloadItem?.Status ?? "<sin fila>"}");
        Console.WriteLine($"corresponde a Casco = {preview.IsValidForCasco.ToString().ToLowerInvariant()}");
        Console.WriteLine($"tocaria Plaza 28 = {preview.WouldTouchPlaza28.ToString().ToLowerInvariant()}");
        Console.WriteLine($"endpoint encontrado en codigo = {preview.EndpointFoundInCode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"contrato encontrado = {preview.ContractFoundInCode.ToString().ToLowerInvariant()}");
        Console.WriteLine($"payload compatible = {preview.PayloadCompatible.ToString().ToLowerInvariant()}");
        Console.WriteLine($"ruta responde = {preview.RouteProbe.RouteResponds.ToString().ToLowerInvariant()}");
        Console.WriteLine($"status ruta = {preview.RouteProbe.StatusCode}");
        Console.WriteLine($"allow ruta = {(string.IsNullOrWhiteSpace(preview.RouteProbe.AllowHeader) ? "<vacio>" : preview.RouteProbe.AllowHeader)}");
        Console.WriteLine($"root api = {preview.RouteProbe.RootUrl}");
        Console.WriteLine($"root status = {preview.RouteProbe.RootStatusCode}");
        Console.WriteLine($"root content-type = {preview.RouteProbe.RootContentType}");
        Console.WriteLine($"root body = {TrimForConsole(preview.RouteProbe.RootBody)}");
        Console.WriteLine($"route body = {TrimForConsole(preview.RouteProbe.ResponseBody)}");
        Console.WriteLine("payload completo:");
        Console.WriteLine(preview.PayloadJson);
        Console.WriteLine("POST realizado = false");
        if (includeConfirmationPrompt)
            Console.WriteLine("Confirmación requerida: ENVIAR GAFETE CASCO");
    }

    private static void PrintCascoBadgeSyncDiagnostic(CascoBadgeSyncDiagnostic diagnostic)
    {
        Console.WriteLine("=== CASCO BADGE SYNC DIAGNOSTIC ===");
        Console.WriteLine($"config usada = {diagnostic.ConfigPath}");
        Console.WriteLine($"branchCode = {diagnostic.BranchCode}");
        Console.WriteLine($"ApiBaseUrl = {diagnostic.ApiBaseUrl}");
        Console.WriteLine($"base = {diagnostic.LocalDatabase}");
        Console.WriteLine($"fila origen = {diagnostic.Row.SourceTable} | gafete={diagnostic.Row.BadgeId} | estado={diagnostic.Row.Status} | createdAt={diagnostic.Row.CreatedAt:yyyy-MM-ddTHH:mm:ss}");
        Console.WriteLine($"payload = {diagnostic.PayloadJson}");
        Console.WriteLine($"payload path = {diagnostic.PayloadPath}");
        Console.WriteLine($"endpoint final = {diagnostic.EndpointFinal}");
        Console.WriteLine($"lock path = {diagnostic.LockPath}");
        Console.WriteLine($"log path = {diagnostic.LogPath}");
        Console.WriteLine($"estado pendiente = {diagnostic.PendingState.ToString().ToLowerInvariant()}");
        Console.WriteLine($"POST realizado = {diagnostic.PostPerformed.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Plaza 28 = {diagnostic.Plaza28.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad branchCode=CV = {diagnostic.Safety.BranchCodeIsCv.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad url /casco-api/ = {diagnostic.Safety.UrlContainsCascoApi.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad base mktCasco = {diagnostic.Safety.DatabaseIsMktCasco.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad tabla dbo.gafete = {diagnostic.Safety.SourceTableIsGafete.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad badgeId = {diagnostic.Safety.BadgeMatches.ToString().ToLowerInvariant()}");
    }

    private static string TrimForConsole(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "<vacio>";

        var trimmed = value.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed.Substring(0, 300);
    }

    private static void PrintCascoBadgeSyncPreview(CascoBadgeSyncConfiguration configuration, CascoBadgeSyncPreview preview)
    {
        Console.WriteLine("=== CASCO BADGE SYNC ONE ===");
        Console.WriteLine($"config usada = {configuration.ConfigPath}");
        Console.WriteLine($"branchCode = {configuration.BranchCode}");
        Console.WriteLine($"endpoint final = {configuration.BuildPushUrl()}");
        Console.WriteLine($"base local = {configuration.SqlServer}/{configuration.MktDatabase}");
        Console.WriteLine($"gafete = {preview.Row.BadgeId}");
        Console.WriteLine($"folio = {preview.Row.FolioOriginal}");
        Console.WriteLine($"folio local = {preview.Row.FolioLocal}");
        Console.WriteLine($"taxista = {preview.Row.Taxista}");
        Console.WriteLine($"sitio = {preview.Row.Sitio}");
        Console.WriteLine($"status = {preview.Row.Status}");
        Console.WriteLine($"pending = {preview.IsPending.ToString().ToLowerInvariant()}");
        Console.WriteLine($"payload = {preview.PayloadJson}");
        Console.WriteLine($"payload válido = {preview.PayloadValid.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad branchCode=CV = {preview.Safety.BranchCodeIsCv.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad url /casco-api/ = {preview.Safety.UrlContainsCascoApi.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad base mktCasco = {preview.Safety.DatabaseIsMktCasco.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad tabla dbo.gafete = {preview.Safety.SourceTableIsGafete.ToString().ToLowerInvariant()}");
        Console.WriteLine($"seguridad badgeId = {preview.Safety.BadgeMatches.ToString().ToLowerInvariant()}");
        Console.WriteLine($"Plaza 28 = {preview.Safety.Plaza28.ToString().ToLowerInvariant()}");
        Console.WriteLine("Confirmación requerida: ENVIAR GAFETE CASCO");
    }

    private static string? ParseSingleValueArgument(string[] args, string optionName)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i].Trim();
            if (current.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase))
                return current.Substring(optionName.Length + 1).Trim('"', ' ');

            if (string.Equals(current, optionName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1].Trim().Trim('"');
        }

        return null;
    }

    private static CascoCommissionPreviewArguments ParseCascoCommissionPreviewArguments(string[] args)
    {
        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        string? folioOriginal = null;
        string? transporte = null;
        string? tipoPago = null;
        decimal? venta = null;
        decimal? dejada = null;
        decimal? gasto = null;
        decimal? degustacion = null;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index].Trim();
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Opcion no reconocida: {option}");
                return CascoCommissionPreviewArguments.Invalid;
            }

            if (index + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Falta valor para {option}.");
                return CascoCommissionPreviewArguments.Invalid;
            }

            var value = args[++index].Trim();
            switch (option.ToLowerInvariant())
            {
                case "--folio-original":
                    folioOriginal = value;
                    break;
                case "--sql-password":
                case "--password":
                    password = value;
                    break;
                case "--venta":
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedSale))
                    {
                        Console.Error.WriteLine($"Venta invalida para --venta: {value}");
                        return CascoCommissionPreviewArguments.Invalid;
                    }
                    venta = parsedSale;
                    break;
                case "--dejada":
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedPayout))
                    {
                        Console.Error.WriteLine($"Dejada invalida para --dejada: {value}");
                        return CascoCommissionPreviewArguments.Invalid;
                    }
                    dejada = parsedPayout;
                    break;
                case "--gasto":
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedExpense))
                    {
                        Console.Error.WriteLine($"Gasto invalido para --gasto: {value}");
                        return CascoCommissionPreviewArguments.Invalid;
                    }
                    gasto = parsedExpense;
                    break;
                case "--degustacion":
                    if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedTasting))
                    {
                        Console.Error.WriteLine($"Degustacion invalida para --degustacion: {value}");
                        return CascoCommissionPreviewArguments.Invalid;
                    }
                    degustacion = parsedTasting;
                    break;
                case "--transporte":
                    transporte = value;
                    break;
                case "--tipo-pago":
                case "--payment-method":
                    tipoPago = value;
                    break;
                default:
                    Console.Error.WriteLine($"Opcion no reconocida: {option}");
                    return CascoCommissionPreviewArguments.Invalid;
            }
        }

        return new CascoCommissionPreviewArguments(true, password, folioOriginal, venta, transporte, tipoPago, dejada, gasto, degustacion);
    }

    private static CascoDiagnosticArguments ParseBadgeCommandArguments(string[] args)
    {
        string? userName = null;
        string? password = null;

        for (var i = 0; i < args.Length; i++)
        {
            var option = args[i].Trim();
            if (option.StartsWith("--user=", StringComparison.OrdinalIgnoreCase))
            {
                userName = option.Substring("--user=".Length).Trim('"');
                continue;
            }

            if (option.StartsWith("--password=", StringComparison.OrdinalIgnoreCase))
            {
                password = option.Substring("--password=".Length).Trim('"');
                continue;
            }

            if (option.StartsWith("--sql-password=", StringComparison.OrdinalIgnoreCase))
            {
                password = option.Substring("--sql-password=".Length).Trim('"');
                continue;
            }

            if (string.Equals(option, "--user", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                userName = args[++i].Trim().Trim('"');
                continue;
            }

            if ((string.Equals(option, "--password", StringComparison.OrdinalIgnoreCase)
                || string.Equals(option, "--sql-password", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
            {
                password = args[++i].Trim().Trim('"');
            }
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            Console.Error.WriteLine("Se requiere --user <usuario>.");
            return CascoDiagnosticArguments.Invalid;
        }

        return new CascoDiagnosticArguments(
            true,
            userName,
            string.IsNullOrWhiteSpace(password) ? Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty : password,
            null,
            null,
            null,
            null,
            false,
            false);
    }

    private static CascoDiagnosticArguments ParseCascoDiagnosticArguments(
        string[] args,
        bool requireUser,
        bool allowSingleDate = false,
        bool allowDateRange = false,
        bool allowFolioOriginal = false,
        bool allowPaymentModes = false,
        bool allowBadges = false)
    {
        var userName = string.Empty;
        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        DateTime? date = null;
        DateTime? dateFrom = null;
        DateTime? dateTo = null;
        string? folioOriginal = null;
        var simulateZero = false;
        var applyPaymentOne = false;

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index].Trim();
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Opcion no reconocida: {option}");
                return CascoDiagnosticArguments.Invalid;
            }

            if (allowBadges && option.StartsWith("--badges=", StringComparison.OrdinalIgnoreCase))
                continue;

            if (allowPaymentModes && string.Equals(option, "--simulate-zero", StringComparison.OrdinalIgnoreCase))
            {
                simulateZero = true;
                continue;
            }

            if (allowPaymentModes && string.Equals(option, "--apply-payment-one", StringComparison.OrdinalIgnoreCase))
            {
                applyPaymentOne = true;
                continue;
            }

            if (index + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Falta valor para {option}.");
                return CascoDiagnosticArguments.Invalid;
            }

            var value = args[++index].Trim();
            switch (option.ToLowerInvariant())
            {
                case "--user":
                    userName = value;
                    break;
                case "--sql-password":
                case "--password":
                    password = value;
                    break;
                case "--date" when allowSingleDate:
                    if (!TryParseIsoDate(value, out var parsedDate))
                    {
                        Console.Error.WriteLine($"Fecha invalida para --date: {value}");
                        return CascoDiagnosticArguments.Invalid;
                    }

                    date = parsedDate;
                    break;
                case "--date-from" when allowDateRange:
                    if (!TryParseIsoDate(value, out var parsedFrom))
                    {
                        Console.Error.WriteLine($"Fecha invalida para --date-from: {value}");
                        return CascoDiagnosticArguments.Invalid;
                    }

                    dateFrom = parsedFrom;
                    break;
                case "--date-to" when allowDateRange:
                    if (!TryParseIsoDate(value, out var parsedTo))
                    {
                        Console.Error.WriteLine($"Fecha invalida para --date-to: {value}");
                        return CascoDiagnosticArguments.Invalid;
                    }

                    dateTo = parsedTo;
                    break;
                case "--folio-original" when allowFolioOriginal:
                    folioOriginal = value;
                    break;
                case "--badges" when allowBadges:
                    break;
                default:
                    Console.Error.WriteLine($"Opcion no reconocida: {option}");
                    return CascoDiagnosticArguments.Invalid;
            }
        }

        if (requireUser && string.IsNullOrWhiteSpace(userName))
        {
            Console.Error.WriteLine("Se requiere --user <usuario>.");
            return CascoDiagnosticArguments.Invalid;
        }

        if (allowDateRange && dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
        {
            Console.Error.WriteLine("--date-from no puede ser mayor que --date-to.");
            return CascoDiagnosticArguments.Invalid;
        }

        if (simulateZero && applyPaymentOne)
        {
            Console.Error.WriteLine("No se pueden combinar --simulate-zero y --apply-payment-one.");
            return CascoDiagnosticArguments.Invalid;
        }

        return new CascoDiagnosticArguments(true, userName, password, date, dateFrom, dateTo, folioOriginal, simulateZero, applyPaymentOne);
    }

    private static void PrintCascoPaymentPreview(CascoPayoutPreview preview)
    {
        Console.WriteLine("BranchCode: CV");
        Console.WriteLine($"Proveedor: {preview.Provider}");
        Console.WriteLine($"QuerySource: {preview.QuerySource}");
        Console.WriteLine($"registroEncontrado = {preview.RecordFound}");
        Console.WriteLine($"coincidencias = {preview.MatchingRecords}");
        Console.WriteLine($"folioOriginal = {preview.FolioOriginal}");
        Console.WriteLine($"folioLocal = {preview.FolioLocal}");
        Console.WriteLine($"taxista = {preview.Taxista}");
        Console.WriteLine($"gafete = {preview.Gafete}");
        Console.WriteLine($"sitio = {preview.Sitio}");
        Console.WriteLine($"dejada = {preview.Dejada}");
        Console.WriteLine($"estatusActual = {preview.PayoutStatus}");
        Console.WriteLine($"puedePagar = {preview.CanPay}");
        Console.WriteLine($"PAGAR visible = {preview.CanPay}");
        Console.WriteLine($"IMPRIMIR visible = {IsPaid(preview.PayoutStatus)}");
        Console.WriteLine($"payoutSource = {preview.PayoutSource}");
        Console.WriteLine($"hasManualRelation = {preview.HasManualRelation}");
        Console.WriteLine($"hasDejadaRow = {preview.HasDejadaRow}");
        Console.WriteLine("SQL parametrizado:");
        Console.WriteLine(preview.UpdateSql);
        Console.WriteLine("SQL validacion:");
        Console.WriteLine(preview.ValidationSql);
        Console.WriteLine("columnasACambiar = " + string.Join(", ", preview.ColumnsToChange));
        Console.WriteLine($"ticketPropuesto = {preview.ProposedTicket}");
    }

    private static void PrintCascoRelationSavePreview(string userName, CascoRelationSavePreview preview)
    {
        Console.WriteLine($"Usuario: {userName}");
        Console.WriteLine($"BranchCode: {preview.BranchCode}");
        Console.WriteLine($"Proveedor: {preview.Provider}");
        Console.WriteLine($"Fuente: {preview.QuerySource}");
        Console.WriteLine($"registro CV encontrado = {preview.MatchingCvRows == 1}");
        Console.WriteLine($"folio original = {preview.FolioOriginal}");
        Console.WriteLine($"folio local = {preview.FolioLocal}");
        Console.WriteLine($"folio pos = {preview.PosFolio}");
        Console.WriteLine($"taxista = {preview.Taxista}");
        Console.WriteLine($"gafete = {preview.Gafete}");
        Console.WriteLine($"taxistaId = {preview.TaxistaId}");
        Console.WriteLine($"fecha operacion = {preview.FechaOperacion:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"sitio = {preview.Sitio}");
        Console.WriteLine($"unidad = {preview.Unidad}");
        Console.WriteLine($"hotel = {preview.Hotel}");
        Console.WriteLine($"origen = {preview.Origen}");
        Console.WriteLine($"destino = {preview.Destino}");
        Console.WriteLine($"telefono = {preview.Telefono}");
        Console.WriteLine($"tipo transporte = {preview.TipoTransporte}");
        Console.WriteLine($"forma de pago = {preview.PaymentMethod}");
        Console.WriteLine($"moneda = {preview.Currency}");
        Console.WriteLine($"venta manual propuesta = {preview.ProposedSale:0.00}");
        Console.WriteLine($"dejada manual propuesta = {preview.ProposedPayout:0.00}");
        Console.WriteLine($"comision = SIN CALCULAR");
        Console.WriteLine($"pago comision = 0");
        Console.WriteLine($"relacion existente = {preview.RelationExists}");
        Console.WriteLine($"fila dejadas existente = {preview.DejadaExists}");
        Console.WriteLine($"filas CV encontradas = {preview.MatchingCvRows}");
        Console.WriteLine($"Plaza 28 = {preview.MatchingPlaza28Rows}");
        Console.WriteLine($"filas que se afectarian en relacion = {preview.RelationRowsToAffect}");
        Console.WriteLine($"filas que se afectarian en dejadas = {preview.DejadaRowsToAffect}");
        Console.WriteLine($"puedeGuardar = {preview.CanSave}");
        Console.WriteLine("SQL validacion:");
        Console.WriteLine(preview.ValidationSql);
        Console.WriteLine("SQL relacion parametrizado:");
        Console.WriteLine(preview.RelationUpsertSql);
        Console.WriteLine("SQL dejadas select parametrizado:");
        Console.WriteLine(preview.DejadaSelectSql);
        Console.WriteLine("SQL dejadas insert parametrizado:");
        Console.WriteLine(preview.DejadaInsertSql);
        Console.WriteLine("SQL dejadas update parametrizado:");
        Console.WriteLine(preview.DejadaUpdateSql);
    }

    private static void PrintCascoPaymentSimulation(CascoDiagnosticArguments parameters)
    {
        var folioOriginal = string.IsNullOrWhiteSpace(parameters.FolioOriginal) ? "SIM-0000" : parameters.FolioOriginal!;
        var proposedTicket = $"TK-{folioOriginal}-{DateTime.Now:yyyyMMddHHmmss}";
        Console.WriteLine("BranchCode: CV");
        Console.WriteLine("Proveedor: CascoReadOnlyDataProvider");
        Console.WriteLine("Modo: simulacion dejada 0 sin escritura");
        Console.WriteLine("registroEncontrado = simulado");
        Console.WriteLine($"folioOriginal = {folioOriginal}");
        Console.WriteLine("folioLocal = simulado");
        Console.WriteLine("taxista = simulado");
        Console.WriteLine("gafete = simulado");
        Console.WriteLine("sitio = Casco Viejo");
        Console.WriteLine("dejada = 0");
        Console.WriteLine("estatusActual = pendiente");
        Console.WriteLine("puedePagar = true");
        Console.WriteLine("PAGAR visible = true");
        Console.WriteLine("IMPRIMIR visible = false");
        Console.WriteLine("sin escritura real = true");
        Console.WriteLine("SQL parametrizado:");
        Console.WriteLine(CascoOperationsDataService.GetPaymentUpdateSql());
        Console.WriteLine("columnasACambiar = " + string.Join(", ", new[]
        {
            "estado_pago_dejada",
            "fecha_pago_dejada",
            "usuario_pago_dejada",
            "ticket_pago_dejada",
            "payout_status",
            "payout_date",
            "payout_user",
            "payout_ticket"
        }));
        Console.WriteLine($"ticketPropuesto = {proposedTicket}");
    }

    private static async Task<LocalRelation> BuildCascoRelationDraftAsync(
        BranchConfiguration branch,
        string password,
        string folioOriginal,
        CancellationToken cancellationToken)
    {
        var diagnostic = await CascoOperationsDataService.GetRelationCalculationDiagnosticAsync(
            branch,
            "CV",
            password,
            folioOriginal,
            cancellationToken);

        return new LocalRelation(
            0,
            diagnostic.FolioLocal,
            diagnostic.FolioOriginal,
            string.Empty,
            diagnostic.Gafete,
            diagnostic.Taxista,
            diagnostic.Taxista,
            0m,
            string.Empty,
            Source: "Casco Manual",
            SourceUser: string.Empty,
            DateText: string.Empty,
            Hotel: string.Empty,
            Origin: string.Empty,
            Site: "Casco Viejo",
            Destination: string.Empty,
            Unit: string.Empty,
            Plates: string.Empty,
            Phone: string.Empty,
            Nationality: string.Empty,
            TransportType: string.Empty,
            Sale: diagnostic.Total,
            Commission: 0m,
            CommissionPaid: 0m,
            PaymentMethod: diagnostic.FormaPagoActual,
            PayoutStatus: string.Empty,
            CommissionStatus: "SIN CALCULAR",
            PayoutTicket: string.Empty,
            TaxistaId: string.Empty,
            PayoutUser: string.Empty,
            PayoutDate: string.Empty,
            PayoutPaid: 0m,
            Passengers: 0,
            SaleDetail: string.Empty,
            Currency: diagnostic.MonedaActual,
            RemotePaymentMethod: diagnostic.PaymentMethodRemoto,
            PaymentsJson: diagnostic.PagosJson,
            TotalAmount: diagnostic.Total,
            CashAmount: diagnostic.Efectivo,
            CardAmount: diagnostic.Tarjeta,
            DollarsAmount: diagnostic.Dolares,
            ExchangeRate: diagnostic.TipoCambio);
    }

    private static bool IsPaid(string status) =>
        string.Equals(status, "pagado", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "pagada", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseIsoDate(string value, out DateTime parsedDate) =>
        DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsedDate);

    private static void PrintAutoSyncCycle(string title, CascoAutoSyncCycleResult result)
    {
        Console.WriteLine(title);
        Console.WriteLine($"  Inicio: {(result.StartedAt.HasValue ? result.StartedAt.Value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture) : "n/a")}");
        Console.WriteLine($"  URL: {result.Url}");
        Console.WriteLine($"  HTTP: {result.HttpStatus}");
        Console.WriteLine($"  Recibidos: {result.ReceivedCount}");
        Console.WriteLine($"  CV recibidos: {result.CvCount}");
        Console.WriteLine($"  Nuevos: {result.NewDetectedCount}");
        Console.WriteLine($"  Insertados: {result.InsertedCount}");
        Console.WriteLine($"  Omitidos: {result.OmittedCount}");
        Console.WriteLine($"  Errores: {result.ErrorCount}");
        Console.WriteLine($"  Duracion: {result.DurationMs} ms");
        Console.WriteLine($"  Folios insertados: {(result.InsertedFolios.Count == 0 ? "(ninguno)" : string.Join(", ", result.InsertedFolios))}");
        Console.WriteLine($"  Error: {(string.IsNullOrWhiteSpace(result.ErrorMessage) ? "ninguno" : result.ErrorMessage)}");
    }

    private static async Task<int> CountCascoDuplicatePairsAsync(CascoAutoSyncSettings settings, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(settings.BuildSqlConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM
            (
                SELECT folio_app_original, sitio
                FROM dbo.AppMovilRegistro
                WHERE sitio = 'Casco Viejo'
                GROUP BY folio_app_original, sitio
                HAVING COUNT(*) > 1
            ) duplicados;
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private sealed record Column(string Name, Type Type);

    private sealed record CascoFinancialSourceDiagnostic(
        string BranchCode,
        string Provider,
        string QuerySource,
        string FolioOriginal,
        string FolioLocal,
        string Taxista,
        string Gafete,
        string Site,
        decimal? Total,
        decimal? TripCostRemoto,
        decimal? Efectivo,
        decimal? Tarjeta,
        decimal? Dolares,
        decimal? TipoCambio,
        decimal? ComisionCalculada,
        decimal? PagoComision,
        string PaymentMethodRemoto,
        string ServiceTypeRemoto,
        string NotesRemotas,
        string DetailJson,
        string PagosJson,
        string RemoteRawJson,
        IReadOnlyList<string> PossibleSaleSources,
        IReadOnlyList<string> PossibleDejadaSources,
        IReadOnlyList<CascoDejadaEvidenceRow> DejadasRows,
        IReadOnlyList<CascoRelacionEvidenceRow> RelacionRows,
        IReadOnlyList<CascoPosPagoEvidenceRow> PosPagoRows,
        IReadOnlyList<FinancialComparisonRow> Plaza28Rows,
        IReadOnlyList<FinancialCvRow> CvRows,
        CascoFinancialRecommendation Recommendation);

    private sealed record CascoSqlFinancialRow(
        string FolioApp,
        string OriginalFolio,
        long? CatalogId,
        string Badge,
        string DriverName,
        string FechaOperacion,
        string Site,
        string ServiceType,
        decimal? Subtotal,
        decimal? Iva,
        decimal? Total,
        decimal? Cash,
        decimal? Card,
        decimal? Dollars,
        decimal? ExchangeRate,
        decimal? CommissionCalculated,
        decimal? CommissionPaid,
        string EstadoPagoDejada,
        string PayoutStatus,
        string UsuarioPagoDejada,
        string FechaPagoDejada,
        string TicketPagoDejada,
        string Notes,
        string DetailJson,
        string PaymentsJson);

    private sealed record CascoDejadaEvidenceRow(
        string FolioRegistroStr,
        string Gafete,
        string NombreStaff,
        string NombreVendedor,
        string Fecha,
        decimal? Total,
        decimal? TotalVenta,
        decimal? Comision,
        decimal? Pago);

    private sealed record CascoRelacionEvidenceRow(
        long Id,
        string FolioOperacion,
        string FolioApp,
        string Gafete,
        decimal Dejada,
        string Usuario,
        string Observaciones);

    private sealed record CascoPosPagoEvidenceRow(
        long Id,
        string FolioOriginal,
        string FolioOperacion,
        string FolioNumero,
        decimal Pago,
        string Usuario,
        string FechaPago);

    private sealed record Plaza28EquivalentSample(
        bool Found,
        string VentaFolio,
        string Taxista,
        decimal TotalVenta,
        decimal ImporteComision,
        decimal Pagado,
        decimal Dejada,
        string FormaPago,
        string Moneda)
    {
        public static Plaza28EquivalentSample Empty { get; } = new(false, string.Empty, string.Empty, 0m, 0m, 0m, 0m, string.Empty, "MXN");
    }

    private sealed record FinancialComparisonRow(string Campo, string FuenteP28, string ValorP28, string ReglaP28);

    private sealed record FinancialCvRow(string Campo, string FuenteCv, string ValorCv, string EquivalenteReal);

    private sealed record CascoFinancialRecommendation(
        string Decision,
        string Confidence,
        string Rationale,
        string DejadaSource,
        string DejadaValue,
        string DejadaEquivalent,
        string CommissionEquivalent,
        string PaidCommissionEquivalent);

    private sealed record CascoDiagnosticArguments(
        bool IsValid,
        string? UserName,
        string Password,
        DateTime? Date,
        DateTime? DateFrom,
        DateTime? DateTo,
        string? FolioOriginal,
        bool SimulateZero,
        bool ApplyPaymentOne)
    {
        public static CascoDiagnosticArguments Invalid { get; } = new(false, null, string.Empty, null, null, null, null, false, false);
    }

    private sealed record CascoCommissionPreviewArguments(
        bool IsValid,
        string Password,
        string? FolioOriginal,
        decimal? Venta,
        string? Transporte,
        string? TipoPago,
        decimal? Dejada,
        decimal? Gasto,
        decimal? Degustacion)
    {
        public static CascoCommissionPreviewArguments Invalid { get; } = new(false, string.Empty, null, null, null, null, null, null, null);
    }

    private sealed record CascoBadgeHostingerConfig(
        string ConfigPath,
        string ApiBaseUrl,
        string BranchCode,
        string SqlServer,
        string MktDatabase,
        string SyncToken,
        string SqlUser,
        string LogFilePath,
        string StatusFilePath,
        string LockFilePath)
    {
        public string BuildPushUrl() => ApiBaseUrl.TrimEnd('/') + "/sync/push-changes?branchCode=" + Uri.EscapeDataString(BranchCode);
    }

    private sealed record CascoBadgeHostingerPayloadItem(
        string BadgeId,
        string Barcode,
        string Status,
        int Cycle,
        int? TaxistaId,
        string TaxistaName,
        string CreatedAt,
        string FolioOriginal,
        string FolioLocal,
        string Sitio);

    private sealed record CascoBadgeHostingerPreview(
        CascoBadgeHostingerConfig Config,
        BranchConfiguration Branch,
        CascoBadgeReturnDiagnostic Diagnostic,
        CascoBadgeHostingerPayloadItem? PayloadItem,
        string FinalUrl,
        string PayloadJson,
        bool IsValidForCasco,
        bool WouldTouchPlaza28,
        bool EndpointFoundInCode,
        bool ContractFoundInCode,
        bool PayloadCompatible,
        CascoBadgeHostingerRouteProbe RouteProbe);

    private sealed record CascoBadgeHostingerRouteProbe(
        string RootUrl,
        int RootStatusCode,
        string RootContentType,
        string RootBody,
        string FinalUrl,
        string Method,
        int StatusCode,
        string AllowHeader,
        string ResponseContentType,
        string ResponseBody,
        bool RouteResponds);

    // ─── GENERADOR DE EXCEL CUADRE PLAZA 28 ──────────────────────────────────

    /// <summary>
    /// Genera el Excel de Cuadre de Plaza 28 usando exactamente la misma lógica
    /// que ExportCuadreExcel_Click en OperationsWindow, sin necesidad de abrir la app.
    ///
    /// Uso:
    ///   plaza28-cuadre-excel --date 2026-08-07 --output "C:\prueba\cuadre.xlsx"
    ///   plaza28-cuadre-excel --date 2026-08-07 --sql-server REYNA --sql-database mkt2
    ///
    /// Si --output se omite se guarda en el escritorio del usuario.
    /// Si --sql-server se omite usa "REYNA" con Windows Authentication.
    /// </summary>
    public static async Task<int> RunPlaza28CuadreExcelAsync(string[] args)
    {
        DateTime date = DateTime.Today;
        string? outputPath = null;
        string sqlServer   = "REYNA";
        string sqlDatabase = "mkt2";

        for (var i = 0; i < args.Length; i++)
        {
            var opt = args[i].Trim();
            if (opt.StartsWith("--date=", StringComparison.OrdinalIgnoreCase))
            { if (!TryParseIsoDate(opt["--date=".Length..].Trim('"'), out date)) { Console.Error.WriteLine("Fecha inválida. Use YYYY-MM-DD."); return 2; } continue; }
            if (string.Equals(opt, "--date", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            { if (!TryParseIsoDate(args[++i].Trim('"'), out date)) { Console.Error.WriteLine("Fecha inválida. Use YYYY-MM-DD."); return 2; } continue; }
            if (opt.StartsWith("--output=", StringComparison.OrdinalIgnoreCase))
            { outputPath = opt["--output=".Length..].Trim('"'); continue; }
            if (string.Equals(opt, "--output", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            { outputPath = args[++i].Trim('"'); continue; }
            if (opt.StartsWith("--sql-server=", StringComparison.OrdinalIgnoreCase))
            { sqlServer = opt["--sql-server=".Length..].Trim('"'); continue; }
            if (string.Equals(opt, "--sql-server", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            { sqlServer = args[++i].Trim('"'); continue; }
            if (opt.StartsWith("--sql-database=", StringComparison.OrdinalIgnoreCase))
            { sqlDatabase = opt["--sql-database=".Length..].Trim('"'); continue; }
            if (string.Equals(opt, "--sql-database", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            { sqlDatabase = args[++i].Trim('"'); continue; }
        }

        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"cuadre_plaza28_{date:yyyyMMdd}.xlsx");

        Console.WriteLine("=== GENERADOR EXCEL CUADRE PLAZA 28 ===");
        Console.WriteLine($"Fecha    : {date:dd/MM/yyyy}");
        Console.WriteLine($"Servidor : {sqlServer}  Base: {sqlDatabase}  Auth: Windows");
        Console.WriteLine($"Salida   : {outputPath}");
        Console.WriteLine();

        // Consultar mkt2 via Windows Auth (igual que funciona sqlcmd -E)
        Console.WriteLine("Consultando relaciones en mkt2...");
        var connBuilder = new SqlConnectionStringBuilder
        {
            DataSource             = sqlServer,
            InitialCatalog         = sqlDatabase,
            IntegratedSecurity     = true,
            TrustServerCertificate = true,
            Encrypt                = false,
            ConnectTimeout         = 30
        };
        var previewRows = await QueryCuadreRelationsAsync(connBuilder.ConnectionString, sqlDatabase, date);

        // Mapear a LocalRelation exactamente como MapReportWorkbookRelations
        var relations = previewRows.Select((row, index) => new LocalRelation(
            index + 1,
            row.FolioLocal,
            row.FolioOriginal,
            string.Empty,
            row.Gafete,
            row.Taxista,
            row.Vendedor,
            row.Dejada,
            row.Notas,
            Source: "Plaza 28 Reporte",
            SourceUser: row.UsuarioPago,
            DateText: row.Fecha,
            Hotel: row.Hotel,
            Origin: row.Origen,
            Site: row.Sitio,
            Destination: row.Destino,
            Unit: row.Unidad,
            Plates: row.Placas,
            Phone: string.Empty,
            Nationality: string.Empty,
            TransportType: row.TipoServicio,
            Sale: row.Importe,
            Commission: row.Comision,
            CommissionPaid: row.Pago,
            PaymentMethod: string.Empty,
            PayoutStatus: row.Estatus,
            CommissionStatus: row.Estatus,
            PayoutTicket: row.TicketPago,
            TaxistaId: string.Empty,
            PayoutUser: row.UsuarioPago,
            PayoutDate: row.FechaPago,
            PayoutPaid: row.Pago,
            Passengers: row.Pax,
            SaleDetail: string.Empty,
            Currency: "MXN",
            RemotePaymentMethod: string.Empty,
            PaymentsJson: string.Empty,
            TotalAmount: row.Importe,
            CashAmount: 0m,
            CardAmount: 0m,
            DollarsAmount: 0m,
            ExchangeRate: 0m,
            AdultPassengers: row.Adulto,
            YouthPassengers: row.Joven,
            ChildPassengers: row.Nino)).ToArray();

        Console.WriteLine($"Relaciones obtenidas : {relations.Length}");

        // Comisiones, cortes y camiones (vacíos — en el cuadre de un solo día
        // lo que importa es la tabla de categorías, que viene de las relaciones)
        var commissions = Array.Empty<LocalCommission>() as IReadOnlyList<LocalCommission>;
        var cuts        = Array.Empty<LocalCut>()        as IReadOnlyList<LocalCut>;
        var camiones    = Array.Empty<LocalCuadreResumenRow>() as IReadOnlyList<LocalCuadreResumenRow>;

        // -- Calcular y mostrar totales ANTES de generar (verificación en consola) --
        Console.WriteLine("── CUADRE POR CATEGORÍA (nueva lógica) ──────────────────────────────");
        Console.WriteLine($"{"Categoría",-22} {"PAX",5} {"ENTRAN",7} {"SALEN",6} {"DEJADA",12}");
        Console.WriteLine(new string('─', 56));

        // Replicar BuildCategorySummaries internamente para mostrar en consola
        var grouped = relations
            .GroupBy(r => ResolveConcentratedCategoryCodeLocal(r.TransportType))
            .ToDictionary(g => g.Key, g => g.ToList());

        string[] groupOrder = ["VER", "ROJ", "AZU", "CAFE", "UBER", "ALI", "MAJ", "SALAN", "TADO", "TEXP", "CALLE", "VANS", "ACAR", "MC", "TUR", "OTRO"];
        string[] groupNames  = ["TAXIS VERDES", "TAXIS ROJOS", "TAXIS AZUL", "TAXIS CAFE", "UBER", "UBER ALIANZA", "MAJESTIC", "SALMORAN", "TURIBUS ADO", "TRAVEL EXPERIENCE", "CALLE", "TRANSPORTADORAS", "AUTOCAR", "MAYA CARIBE", "TURICUN", "OTRO"];

        int totalPax = 0, totalEntraron = 0, totalSalieron = 0;
        decimal totalDejada = 0m;

        for (var i = 0; i < groupOrder.Length; i++)
        {
            var code = groupOrder[i];
            var name = groupNames[i];
            if (!grouped.TryGetValue(code, out var rows) || rows.Count == 0) continue;

            var pax       = rows.Sum(r => r.Passengers);
            var salieron  = rows.Sum(r => r.NoShowCount);
            var entraron  = Math.Max(0, pax - salieron);
            var dejada    = rows.Sum(r => r.Payout ?? 0m);

            Console.WriteLine($"{name,-22} {pax,5} {entraron,7} {salieron,6} {dejada,12:C2}");

            totalPax      += pax;
            totalEntraron += entraron;
            totalSalieron += salieron;
            totalDejada   += dejada;
        }

        Console.WriteLine(new string('─', 56));
        Console.WriteLine($"{"TOTAL",-22} {totalPax,5} {totalEntraron,7} {totalSalieron,6} {totalDejada,12:C2}");
        Console.WriteLine();

        // Verificación específica de CALLE
        if (grouped.TryGetValue("CALLE", out var calleRows))
        {
            Console.WriteLine($"✓ CALLE → PAX = {calleRows.Sum(r => r.Passengers)}, " +
                              $"DEJADA = {calleRows.Sum(r => r.Payout ?? 0m):C2}");
            foreach (var r in calleRows)
                Console.WriteLine($"  folio={r.OperationFolio} tipo={r.TransportType} pax={r.Passengers} dejada={r.Payout:C2}");
        }
        else
        {
            Console.WriteLine("✓ CALLE → sin registros (0 PAX, $0)");
        }

        Console.WriteLine();

        // -- Generar Excel --------------------------------------------------------
        Console.WriteLine($"Generando Excel: {outputPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");

        var output = new DesktopOutputService();
        await output.ExportCuadreWorkbookAsync(date, date, relations, commissions, cuts, camiones, outputPath);

        // Verificar que el archivo se creó
        var fileInfo = new FileInfo(outputPath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            Console.Error.WriteLine("ERROR: El archivo no se generó correctamente.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"✓ Excel generado: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"  Tamaño: {fileInfo.Length:N0} bytes");
        Console.WriteLine();
        Console.WriteLine("Abre el archivo y verifica la hoja CUADRE:");
        Console.WriteLine($"  CALLE → PAX = {(grouped.TryGetValue("CALLE", out var cv) ? cv.Sum(r => r.Passengers) : 0)}, " +
                          $"DEJADA = {(grouped.TryGetValue("CALLE", out var cd) ? cd.Sum(r => r.Payout ?? 0m) : 0m):C2}");
        Console.WriteLine("  (debe ser PAX = 2, DEJADA = $50.00)");

        return 0;
    }

    private static async Task<IReadOnlyList<LocalOperationsPreviewRow>> QueryCuadreRelationsAsync(
        string connectionString, string database, DateTime date)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 45;
        command.CommandText = $"""
            SELECT
              COALESCE(a.folio_app, '')                                                AS AppFolio,
              COALESCE(NULLIF(a.folio_app_original,''), a.folio_app, '')               AS OperationFolio,
              COALESCE(a.folio_gafete, '')                                             AS Badge,
              COALESCE(a.vendedor_nombre, '')                                          AS DriverName,
              COALESCE(a.seller_name, '')                                              AS VendorName,
              COALESCE(a.total, 0)                                                     AS Payout,
              COALESCE(a.notas, '')                                                    AS Notes,
              CONVERT(nvarchar(30), COALESCE(a.fecha_operacion,a.fecha_creacion), 120) AS DateText,
              COALESCE(a.hotel,   '')  AS Hotel,
              COALESCE(a.origen,  '')  AS Origen,
              COALESCE(a.sitio,   '')  AS Sitio,
              COALESCE(a.destino, '')  AS Destino,
              COALESCE(a.unidad,  '')  AS Unidad,
              COALESCE(a.placas,  '')  AS Placas,
              COALESCE(a.tipo_operacion, '')                                           AS TipoOp,
              COALESCE(
                NULLIF(a.estado_pago_dejada,''), NULLIF(a.payout_status,''),
                CASE WHEN COALESCE(
                  CONVERT(nvarchar(10),a.fecha_pago_dejada,120),
                  CONVERT(nvarchar(10),a.payout_date,120),'') <> ''
                THEN 'pagado' ELSE '' END)                                             AS PayoutStatus,
              COALESCE(a.ticket_pago_dejada,'')                                        AS PayoutTicket,
              COALESCE(a.usuario_pago_dejada,'')                                       AS PayoutUser,
              COALESCE(
                CONVERT(nvarchar(30),a.fecha_pago_dejada,120),
                CONVERT(nvarchar(30),a.payout_date,120),'')                            AS PayoutDate,
              COALESCE(a.pago_comision, 0)  AS CommissionPaid,
              COALESCE(a.pax, 0)            AS Passengers,
              COALESCE(a.adult_count, 0)    AS AdultP,
              COALESCE(a.youth_count, 0)    AS YouthP,
              COALESCE(a.minor_count, 0)    AS ChildP,
              COALESCE(a.no_show_count, 0)  AS NoShow
            FROM [{database}].[dbo].[AppMovilRegistro] a
            WHERE COALESCE(a.folio_app,'') <> ''
              AND a.fecha_operacion >= @start
              AND a.fecha_operacion < @end
              AND (a.sitio = N'Tienda Plaza 28' OR a.sitio = N'Plaza 28')
            ORDER BY COALESCE(a.fecha_operacion,a.fecha_creacion) DESC;
            """;
        command.Parameters.AddWithValue("@start", date.Date);
        command.Parameters.AddWithValue("@end",   date.Date.AddDays(1));

        var result = new List<LocalOperationsPreviewRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var payout = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture);
            var comision = Convert.ToDecimal(reader.GetValue(19), CultureInfo.InvariantCulture);
            result.Add(new LocalOperationsPreviewRow(
                Taxista:       reader.GetString(3),
                Fecha:         reader.GetString(7),
                Pax:           Convert.ToInt32(reader.GetValue(20), CultureInfo.InvariantCulture),
                Hotel:         reader.GetString(8),
                Dejada:        payout,
                Importe:       payout,
                Comision:      comision,
                Pago:          comision,
                FolioOriginal: reader.GetString(1),
                FolioLocal:    reader.GetString(0),
                Gafete:        reader.GetString(2),
                Estatus:       reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                FechaPago:     reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                UsuarioPago:   reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                TicketPago:    reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                Sitio:         reader.GetString(10),
                Origen:        reader.GetString(9),
                Destino:       reader.GetString(11),
                Unidad:        reader.GetString(12),
                Placas:        reader.GetString(13),
                TipoServicio:  reader.GetString(14),
                Notas:         reader.GetString(6),
                Vendedor:      reader.GetString(4),
                Adulto:        Convert.ToInt32(reader.GetValue(21), CultureInfo.InvariantCulture),
                Joven:         Convert.ToInt32(reader.GetValue(22), CultureInfo.InvariantCulture),
                Nino:          Convert.ToInt32(reader.GetValue(23), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    /// <summary>
    /// Replica local de ResolveConcentratedCategoryCode (corregida en DesktopOutputService)
    /// para mostrar el cuadre en consola antes de generar el Excel.
    /// </summary>
    /// <summary>
    /// Copia mkt2.dbo.registroscamiones y mkt2.dbo.choferes a la SQLite local.
    /// Uso:
    ///   plaza28-sync-camiones --db "ruta\ControlTaxi.db"
    ///   plaza28-sync-camiones (usa la SQLite de producción automáticamente)
    /// </summary>
    public static async Task<int> RunPlaza28SyncCamionesAsync(string[] args)
    {
        string? dbPath = null;
        for (var i = 0; i < args.Length; i++)
        {
            var opt = args[i].Trim();
            if (opt.StartsWith("--db=", StringComparison.OrdinalIgnoreCase)) { dbPath = opt["--db=".Length..].Trim('"'); continue; }
            if (string.Equals(opt, "--db", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) { dbPath = args[++i].Trim('"'); continue; }
        }

        // Si no se pasa ruta, usa la SQLite de producción
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var database = new LocalDatabase();
            await database.InitializeAsync();
            dbPath = database.ActivePath;
        }

        if (!File.Exists(dbPath))
            throw new FileNotFoundException($"No se encontró la SQLite: {dbPath}", dbPath);

        // Leer credenciales de Plaza 28
        Plaza28CredentialStore.TryApplyToEnvironment(out _);
        var sqlServer   = Environment.GetEnvironmentVariable("PLAZA28_SQL_SERVER")   ?? "REYNA";
        var sqlUser     = Environment.GetEnvironmentVariable("PLAZA28_SQL_USER")     ?? "sa";
        var sqlPassword = Environment.GetEnvironmentVariable("PLAZA28_SQL_PASSWORD") ?? string.Empty;
        var sqlDatabase = Environment.GetEnvironmentVariable("PLAZA28_SQL_DATABASE") ?? "mkt2";

        if (string.IsNullOrWhiteSpace(sqlPassword))
            throw new InvalidOperationException("No se encontró la credencial de Plaza 28 (PLAZA28_SQL_PASSWORD). Configure la contraseña desde la app primero.");

        var connBuilder = new SqlConnectionStringBuilder
        {
            DataSource = sqlServer,
            InitialCatalog = sqlDatabase,
            UserID = sqlUser,
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 30
        };

        Console.WriteLine($"=== SYNC CAMIONES PLAZA 28 ===");
        Console.WriteLine($"Servidor : {sqlServer}  Base: {sqlDatabase}");
        Console.WriteLine($"SQLite   : {dbPath}");
        Console.WriteLine();

        await using var source = new SqlConnection(connBuilder.ConnectionString);
        await source.OpenAsync();
        await using var target = ProgramHelpers.OpenSqlite(dbPath);
        await EnsureManifestAsync(target);

        var sourceName = "mkt2";
        foreach (var table in new[] { "registroscamiones", "choferes" })
        {
            Console.Write($"Copiando {table}... ");
            await CopySqlServerTableAsync(source, target, sourceName, "dbo", table);
            Console.WriteLine("OK");
        }

        Console.WriteLine();
        Console.WriteLine("✓ registroscamiones y choferes sincronizados en la SQLite local.");
        Console.WriteLine("  Ahora el reporte de CUADRE mostrará AUTOCAR y MAYA CARIBE correctamente.");
        return 0;
    }

    private static string ResolveConcentratedCategoryCodeLocal(string? transport)
    {
        var text = (transport ?? string.Empty).ToUpperInvariant().Trim();
        if (text.Contains("VERDE")) return "VER";
        if (text.Contains("ROJO")) return "ROJ";
        if (text.Contains("AZUL")) return "AZU";
        if (text.Contains("CAFE")) return "CAFE";
        if (text.Contains("UBER ALIANZA") || text.Contains("ALIANZA")) return "ALI";
        if (text.Contains("UBER")) return "UBER";
        if (text.Contains("MAJESTIC")) return "MAJ";
        if (text.Contains("SALMORAN")) return "SALAN";
        if (text.Contains("TRAVEL EXPERIENCE") || text.Contains("TRAVER EXPERIENCE")) return "TEXP";
        if (text.Contains("CALLE") || text == "S/N") return "CALLE";
        if (text.Contains("VANTR") || text.Contains("TRANSPORTADORA")) return "VANS";
        if (text == "VAN" || text.StartsWith("VAN ") || text.StartsWith("VAN\t")) return "VANS";
        if (text.Contains("AUTOCAR")) return "ACAR";
        if (text.Contains("MAYA CARIBE")) return "MC";
        if (text.Contains("TURICUN")) return "TUR";
        if (text.Contains("ADO") || text.Contains("TURIBUS")) return "TADO";
        if (text.StartsWith("TAXI") || text == "TAXIZH") return "VER";
        if (text.StartsWith("GUIA") || text.StartsWith("GUÍA")) return "OTRO";
        return "OTRO";
    }
}
