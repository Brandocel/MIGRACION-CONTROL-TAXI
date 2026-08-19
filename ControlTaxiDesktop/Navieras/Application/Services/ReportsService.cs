using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class ReportsService(
    IServicesService servicesService,
    ICatalogsService catalogsService) : IReportsService
{
    public async Task<IReadOnlyList<NavierasDailyReportRow>> GetDailyReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => NavierasReportBuilder.BuildDailyReport(await GetFilteredServicesAsync(from, to, cancellationToken));

    public async Task<IReadOnlyList<NavierasCompanyReportItem>> GetCompanyReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => NavierasReportBuilder.BuildCompanyReport(await GetFilteredServicesAsync(from, to, cancellationToken));

    public async Task<IReadOnlyList<NavierasBoatReportItem>> GetShipReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => NavierasReportBuilder.BuildBoatReport(await GetFilteredServicesAsync(from, to, cancellationToken));

    public async Task<IReadOnlyList<NavierasPersonnelReportRow>> GetGuideReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var services = await GetFilteredServicesAsync(from, to, cancellationToken);
        var guides = await catalogsService.GetGuidesAsync(cancellationToken);
        return NavierasReportBuilder.BuildGuideReport(services, guides);
    }

    public async Task<IReadOnlyList<NavierasPersonnelReportRow>> GetCaptainReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var services = await GetFilteredServicesAsync(from, to, cancellationToken);
        var captains = await catalogsService.GetCaptainsAsync(cancellationToken);
        return NavierasReportBuilder.BuildCaptainReport(services, captains);
    }

    public async Task<IReadOnlyList<NavierasPassengerReportRow>> GetPassengerReportAsync(DateOnly from, DateOnly to, string grouping, CancellationToken cancellationToken = default)
        => NavierasReportBuilder.BuildPassengerReport(
            await GetFilteredServicesAsync(from, to, cancellationToken),
            ParseGrouping(grouping));

    public async Task<IReadOnlyList<NavierasPunctualitySummaryRow>> GetPunctualityReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => NavierasReportBuilder.BuildPunctualityReport(await GetFilteredServicesAsync(from, to, cancellationToken));

    public async Task<IReadOnlyList<NavierasBraceletReportRow>> GetBraceletReportAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => NavierasReportBuilder.BuildBraceletReport(await GetFilteredServicesAsync(from, to, cancellationToken));

    public async Task<NavierasExportPreview> BuildExportPreviewAsync(string reportName, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var lines = reportName.Trim().ToLowerInvariant() switch
        {
            "daily" or "reporte diario" => (await GetDailyReportAsync(from, to, cancellationToken))
                .Select(x => $"{x.OperationDate:dd/MM/yyyy} | {x.ServiceFolio} | {x.CompanyName} | {x.BoatName} | PAX {x.ScheduledPax}/{x.RealPax?.ToString() ?? "Pend."}/{x.DeparturePax?.ToString() ?? "Pend."} | {x.Status}"),
            "company" or "shipping company report" => (await GetCompanyReportAsync(from, to, cancellationToken))
                .Select(x => $"{x.CompanyName} | Servicios {x.ServiceCount} | Programados {x.ScheduledPassengers} | Llegada {x.ArrivalPassengers} | Salida {x.DeparturePassengers}"),
            "ship" or "ship report" => (await GetShipReportAsync(from, to, cancellationToken))
                .Select(x => $"{x.BoatName} | {x.CompanyName} | Servicios {x.ServiceCount} | Estado {x.DominantStatus}"),
            "guide" or "guide report" => (await GetGuideReportAsync(from, to, cancellationToken))
                .Select(x => $"{x.PersonName} | Servicios {x.ProgrammedServices} | Reales {x.RealServices} | Cambios {x.AssignmentChanges}"),
            "captain" or "captain report" => (await GetCaptainReportAsync(from, to, cancellationToken))
                .Select(x => $"{x.PersonName} | Servicios {x.ProgrammedServices} | Reales {x.RealServices} | Cambios {x.AssignmentChanges}"),
            "passenger" or "passenger report" => (await GetPassengerReportAsync(from, to, "service", cancellationToken))
                .Select(x => $"{x.Label} | Programados {x.ScheduledPassengers} | Llegada {x.ArrivalPassengers?.ToString() ?? "Pend."} | Salida {x.DeparturePassengers?.ToString() ?? "Pend."}"),
            "punctuality" or "punctuality report" => (await GetPunctualityReportAsync(from, to, cancellationToken))
                .Select(x => $"{x.Label} | Llegada {x.ArrivalOnTimeRate:N1}% | Salida {x.DepartureOnTimeRate:N1}% | Servicios {x.ServiceCount}"),
            "bracelet" or "bracelet report" => (await GetBraceletReportAsync(from, to, cancellationToken))
                .Select(x => $"{x.ServiceFolio} | {x.CompanyName} | {x.BoatName} | {x.BraceletFolio} | {(x.IsValidated ? "Validado" : "Pendiente")}"),
            _ => []
        };

        return NavierasReportBuilder.BuildExportPreview(reportName, from, to, lines);
    }

    private async Task<IReadOnlyList<NavierasServiceSummary>> GetFilteredServicesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
        => (await servicesService.GetServicesAsync(cancellationToken: cancellationToken))
            .Where(x => x.OperationDate >= from && x.OperationDate <= to)
            .ToArray();

    private static NavierasPassengerGrouping ParseGrouping(string grouping)
        => grouping.Trim().ToLowerInvariant() switch
        {
            "ship" or "barco" => NavierasPassengerGrouping.Ship,
            "company" or "naviera" => NavierasPassengerGrouping.Company,
            "dock" or "muelle" => NavierasPassengerGrouping.Dock,
            _ => NavierasPassengerGrouping.Service
        };
}
