using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class RolesService(INavierasModuleService moduleService) : IRolesService
{
    public Task<IReadOnlyList<NavierasRole>> GetRolesAsync(CancellationToken cancellationToken = default)
        => moduleService.GetRolesAsync(cancellationToken);

    public Task<IReadOnlyList<NavierasPermissionItem>> GetPermissionsAsync(CancellationToken cancellationToken = default)
        => moduleService.GetPermissionsAsync(cancellationToken);

    public Task SaveRoleAsync(NavierasRoleUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.SaveRoleAsync(request, performedBy, cancellationToken);

    public Task SetRoleActiveAsync(string roleId, bool isActive, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.SetRoleActiveAsync(roleId, isActive, performedBy, cancellationToken);
}
