using System.Collections.Concurrent;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

public sealed class CascoSyncLockService
{
    private static readonly ConcurrentDictionary<string, byte> Locks = new(StringComparer.OrdinalIgnoreCase);

    public bool TryAcquire(string name)
    {
        return Locks.TryAdd(name, 0);
    }

    public void Release(string name)
    {
        Locks.TryRemove(name, out _);
    }
}
