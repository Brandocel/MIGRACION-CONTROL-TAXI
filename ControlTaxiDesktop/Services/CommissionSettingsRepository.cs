using System.Globalization;
using System.IO;
using System.Text;
using ControlTaxiDesktop.Models;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Services;

public sealed class CommissionSettingsRepository(LocalDatabase database)
{
    private const string DefaultStart = "2026-01-01";

    public async Task InitializeAsync()
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS CommissionSettingsRules (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Category TEXT NOT NULL,
              Code TEXT NOT NULL,
              Name TEXT NOT NULL,
              CommissionPercent REAL NOT NULL DEFAULT 0,
              CashRetentionPercent REAL NOT NULL DEFAULT 0,
              CardRetentionPercent REAL NOT NULL DEFAULT 0,
              AmexRetentionPercent REAL NOT NULL DEFAULT 0,
              PaymentKind TEXT NOT NULL DEFAULT '',
              AppliesPayout INTEGER NOT NULL DEFAULT 1,
              AppliesExpense INTEGER NOT NULL DEFAULT 1,
              Active INTEGER NOT NULL DEFAULT 1,
              EffectiveFrom TEXT NOT NULL,
              EffectiveTo TEXT NULL,
              UpdatedAt TEXT NOT NULL,
              UpdatedBy TEXT NOT NULL DEFAULT '',
              Notes TEXT NOT NULL DEFAULT '',
              UNIQUE(Category, Code, EffectiveFrom)
            );
            CREATE TABLE IF NOT EXISTS CommissionSettingsAudit (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Date TEXT NOT NULL,
              User TEXT NOT NULL,
              Module TEXT NOT NULL DEFAULT 'ConfiguracionComisiones',
              Category TEXT NOT NULL,
              Code TEXT NOT NULL,
              FieldName TEXT NOT NULL,
              PreviousValue TEXT NOT NULL DEFAULT '',
              NewValue TEXT NOT NULL DEFAULT '',
              Reason TEXT NOT NULL,
              EffectiveFrom TEXT NOT NULL DEFAULT '',
              EffectiveTo TEXT NOT NULL DEFAULT '',
              Machine TEXT NOT NULL DEFAULT '',
              Branch TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS CommissionSettingsDiagnostics (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              Date TEXT NOT NULL,
              Severity TEXT NOT NULL,
              Category TEXT NOT NULL,
              Concept TEXT NOT NULL,
              Detail TEXT NOT NULL,
              Action TEXT NOT NULL,
              Source TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_CommissionSettingsRules_CategoryCode
              ON CommissionSettingsRules(Category, Code, Active, EffectiveFrom, EffectiveTo);
            CREATE INDEX IF NOT EXISTS IX_CommissionSettingsAudit_Date
              ON CommissionSettingsAudit(Date);
            CREATE INDEX IF NOT EXISTS IX_CommissionSettingsDiagnostics_Date
              ON CommissionSettingsDiagnostics(Date);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "Module", "TEXT NOT NULL DEFAULT 'ConfiguracionComisiones'");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "EffectiveFrom", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "EffectiveTo", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "Machine", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "Branch", "TEXT NOT NULL DEFAULT ''");
        await SeedDefaultsAsync(connection);
    }

    public async Task<IReadOnlyList<CommissionSettingsRule>> GetRulesAsync(string? category = null, string? search = null, bool? active = null, DateTime? date = null)
    {
        await InitializeAsync();
        var filters = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (!string.IsNullOrWhiteSpace(category))
        {
            filters.Add("Category = $category");
            parameters.Add(("$category", category.Trim().ToUpperInvariant()));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("(Code LIKE $search OR Name LIKE $search OR Category LIKE $search OR PaymentKind LIKE $search)");
            parameters.Add(("$search", "%" + search.Trim() + "%"));
        }

        if (active is not null)
        {
            filters.Add("Active = $active");
            parameters.Add(("$active", active.Value ? 1 : 0));
        }

        if (date is not null)
        {
            filters.Add("EffectiveFrom <= $date AND (EffectiveTo IS NULL OR EffectiveTo >= $date)");
            parameters.Add(("$date", date.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        var where = filters.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filters);
        return await ReadRulesAsync($"""
            SELECT Id,Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,
                   PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes
            FROM CommissionSettingsRules
            {where}
            ORDER BY Category, Name, EffectiveFrom DESC;
            """, parameters.ToArray());
    }

    public async Task<IReadOnlyList<CommissionPaymentConfiguration>> GetActivePaymentConfigurationsAsync(DateTime? date = null)
    {
        var rules = await GetRulesAsync("FORMA_PAGO", active: true, date: date ?? DateTime.Today);
        return rules
            .Select(x => new CommissionPaymentConfiguration(x.Code, x.Name, x.PaymentKind, ResolveRetentionPercent(x), x.Active))
            .ToArray();
    }

    public async Task<CommissionSettingsSummary> GetSummaryAsync()
    {
        await InitializeAsync();
        await using var connection = database.Open();
        var active = await ScalarAsync(connection, "SELECT COUNT(*) FROM CommissionSettingsRules WHERE Active=1;");
        var transports = await ScalarAsync(connection, "SELECT COUNT(*) FROM CommissionSettingsRules WHERE Category='TRANSPORTE';");
        var payments = await ScalarAsync(connection, "SELECT COUNT(*) FROM CommissionSettingsRules WHERE Category='FORMA_PAGO';");
        var expiring = await ScalarAsync(connection, "SELECT COUNT(*) FROM CommissionSettingsRules WHERE Active=1 AND EffectiveTo IS NOT NULL AND EffectiveTo BETWEEN date('now') AND date('now', '+30 day');");
        var recent = await ScalarAsync(connection, "SELECT COUNT(*) FROM CommissionSettingsAudit WHERE Date >= datetime('now', '-7 day');");
        return new CommissionSettingsSummary((int)active, (int)transports, (int)payments, (int)expiring, (int)recent);
    }

    public async Task<IReadOnlyList<CommissionSettingsAuditRow>> GetAuditAsync()
    {
        await InitializeAsync();
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id,Date,User,Category,Code,FieldName,PreviousValue,NewValue,Reason
            FROM CommissionSettingsAudit
            ORDER BY Date DESC, Id DESC
            LIMIT 300;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<CommissionSettingsAuditRow>();
        while (await reader.ReadAsync())
        {
            result.Add(new CommissionSettingsAuditRow(
                reader.GetInt64(0),
                DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : DateTime.Today,
                Text(reader, 2),
                Text(reader, 3),
                Text(reader, 4),
                Text(reader, 5),
                Text(reader, 6),
                Text(reader, 7),
                Text(reader, 8)));
        }
        return result;
    }

    public async Task SaveRuleAsync(CommissionSettingsRule rule, string user, string reason, bool canEdit = true)
    {
        await InitializeAsync();
        if (!canEdit)
            throw new UnauthorizedAccessException("El usuario no tiene permiso para editar configuración de comisiones.");
        ValidateRule(rule, reason);
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var current = rule.Id > 0 ? await GetRuleByIdAsync(connection, transaction, rule.Id) : null;
        await EnsureNoOverlappingRuleAsync(connection, transaction, rule);

        if (rule.Id > 0 && current is not null)
        {
            if (rule.EffectiveFrom.Date <= current.EffectiveFrom.Date)
                throw new InvalidOperationException("Para conservar historial inmutable, la nueva vigencia debe iniciar despues de la vigencia actual. Usa una fecha posterior para crear la nueva regla.");
            await PreserveExistingRuleAsync(connection, transaction, current, rule.EffectiveFrom, user);
            await InsertRuleAsync(connection, transaction, rule with { Id = 0 }, user);
        }
        else
        {
            await InsertRuleAsync(connection, transaction, rule, user);
        }

        await WriteAuditAsync(connection, transaction, current, rule, user, reason);
        await transaction.CommitAsync();
    }

    public async Task DeactivatePrepublicationTestRuleAsync(long id, string user, string reason, bool canEdit = true)
    {
        await InitializeAsync();
        if (!canEdit)
            throw new UnauthorizedAccessException("El usuario no tiene permiso para editar configuracion de comisiones.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("El motivo del cambio es obligatorio.");

        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var current = await GetRuleByIdAsync(connection, transaction, id)
            ?? throw new InvalidOperationException("No se encontro la regla seleccionada.");
        if (!IsPrepublicationTestRule(current))
            throw new InvalidOperationException("Esta accion solo puede retirar la regla aislada PRUEBA PREPUBLICACION. No modifica reglas reales.");

        var updated = current with
        {
            Active = false,
            EffectiveTo = current.EffectiveTo ?? DateTime.Today,
            UpdatedBy = user
        };

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE CommissionSettingsRules
            SET Active=0,
                EffectiveTo=$to,
                UpdatedAt=$updatedAt,
                UpdatedBy=$updatedBy
            WHERE Id=$id;
            """;
        command.Parameters.AddWithValue("$to", updated.EffectiveTo?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedBy", string.IsNullOrWhiteSpace(user) ? Environment.UserName : user);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();

        await WriteAuditAsync(connection, transaction, current, updated, user, reason);
        await transaction.CommitAsync();
    }

    public async Task<CommissionSimulationResult> SimulateAsync(CommissionSimulationInput input)
    {
        await InitializeAsync();
        return await new CommissionConfigurationResolver(this).SimulateAsync(input);
    }

    public async Task ExportCatalogCsvAsync(string path)
    {
        var rows = await GetRulesAsync();
        var builder = new StringBuilder();
        builder.AppendLine("Categoria,Clave,Nombre,Comision %,Efectivo %,Tarjeta %,AMEX %,Tipo pago,Vigencia,Estado,Actualizado,Usuario,Notas");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(row.Category), Csv(row.Code), Csv(row.Name), row.CommissionPercent.ToString(CultureInfo.InvariantCulture),
                row.CashRetentionPercent.ToString(CultureInfo.InvariantCulture), row.CardRetentionPercent.ToString(CultureInfo.InvariantCulture),
                row.AmexRetentionPercent.ToString(CultureInfo.InvariantCulture), Csv(row.PaymentKind), Csv(row.EffectiveRange),
                Csv(row.StatusText), Csv(row.UpdatedAt), Csv(row.UpdatedBy), Csv(row.Notes)
            }));
        }
        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8);
    }

    public async Task ExportCatalogExcelAsync(string path)
    {
        var rows = await GetRulesAsync();
        var sheetRows = rows.Select(row => new
        {
            Categoria = row.Category,
            Concepto = row.Name,
            Valor = row.Category == "FORMA_PAGO" ? ResolveRetentionPercent(row) : row.CommissionPercent,
            Vigencia = row.EffectiveRange,
            Activo = row.StatusText,
            Fuente = row.UpdatedBy == "MIGRACION" ? "CATALOGO_BASE/SEMILLA" : "CONFIGURACION_LOCAL",
            UltimaModificacion = row.UpdatedAt,
            Usuario = row.UpdatedBy
        }).ToArray();

        var html = new StringBuilder();
        html.AppendLine("<html><head><meta charset='utf-8'><style>");
        html.AppendLine("table{border-collapse:collapse;font-family:Calibri;font-size:11pt} th{background:#0E4DB7;color:white;font-weight:bold} td,th{border:1px solid #9DB8D3;padding:6px} .money{mso-number-format:'0.00'}");
        html.AppendLine("</style></head><body><table>");
        html.AppendLine("<tr><th>Categoría</th><th>Concepto</th><th>Valor</th><th>Vigencia</th><th>Activo</th><th>Fuente</th><th>Última modificación</th><th>Usuario</th></tr>");
        foreach (var row in sheetRows)
        {
            html.AppendLine($"<tr><td>{Html(row.Categoria)}</td><td>{Html(row.Concepto)}</td><td class='money'>{row.Valor.ToString(CultureInfo.InvariantCulture)}</td><td>{Html(row.Vigencia)}</td><td>{Html(row.Activo)}</td><td>{Html(row.Fuente)}</td><td>{Html(row.UltimaModificacion)}</td><td>{Html(row.Usuario)}</td></tr>");
        }
        html.AppendLine("</table></body></html>");
        await File.WriteAllTextAsync(path, html.ToString(), Encoding.UTF8);
    }

    public async Task<IReadOnlyList<CommissionDiagnosticRow>> GetDiagnosticsAsync()
    {
        await InitializeAsync();
        await using var connection = database.Open();
        var result = new List<CommissionDiagnosticRow>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT Id,Date,Severity,Category,Concept,Detail,Action,Source
                FROM CommissionSettingsDiagnostics
                ORDER BY Date DESC, Id DESC
                LIMIT 300;
                """;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new CommissionDiagnosticRow(
                    reader.GetInt64(0),
                    DateTime.TryParse(Text(reader, 1), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : DateTime.Today,
                    Text(reader, 2),
                    Text(reader, 3),
                    Text(reader, 4),
                    Text(reader, 5),
                    Text(reader, 6),
                    Text(reader, 7)));
            }
        }

        var rules = await GetRulesAsync();
        var today = DateTime.Today;
        foreach (var expired in rules.Where(x => x.Active && x.EffectiveTo is not null && x.EffectiveTo.Value.Date < today))
            result.Add(new CommissionDiagnosticRow(0, today, "BAJA", expired.Category, expired.Code, "Regla vencida.", "Revisar si requiere nueva vigencia.", "ANALISIS_LOCAL"));
        foreach (var future in rules.Where(x => x.Active && x.EffectiveFrom.Date > today))
            result.Add(new CommissionDiagnosticRow(0, today, "INFO", future.Category, future.Code, "Regla futura.", "Validar fecha de entrada en vigor.", "ANALISIS_LOCAL"));

        return result;
    }

    public async Task LogDiagnosticAsync(string severity, string category, string concept, string detail, string action, string source)
    {
        await InitializeAsync();
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CommissionSettingsDiagnostics (Date,Severity,Category,Concept,Detail,Action,Source)
            VALUES ($date,$severity,$category,$concept,$detail,$action,$source);
            """;
        command.Parameters.AddWithValue("$date", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$severity", severity);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$concept", concept);
        command.Parameters.AddWithValue("$detail", detail);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$source", source);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedDefaultsAsync(SqliteConnection connection)
    {
        await SeedGeneralAsync(connection);
        await SeedPaymentsAsync(connection);
        await SeedTransportsAsync(connection);
        await SeedGuidesAsync(connection);
        await SeedConceptAsync(connection, "JOYERIA", "JOYERIA_GENERAL", "Joyería general", 10m, "Regla económica inicial para joyería.");
        await SeedConceptAsync(connection, "TEQUILA", "TEQUILA_GENERAL", "Tequila general", 10m, "Regla económica inicial para tequila.");
        await SeedConceptAsync(connection, "DEJADA", "DEJADA_GENERAL", "Aplicar dejada una sola vez", 0m, "Regla de aplicación de dejadas; no captura importes históricos.");
        await SeedConceptAsync(connection, "GASTO", "GASTO_GENERAL", "Gastos y egresos reales", 0m, "Descuenta gastos vinculados por folio/remisión real.");
    }

    private static async Task SeedGeneralAsync(SqliteConnection connection)
    {
        await InsertSeedAsync(connection, "GENERAL", "DEFAULT_COMMISSION", "Comisión general por defecto", 10m, 0m, 19m, 24m, "", "Fallback documentado cuando no hay transporte configurado.");
    }

    private static async Task PreserveExistingRuleAsync(SqliteConnection connection, SqliteTransaction transaction, CommissionSettingsRule current, DateTime newEffectiveFrom, string user)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var closeDate = newEffectiveFrom.Date > current.EffectiveFrom.Date
            ? newEffectiveFrom.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : current.EffectiveTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        command.CommandText = """
            UPDATE CommissionSettingsRules
            SET EffectiveTo=$to, Active=$active, UpdatedAt=$updatedAt, UpdatedBy=$updatedBy
            WHERE Id=$id;
            """;
        command.Parameters.AddWithValue("$to", closeDate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$active", newEffectiveFrom.Date > current.EffectiveFrom.Date ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedBy", string.IsNullOrWhiteSpace(user) ? Environment.UserName : user);
        command.Parameters.AddWithValue("$id", current.Id);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertRuleAsync(SqliteConnection connection, SqliteTransaction transaction, CommissionSettingsRule rule, string user)
    {
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO CommissionSettingsRules
            (Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,
             PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes)
            VALUES
            ($category,$code,$name,$commission,$cash,$card,$amex,$paymentKind,$payout,$expense,$active,$from,$to,$updatedAt,$updatedBy,$notes);
            """;
        AddRuleParameters(insert, rule, user);
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task SeedPaymentsAsync(SqliteConnection connection)
    {
        await InsertSeedAsync(connection, "FORMA_PAGO", "PESOS", "Pesos / efectivo", 0m, 0m, 0m, 0m, "EFECTIVO", "Efectivo sin retención.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "EFECTIVO", "Efectivo", 0m, 0m, 0m, 0m, "EFECTIVO", "Efectivo sin retención.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "MERCADO PAGO", "Mercado Pago", 0m, 0m, 19m, 0m, "TARJETA_NORMAL", "Clasificación inicial validada.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "MIFEL", "MIFEL", 0m, 0m, 19m, 0m, "TARJETA_NORMAL", "Clasificación inicial validada.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "TARJETA", "Tarjeta normal", 0m, 0m, 19m, 0m, "TARJETA_NORMAL", "Retención general de tarjeta.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "AMEX", "AMEX", 0m, 0m, 0m, 24m, "AMEX", "Retención especial AMEX.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "AMEXCO", "AMEXCO", 0m, 0m, 0m, 24m, "AMEX", "Retención especial AMEX.");
    }

    private static async Task SeedTransportsAsync(SqliteConnection connection)
    {
        if (await HasTableAsync(connection, "LocalTransportes"))
        {
            await SeedTransportsFromTableAsync(connection, "LocalTransportes", "Clave", "Nombre", "Comision", "DescuentoEfectivo", "DescuentoTarjeta", "DescuentoAmex");
        }
        if (await HasTableAsync(connection, "mkt__dbo__transporte"))
        {
            await SeedTransportsFromTableAsync(connection, "mkt__dbo__transporte", "tipo", "nombre", "comision", "efectivo", "tarjeta", "amexco");
        }
        await InsertSeedAsync(connection, "TRANSPORTE", "TURIBUS ADO", "TURIBUS ADO", 10m, 0m, 19m, 24m, "", "Semilla segura si el catálogo importado aún no existe.");
        await InsertSeedAsync(connection, "TRANSPORTE", "TURIBUS SALMORAN", "TURIBUS SALMORAN", 20m, 16m, 19m, 24m, "", "Semilla segura validada para SALMORAN.");
        await InsertSeedAsync(connection, "TRANSPORTE", "MAJESTIC", "MAJESTIC / guía", 8m, 0m, 19m, 24m, "", "Semilla inicial para regla tipo guía.");
    }

    private static async Task SeedGuidesAsync(SqliteConnection connection)
    {
        if (!await HasTableAsync(connection, "LocalGuias")) return;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Clave,Nombre,Comision FROM LocalGuias WHERE COALESCE(TRIM(Clave), '')<>'' OR COALESCE(TRIM(Nombre), '')<>'';";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = Text(reader, 0);
            var name = Text(reader, 1);
            var commission = Decimal(reader, 2);
            await InsertSeedAsync(connection, "GUIA", string.IsNullOrWhiteSpace(code) ? name : code, name, commission, 0m, 19m, 24m, "", "Migrado desde LocalGuias.");
        }
    }

    private static async Task SeedTransportsFromTableAsync(SqliteConnection connection, string table, string codeColumn, string nameColumn, string commissionColumn, string cashColumn, string cardColumn, string amexColumn)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COALESCE("{codeColumn}", ''), COALESCE("{nameColumn}", ''), COALESCE("{commissionColumn}", 0),
                   COALESCE("{cashColumn}", 0), COALESCE("{cardColumn}", 0), COALESCE("{amexColumn}", 0)
            FROM "{table}"
            WHERE COALESCE(TRIM("{codeColumn}"), '')<>'' OR COALESCE(TRIM("{nameColumn}"), '')<>'';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var code = Text(reader, 0);
            var name = Text(reader, 1);
            await InsertSeedAsync(connection, "TRANSPORTE", string.IsNullOrWhiteSpace(code) ? name : code, name, Decimal(reader, 2), Decimal(reader, 3), Decimal(reader, 4), Decimal(reader, 5), "", "Migrado desde " + table + ".");
        }
    }

    private static async Task SeedConceptAsync(SqliteConnection connection, string category, string code, string name, decimal commission, string notes) =>
        await InsertSeedAsync(connection, category, code, name, commission, 0m, 19m, 24m, "", notes);

    private static async Task InsertSeedAsync(SqliteConnection connection, string category, string code, string name, decimal commission, decimal cash, decimal card, decimal amex, string paymentKind, string notes)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name)) return;
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO CommissionSettingsRules
            (Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,
             PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes)
            VALUES ($category,$code,$name,$commission,$cash,$card,$amex,$paymentKind,1,1,1,$from,NULL,$updatedAt,'MIGRACION',$notes);
            """;
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$code", NormalizeCode(code));
        command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? code.Trim() : name.Trim());
        command.Parameters.AddWithValue("$commission", NormalizePercent(commission));
        command.Parameters.AddWithValue("$cash", NormalizePercent(cash));
        command.Parameters.AddWithValue("$card", NormalizePercent(card));
        command.Parameters.AddWithValue("$amex", NormalizePercent(amex));
        command.Parameters.AddWithValue("$paymentKind", paymentKind);
        command.Parameters.AddWithValue("$from", DefaultStart);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$notes", notes);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<CommissionSettingsRule>> ReadRulesAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<CommissionSettingsRule>();
        while (await reader.ReadAsync())
            result.Add(MapRule(reader));
        return result;
    }

    private static CommissionSettingsRule MapRule(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        Text(reader, 1),
        Text(reader, 2),
        Text(reader, 3),
        Decimal(reader, 4),
        Decimal(reader, 5),
        Decimal(reader, 6),
        Decimal(reader, 7),
        Text(reader, 8),
        reader.GetInt64(9) == 1,
        reader.GetInt64(10) == 1,
        reader.GetInt64(11) == 1,
        DateTime.TryParse(Text(reader, 12), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var from) ? from : DateTime.Parse(DefaultStart, CultureInfo.InvariantCulture),
        string.IsNullOrWhiteSpace(Text(reader, 13)) ? null : DateTime.TryParse(Text(reader, 13), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var to) ? to : null,
        Text(reader, 14),
        Text(reader, 15),
        Text(reader, 16));

    private static void ValidateRule(CommissionSettingsRule rule, string reason)
    {
        if (string.IsNullOrWhiteSpace(rule.Category)) throw new InvalidOperationException("La categoria es obligatoria.");
        if (string.IsNullOrWhiteSpace(rule.Code)) throw new InvalidOperationException("La clave es obligatoria.");
        if (string.IsNullOrWhiteSpace(rule.Name)) throw new InvalidOperationException("El nombre es obligatorio.");
        if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("El motivo del cambio es obligatorio.");
        foreach (var value in new[] { rule.CommissionPercent, rule.CashRetentionPercent, rule.CardRetentionPercent, rule.AmexRetentionPercent })
        {
            if (value < 0m || value > 100m) throw new InvalidOperationException("Los porcentajes deben estar entre 0 y 100.");
        }
        if (rule.EffectiveTo is not null && rule.EffectiveTo.Value.Date < rule.EffectiveFrom.Date)
            throw new InvalidOperationException("La fecha fin no puede ser menor a la fecha inicio.");
    }

    private static async Task EnsureNoOverlappingRuleAsync(SqliteConnection connection, SqliteTransaction transaction, CommissionSettingsRule rule)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM CommissionSettingsRules
            WHERE Id <> $id
              AND Category=$category
              AND Code=$code
              AND Active=1
              AND $active=1
              AND EffectiveFrom <= COALESCE($to, '9999-12-31')
              AND COALESCE(EffectiveTo, '9999-12-31') >= $from;
            """;
        command.Parameters.AddWithValue("$id", rule.Id);
        command.Parameters.AddWithValue("$category", rule.Category.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$code", NormalizeCode(rule.Code));
        command.Parameters.AddWithValue("$active", rule.Active ? 1 : 0);
        command.Parameters.AddWithValue("$from", rule.EffectiveFrom.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", rule.EffectiveTo?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        if (count > 0)
            throw new InvalidOperationException("Ya existe una regla activa para ese concepto y periodo.");
    }

    private static async Task<CommissionSettingsRule?> GetRuleByIdAsync(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id,Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,
                   PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes
            FROM CommissionSettingsRules
            WHERE Id=$id;
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapRule(reader) : null;
    }

    private static async Task WriteAuditAsync(SqliteConnection connection, SqliteTransaction transaction, CommissionSettingsRule? previous, CommissionSettingsRule current, string user, string reason)
    {
        var changes = previous is null
            ? [("ALTA", "", Snapshot(current))]
            : Diff(previous, current);
        foreach (var change in changes)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO CommissionSettingsAudit
                (Date,User,Module,Category,Code,FieldName,PreviousValue,NewValue,Reason,EffectiveFrom,EffectiveTo,Machine,Branch)
                VALUES ($date,$user,'ConfiguracionComisiones',$category,$code,$field,$previous,$new,$reason,$from,$to,$machine,$branch);
                """;
            command.Parameters.AddWithValue("$date", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(user) ? Environment.UserName : user);
            command.Parameters.AddWithValue("$category", current.Category.Trim().ToUpperInvariant());
            command.Parameters.AddWithValue("$code", NormalizeCode(current.Code));
            command.Parameters.AddWithValue("$field", change.Item1);
            command.Parameters.AddWithValue("$previous", change.Item2);
            command.Parameters.AddWithValue("$new", change.Item3);
            command.Parameters.AddWithValue("$reason", reason.Trim());
            command.Parameters.AddWithValue("$from", current.EffectiveFrom.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$to", current.EffectiveTo?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);
            command.Parameters.AddWithValue("$machine", Environment.MachineName);
            command.Parameters.AddWithValue("$branch", string.Empty);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<(string, string, string)> Diff(CommissionSettingsRule oldRule, CommissionSettingsRule newRule)
    {
        var oldSnapshot = Snapshot(oldRule);
        var newSnapshot = Snapshot(newRule);
        if (!string.Equals(oldSnapshot, newSnapshot, StringComparison.Ordinal))
            yield return ("CAMBIO", oldSnapshot, newSnapshot);
    }

    private static string Snapshot(CommissionSettingsRule rule) =>
        $"{rule.Category}|{rule.Code}|{rule.Name}|C={rule.CommissionPercent:0.####}|E={rule.CashRetentionPercent:0.####}|T={rule.CardRetentionPercent:0.####}|A={rule.AmexRetentionPercent:0.####}|Pago={rule.PaymentKind}|Vig={rule.EffectiveRange}|Activo={rule.Active}";

    private static bool IsPrepublicationTestRule(CommissionSettingsRule rule) =>
        string.Equals(NormalizeCode(rule.Code), "PRUEBA PREPUBLICACION", StringComparison.OrdinalIgnoreCase)
        || string.Equals(rule.Name.Trim(), "PRUEBA PREPUBLICACION", StringComparison.OrdinalIgnoreCase);

    private static void AddRuleParameters(SqliteCommand command, CommissionSettingsRule rule, string user)
    {
        command.Parameters.AddWithValue("$category", rule.Category.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$code", NormalizeCode(rule.Code));
        command.Parameters.AddWithValue("$name", rule.Name.Trim());
        command.Parameters.AddWithValue("$commission", NormalizePercent(rule.CommissionPercent));
        command.Parameters.AddWithValue("$cash", NormalizePercent(rule.CashRetentionPercent));
        command.Parameters.AddWithValue("$card", NormalizePercent(rule.CardRetentionPercent));
        command.Parameters.AddWithValue("$amex", NormalizePercent(rule.AmexRetentionPercent));
        command.Parameters.AddWithValue("$paymentKind", rule.PaymentKind.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$payout", rule.AppliesPayout ? 1 : 0);
        command.Parameters.AddWithValue("$expense", rule.AppliesExpense ? 1 : 0);
        command.Parameters.AddWithValue("$active", rule.Active ? 1 : 0);
        command.Parameters.AddWithValue("$from", rule.EffectiveFrom.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", rule.EffectiveTo?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updatedBy", string.IsNullOrWhiteSpace(user) ? Environment.UserName : user);
        command.Parameters.AddWithValue("$notes", rule.Notes.Trim());
    }

    private static decimal ResolveRetentionPercent(CommissionSettingsRule rule)
    {
        if (rule.PaymentKind.Equals("AMEX", StringComparison.OrdinalIgnoreCase))
            return rule.AmexRetentionPercent;
        if (rule.PaymentKind.Contains("TARJETA", StringComparison.OrdinalIgnoreCase))
            return rule.CardRetentionPercent;
        return rule.CashRetentionPercent;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task EnsureColumnAsync(SqliteConnection connection, string table, string column, string definition)
    {
        await using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await info.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        await alter.ExecuteNonQueryAsync();
    }

    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static decimal NormalizePercent(decimal value) => value <= 1m && value > 0m ? value * 100m : value;
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);
    private static string Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? string.Empty : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
    private static decimal Decimal(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? 0m : Convert.ToDecimal(reader.GetValue(index), CultureInfo.InvariantCulture);
}
