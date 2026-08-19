using System.Text.Json;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface INavierasApiClient
{
    Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken = default);
    Task<JsonDocument> PostAsync(string path, object? body = null, bool authenticated = true, CancellationToken cancellationToken = default);
    Task<JsonDocument> PutAsync(string path, object? body = null, CancellationToken cancellationToken = default);
    Task<JsonDocument> DeleteAsync(string path, CancellationToken cancellationToken = default);
}
