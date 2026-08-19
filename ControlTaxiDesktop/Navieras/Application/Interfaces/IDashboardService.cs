using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface IDashboardService
{
    Task<NavierasDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasIndicatorItem>> GetIndicatorsAsync(CancellationToken cancellationToken = default);
}
