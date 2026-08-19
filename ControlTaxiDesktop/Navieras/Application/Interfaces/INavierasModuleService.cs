using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface INavierasModuleService
{
    Task<NavierasDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasServiceSummary>> GetServicesAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default);
    Task<NavierasServiceSummary?> GetServiceByIdAsync(string serviceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasCompany>> GetCompaniesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasBoat>> GetBoatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasDock>> GetDocksAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPerson>> GetGuidesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPerson>> GetCaptainsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasTimelineEvent>> GetTimelineAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasOperationFolio>> GetOperationFoliosAsync(string? serviceId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasCatalogItem>> GetCatalogItemsAsync(string type, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasEditableUser>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasRole>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPermissionItem>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task CreateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task UpdateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task RescheduleServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task RegisterArrivalAsync(NavierasArrivalRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task RegisterDepartureAsync(NavierasDepartureRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task ValidateBraceletAsync(string serviceId, string braceletFolio, string performedBy, CancellationToken cancellationToken = default);
    Task FinalizeServiceAsync(string serviceId, string performedBy, CancellationToken cancellationToken = default);
    Task CancelServiceAsync(string serviceId, string reason, string performedBy, CancellationToken cancellationToken = default);
    Task<int> SaveOperationFoliosBatchAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ValidateOperationFoliosAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default);
    Task DeleteOperationFolioAsync(string folioId, string performedBy, CancellationToken cancellationToken = default);
    Task SaveCatalogItemAsync(NavierasCatalogUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task SetCatalogItemActiveAsync(string type, string itemId, bool isActive, string performedBy, CancellationToken cancellationToken = default);
    Task SaveUserAsync(NavierasUserUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task SetUserBlockedAsync(string userId, bool isBlocked, string performedBy, CancellationToken cancellationToken = default);
    Task ResetUserPasswordAsync(string userId, string newPassword, string confirmPassword, string performedBy, CancellationToken cancellationToken = default);
    Task SaveRoleAsync(NavierasRoleUpsertRequest request, string performedBy, CancellationToken cancellationToken = default);
    Task SetRoleActiveAsync(string roleId, bool isActive, string performedBy, CancellationToken cancellationToken = default);
}
