using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.ViewModels;

public sealed class NavierasWindowViewModel : NavierasViewModelBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IServicesService _servicesService;
    private readonly IFoliosService _foliosService;
    private readonly ITimelineService _timelineService;
    private readonly ICatalogsService _catalogsService;
    private readonly IUsersService _usersService;
    private readonly IRolesService _rolesService;
    private readonly IReportsService _reportsService;
    private NavierasUser? _currentUser;
    private string _sessionText = string.Empty;
    private string _statusText = string.Empty;

    public NavierasWindowViewModel(
        IDashboardService dashboardService,
        IServicesService servicesService,
        IFoliosService foliosService,
        ITimelineService timelineService,
        ICatalogsService catalogsService,
        IUsersService usersService,
        IRolesService rolesService,
        IReportsService reportsService)
    {
        _dashboardService = dashboardService;
        _servicesService = servicesService;
        _foliosService = foliosService;
        _timelineService = timelineService;
        _catalogsService = catalogsService;
        _usersService = usersService;
        _rolesService = rolesService;
        _reportsService = reportsService;
        Dashboard = new DashboardViewModel();
        Services = new ServicesViewModel();
        Operations = new OperationsViewModel();
        Catalogs = new CatalogsViewModel();
        Reports = new ReportsViewModel();
        Administration = new AdministrationViewModel();
    }

    public NavierasUser? CurrentUser
    {
        get => _currentUser;
        private set => SetField(ref _currentUser, value);
    }

    public string SessionText
    {
        get => _sessionText;
        private set => SetField(ref _sessionText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public DashboardViewModel Dashboard { get; }
    public ServicesViewModel Services { get; }
    public OperationsViewModel Operations { get; }
    public CatalogsViewModel Catalogs { get; }
    public ReportsViewModel Reports { get; }
    public AdministrationViewModel Administration { get; }

    public void SetSession(NavierasUser user, string baseUrl)
    {
        CurrentUser = user;
        SessionText = $"{user.FullName} - {user.Role} - API: {baseUrl}";
    }

    public async Task LoadAllAsync(string catalogType, CancellationToken cancellationToken = default)
    {
        StatusText = "Cargando informacion de Navieras...";
        var dashboard = await _dashboardService.GetDashboardAsync(cancellationToken);
        var indicators = await _dashboardService.GetIndicatorsAsync(cancellationToken);
        Catalogs.Companies = await _catalogsService.GetCompaniesAsync(cancellationToken);
        Catalogs.Boats = await _catalogsService.GetBoatsAsync(cancellationToken);
        Catalogs.Docks = await _catalogsService.GetDocksAsync(cancellationToken);
        Catalogs.Guides = await _catalogsService.GetGuidesAsync(cancellationToken);
        Catalogs.Captains = await _catalogsService.GetCaptainsAsync(cancellationToken);
        var services = await _servicesService.GetServicesAsync(cancellationToken: cancellationToken);
        var timeline = await _timelineService.GetTimelineAsync(cancellationToken: cancellationToken);
        var folios = await _foliosService.GetOperationFoliosAsync(cancellationToken: cancellationToken);
        var users = await _usersService.GetUsersAsync(cancellationToken);
        var roles = await _rolesService.GetRolesAsync(cancellationToken);
        var catalog = await _catalogsService.GetCatalogItemsAsync(catalogType, cancellationToken);

        Dashboard.Boats = dashboard.BoatsToday;
        Dashboard.Services = dashboard.ServicesToday;
        Dashboard.Passengers = dashboard.ScheduledPassengers;
        Dashboard.InOperation = dashboard.InOperation;
        Dashboard.Finished = dashboard.Finished;
        Dashboard.Delayed = dashboard.Delayed;

        ReplaceCollection(Dashboard.Indicators, indicators);
        ReplaceCollection(Dashboard.QuickActions, NavierasReportBuilder.BuildQuickActions());
        ReplaceCollection(Services.Services, services);
        ReplaceCollection(Dashboard.UpcomingArrivals, services.OrderBy(static x => x.ScheduledArrival).ToArray());
        ReplaceCollection(Dashboard.ShipsOfDay, NavierasReportBuilder.BuildBoatReport(services).Select(static x => new NavierasShipDayItem(x.ShipId, x.BoatName, x.CompanyName, x.ServiceCount, x.DominantStatus)));
        ReplaceCollection(Dashboard.DockMap, NavierasReportBuilder.BuildDockMap(Catalogs.Docks, services));
        ReplaceCollection(Operations.Timeline, timeline);
        ReplaceCollection(Operations.Folios, folios);
        ReplaceCollection(Administration.Users, users);
        ReplaceCollection(Administration.Roles, roles);
        ReplaceCollection(Catalogs.CatalogItems, catalog);

        await LoadReportsAsync(cancellationToken);
        StatusText = $"Navieras lista. Servicios cargados: {Services.Services.Count}.";
    }

    public async Task ReloadCatalogAsync(string catalogType, CancellationToken cancellationToken = default)
        => ReplaceCollection(Catalogs.CatalogItems, await _catalogsService.GetCatalogItemsAsync(catalogType, cancellationToken));

    public async Task LoadReportsAsync(CancellationToken cancellationToken = default)
    {
        Reports.ReportStatus = "Cargando reportes...";
        ReplaceCollection(Reports.DailyReports, await _reportsService.GetDailyReportAsync(Reports.FromDate, Reports.ToDate, cancellationToken));
        ReplaceCollection(Reports.CompanyReports, await _reportsService.GetCompanyReportAsync(Reports.FromDate, Reports.ToDate, cancellationToken));
        ReplaceCollection(Reports.ShipReports, await _reportsService.GetShipReportAsync(Reports.FromDate, Reports.ToDate, cancellationToken));
        ReplaceCollection(Reports.GuideReports, await _reportsService.GetGuideReportAsync(Reports.FromDate, Reports.ToDate, cancellationToken));
        ReplaceCollection(Reports.CaptainReports, await _reportsService.GetCaptainReportAsync(Reports.FromDate, Reports.ToDate, cancellationToken));
        ReplaceCollection(Reports.PassengerReports, await _reportsService.GetPassengerReportAsync(Reports.FromDate, Reports.ToDate, Reports.PassengerGrouping, cancellationToken));
        ReplaceCollection(Reports.PunctualityReports, await _reportsService.GetPunctualityReportAsync(Reports.FromDate, Reports.ToDate, cancellationToken));
        ReplaceCollection(Reports.BraceletReports, await _reportsService.GetBraceletReportAsync(Reports.FromDate, Reports.ToDate, cancellationToken));
        var export = await _reportsService.BuildExportPreviewAsync("daily", Reports.FromDate, Reports.ToDate, cancellationToken);
        Reports.ExportPreviewText = export.Content;
        Reports.ReportStatus = Reports.DailyReports.Count == 0 ? "Sin registros para el rango seleccionado." : "Reportes listos.";
    }
}
