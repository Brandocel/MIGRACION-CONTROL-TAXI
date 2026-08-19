namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface INavierasCacheRepository
{
    Task SaveAsync(string key, string payloadJson, CancellationToken cancellationToken = default);
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
}
