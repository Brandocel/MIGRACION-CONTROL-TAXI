using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed class LocalSqlServerSource
{
    private readonly string _defaultConnection;

    private LocalSqlServerSource(string defaultConnection, string posDatabase, string appDatabase, string compuadmoDatabase, string joyeriaDatabase, string posSchema, string appSchema, string storeSchema)
    {
        _defaultConnection = defaultConnection;
        PosDatabase = posDatabase;
        AppDatabase = appDatabase;
        CompuadmoDatabase = compuadmoDatabase;
        JoyeriaDatabase = joyeriaDatabase;
        PosSchema = string.IsNullOrWhiteSpace(posSchema) ? "dbo" : posSchema.Trim();
        AppSchema = string.IsNullOrWhiteSpace(appSchema) ? "dbo" : appSchema.Trim();
        StoreSchema = string.IsNullOrWhiteSpace(storeSchema) ? "dbo" : storeSchema.Trim();
    }

    public string PosDatabase { get; }
    public string AppDatabase { get; }
    public string CompuadmoDatabase { get; }
    public string JoyeriaDatabase { get; }
    public string PosSchema { get; }
    public string AppSchema { get; }
    public string StoreSchema { get; }

    public static LocalSqlServerSource? TryLoad()
    {
        var plaza28Production = TryLoadPlaza28Production();
        if (plaza28Production is not null)
        {
            return plaza28Production;
        }

        var root = FindWorkspaceRoot();
        if (root is null) return null;

        var appSettings = Path.Combine(root, "Config", "appsettings.json");
        var devSettings = Path.Combine(root, "Config", "appsettings.Development.json");
        if (!File.Exists(appSettings)) return null;

        using var baseDocument = JsonDocument.Parse(File.ReadAllText(appSettings));
        JsonDocument? devDocument = File.Exists(devSettings) ? JsonDocument.Parse(File.ReadAllText(devSettings)) : null;

        string? ReadString(string section, string key)
        {
            if (devDocument is not null
                && devDocument.RootElement.TryGetProperty(section, out var devSection)
                && devSection.ValueKind == JsonValueKind.Object
                && devSection.TryGetProperty(key, out var devValue)
                && devValue.ValueKind == JsonValueKind.String)
            {
                return devValue.GetString();
            }

            if (baseDocument.RootElement.TryGetProperty(section, out var baseSection)
                && baseSection.ValueKind == JsonValueKind.Object
                && baseSection.TryGetProperty(key, out var baseValue)
                && baseValue.ValueKind == JsonValueKind.String)
            {
                return baseValue.GetString();
            }

            return null;
        }

        var defaultConnection = ReadString("ConnectionStrings", "DefaultConnection");
        var posDatabase = ReadString("DatabaseNames", "Pos");
        var appDatabase = ReadString("DatabaseNames", "App");
        var compuadmoDatabase = ReadString("DatabaseNames", "Compuadmo");
        var joyeriaDatabase = ReadString("DatabaseNames", "Joyeria");
        var posSchema = ReadString("DatabaseSchemas", "Pos");
        var appSchema = ReadString("DatabaseSchemas", "App");
        var storeSchema = ReadString("DatabaseSchemas", "Store");

        if (string.IsNullOrWhiteSpace(defaultConnection)
            || string.IsNullOrWhiteSpace(posDatabase)
            || string.IsNullOrWhiteSpace(appDatabase)
            || string.IsNullOrWhiteSpace(compuadmoDatabase)
            || string.IsNullOrWhiteSpace(joyeriaDatabase))
        {
            return null;
        }

        return new LocalSqlServerSource(defaultConnection, posDatabase, appDatabase, compuadmoDatabase, joyeriaDatabase, posSchema ?? "dbo", appSchema ?? "dbo", storeSchema ?? "dbo");
    }

    private static LocalSqlServerSource? TryLoadPlaza28Production()
    {
        var productionRoot = FindProductionRoot();
        if (productionRoot is null)
        {
            return null;
        }

        var rootMarker = productionRoot.Replace('\\', '/').ToUpperInvariant();
        if (!rootMarker.Contains("PLAZA28") && !rootMarker.Contains("PLAZA 28") && !rootMarker.Contains("DESKTOP TAXIS"))
        {
            return null;
        }

        _ = Plaza28CredentialStore.TryApplyToEnvironment(out _);

        var branchService = new BranchConfigurationService();
        var branch = branchService.GetBranch("P28");
        var sqlServer = Environment.GetEnvironmentVariable("PLAZA28_SQL_SERVER");
        var sqlUser = Environment.GetEnvironmentVariable("PLAZA28_SQL_USER");
        var sqlPassword = Environment.GetEnvironmentVariable("PLAZA28_SQL_PASSWORD");
        var sqlDatabase = Environment.GetEnvironmentVariable("PLAZA28_SQL_DATABASE");

        sqlServer = string.IsNullOrWhiteSpace(sqlServer) ? branch.SqlServer : sqlServer;
        sqlUser = string.IsNullOrWhiteSpace(sqlUser) ? branch.SqlUser : sqlUser;
        sqlDatabase = string.IsNullOrWhiteSpace(sqlDatabase) ? branch.Database : sqlDatabase;

        if (string.IsNullOrWhiteSpace(sqlServer)
            || string.IsNullOrWhiteSpace(sqlUser)
            || string.IsNullOrWhiteSpace(sqlPassword)
            || string.IsNullOrWhiteSpace(sqlDatabase))
        {
            return null;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = sqlServer,
            InitialCatalog = sqlDatabase,
            UserID = sqlUser,
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            MultipleActiveResultSets = true
        };

        var compuadmoDatabase = string.IsNullOrWhiteSpace(branch.CompuadmoDatabase) ? "compuadmo" : branch.CompuadmoDatabase;
        var joyeriaDatabase = string.IsNullOrWhiteSpace(branch.JoyeriaDatabase) ? "joyeria" : branch.JoyeriaDatabase;

        return new LocalSqlServerSource(
            builder.ConnectionString,
            sqlDatabase,
            sqlDatabase,
            compuadmoDatabase,
            joyeriaDatabase,
            "dbo",
            "dbo",
            "dbo");
    }

    public async Task<SqlConnection> OpenPosAsync()
    {
        var connection = new SqlConnection(BuildConnectionString(PosDatabase));
        await connection.OpenAsync();
        return connection;
    }

    public async Task<SqlConnection> OpenAppAsync()
    {
        var connection = new SqlConnection(BuildConnectionString(AppDatabase));
        await connection.OpenAsync();
        return connection;
    }

    public async Task<SqlConnection> OpenCompuadmoAsync()
    {
        var connection = new SqlConnection(BuildConnectionString(CompuadmoDatabase));
        await connection.OpenAsync();
        return connection;
    }

    public async Task<SqlConnection> OpenJoyeriaAsync()
    {
        var connection = new SqlConnection(BuildConnectionString(JoyeriaDatabase));
        await connection.OpenAsync();
        return connection;
    }

    public string PosTable(string table) => Quote(PosDatabase, PosSchema, table);
    public string AppTable(string table) => Quote(AppDatabase, AppSchema, table);
    public string CompuadmoTable(string table) => Quote(CompuadmoDatabase, StoreSchema, table);
    public string JoyeriaTable(string table) => Quote(JoyeriaDatabase, StoreSchema, table);

    private string BuildConnectionString(string database)
    {
        var builder = new SqlConnectionStringBuilder(_defaultConnection)
        {
            InitialCatalog = database,
            TrustServerCertificate = true,
            Encrypt = false
        };
        return builder.ConnectionString;
    }

    private static string Quote(string database, string schema, string table) => $"[{database}].[{schema}].[{table}]";

    private static string? FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Config"))
                && Directory.Exists(Path.Combine(current.FullName, "ControlTaxiDesktop")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        var working = Environment.CurrentDirectory;
        return Directory.Exists(Path.Combine(working, "Config")) ? working : null;
    }

    private static string? FindProductionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if ((string.Equals(current.Name, "Desktop", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current.Name, "Tools", StringComparison.OrdinalIgnoreCase))
                && current.Parent is not null
                && IsProductionRoot(current.Parent.FullName))
            {
                return current.Parent.FullName;
            }

            if (IsProductionRoot(current.FullName))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return IsProductionRoot(Environment.CurrentDirectory) ? Environment.CurrentDirectory : null;
    }

    private static bool IsProductionRoot(string path) =>
        File.Exists(Path.Combine(path, "appsettings.production.json"))
        || File.Exists(Path.Combine(path, "branches.production.json"))
        || File.Exists(Path.Combine(path, "SyncTaxi_Plaza28", "sync.plaza28.config.json"));
}
