using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface IReportsService
{
    Task<IReadOnlyList<NavierasDailyReportRow>> GetDailyReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasCompanyReportItem>> GetCompanyReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasBoatReportItem>> GetShipReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPersonnelReportRow>> GetGuideReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPersonnelReportRow>> GetCaptainReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPassengerReportRow>> GetPassengerReportAsync(DateOnly from, DateOnly to, string grouping, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasPunctualitySummaryRow>> GetPunctualityReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NavierasBraceletReportRow>> GetBraceletReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<NavierasExportPreview> BuildExportPreviewAsync(string reportName, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
