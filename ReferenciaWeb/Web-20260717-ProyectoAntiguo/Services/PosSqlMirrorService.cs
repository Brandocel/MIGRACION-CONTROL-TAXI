using ControlTaxiWeb.Data;
using ControlTaxiWeb.Interfaces;
using System.Collections.Concurrent;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService : IPosSqlMirrorService
    {
        private static string _externalDatabaseName = "PosDb";
        private static string _externalSchemaName = "PosSchema";
        private static string _appDatabaseName = "AppDb";
        private static string _appSchemaName = "AppSchema";
        private static string _compuadmoStoreDatabaseName = "StoreCompuadmoDb";
        private static string _joyeriaStoreDatabaseName = "StoreJoyeriaDb";
        private static string _storeSchemaName = "StoreSchema";
        private static readonly ConcurrentDictionary<string, DateTime> AppMovilSyncMarks = new(StringComparer.OrdinalIgnoreCase);
        private readonly CompuadmoPlazaContext _dbContext;
        private readonly PosDbContext _posContext;
        private readonly AppDbContext _appContext;
        private readonly IAppTaxiApiClient _appTaxiApi;
        private readonly ILogger<PosSqlMirrorService> _logger;
        private readonly bool _autoSyncAppMovilFromApi;

        public PosSqlMirrorService(
            CompuadmoPlazaContext dbContext,
            PosDbContext posContext,
            AppDbContext appContext,
            IAppTaxiApiClient appTaxiApi,
            ILogger<PosSqlMirrorService> logger,
            IConfiguration configuration)
        {
            _dbContext = dbContext;
            _posContext = posContext;
            _appContext = appContext;
            _appTaxiApi = appTaxiApi;
            _logger = logger;
            _autoSyncAppMovilFromApi = configuration.GetValue<bool?>("PosSqlMirror:AutoSyncAppMovilFromApi") ?? false;
            _externalDatabaseName = CleanSqlIdentifier(configuration["DatabaseNames:Pos"] ?? configuration["PosSqlMirror:ExternalDatabase"], _externalDatabaseName);
            _externalSchemaName = CleanSqlIdentifier(configuration["DatabaseSchemas:Pos"] ?? configuration["PosSqlMirror:ExternalSchema"], _externalSchemaName);
            _appDatabaseName = CleanSqlIdentifier(configuration["DatabaseNames:App"], _appDatabaseName);
            _appSchemaName = CleanSqlIdentifier(configuration["DatabaseSchemas:App"] ?? configuration["PosSqlMirror:AppSchema"], _appSchemaName);
            _compuadmoStoreDatabaseName = CleanSqlIdentifier(configuration["DatabaseNames:Compuadmo"] ?? configuration["StoreDatabases:Compuadmo"], _compuadmoStoreDatabaseName);
            _joyeriaStoreDatabaseName = CleanSqlIdentifier(configuration["DatabaseNames:Joyeria"] ?? configuration["StoreDatabases:Joyeria"], _joyeriaStoreDatabaseName);
            _storeSchemaName = CleanSqlIdentifier(configuration["DatabaseSchemas:Store"] ?? configuration["StoreDatabases:Schema"], _storeSchemaName);
        }
    }
}
