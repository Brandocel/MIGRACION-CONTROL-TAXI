using System.Text.Json;
using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class NavierasModuleService(
    INavierasApiClient apiClient,
    INavierasCacheRepository cacheRepository,
    INavierasAuditRepository auditRepository) : INavierasModuleService
{
    public Task<NavierasDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("dashboard", "data/dashboard", NavierasJsonMapper.MapDashboard, cancellationToken);

    public Task<IReadOnlyList<NavierasServiceSummary>> GetServicesAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default)
    {
        var path = operationDate.HasValue ? $"data/services?date={operationDate:yyyy-MM-dd}" : "data/services";
        var cacheKey = "services:" + (operationDate?.ToString("yyyy-MM-dd") ?? "current");
        return GetOrCacheAsync(cacheKey, path, NavierasJsonMapper.MapServices, cancellationToken);
    }

    public async Task<NavierasServiceSummary?> GetServiceByIdAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId))
            return null;

        var cacheKey = $"service:{serviceId}";
        try
        {
            using var document = await apiClient.GetAsync($"services/{serviceId}", cancellationToken);
            var data = document.RootElement.TryGetProperty("data", out var dataElement) ? dataElement : document.RootElement;
            if (data.ValueKind == JsonValueKind.Null || data.ValueKind == JsonValueKind.Undefined)
                return null;

            var json = data.GetRawText();
            await cacheRepository.SaveAsync(cacheKey, json, cancellationToken);
            return NavierasJsonMapper.MapService(data);
        }
        catch (NavierasApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (Exception)
        {
            var cached = await cacheRepository.GetAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(cached))
            {
                using var cachedDocument = JsonDocument.Parse(cached);
                return NavierasJsonMapper.MapService(cachedDocument.RootElement);
            }

            var services = await GetServicesAsync(cancellationToken: cancellationToken);
            return services.FirstOrDefault(x => string.Equals(x.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public Task<IReadOnlyList<NavierasCompany>> GetCompaniesAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("companies", "data/catalogs/companies", NavierasJsonMapper.MapCompanies, cancellationToken);

    public Task<IReadOnlyList<NavierasBoat>> GetBoatsAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("boats", "data/catalogs/boats", NavierasJsonMapper.MapBoats, cancellationToken);

    public Task<IReadOnlyList<NavierasDock>> GetDocksAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("docks", "data/catalogs/docks", NavierasJsonMapper.MapDocks, cancellationToken);

    public Task<IReadOnlyList<NavierasPerson>> GetGuidesAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("guides", "data/catalogs/guides", static data => NavierasJsonMapper.MapPeople(data, "Guia"), cancellationToken);

    public Task<IReadOnlyList<NavierasPerson>> GetCaptainsAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("captains", "data/catalogs/captains", static data => NavierasJsonMapper.MapPeople(data, "Capitan"), cancellationToken);

    public Task<IReadOnlyList<NavierasTimelineEvent>> GetTimelineAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default)
    {
        var path = operationDate.HasValue ? $"data/timeline?date={operationDate:yyyy-MM-dd}" : "data/timeline";
        var cacheKey = "timeline:" + (operationDate?.ToString("yyyy-MM-dd") ?? "current");
        return GetOrCacheAsync(cacheKey, path, NavierasJsonMapper.MapTimeline, cancellationToken);
    }

    public Task<IReadOnlyList<NavierasOperationFolio>> GetOperationFoliosAsync(string? serviceId = null, CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(serviceId) ? "data/operation-folios" : $"data/operation-folios?serviceId={serviceId}";
        var cacheKey = "operation-folios:" + (serviceId ?? "all");
        return GetOrCacheAsync(cacheKey, path, NavierasJsonMapper.MapOperationFolios, cancellationToken);
    }

    public Task<IReadOnlyList<NavierasCatalogItem>> GetCatalogItemsAsync(string type, CancellationToken cancellationToken = default)
        => GetOrCacheAsync("catalog:" + type, $"data/catalogs/{type}", data => NavierasJsonMapper.MapCatalogItems(data, type), cancellationToken);

    public Task<IReadOnlyList<NavierasEditableUser>> GetUsersAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("users", "users", NavierasJsonMapper.MapUsers, cancellationToken);

    public Task<IReadOnlyList<NavierasRole>> GetRolesAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("roles", "roles", NavierasJsonMapper.MapRoles, cancellationToken);

    public Task<IReadOnlyList<NavierasPermissionItem>> GetPermissionsAsync(CancellationToken cancellationToken = default)
        => GetOrCacheAsync("permissions", "permissions", NavierasJsonMapper.MapPermissions, cancellationToken);

    public async Task CreateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync("services", request, performedBy, "create_service", "service", request.ServiceFolio, cancellationToken);
    }

    public async Task UpdateServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ServiceId))
            throw new NavierasApiException("El servicio a editar no tiene identificador.");
        await ExecuteAndAuditAsync($"services/{request.ServiceId}", request, performedBy, "update_service", "service", request.ServiceId!, cancellationToken, usePut: true);
    }

    public async Task RescheduleServiceAsync(NavierasServiceUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ServiceId))
            throw new NavierasApiException("El servicio a reprogramar no tiene identificador.");
        await ExecuteAndAuditAsync($"services/{request.ServiceId}/reschedule", request, performedBy, "reschedule_service", "service", request.ServiceId!, cancellationToken);
    }

    public async Task RegisterArrivalAsync(NavierasArrivalRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"services/{request.ServiceId}/arrival", new
        {
            realArrival = request.RealArrival.ToString("O"),
            dockId = int.TryParse(request.RealDockId, out var dockId) ? dockId : 0,
            guideId = int.TryParse(request.RealGuideId, out var guideId) ? guideId : 0,
            captainId = int.TryParse(request.RealCaptainId, out var captainId) ? captainId : 0,
            realPax = request.RealPax,
            observations = request.Notes
        }, performedBy, "register_arrival", "service", request.ServiceId, cancellationToken);
    }

    public async Task RegisterDepartureAsync(NavierasDepartureRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"services/{request.ServiceId}/departure", new
        {
            realDeparture = request.RealDeparture.ToString("O"),
            departurePax = request.DeparturePax,
            observations = request.Notes
        }, performedBy, "register_departure", "service", request.ServiceId, cancellationToken);
    }

    public async Task ValidateBraceletAsync(string serviceId, string braceletFolio, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"services/{serviceId}/bracelet/validate", new { braceletFolio }, performedBy, "validate_bracelet", "service", serviceId, cancellationToken);
    }

    public async Task FinalizeServiceAsync(string serviceId, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"services/{serviceId}/finalize", new { serviceId }, performedBy, "finalize_service", "service", serviceId, cancellationToken);
    }

    public async Task CancelServiceAsync(string serviceId, string reason, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"services/{serviceId}/cancel", new { reason }, performedBy, "cancel_service", "service", serviceId, cancellationToken);
    }

    public async Task<int> SaveOperationFoliosBatchAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default)
    {
        using var document = await apiClient.PostAsync("operation-folios/batch", new
        {
            serviceId = int.TryParse(serviceId, out var parsedServiceId) ? parsedServiceId : 0,
            boatId = int.TryParse(boatId, out var parsedBoatId) ? parsedBoatId : 0,
            companyId = int.TryParse(companyId, out var parsedCompanyId) ? parsedCompanyId : 0,
            operationDate = operationDate.ToString("yyyy-MM-dd"),
            folios
        }, cancellationToken: cancellationToken);
        await auditRepository.LogAsync(performedBy, "save_operation_folios_batch", "folio_batch", serviceId, $"Folios: {folios.Count}", cancellationToken);
        await InvalidateOperationalCachesAsync(cancellationToken);
        return document.RootElement.GetProperty("data").GetProperty("saved").GetInt32();
    }

    public async Task<IReadOnlyList<string>> ValidateOperationFoliosAsync(string serviceId, string boatId, string companyId, DateOnly operationDate, IReadOnlyList<string> folios, string performedBy, CancellationToken cancellationToken = default)
    {
        using var document = await apiClient.PostAsync("operation-folios/validate", new
        {
            serviceId = int.TryParse(serviceId, out var parsedServiceId) ? parsedServiceId : 0,
            boatId = int.TryParse(boatId, out var parsedBoatId) ? parsedBoatId : 0,
            companyId = int.TryParse(companyId, out var parsedCompanyId) ? parsedCompanyId : 0,
            operationDate = operationDate.ToString("yyyy-MM-dd"),
            folios = folios.Select(static x => x.Trim().ToUpperInvariant()).ToArray()
        }, cancellationToken: cancellationToken);
        await auditRepository.LogAsync(performedBy, "validate_operation_folios", "folio_batch", serviceId, $"Folios: {folios.Count}", cancellationToken);

        if (!document.RootElement.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("validatedFolios", out var validatedElement) ||
            validatedElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return validatedElement.EnumerateArray()
            .Select(static item =>
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("folio", out var folio))
                        return folio.GetString() ?? item.GetRawText();
                    if (item.TryGetProperty("code", out var code))
                        return code.GetString() ?? item.GetRawText();
                }

                return item.ToString();
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    public async Task DeleteOperationFolioAsync(string folioId, string performedBy, CancellationToken cancellationToken = default)
    {
        using var _ = await apiClient.DeleteAsync($"operation-folios/{folioId}", cancellationToken);
        await auditRepository.LogAsync(performedBy, "delete_operation_folio", "operation_folio", folioId, string.Empty, cancellationToken);
        await InvalidateOperationalCachesAsync(cancellationToken);
    }

    public async Task SaveCatalogItemAsync(NavierasCatalogUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        object body = request.Type switch
        {
            "boats" => new { code = request.Code, name = request.Name, parentCompanyId = request.ParentCompanyId, imageUrl = request.SecondaryValue },
            "guides" => new { code = request.Code, name = request.Name, guideNumber = request.SecondaryValue ?? request.Code },
            "docks" => new { code = request.Code, name = request.Name, colorHex = request.SecondaryValue },
            _ => new { code = request.Code, name = request.Name }
        };

        var path = string.IsNullOrWhiteSpace(request.ItemId)
            ? $"catalogs/{request.Type}"
            : $"catalogs/{request.Type}/{request.ItemId}";
        await ExecuteAndAuditAsync(path, body, performedBy, "save_catalog_item", request.Type, request.ItemId ?? request.Code, cancellationToken, usePut: !string.IsNullOrWhiteSpace(request.ItemId));
    }

    public async Task SetCatalogItemActiveAsync(string type, string itemId, bool isActive, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"catalogs/{type}/{itemId}/{(isActive ? "activate" : "deactivate")}", null, performedBy, isActive ? "activate_catalog_item" : "deactivate_catalog_item", type, itemId, cancellationToken);
    }

    public async Task SaveUserAsync(NavierasUserUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(request.UserId) ? "users" : $"users/{request.UserId}";
        await ExecuteAndAuditAsync(path, new
        {
            username = request.Username,
            name = request.Name,
            email = request.Email,
            password = request.Password,
            roleId = int.TryParse(request.RoleId, out var roleId) ? roleId : 0,
            isActive = request.IsActive,
            isBlocked = request.IsBlocked
        }, performedBy, "save_user", "user", request.UserId ?? request.Username, cancellationToken, usePut: !string.IsNullOrWhiteSpace(request.UserId));
    }

    public async Task SetUserBlockedAsync(string userId, bool isBlocked, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"users/{userId}/{(isBlocked ? "block" : "unblock")}", null, performedBy, isBlocked ? "block_user" : "unblock_user", "user", userId, cancellationToken);
    }

    public async Task ResetUserPasswordAsync(string userId, string newPassword, string confirmPassword, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"users/{userId}/reset-password", new { newPassword, confirmPassword }, performedBy, "reset_user_password", "user", userId, cancellationToken);
    }

    public async Task SaveRoleAsync(NavierasRoleUpsertRequest request, string performedBy, CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(request.RoleId) ? "roles" : $"roles/{request.RoleId}";
        await ExecuteAndAuditAsync(path, new
        {
            key = request.Key,
            name = request.Name,
            description = request.Description,
            isActive = request.IsActive,
            permissionCodes = request.PermissionCodes
        }, performedBy, "save_role", "role", request.RoleId ?? request.Key, cancellationToken, usePut: !string.IsNullOrWhiteSpace(request.RoleId));
    }

    public async Task SetRoleActiveAsync(string roleId, bool isActive, string performedBy, CancellationToken cancellationToken = default)
    {
        await ExecuteAndAuditAsync($"roles/{roleId}/{(isActive ? "activate" : "deactivate")}", null, performedBy, isActive ? "activate_role" : "deactivate_role", "role", roleId, cancellationToken);
    }

    private async Task<T> GetOrCacheAsync<T>(string cacheKey, string path, Func<JsonElement, T> mapper, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await apiClient.GetAsync(path, cancellationToken);
            var data = document.RootElement.TryGetProperty("data", out var dataElement) ? dataElement : document.RootElement;
            var json = data.GetRawText();
            await cacheRepository.SaveAsync(cacheKey, json, cancellationToken);
            return mapper(data);
        }
        catch (Exception)
        {
            var cached = await cacheRepository.GetAsync(cacheKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(cached))
                throw;

            using var cachedDocument = JsonDocument.Parse(cached);
            return mapper(cachedDocument.RootElement);
        }
    }

    private async Task ExecuteAndAuditAsync(string path, object? body, string performedBy, string action, string entityType, string entityId, CancellationToken cancellationToken, bool usePut = false)
    {
        if (usePut)
            using (await apiClient.PutAsync(path, body, cancellationToken)) { }
        else
            using (await apiClient.PostAsync(path, body, cancellationToken: cancellationToken)) { }
        await auditRepository.LogAsync(performedBy, action, entityType, entityId, body is null ? string.Empty : JsonSerializer.Serialize(body), cancellationToken);
        await InvalidateOperationalCachesAsync(cancellationToken);
    }

    private async Task InvalidateOperationalCachesAsync(CancellationToken cancellationToken)
    {
        await cacheRepository.SaveAsync("dashboard", string.Empty, cancellationToken);
        await cacheRepository.SaveAsync("services:current", string.Empty, cancellationToken);
        await cacheRepository.SaveAsync("operation-folios:all", string.Empty, cancellationToken);
        await cacheRepository.SaveAsync("timeline:current", string.Empty, cancellationToken);
        await cacheRepository.SaveAsync("roles", string.Empty, cancellationToken);
        await cacheRepository.SaveAsync("users", string.Empty, cancellationToken);
    }
}
