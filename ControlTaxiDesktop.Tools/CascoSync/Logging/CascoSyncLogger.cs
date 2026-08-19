using System.Globalization;
using System.Text;

namespace ControlTaxiDesktop.Tools.CascoSync.Logging;

public sealed class CascoSyncLogger
{
    private readonly string _directory;
    private readonly string _logFile;

    public CascoSyncLogger(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        _logFile = Path.Combine(directory, $"casco-sync-{DateTime.UtcNow:yyyyMMddHHmmss}.log");
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.UtcNow:O}] {message}";
        File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
    }

    public string LogPath => _logFile;
}
