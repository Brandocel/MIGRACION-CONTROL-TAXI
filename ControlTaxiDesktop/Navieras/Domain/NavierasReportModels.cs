namespace ControlTaxiDesktop.Navieras.Domain;

public enum NavierasPassengerGrouping
{
    Service,
    Ship,
    Company,
    Dock
}

public sealed record NavierasCompanyReportItem(
    string CompanyId,
    string CompanyName,
    int UniqueBoatCount,
    int ServiceCount,
    int ScheduledPassengers,
    int ArrivalPassengers,
    int DeparturePassengers,
    int FinalizedServices,
    int DelayedServices,
    double? ArrivalPunctuality,
    double? DeparturePunctuality);

public sealed record NavierasBoatReportItem(
    string ShipId,
    string BoatName,
    string CompanyId,
    string CompanyName,
    int ServiceCount,
    int ScheduledPassengers,
    int ArrivalPassengers,
    int DeparturePassengers,
    int ArrivalDelays,
    int DepartureDelays,
    int FinalizedServices,
    string DominantStatus);

public sealed record NavierasExportPreview(
    string Title,
    string FormatLabel,
    string FileName,
    string PeriodLabel,
    DateTimeOffset GeneratedAt,
    int RecordCount,
    string Content);
