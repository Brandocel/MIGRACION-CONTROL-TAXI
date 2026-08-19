using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface IRolesService
{
    Task<IReadOnlyList<NavierasRole>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPermissionItem>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task SaveRoleAsync(NavierasRoleUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task SetRoleActiveAsync(string roleId, bool isActive, string performedBy, CancellationToken cancellationToken = default);
}
