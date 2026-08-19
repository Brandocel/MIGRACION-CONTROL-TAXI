using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class DashboardService(INavierasModuleService moduleService, IServicesService servicesService) : IDashboardService
{
    public Task<NavierasDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
        => moduleService.GetDashboardAsync(cancellationToken);

    public async Task<IReadOnlyList<NavierasIndicatorItem>> GetIndicatorsAsync(CancellationToken cancellationToken = default)
    {
        var services = await servicesService.GetServicesAsync(cancellationToken: cancellationToken);
        var total = services.Count;
        var arrivalsRegistered = services.Count(x => x.RealArrival is not null);
        var departuresRegistered = services.Count(x => x.RealDeparture is not null);
        var braceletValidated = services.Count(x => x.BraceletValidated);
        var cancelled = services.Count(x => string.Equals(x.Status, "cancelado", StringComparison.OrdinalIgnoreCase));
        return
        [
            new("Servicios con llegada", arrivalsRegistered.ToString(), $"de {total}"),
            new("Servicios con salida", departuresRegistered.ToString(), $"de {total}"),
            new("Brazaletes validados", braceletValidated.ToString(), $"de {total}"),
            new("Servicios cancelados", cancelled.ToString(), $"de {total}")
        ];
    }
}
