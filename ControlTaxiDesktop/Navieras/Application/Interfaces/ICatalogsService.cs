using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface ICatalogsService
{
    Task<IReadOnlyList<NavierasCatalogItem>> GetCatalogItemsAsync(string type, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasCompany>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasBoat>> GetBoatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasDock>> GetDocksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPerson>> GetGuidesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPerson>> GetCaptainsAsync(CancellationToken cancellationToken = default);
    Task SaveCatalogItemAsync(NavierasCatalogUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task SetCatalogItemActiveAsync(string type, string itemId, bool isActive, string performedBy, CancellationToken cancellationToken = default);
}
