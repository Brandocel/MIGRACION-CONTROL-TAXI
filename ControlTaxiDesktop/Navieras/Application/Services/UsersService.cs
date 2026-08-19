using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class UsersService(INavierasModuleService moduleService) : IUsersService
{
    public Task<IReadOnlyList<NavierasEditableUser>> GetUsersAsync(CancellationToken cancellationToken = default)
        => moduleService.GetUsersAsync(cancellationToken);

    public Task SaveUserAsync(NavierasUserUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.SaveUserAsync(request, performedBy, cancellationToken);

    public Task SetUserBlockedAsync(string userId, bool isBlocked, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.SetUserBlockedAsync(userId, isBlocked, performedBy, cancellationToken);

    public Task ResetUserPasswordAsync(string userId, string newPassword, string confirmPassword, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.ResetUserPasswordAsync(userId, newPassword, confirmPassword, performedBy, cancellationToken);
}
