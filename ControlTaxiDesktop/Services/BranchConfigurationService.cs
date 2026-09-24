using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ControlTaxiDesktop.Services;

public sealed class BranchConfigurationService
{
    private static readonly Dictionary<string, BranchConfiguration> DefaultBranches = new(StringComparer.OrdinalIgnoreCase)
    {
        ["P28"] = new BranchConfiguration(
            Code: "P28",
            Name: "Plaza 28",
            SqlServer: "26.38.252.71\\SQLEXPRESS",
            Database: "mkt",
            SiteName: "Plaza 28",
            ApiBaseUrl: "https://lightyellow-porpoise-679527.hostingersite.com/",
            IsReadOnly: false)
        {
            CompuadmoDatabase = "compuadmo",
            JoyeriaDatabase = "joyeria",
            SqlUser = "sa"
        },
        ["CV"] = new BranchConfiguration(
            Code: "CV",
            Name: "Casco Viejo",
            SqlServer: "192.168.1.70,50807",
            Database: "mkt",
            SiteName: "Casco Viejo",
            ApiBaseUrl: "https://lightyellow-porpoise-679527.hostingersite.com/casco-api/",
            IsReadOnly: true)
        {
            CompuadmoDatabase = "compuadmo",
            JoyeriaDatabase = "joyeria",
            SqlUser = "sa"
        },
        ["ALL"] = new BranchConfiguration(
            Code: "ALL",
            Name: "Ambas",
            SqlServer: string.Empty,
            Database: string.Empty,
            SiteName: string.Empty,
            ApiBaseUrl: string.Empty,
            IsReadOnly: false)
    };

    private static readonly Lazy<Dictionary<string, BranchConfiguration>> LoadedBranches = new(LoadBranches);
    // Ruta del fichero de configuración cargado (si existe). Puede ser branches.local.json o branches.production.json
    private static string? LoadedConfigPath;

    public BranchConfiguration GetBranch(string code)
    {
        if (LoadedBranches.Value.TryGetValue(code, out var config))
            return config;

        throw new KeyNotFoundException($"La sucursal '{code}' no esta definida.");
    }

    public IEnumerable<BranchConfiguration> GetAllBranches() => LoadedBranches.Value.Values;

    private static Dictionary<string, BranchConfiguration> LoadBranches()
    {
        var branches = new Dictionary<string, BranchConfiguration>(DefaultBranches, StringComparer.OrdinalIgnoreCase);
        // Load production config first (if present), then apply local override (branches.local.json)
        var candidates = EnumerateBranchConfigCandidates().Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // production path: first candidate whose filename is branches.production.json
        var productionPath = candidates.FirstOrDefault(p => string.Equals(Path.GetFileName(p), "branches.production.json", StringComparison.OrdinalIgnoreCase) && File.Exists(p));
        if (!string.IsNullOrWhiteSpace(productionPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(productionPath));
                if (doc.RootElement.TryGetProperty("Branches", out var prodArray) && prodArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prodArray.EnumerateArray())
                    {
                        var code = ReadString(item, "Code");
                        if (string.IsNullOrWhiteSpace(code))
                            continue;

                        branches[code] = new BranchConfiguration(
                            Code: code,
                            Name: ReadString(item, "Name"),
                            SqlServer: ReadString(item, "SqlServer"),
                            Database: ReadString(item, "Database"),
                            SiteName: ReadString(item, "SiteName"),
                            ApiBaseUrl: ReadString(item, "ApiBaseUrl"),
                            IsReadOnly: ReadBool(item, "IsReadOnly"))
                        {
                            CompuadmoDatabase = ReadString(item, "CompuadmoDatabase"),
                            JoyeriaDatabase = ReadString(item, "JoyeriaDatabase"),
                            SqlUser = string.IsNullOrWhiteSpace(ReadString(item, "SqlUser")) ? "sa" : ReadString(item, "SqlUser")
                        };
                    }
                }
                LoadedConfigPath = productionPath;
            }
            catch
            {
                // ignore parse errors and continue with defaults
            }
        }

        // local override: apply only entries present in branches.local.json (e.g. CV) on top of production/default
        var localPath = candidates.FirstOrDefault(p => string.Equals(Path.GetFileName(p), "branches.local.json", StringComparison.OrdinalIgnoreCase) && File.Exists(p));
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(localPath));
                if (doc.RootElement.TryGetProperty("Branches", out var localArray) && localArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in localArray.EnumerateArray())
                    {
                        var code = ReadString(item, "Code");
                        if (string.IsNullOrWhiteSpace(code))
                            continue;

                        // overlay/replace only the specified branches
                        branches[code] = new BranchConfiguration(
                            Code: code,
                            Name: ReadString(item, "Name"),
                            SqlServer: ReadString(item, "SqlServer"),
                            Database: ReadString(item, "Database"),
                            SiteName: ReadString(item, "SiteName"),
                            ApiBaseUrl: ReadString(item, "ApiBaseUrl"),
                            IsReadOnly: ReadBool(item, "IsReadOnly"))
                        {
                            CompuadmoDatabase = ReadString(item, "CompuadmoDatabase"),
                            JoyeriaDatabase = ReadString(item, "JoyeriaDatabase"),
                            SqlUser = string.IsNullOrWhiteSpace(ReadString(item, "SqlUser")) ? "sa" : ReadString(item, "SqlUser")
                        };
                    }
                }
                // mark that a local override was applied
                LoadedConfigPath = localPath;
            }
            catch
            {
                // ignore parse errors
            }
        }

        return branches;
    }

    private static string? ResolveBranchConfigPath()
    {
        var candidates = EnumerateBranchConfigCandidates()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> EnumerateBranchConfigCandidates()
    {
        // Prefer a local override file for development/testing that does NOT modify
        // the production configuration. If branches.local.json exists it will
        // override branches.production.json.
        // Check for a local override both in the running output folder and in the
        // workspace root. The project copies ..\branches.local.json to the
        // output folder for Debug builds, but when running from dotnet the
        // workspace file may be the authoritative source. Include both.
        yield return Path.Combine(AppContext.BaseDirectory, "branches.local.json");
        yield return Path.Combine(Environment.CurrentDirectory, "branches.local.json");

        yield return Path.Combine(AppContext.BaseDirectory, "branches.production.json");
        yield return Path.Combine(Environment.CurrentDirectory, "branches.production.json");

        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = baseDirectory; current is not null; current = current.Parent)
        {
            yield return Path.Combine(current.FullName, "branches.production.json");
        }

        var wr2 = FindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(wr2))
            yield return Path.Combine(wr2, "branches.production.json");
    }

    private static string? FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CONTROL TAXI.sln")))
                return current.FullName;

            current = current.Parent;
        }

        return Directory.Exists(Environment.CurrentDirectory) ? Environment.CurrentDirectory : null;
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false
        };
    }

    // Devuelve la ruta del fichero de configuración aplicada actualmente, o null si no se aplicó ninguno.
    public string? GetLoadedConfigPath() => LoadedConfigPath;

    // Indica si se está usando el override local (branches.local.json) en lugar de la configuración de producción.
    public bool IsLocalOverrideActive()
    {
        if (string.IsNullOrWhiteSpace(LoadedConfigPath))
            return false;
        return string.Equals(Path.GetFileName(LoadedConfigPath), "branches.local.json", StringComparison.OrdinalIgnoreCase);
    }
}
