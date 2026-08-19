using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface IFoliosService
{
    Task<IReadOnlyList<NavierasOperationFolio>> GetOperationFoliosAsync(string? serviceId = null, CancellationToken cancellationToken = default);
    Task<int> SaveOperationFoliosBatchAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ValidateOperationFoliosAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default);
    Task DeleteOperationFolioAsync(string folioId, string performedBy, CancellationToken cancellationToken = default);
}
