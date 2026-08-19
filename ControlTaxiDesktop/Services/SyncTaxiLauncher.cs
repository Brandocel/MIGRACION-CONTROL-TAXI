using System.Diagnostics;
using System.IO;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Lanza el sincronizador SyncTaxi (los scripts PowerShell ya probados) junto
/// con el Desktop. El sincronizador trae los registros de la App móvil desde
/// Hostinger y los guarda en SQL Server local (mkt2), que es de donde el
/// Desktop lee <c>AppMovilRegistro</c>. Así el Desktop recibe los datos igual
/// que el Web.
///
/// No reimplementa la sincronización: sólo dispara el loop oculto existente.
/// El propio loop (<c>sync-sqlserver-hostinger-loop.ps1</c>) evita procesos
/// duplicados mediante su archivo de bloqueo, así que es seguro llamarlo aunque
/// ya esté corriendo por la tarea programada del servidor.
///
/// Cualquier fallo se ignora en silencio: el sincronizador nunca debe impedir
/// que el Desktop abra.
/// </summary>
public static class SyncTaxiLauncher
{
    private const string CascoLauncherFileName = "iniciar-sincronizador-casco-oculto.vbs";
    private const string LoopLauncherFileName = "iniciar-sincronizador-automatico.vbs";

    public static void TryStart()
    {
        try
        {
            var launcher = FindLoopLauncher();
            if (launcher is null)
            {
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "wscript.exe",
                Arguments = $"\"{launcher}\"",
                WorkingDirectory = Path.GetDirectoryName(launcher) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(startInfo);
        }
        catch
        {
            // El arranque del sincronizador no debe bloquear la apertura del Desktop.
        }
    }

    private static string? FindLoopLauncher()
    {
        foreach (var directory in CandidateDirectories())
        {
            var cascoCandidate = Path.Combine(directory, CascoLauncherFileName);
            if (File.Exists(cascoCandidate))
                return cascoCandidate;
        }

        foreach (var directory in CandidateDirectories())
        {
            var candidate = Path.Combine(directory, LoopLauncherFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        // 1) Empaquetado junto al ejecutable del Desktop (bin\...\SyncTaxi).
        yield return Path.Combine(AppContext.BaseDirectory, "SyncTaxi");
        // 2) Mismo directorio del ejecutable.
        yield return AppContext.BaseDirectory;

        // 3) Copia hermana del proyecto SyncTaxi durante el desarrollo local.
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var sibling = Path.Combine(current.FullName, "SyncTaxi", "SyncTaxi");
            if (Directory.Exists(sibling))
            {
                yield return sibling;
            }

            current = current.Parent;
        }
    }
}
