using System.Text.Json;
using System.IO;

namespace ControlTaxiDesktop.Tools.CascoSync.State;

public sealed class CascoBadgeSyncStateStore
{
    private readonly string _path;

    public CascoBadgeSyncStateStore(string path)
    {
        _path = path;
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    public CascoBadgeSyncState Load()
    {
        if (!File.Exists(_path))
            return new CascoBadgeSyncState(DateTimeOffset.MinValue, null, new Dictionary<string, CascoBadgeSyncEntryState>(StringComparer.OrdinalIgnoreCase));

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<CascoBadgeSyncState>(json) ?? new CascoBadgeSyncState(DateTimeOffset.MinValue, null, new Dictionary<string, CascoBadgeSyncEntryState>(StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return new CascoBadgeSyncState(DateTimeOffset.MinValue, null, new Dictionary<string, CascoBadgeSyncEntryState>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public void Save(CascoBadgeSyncState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    public string Path => _path;
}

public sealed record CascoBadgeSyncState(
    DateTimeOffset LastRunAt,
    int? LastHttpStatus,
    IReadOnlyDictionary<string, CascoBadgeSyncEntryState> Entries);

public sealed record CascoBadgeSyncEntryState(
    string BadgeId,
    string Fingerprint,
    string Status,
    DateTimeOffset SourceCreatedAt,
    DateTimeOffset? LastAttemptAt,
    int? LastHttpStatus,
    bool Synced,
    string? LastError,
    DateTimeOffset? NextRetryAt);
