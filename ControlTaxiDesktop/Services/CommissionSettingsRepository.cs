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
        await InitializeSchemaAsync();
        await using var connection = database.Open();
        await SeedDefaultsAsync(connection);
        await SeedPayoutTariffsAsync(connection);
        await FixKnownWrongRatesAsync(connection);
    }

    public async Task InitializeSchemaAsync()
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
              PayoutAmount REAL NOT NULL DEFAULT 0,
              PaxKind TEXT NOT NULL DEFAULT '',
              MonedaId INTEGER NOT NULL DEFAULT -1,
              Branch TEXT NOT NULL DEFAULT '',
              CvPayoutOneToFourAdults REAL NULL CHECK (CvPayoutOneToFourAdults IS NULL OR CvPayoutOneToFourAdults >= 0),
              CvPayoutFiveOrMoreAdults REAL NULL CHECK (CvPayoutFiveOrMoreAdults IS NULL OR CvPayoutFiveOrMoreAdults >= 0),
              CvTastingPercent REAL NULL CHECK (CvTastingPercent IS NULL OR (CvTastingPercent >= 0 AND CvTastingPercent <= 100)),
              AppliesExpense INTEGER NOT NULL DEFAULT 1,
              Active INTEGER NOT NULL DEFAULT 1,
              EffectiveFrom TEXT NOT NULL,
              EffectiveTo TEXT NULL,
              UpdatedAt TEXT NOT NULL,
              UpdatedBy TEXT NOT NULL DEFAULT '',
              Notes TEXT NOT NULL DEFAULT '',
              UNIQUE(Category, Code, EffectiveFrom, Branch)
            );
            -- Reglas globales de comision (aplican a todos los transportes por igual), a
            -- diferencia de CommissionSettingsRules que va por transporte/forma de pago.
            CREATE TABLE IF NOT EXISTS CommissionGlobalSettings (
              Clave TEXT PRIMARY KEY,
              Valor REAL NOT NULL,
              ActualizadoEn TEXT NOT NULL,
              ActualizadoPor TEXT NOT NULL DEFAULT '',
              Notas TEXT NOT NULL DEFAULT ''
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
        // Tabulador de dejada por transporte. Va como migracion porque las maquinas que ya
        // tienen catalogo no vuelven a ejecutar el CREATE TABLE.
        await EnsureColumnAsync(connection, "CommissionSettingsRules", "PayoutAmount", "REAL NOT NULL DEFAULT 0");
        await EnsureColumnAsync(connection, "CommissionSettingsRules", "PaxKind", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CommissionSettingsRules", "MonedaId", "INTEGER NOT NULL DEFAULT -1");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "Module", "TEXT NOT NULL DEFAULT 'ConfiguracionComisiones'");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "EffectiveFrom", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "EffectiveTo", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "Machine", "TEXT NOT NULL DEFAULT ''");
        await EnsureColumnAsync(connection, "CommissionSettingsAudit", "Branch", "TEXT NOT NULL DEFAULT ''");
        // Before seeding defaults, ensure migration of existing table to include Branch and updated UNIQUE
        await MigrateAddBranchAsync(connection);
        await MigrateCvPayoutBandsAsync(connection);
        await MigrateCvTastingPercentAsync(connection);
    }

    // Explicit import only. No automatic seeding, overwrites or activation of commission percentages.
    public async Task SaveCvPayoutDraftAsync(CommissionSettingsRule rule, string user, string reason)
    {
        if (rule.Branch != "CV" || rule.Category != "TRANSPORTE" || rule.Active || rule.Id != 0
            || rule.CommissionPercent != 0m || rule.CashRetentionPercent != 0m
            || rule.CardRetentionPercent != 0m || rule.AmexRetentionPercent != 0m)
            throw new InvalidOperationException("La carga de dejadas requiere una ficha CV nueva e inactiva, con porcentajes pendientes.");
        ValidateRule(rule, reason);
        await InitializeSchemaAsync();
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        await using var existing = connection.CreateCommand();
        existing.Transaction = transaction;
        existing.CommandText = "SELECT COUNT(*) FROM CommissionSettingsRules WHERE Category='TRANSPORTE' AND Branch='CV' COLLATE NOCASE AND (Code=$code COLLATE NOCASE OR Name=$name COLLATE NOCASE);";
        existing.Parameters.AddWithValue("$code", rule.Code);
        existing.Parameters.AddWithValue("$name", rule.Name);
        if (Convert.ToInt64(await existing.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0)
            throw new InvalidOperationException("Ya existe una regla CV con ese código o nombre. No se sobrescriben reglas ni históricos; hay que seleccionar una vigencia explícitamente.");
        await InsertRuleAsync(connection, transaction, rule, user);
        await WriteAuditAsync(connection, transaction, null, rule, user, reason);
        await transaction.CommitAsync();
    }

    private static async Task MigrateCvPayoutBandsAsync(SqliteConnection connection)
    {
        // Additive, transactional migration: never derive these amounts from P28/legacy rates.
        await using var transaction = connection.BeginTransaction();
        foreach (var column in new[] { "CvPayoutOneToFourAdults", "CvPayoutFiveOrMoreAdults" })
        {
            await using var inspect = connection.CreateCommand();
            inspect.Transaction = transaction;
            inspect.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CommissionSettingsRules') WHERE name=$column;";
            inspect.Parameters.AddWithValue("$column", column);
            if (Convert.ToInt64(await inspect.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0) continue;
            await using var alter = connection.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = $"ALTER TABLE CommissionSettingsRules ADD COLUMN {column} REAL NULL CHECK ({column} IS NULL OR {column} >= 0);";
            await alter.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task MigrateCvTastingPercentAsync(SqliteConnection connection)
    {
        // Additive and nullable: existing rules remain valid and "not configured" stays
        // distinguishable from an explicitly configured 0%.
        await using var transaction = connection.BeginTransaction();
        await using var inspect = connection.CreateCommand();
        inspect.Transaction = transaction;
        inspect.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CommissionSettingsRules') WHERE name='CvTastingPercent';";
        if (Convert.ToInt64(await inspect.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
        {
            await using var alter = connection.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = "ALTER TABLE CommissionSettingsRules ADD COLUMN CvTastingPercent REAL NULL CHECK (CvTastingPercent IS NULL OR (CvTastingPercent >= 0 AND CvTastingPercent <= 100));";
            await alter.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    private static async Task MigrateAddBranchAsync(SqliteConnection connection)
    {
        // Verifica también la unicidad para reparar migraciones parciales.
        await using var info = connection.CreateCommand();
        info.CommandText = "PRAGMA table_info(\"CommissionSettingsRules\");";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await info.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        }
        var hasBranch = columns.Contains("Branch");
        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='CommissionSettingsRules';";
        var tableSql = Convert.ToString(await schema.ExecuteScalarAsync()) ?? string.Empty;
        var normalized = new string(tableSql.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (hasBranch && normalized.Contains("UNIQUE(Category,Code,EffectiveFrom,Branch)", StringComparison.OrdinalIgnoreCase)) return;
        var indexes = new List<string>();
        await using (var indexQuery = connection.CreateCommand())
        {
            indexQuery.CommandText = "SELECT sql FROM sqlite_master WHERE tbl_name='CommissionSettingsRules' AND type IN ('index','trigger') AND sql IS NOT NULL;";
            await using var reader = await indexQuery.ExecuteReaderAsync();
            while (await reader.ReadAsync()) indexes.Add(reader.GetString(0));
        }

        // Create new table with Branch column and UNIQUE including Branch, copy data, preserve indexes
        await using var transaction = connection.BeginTransaction();

        await using var create = connection.CreateCommand();
        create.Transaction = transaction;
        create.CommandText = """
            CREATE TABLE CommissionSettingsRules_new (
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
              PayoutAmount REAL NOT NULL DEFAULT 0,
              PaxKind TEXT NOT NULL DEFAULT '',
              MonedaId INTEGER NOT NULL DEFAULT -1,
              Branch TEXT NOT NULL DEFAULT '',
              CvPayoutOneToFourAdults REAL NULL CHECK (CvPayoutOneToFourAdults IS NULL OR CvPayoutOneToFourAdults >= 0),
              CvPayoutFiveOrMoreAdults REAL NULL CHECK (CvPayoutFiveOrMoreAdults IS NULL OR CvPayoutFiveOrMoreAdults >= 0),
              CvTastingPercent REAL NULL CHECK (CvTastingPercent IS NULL OR (CvTastingPercent >= 0 AND CvTastingPercent <= 100)),
              AppliesExpense INTEGER NOT NULL DEFAULT 1,
              Active INTEGER NOT NULL DEFAULT 1,
              EffectiveFrom TEXT NOT NULL,
              EffectiveTo TEXT NULL,
              UpdatedAt TEXT NOT NULL,
              UpdatedBy TEXT NOT NULL DEFAULT '',
              Notes TEXT NOT NULL DEFAULT '',
              UNIQUE(Category, Code, EffectiveFrom, Branch)
            );
            """;
        await create.ExecuteNonQueryAsync();

        // Copy existing data, setting Branch to empty string for migrated rows.
        await using var copy = connection.CreateCommand();
        copy.Transaction = transaction;
        var branchExpression = hasBranch ? "Branch" : "''";
        var smallPayoutExpression = columns.Contains("CvPayoutOneToFourAdults") ? "CvPayoutOneToFourAdults" : "NULL";
        var largePayoutExpression = columns.Contains("CvPayoutFiveOrMoreAdults") ? "CvPayoutFiveOrMoreAdults" : "NULL";
        var tastingExpression = columns.Contains("CvTastingPercent") ? "CvTastingPercent" : "NULL";
        copy.CommandText = $"""
            INSERT INTO CommissionSettingsRules_new
            (Id,Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,PaymentKind,AppliesPayout,PayoutAmount,PaxKind,MonedaId,Branch,CvPayoutOneToFourAdults,CvPayoutFiveOrMoreAdults,CvTastingPercent,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes)
            SELECT Id,Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,PaymentKind,AppliesPayout,PayoutAmount,PaxKind,MonedaId,{branchExpression},{smallPayoutExpression},{largePayoutExpression},{tastingExpression},AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes
            FROM CommissionSettingsRules;
            """;
        await copy.ExecuteNonQueryAsync();

        await using var drop = connection.CreateCommand();
        drop.Transaction = transaction;
        drop.CommandText = "DROP TABLE CommissionSettingsRules;";
        await drop.ExecuteNonQueryAsync();

        await using var rename = connection.CreateCommand();
        rename.Transaction = transaction;
        rename.CommandText = "ALTER TABLE CommissionSettingsRules_new RENAME TO CommissionSettingsRules;";
        await rename.ExecuteNonQueryAsync();

        foreach (var sql in indexes)
        {
            await using var restore = connection.CreateCommand();
            restore.Transaction = transaction;
            restore.CommandText = sql;
            await restore.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    /// <summary>
    /// Autocorrige, en cada arranque y en cada maquina, catalogo local que quedo mal desde una
    /// siembra vieja: duplicados de TRANSPORTE (misma unidad con dos filas y distinta tasa) y la
    /// tasa de VAN TRANSPORTADORAS que se sembro en 20% cuando debia ser 10%. Antes esto se
    /// arreglaba a mano por maquina con sqlite3; con esto ya no hace falta repetirlo al pasar el
    /// paquete a otro equipo.
    ///
    /// Solo toca filas con UpdatedBy='MIGRACION' (nunca editadas por una persona desde la
    /// pantalla de Configuracion de Comisiones): si alguien ya corrigio la tasa a mano, esta
    /// correccion no la vuelve a tocar. Confirmado con el usuario 2026-08-26.
    /// </summary>
    private static async Task FixKnownWrongRatesAsync(SqliteConnection connection)
    {
        await using var dedupe = connection.CreateCommand();
        dedupe.CommandText = """
            DELETE FROM CommissionSettingsRules
            WHERE Category='TRANSPORTE' AND Branch=''
              AND Id NOT IN (
                SELECT MIN(Id) FROM CommissionSettingsRules
                WHERE Category='TRANSPORTE' AND Branch=''
                GROUP BY UPPER(TRIM(Name)), EffectiveFrom
              );
            """;
        await dedupe.ExecuteNonQueryAsync();

        await using var fix = connection.CreateCommand();
        fix.CommandText = """
            UPDATE CommissionSettingsRules
            SET CommissionPercent = 10, UpdatedAt = $updatedAt
            WHERE Category='TRANSPORTE' AND Branch=''
              AND UPPER(TRIM(Name)) = 'VAN TRANSPORTADORAS'
              AND CommissionPercent = 20
              AND UpdatedBy = 'MIGRACION';
            """;
        fix.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        await fix.ExecuteNonQueryAsync();
    }

    // Added optional sessionBranch parameter at the end for branch-scoped reads. Keep existing signature compatible
    public async Task<IReadOnlyList<CommissionSettingsRule>> GetRulesAsync(string? category = null, string? search = null, bool? active = null, DateTime? date = null, string sessionBranch = "")
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
        var stored = await ReadRulesAsync($"""
            SELECT Id,Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,
                   PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes,PayoutAmount,PaxKind,MonedaId,Branch,CvPayoutOneToFourAdults,CvPayoutFiveOrMoreAdults,CvTastingPercent
            FROM CommissionSettingsRules
            {where}
            ORDER BY Category, Name, EffectiveFrom DESC;
            """, parameters.ToArray());
        // CV usa exclusivamente SQLite; Plaza 28 conserva el catálogo fijo.
        var wantsTransport = string.IsNullOrWhiteSpace(category)
            || string.Equals(category.Trim(), TransportCategory, StringComparison.OrdinalIgnoreCase);
        if (!wantsTransport) return stored;

        var branchIsCv = string.Equals(sessionBranch.Trim(), "CV", StringComparison.OrdinalIgnoreCase);
        var result = stored.Where(x => !string.Equals(x.Category, TransportCategory, StringComparison.OrdinalIgnoreCase));
        return result.Concat(branchIsCv
                ? stored.Where(x => x.Category == TransportCategory && string.Equals(x.Branch, "CV", StringComparison.OrdinalIgnoreCase))
                : FilterFixedTransportRules(search, active, date))
            .OrderBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenByDescending(x => x.EffectiveFrom)
            .ToArray();
    }

    /// <summary>
    /// Categoria cuyas reglas viven en el programa y no en la base local.
    /// </summary>
    private const string TransportCategory = "TRANSPORTE";

    /// <summary>
    /// Aplica al catalogo fijo los mismos filtros que la consulta hace en la base, para que la
    /// pantalla se comporte igual que antes (buscador, activos/inactivos y vigentes al dia).
    /// </summary>
    private static IEnumerable<CommissionSettingsRule> FilterFixedTransportRules(string? search, bool? active, DateTime? date)
    {
        var rules = HardcodedTransportCatalog.AsRules().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim();
            rules = rules.Where(x =>
                x.Code.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || x.Category.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        if (active is not null)
        {
            rules = rules.Where(x => x.Active == active.Value);
        }

        if (date is not null)
        {
            var day = date.Value.Date;
            rules = rules.Where(x => x.EffectiveFrom.Date <= day
                                     && (x.EffectiveTo is null || x.EffectiveTo.Value.Date >= day));
        }

        return rules;
    }

    /// <summary>
    /// El catalogo de transportes es fijo: se cambia en el programa y se reinstala, no maquina por
    /// maquina. Permitir editarlo aqui devolveria el problema que resolvio: cada equipo con su
    /// propia tarifa y el mismo folio dando cifras distintas segun desde donde se mire.
    /// </summary>
    private static void EnsureCategoryIsEditable(CommissionSettingsRule rule)
    {
        if (string.Equals(rule.Category?.Trim(), TransportCategory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Las comisiones por transporte son del catálogo fijo del sistema y son iguales en "
                + "todas las máquinas, así que no se editan desde aquí. Para cambiar una tarifa hay "
                + "que actualizarla en el sistema y reinstalar.");
        }
    }

    public async Task<IReadOnlyList<CommissionPaymentConfiguration>> GetActivePaymentConfigurationsAsync(DateTime? date = null)
    {
        var rules = await GetRulesAsync("FORMA_PAGO", active: true, date: date ?? DateTime.Today);
        return rules
            .Select(x => new CommissionPaymentConfiguration(x.Code, x.Name, x.PaymentKind, ResolveRetentionPercent(x), x.Active, x.MonedaId))
            .ToArray();
    }

    /// <summary>
    /// Clave de la regla global "venta minima para descontar la dejada".
    /// </summary>
    public const string PayoutDeductionMinSaleKey = "UMBRAL_DESCUENTO_DEJADA";

    /// <summary>
    /// Lee una regla global. Si no esta configurada, devuelve el valor por omision recibido
    /// (no se inserta nada: la fila se crea solo cuando alguien la configura de verdad).
    /// </summary>
    public async Task<decimal> GetGlobalSettingAsync(string clave, decimal valorPorOmision)
    {
        await InitializeAsync();
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Valor FROM CommissionGlobalSettings WHERE Clave=$clave;";
        command.Parameters.AddWithValue("$clave", clave);
        var result = await command.ExecuteScalarAsync();
        if (result is null || result is DBNull) return valorPorOmision;
        return Convert.ToDecimal(result, CultureInfo.InvariantCulture);
    }

    public async Task SetGlobalSettingAsync(string clave, decimal valor, string usuario, string notas = "")
    {
        await InitializeAsync();
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CommissionGlobalSettings (Clave, Valor, ActualizadoEn, ActualizadoPor, Notas)
            VALUES ($clave, $valor, $fecha, $usuario, $notas)
            ON CONFLICT(Clave) DO UPDATE SET
              Valor = excluded.Valor,
              ActualizadoEn = excluded.ActualizadoEn,
              ActualizadoPor = excluded.ActualizadoPor,
              Notas = excluded.Notas;
            """;
        command.Parameters.AddWithValue("$clave", clave);
        command.Parameters.AddWithValue("$valor", (double)valor);
        command.Parameters.AddWithValue("$fecha", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$usuario", usuario ?? string.Empty);
        command.Parameters.AddWithValue("$notas", notas ?? string.Empty);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<CommissionSettingsSummary> GetSummaryAsync(string sessionBranch = "")
    {
        var rules = await GetRulesAsync(sessionBranch: sessionBranch);
        await using var connection = database.Open();
        var recent = await ScalarAsync(connection, "SELECT COUNT(*) FROM CommissionSettingsAudit WHERE Date >= datetime('now', '-7 day');");
        return new CommissionSettingsSummary(rules.Count(x => x.Active), rules.Count(x => x.Category == TransportCategory),
            rules.Count(x => x.Category == "FORMA_PAGO"),
            rules.Count(x => x.Active && x.EffectiveTo >= DateTime.Today && x.EffectiveTo <= DateTime.Today.AddDays(30)), (int)recent);
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
        // Only allow editing/creating TRANSPORTE from CV branch. If Branch not provided, treat as global edit disallowed.
        if (string.Equals(rule.Category?.Trim(), TransportCategory, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(rule.Branch, "CV", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Solo es posible crear o editar reglas de TRANSPORTE desde la sucursal CV.");
        }
        else
        {
            EnsureCategoryIsEditable(rule);
        }
        ValidateRule(rule, reason);
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var current = rule.Id > 0 ? await GetRuleByIdAsync(connection, transaction, rule.Id) : null;
        if (rule.Id > 0 && (current is null || !string.Equals(current.Branch, rule.Branch, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("La regla no existe en la sucursal indicada.");
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

    /// <summary>
    /// Corrige la regla que ya existe, sobre el mismo Id, sin abrir una vigencia nueva.
    ///
    /// SaveRuleAsync versiona: cierra la regla anterior e inserta otra, y por eso exige que la
    /// nueva vigencia empiece despues. Eso sirve para "de hoy en adelante cobramos otro
    /// porcentaje", pero no para corregir un dato mal capturado: ahi el operador quiere arreglar
    /// ESA regla, no dejar dos. El historial no se pierde: el cambio se sigue escribiendo campo
    /// por campo en CommissionSettingsAudit.
    /// </summary>
    public async Task UpdateRuleAsync(CommissionSettingsRule rule, string user, string reason, bool canEdit = true)
    {
        await InitializeAsync();
        if (!canEdit)
            throw new UnauthorizedAccessException("El usuario no tiene permiso para editar configuración de comisiones.");
        // Only allow editing TRANSPORTE from CV branch
        if (string.Equals(rule.Category?.Trim(), TransportCategory, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(rule.Branch, "CV", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Solo es posible crear o editar reglas de TRANSPORTE desde la sucursal CV.");
        }
        else
        {
            EnsureCategoryIsEditable(rule);
        }
        if (rule.Id <= 0)
            throw new InvalidOperationException("Solo se puede corregir una regla que ya existe. Usa Nueva regla para dar de alta.");
        ValidateRule(rule, reason);

        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        var current = await GetRuleByIdAsync(connection, transaction, rule.Id)
            ?? throw new InvalidOperationException("No se encontro la regla seleccionada.");
        if (!string.Equals(current.Branch, rule.Branch, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("No se puede cambiar la sucursal de una regla existente.");
        // La consulta de traslape ya excluye la propia regla (Id <> $id).
        await EnsureNoOverlappingRuleAsync(connection, transaction, rule);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE CommissionSettingsRules
            SET Category=$category,
                Code=$code,
                Name=$name,
                CommissionPercent=$commission,
                CashRetentionPercent=$cash,
                CardRetentionPercent=$card,
                AmexRetentionPercent=$amex,
                PaymentKind=$paymentKind,
                AppliesPayout=$payout,
                AppliesExpense=$expense,
                Active=$active,
                EffectiveFrom=$from,
                EffectiveTo=$to,
                UpdatedAt=$updatedAt,
                UpdatedBy=$updatedBy,
                Notes=$notes,
                PayoutAmount=$payoutAmount,
                PaxKind=$paxKind,
                MonedaId=$monedaId, Branch=$branch,
                CvPayoutOneToFourAdults=$cvPayoutSmall, CvPayoutFiveOrMoreAdults=$cvPayoutLarge,
                CvTastingPercent=$cvTasting
            WHERE Id=$id;
            """;
        AddRuleParameters(command, rule, user);
        command.Parameters.AddWithValue("$id", rule.Id);
        await command.ExecuteNonQueryAsync();

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

    public async Task<CommissionSimulationResult> SimulateAsync(CommissionSimulationInput input, string sessionBranch = "")
    {
        await InitializeAsync();
        return await new CommissionConfigurationResolver(this, sessionBranch).SimulateAsync(input);
    }

    public async Task ExportCatalogCsvAsync(string path, string sessionBranch = "")
    {
        var rows = await GetRulesAsync(sessionBranch: sessionBranch);
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

    public async Task ExportCatalogExcelAsync(string path, string sessionBranch = "")
    {
        var rows = await GetRulesAsync(sessionBranch: sessionBranch);
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
             PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes,PayoutAmount,PaxKind,MonedaId,Branch,CvPayoutOneToFourAdults,CvPayoutFiveOrMoreAdults,CvTastingPercent)
            VALUES
            ($category,$code,$name,$commission,$cash,$card,$amex,$paymentKind,$payout,$expense,$active,$from,$to,$updatedAt,$updatedBy,$notes,$payoutAmount,$paxKind,$monedaId,$branch,$cvPayoutSmall,$cvPayoutLarge,$cvTasting);
            """;
        AddRuleParameters(insert, rule, user);
        await insert.ExecuteNonQueryAsync();
    }


    /// <summary>
    /// Siembra el tabulador de dejada de Plaza 28 ("TARIFA PLAZA 28") sobre el catalogo.
    ///
    /// Antes la dejada no salia de ninguna tarifa: era el importe que el operador tecleaba en la
    /// app movil, y por eso la misma combinacion aparecia con tres importes distintos (VAN VERDE
    /// con extranjeros salio $350, $450 y $250). Con el tabulador en el catalogo el calculo deja
    /// de depender de la captura.
    ///
    /// Es idempotente y NO pisa lo capturado a mano: si la regla ya tiene un importe distinto de
    /// cero se respeta. Corre en cada arranque para que las maquinas nuevas queden completas.
    /// </summary>

    /// <summary>
    /// Da de alta en el catalogo las monedas del punto de venta (dbo.Monedas) que todavia no
    /// estan, con una retencion sugerida. NO pisa lo que ya se configuro a mano.
    ///
    /// La forma de pago real del ticket es el NUMERO de moneda, no un texto: por eso la regla se
    /// amarra a MonedaId. Antes se adivinaba buscando "TARJ"/"BBVA" dentro de la descripcion y
    /// "T.CREDITO DLS" caia como efectivo, perdonandole la retencion del 19%.
    /// </summary>
    public async Task<int> SyncMonedasAsync(IReadOnlyList<(int Id, string Nombre)> monedas)
    {
        if (monedas.Count == 0) return 0;
        await InitializeAsync();

        await using var connection = database.Open();
        await connection.OpenAsync();

        var existentes = new HashSet<int>();
        await using (var lectura = connection.CreateCommand())
        {
            lectura.CommandText = "SELECT MonedaId FROM CommissionSettingsRules WHERE MonedaId >= 0;";
            await using var reader = await lectura.ExecuteReaderAsync();
            while (await reader.ReadAsync()) existentes.Add(reader.GetInt32(0));
        }

        var altas = 0;
        foreach (var moneda in monedas)
        {
            if (moneda.Id < 0 || existentes.Contains(moneda.Id)) continue;

            var retencion = SugerirRetencion(moneda.Nombre);
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO CommissionSettingsRules
                (Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,
                 PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes,PayoutAmount,PaxKind,MonedaId)
                VALUES ($code,$code,$name,0,$efectivo,$tarjeta,$amex,$kind,1,1,1,$from,NULL,$updatedAt,'MONEDAS',
                        'Alta automatica desde dbo.Monedas del punto de venta.',0,'',$id);
                """;
            insert.Parameters.AddWithValue("$code", "MONEDA " + moneda.Id.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(moneda.Nombre) ? "MONEDA " + moneda.Id : moneda.Nombre.Trim());
            insert.Parameters.AddWithValue("$efectivo", retencion.Kind == "EFECTIVO" ? retencion.Percent : 0m);
            insert.Parameters.AddWithValue("$tarjeta", retencion.Kind == "TARJETA_NORMAL" ? retencion.Percent : 0m);
            insert.Parameters.AddWithValue("$amex", retencion.Kind == "AMEX" ? retencion.Percent : 0m);
            insert.Parameters.AddWithValue("$kind", retencion.Kind);
            insert.Parameters.AddWithValue("$from", DefaultStart);
            insert.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$id", moneda.Id);
            await insert.ExecuteNonQueryAsync();
            altas++;
        }

        return altas;
    }

    /// <summary>
    /// Retencion sugerida al dar de alta una moneda nueva. Es solo un punto de partida: quien
    /// opera la ajusta en la pantalla de reglas. Criterio confirmado con el usuario el
    /// 2026-08-21: divisas y vales sin retencion, tarjetas y transferencia 19%, AMEX 24%.
    /// </summary>
    private static (string Kind, decimal Percent) SugerirRetencion(string? nombre)
    {
        var texto = new string((nombre ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (texto.Contains("AMEX", StringComparison.Ordinal)) return ("AMEX", 24m);
        if (texto.Contains("TARJETA", StringComparison.Ordinal)
            || texto.Contains("CREDITO", StringComparison.Ordinal)
            || texto.Contains("DEBITO", StringComparison.Ordinal)
            || texto.Contains("TRANSFERENCIA", StringComparison.Ordinal)
            || texto.Contains("TC", StringComparison.Ordinal)
            || texto.Contains("MIFEL", StringComparison.Ordinal)
            || texto.Contains("MERCADOPAGO", StringComparison.Ordinal))
            return ("TARJETA_NORMAL", 19m);
        return ("EFECTIVO", 0m);
    }

    private static async Task SeedPayoutTariffsAsync(SqliteConnection connection)
    {
        // Nombre para dar de alta, tipo de pax, importe, y los nombres con los que puede estar
        // ya guardada la regla en esta maquina.
        (string Nombre, string PaxKind, decimal Dejada, string[] Alias)[] tarifas =
        [
            ("VAN ROJO", "", 300m, ["VANROJO"]),
            ("TAXI ROJO", "", 300m, ["TAXIROJO"]),
            ("TAXI AZUL", "", 250m, ["TAXIAZUL"]),
            ("VAN AZUL", "", 350m, ["VANAZUL"]),
            ("TAXI CAFE", "", 250m, ["TAXICAFE"]),
            ("VAN CAFE", "", 350m, ["VANCAFE"]),
            // Las unicas dos unidades cuyo tabulador cambia segun quien llegue.
            ("TAXI VERDE EXTRANJERO", "EXTRANJEROS", 350m, ["TAXIVERDEEXTRANJERO", "TAXIVERDEEXTRANJEROS", "TAXIVERDEGABACHO", "TAXIVERDEGABACHOS"]),
            ("TAXI VERDE NACIONAL", "NACIONALES", 250m, ["TAXIVERDENACIONAL", "TAXIVERDENACIONALES"]),
            ("VAN VERDE EXTRANJERO", "EXTRANJEROS", 450m, ["VANVERDEEXTRANJERO", "VANVERDEEXTRANJEROS", "VANVERDEGABACHO", "VANVERDEGABACHOS"]),
            ("VAN VERDE NACIONAL", "NACIONALES", 350m, ["VANVERDENACIONAL", "VANVERDENACIONALES"]),
            ("TURIBUS SALMORAN", "", 200m, ["TURIBUSSALMORAN", "SALMORAN"]),
            ("TURIBUS ADO", "", 100m, ["TURIBUSADO"]),
            ("TRANSPORTADORAS", "", 200m, ["TRANSPORTADORAS", "TRANSPORTADORA"]),
            ("TRAVEL EXPERIENCE", "", 200m, ["TRAVELEXPERIENCE"]),
            ("TULAKA", "", 200m, ["TULAKA"]),
            ("UBER", "", 200m, ["UBER"]),
            ("UBER ALIANZA", "", 250m, ["UBERALIANZA", "ALIANZA"]),
            ("GUIAS CALLE", "", 50m, ["GUIASCALLE", "GUIACALLE", "GUIAS"]),
            // MAJESTIC va con $0 en el tabulador. Se deja SIN sembrar a proposito: en este
            // catalogo un importe en cero significa "no capturado, usa lo de la operacion", y la
            // operacion ya registra 0 para Majestic. Sembrarlo no cambiaria nada.
        ];

        var existentes = new List<(long Id, string Nombre, decimal Dejada)>();
        await using (var lectura = connection.CreateCommand())
        {
            lectura.CommandText = "SELECT Id, Name, PayoutAmount FROM CommissionSettingsRules WHERE Category = 'TRANSPORTE' AND Branch='';";
            await using var reader = await lectura.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existentes.Add((reader.GetInt64(0), Text(reader, 1), Decimal(reader, 2)));
        }

        foreach (var tarifa in tarifas)
        {
            var existente = existentes.FirstOrDefault(x => tarifa.Alias.Contains(SoloLetrasYNumeros(x.Nombre), StringComparer.Ordinal));
            if (existente.Id > 0)
            {
                // Ya capturada a mano: mandan las manos, no la siembra.
                if (existente.Dejada > 0m) continue;

                await using var update = connection.CreateCommand();
                update.CommandText = "UPDATE CommissionSettingsRules SET PayoutAmount = $dejada, PaxKind = $pax WHERE Id = $id;";
                update.Parameters.AddWithValue("$dejada", tarifa.Dejada);
                update.Parameters.AddWithValue("$pax", tarifa.PaxKind);
                update.Parameters.AddWithValue("$id", existente.Id);
                await update.ExecuteNonQueryAsync();
                continue;
            }

            // Unidad que no estaba en el catalogo. Los porcentajes van con los valores por
            // omision del sistema (10% de comision, 19% tarjeta, 24% AMEX); lo que aporta la
            // siembra es la tarifa de dejada, no las tasas.
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT OR IGNORE INTO CommissionSettingsRules
                (Category,Code,Name,CommissionPercent,CashRetentionPercent,CardRetentionPercent,AmexRetentionPercent,
                 PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes,PayoutAmount,PaxKind,MonedaId)
                VALUES ($code,$code,$name,10,0,19,24,'',1,1,1,$from,NULL,$updatedAt,'TABULADOR',
                        'Sembrado del tabulador TARIFA PLAZA 28.',$dejada,$pax,-1);
                """;
            insert.Parameters.AddWithValue("$code", NormalizeCode(tarifa.Nombre));
            insert.Parameters.AddWithValue("$name", tarifa.Nombre);
            insert.Parameters.AddWithValue("$from", DefaultStart);
            insert.Parameters.AddWithValue("$updatedAt", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$dejada", tarifa.Dejada);
            insert.Parameters.AddWithValue("$pax", tarifa.PaxKind);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static string SoloLetrasYNumeros(string? value) =>
        new(( value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static async Task SeedPaymentsAsync(SqliteConnection connection)
    {
        await InsertSeedAsync(connection, "FORMA_PAGO", "PESOS", "Pesos / efectivo", 0m, 0m, 0m, 0m, "EFECTIVO", "Efectivo sin retención.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "EFECTIVO", "Efectivo", 0m, 0m, 0m, 0m, "EFECTIVO", "Efectivo sin retención.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "MERCADO PAGO", "Mercado Pago", 0m, 0m, 19m, 0m, "TARJETA_NORMAL", "Clasificación inicial validada.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "MIFEL", "MIFEL", 0m, 0m, 19m, 0m, "TARJETA_NORMAL", "Clasificación inicial validada.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "TARJETA", "Tarjeta normal", 0m, 0m, 19m, 0m, "TARJETA_NORMAL", "Retención general de tarjeta.");
        await InsertSeedAsync(connection, "FORMA_PAGO", "AMEX", "AMEX", 0m, 0m, 0m, 24m, "AMEX", "Retención especial AMEX. AMEXCO es lo mismo, no se siembra por separado (confirmado 2026-08-19).");
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
        // Estas semillas existen para cubrir unidades que podrian no venir en las tablas
        // importadas. Van con guarda por NOMBRE: si el transporte ya se sembro desde una tabla
        // (aunque sea con otra clave), no se vuelve a insertar.
        //
        // Sin la guarda, cada arranque agregaba una segunda fila del mismo transporte con
        // clave distinta, y al resolver la comision se tomaba la primera coincidencia: podia
        // aplicarse una retencion que no correspondia.
        await InsertTransportSeedIfNameFreeAsync(connection, "TURIBUS ADO", "TURIBUS ADO", 10m, 0m, 19m, 24m, "Semilla segura si el catálogo importado aún no existe.");
        await InsertTransportSeedIfNameFreeAsync(connection, "TURIBUS SALMORAN", "TURIBUS SALMORAN", 20m, 16m, 19m, 24m, "Semilla segura validada para SALMORAN.");
        await InsertTransportSeedIfNameFreeAsync(connection, "MAJESTIC", "MAJESTIC EXPEDITIONS", 8m, 0m, 19m, 24m, "Semilla inicial para regla tipo guía.");
        // Unidades de la tarifa oficial Plaza 28 sin fila propia en las tablas importadas
        // (confirmado por Brandon 2026-08-19).
        await InsertTransportSeedIfNameFreeAsync(connection, "TAXICAFE", "TAXI CAFE", 10m, 0m, 19m, 24m, "Unidad de la tarifa Plaza 28 (Puerto Morelos).");
        await InsertTransportSeedIfNameFreeAsync(connection, "VANCAFE", "VAN CAFE", 10m, 0m, 19m, 24m, "Unidad de la tarifa Plaza 28 (Puerto Morelos).");
        await InsertTransportSeedIfNameFreeAsync(connection, "TAXIAZUL", "TAXI AZUL", 10m, 0m, 19m, 24m, "Unidad de la tarifa Plaza 28 (Playa del Carmen).");
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
            var rawCode = Text(reader, 0);
            if (ExcludedTransportCodes.Contains(rawCode.Trim()))
                continue;
            var code = CanonicalTransportCode(rawCode);
            var name = Text(reader, 1);

            // El INSERT OR IGNORE de InsertSeedAsync deduplica por la restriccion
            // UNIQUE(Category, Code, EffectiveFrom), es decir por CLAVE. Cuando las tablas
            // importadas traen el mismo transporte con dos claves distintas (por ejemplo
            // "GUIAS" o "TAXI PLAYA GABACHO"), pasaban las dos filas y el catalogo mostraba
            // el transporte duplicado, a veces con retenciones distintas.
            // Aqui se corta por NOMBRE: la primera fila de ese transporte gana y las demas
            // se ignoran. Las semillas fijas de mas abajo no pasan por este filtro, para no
            // perder los casos historicos que si deben convivir (MAJESTIC 8% y 20%).
            if (await TransportNameAlreadySeededAsync(connection, name))
                continue;

            await InsertSeedAsync(connection, "TRANSPORTE", string.IsNullOrWhiteSpace(code) ? name : code, name, Decimal(reader, 2), Decimal(reader, 3), Decimal(reader, 4), Decimal(reader, 5), "", "Migrado desde " + table + ".");
        }
    }

    // Las tablas importadas (mkt__dbo__transporte) traen claves cortas para algunas unidades
    // que ya tienen una semilla fija con clave descriptiva mas abajo (ver SeedTransportsAsync).
    // Sin este mapeo, cada arranque siembra las dos claves como si fueran unidades distintas
    // y el catalogo se vuelve a duplicar solo. Confirmado por el usuario 2026-08-19.
    private static readonly Dictionary<string, string> TransportCodeCanonicalMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TURIBUS"] = "TURIBUS ADO",
        ["SALMORAN"] = "TURIBUS SALMORAN",
        // Taxi/Van Verde y Van Azul se unificaron (la comision no varia por nacional/extranjero
        // ni gabacho/pocho, esa distincion solo aplica a la dejada por zona). Confirmado 2026-08-19.
        ["TAXIV"] = "TAXIVERDE",
        ["TAXIVG"] = "TAXIVERDE",
        ["VANN"] = "VANVERDE",
        ["VANE"] = "VANVERDE",
        ["VANAG"] = "VANAZUL",
        ["VANAP"] = "VANAZUL",
    };

    // Unidades que ya no forman parte del catalogo autorizado (confirmado por Brandon 2026-08-19
    // contra la tarifa oficial de Plaza 28). Se excluyen de la siembra para que una instalacion
    // nueva no las vuelva a traer desde las tablas importadas.
    private static readonly HashSet<string> ExcludedTransportCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MAYAN", "MKT:MAYAN",
        "7 TOUR", "MKT:7 TOUR",
        "TRANS", "MKT:TRANS",
        "TAXI", "MKT:TAXI",
        "VAN", "MKT:VAN",
    };

    private static string CanonicalTransportCode(string code)
    {
        var trimmed = code.Trim();
        return TransportCodeCanonicalMap.TryGetValue(trimmed, out var canonical) ? canonical : trimmed;
    }

    private static async Task SeedConceptAsync(SqliteConnection connection, string category, string code, string name, decimal commission, string notes) =>
        await InsertSeedAsync(connection, category, code, name, commission, 0m, 19m, 24m, "", notes);

    /// <summary>
    /// Quita del catalogo los transportes repetidos: deja UNA fila por nombre y vigencia, la
    /// mas antigua, y borra las demas.
    ///
    /// Hace falta porque el anti-duplicados original se apoya en UNIQUE(Category, Code,
    /// EffectiveFrom), o sea que deduplica por CLAVE. Si las tablas importadas traen el mismo
    /// transporte con dos claves distintas, entraban las dos filas. Al resolver la comision se
    /// toma la PRIMERA coincidencia, asi que un duplicado con retenciones distintas puede
    /// hacer que se aplique la tasa equivocada.
    ///
    /// Respeta las versiones historicas: dos filas del mismo transporte con vigencias
    /// diferentes (por ejemplo MAJESTIC 8% y 20%) NO se tocan.
    /// </summary>
    /// <summary>
    /// Nombres de transporte que aparecen mas de una vez con la misma vigencia.
    /// Sirve para verificar la limpieza sin depender de herramientas externas.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetDuplicateTransportNamesAsync()
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT UPPER(TRIM(Name)) || '  (x' || COUNT(*) || ')'
            FROM CommissionSettingsRules
            WHERE Category='TRANSPORTE' AND Branch=''
            GROUP BY UPPER(TRIM(Name)), EffectiveFrom
            HAVING COUNT(*) > 1
            ORDER BY 1;
            """;
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }

    /// <returns>Cuantas filas se eliminaron.</returns>
    public async Task<int> RemoveDuplicateTransportsAsync()
    {
        await InitializeAsync();
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM CommissionSettingsRules
            WHERE Category='TRANSPORTE' AND Branch=''
              AND Id NOT IN (
                SELECT MIN(Id) FROM CommissionSettingsRules
                WHERE Category='TRANSPORTE' AND Branch=''
                GROUP BY UPPER(TRIM(Name)), EffectiveFrom
              );
            """;
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Indica si ya existe una regla de TRANSPORTE con ese nombre (ignorando mayusculas y
    /// espacios). Se usa para que las tablas importadas no siembren el mismo transporte dos
    /// veces cuando viene con claves distintas.
    /// </summary>
    private static async Task<bool> TransportNameAlreadySeededAsync(SqliteConnection connection, string name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) return false;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1 FROM CommissionSettingsRules
            WHERE Category='TRANSPORTE' AND Branch='' AND UPPER(TRIM(Name))=UPPER($name)
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", normalized);
        return await command.ExecuteScalarAsync() is not null;
    }

    /// <summary>
    /// Siembra un transporte solo si su nombre no esta ya en el catalogo. Es la version con
    /// guarda de InsertSeedAsync para las semillas fijas.
    /// </summary>
    private static async Task InsertTransportSeedIfNameFreeAsync(SqliteConnection connection, string code, string name, decimal commission, decimal cash, decimal card, decimal amex, string notes)
    {
        if (await TransportNameAlreadySeededAsync(connection, name)) return;
        await InsertSeedAsync(connection, "TRANSPORTE", code, name, commission, cash, card, amex, "", notes);
    }

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
        Text(reader, 16),
        reader.FieldCount > 17 ? Decimal(reader, 17) : 0m,
        reader.FieldCount > 18 ? Text(reader, 18) : string.Empty,
        reader.FieldCount > 19 && !reader.IsDBNull(19) ? Convert.ToInt32(reader.GetValue(19), CultureInfo.InvariantCulture) : -1)
        {
            Branch = reader.FieldCount > 20 ? Text(reader, 20) : string.Empty,
            CvPayoutOneToFourAdults = reader.FieldCount > 21 && !reader.IsDBNull(21) ? Decimal(reader, 21) : null,
            CvPayoutFiveOrMoreAdults = reader.FieldCount > 22 && !reader.IsDBNull(22) ? Decimal(reader, 22) : null,
            CvTastingPercent = reader.FieldCount > 23 && !reader.IsDBNull(23) ? Decimal(reader, 23) : null
        };

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
        if (rule.CvPayoutOneToFourAdults < 0m || rule.CvPayoutFiveOrMoreAdults < 0m)
            throw new InvalidOperationException("Las dejadas CV no pueden ser negativas.");
        if (rule.CvTastingPercent is < 0m or > 100m)
            throw new InvalidOperationException("Degustación debe estar entre 0 y 100 por ciento, o quedar sin configurar.");
        if ((rule.CvPayoutOneToFourAdults.HasValue || rule.CvPayoutFiveOrMoreAdults.HasValue)
            && !(rule.Category.Equals("TRANSPORTE", StringComparison.OrdinalIgnoreCase) && rule.Branch.Equals("CV", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Las dejadas por adultos solo corresponden a TRANSPORTE de CV.");
        if (rule.CvTastingPercent.HasValue
            && !(rule.Category.Equals("TRANSPORTE", StringComparison.OrdinalIgnoreCase) && rule.Branch.Equals("CV", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Degustación solo corresponde a TRANSPORTE de CV.");
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
              AND Code=$code AND Branch=$branch COLLATE NOCASE
              AND Active=1
              AND $active=1
              AND EffectiveFrom <= COALESCE($to, '9999-12-31')
              AND COALESCE(EffectiveTo, '9999-12-31') >= $from;
            """;
        command.Parameters.AddWithValue("$id", rule.Id);
        command.Parameters.AddWithValue("$category", rule.Category.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$branch", rule.Branch.Trim().ToUpperInvariant());
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
                   PaymentKind,AppliesPayout,AppliesExpense,Active,EffectiveFrom,EffectiveTo,UpdatedAt,UpdatedBy,Notes,PayoutAmount,PaxKind,MonedaId,Branch,CvPayoutOneToFourAdults,CvPayoutFiveOrMoreAdults,CvTastingPercent
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
            // La sucursal auditada pertenece a la regla, no al entorno del proceso.
            var branchValue = current.Branch;
            command.Parameters.AddWithValue("$branch", branchValue);
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
        $"{rule.Category}|{rule.Code}|{rule.Name}|C={rule.CommissionPercent:0.####}|E={rule.CashRetentionPercent:0.####}|T={rule.CardRetentionPercent:0.####}|A={rule.AmexRetentionPercent:0.####}|Pago={rule.PaymentKind}|Vig={rule.EffectiveRange}|Activo={rule.Active}|Dejada={rule.PayoutAmount:0.##}|Pax={rule.PaxKind}"
        + (rule.Branch == "CV" ? $"|Dejada1a4={rule.CvPayoutOneToFourAdults?.ToString("0.##", CultureInfo.InvariantCulture) ?? "PENDIENTE"}|Dejada5mas={rule.CvPayoutFiveOrMoreAdults?.ToString("0.##", CultureInfo.InvariantCulture) ?? "PENDIENTE"}|Degustacion={rule.CvTastingPercent?.ToString("0.####", CultureInfo.InvariantCulture) ?? "PENDIENTE"}" : string.Empty);

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
        command.Parameters.AddWithValue("$payoutAmount", Math.Max(0m, rule.PayoutAmount));
        command.Parameters.AddWithValue("$cvPayoutSmall", (object?)rule.CvPayoutOneToFourAdults ?? DBNull.Value);
        command.Parameters.AddWithValue("$cvPayoutLarge", (object?)rule.CvPayoutFiveOrMoreAdults ?? DBNull.Value);
        command.Parameters.AddWithValue("$cvTasting", (object?)rule.CvTastingPercent ?? DBNull.Value);
        command.Parameters.AddWithValue("$paxKind", rule.PaxKind.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("$monedaId", rule.MonedaId);
        command.Parameters.AddWithValue("$expense", rule.AppliesExpense ? 1 : 0);
        command.Parameters.AddWithValue("$branch", rule.Branch.Trim().ToUpperInvariant());
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
