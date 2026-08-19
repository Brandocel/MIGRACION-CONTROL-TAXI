using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class ServicesService(INavierasModuleService moduleService) : IServicesService
{
    public Task<IReadOnlyList<NavierasServiceSummary>> GetServicesAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default)
        => moduleService.GetServicesAsync(operationDate, cancellationToken);

    public async Task<IReadOnlyList<NavierasServiceSummary>> GetUpcomingArrivalsAsync(CancellationToken cancellationToken = default)
        => (await moduleService.GetServicesAsync(cancellationToken: cancellationToken))
            .OrderBy(x => x.ScheduledArrival)
            .ToArray();

    public async Task<IReadOnlyList<NavierasShipDayItem>> GetShipsOfDayAsync(CancellationToken cancellationToken = default)
        => (await moduleService.GetServicesAsync(cancellationToken: cancellationToken))
            .GroupBy(x => new { x.BoatId, x.BoatName, x.CompanyName })
            .Select(x => new NavierasShipDayItem(
                x.Key.BoatId,
                x.Key.BoatName,
                x.Key.CompanyName,
                x.Count(),
                x.GroupBy(static item => item.Status).OrderByDescending(static grp => grp.Count()).Select(static grp => grp.Key).FirstOrDefault() ?? string.Empty))
            .OrderBy(x => x.BoatName)
            .ToArray();

    public async Task<NavierasServiceSummary?> GetServiceByIdAsync(string serviceId, CancellationToken cancellationToken = default)
        => await moduleService.GetServiceByIdAsync(serviceId, cancellationToken);

    public Task CreateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.CreateServiceAsync(request, performedBy, cancellationToken);

    public Task UpdateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.UpdateServiceAsync(request, performedBy, cancellationToken);

    public Task RescheduleServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.RescheduleServiceAsync(request, performedBy, cancellationToken);

    public Task RegisterArrivalAsync(NavierasArrivalRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.RegisterArrivalAsync(request, performedBy, cancellationToken);

    public Task RegisterDepartureAsync(NavierasDepartureRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.RegisterDepartureAsync(request, performedBy, cancellationToken);

    public Task ValidateBraceletAsync(string serviceId, string braceletFolio, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.ValidateBraceletAsync(serviceId, braceletFolio, performedBy, cancellationToken);

    public Task FinalizeServiceAsync(string serviceId, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.FinalizeServiceAsync(serviceId, performedBy, cancellationToken);

    public Task CancelServiceAsync(string serviceId, string reason, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.CancelServiceAsync(serviceId, reason, performedBy, cancellationToken);
}
