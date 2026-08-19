using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class FoliosService(INavierasModuleService moduleService) : IFoliosService
{
    public Task<IReadOnlyList<NavierasOperationFolio>> GetOperationFoliosAsync(string? serviceId = null, CancellationToken cancellationToken = default)
        => moduleService.GetOperationFoliosAsync(serviceId, cancellationToken);

    public Task<int> SaveOperationFoliosBatchAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.SaveOperationFoliosBatchAsync(serviceId, boatId, companyId, operationDate, folios, performedBy, cancellationToken);

    public Task<IReadOnlyList<string>> ValidateOperationFoliosAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.ValidateOperationFoliosAsync(serviceId, boatId, companyId, operationDate, folios, performedBy, cancellationToken);

    public Task DeleteOperationFolioAsync(string folioId, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.DeleteOperationFolioAsync(folioId, performedBy, cancellationToken);
}
