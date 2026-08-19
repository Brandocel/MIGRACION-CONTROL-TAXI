using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiDesktop.Navieras.Infrastructure.Repositories;

public sealed class NavierasCacheRepository(Func<NavierasDbContext> contextFactory) : INavierasCacheRepository
{
    public async Task SaveAsync(string key, string payloadJson, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        var entity = await context.CacheEntries.SingleOrDefaultAsync(x => x.CacheKey == key, cancellationToken)
            ?? new NavierasCacheEntryEntity { CacheKey = key };
        entity.PayloadJson = payloadJson;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        if (context.Entry(entity).State == EntityState.Detached)
            context.CacheEntries.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var context = contextFactory();
        return await context.CacheEntries
            .AsNoTracking()
            .Where(x => x.CacheKey == key)
            .Select(x => x.PayloadJson)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
