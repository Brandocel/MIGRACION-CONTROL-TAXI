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
        var configPath = ResolveBranchConfigPath();
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            return branches;

        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        if (!document.RootElement.TryGetProperty("Branches", out var branchArray) || branchArray.ValueKind != JsonValueKind.Array)
            return branches;

        foreach (var item in branchArray.EnumerateArray())
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
        yield return Path.Combine(AppContext.BaseDirectory, "branches.production.json");
        yield return Path.Combine(Environment.CurrentDirectory, "branches.production.json");

        var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var current = baseDirectory; current is not null; current = current.Parent)
        {
            yield return Path.Combine(current.FullName, "branches.production.json");
        }

        var workspaceRoot = FindWorkspaceRoot();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            yield return Path.Combine(workspaceRoot, "branches.production.json");
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
}
