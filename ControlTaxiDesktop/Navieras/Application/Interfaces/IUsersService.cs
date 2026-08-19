using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface IUsersService
{
    Task<IReadOnlyList<NavierasEditableUser>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task SaveUserAsync(NavierasUserUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task SetUserBlockedAsync(string userId, bool isBlocked, string performedBy, CancellationToken cancellationToken = default);
    Task ResetUserPasswordAsync(string userId, string newPassword, string confirmPassword, string performedBy, CancellationToken cancellationToken = default);
}
