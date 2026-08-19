using System.ComponentModel.DataAnnotations;

namespace ControlTaxiDesktop.Navieras.Infrastructure.Entities;

public sealed class NavierasSessionEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(512)]
    public string AccessToken { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Role { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string PermissionsJson { get; set; } = "[]";

    [MaxLength(500)]
    public string BaseUrl { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
