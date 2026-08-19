using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface IServicesService
{
    Task<IReadOnlyList<NavierasServiceSummary>> GetServicesAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasServiceSummary>> GetUpcomingArrivalsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasShipDayItem>> GetShipsOfDayAsync(CancellationToken cancellationToken = default);
    Task<NavierasServiceSummary?> GetServiceByIdAsync(string serviceId, CancellationToken cancellationToken = default);
    Task CreateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task UpdateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task RescheduleServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task RegisterArrivalAsync(NavierasArrivalRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task RegisterDepartureAsync(NavierasDepartureRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task ValidateBraceletAsync(string serviceId, string braceletFolio, string performedBy, CancellationToken cancellationToken = default);
    Task FinalizeServiceAsync(string serviceId, string performedBy, CancellationToken cancellationToken = default);
    Task CancelServiceAsync(string serviceId, string reason, string performedBy, CancellationToken cancellationToken = default);
}
