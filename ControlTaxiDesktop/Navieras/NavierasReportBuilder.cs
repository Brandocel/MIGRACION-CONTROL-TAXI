using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras;

public static class NavierasReportBuilder
{
    private static readonly TimeSpan DelayTolerance = TimeSpan.FromMinutes(15);

    public static IReadOnlyList<NavierasQuickActionItem> BuildQuickActions()
        =>
        [
            new("Nuevo servicio", "Crea un servicio operativo mediante la API.", "Servicios"),
            new("Registrar llegada", "Actualiza muelle, guia, capitan y PAX reales.", "Quick Actions"),
            new("Registrar salida", "Captura la salida real y los pasajeros embarcados.", "Quick Actions"),
            new("Validar brazalete", "Confirma el folio de brazalete del servicio.", "Quick Actions"),
            new("Finalizar servicio", "Cierra la operacion del servicio seleccionado.", "Quick Actions"),
            new("Cancelar servicio", "Registra la cancelacion en la API.", "Quick Actions")
        ];

    public static IReadOnlyList<NavierasDailyReportRow> BuildDailyReport(IEnumerable<NavierasServiceSummary> services)
        => services
            .OrderBy(static x => x.ScheduledArrival)
            .ThenBy(static x => x.BoatName)
            .Select(static x => new NavierasDailyReportRow(
                x.ServiceId,
                x.ServiceFolio,
                x.OperationDate,
                x.CompanyName,
                x.BoatName,
                x.RealGuideName ?? x.ScheduledGuideName,
                x.RealCaptainName ?? x.ScheduledCaptainName,
                x.RealDockName ?? x.ScheduledDockName,
                x.ScheduledPax,
                x.RealPax,
                x.DeparturePax,
                IsArrivalDelayed(x),
                IsDepartureDelayed(x),
                x.Status))
            .ToArray();

    public static IReadOnlyList<NavierasCompanyReportItem> BuildCompanyReport(IEnumerable<NavierasServiceSummary> services)
        => services
            .GroupBy(x => new { x.CompanyId, x.CompanyName })
            .Select(static x => new NavierasCompanyReportItem(
                x.Key.CompanyId,
                x.Key.CompanyName,
                x.Select(static item => item.BoatId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                x.Count(),
                x.Sum(static item => item.ScheduledPax),
                x.Sum(static item => item.RealPax ?? 0),
                x.Sum(static item => item.DeparturePax ?? 0),
                x.Count(static item => IsFinalized(item.Status)),
                x.Count(static item => HasOperationalDelay(item)),
                BuildPunctualityRate(x, true),
                BuildPunctualityRate(x, false)))
            .OrderBy(static x => x.CompanyName)
            .ToArray();

    public static IReadOnlyList<NavierasBoatReportItem> BuildBoatReport(IEnumerable<NavierasServiceSummary> services)
        => services
            .GroupBy(x => new { x.BoatId, x.BoatName, x.CompanyId, x.CompanyName })
            .Select(static x => new NavierasBoatReportItem(
                x.Key.BoatId,
                x.Key.BoatName,
                x.Key.CompanyId,
                x.Key.CompanyName,
                x.Count(),
                x.Sum(static item => item.ScheduledPax),
                x.Sum(static item => item.RealPax ?? 0),
                x.Sum(static item => item.DeparturePax ?? 0),
                x.Count(static item => IsArrivalDelayed(item)),
                x.Count(static item => IsDepartureDelayed(item)),
                x.Count(static item => IsFinalized(item.Status)),
                x.GroupBy(static item => item.Status)
                    .OrderByDescending(static grp => grp.Count())
                    .ThenBy(static grp => grp.Key)
                    .Select(static grp => grp.Key)
                    .FirstOrDefault() ?? string.Empty))
            .OrderBy(static x => x.BoatName)
            .ToArray();

    public static IReadOnlyList<NavierasPersonnelReportRow> BuildGuideReport(IEnumerable<NavierasServiceSummary> services, IEnumerable<NavierasPerson> guides)
        => BuildPersonnelReport(
            services,
            guides,
            "Guia",
            static person => person.ReferenceNumber is { Length: > 0 } ? $"{person.Name} ({person.ReferenceNumber})" : person.Name,
            static service => service.ScheduledGuideId,
            static service => service.RealGuideId,
            static service => service.RealGuideName,
            static service => !string.Equals(service.RealGuideId, service.ScheduledGuideId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<NavierasPersonnelReportRow> BuildCaptainReport(IEnumerable<NavierasServiceSummary> services, IEnumerable<NavierasPerson> captains)
        => BuildPersonnelReport(
            services,
            captains,
            "Capitan",
            static person => person.Name,
            static service => service.ScheduledCaptainId,
            static service => service.RealCaptainId,
            static service => service.RealCaptainName,
            static service => !string.IsNullOrWhiteSpace(service.RealCaptainId) &&
                              !string.Equals(service.RealCaptainId, service.ScheduledCaptainId, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<NavierasPassengerReportRow> BuildPassengerReport(IEnumerable<NavierasServiceSummary> services, NavierasPassengerGrouping grouping)
        => services
            .GroupBy(service => ResolvePassengerKey(service, grouping))
            .Select(group =>
            {
                var first = group.First();
                var arrivals = group.Where(static x => x.RealPax.HasValue).ToArray();
                var departures = group.Where(static x => x.DeparturePax.HasValue).ToArray();
                return new NavierasPassengerReportRow(
                    group.Key,
                    ResolvePassengerLabel(first, grouping),
                    ResolvePassengerSecondaryLabel(first, grouping),
                    group.Sum(static x => x.ScheduledPax),
                    arrivals.Length == 0 ? null : arrivals.Sum(static x => x.RealPax ?? 0),
                    departures.Length == 0 ? null : departures.Sum(static x => x.DeparturePax ?? 0),
                    group.Count(static x => !x.RealPax.HasValue),
                    group.Count(static x => !x.DeparturePax.HasValue));
            })
            .OrderBy(static x => x.Label)
            .ToArray();

    public static IReadOnlyList<NavierasPunctualitySummaryRow> BuildPunctualityReport(IEnumerable<NavierasServiceSummary> services)
    {
        var source = services.ToArray();
        return
        [
            BuildPunctualityRow("General", source),
            ..source.GroupBy(x => x.CompanyName).OrderBy(static x => x.Key).Select(static x => BuildPunctualityRow($"Naviera: {x.Key}", x)),
            ..source.GroupBy(x => x.BoatName).OrderBy(static x => x.Key).Select(static x => BuildPunctualityRow($"Barco: {x.Key}", x))
        ];
    }

    public static IReadOnlyList<NavierasBraceletReportRow> BuildBraceletReport(IEnumerable<NavierasServiceSummary> services)
        => services
            .OrderBy(static x => x.CompanyName)
            .ThenBy(static x => x.BoatName)
            .ThenBy(static x => x.ServiceFolio)
            .Select(static x => new NavierasBraceletReportRow(
                x.ServiceId,
                x.ServiceFolio,
                x.BoatName,
                x.CompanyName,
                x.BraceletFolio ?? string.Empty,
                x.BraceletValidated,
                x.BraceletValidatedAt,
                x.Status))
            .ToArray();

    public static IReadOnlyList<NavierasDockMapRow> BuildDockMap(IEnumerable<NavierasDock> docks, IEnumerable<NavierasServiceSummary> services)
    {
        var source = services.ToArray();
        return docks
            .Select(dock =>
            {
                var dockServices = source.Where(x =>
                    string.Equals(x.ScheduledDockId, dock.DockId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.RealDockId, dock.DockId, StringComparison.OrdinalIgnoreCase)).ToArray();
                return new NavierasDockMapRow(
                    dock.DockId,
                    dock.Name,
                    dock.Zone,
                    dockServices.Length,
                    dockServices.Count(static x => IsInOperation(x.Status)),
                    dockServices.Sum(static x => x.ScheduledPax),
                    dockServices.Sum(static x => x.RealPax ?? 0));
            })
            .OrderBy(static x => x.DockName)
            .ToArray();
    }

    public static NavierasExportPreview BuildExportPreview(string reportName, DateOnly from, DateOnly to, IEnumerable<string> lines)
    {
        var normalizedName = string.IsNullOrWhiteSpace(reportName) ? "reporte" : reportName.Trim();
        var items = lines.ToArray();
        return new NavierasExportPreview(
            normalizedName,
            "Texto estructurado",
            $"navieras_{Slugify(normalizedName)}_{from:yyyyMMdd}_{to:yyyyMMdd}.txt",
            from == to ? from.ToString("dd/MM/yyyy") : $"{from:dd/MM/yyyy} - {to:dd/MM/yyyy}",
            DateTimeOffset.Now,
            items.Length,
            string.Join(Environment.NewLine, items));
    }

    public static bool HasOperationalDelay(NavierasServiceSummary service)
        => IsArrivalDelayed(service) || IsDepartureDelayed(service) || string.Equals(service.Status, "retrasado", StringComparison.OrdinalIgnoreCase);

    public static bool IsArrivalDelayed(NavierasServiceSummary service)
        => service.RealArrival is DateTimeOffset realArrival && realArrival - service.ScheduledArrival > DelayTolerance;

    public static bool IsDepartureDelayed(NavierasServiceSummary service)
        => service.RealDeparture is DateTimeOffset realDeparture && realDeparture - service.ScheduledDeparture > DelayTolerance;

    private static IReadOnlyList<NavierasPersonnelReportRow> BuildPersonnelReport(
        IEnumerable<NavierasServiceSummary> services,
        IEnumerable<NavierasPerson> people,
        string roleLabel,
        Func<NavierasPerson, string> displayNameSelector,
        Func<NavierasServiceSummary, string> scheduledIdSelector,
        Func<NavierasServiceSummary, string?> realIdSelector,
        Func<NavierasServiceSummary, string?> realNameSelector,
        Func<NavierasServiceSummary, bool> changedSelector)
    {
        var rows = people.ToDictionary(
            static x => x.PersonId,
            x => new NavierasPersonnelAccumulator(x.PersonId, displayNameSelector(x), roleLabel));

        foreach (var service in services)
        {
            var scheduledId = scheduledIdSelector(service);
            if (rows.TryGetValue(scheduledId, out var scheduled))
            {
                scheduled.ProgrammedServices++;
                scheduled.ProgrammedPassengers += service.ScheduledPax;
                if (IsFinalized(service.Status))
                    scheduled.FinalizedServices++;
                if (HasOperationalDelay(service))
                    scheduled.DelayedServices++;
            }

            var realId = realIdSelector(service);
            if (!string.IsNullOrWhiteSpace(realId))
            {
                if (!rows.TryGetValue(realId, out var real))
                {
                    real = new NavierasPersonnelAccumulator(realId, realNameSelector(service) ?? realId, roleLabel);
                    rows[realId] = real;
                }

                real.RealServices++;
                real.RealPassengers += service.RealPax ?? 0;
                if (changedSelector(service))
                    real.AssignmentChanges++;
            }
        }

        return rows.Values
            .OrderBy(static x => x.PersonName)
            .Select(static x => x.ToRow())
            .ToArray();
    }

    private static NavierasPunctualitySummaryRow BuildPunctualityRow(string label, IEnumerable<NavierasServiceSummary> services)
    {
        var source = services.ToArray();
        var arrivalAvailable = source.Where(static x => x.RealArrival.HasValue).ToArray();
        var departureAvailable = source.Where(static x => x.RealDeparture.HasValue).ToArray();

        return new NavierasPunctualitySummaryRow(
            label,
            arrivalAvailable.Length == 0 ? 0 : arrivalAvailable.Count(static x => !IsArrivalDelayed(x)) * 100d / arrivalAvailable.Length,
            departureAvailable.Length == 0 ? 0 : departureAvailable.Count(static x => !IsDepartureDelayed(x)) * 100d / departureAvailable.Length,
            AverageDelayMinutes(arrivalAvailable, true),
            AverageDelayMinutes(departureAvailable, false),
            source.Length);
    }

    private static double? BuildPunctualityRate(IEnumerable<NavierasServiceSummary> services, bool forArrival)
    {
        var source = services.Where(x => forArrival ? x.RealArrival.HasValue : x.RealDeparture.HasValue).ToArray();
        if (source.Length == 0)
            return null;

        var onTime = source.Count(x => forArrival ? !IsArrivalDelayed(x) : !IsDepartureDelayed(x));
        return onTime * 100d / source.Length;
    }

    private static double AverageDelayMinutes(IEnumerable<NavierasServiceSummary> services, bool forArrival)
    {
        var values = services
            .Select(x =>
            {
                var actual = forArrival ? x.RealArrival : x.RealDeparture;
                var scheduled = forArrival ? x.ScheduledArrival : x.ScheduledDeparture;
                return actual.HasValue ? Math.Max(0, (actual.Value - scheduled).TotalMinutes) : 0;
            })
            .Where(static x => x > 0)
            .ToArray();

        return values.Length == 0 ? 0 : values.Average();
    }

    private static string ResolvePassengerKey(NavierasServiceSummary service, NavierasPassengerGrouping grouping) => grouping switch
    {
        NavierasPassengerGrouping.Service => service.ServiceId,
        NavierasPassengerGrouping.Ship => service.BoatId,
        NavierasPassengerGrouping.Company => service.CompanyId,
        NavierasPassengerGrouping.Dock => service.RealDockId ?? service.ScheduledDockId,
        _ => service.ServiceId
    };

    private static string ResolvePassengerLabel(NavierasServiceSummary service, NavierasPassengerGrouping grouping) => grouping switch
    {
        NavierasPassengerGrouping.Service => service.ServiceFolio,
        NavierasPassengerGrouping.Ship => service.BoatName,
        NavierasPassengerGrouping.Company => service.CompanyName,
        NavierasPassengerGrouping.Dock => service.RealDockName ?? service.ScheduledDockName,
        _ => service.ServiceFolio
    };

    private static string ResolvePassengerSecondaryLabel(NavierasServiceSummary service, NavierasPassengerGrouping grouping) => grouping switch
    {
        NavierasPassengerGrouping.Service => $"{service.BoatName} / {service.CompanyName}",
        NavierasPassengerGrouping.Ship => service.CompanyName,
        NavierasPassengerGrouping.Company => service.BoatName,
        NavierasPassengerGrouping.Dock => "Servicios del muelle",
        _ => string.Empty
    };

    private static bool IsFinalized(string status)
        => string.Equals(status, "finalizado", StringComparison.OrdinalIgnoreCase);

    private static bool IsInOperation(string status)
        => status.Contains("oper", StringComparison.OrdinalIgnoreCase) ||
           status.Contains("llegada", StringComparison.OrdinalIgnoreCase);

    private static string Slugify(string value)
        => string.Concat(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

    private sealed class NavierasPersonnelAccumulator(string personId, string personName, string roleLabel)
    {
        public string PersonId { get; } = personId;
        public string PersonName { get; } = personName;
        public string RoleLabel { get; } = roleLabel;
        public int ProgrammedServices { get; set; }
        public int RealServices { get; set; }
        public int ProgrammedPassengers { get; set; }
        public int RealPassengers { get; set; }
        public int AssignmentChanges { get; set; }
        public int FinalizedServices { get; set; }
        public int DelayedServices { get; set; }

        public NavierasPersonnelReportRow ToRow()
            => new(
                PersonId,
                PersonName,
                RoleLabel,
                ProgrammedServices,
                RealServices,
                ProgrammedPassengers,
                RealPassengers,
                AssignmentChanges,
                FinalizedServices,
                DelayedServices);
    }
}
