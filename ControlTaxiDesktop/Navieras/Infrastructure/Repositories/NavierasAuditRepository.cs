using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Infrastructure.Entities;

namespace ControlTaxiDesktop.Navieras.Infrastructure.Repositories;

public sealed class NavierasAuditRepository(Func<NavierasDbContext> contextFactory) : INavierasAuditRepository
{
    public async Task LogAsync(string userName, string action, string entityType, string entityId, string details, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        context.AuditEntries.Add(new NavierasAuditEntryEntity
        {
            UserName = userName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
