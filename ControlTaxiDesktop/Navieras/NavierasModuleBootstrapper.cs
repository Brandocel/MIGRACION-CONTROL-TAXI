using System.Net.Http;
using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Application.Services;
using ControlTaxiDesktop.Navieras.Domain;
using ControlTaxiDesktop.Navieras.Infrastructure;
using ControlTaxiDesktop.Navieras.Infrastructure.Repositories;

namespace ControlTaxiDesktop.Navieras;

public sealed class NavierasModuleBootstrapper
{
    private readonly NavierasApiOptions _options = new();
    private readonly Func<NavierasDbContext> _contextFactory = static () => NavierasDbContextFactory.Create();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private INavierasApiClient? _apiClient;
    private INavierasModuleService? _moduleService;
    private INavierasSessionRepository? _sessionRepository;
    private INavierasCacheRepository? _cacheRepository;
    private INavierasAuditRepository? _auditRepository;
    private INavierasAuthService? _authService;
    private IServicesService? _servicesService;
    private ICatalogsService? _catalogsService;
    private IFoliosService? _foliosService;
    private ITimelineService? _timelineService;
    private IUsersService? _usersService;
    private IRolesService? _rolesService;
    private IDashboardService? _dashboardService;
    private IReportsService? _reportsService;

    public INavierasSessionRepository CreateSessionRepository() => _sessionRepository ??= new NavierasSessionRepository(_contextFactory);
    public INavierasCacheRepository CreateCacheRepository() => _cacheRepository ??= new NavierasCacheRepository(_contextFactory);
    public INavierasAuditRepository CreateAuditRepository() => _auditRepository ??= new NavierasAuditRepository(_contextFactory);

    public INavierasApiClient CreateApiClient()
        => _apiClient ??= new NavierasApiClient(_httpClient, CreateSessionRepository(), _options);

    public INavierasAuthService CreateAuthService()
        => _authService ??= new NavierasAuthService(CreateApiClient(), CreateSessionRepository(), _options);

    public INavierasModuleService CreateModuleService()
        => _moduleService ??= new NavierasModuleService(CreateApiClient(), CreateCacheRepository(), CreateAuditRepository());

    public IServicesService CreateServicesService()
        => _servicesService ??= new ServicesService(CreateModuleService());

    public ICatalogsService CreateCatalogsService()
        => _catalogsService ??= new CatalogsService(CreateModuleService());

    public IFoliosService CreateFoliosService()
        => _foliosService ??= new FoliosService(CreateModuleService());

    public ITimelineService CreateTimelineService()
        => _timelineService ??= new TimelineService(CreateModuleService());

    public IUsersService CreateUsersService()
        => _usersService ??= new UsersService(CreateModuleService());

    public IRolesService CreateRolesService()
        => _rolesService ??= new RolesService(CreateModuleService());

    public IDashboardService CreateDashboardService()
        => _dashboardService ??= new DashboardService(CreateModuleService(), CreateServicesService());

    public IReportsService CreateReportsService()
        => _reportsService ??= new ReportsService(CreateServicesService(), CreateCatalogsService());
}
