using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface INavierasSessionRepository
{
    Task SaveAsync(string accessToken, NavierasUser user, string baseUrl, CancellationToken cancellationToken = default);
    Task<(string AccessToken, NavierasUser User, string BaseUrl)?> GetAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}
