namespace ControlTaxiDesktop.Navieras.Domain;

public sealed record NavierasQuickActionItem(string Title, string Description, string TargetView);

public sealed record NavierasShipDayItem(
    string BoatId,
    string BoatName,
    string CompanyName,
    int ServiceCount,
    string DominantStatus);

public sealed record NavierasIndicatorItem(string Title, string Value, string Detail);

public sealed record NavierasDockMapRow(
    string DockId,
    string DockName,
    string Zone,
    int ScheduledServices,
    int ActiveServices,
    int ScheduledPassengers,
    int ArrivalPassengers);

public sealed record NavierasDailyReportRow(
    string ServiceId,
    string ServiceFolio,
    DateOnly OperationDate,
    string CompanyName,
    string BoatName,
    string GuideName,
    string CaptainName,
    string DockName,
    int ScheduledPax,
    int? RealPax,
    int? DeparturePax,
    bool ArrivalDelayed,
    bool DepartureDelayed,
    string Status);

public sealed record NavierasPersonnelReportRow(
    string PersonId,
    string PersonName,
    string RoleLabel,
    int ProgrammedServices,
    int RealServices,
    int ProgrammedPassengers,
    int RealPassengers,
    int AssignmentChanges,
    int FinalizedServices,
    int DelayedServices);

public sealed record NavierasPassengerReportRow(
    string GroupId,
    string Label,
    string SecondaryLabel,
    int ScheduledPassengers,
    int? ArrivalPassengers,
    int? DeparturePassengers,
    int PendingArrival,
    int PendingDeparture);

public sealed record NavierasPunctualitySummaryRow(
    string Label,
    double ArrivalOnTimeRate,
    double DepartureOnTimeRate,
    double AverageArrivalDelay,
    double AverageDepartureDelay,
    int ServiceCount);

public sealed record NavierasBraceletReportRow(
    string ServiceId,
    string ServiceFolio,
    string BoatName,
    string CompanyName,
    string BraceletFolio,
    bool IsValidated,
    DateTimeOffset? ValidatedAt,
    string Status);
