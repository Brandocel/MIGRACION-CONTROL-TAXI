using System.Text.Json;

namespace ControlTaxiDesktop.Tools.CascoSync.State;

public sealed class CascoSyncStateStore
{
    private readonly string _directory;

    public CascoSyncStateStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public void Save(string name, object value)
    {
        var path = Path.Combine(_directory, name + ".json");
        var serialized = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, serialized);
    }
}
