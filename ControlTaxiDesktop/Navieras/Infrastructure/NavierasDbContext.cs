using ControlTaxiDesktop.Navieras.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiDesktop.Navieras.Infrastructure;

public sealed class NavierasDbContext(DbContextOptions<NavierasDbContext> options) : DbContext(options)
{
    public DbSet<NavierasSessionEntity> Sessions => Set<NavierasSessionEntity>();
    public DbSet<NavierasCacheEntryEntity> CacheEntries => Set<NavierasCacheEntryEntity>();
    public DbSet<NavierasAuditEntryEntity> AuditEntries => Set<NavierasAuditEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NavierasSessionEntity>().ToTable("NavierasSessions");
        modelBuilder.Entity<NavierasCacheEntryEntity>().ToTable("NavierasCacheEntries");
        modelBuilder.Entity<NavierasAuditEntryEntity>().ToTable("NavierasAuditEntries");
    }
}
