namespace ControlTaxiDesktop.Navieras.Domain;

public sealed record NavierasCompany(string CompanyId, string Code, string Name, bool IsActive);

public sealed record NavierasBoat(string BoatId, string CompanyId, string Name, string VesselType, bool IsActive);

public sealed record NavierasDock(string DockId, string Code, string Name, string Zone, bool IsActive);

public sealed record NavierasPerson(string PersonId, string Code, string Name, string RoleLabel, bool IsActive, string? ReferenceNumber = null);

public sealed record NavierasCatalogItem(
    string Id,
    string Type,
    string Code,
    string Name,
    string Subtitle,
    bool IsActive,
    int UsageCount,
    string? ParentId = null);

public sealed record NavierasRole(
    string RoleId,
    string Key,
    string Name,
    bool IsActive,
    int UserCount,
    IReadOnlySet<string> Permissions);

public sealed record NavierasPermissionItem(
    string PermissionId,
    string Code,
    string Name,
    string Description,
    bool IsActive);

public sealed record NavierasEditableUser(
    NavierasUser User,
    bool IsActive,
    bool IsBlocked,
    int FailedAttempts,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLoginAt);
