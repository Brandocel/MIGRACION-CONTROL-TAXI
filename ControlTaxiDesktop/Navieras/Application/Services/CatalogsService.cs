using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class CatalogsService(INavierasModuleService moduleService) : ICatalogsService
{
    public Task<IReadOnlyList<NavierasCatalogItem>> GetCatalogItemsAsync(string type, CancellationToken cancellationToken = default)
        => moduleService.GetCatalogItemsAsync(type, cancellationToken);

    public Task<IReadOnlyList<NavierasCompany>> GetCompaniesAsync(CancellationToken cancellationToken = default)
        => moduleService.GetCompaniesAsync(cancellationToken);

    public Task<IReadOnlyList<NavierasBoat>> GetBoatsAsync(CancellationToken cancellationToken = default)
        => moduleService.GetBoatsAsync(cancellationToken);

    public Task<IReadOnlyList<NavierasDock>> GetDocksAsync(CancellationToken cancellationToken = default)
        => moduleService.GetDocksAsync(cancellationToken);

    public Task<IReadOnlyList<NavierasPerson>> GetGuidesAsync(CancellationToken cancellationToken = default)
        => moduleService.GetGuidesAsync(cancellationToken);

    public Task<IReadOnlyList<NavierasPerson>> GetCaptainsAsync(CancellationToken cancellationToken = default)
        => moduleService.GetCaptainsAsync(cancellationToken);

    public Task SaveCatalogItemAsync(NavierasCatalogUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.SaveCatalogItemAsync(request, performedBy, cancellationToken);

    public Task SetCatalogItemActiveAsync(string type, string itemId, bool isActive, string performedBy, CancellationToken cancellationToken = default)
        => moduleService.SetCatalogItemActiveAsync(type, itemId, isActive, performedBy, cancellationToken);
}
