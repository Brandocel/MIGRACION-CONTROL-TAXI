namespace ControlTaxiDesktop.Navieras.Domain;

public sealed record NavierasTimelineEvent(
    string EventId,
    DateTimeOffset Timestamp,
    string ServiceId,
    string Title,
    string Description,
    string ShipName,
    string CompanyName,
    string? UserName,
    string Type);

public sealed record NavierasOperationFolio(
    string FolioId,
    string ServiceId,
    string Folio,
    string CompanyName,
    string BoatName,
    DateOnly OperationDate,
    string CapturedBy,
    DateTimeOffset? CapturedAt);

public sealed record NavierasDashboardSnapshot(
    int BoatsToday,
    int ServicesToday,
    int ScheduledPassengers,
    int InOperation,
    int Finished,
    int Delayed);

public sealed record NavierasServiceSummary(
    string ServiceId,
    string ServiceFolio,
    string CompanyId,
    string CompanyName,
    string BoatId,
    string BoatName,
    DateOnly OperationDate,
    DateTimeOffset ScheduledArrival,
    DateTimeOffset ScheduledDeparture,
    DateTimeOffset? RealArrival,
    DateTimeOffset? RealDeparture,
    string ScheduledDockId,
    string ScheduledDockName,
    string? RealDockId,
    string? RealDockName,
    string ScheduledGuideId,
    string ScheduledGuideName,
    string? RealGuideId,
    string? RealGuideName,
    string ScheduledCaptainId,
    string ScheduledCaptainName,
    string? RealCaptainId,
    string? RealCaptainName,
    int ScheduledPax,
    int? RealPax,
    int? DeparturePax,
    string Status,
    string Notes,
    string? BraceletFolio,
    bool BraceletValidated,
    string? BraceletValidatedBy,
    DateTimeOffset? BraceletValidatedAt,
    string? CancelReason);

public sealed record NavierasServiceUpsertRequest(
    string? ServiceId,
    string ServiceFolio,
    string CompanyId,
    string CompanyName,
    string BoatId,
    string BoatName,
    DateOnly OperationDate,
    string GuideId,
    string CaptainId,
    string CaptainName,
    string DockId,
    DateTimeOffset ScheduledArrival,
    DateTimeOffset ScheduledDeparture,
    int ScheduledPax,
    string BraceletFolio,
    string Notes,
    string VesselType);

public sealed record NavierasArrivalRequest(
    string ServiceId,
    DateTimeOffset RealArrival,
    string RealDockId,
    string RealGuideId,
    string RealCaptainId,
    int RealPax,
    string Notes);

public sealed record NavierasDepartureRequest(
    string ServiceId,
    DateTimeOffset RealDeparture,
    int DeparturePax,
    string Notes);

public sealed record NavierasUserUpsertRequest(
    string? UserId,
    string Username,
    string Name,
    string Email,
    string Password,
    string RoleId,
    bool IsActive,
    bool IsBlocked);

public sealed record NavierasRoleUpsertRequest(
    string? RoleId,
    string Key,
    string Name,
    string Description,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes);

public sealed record NavierasCatalogUpsertRequest(
    string Type,
    string? ItemId,
    string Code,
    string Name,
    string? ParentCompanyId,
    string? SecondaryValue);
