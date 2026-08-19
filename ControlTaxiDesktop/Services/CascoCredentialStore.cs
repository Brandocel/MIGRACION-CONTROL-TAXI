using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public static class CascoCredentialStore
{
    private const string FileName = "casco.credentials.dat";
    private const string FolderName = "Config";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ControlTaxiDesktop.Casco.Credentials.v1");

    public static string GetCredentialPath()
    {
        var configDirectory = Path.Combine(AppRoot(), FolderName);
        Directory.CreateDirectory(configDirectory);
        return Path.Combine(configDirectory, FileName);
    }

    private static IEnumerable<string> GetCredentialCandidatePaths()
    {
        var root = AppRoot();
        yield return Path.Combine(root, FolderName, FileName);
        yield return Path.Combine(root, "Desktop", FolderName, FileName);
        yield return Path.Combine(root, "Tools", FolderName, FileName);
        yield return Path.Combine(AppContext.BaseDirectory, FolderName, FileName);

        var baseParent = Directory.GetParent(AppContext.BaseDirectory);
        if (baseParent is not null)
            yield return Path.Combine(baseParent.FullName, FolderName, FileName);
    }

    public static bool HasSavedCredential() => File.Exists(GetCredentialPath());

    public static bool TryLoad(out CascoSqlCredential? credential, out string error)
    {
        credential = null;
        error = string.Empty;

        try
        {
            var path = GetCredentialCandidatePaths()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(File.Exists);
            if (path is null)
            {
                error = "No existe una credencial guardada de Casco.";
                return false;
            }

            var encrypted = File.ReadAllBytes(path);
            var jsonBytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.LocalMachine);
            credential = JsonSerializer.Deserialize<CascoSqlCredential>(jsonBytes);
            if (credential is null
                || string.IsNullOrWhiteSpace(credential.SqlServer)
                || string.IsNullOrWhiteSpace(credential.SqlUser)
                || string.IsNullOrWhiteSpace(credential.SqlPassword)
                || string.IsNullOrWhiteSpace(credential.Database)
                || string.IsNullOrWhiteSpace(credential.BranchCode))
            {
                error = "La credencial cifrada de Casco esta incompleta.";
                credential = null;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            credential = null;
            return false;
        }
    }

    public static bool TryApplyToEnvironment(out string error)
    {
        error = string.Empty;
        if (!TryLoad(out var credential, out error) || credential is null)
            return false;

        ApplyToEnvironment(credential);
        return true;
    }

    public static void ApplyToEnvironment(CascoSqlCredential credential)
    {
        Environment.SetEnvironmentVariable("CASCO_SQL_PASSWORD", credential.SqlPassword);
        Environment.SetEnvironmentVariable("CASCO_SQL_SERVER", credential.SqlServer);
        Environment.SetEnvironmentVariable("CASCO_SQL_USER", credential.SqlUser);
        Environment.SetEnvironmentVariable("CASCO_SQL_DATABASE", credential.Database);
        Environment.SetEnvironmentVariable("CASCO_BRANCH_CODE", credential.BranchCode);
    }

    public static void Save(CascoSqlCredential credential)
    {
        var path = GetCredentialPath();
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(credential);
        var encrypted = ProtectedData.Protect(jsonBytes, Entropy, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(path, encrypted);
        ApplyToEnvironment(credential);
    }

    public static async Task TestConnectionAsync(CascoSqlCredential credential, CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = credential.SqlServer,
            InitialCatalog = credential.Database,
            UserID = credential.SqlUser,
            Password = credential.SqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 5
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@SERVERNAME;";
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public static CascoSqlCredential CreateDefault(BranchConfiguration branch) =>
        new(
            string.IsNullOrWhiteSpace(branch.SqlServer) ? "192.168.1.70,50807" : branch.SqlServer,
            string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser,
            string.Empty,
            string.IsNullOrWhiteSpace(branch.Database) ? "mkt" : branch.Database,
            string.IsNullOrWhiteSpace(branch.Code) ? "CV" : branch.Code);

    private static string AppRoot()
    {
        // Recorre hasta la raiz del disco y se queda con la coincidencia MAS ALTA (no la primera),
        // porque el build copia SyncTaxi\sync.casco.config.json dentro de la propia carpeta
        // bin\Release\net9.0-windows: si nos quedamos con la primera coincidencia, la busqueda se
        // detiene ahi mismo y nunca sube hasta la carpeta real del proyecto donde vive Config\.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? found = null;
        while (current is not null)
        {
            if ((string.Equals(current.Name, "Desktop", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(current.Name, "Tools", StringComparison.OrdinalIgnoreCase))
                && current.Parent is not null
                && (File.Exists(Path.Combine(current.Parent.FullName, "appsettings.production.json"))
                    || File.Exists(Path.Combine(current.Parent.FullName, "SyncTaxi", "sync.casco.config.json"))))
            {
                found = current.Parent.FullName;
            }
            else if (File.Exists(Path.Combine(current.FullName, "CONTROL TAXI.sln"))
                || File.Exists(Path.Combine(current.FullName, "appsettings.production.json"))
                || File.Exists(Path.Combine(current.FullName, "SyncTaxi", "sync.casco.config.json"))
                || File.Exists(Path.Combine(current.FullName, "Tools", "ControlTaxiDesktop.Tools.exe"))
                || File.Exists(Path.Combine(current.FullName, "Desktop", "ControlTaxiDesktop.exe")))
            {
                found = current.FullName;
            }

            current = current.Parent;
        }

        return found ?? AppContext.BaseDirectory;
    }
}
