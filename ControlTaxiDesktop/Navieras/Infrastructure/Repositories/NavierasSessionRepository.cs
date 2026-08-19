using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;
using ControlTaxiDesktop.Navieras.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiDesktop.Navieras.Infrastructure.Repositories;

public sealed class NavierasSessionRepository(Func<NavierasDbContext> contextFactory) : INavierasSessionRepository
{
    public async Task SaveAsync(string accessToken, NavierasUser user, string baseUrl, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var entity = await context.Sessions.SingleOrDefaultAsync(cancellationToken) ?? new NavierasSessionEntity();
        entity.AccessToken = Protect(accessToken);
        entity.Username = user.Username;
        entity.DisplayName = user.FullName;
        entity.Role = user.Role;
        entity.BaseUrl = baseUrl;
        entity.PermissionsJson = JsonSerializer.Serialize(user.Permissions.OrderBy(static x => x));
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        if (entity.Id == 0)
            context.Sessions.Add(entity);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(string AccessToken, NavierasUser User, string BaseUrl)?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var entity = await context.Sessions.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (entity is null || string.IsNullOrWhiteSpace(entity.AccessToken))
            return null;

        var permissions = JsonSerializer.Deserialize<string[]>(entity.PermissionsJson) ?? [];
        var user = new NavierasUser(
            entity.Id.ToString(),
            entity.Username,
            entity.DisplayName,
            entity.Role,
            permissions.ToHashSet(StringComparer.OrdinalIgnoreCase),
            true,
            false);
        return (Unprotect(entity.AccessToken), user, entity.BaseUrl);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        context.Sessions.RemoveRange(context.Sessions);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string Protect(string value)
    {
        var plainBytes = Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string value)
    {
        var protectedBytes = Convert.FromBase64String(value);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
