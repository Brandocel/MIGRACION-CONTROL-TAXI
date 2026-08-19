using System.ComponentModel.DataAnnotations;

namespace ControlTaxiDesktop.Navieras.Infrastructure.Entities;

public sealed class NavierasAuditEntryEntity
{
    [Key]
    public long Id { get; set; }

    [MaxLength(100)]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    [MaxLength(100)]
    public string EntityId { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Details { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }
}
