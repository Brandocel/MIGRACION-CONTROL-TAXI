using System.ComponentModel.DataAnnotations;

namespace ControlTaxiDesktop.Navieras.Infrastructure.Entities;

public sealed class NavierasCacheEntryEntity
{
    [Key]
    [MaxLength(80)]
    public string CacheKey { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string PayloadJson { get; set; } = "{}";

    public DateTimeOffset UpdatedAt { get; set; }
}
