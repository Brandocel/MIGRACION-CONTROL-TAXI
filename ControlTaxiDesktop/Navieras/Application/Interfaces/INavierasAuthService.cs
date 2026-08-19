using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface INavierasAuthService
{
    Task<NavierasUser> LoginAsync(string username, string password, string? baseUrl = null, CancellationToken cancellationToken = default);
    Task<NavierasUser?> RestoreAsync(CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, string confirmPassword, CancellationToken cancellationToken = default);
    string CurrentBaseUrl { get; }
}
