using System.Text.Json;

namespace ControlTaxiDesktop.Tools.CascoSync.Configuration;

public sealed record CascoBadgeSyncConfiguration(
    string ConfigPath,
    string ApiBaseUrl,
    string BranchCode,
    string SqlServer,
    string MktDatabase,
    string SqlUser,
    string SyncToken,
    string LogFilePath,
    string StatusFilePath,
    string LockFilePath,
    int IntervalSeconds)
{
    public string BuildPushUrl() => ApiBaseUrl.TrimEnd('/') + "/api/taxis/gafetes/sync";

    public static CascoBadgeSyncConfiguration LoadFromWorkspace(string? workspaceRoot = null)
    {
        workspaceRoot ??= FindWorkspaceRoot();
        var configPath = ResolveConfigPath(workspaceRoot);
        if (!File.Exists(configPath))
            throw new FileNotFoundException("No se encontro sync.casco.config.json. El sincronizador de Casco se aborta sin enviar.", configPath);

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = document.RootElement;

        var configuration = new CascoBadgeSyncConfiguration(
            configPath,
            ReadRequired(root, "ApiBaseUrl"),
            ReadRequired(root, "BranchCode"),
            ReadRequired(root, "SqlServer"),
            ReadRequired(root, "MktDatabase"),
            ReadOptional(root, "SqlUser") ?? "sa",
            ReadRequired(root, "SyncToken"),
            ResolveWorkspacePath(workspaceRoot, ReadOptional(root, "LogFilePath") ?? Path.Combine("Logs", "CascoSync", "casco-badge-sync.log")),
            ResolveWorkspacePath(workspaceRoot, ReadOptional(root, "StatusFilePath") ?? Path.Combine("Logs", "CascoSync", "casco-badge-sync-status.json")),
            ResolveWorkspacePath(workspaceRoot, ReadOptional(root, "LockFilePath") ?? Path.Combine("Logs", "CascoSync", "casco-badge-sync.lock")),
            ReadInt(root, "IntervalSeconds", 20));

        Validate(configuration);
        return configuration;
    }

    private static void Validate(CascoBadgeSyncConfiguration configuration)
    {
        if (string.Equals(configuration.BranchCode, "28", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configuracion invalida: BranchCode 28 pertenece a Plaza 28.");

        if (!string.Equals(configuration.BranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Configuracion invalida: BranchCode '{configuration.BranchCode}' no corresponde a Casco Viejo.");

        if (!configuration.ApiBaseUrl.Contains("/casco-api", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configuracion invalida: la URL de Casco debe contener /casco-api/.");

        if (!string.Equals(configuration.MktDatabase, "mkt", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configuracion invalida: la base local debe ser mkt.");
    }

    private static string ReadRequired(JsonElement element, string propertyName)
    {
        var value = ReadOptional(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Falta '{propertyName}' en sync.casco.config.json.");
        return value.Trim();
    }

    private static string? ReadOptional(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadInt(JsonElement element, string propertyName, int defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return defaultValue;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            return number;
        return defaultValue;
    }

    private static string ResolveWorkspacePath(string workspaceRoot, string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(workspaceRoot, path));
    }

    private static string ResolveConfigPath(string workspaceRoot)
    {
        var candidates = new[]
        {
            Path.Combine(workspaceRoot, "SyncTaxi", "sync.casco.config.json"),
            Path.Combine(workspaceRoot, "ControlTaxiDesktop", "SyncTaxi", "sync.casco.config.json"),
            Path.Combine(Environment.CurrentDirectory, "sync.casco.config.json"),
            Path.Combine(AppContext.BaseDirectory, "SyncTaxi", "sync.casco.config.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CONTROL TAXI.sln")))
                return current.FullName;
            current = current.Parent;
        }

        return Path.GetFullPath(Environment.CurrentDirectory);
    }
}
