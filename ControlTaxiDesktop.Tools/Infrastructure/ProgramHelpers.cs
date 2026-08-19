using System.Globalization;
using System.Text.Json;
using ControlTaxiDesktop.Tools.CascoSync.Configuration;
using ControlTaxiDesktop.Tools.Help;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Tools.Infrastructure;

internal static class ProgramHelpers
{
    public static SqliteConnection OpenSqlite(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        connection.Open();
        return connection;
    }

    public static void EnsureOutputDirectory(string databasePath) => Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);

    public static string QuoteSqlServer(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    public static string QuoteSqlLiteral(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    public static string QuoteSqlite(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    public static string SanitizeName(string name) => string.Concat(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_'));
    public static string EscapeMarkdown(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    public static string BuildSqlConnectionString(CascoSyncOptions options)
    {
        return new SqlConnectionStringBuilder
        {
            DataSource = options.SqlServer,
            InitialCatalog = options.SqlDatabase,
            UserID = options.SqlUser,
            Password = options.SqlPassword ?? string.Empty,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15
        }.ConnectionString;
    }

    public static object ConvertValue(object value) => value switch
    {
        DBNull => DBNull.Value,
        byte[] bytes => bytes,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        bool boolean => boolean ? 1L : 0L,
        Guid guid => guid.ToString("D"),
        _ => value
    };

    public static string JsonToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText()
    };

    public static string MapType(Type type) => type == typeof(byte[]) ? "BLOB" :
        type == typeof(bool) || type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ? "INTEGER" :
        type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? "REAL" : "TEXT";

    public static int Fail(string error)
    {
        Console.Error.WriteLine(error);
        Console.WriteLine(UsageText.Text);
        return 2;
    }

    public static string Required(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"Falta --{key}.");

    public static IReadOnlyDictionary<string, string> ReadOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Opción inválida: {args[index]}");
            result[args[index][2..]] = args[index + 1];
        }
        return result;
    }

    public static IReadOnlyDictionary<string, string> ApplyConfigDefaults(string command, IReadOnlyDictionary<string, string> options)
    {
        var merged = new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(command, "import-sqlserver", StringComparison.OrdinalIgnoreCase))
            return merged;
        if (merged.ContainsKey("connection"))
            return merged;

        var config = TryLoadLocalSqlServerConfig();
        if (config is null)
            return merged;

        if (!merged.ContainsKey("server") && !string.IsNullOrWhiteSpace(config.Server))
            merged["server"] = config.Server;
        if (!merged.ContainsKey("user") && !string.IsNullOrWhiteSpace(config.User))
            merged["user"] = config.User;
        if (!merged.ContainsKey("password") && !string.IsNullOrWhiteSpace(config.Password))
            merged["password"] = config.Password;
        if (!merged.ContainsKey("databases") && config.DatabaseNames.Count > 0)
            merged["databases"] = string.Join(",", config.DatabaseNames);
        if (!merged.ContainsKey("db") && !merged.ContainsKey("output"))
            merged["db"] = Path.Combine(FindWorkspaceRoot(), "DatosLocal", "ControlTaxi.db");
        if (!merged.ContainsKey("report"))
            merged["report"] = Path.Combine(FindWorkspaceRoot(), "REPORTE_IMPORTACION_REAL.md");

        return merged;
    }

    public static LocalSqlServerConfig? TryLoadLocalSqlServerConfig()
    {
        try
        {
            var root = FindWorkspaceRoot();
            var configPath = Path.Combine(root, "Config", "appsettings.json");
            if (!File.Exists(configPath))
                return null;

            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var rootElement = document.RootElement;
            var defaultConnection = rootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString();
            if (string.IsNullOrWhiteSpace(defaultConnection))
                return null;

            var builder = new SqlConnectionStringBuilder(defaultConnection);
            if (!rootElement.TryGetProperty("DatabaseNames", out var databaseNamesElement))
                return null;

            var databaseNames = new[]
            {
                TryReadJsonString(databaseNamesElement, "Compuadmo"),
                TryReadJsonString(databaseNamesElement, "Joyeria"),
                TryReadJsonString(databaseNamesElement, "Pos"),
                TryReadJsonString(databaseNamesElement, "App")
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

            return new LocalSqlServerConfig(
                builder.DataSource,
                builder.UserID,
                builder.Password,
                databaseNames);
        }
        catch
        {
            return null;
        }
    }

    public static string FindWorkspaceRoot()
    {
        var candidates = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..")
        };

        foreach (var candidate in candidates)
        {
            var current = new DirectoryInfo(Path.GetFullPath(candidate));
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "CONTROL TAXI.sln")))
                    return current.FullName;
                current = current.Parent;
            }
        }

        return Path.GetFullPath(Environment.CurrentDirectory);
    }

    private static string? TryReadJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetString() : null;

    public sealed record LocalSqlServerConfig(string Server, string User, string Password, IReadOnlyList<string> DatabaseNames);
}
