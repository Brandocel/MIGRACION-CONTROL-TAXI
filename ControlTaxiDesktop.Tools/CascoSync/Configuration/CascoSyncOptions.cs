using System.Text.Json;

namespace ControlTaxiDesktop.Tools.CascoSync.Configuration;

public sealed class CascoSyncOptions
{
    public required string ApiBaseUrl { get; init; }
    public required string ApiEndpointPath { get; init; }
    public required string BranchCode { get; init; }
    public required string SqlServer { get; init; }
    public required string SqlDatabase { get; init; }
    public required string SqlUser { get; init; }
    public string? SqlPassword { get; init; }
    public int RecordLimit { get; init; } = 10;
    public required string LogDirectory { get; init; }
    public required string StateDirectory { get; init; }

    public string BuildApiUrl()
    {
        var baseUri = new Uri(ApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, ApiEndpointPath.TrimStart('/')).ToString();
    }

    public static CascoSyncOptions LoadFromWorkspace()
    {
        var workspaceRoot = FindWorkspaceRoot();
        var configPath = Path.Combine(workspaceRoot, "Config", "appsettings.json");
        var devConfigPath = Path.Combine(workspaceRoot, "Config", "appsettings.Development.json");

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(configPath))
            MergeJsonFile(values, configPath);
        if (File.Exists(devConfigPath))
            MergeJsonFile(values, devConfigPath);

        var apiBaseUrl = ReadValue(values, "CascoSync:ApiBaseUrl") ?? "https://lightyellow-porpoise-679527.hostingersite.com/casco-api/";
        var apiEndpointPath = ReadValue(values, "CascoSync:ApiEndpointPath") ?? "api/taxis/registros";
        var branchCode = ReadValue(values, "CascoSync:BranchCode") ?? "CV";
        var sqlServer = ReadValue(values, "CascoSync:SqlServer") ?? "192.168.1.70,50807";
        var sqlDatabase = ReadValue(values, "CascoSync:SqlDatabase") ?? "mkt";
        var sqlUser = ReadValue(values, "CascoSync:SqlUser") ?? "sa";
        var recordLimitValue = ReadValue(values, "CascoSync:RecordLimit") ?? "10";
        var logDirectory = Path.Combine(workspaceRoot, "ControlTaxiDesktop.Tools", "CascoSync", "Logging");
        var stateDirectory = Path.Combine(workspaceRoot, "ControlTaxiDesktop.Tools", "CascoSync", "State");

        return new CascoSyncOptions
        {
            ApiBaseUrl = apiBaseUrl,
            ApiEndpointPath = apiEndpointPath,
            BranchCode = branchCode,
            SqlServer = sqlServer,
            SqlDatabase = sqlDatabase,
            SqlUser = sqlUser,
            SqlPassword = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD"),
            RecordLimit = int.TryParse(recordLimitValue, out var recordLimit) ? recordLimit : 10,
            LogDirectory = logDirectory,
            StateDirectory = stateDirectory
        };
    }

    private static void MergeJsonFile(IDictionary<string, string?> values, string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Flatten(document.RootElement, string.Empty, values);
    }

    private static void Flatten(JsonElement element, string prefix, IDictionary<string, string?> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var nextKey = string.IsNullOrEmpty(prefix) ? property.Name : prefix + ":" + property.Name;
                    Flatten(property.Value, nextKey, values);
                }
                break;
            case JsonValueKind.String:
                values[prefix] = element.GetString();
                break;
            case JsonValueKind.Number:
                values[prefix] = element.ToString();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                values[prefix] = element.GetBoolean().ToString();
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                values[prefix] = null;
                break;
        }
    }

    private static string? ReadValue(IReadOnlyDictionary<string, string?> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CONTROL TAXI.sln")))
                return current.FullName;
            current = current.Parent;
        }

        return Environment.CurrentDirectory;
    }
}
