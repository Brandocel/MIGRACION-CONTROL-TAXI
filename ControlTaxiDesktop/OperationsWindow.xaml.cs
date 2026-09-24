using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;
using System.Collections.ObjectModel;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop;

public partial class OperationsWindow : Window
{
    private readonly LocalDatabase _database;
    private readonly LocalOperationsRepository _operations;
    private readonly LocalPosRepository _pos;
    private readonly BranchConfigurationService _branchService = new();
    private readonly string _branchCode;
    private CascoTaxistasApiService? _cascoTaxistasApi;
    private readonly CascoTripRecordsApiService? _cascoTripRecordsApi;
    private BranchConfiguration? _currentBranch;
    private readonly DesktopOutputService _output = new();
    private readonly LocalErrorLogger _errors;
    private LocalReport? _report;
    private readonly string _user;
    private readonly List<string> _appGafetes = new();
    private readonly DispatcherTimer _appGafeteAutoCommitTimer;
    private readonly DispatcherTimer _relationSearchDebounceTimer;
    private DateTime? _appGafeteAutoCommitStart;
    private DateTime? _appGafeteAutoCommitLastChange;
    private int _appGafeteAutoCommitChangeCount;
    private readonly ObservableCollection<LocalScannedBadgeItem> _scannedBadges = [];
    private readonly ObservableCollection<LocalRegistroDiarioRow> _registroRows = [];
    private IReadOnlyList<LocalAppRecordRow> _cachedAppRecords = Array.Empty<LocalAppRecordRow>();
    private string? _cachedAppBranch;
    private IReadOnlyList<AppRecordGridRow> _cachedAppGridRows = Array.Empty<AppRecordGridRow>();
    private readonly Dictionary<string, LocalRelation> _relationDetailsCache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LocalDriver> _driverCatalog = Array.Empty<LocalDriver>();
    private IReadOnlyList<LocalDriver> _relationDriverCatalog = Array.Empty<LocalDriver>();
    private IReadOnlyList<LocalTransport> _transportCatalog = Array.Empty<LocalTransport>();
    private IReadOnlyList<RelationTransportOption> _relationTransportOptions = Array.Empty<RelationTransportOption>();
    private IReadOnlyList<RelationUnitOption> _relationUnitOptions = Array.Empty<RelationUnitOption>();
    private LocalTransport? _selectedCascoTransport;
    private LocalRelation? _loadedRelationForm;
    private IReadOnlyList<LocalExpense> _expenseCatalog = Array.Empty<LocalExpense>();
    private string? _relationVendorOptionsBranch;
    private IReadOnlyDictionary<long, IReadOnlyList<string>> _driverBadgeLookup = new Dictionary<long, IReadOnlyList<string>>();
    private IReadOnlyDictionary<long, CascoTaxistaRecord> _cascoDriverDetails = new Dictionary<long, CascoTaxistaRecord>();
    private CascoReportCenterLoadResult? _cascoReportCache;
    private string? _cascoReportCacheSiteName;
    private DateTime? _cascoReportCacheStart;
    private DateTime? _cascoReportCacheEnd;
    private IReadOnlyList<LocalRelation>? _cascoRelationCache;
    private string? _cascoRelationCacheSiteName;
    private DateTime? _cascoRelationCacheStart;
    private DateTime? _cascoRelationCacheEnd;
    private string? _cascoRelationCacheSearch;
    private System.Threading.CancellationTokenSource? _driverSearchCts;
    private System.Threading.CancellationTokenSource? _transportSearchCts;
    private readonly CancellationTokenSource _windowCts = new();
    private bool _isClosing;
    private bool _isUpdatingRelationTransport;
    private bool _autoSelectSingleRelationSearchResult;
    private bool _windowReady;
    private const int DriverInitialRowLimit = 250;
    private const int TransportInitialRowLimit = 100;
    private sealed record RelationTransportOption(LocalDriver? Driver, string Value, string Display, string Plates, string Unit);
    private sealed record RelationUnitOption(LocalDriver? Driver, string Value, string Display, string Plates, string ServiceType);
    public OperationsWindow(LocalDatabase database, string user, string branchCode, string? selectedModule = null)
    {
        _database = database;
        _operations = new LocalOperationsRepository(database);
        _pos = new LocalPosRepository(database);
        _branchCode = NormalizeBranchCode(branchCode);
        _cascoTripRecordsApi = new CascoTripRecordsApiService();
        _user = user;
        Debug.WriteLine($"Usuario de sesión: {_user}");
        Debug.WriteLine($"BranchCode recibido en constructor: {branchCode}");
        Debug.WriteLine($"BranchCode normalizado: {_branchCode}");
        _appGafeteAutoCommitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _appGafeteAutoCommitTimer.Tick += (_, _) =>
        {
            if (_appGafeteAutoCommitLastChange is null || _appGafeteAutoCommitStart is null)
                return;

            var now = DateTime.UtcNow;
            if (now - _appGafeteAutoCommitLastChange.Value < TimeSpan.FromMilliseconds(120))
                return;

            _appGafeteAutoCommitTimer.Stop();
            if (_appGafeteAutoCommitChangeCount < 3)
            {
                _appGafeteAutoCommitStart = null;
                _appGafeteAutoCommitLastChange = null;
                _appGafeteAutoCommitChangeCount = 0;
                return;
            }

            if (now - _appGafeteAutoCommitStart.Value > TimeSpan.FromMilliseconds(750))
            {
                _appGafeteAutoCommitStart = null;
                _appGafeteAutoCommitLastChange = null;
                _appGafeteAutoCommitChangeCount = 0;
                return;
            }

            _appGafeteAutoCommitChangeCount = 0;
            _appGafeteAutoCommitStart = null;
            _appGafeteAutoCommitLastChange = null;
            CommitAppGafete();
        };
        _relationSearchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(260)
        };
        _relationSearchDebounceTimer.Tick += RelationSearchDebounceTimer_Tick;
        InitializeComponent();
        ApplyWindowBounds();
        SizeChanged += (_, _) => UpdateAdaptiveActionPanels();
        BadgeScannedItems.ItemsSource = _scannedBadges;
        RegistroGrid.ItemsSource = _registroRows;
        _errors = new LocalErrorLogger(database);
        ReportStart.SelectedDate = DateTime.Today; ReportEnd.SelectedDate = DateTime.Today; ExpenseDate.SelectedDate = DateTime.Today;
        BadgeStart.SelectedDate = DateTime.Today;
        BadgeEnd.SelectedDate = DateTime.Today;
        RegistroStart.SelectedDate = DateTime.Today;
        RegistroEnd.SelectedDate = DateTime.Today;
        RelationStart.SelectedDate = DateTime.Today;
        RelationEnd.SelectedDate = DateTime.Today;
        DriverCode.IsReadOnly = false;
        InitializeBranchSelector();
        ApplySelectedModule(selectedModule);
        Tabs.SelectionChanged += Tabs_SelectionChanged;
        Loaded += async (_, _) =>
        {
            if (_isClosing)
                return;
            _windowReady = true;
            UpdateAdaptiveActionPanels();
            await RunAsync(RefreshAsync);
        };
    }

    public OperationsWindow(LocalDatabase database, string user, string? selectedModule = null)
        : this(database, user, "P28", selectedModule)
    {
    }

    private void UpdateAdaptiveActionPanels()
    {
        var useLargePanels = ActualWidth >= 1550;
        SetAdaptiveActions(BadgeActionsLarge, BadgeActionsCompact, useLargePanels);
        SetAdaptiveActions(RelationActionsLarge, RelationActionsCompact, useLargePanels);
    }

    private static void SetAdaptiveActions(UIElement largePanel, UIElement compactPanel, bool useLarge)
    {
        largePanel.Visibility = useLarge ? Visibility.Visible : Visibility.Collapsed;
        compactPanel.Visibility = useLarge ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AppGafeteEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = AppGafeteEntry.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _appGafeteAutoCommitTimer.Stop();
            _appGafeteAutoCommitStart = null;
            _appGafeteAutoCommitLastChange = null;
            _appGafeteAutoCommitChangeCount = 0;
            return;
        }

        if (text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            AppGafeteEntry.Text = text.Replace("\r", string.Empty).Replace("\n", string.Empty);
            AppGafeteEntry.CaretIndex = AppGafeteEntry.Text.Length;
            CommitAppGafete(keepFocus: true);
            return;
        }

        if (_appGafeteAutoCommitStart is null)
            _appGafeteAutoCommitStart = DateTime.UtcNow;

        _appGafeteAutoCommitLastChange = DateTime.UtcNow;
        _appGafeteAutoCommitChangeCount++;
        _appGafeteAutoCommitTimer.Stop();
        _appGafeteAutoCommitTimer.Start();
    }

    private void ApplySelectedModule(string? selectedModule)
    {
        Tabs.SelectedIndex = selectedModule switch
        {
            "Registro aplicacion movil" => 1,
            "Registro aplicación móvil" => 1,
            "Tarifas" => 2,
            "Hoteles" => 3,
            "Taxistas" => 4,
            "Gafetes" => 5,
            "Transportes" => 6,
            "Guias" or "Guías" => 7,
            "Gastos" => 8,
            "Relaciones" => 9,
            "Reportes" => 10,
            _ => 0
        };
        TitleBarText.Text = selectedModule switch
        {
            "Registro aplicacion movil" => "REGISTRO APP MOVIL",
            "Registro aplicación móvil" => "REGISTRO APP MOVIL",
            "Tarifas" => "TARIFAS",
            "Hoteles" => "HOTELES",
            "Taxistas" => "TAXISTAS",
            "Gafetes" => "LIBERACION DE GAFETES",
            "Transportes" => "TRANSPORTE",
            "Guias" or "Guías" => "GUIAS",
            "Gastos" => "GASTOS",
            "Relaciones" => "RELACION TICKET - TAXISTA v2",
            "Reportes" => "CENTRO DE REPORTES",
            _ => "REGISTRO"
        };
    }
    private async Task RefreshAsync()
    {
        if (_isClosing || _windowCts.IsCancellationRequested)
            return;

        switch (Tabs.SelectedIndex)
        {
            case 0:
                await LoadRegistroAsync();
                break;
            case 1:
                await LoadAppModuleAsync();
                break;
            case 2:
                await LoadRatesModuleAsync();
                break;
            case 3:
                await LoadHotelsModuleAsync();
                break;
            case 4:
                await LoadDriversModuleAsync();
                break;
            case 5:
                await LoadBadgesAsync();
                break;
            case 6:
                await LoadTransportsModuleAsync();
                break;
            case 7:
                GuidesGrid.ItemsSource = await _operations.GetGuidesAsync();
                break;
            case 8:
                await LoadExpensesAsync();
                break;
            case 9:
                await LoadRelationVendorOptionsAsync();
                await LoadRelationsAsync();
                break;
            case 10:
                await LoadReportsAsync();
                break;
            default:
                await LoadRegistroAsync();
                break;
        }
    }

    private static string NormalizeBranchCode(string? branchCode)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            return "P28";

        var normalized = branchCode.Trim().ToUpperInvariant();
        return normalized switch
        {
            "P28" => "P28",
            "CV" => "CV",
            "ALL" => "ALL",
            _ => "P28"
        };
    }

    private void ApplyWindowBounds()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Manual;
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;

        MinWidth = Math.Max(MinWidth, 920);
        MinHeight = Math.Max(MinHeight, 620);

        var workArea = SystemParameters.WorkArea;
        ClearValue(MaxWidthProperty);
        ClearValue(MaxHeightProperty);
        Width = Math.Min(Math.Max(Width, 980), Math.Max(MinWidth, workArea.Width - 24));
        Height = Math.Min(Math.Max(Height, 660), Math.Max(MinHeight, workArea.Height - 24));
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(12, (workArea.Height - Height) / 2);
    }

    private bool IsCascoBranch() =>
        string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase);

    private bool UsesHostingerCatalog()
    {
        var branch = _currentBranch;
        var code = branch?.Code ?? _branchCode;
        return !string.IsNullOrWhiteSpace(branch?.ApiBaseUrl)
            && (string.Equals(code, "CV", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "P28", StringComparison.OrdinalIgnoreCase));
    }

    private string CurrentBranchDisplayName() =>
        _currentBranch?.Name
        ?? (IsCascoBranch() ? "Casco Viejo" : "Plaza 28");

    private static LocalDriver MapCascoTaxistaToLocalDriver(CascoTaxistaRecord item) => new(
        item.CatalogId,
        item.CatalogId.ToString(CultureInfo.InvariantCulture),
        item.DriverName ?? string.Empty,
        item.PhoneNumber ?? string.Empty,
        item.Plate ?? string.Empty,
        item.VehicleModel ?? string.Empty,
        item.UnitNumber ?? string.Empty,
        item.ServiceType ?? string.Empty,
        item.Active ? "Activo" : "Inactivo");

    private static IReadOnlyList<LocalTransport> BuildCascoTransportRows(CascoTransportOptions options)
    {
        var rows = new List<LocalTransport>();

        void AddRows(IEnumerable<string> values, string type)
        {
            foreach (var value in values
                         .Where(text => !string.IsNullOrWhiteSpace(text))
                         .Select(text => text.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(text => text, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new LocalTransport(0, type, value, 0m, 0m, 0m, 0m, 0m, 0m, true));
            }
        }

        AddRows(options.ServiceTypes, "SERVICIO");
        AddRows(options.Sites, "SITIO");
        AddRows(options.Hotels, "HOTEL");

        return rows;
    }

    private async Task LoadRelationVendorOptionsAsync()
    {
        var branchCode = _currentBranch?.Code ?? _branchCode;
        var branchKey = string.Join("|", branchCode, _currentBranch?.SqlServer, _currentBranch?.CompuadmoDatabase, _currentBranch?.JoyeriaDatabase);
        if (string.Equals(_relationVendorOptionsBranch, branchKey, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (string.Equals(branchCode, "P28", StringComparison.OrdinalIgnoreCase))
            {
                var plazaVendors = await LoadPlaza28VendorNamesAsync();
                RelationVendor.ItemsSource = plazaVendors;
                _relationDriverCatalog = await LoadPlaza28RelationDriversAsync();
                RelationDriver.ItemsSource = BuildRelationDriverLookup(_relationDriverCatalog);
                RefreshRelationTransportOptions();
                _relationVendorOptionsBranch = branchKey;
                return;
            }

            if (!string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase))
            {
                RelationVendor.ItemsSource = null;
                RelationDriver.ItemsSource = null;
                RelationTransportType.ItemsSource = null;
                RelationUnit.ItemsSource = null;
                _relationDriverCatalog = Array.Empty<LocalDriver>();
                _relationTransportOptions = Array.Empty<RelationTransportOption>();
                _relationUnitOptions = Array.Empty<RelationUnitOption>();
                _relationVendorOptionsBranch = null;
                return;
            }

            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(password) && CascoCredentialStore.TryApplyToEnvironment(out _))
                password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(password))
                return;

            var provider = new CascoSalesDataProvider(_currentBranch ?? _branchService.GetBranch("CV"));
            var vendors = await provider.GetVendorNamesAsync(password);
            RelationVendor.ItemsSource = vendors;
            _relationVendorOptionsBranch = branchKey;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No se pudo cargar lista de vendedores {branchCode}: {ex.Message}");
        }
    }

    private static async Task<IReadOnlyList<string>> LoadPlaza28VendorNamesAsync()
    {
        var source = LocalSqlServerSource.TryLoad();
        if (source is null)
            return Array.Empty<string>();

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var compuadmo = await source.OpenCompuadmoAsync())
        {
            foreach (var name in await ReadSqlVendorNamesAsync(compuadmo))
                names.Add(name);
        }

        await using (var joyeria = await source.OpenJoyeriaAsync())
        {
            foreach (var name in await ReadSqlVendorNamesAsync(joyeria))
                names.Add(name);
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalDriver>> LoadPlaza28RelationDriversAsync()
    {
        try
        {
            return (await _operations.GetDriversAsync())
                .Where(d => !string.IsNullOrWhiteSpace(d.Name))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.ServiceType, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No se pudo cargar lista de taxistas Plaza28: {ex.Message}");
            return Array.Empty<LocalDriver>();
        }
    }

    private static IReadOnlyList<LocalDriver> BuildRelationDriverLookup(IReadOnlyList<LocalDriver> drivers)
    {
        return drivers
            .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.ServiceType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Plates, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Unit, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> ReadSqlVendorNamesAsync(SqlConnection connection)
    {
        if (!await SqlTableExistsAsync(connection, "vendedor") || !await SqlColumnExistsAsync(connection, "vendedor", "Nombre"))
            return Array.Empty<string>();

        var hasLastName = await SqlColumnExistsAsync(connection, "vendedor", "Apellidos");
        var nameExpression = hasLastName
            ? """
              NULLIF(LTRIM(RTRIM(
                CASE
                  WHEN NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200), Apellidos))), N'') IS NULL
                    THEN CONVERT(nvarchar(200), Nombre)
                  ELSE CONCAT(CONVERT(nvarchar(200), Nombre), N' ', CONVERT(nvarchar(200), Apellidos))
                END)), N'')
              """
            : "NULLIF(LTRIM(RTRIM(CONVERT(nvarchar(200), Nombre))), N'')";

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 45;
        command.CommandText = $"""
            SELECT DISTINCT {nameExpression} AS NombreVendedor
            FROM dbo.vendedor
            WHERE {nameExpression} IS NOT NULL
            ORDER BY NombreVendedor;
            """;

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var value = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(value.Trim());
        }

        return result;
    }

    private static async Task<bool> SqlTableExistsAsync(SqlConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN OBJECT_ID(@tableName, N'U') IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 256).Value = "dbo." + table;
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> SqlColumnExistsAsync(SqlConnection connection, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add("@tableName", SqlDbType.NVarChar, 256).Value = "dbo." + table;
        command.Parameters.Add("@columnName", SqlDbType.NVarChar, 128).Value = column;
        return Convert.ToInt32(await command.ExecuteScalarAsync() ?? 0, CultureInfo.InvariantCulture) == 1;
    }

    private async Task LoadExpensesAsync()
    {
        if (IsCascoBranch())
        {
            var selectedDay = (ExpenseDate.SelectedDate ?? DateTime.Today).Date;
            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            var rows = await CascoOperationsDataService.LoadRelationsAsync(
                _currentBranch,
                _branchCode,
                password,
                start: selectedDay,
                end: selectedDay,
                cancellationToken: _windowCts.Token);

            _expenseCatalog = rows
                .Where(x => (x.Payout ?? 0m) > 0m)
                .Select((row, index) => new LocalExpense(
                    index + 1,
                    ParseExpenseDate(row.DateText, selectedDay),
                    string.IsNullOrWhiteSpace(row.OperationFolio) ? row.DisplayLocalFolio : row.OperationFolio,
                    "Dejada",
                    row.Payout ?? 0m,
                    BuildCascoExpenseNotes(row),
                    string.IsNullOrWhiteSpace(row.PayoutStatus) ? "Activo" : row.PayoutStatus,
                    string.IsNullOrWhiteSpace(row.PayoutUser) ? _user : row.PayoutUser))
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Folio, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ExpensesGrid.ItemsSource = _expenseCatalog;
            return;
        }

        ExpenseDate.SelectedDate ??= DateTime.Today;
        _expenseCatalog = await _operations.GetExpensesAsync(ExpenseDate.SelectedDate.Value);
        ExpensesGrid.ItemsSource = _expenseCatalog;
    }

    private static DateTime ParseExpenseDate(string? text, DateTime fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant)
            ? invariant
            : DateTime.TryParse(text, new CultureInfo("es-MX"), DateTimeStyles.AllowWhiteSpaces, out var mexican)
                ? mexican
                : DateTime.TryParse(text, out var generic)
                    ? generic
                    : fallback;
    }

    private static string BuildCascoExpenseNotes(LocalRelation row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Hotel))
            parts.Add(row.Hotel);
        if (!string.IsNullOrWhiteSpace(row.TransportType))
            parts.Add(row.TransportType);
        if (!string.IsNullOrWhiteSpace(row.Badge))
            parts.Add($"Gafete {row.Badge}");
        if (!string.IsNullOrWhiteSpace(row.PosFolio))
            parts.Add($"Ticket {row.PosFolio}");
        return string.Join(" | ", parts);
    }

    private void ConfigureTransportGridForBranch(bool casco)
    {
        if (TransportsGrid.Columns.Count < 8)
            return;

        TransportsGrid.Columns[0].Header = casco ? "Origen" : "Clave";
        TransportsGrid.Columns[1].Header = casco ? "Valor real" : "Nombre";

        for (var index = 2; index < TransportsGrid.Columns.Count; index++)
            TransportsGrid.Columns[index].Visibility = casco ? Visibility.Collapsed : Visibility.Visible;
    }

    private CascoTaxistaUpsertRequest BuildCascoTaxistaRequest(CascoTaxistaRecord? current)
    {
        var isActive = string.Equals(SelectedText(DriverStatus), "Activo", StringComparison.OrdinalIgnoreCase);
        return new CascoTaxistaUpsertRequest(
            current?.BadgeId ?? string.Empty,
            (DriverName.Text ?? string.Empty).Trim(),
            (DriverPhone.Text ?? string.Empty).Trim(),
            (DriverPlates.Text ?? string.Empty).Trim(),
            current?.VehicleModel ?? "TAXI",
            (DriverUnit.Text ?? string.Empty).Trim(),
            (DriverService.Text ?? string.Empty).Trim(),
            current?.Site ?? string.Empty,
            current?.Hotel ?? string.Empty,
            current?.Notes ?? string.Empty,
            isActive);
    }

    private async Task SaveCascoDriverAsync()
    {
        if (_cascoTaxistasApi is null)
            throw new InvalidOperationException("El servicio de catalogo de Hostinger no esta disponible.");

        var current = DriversGrid.SelectedItem is LocalDriver selectedDriver && _cascoDriverDetails.TryGetValue(selectedDriver.Id, out var selectedRecord)
            ? selectedRecord
            : TryResolveCascoDriverFromForm();

        var request = BuildCascoTaxistaRequest(current);
        if (string.IsNullOrWhiteSpace(request.DriverName))
            throw new InvalidOperationException("El nombre del taxista es obligatorio.");

        var wantsActive = string.Equals(SelectedText(DriverStatus), "Activo", StringComparison.OrdinalIgnoreCase);
        CascoTaxistaRecord? saved = null;
        if (current is not null && !wantsActive)
        {
            await _cascoTaxistasApi.DeleteTaxistaAsync(current.CatalogId, _windowCts.Token);
        }
        else if (current is null)
        {
            saved = await _cascoTaxistasApi.CreateTaxistaAsync(request, _windowCts.Token);
        }
        else
        {
            saved = await _cascoTaxistasApi.UpdateTaxistaAsync(current.CatalogId, request, _windowCts.Token);
        }

        await LoadDriversModuleAsync();
        if (saved is null)
        {
            ClearDriverForm();
            return;
        }

        var refreshed = _driverCatalog.FirstOrDefault(driver => driver.Id == saved.CatalogId)
            ?? MapCascoTaxistaToLocalDriver(saved);
        DriversGrid.SelectedItem = refreshed;
        DriversGrid.ScrollIntoView(refreshed);
        LoadDriverIntoForm(refreshed);
    }

    private CascoTaxistaRecord? TryResolveCascoDriverFromForm()
    {
        if (long.TryParse((DriverCode.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var catalogId)
            && _cascoDriverDetails.TryGetValue(catalogId, out var exact))
        {
            return exact;
        }

        var name = (DriverName.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return _cascoDriverDetails.Values.FirstOrDefault(item =>
            string.Equals(item.DriverName, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.UnitNumber ?? string.Empty, (DriverUnit.Text ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async void Tabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isClosing || !_windowReady || !IsLoaded)
            return;

        if (!ReferenceEquals(sender, Tabs) || !ReferenceEquals(e.Source, Tabs))
            return;

        await RunAsync(RefreshAsync);
    }

    private void InitializeBranchSelector()
    {
        var branches = _branchService.GetAllBranches().Where(b => !string.Equals(b.Code, "ALL", StringComparison.OrdinalIgnoreCase)).ToArray();
        BranchSelector.ItemsSource = branches;
        var selectedBranch = _branchCode == "CV"
            ? branches.FirstOrDefault(b => b.Code.Equals("CV", StringComparison.OrdinalIgnoreCase))
            : branches.FirstOrDefault(b => b.Code.Equals("P28", StringComparison.OrdinalIgnoreCase));
        selectedBranch ??= branches.First();
        BranchSelector.SelectedItem = selectedBranch;
        BranchSelector.IsEnabled = _branchCode == "ALL";
        ApplyBranch(selectedBranch);
    }

    private void ApplyBranch(BranchConfiguration branch)
    {
        _currentBranch = branch;
        var isPlaza28 = string.Equals(branch.Code, "P28", StringComparison.OrdinalIgnoreCase);
        _cascoTaxistasApi = UsesHostingerCatalog()
            ? new CascoTaxistasApiService(branch.ApiBaseUrl)
            : null;
        AppBranchLabel.Text = $"Sucursal asignada: {branch.Name}";
        BranchModeIndicator.Visibility = branch.IsReadOnly ? Visibility.Visible : Visibility.Collapsed;
        RelationNoShowCard.Visibility = isPlaza28 ? Visibility.Visible : Visibility.Collapsed;
        RelationPlatesPanel.Visibility = isPlaza28 ? Visibility.Visible : Visibility.Collapsed;
        RelationUnitPanel.Visibility = isPlaza28 ? Visibility.Visible : Visibility.Collapsed;
        if (!isPlaza28)
        {
            RelationNoShowCount.Text = "0";
            RelationPlates.Clear();
            RelationUnit.SelectedItem = null;
            RelationUnit.Text = string.Empty;
        }
        SaveAppRecordButton.IsEnabled = !branch.IsReadOnly;
        LoadAppDriverButton.IsEnabled = !branch.IsReadOnly;
        AppInputsPanel.IsEnabled = !branch.IsReadOnly;
        Debug.WriteLine($"Sucursal activa: {branch.Code} / {branch.Name}");
    }

    private string? GetCurrentSiteName()
    {
        if (string.Equals(_currentBranch?.Code, "CV", StringComparison.OrdinalIgnoreCase))
            return _currentBranch?.SiteName;

        if (string.Equals(_currentBranch?.Code, "ALL", StringComparison.OrdinalIgnoreCase))
            return null;

        return _currentBranch?.SiteName;
    }

    private void BranchSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isClosing)
            return;

        if (BranchSelector.SelectedItem is not BranchConfiguration branch)
            return;

        if (_branchCode != "ALL")
            return;

        ApplyBranch(branch);
        _ = RunAsync(LoadAppModuleAsync);
    }

    private async Task LoadAppModuleAsync()
    {
        _windowCts.Token.ThrowIfCancellationRequested();
        var rates = await _operations.GetRatesAsync();
        var hotels = await _operations.GetHotelsAsync();
        var drivers = await _operations.GetDriversAsync();
        if (_isClosing || _windowCts.IsCancellationRequested)
            return;
        AppTaxistaEncontrado.ItemsSource = drivers;
        AppHotel.ItemsSource = hotels;
        AppTarifa.ItemsSource = rates;

        ResetAppModuleVisualFilters();
        AssignAppRecordsGridItemsSource(nameof(LoadAppModuleAsync), "ninguno", null);
        var currentBranch = _currentBranch ?? _branchService.GetBranch(_branchCode);
        var branchCodeForLogging = currentBranch?.Code ?? _branchCode;
        Debug.WriteLine($"BranchCode sesión: {_branchCode}");
        Debug.WriteLine($"Sucursal activa: {branchCodeForLogging}");

        var currentBranchCode = currentBranch?.Code ?? _branchCode;
        if (_branchCode == "CV" && currentBranchCode != "CV")
            throw new InvalidOperationException("Seguridad de sucursal: un usuario CV intentó cargar Plaza 28.");

        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;

        if (string.Equals(currentBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            AppRecordsGrid.ItemsSource = null;
            var cascoData = await CascoOperationsDataService.LoadAsync(
                currentBranch,
                _branchCode,
                password,
                start: DateTime.Today,
                end: DateTime.Today,
                cancellationToken: _windowCts.Token);
            if (_isClosing || _windowCts.IsCancellationRequested)
                return;
            var rows = cascoData.AppGridRows;
            _cachedAppBranch = currentBranchCode;
            _cachedAppRecords = cascoData.SourceRows;
            _cachedAppGridRows = rows;
            AssignAppRecordsGridItemsSource(nameof(LoadAppModuleAsync), cascoData.Provider, rows);
            Debug.WriteLine($"Cantidad final en AppRecordsGrid: {rows.Count}");
            Debug.WriteLine($"Tipo de fila: {rows.FirstOrDefault()?.GetType().FullName ?? "ninguno"}");
            Debug.WriteLine($"Fila 1: folioOriginal={rows.FirstOrDefault()?.FolioOriginal ?? ""}; folioLocal={rows.FirstOrDefault()?.FolioLocal ?? ""}; taxista={rows.FirstOrDefault()?.Taxista ?? ""}; sitio={rows.FirstOrDefault()?.Sitio ?? ""}");
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing || _windowCts.IsCancellationRequested)
                    return;
                var items = AppRecordsGrid.ItemsSource as IEnumerable;
                var list = items?.Cast<object?>().ToList() ?? [];
                Debug.WriteLine($"[Dispatcher] AppRecordsGrid count={list.Count}; firstType={list.FirstOrDefault()?.GetType().FullName ?? "ninguno"}; firstFolioOriginal={GetGridRowValue(list.FirstOrDefault(), "FolioOriginal")}; firstFolioLocal={GetGridRowValue(list.FirstOrDefault(), "FolioLocal")}; firstTaxista={GetGridRowValue(list.FirstOrDefault(), "Taxista")}; firstSitio={GetGridRowValue(list.FirstOrDefault(), "Sitio")}");
            }));
            AppBranchLabel.Text = $"Sucursal asignada: {currentBranch?.Name ?? currentBranchCode} · Casco Viejo: {rows.Count} registros cargados.";
            return;
        }

        var records = await ResolveAppRecordsAsync(_operations, currentBranch, _branchCode, password);
        Debug.WriteLine("Proveedor seleccionado: Plaza28");
        AssignAppRecordsGridItemsSource(nameof(LoadAppModuleAsync), "LocalOperationsRepository", records);
        Debug.WriteLine($"Cantidad de registros cargados: {records?.Count ?? 0}");
    }

    private void ResetAppModuleVisualFilters()
    {
        AppTaxistaNombre.Text = string.Empty;
        AppTaxistaEncontrado.SelectedItem = null;
        AppHotel.SelectedItem = null;
        AppRecordsGrid.SelectedItem = null;
    }

    private void AssignAppRecordsGridItemsSource(string methodName, string provider, object? rows)
    {
        var itemList = rows is System.Collections.IEnumerable enumerable ? enumerable.Cast<object?>() : Enumerable.Empty<object?>();
        var rowCount = itemList.Count();
        var firstItem = itemList.FirstOrDefault();
        var firstFolioOriginal = GetGridRowValue(firstItem, "FolioOriginal") ?? string.Empty;
        var firstFolioLocal = GetGridRowValue(firstItem, "FolioLocal") ?? GetGridRowValue(firstItem, "FolioControl") ?? string.Empty;
        var firstTaxista = GetGridRowValue(firstItem, "Taxista") ?? GetGridRowValue(firstItem, "DriverName") ?? string.Empty;
        var firstSitio = GetGridRowValue(firstItem, "Sitio") ?? string.Empty;
        Debug.WriteLine($"[{methodName}] AppRecordsGrid.ItemsSource = | branch={_branchCode}/{_currentBranch?.Code ?? "n/a"} | provider={provider} | count={rowCount} | firstFolioOriginal={firstFolioOriginal} | firstFolioLocal={firstFolioLocal} | firstTaxista={firstTaxista} | firstSitio={firstSitio}");
        AppRecordsGrid.ItemsSource = rows as System.Collections.IEnumerable;
    }

    public static async Task<IReadOnlyList<AppRecordGridRow>> LoadCascoGridRowsAsync(BranchConfiguration? currentBranch, string branchCode, string sqlPassword)
    {
        var result = await CascoOperationsDataService.LoadAsync(currentBranch, branchCode, sqlPassword);
        return result.AppGridRows;
    }

    private static string? GetGridRowValue(object? row, string propertyName)
    {
        if (row is null)
            return null;

        return row.GetType().GetProperty(propertyName)?.GetValue(row)?.ToString();
    }

    internal static async Task<IReadOnlyList<LocalAppRecordRow>> ResolveAppRecordsAsync(LocalOperationsRepository operations, BranchConfiguration? currentBranch, string branchCode, string sqlPassword)
    {
        var resolvedBranchCode = currentBranch?.Code ?? branchCode;
        if (branchCode == "CV" && resolvedBranchCode != "CV")
            throw new InvalidOperationException("Seguridad de sucursal: un usuario CV intentó cargar Plaza 28.");

        if (resolvedBranchCode == "CV")
        {
            var provider = new CascoReadOnlyDataProvider(currentBranch ?? new BranchConfigurationService().GetBranch("CV"));
            return await provider.GetAppRecordsAsync(sqlPassword);
        }

        if (branchCode == "CV" || resolvedBranchCode == "CV")
            throw new InvalidOperationException("Usuario CV intentó cargar datos de Plaza 28.");

        return await operations.GetAppRecordsAsync();
    }

    private async Task LoadRatesModuleAsync() => RatesGrid.ItemsSource = await _operations.GetRatesAsync();
    private async Task LoadHotelsModuleAsync() => HotelsGrid.ItemsSource = await _operations.GetHotelsAsync();

    private async Task LoadDriversModuleAsync()
    {
        var useHostingerCatalog = UsesHostingerCatalog();
        DriverCode.IsReadOnly = useHostingerCatalog;
        if (useHostingerCatalog)
        {
            var search = (DriverSearch.Text ?? string.Empty).Trim();
            var isPlaza28 = string.Equals(_currentBranch?.Code ?? _branchCode, "P28", StringComparison.OrdinalIgnoreCase);
            var maxRecords = isPlaza28 ? DriverInitialRowLimit : int.MaxValue;

            DriverResultsText.Text = "Cargando taxistas desde Hostinger...";
            var records = await (_cascoTaxistasApi?.GetTaxistasAsync(search, maxRecords, _windowCts.Token)
                ?? Task.FromResult<IReadOnlyList<CascoTaxistaRecord>>(Array.Empty<CascoTaxistaRecord>()));

            _cascoDriverDetails = records.ToDictionary(item => item.CatalogId);
            _driverCatalog = records
                .Select(MapCascoTaxistaToLocalDriver)
                .OrderBy(driver => driver.Name)
                .ThenBy(driver => driver.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _driverBadgeLookup = records
                .Where(item => !string.IsNullOrWhiteSpace(item.BadgeId))
                .ToDictionary(
                    item => item.CatalogId,
                    item => (IReadOnlyList<string>)new[] { item.BadgeId.Trim() });

            await ApplyDriverSearchAsync();
            return;
        }

        var payload = await Task.Run(async () =>
        {
            var drivers = await _operations.GetDriversAsync();
            var badges = await _operations.GetDriverBadgeLookupAsync();
            return (Drivers: drivers, Badges: badges);
        });
        _driverCatalog = payload.Drivers;
        _driverBadgeLookup = payload.Badges;
        await ApplyDriverSearchAsync();
    }

    private async Task LoadTransportsModuleAsync()
    {
        var useHostingerCatalog = UsesHostingerCatalog();
        ConfigureTransportGridForBranch(useHostingerCatalog);
        if (useHostingerCatalog)
        {
            var options = await (_cascoTaxistasApi?.GetTransportOptionsAsync(_windowCts.Token)
                ?? Task.FromResult(new CascoTransportOptions()));
            _transportCatalog = BuildCascoTransportRows(options);
            await ApplyTransportSearchAsync();
            return;
        }

        _transportCatalog = await Task.Run(async () => await _operations.GetTransportsAsync());
        await ApplyTransportSearchAsync();
    }
    private async void SaveRate_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { await _operations.SaveRateAsync(new LocalRate(0, RateType.Text, RateName.Text, Number(RatePayout.Text), Number(RateMin.Text), Number(RateMax.Text), true)); await RefreshAsync(); });
    private async void SaveHotel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { await _operations.SaveHotelAsync(HotelName.Text); await RefreshAsync(); });
    private async void SaveDriver_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (UsesHostingerCatalog())
        {
            await SaveCascoDriverAsync();
            return;
        }

        await _operations.SaveDriverAsync(new LocalDriver(0, DriverCode.Text, DriverName.Text, DriverPhone.Text, DriverPlates.Text, "", DriverUnit.Text, DriverService.Text, SelectedText(DriverStatus)));
        await RefreshAsync();
        ApplyDriverMode(false);
    });
    private async void SaveTransport_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (UsesHostingerCatalog())
        {
            var selected = _selectedCascoTransport ?? TransportsGrid.SelectedItem as LocalTransport
                ?? throw new InvalidOperationException($"Selecciona primero el transporte de {CurrentBranchDisplayName()} que quieres editar.");

            var origin = selected.Code.Trim();
            var currentValue = selected.Name.Trim();
            var newValue = (TransportName.Text ?? string.Empty).Trim();
            var formOrigin = (TransportCode.Text ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(newValue))
                throw new InvalidOperationException("Captura el nombre del transporte antes de actualizar.");

            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(currentValue))
                throw new InvalidOperationException("El transporte seleccionado no tiene origen o valor valido.");

            if (!string.Equals(formOrigin, origin, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("No cambies la clave/tipo. Solo se edita el valor real del transporte seleccionado.");

            var confirm = WebDialogWindow.Confirm(
                this,
                $"Se actualizara {origin}: \"{currentValue}\" por \"{newValue}\" en el catalogo real de {CurrentBranchDisplayName()}.",
                "Actualizar transporte",
                "?",
                "ACTUALIZAR",
                "CANCELAR");

            if (!confirm)
                return;

            var result = await (_cascoTaxistasApi?.UpdateTransportOptionAsync(
                    new CascoTransportUpdateRequest(origin, currentValue, newValue),
                    _windowCts.Token)
                ?? throw new InvalidOperationException("El servicio de catalogo de Hostinger no esta disponible."));

            WebDialogWindow.Show(
                this,
                $"Hostinger actualizo {result.Updated:N0} registro(s) de {result.Origin}.",
                "Transporte actualizado",
                "OK");

            await LoadTransportsModuleAsync();
            TransportSearch.Text = newValue;
            await ApplyTransportSearchAsync();
            var refreshed = _transportCatalog.FirstOrDefault(transport =>
                string.Equals(transport.Code, origin, StringComparison.OrdinalIgnoreCase)
                && string.Equals(transport.Name, newValue, StringComparison.OrdinalIgnoreCase));
            if (refreshed is not null)
            {
                TransportsGrid.SelectedItem = refreshed;
                TransportsGrid.ScrollIntoView(refreshed);
                LoadTransportIntoForm(refreshed);
            }
            else
            {
                ApplyTransportMode(false);
            }
            return;
        }

        await _operations.SaveTransportAsync(new LocalTransport(0, TransportCode.Text, TransportName.Text, Number(TransportMin.Text), Number(TransportMax.Text), Number(TransportCommission.Text), Number(TransportCash.Text), Number(TransportCard.Text), Number(TransportAmex.Text), true));
        await RefreshAsync();
        ApplyTransportMode(false);
    });
    private async void SaveGuide_Click(object sender, RoutedEventArgs e) => await RunAsync(async () => { await _operations.SaveGuideAsync(new LocalGuide(0, GuideCode.Text, GuideName.Text, GuidePhone.Text, Number(GuideCommission.Text), SelectedText(GuideStatus))); await RefreshAsync(); });
    private async void SaveExpense_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (IsCascoBranch())
            throw new InvalidOperationException("Gastos de Casco Viejo esta en modo solo lectura en esta vista.");

        await _operations.SaveExpenseAsync(new LocalExpense(0, ExpenseDate.SelectedDate ?? DateTime.Today, ExpenseFolio.Text, ExpenseConcept.Text, Number(ExpenseAmount.Text), ExpenseNotes.Text, "Activo", _user));
        await RefreshAsync();
    });
    private async void SaveRelation_Click(object sender, RoutedEventArgs e) => await RunAsync(SaveRelationCoreAsync);

    /// <summary>
    /// La unidad y el taxista son obligatorios. Sin unidad el folio se queda sin regla de
    /// comision y termina cobrandose el 10 % por omision, que es de donde salieron varias de las
    /// diferencias del cuadre; y sin taxista no hay a quien pagarle. Un espacio en blanco no
    /// cuenta como capturado. Pedido por operacion el 2026-09-03.
    /// </summary>
    private static void EnsureRelationIsComplete(string? driver, string? transportType)
    {
        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(driver)) faltantes.Add("el nombre del taxista");
        if (string.IsNullOrWhiteSpace(transportType)) faltantes.Add("la unidad (tipo de transporte)");

        if (faltantes.Count > 0)
        {
            throw new InvalidOperationException(
                "Falta capturar " + string.Join(" y ", faltantes)
                + ". Sin ese dato la comisión no se puede calcular bien, así que no se guarda la relación.");
        }
    }

    private async Task SaveRelationCoreAsync()
    {
        EnsureRelationIsComplete(RelationDriver.Text, RelationTransportType.Text);

        var relation = BuildRelationFromForm(_loadedRelationForm ?? new LocalRelation(
            0,
            RelationAppFolio.Text,
            RelationOperationFolio.Text,
            RelationPosFolio.Text,
            RelationBadge.Text,
            RelationDriver.Text,
            RelationVendor.Text,
            NumberOrNull(RelationPayout.Text),
            RelationNotes.Text));

        var isCasco = string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase);
        if (isCasco)
        {
            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            await CascoOperationsDataService.SaveRelationAsync(
                _currentBranch,
                _branchCode,
                password,
                relation,
                _user,
                _windowCts.Token);
            await UpdateCascoHostingerRecordAsync(relation);
            InvalidateCascoRelationCache();
            await LoadRelationsAsync();
            var saved = (RelationsGrid.ItemsSource as IEnumerable<LocalRelation>)?
                .FirstOrDefault(x =>
                    string.Equals(x.OperationFolio, relation.OperationFolio, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.AppFolio, relation.AppFolio, StringComparison.OrdinalIgnoreCase));
            LoadRelationIntoForm(saved ?? relation);
            return;
        }
        else
        {
            await _operations.SaveRelationAsync(relation);
            await UpdatePlazaHostingerRecordAsync(relation);
        }

        await RefreshAsync();
        LoadRelationIntoForm(relation);
    }

    private async void ConfirmSaveRelation_Click(object sender, RoutedEventArgs e)
    {
        var confirm = WebDialogWindow.Confirm(
            this,
            "¿Seguro que quieres actualizar esta relación?",
            "Actualizar relación",
            "?",
            "SI, ACTUALIZAR",
            "CANCELAR");

        if (!confirm)
            return;

        await RunAsync(SaveRelationCoreAsync);
    }

    private async Task UpdateCascoHostingerRecordAsync(LocalRelation relation)
    {
        if (_currentBranch is null || !string.Equals(_currentBranch.Code, "CV", StringComparison.OrdinalIgnoreCase))
            return;

        var recordId = FirstFilled(relation.AppFolio, relation.OperationFolio);
        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("Se requiere el folio original para actualizar Hostinger.");

        var api = new CascoTripRecordsApiService(_currentBranch.ApiBaseUrl);
        await api.UpdateTripRecordAsync(
            new CascoTripRecordUpdateRequest(
                recordId.Trim(),
                NullIfWhiteSpace(relation.Badge),
                NullIfWhiteSpace(relation.Driver),
                NullIfWhiteSpace(relation.Nationality),
                NullIfWhiteSpace(relation.Unit),
                NullIfWhiteSpace(relation.Hotel),
                NullIfWhiteSpace(relation.Origin),
                NullIfWhiteSpace(_currentBranch.SiteName),
                NullIfWhiteSpace(relation.Destination),
                NullIfWhiteSpace(relation.Vendor),
                Math.Max(0, relation.AdultPassengers),
                Math.Max(0, relation.YouthPassengers),
                Math.Max(0, relation.ChildPassengers),
                Math.Max(0, relation.NoShowCount),
                Math.Max(0, relation.Passengers),
                NullIfWhiteSpace(relation.TransportType),
                relation.Payout ?? 0m,
                NullIfWhiteSpace(relation.PaymentMethod),
                NullIfWhiteSpace(relation.Notes),
                NullIfWhiteSpace(_currentBranch.SiteName),
                NullIfWhiteSpace(_currentBranch.Code),
                NullIfWhiteSpace(_currentBranch.Name)),
            _windowCts.Token);
    }

    private async Task UpdatePlazaHostingerRecordAsync(LocalRelation relation)
    {
        if (_currentBranch is null || !string.Equals(_currentBranch.Code, "P28", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(_currentBranch.ApiBaseUrl))
            return;

        var recordId = FirstFilled(relation.AppFolio, relation.OperationFolio);
        if (string.IsNullOrWhiteSpace(recordId))
            throw new InvalidOperationException("Se requiere el folio para actualizar Hostinger.");

        var api = new PlazaTripRecordsApiService(_currentBranch.ApiBaseUrl);
        await api.UpdateTripRecordAsync(
            new CascoTripRecordUpdateRequest(
                recordId.Trim(),
                NullIfWhiteSpace(relation.Badge),
                NullIfWhiteSpace(relation.Driver),
                NullIfWhiteSpace(relation.Nationality),
                NullIfWhiteSpace(relation.Unit),
                NullIfWhiteSpace(relation.Hotel),
                NullIfWhiteSpace(relation.Origin),
                NullIfWhiteSpace(_currentBranch.SiteName),
                NullIfWhiteSpace(relation.Destination),
                NullIfWhiteSpace(relation.Vendor),
                Math.Max(0, relation.AdultPassengers),
                Math.Max(0, relation.YouthPassengers),
                Math.Max(0, relation.ChildPassengers),
                Math.Max(0, relation.NoShowCount),
                Math.Max(0, relation.Passengers),
                NullIfWhiteSpace(relation.TransportType),
                relation.Payout ?? 0m,
                NullIfWhiteSpace(relation.PaymentMethod),
                NullIfWhiteSpace(relation.Notes),
                NullIfWhiteSpace(_currentBranch.SiteName),
                "28",
                NullIfWhiteSpace(_currentBranch.Name),
                ParseCatalogIdOrNull(relation.TaxistaId)),
            _windowCts.Token);
    }

    private static int? ParseCatalogIdOrNull(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return null;

        return parsed > 0 ? parsed : null;
    }
    private void RelationSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _relationSearchDebounceTimer.Stop();
        _relationSearchDebounceTimer.Start();
    }

    private void RelationSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        _autoSelectSingleRelationSearchResult = string.Equals(_currentBranch?.Code ?? _branchCode, "P28", StringComparison.OrdinalIgnoreCase);
        _relationSearchDebounceTimer.Stop();
        RefreshRelations_Click(RelationSearch, new RoutedEventArgs());
    }

    private async void RelationSearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _relationSearchDebounceTimer.Stop();
        await RunAsync(LoadRelationsAsync);
    }

    private async void RefreshRelations_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadRelationsAsync);
    private async void ConfirmRefreshRelations_Click(object sender, RoutedEventArgs e)
    {
        var confirm = WebDialogWindow.Confirm(
            this,
            "¿Seguro que quieres actualizar la lista de relaciones?",
            "Actualizar relaciones",
            "?",
            "SI, ACTUALIZAR",
            "CANCELAR");

        if (!confirm)
            return;

        await RunAsync(LoadRelationsAsync);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshAsync);
    private void OpenRelations_Click(object sender, RoutedEventArgs e) => ApplySelectedModule("Relaciones");
    private async void SearchDriver_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (UsesHostingerCatalog())
        {
            await LoadDriversModuleAsync();
            return;
        }

        await ApplyDriverSearchAsync();
    });
    private async void SearchTransport_Click(object sender, RoutedEventArgs e) => await RunAsync(ApplyTransportSearchAsync);
    private void ClearDriver_Click(object sender, RoutedEventArgs e) => ClearDriverForm();
    private void ClearTransport_Click(object sender, RoutedEventArgs e) => ClearTransportForm();
    private void EditDriver_Click(object sender, RoutedEventArgs e)
    {
        if (DriversGrid.SelectedItem is LocalDriver driver)
            LoadDriverIntoForm(driver);
    }
    private void EditTransport_Click(object sender, RoutedEventArgs e)
    {
        if (TransportsGrid.SelectedItem is LocalTransport transport)
            LoadTransportIntoForm(transport);
    }
    private void ClearRelation_Click(object sender, RoutedEventArgs e)
    {
        // NUEVO limpia el formulario y abre el cajon listo para capturar.
        OpenRelationEdit(string.Empty, string.Empty);
        _loadedRelationForm = null;
        RelationAssignedTicket.Clear();
        RelationAppFolio.Clear();
        RelationOperationFolio.Clear();
        RelationPosFolio.Clear();
        RelationBadge.Clear();
        RelationTaxistaId.Clear();
        RelationDriver.Text = string.Empty;
        RelationVendor.Text = string.Empty;
        RelationNationality.Clear();
        RelationTransportType.SelectedItem = null;
        RelationTransportType.Text = string.Empty;
        RelationPlates.Clear();
        RelationUnit.SelectedItem = null;
        RelationUnit.Text = string.Empty;
        RelationAdultPassengers.Text = "0";
        RelationYouthPassengers.Text = "0";
        RelationChildPassengers.Text = "0";
        RelationNoShowCount.Text = "0";
        RelationSellerBadges.Text = "Sin captura de vendedores";
        RelationPassengers.Text = "0";
        RelationSale.Text = "0";
        RelationPayout.Text = "0";
        RelationPaymentMethod.Clear();
        RelationCurrency.Clear();
        RelationCommission.Text = string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase)
            ? "0.00"
            : string.Empty;
        RelationNotes.Clear();
        RelationsGrid.SelectedItem = null;
    }
    private async void ClearRelationFilters_Click(object sender, RoutedEventArgs e)
    {
        RelationSearch.Clear();
        RelationStart.SelectedDate = DateTime.Today;
        RelationEnd.SelectedDate = DateTime.Today;
        ClearRelation_Click(sender, e);
        await RunAsync(RefreshAsync);
    }
    private async void LoadRelation_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LocalRelation relation) return;
        var detailed = await GetRelationDetailsAsync(relation);
        LoadRelationIntoForm(detailed);
        OpenRelationEdit(FirstFilled(detailed.AppFolio, detailed.OperationFolio, detailed.PosFolio), detailed.Driver);
    }

    // ===== Cajon lateral de edicion =====
    // El formulario de taxista/pasajeros dejo de estar incrustado en la pagina y ahora se abre
    // en un panel lateral, para que la tabla no quede empujada hacia abajo cuando no se esta
    // editando nada.
    private void OpenRelationEdit(string folio, string driver)
    {
        // El titulo distingue alta de edicion: con NUEVO decia "EDITAR RELACION", que confundia
        // porque no se estaba editando nada.
        var isNew = string.IsNullOrWhiteSpace(folio);
        RelationEditTitle.Text = isNew ? "NUEVA RELACIÓN" : "EDITAR RELACIÓN";

        var subtitle = isNew ? "Captura de una relación nueva" : $"Folio {folio}";
        if (!isNew && !string.IsNullOrWhiteSpace(driver)) subtitle += $" · {driver}";
        RelationEditSubtitle.Text = subtitle;
        RelationEditScrim.Visibility = Visibility.Visible;
        RelationEditPanel.Visibility = Visibility.Visible;
    }

    private void CloseRelationEdit_Click(object sender, RoutedEventArgs e) => CloseRelationEdit();

    // Handler aparte porque MouseLeftButtonDown exige MouseButtonEventArgs.
    private void RelationEditScrim_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloseRelationEdit();

    private void CloseRelationEdit()
    {
        RelationEditScrim.Visibility = Visibility.Collapsed;
        RelationEditPanel.Visibility = Visibility.Collapsed;
    }
    private void RelationSale_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LocalRelation relation) return;
        LoadRelationIntoForm(relation);
        var saleLookup = IsCascoBranch()
            ? FirstFilled(relation.OperationFolio, relation.AppFolio, relation.PosFolio)
            : FirstFilled(relation.PosFolio, relation.OperationFolio, relation.AppFolio);
        var hasSaleLookup = !string.IsNullOrWhiteSpace(saleLookup);
        var currentBranchCode = _currentBranch?.Code ?? _branchCode;
        new PosWindow(_database, _user, currentBranchCode, "Ventas", saleLookup, startEmpty: !hasSaleLookup, cascoOperationFolioForSaleLink: relation.OperationFolio) { Owner = this }.ShowDialog();
    }
    /// <summary>
    /// Candado contra doble cobro de dejada: el boton sigue habilitado mientras corre el
    /// dialogo de confirmacion y los awaits, asi que dos clics seguidos arrancarian dos pagos
    /// del mismo viaje.
    /// </summary>
    private bool _payoutPaymentInProgress;

    private void RelationPay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LocalRelation relation) return;
        if (_payoutPaymentInProgress) return;
        _payoutPaymentInProgress = true;
        _ = RunAsync(async () =>
        {
            try
            {
            var currentBranchCode = _currentBranch?.Code ?? _branchCode;
            LocalRelation workingRelation;
            if (string.Equals(currentBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCurrentRelationLoaded(relation))
                    LoadRelationIntoForm(relation);

                workingRelation = BuildRelationFromForm(relation);
            }
            else
            {
                LoadRelationIntoForm(relation);
                workingRelation = relation;
            }

            if (string.Equals(workingRelation.PayoutStatus, "pagado", StringComparison.OrdinalIgnoreCase)
                || string.Equals(workingRelation.PayoutStatus, "pagada", StringComparison.OrdinalIgnoreCase))
            {
                WebDialogWindow.Show(this, "La dejada de este viaje ya está pagada.", "Control Taxi", "!");
                return;
            }

            try
            {
                string ticket;
                decimal amount;
                string paidBy;
                string paidAt;
                if (string.Equals(currentBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await PayCascoRelationAsync(workingRelation);
                    ticket = result.PayoutTicket;
                    amount = result.Dejada;
                    paidBy = result.PayoutUser;
                    paidAt = result.PayoutDate;
                }
                else
                {
                    var result = await _operations.PayPayoutAsync(workingRelation.AppFolio, workingRelation.OperationFolio, _user);
                    ticket = result.Ticket;
                    amount = result.Amount;
                    paidBy = _user;
                    paidAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }

                var remotePayoutMessage = await MarkCascoPayoutAsPaidRemotelyAsync(workingRelation.AppFolio, workingRelation.OperationFolio, _user);

                _cascoReportCache = null;
                _cascoReportCacheSiteName = null;
                _cascoReportCacheStart = null;
                _cascoReportCacheEnd = null;
                InvalidateCascoRelationCache();
                await LoadRelationsAsync();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                LocalRelation freshRelation = (RelationsGrid.ItemsSource as IEnumerable<LocalRelation>)?
                    .FirstOrDefault(x => string.Equals(x.OperationFolio, workingRelation.OperationFolio, StringComparison.OrdinalIgnoreCase))
                    ?? workingRelation;
                var detailed = await GetRelationDetailsAsync(freshRelation);
                var commissionPersistenceMessage = await TrySaveCascoCommissionAfterPayoutAsync(detailed);
                var ticketText = await BuildRelationDejadaTicketAsync(detailed);
                var preview = new TicketPreviewWindow(ticketText);
                if (CanOwnChildWindow())
                    preview.Owner = this;
                preview.ShowDialog();

                WebDialogWindow.Show(
                    this,
                    "La dejada fue marcada como pagada correctamente."
                    + Environment.NewLine + Environment.NewLine
                    + $"Folio original: {detailed.OperationFolio}"
                    + Environment.NewLine + $"Folio local: {detailed.AppFolio}"
                    + Environment.NewLine + $"Taxista: {detailed.Driver}"
                    + Environment.NewLine + $"Importe: {amount.ToString("C2", CultureInfo.CurrentCulture)}"
                    + Environment.NewLine + $"Usuario: {FirstFilled(detailed.PayoutUser, paidBy)}"
                    + Environment.NewLine + $"Fecha pago: {FirstFilled(detailed.PayoutDate, paidAt)}"
                    + Environment.NewLine + $"Ticket: {FirstFilled(detailed.PayoutTicket, ticket)}"
                    + (string.IsNullOrWhiteSpace(remotePayoutMessage)
                        ? string.Empty
                        : Environment.NewLine + Environment.NewLine + remotePayoutMessage)
                    + (string.IsNullOrWhiteSpace(commissionPersistenceMessage)
                        ? string.Empty
                        : Environment.NewLine + Environment.NewLine + commissionPersistenceMessage),
                    "Pago exitoso",
                    "✓");
            }
            catch (Exception ex)
            {
                if (_isClosing)
                    return;

                await _errors.LogAsync(_user, "Operaciones", "Pago de dejada", ex);
                if (ex is OperationCanceledException)
                    return;
                WebDialogWindow.Show(this, ex.ToString(), "No se pudo completar el pago", "!");
            }
            }
            finally
            {
                _payoutPaymentInProgress = false;
            }
        });
    }

    private async Task<string> MarkCascoPayoutAsPaidRemotelyAsync(string? appFolio, string? operationFolio, string user)
    {
        // Plaza 28: notificar a Hostinger usando el endpoint propio de Plaza28
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "P28", StringComparison.OrdinalIgnoreCase)
            && _currentBranch is not null
            && !string.IsNullOrWhiteSpace(_currentBranch.ApiBaseUrl))
        {
            var folioPlaza = FirstFilled(appFolio, operationFolio);
            if (!string.IsNullOrWhiteSpace(folioPlaza))
            {
                try
                {
                    var plazaApi = new PlazaTripRecordsApiService(_currentBranch.ApiBaseUrl);
                    await plazaApi.MarkTripPayoutPaidAsync(folioPlaza, user, _windowCts.Token);
                }
                catch (Exception ex)
                {
                    await _errors.LogAsync(_user, "Operaciones", "Marcar dejada pagada Hostinger Plaza28", ex);
                    return "Pago confirmado localmente. No se pudo marcar como pagado en la app movil: " + ex.Message;
                }
            }
            return string.Empty;
        }

        if (!IsCascoBranch() || _cascoTripRecordsApi is null)
            return string.Empty;

        var folioOriginal = (operationFolio ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(folioOriginal))
            return string.Empty;

        try
        {
            await _cascoTripRecordsApi.MarkTripPayoutPaidAsync(folioOriginal, user, _windowCts.Token);
            return string.Empty;
        }
        catch (Exception ex)
        {
            await _errors.LogAsync(_user, "Operaciones", "Marcar dejada pagada Hostinger", ex);
            return "Pago confirmado localmente. No se pudo marcar como pagado en la app movil: " + ex.Message;
        }
    }

    private async Task<string> TrySaveCascoCommissionAfterPayoutAsync(LocalRelation relation)
    {
        if (!IsCascoBranch())
            return string.Empty;

        if (!IsPaidPayoutStatus(relation.PayoutStatus)
            || relation.Sale <= 0m
            || relation.Commission <= 0m
            || !string.Equals(relation.CommissionStatus, "PENDIENTE", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var branch = _currentBranch;
        if (branch is null || !CascoCommissionRuleService.IsCascoCommissionEnvironment(branch, branch.Code))
            return string.Empty;

        var folioOriginal = FirstFilled(relation.OperationFolio, relation.AppFolio);
        if (string.IsNullOrWhiteSpace(folioOriginal))
            return string.Empty;

        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
            return "Pago confirmado. No se guardo la comision porque falta la credencial SQL de Casco.";

        try
        {
            var service = new CascoCommissionPersistenceService();
            var commissionPreview = await service.PreviewCommissionAsync(
                branch,
                password,
                folioOriginal,
                _user,
                _windowCts.Token);

            if (!commissionPreview.RuleFound)
                return "Comisión pendiente: " + commissionPreview.Detail;
            if (commissionPreview.ReglaId is null or <= 0
                || commissionPreview.VentaTotal <= 0m
                || commissionPreview.ComisionCalculada <= 0m
                || !string.Equals(commissionPreview.Estatus, "PENDIENTE", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var save = await service.SaveCommissionAsync(branch, password, commissionPreview, _windowCts.Token);
            return save.Saved
                ? $"Comision generada: {save.Preview.ComisionCalculada.ToString("C2", CultureInfo.CurrentCulture)}"
                : "Comision ya registrada para este folio/regla. No se creo duplicado.";
        }
        catch (Exception ex)
        {
            await _errors.LogAsync(_user, "Operaciones", "Guardar comision Casco", ex);
            return "Pago confirmado, pero no se pudo guardar la comision: " + ex.Message;
        }
    }

    private static bool IsPaidPayoutStatus(string? status)
    {
        return string.Equals(status?.Trim(), "pagado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status?.Trim(), "pagada", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<CascoPayoutResult> PayCascoRelationAsync(LocalRelation relation)
    {
        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("No se encontro CASCO_SQL_PASSWORD para pagar la dejada en Casco Viejo.");

        var folioOriginal = FirstFilled(relation.OperationFolio, relation.AppFolio);
        if (string.IsNullOrWhiteSpace(folioOriginal))
            throw new InvalidOperationException("La relacion seleccionada no tiene folio original para pagar la dejada.");

        var preview = await CascoOperationsDataService.GetPayoutPreviewAsync(
            _currentBranch,
            _branchCode,
            password,
            folioOriginal,
            _windowCts.Token);

        var needsAutoSave = !preview.HasManualRelation
            || !preview.HasDejadaRow
            || (relation.Payout ?? 0m) > 0m && preview.Dejada != (relation.Payout ?? 0m);

        if (needsAutoSave)
        {
            await CascoOperationsDataService.SaveRelationAsync(
                _currentBranch,
                _branchCode,
                password,
                relation,
                _user,
                _windowCts.Token);

            preview = await CascoOperationsDataService.GetPayoutPreviewAsync(
                _currentBranch,
                _branchCode,
                password,
                folioOriginal,
                _windowCts.Token);
        }

        if (!preview.RecordFound)
            throw new InvalidOperationException($"No se encontro el viaje {folioOriginal} en Casco Viejo para pagar la dejada.");

        if (!preview.CanPay)
        {
            var details = new[]
            {
                $"Folio original: {FirstFilled(preview.FolioOriginal, folioOriginal)}",
                $"Folio local: {FirstFilled(preview.FolioLocal, relation.AppFolio, "-")}",
                $"Taxista: {FirstFilled(preview.Taxista, relation.Driver, "-")}",
                $"Dejada: {preview.Dejada.ToString("C2", CultureInfo.CurrentCulture)}",
                $"Estatus: {FirstFilled(preview.PayoutStatus, "pendiente")}",
                $"Fuente: {FirstFilled(preview.PayoutSource, "none")}",
                $"Ticket: {FirstFilled(preview.PayoutTicket, "-")}",
                $"Usuario: {FirstFilled(preview.PayoutUser, "-")}",
                $"Fecha pago: {FirstFilled(preview.PayoutDate, "-")}"
            };
            throw new InvalidOperationException("La dejada no se puede pagar en este momento."
                + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, details));
        }

        var confirmationMessage = string.Join(Environment.NewLine, new[]
        {
            "Se pagara la dejada de Casco Viejo con estos datos:",
            string.Empty,
            $"Folio original: {preview.FolioOriginal}",
            $"Folio local: {FirstFilled(preview.FolioLocal, relation.AppFolio, "-")}",
            $"Taxista: {FirstFilled(preview.Taxista, relation.Driver, "-")}",
            $"Gafete: {FirstFilled(preview.Gafete, relation.Badge, "-")}",
            $"Sitio: {FirstFilled(preview.Sitio, relation.Site, "-")}",
            $"Importe: {preview.Dejada.ToString("C2", CultureInfo.CurrentCulture)}",
            $"Ticket propuesto: {FirstFilled(preview.ProposedTicket, "-")}",
            $"Usuario: {_user}",
            string.Empty,
            "Deseas continuar?"
        });

        var confirmation = WebDialogWindow.Confirm(
            this,
            confirmationMessage,
            "PAGAR DEJADA | Casco Viejo",
            "?",
            "PAGAR",
            "CANCELAR");

        if (!confirmation)
            throw new OperationCanceledException("El pago de la dejada fue cancelado por el usuario.");

        return await CascoOperationsDataService.PayPayoutAsync(
            _currentBranch,
            _branchCode,
            password,
            folioOriginal,
            _user,
            _windowCts.Token,
            selectedBadge: relation.Badge);
    }
    private bool IsCurrentRelationLoaded(LocalRelation relation)
    {
        var formOriginal = (RelationOperationFolio.Text ?? string.Empty).Trim();
        var formLocal = (RelationAppFolio.Text ?? string.Empty).Trim();
        var relationOriginal = (relation.OperationFolio ?? string.Empty).Trim();
        var relationLocal = (relation.AppFolio ?? string.Empty).Trim();
        return string.Equals(formOriginal, relationOriginal, StringComparison.OrdinalIgnoreCase)
            && string.Equals(formLocal, relationLocal, StringComparison.OrdinalIgnoreCase);
    }

    private LocalRelation BuildRelationFromForm(LocalRelation relation)
    {
        return relation with
        {
            AppFolio = (RelationAppFolio.Text ?? string.Empty).Trim(),
            OperationFolio = (RelationOperationFolio.Text ?? string.Empty).Trim(),
            PosFolio = (RelationPosFolio.Text ?? string.Empty).Trim(),
            Badge = (RelationBadge.Text ?? string.Empty).Trim(),
            Driver = (RelationDriver.Text ?? string.Empty).Trim(),
            Vendor = (RelationVendor.Text ?? string.Empty).Trim(),
            Payout = NumberOrNull(RelationPayout.Text),
            Notes = (RelationNotes.Text ?? string.Empty).Trim(),
            TaxistaId = (RelationTaxistaId.Text ?? string.Empty).Trim(),
            Nationality = (RelationNationality.Text ?? string.Empty).Trim(),
            TransportType = (RelationTransportType.Text ?? string.Empty).Trim(),
            Plates = (RelationPlates.Text ?? string.Empty).Trim(),
            Unit = (RelationUnit.Text ?? string.Empty).Trim(),
            Sale = Number(RelationSale.Text),
            PaymentMethod = (RelationPaymentMethod.Text ?? string.Empty).Trim(),
            Currency = (RelationCurrency.Text ?? string.Empty).Trim(),
            Passengers = GetRelationPassengerTotal(),
            AdultPassengers = PositiveInt(RelationAdultPassengers.Text),
            YouthPassengers = PositiveInt(RelationYouthPassengers.Text),
            ChildPassengers = PositiveInt(RelationChildPassengers.Text),
            NoShowCount = PositiveInt(RelationNoShowCount.Text)
        };
    }
    private async void RelationCalculate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LocalRelation relation) LoadRelationIntoForm(relation);
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await RunAsync(async () =>
            {
                if (!CascoCommissionRuleService.IsCascoCommissionEnvironment(_currentBranch, _branchCode))
                {
                    WebDialogWindow.Show(
                        this,
                        "El preview de comisiones de Casco requiere una configuracion CV valida.",
                        "Comisiones Casco",
                        "!");
                    return;
                }

                var folioOriginal = FirstFilled(RelationOperationFolio.Text, RelationAppFolio.Text);
                if (string.IsNullOrWhiteSpace(folioOriginal))
                    throw new InvalidOperationException("Selecciona una relacion de Casco con folio original para calcular la comision.");

                var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
                var preview = await CascoOperationsDataService.GetLocalCommissionPreviewAsync(
                    password,
                    folioOriginal,
                    transportOverride: RelationTransportType.Text,
                    paymentMethodOverride: RelationPaymentMethod.Text,
                    cancellationToken: _windowCts.Token);

                RelationSale.Text = preview.VentaTotal.ToString("0.##", CultureInfo.InvariantCulture);
                RelationCommission.Text = preview.CommissionAmount.ToString("0.##", CultureInfo.InvariantCulture);
                WebDialogWindow.Show(
                    this,
                    "Preview de comision Casco"
                    + Environment.NewLine + Environment.NewLine
                    + $"Folio: {preview.FolioOriginal}"
                    + Environment.NewLine + $"Venta compuadmo: {preview.VentaCompuadmo.ToString("C2", CultureInfo.CurrentCulture)}"
                    + Environment.NewLine + $"Venta joyeria: {preview.VentaJoyeria.ToString("C2", CultureInfo.CurrentCulture)}"
                    + Environment.NewLine + $"Venta total: {preview.VentaTotal.ToString("C2", CultureInfo.CurrentCulture)}"
                    + Environment.NewLine + $"Transporte: {preview.TransporteOriginal}"
                    + Environment.NewLine + $"Regla encontrada: {(preview.RuleFound ? preview.Rule?.ReglaNombre : "NO")}"
                    + Environment.NewLine + $"Comision: {preview.CommissionAmount.ToString("C2", CultureInfo.CurrentCulture)}"
                    + Environment.NewLine + preview.Detail,
                    "Comisiones Casco",
                    "OK");
            });
            return;
        }
        await RunAsync(async () =>
        {
            _report = await _operations.GetReportAsync(DateTime.Today, DateTime.Today);
            ReportResult.Text = $"Registros: {_report.Records:N0}\nTotal: {_report.Total:C2}\nEfectivo: {_report.Cash:C2}\nTarjeta: {_report.Card:C2}";
            WebDialogWindow.Show(this, "Calculo local actualizado. Revisa la vista de reportes/comisiones para el detalle.", "Control Taxi", "OK");
        });
    }
    private void RelationPrint_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LocalRelation relation) return;
        _ = RunAsync(async () =>
        {
            var detailed = await GetRelationDetailsAsync(relation);
            if (IsLoaded)
                LoadRelationIntoForm(detailed);
            var ticket = await BuildRelationDejadaTicketAsync(detailed);
            var preview = new TicketPreviewWindow(ticket);
            if (CanOwnChildWindow())
                preview.Owner = this;
            preview.ShowDialog();
        });
    }
    private void LoadRelationIntoForm(LocalRelation relation)
    {
        _loadedRelationForm = relation;
        ClearRelationFormFields();
        RelationAppFolio.Text = relation.AppFolio;
        RelationOperationFolio.Text = relation.OperationFolio;
        RelationAssignedTicket.Text = relation.PosFolio;
        RelationPosFolio.Text = relation.PosFolio;
        RelationBadge.Text = relation.Badge;
        RelationTaxistaId.Text = relation.TaxistaId;
        RelationDriver.Text = relation.Driver;
        RelationVendor.Text = relation.Vendor;
        RelationNationality.Text = relation.Nationality;
        RelationPlates.Text = relation.Plates;
        RelationUnit.Text = relation.Unit;
        RefreshRelationTransportOptions(relation.Driver, relation.TaxistaId, relation.TransportType, relation.Plates, relation.Unit);
        RelationTransportType.Text = relation.TransportType;
        RelationAdultPassengers.Text = Math.Max(0, relation.AdultPassengers).ToString(CultureInfo.InvariantCulture);
        RelationYouthPassengers.Text = Math.Max(0, relation.YouthPassengers).ToString(CultureInfo.InvariantCulture);
        RelationChildPassengers.Text = Math.Max(0, relation.ChildPassengers).ToString(CultureInfo.InvariantCulture);
        RelationNoShowCount.Text = Math.Max(0, relation.NoShowCount).ToString(CultureInfo.InvariantCulture);
        RelationSellerBadges.Text = string.IsNullOrWhiteSpace(relation.SellerBadges)
            ? "Sin captura de vendedores"
            : relation.SellerBadges;
        RelationPassengers.Text = Math.Max(0, relation.Passengers).ToString(CultureInfo.InvariantCulture);
        RelationSale.Text = relation.Sale.ToString("0.##", CultureInfo.InvariantCulture);
        RelationPayout.Text = relation.Payout?.ToString(CultureInfo.InvariantCulture) ?? "0";
        RelationPaymentMethod.Text = relation.PaymentMethod;
        RelationCurrency.Text = relation.Currency;
        RelationCommission.Text = relation.Commission > 0m
            ? relation.Commission.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase)
                ? "0.00"
                : "0";
        RelationNotes.Text = relation.Notes;
    }

    private void ClearRelationFormFields()
    {
        RelationAssignedTicket.Clear();
        RelationAppFolio.Clear();
        RelationOperationFolio.Clear();
        RelationPosFolio.Clear();
        RelationBadge.Clear();
        RelationTaxistaId.Clear();
        RelationDriver.Text = string.Empty;
        RelationVendor.Text = string.Empty;
        RelationNationality.Clear();
        RelationTransportType.SelectedItem = null;
        RelationTransportType.Text = string.Empty;
        RelationPlates.Clear();
        RelationUnit.SelectedItem = null;
        RelationUnit.Text = string.Empty;
        RelationAdultPassengers.Text = "0";
        RelationYouthPassengers.Text = "0";
        RelationChildPassengers.Text = "0";
        RelationNoShowCount.Text = "0";
        RelationSellerBadges.Text = "Sin captura de vendedores";
        RelationPassengers.Text = "0";
        RelationSale.Text = "0";
        RelationPayout.Text = "0";
        RelationPaymentMethod.Clear();
        RelationCurrency.Clear();
        RelationCommission.Clear();
        RelationNotes.Clear();
    }

    private void RelationDriver_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RelationDriver.SelectedItem is LocalDriver selectedDriver)
        {
            ApplyRelationDriverVariant(selectedDriver, updateDriverIdentity: true);
            RefreshRelationTransportOptions(selectedDriver.Name, selectedDriver.Code, selectedDriver.ServiceType, selectedDriver.Plates, selectedDriver.Unit);
            RelationTransportType.SelectedItem = _relationTransportOptions.FirstOrDefault(option =>
                string.Equals(option.Value, selectedDriver.ServiceType, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void RelationTransportType_DropDownOpened(object sender, EventArgs e)
    {
        RefreshRelationTransportOptions(
            RelationDriver.Text,
            RelationTaxistaId.Text,
            RelationTransportType.Text,
            RelationPlates.Text,
            RelationUnit.Text);
    }

    private void RelationTransportType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingRelationTransport)
            return;

        if (RelationTransportType.SelectedItem is not RelationTransportOption option)
            return;

        var variant = option.Driver ?? FindRelationDriverVariantForTransport(option.Value);
        if (variant is not null)
        {
            ApplyRelationDriverVariant(variant, updateDriverIdentity: false);
            RefreshRelationTransportOptions(
                RelationDriver.Text,
                RelationTaxistaId.Text,
                variant.ServiceType,
                variant.Plates,
                variant.Unit);
        }

        _isUpdatingRelationTransport = true;
        try
        {
            RelationTransportType.SelectedItem = _relationTransportOptions.FirstOrDefault(item =>
                string.Equals(item.Value, option.Value, StringComparison.OrdinalIgnoreCase));
            RelationTransportType.Text = option.Value;
        }
        finally
        {
            _isUpdatingRelationTransport = false;
        }
    }

    private void RelationUnit_DropDownOpened(object sender, EventArgs e)
    {
        RefreshRelationTransportOptions(
            RelationDriver.Text,
            RelationTaxistaId.Text,
            RelationTransportType.Text,
            RelationPlates.Text,
            RelationUnit.Text);
    }

    private void RelationUnit_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingRelationTransport)
            return;

        if (RelationUnit.SelectedItem is not RelationUnitOption option || option.Driver is null)
            return;

        var selectedUnit = option.Value;
        var selectedPlates = option.Plates;
        var selectedServiceType = option.ServiceType;

        _isUpdatingRelationTransport = true;
        try
        {
            RelationPlates.Text = selectedPlates;
            RelationUnit.Text = selectedUnit;
            RelationTransportType.Text = selectedServiceType;
        }
        finally
        {
            _isUpdatingRelationTransport = false;
        }

        RefreshRelationTransportOptions(
            RelationDriver.Text,
            RelationTaxistaId.Text,
            selectedServiceType,
            selectedPlates,
            selectedUnit);

        _isUpdatingRelationTransport = true;
        try
        {
            RelationTransportType.SelectedItem = _relationTransportOptions.FirstOrDefault(item =>
                string.Equals(item.Value, selectedServiceType, StringComparison.OrdinalIgnoreCase));
            RelationTransportType.Text = selectedServiceType;

            RelationUnit.SelectedItem = _relationUnitOptions.FirstOrDefault(item =>
                string.Equals(item.Value, selectedUnit, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Plates, selectedPlates, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ServiceType, selectedServiceType, StringComparison.OrdinalIgnoreCase));
            RelationUnit.Text = selectedUnit;
        }
        finally
        {
            _isUpdatingRelationTransport = false;
        }
    }

    private LocalDriver? FindRelationDriverVariantForTransport(string transportType)
    {
        var selectedTransport = (transportType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(selectedTransport))
            return null;

        var taxistaId = (RelationTaxistaId.Text ?? string.Empty).Trim();
        var driverName = (RelationDriver.Text ?? string.Empty).Trim();
        var currentUnit = (RelationUnit.Text ?? string.Empty).Trim();
        var currentPlates = (RelationPlates.Text ?? string.Empty).Trim();

        var matches = _relationDriverCatalog
            .Where(driver => string.Equals(driver.ServiceType?.Trim(), selectedTransport, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(taxistaId))
        {
            matches = matches.Where(driver => string.Equals(driver.Code, taxistaId, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(driverName))
        {
            matches = matches.Where(driver => string.Equals(driver.Name, driverName, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            return null;
        }

        return matches
            .OrderByDescending(driver => string.Equals(driver.Unit?.Trim(), currentUnit, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(driver => string.Equals(driver.Plates?.Trim(), currentPlates, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(driver => !string.IsNullOrWhiteSpace(driver.Plates))
            .ThenByDescending(driver => !string.IsNullOrWhiteSpace(driver.Unit))
            .FirstOrDefault();
    }

    private void RelationPassengerBreakdown_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        RelationPassengers.Text = GetRelationPassengerTotal().ToString(CultureInfo.InvariantCulture);
    }

    private int GetRelationPassengerTotal()
    {
        return PositiveInt(RelationAdultPassengers.Text)
            + PositiveInt(RelationYouthPassengers.Text)
            + PositiveInt(RelationChildPassengers.Text);
    }

    private static int PositiveInt(string? value) =>
        int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 0;

    private void DriversGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriversGrid.SelectedItem is LocalDriver driver)
            LoadDriverIntoForm(driver);
    }

    private void TransportsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransportsGrid.SelectedItem is LocalTransport transport)
            LoadTransportIntoForm(transport);
    }

    private async Task ApplyDriverSearchAsync()
    {
        var search = (DriverSearch.Text ?? string.Empty).Trim();
        var rows = await Task.Run(() =>
        {
            IEnumerable<LocalDriver> query = _driverCatalog
                .Where(driver => string.IsNullOrWhiteSpace(search) || DriverMatches(driver, search))
                .OrderBy(driver => driver.Name)
                .ThenBy(driver => driver.Code, StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(search))
                query = query.Take(DriverInitialRowLimit);

            return query.ToArray();
        });
        DriversGrid.ItemsSource = rows;
        DriverResultsText.Text = string.IsNullOrWhiteSpace(search)
            ? $"Mostrando {rows.Length:N0} de {_driverCatalog.Count:N0} taxistas. Usa el buscador para filtrar sin congelar la vista."
            : $"Resultados: {rows.Length:N0} taxistas para \"{search}\".";
    }

    private async Task ApplyTransportSearchAsync()
    {
        var search = (TransportSearch.Text ?? string.Empty).Trim();
        var rows = await Task.Run(() =>
        {
            IEnumerable<LocalTransport> query = _transportCatalog
                .Where(transport => string.IsNullOrWhiteSpace(search) || TransportMatches(transport, search))
                .OrderBy(transport => transport.Name)
                .ThenBy(transport => transport.Code, StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(search))
                query = query.Take(TransportInitialRowLimit);

            return query.ToArray();
        });
        TransportsGrid.ItemsSource = rows;
        TransportResultsText.Text = string.IsNullOrWhiteSpace(search)
            ? $"Mostrando {rows.Length:N0} de {_transportCatalog.Count:N0} transportes."
            : $"Resultados: {rows.Length:N0} transportes para \"{search}\".";
    }

    private bool DriverMatches(LocalDriver driver, string search)
    {
        if (Contains(driver.Id.ToString(CultureInfo.InvariantCulture), search)
            || Contains(driver.Code, search)
            || Contains(driver.Name, search)
            || Contains(driver.Phone, search)
            || Contains(driver.Plates, search)
            || Contains(driver.Unit, search)
            || Contains(driver.ServiceType, search)
            || Contains(driver.Status, search))
        {
            return true;
        }

        return _driverBadgeLookup.TryGetValue(driver.Id, out var badges)
            && badges.Any(badge => Contains(badge, search));
    }

    private static bool TransportMatches(LocalTransport transport, string search) =>
        Contains(transport.Code, search)
        || Contains(transport.Name, search)
        || Contains(transport.Minimum.ToString(CultureInfo.InvariantCulture), search)
        || Contains(transport.Maximum.ToString(CultureInfo.InvariantCulture), search)
        || Contains(transport.Commission.ToString(CultureInfo.InvariantCulture), search)
        || Contains(transport.CashDiscount.ToString(CultureInfo.InvariantCulture), search)
        || Contains(transport.CardDiscount.ToString(CultureInfo.InvariantCulture), search)
        || Contains(transport.AmexDiscount.ToString(CultureInfo.InvariantCulture), search);

    private static bool Contains(string? source, string search) =>
        !string.IsNullOrWhiteSpace(source) && source.Contains(search, StringComparison.OrdinalIgnoreCase);

    private void LoadDriverIntoForm(LocalDriver driver)
    {
        DriverCode.Text = driver.Code;
        DriverName.Text = driver.Name;
        DriverPhone.Text = driver.Phone;
        DriverPlates.Text = driver.Plates;
        DriverUnit.Text = driver.Unit;
        DriverService.Text = driver.ServiceType;
        SelectComboText(DriverStatus, driver.Status, "Activo");
        ApplyDriverMode(true);
    }

    private void LoadTransportIntoForm(LocalTransport transport)
    {
        _selectedCascoTransport = UsesHostingerCatalog() ? transport : null;
        TransportCode.Text = transport.Code;
        TransportName.Text = transport.Name;
        TransportMin.Text = transport.Minimum.ToString("0.##", CultureInfo.InvariantCulture);
        TransportMax.Text = transport.Maximum.ToString("0.##", CultureInfo.InvariantCulture);
        TransportCommission.Text = transport.Commission.ToString("0.##", CultureInfo.InvariantCulture);
        TransportCash.Text = transport.CashDiscount.ToString("0.##", CultureInfo.InvariantCulture);
        TransportCard.Text = transport.CardDiscount.ToString("0.##", CultureInfo.InvariantCulture);
        TransportAmex.Text = transport.AmexDiscount.ToString("0.##", CultureInfo.InvariantCulture);
        ApplyTransportMode(true);
    }

    private void ClearDriverForm()
    {
        DriverCode.Clear();
        DriverName.Clear();
        DriverPhone.Clear();
        DriverPlates.Clear();
        DriverUnit.Clear();
        DriverService.Clear();
        DriverSearch.Clear();
        SelectComboText(DriverStatus, "Activo", "Activo");
        DriversGrid.SelectedItem = null;
        _ = UsesHostingerCatalog() ? LoadDriversModuleAsync() : ApplyDriverSearchAsync();
        ApplyDriverMode(false);
    }

    private void ClearTransportForm()
    {
        _selectedCascoTransport = null;
        TransportCode.Clear();
        TransportName.Clear();
        TransportMin.Text = "0";
        TransportMax.Text = "0";
        TransportCommission.Text = "0";
        TransportCash.Text = "0";
        TransportCard.Text = "0";
        TransportAmex.Text = "0";
        TransportSearch.Clear();
        TransportsGrid.SelectedItem = null;
        _ = ApplyTransportSearchAsync();
        ApplyTransportMode(false);
    }

    private void ApplyDriverMode(bool editing) => DriverModeText.Text = editing ? "EDITANDO REGISTRO" : "NUEVO REGISTRO";
    private void ApplyTransportMode(bool editing) => TransportModeText.Text = editing ? "EDITANDO REGISTRO" : "NUEVO REGISTRO";

    private async void DriverSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_windowReady)
            return;

        _driverSearchCts?.Cancel();
        _driverSearchCts = new System.Threading.CancellationTokenSource();
        var token = _driverSearchCts.Token;
        try
        {
            await Task.Delay(250, token);
            if (!token.IsCancellationRequested)
            {
                if (UsesHostingerCatalog())
                    await LoadDriversModuleAsync();
                else
                    await ApplyDriverSearchAsync();
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private async void TransportSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_windowReady)
            return;

        _transportSearchCts?.Cancel();
        _transportSearchCts = new System.Threading.CancellationTokenSource();
        var token = _transportSearchCts.Token;
        try
        {
            await Task.Delay(250, token);
            if (!token.IsCancellationRequested)
                await ApplyTransportSearchAsync();
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static void SelectComboText(ComboBox combo, string? value, string fallback)
    {
        var selected = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(Convert.ToString(item.Content, CultureInfo.InvariantCulture), selected, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private void RefreshRelationTransportOptions(
        string? driverName = null,
        string? taxistaId = null,
        string? transportType = null,
        string? plates = null,
        string? unit = null)
    {
        if (!string.Equals(_currentBranch?.Code ?? _branchCode, "P28", StringComparison.OrdinalIgnoreCase))
            return;

        var normalizedDriver = (driverName ?? RelationDriver.Text ?? string.Empty).Trim();
        var normalizedTaxistaId = (taxistaId ?? RelationTaxistaId.Text ?? string.Empty).Trim();
        var normalizedTransport = (transportType ?? RelationTransportType.Text ?? string.Empty).Trim();
        var normalizedPlates = (plates ?? RelationPlates.Text ?? string.Empty).Trim();
        var normalizedUnit = (unit ?? RelationUnit.Text ?? string.Empty).Trim();

        var sourceDrivers = _relationDriverCatalog
            .Where(driver => !string.IsNullOrWhiteSpace(driver.ServiceType));

        var unitCandidates = sourceDrivers.ToArray();
        var unitOptions = unitCandidates
            .Where(driver => !string.IsNullOrWhiteSpace(driver.Unit))
            .OrderByDescending(driver => !string.IsNullOrWhiteSpace(normalizedTaxistaId)
                && string.Equals(driver.Code, normalizedTaxistaId, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(driver => !string.IsNullOrWhiteSpace(normalizedDriver)
                && string.Equals(driver.Name, normalizedDriver, StringComparison.OrdinalIgnoreCase))
            .Select(driver => new RelationUnitOption(
                driver,
                driver.Unit.Trim(),
                BuildRelationUnitDisplay(driver),
                driver.Plates?.Trim() ?? string.Empty,
                driver.ServiceType?.Trim() ?? string.Empty))
            .DistinctBy(option => string.Join("|", option.Value, option.Plates, option.ServiceType))
            .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.ServiceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Plates, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var options = sourceDrivers
            .Select(driver => driver.ServiceType.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Select(value => new RelationTransportOption(null, value, value, string.Empty, string.Empty))
            .ToArray();

        if (options.Length == 0 && !string.IsNullOrWhiteSpace(normalizedTransport))
        {
            options = new[]
            {
                new RelationTransportOption(
                    null,
                    normalizedTransport,
                    normalizedTransport,
                    normalizedPlates,
                    normalizedUnit)
            }
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .ToArray();
        }

        _relationTransportOptions = options;
        _relationUnitOptions = unitOptions;
        RelationTransportType.ItemsSource = options;
        RelationUnit.ItemsSource = unitOptions;

        var selected = options.FirstOrDefault(option =>
            string.Equals(option.Value, normalizedTransport, StringComparison.OrdinalIgnoreCase));
        var selectedUnit = unitOptions.FirstOrDefault(option =>
            string.Equals(option.Value, normalizedUnit, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(normalizedPlates) || string.Equals(option.Plates, normalizedPlates, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(normalizedTransport) || string.Equals(option.ServiceType, normalizedTransport, StringComparison.OrdinalIgnoreCase)));

        _isUpdatingRelationTransport = true;
        try
        {
            RelationTransportType.SelectedItem = selected;
            RelationTransportType.Text = normalizedTransport;
            RelationUnit.SelectedItem = selectedUnit;
            RelationUnit.Text = normalizedUnit;
        }
        finally
        {
            _isUpdatingRelationTransport = false;
        }
    }

    private static string BuildRelationTransportDisplay(LocalDriver driver)
    {
        var service = (driver.ServiceType ?? string.Empty).Trim();
        var detail = FirstFilled((driver.Plates ?? string.Empty).Trim(), (driver.Unit ?? string.Empty).Trim());
        return string.IsNullOrWhiteSpace(detail)
            ? service
            : $"{service} - {detail}";
    }

    private static string BuildRelationUnitDisplay(LocalDriver driver)
    {
        var unit = (driver.Unit ?? string.Empty).Trim();
        var details = new[] { (driver.ServiceType ?? string.Empty).Trim(), (driver.Plates ?? string.Empty).Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return details.Length == 0 ? unit : $"{unit} - {string.Join(" - ", details)}";
    }

    private void ApplyRelationDriverVariant(LocalDriver driver, bool updateDriverIdentity)
    {
        if (updateDriverIdentity)
        {
            RelationDriver.Text = driver.Name;
            RelationTaxistaId.Text = driver.Code;
        }

        _isUpdatingRelationTransport = true;
        try
        {
            RelationTransportType.Text = driver.ServiceType ?? string.Empty;
        }
        finally
        {
            _isUpdatingRelationTransport = false;
        }

        RelationPlates.Text = driver.Plates ?? string.Empty;
        RelationUnit.Text = driver.Unit ?? string.Empty;
    }
    private async Task<string> BuildRelationDejadaTicketAsync(LocalRelation relation)
    {
        var currentBranchCode = _currentBranch?.Code ?? _branchCode;
        if (string.Equals(currentBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            return await CascoOperationsDataService.BuildDejadaTicketTextAsync(
                _currentBranch,
                _branchCode,
                password,
                relation,
                string.IsNullOrWhiteSpace(relation.PayoutUser) ? _user : relation.PayoutUser,
                _windowCts.Token);
        }

        var folio = FirstFilled(relation.PayoutTicket, relation.OperationFolio, relation.AppFolio, relation.PosFolio);
        if (!string.IsNullOrWhiteSpace(folio))
        {
            try
            {
                return await _pos.BuildDejadaTicketTextAsync(folio, string.IsNullOrWhiteSpace(relation.PayoutUser) ? _user : relation.PayoutUser);
            }
            catch
            {
                // Si no encuentra la dejada en el espejo local, usa el formato Web con los datos ya cargados.
            }
        }

        return BuildRelationDejadaTicketFallback(relation, string.IsNullOrWhiteSpace(relation.PayoutUser) ? _user : relation.PayoutUser);
    }
    private static string BuildRelationDejadaTicketFallback(LocalRelation relation, string user)
    {
        const int width = 42;
        static string Line(char value = '-') => new(value, width);
        static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        static string Center(string value)
        {
            value = Clean(value);
            if (value.Length >= width) return value[..width];
            var left = (width - value.Length) / 2;
            return new string(' ', left) + value;
        }
        static string Pair(string label, string value)
        {
            label = Clean(label).ToUpperInvariant();
            value = Clean(value);
            var prefix = $"{label}: ";
            var maxValue = Math.Max(1, width - prefix.Length);
            return prefix + (value.Length > maxValue ? value[..maxValue] : value);
        }

        var folio = Clean(FirstFilled(relation.OperationFolio, relation.AppFolio, relation.PosFolio));
        var folioApp = Clean(relation.AppFolio);
        var operacion = Clean(relation.OperationFolio);
        var importe = relation.PayoutPaid > 0m ? relation.PayoutPaid : relation.Payout ?? 0m;
        return string.Join(Environment.NewLine, new[]
        {
            Center("CONTROL TAXI"),
            Center("PAGO DE DEJADA"),
            Line(),
            Pair("Ticket", relation.PayoutTicket),
            Pair("Folio", folio),
            folioApp != folio ? Pair("Folio app", relation.AppFolio) : string.Empty,
            operacion != folio && operacion != folioApp ? Pair("Operacion", relation.OperationFolio) : string.Empty,
            Pair("Fecha viaje", relation.DateText),
            Pair("Fecha pago", relation.PayoutDate),
            Line(),
            Pair("Taxista", relation.Driver),
            Pair("Vendedor", relation.Vendor),
            Pair("Gafete", relation.Badge),
            Pair("Unidad", relation.Unit),
            Pair("Placas", relation.Plates),
            Pair("Telefono", relation.Phone),
            Pair("Nacionalidad", relation.Nationality),
            Pair("Transporte", relation.TransportType),
            Pair("Hotel", relation.Hotel),
            Pair("Origen", relation.Origin),
            Pair("Destino", relation.Destination),
            Pair("Pax", relation.Passengers.ToString(CultureInfo.InvariantCulture)),
            Line(),
            Pair("Sucursal", relation.Site),
            Pair("Forma pago", relation.PaymentMethod),
            Pair("Moneda", relation.Currency),
            Pair("Importe", importe.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("Usuario", user),
            Pair("Estatus", relation.PayoutStatus),
            Line(),
            Center("CONSERVE ESTE COMPROBANTE"),
            "________________________",
            Center("FIRMA DEL TAXISTA")
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }
    private async void SaveBadge_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            WebDialogWindow.Show(this, "Gafetes no tiene una fuente equivalente de solo lectura para Casco Viejo. Se evita escribir en Plaza 28.", "Control Taxi", "!");
            return;
        }

        await RunAsync(async () => { await _operations.SaveBadgeRecordAsync(BadgeStaff.Text, BadgeNumber.Text, BadgeOperationFolio.Text, _user, BadgeVendor.Text); await LoadBadgesAsync(); });
    }
    private async void ReturnBadge_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var current = ResolveCurrentBadgeFormSelection();
            if (current is null)
                throw new ArgumentException("Selecciona un gafete ocupado para regresarlo.");

            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            var provider = new CascoBadgeProvider(_currentBranch ?? _branchService.GetBranch("CV"));
            var result = await provider.ReturnBadgesAsync(password, [new LocalBadgeSelection(current.Number, current.OperationFolio)], _user);
            if (result.Updated == 0)
                throw new ArgumentException($"El gafete {current.Number} ya no esta ocupado y no requiere regreso.");

            RemoveReturnedScannedBadges([current.Number]);
            BadgeBulkScan.Clear();
            ShowBadgeBulkInfo("Gafetes regresados correctamente.", Brushes.ForestGreen);
            await LoadBadgesAsync();
            return;
        }

        if (!await _operations.ReturnImportedBadgeAsync(BadgeNumber.Text, BadgeOperationFolio.Text, _user))
            await _operations.AssignBadgeAsync(BadgeNumber.Text, 0, true);
        await LoadBadgesAsync();
    });
    private async void ReturnSelectedBadges_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var cascoSelections = CollectBadgeSelections(requireOccupiedStatus: true);
            if (cascoSelections.Count == 0)
                throw new ArgumentException("Selecciona o escanea al menos un gafete ocupado.");

            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            var provider = new CascoBadgeProvider(_currentBranch ?? _branchService.GetBranch("CV"));
            cascoSelections = await FilterCurrentOccupiedBadgeSelectionsAsync(provider, password, cascoSelections);
            if (cascoSelections.Count == 0)
                throw new ArgumentException("Los gafetes seleccionados ya estan libres. Actualiza la lista y selecciona solo gafetes asignados.");

            var result = await provider.ReturnBadgesAsync(password, cascoSelections, _user);
            if (result.Updated == 0)
                throw new ArgumentException("Los gafetes seleccionados ya no estan ocupados o ya fueron regresados.");

            ClearScannedBadges();
            BadgeBulkScan.Clear();
            ShowBadgeBulkInfo("Gafetes regresados correctamente.", Brushes.ForestGreen);
            await LoadBadgesAsync();
            return;
        }

        var badges = CollectBadgeSelections(requireOccupiedStatus: false);
        if (badges.Count == 0) throw new ArgumentException("Selecciona o escanea al menos un gafete ocupado.");
        await _operations.ReturnImportedBadgesAsync(badges, _user);
        BadgeBulkScan.Clear();
        ClearScannedBadges();
        await LoadBadgesAsync();
    });
    private async void BadgeBulkScan_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var isPlaza28 = string.Equals(_currentBranch?.Code ?? _branchCode, "P28", StringComparison.OrdinalIgnoreCase);
        if (e.Key != System.Windows.Input.Key.Enter && (!isPlaza28 || e.Key != System.Windows.Input.Key.Tab)) return;
        e.Handled = true;
        await RunAsync(() => CommitBadgeBulkScanAsync(keepFocus: true));
    }
    private async void BadgeBulkScan_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!string.Equals(_currentBranch?.Code ?? _branchCode, "P28", StringComparison.OrdinalIgnoreCase))
            return;

        var text = BadgeBulkScan.Text;
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(new[] { '\r', '\n', '\t' }) < 0)
            return;

        await RunAsync(() => CommitBadgeBulkScanAsync(keepFocus: true));
    }
    private async void BadgeBulkScan_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!string.Equals(_currentBranch?.Code ?? _branchCode, "P28", StringComparison.OrdinalIgnoreCase))
            return;

        var text = BadgeBulkScan.Text;
        if (string.IsNullOrWhiteSpace(text) || text.IndexOfAny(new[] { '\r', '\n', '\t' }) < 0)
            return;

        await RunAsync(() => CommitBadgeBulkScanAsync(keepFocus: false));
    }
    private bool _badgeBulkScanInProgress;
    private async Task CommitBadgeBulkScanAsync(bool keepFocus)
    {
        // Prevenir doble disparo: TextChanged + KeyDown pueden disparar simultáneamente
        if (_badgeBulkScanInProgress) return;
        _badgeBulkScanInProgress = true;
        try
        {
            var values = BadgeSelectionWorkflow.SplitScanValues(BadgeBulkScan.Text);
            BadgeBulkScan.Clear();
            foreach (var value in values)
                await AddScannedBadgeAsync(value);
            RefreshBadgeBulkState();
            if (keepFocus)
                BadgeBulkScan.Focus();
        }
        finally
        {
            _badgeBulkScanInProgress = false;
        }
    }
    private async Task AddScannedBadgeAsync(string rawValue)
    {
        var normalized = BadgeSelectionWorkflow.NormalizeScanToken(rawValue);
        if (string.IsNullOrWhiteSpace(normalized)) return;

        var isCascoBranch = string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase);
        BadgeScanResolution resolution;
        try
        {
            resolution = await BadgeSelectionWorkflow.ResolveScanAsync(
                normalized,
                BadgesGrid.Items.OfType<LocalBadge>().ToArray(),
                _scannedBadges,
                ResolveBadgeAsync,
                isCascoBranch);
        }
        catch (ArgumentException ex)
        {
            // Gafete no encontrado: mostrar aviso sin romper el flujo del escáner
            ShowBadgeBulkInfo(ex.Message, Brushes.OrangeRed);
            return;
        }

        var row = EnsureBadgePresentInGrid(resolution.Badge);
        SelectBadgeRow(row);

        if (resolution.AlreadySelected)
        {
            ShowBadgeBulkInfo($"El gafete {row.Number} ya esta seleccionado.", Brushes.DarkGoldenrod);
            return;
        }

        var scannedNumber = isCascoBranch ? normalized : row.Number;
        _scannedBadges.Add(new LocalScannedBadgeItem(scannedNumber, row.Staff, row.OperationFolio, row.Status, row.Unit, row.Phone, row.LocalFolio, row.Vendor));
        if (!resolution.IsOccupied)
        {
            ShowBadgeBulkInfo($"El gafete {row.Number} ya esta libre.", Brushes.SteelBlue);
            return;
        }

        ShowBadgeBulkInfo($"Gafete {row.Number} seleccionado.", Brushes.ForestGreen);
    }
    private void RemoveScannedBadge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LocalScannedBadgeItem item) return;
        _scannedBadges.Remove(item);
        RefreshBadgeBulkState();
        BadgeBulkScan.Focus();
    }
    private void BadgeBulkCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is LocalBadge badge)
        {
            badge.BulkSelected = checkBox.IsChecked == true && badge.CanBulkReturn;
            if (!badge.CanBulkReturn)
                checkBox.IsChecked = false;
        }

        BadgesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        BadgesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        RefreshBadgeBulkState();
    }
    private void ClearScannedBadges()
    {
        _scannedBadges.Clear();
        ShowBadgeBulkInfo(string.Empty, Brushes.Transparent);
        RefreshBadgeBulkState();
    }
    private void RefreshBadgeBulkState()
    {
        var total = (BadgesGrid?.Items.OfType<LocalBadge>()
                .Where(x => x.BulkSelected)
                .Select(x => BadgeSelectionWorkflow.BuildSelectionKey(x.Number, x.OperationFolio))
                .Concat(_scannedBadges.Select(x => BadgeSelectionWorkflow.BuildSelectionKey(x.Number, x.OperationFolio)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() ?? 0);
        if (BadgeBulkReturnButton is not null)
            BadgeBulkReturnButton.Content = total > 0 ? $"REGRESAR SELECCIONADOS ({total})" : "REGRESAR SELECCIONADOS";
    }

    private async Task<LocalBadge?> ResolveBadgeAsync(string normalized)
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            var provider = new CascoBadgeProvider(_currentBranch ?? _branchService.GetBranch("CV"));
            return await provider.FindBadgeAsync(password, normalized);
        }

        return await _operations.FindBadgeAsync(normalized);
    }

    private LocalBadge EnsureBadgePresentInGrid(LocalBadge badge)
    {
        var existing = BadgeSelectionWorkflow.FindLoadedBadge(BadgesGrid.Items.OfType<LocalBadge>(), badge.Number)
            ?? BadgesGrid.Items.OfType<LocalBadge>().FirstOrDefault(x =>
                string.Equals(BadgeSelectionWorkflow.BuildSelectionKey(x.Number, x.OperationFolio),
                    BadgeSelectionWorkflow.BuildSelectionKey(badge.Number, badge.OperationFolio),
                    StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.BulkSelected = true;
            BadgesGrid.Items.Refresh();
            return existing;
        }

        badge.BulkSelected = true;
        var rows = BadgesGrid.Items.OfType<LocalBadge>().ToList();
        rows.Insert(0, badge);
        BadgesGrid.ItemsSource = rows;
        RefreshBadgeBulkState();
        return badge;
    }

    private void SelectBadgeRow(LocalBadge badge)
    {
        badge.BulkSelected = true;
        BadgesGrid.SelectedItem = badge;
        BadgesGrid.UpdateLayout();
        BadgesGrid.ScrollIntoView(badge);
        RefreshBadgeBulkState();
    }

    private void ShowBadgeBulkInfo(string message, Brush brush)
    {
        if (BadgeBulkInfoText is null)
            return;

        BadgeBulkInfoText.Text = message;
        if (brush is not null)
            BadgeBulkInfoText.Foreground = brush;
    }

    private List<LocalBadgeSelection> CollectBadgeSelections(bool requireOccupiedStatus)
    {
        BadgesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        BadgesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        BadgesGrid.UpdateLayout();

        foreach (var checkBox in FindVisualChildren<CheckBox>(BadgesGrid))
        {
            if (checkBox.DataContext is LocalBadge badge)
                badge.BulkSelected = checkBox.IsChecked == true && (!requireOccupiedStatus || badge.CanBulkReturn);
        }

        var checkedBadges = BadgesGrid.Items
            .OfType<LocalBadge>()
            .Where(x => x.BulkSelected && (!requireOccupiedStatus || x.CanBulkReturn))
            .SelectMany(x => BadgeSelectionWorkflow.SplitScanValues(x.Number)
                .DefaultIfEmpty(x.Number)
                .Select(number => new LocalBadgeSelection(number, x.OperationFolio)))
            .ToList();

        var scannedBadges = _scannedBadges
            .Where(x => !requireOccupiedStatus || BadgeSelectionWorkflow.IsOccupiedStatus(x.Status))
            .SelectMany(x => BadgeSelectionWorkflow.SplitScanValues(x.Number)
                .DefaultIfEmpty(x.Number)
                .Select(number => new LocalBadgeSelection(number, x.OperationFolio)))
            .ToList();

        var source = scannedBadges.Count > 0
            ? scannedBadges
            : checkedBadges;

        var badges = source
            .GroupBy(x => BadgeSelectionWorkflow.BuildSelectionKey(x.Number, x.OperationFolio), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        return badges;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static async Task<List<LocalBadgeSelection>> FilterCurrentOccupiedBadgeSelectionsAsync(
        CascoBadgeProvider provider,
        string password,
        IReadOnlyList<LocalBadgeSelection> selections)
    {
        var result = new List<LocalBadgeSelection>();
        foreach (var selection in selections)
        {
            var current = await provider.FindBadgeAsync(password, selection.Number);
            if (current is null || !current.CanBulkReturn)
                continue;

            if (!string.IsNullOrWhiteSpace(selection.OperationFolio)
                && !string.IsNullOrWhiteSpace(current.OperationFolio)
                && !string.Equals(selection.OperationFolio.Trim(), current.OperationFolio.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new LocalBadgeSelection(current.Number, current.OperationFolio));
        }

        return result
            .GroupBy(x => BadgeSelectionWorkflow.BuildSelectionKey(x.Number, x.OperationFolio), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
    }

    private LocalBadge? ResolveCurrentBadgeFormSelection()
    {
        if (BadgesGrid.SelectedItem is LocalBadge selected)
            return selected;

        var number = BadgeNumber.Text.Trim();
        var folio = BadgeOperationFolio.Text.Trim();
        if (string.IsNullOrWhiteSpace(number))
            return null;

        return BadgesGrid.Items.OfType<LocalBadge>().FirstOrDefault(x =>
            string.Equals(x.Number, number, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(folio) || string.Equals(x.OperationFolio ?? string.Empty, folio, StringComparison.OrdinalIgnoreCase)));
    }

    private void RemoveReturnedScannedBadges(IEnumerable<string> badgeNumbers)
    {
        var set = new HashSet<string>(badgeNumbers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
        foreach (var item in _scannedBadges.Where(x => set.Contains(x.Number)).ToList())
            _scannedBadges.Remove(item);
        RefreshBadgeBulkState();
    }
    private async void SaveAppRecord_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentBranch?.Code, "CV", StringComparison.OrdinalIgnoreCase))
        {
            WebDialogWindow.Show(this, "No es posible guardar registros en modo solo lectura Casco Viejo.", "Control Taxi", "!");
            return;
        }

        await RunAsync(async () =>
        {
            var driver = AppTaxistaEncontrado.SelectedItem as LocalDriver;
            var hotel = AppHotel.SelectedItem as LocalHotel;
            var rate = AppTarifa.SelectedItem as LocalRate;
            var folioControl = await _operations.SaveAppRecordAsync(new LocalAppRecordInput(
                string.IsNullOrWhiteSpace(AppFolioOriginal.Text) ? null : AppFolioOriginal.Text,
                Value(AppTaxistaEncontrado.SelectedValue),
                AppTaxistaNombre.Text,
                AppTelefono.Text,
                AppTelefonoContacto.Text,
                AppNacionalidad.Text,
                AppPlacas.Text,
                AppModelo.Text,
                AppUnidad.Text,
                hotel?.Name ?? string.Empty,
                AppOrigen.Text,
                AppSitio.Text,
                AppDestino.Text,
                int.TryParse(AppPax.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pax) ? Math.Max(0, pax) : 1,
                !string.IsNullOrWhiteSpace(AppTipoTransporte.Text) ? AppTipoTransporte.Text : rate?.Type ?? driver?.ServiceType ?? string.Empty,
                Number(AppDejada.Text),
                Value(AppTarifa.SelectedValue),
                AppGafete.Text,
                AppNotas.Text,
                _user));
            AppFolioOriginal.Text = folioControl;
            await RefreshAsync();
        });
    }
    private void LoadAppDriver_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentBranch?.Code, "CV", StringComparison.OrdinalIgnoreCase))
        {
            WebDialogWindow.Show(this, "No es posible cargar datos de conductor en modo solo lectura Casco Viejo.", "Control Taxi", "!");
            return;
        }

        if (AppTaxistaEncontrado.SelectedItem is not LocalDriver driver) { WebDialogWindow.Show(this, "Selecciona un taxista para cargar sus datos.", "Control Taxi", "!"); return; }
        AppTaxistaNombre.Text = driver.Name; AppTaxistaId.Text = driver.Id.ToString(CultureInfo.InvariantCulture); AppTelefono.Text = driver.Phone; AppUnidad.Text = driver.Unit; AppPlacas.Text = driver.Plates; AppModelo.Text = driver.Model; AppTipoTransporte.Text = driver.ServiceType;
    }
    private void AppTarifa_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppTarifa.SelectedItem is not LocalRate rate) return;
        AppTipoTransporte.Text = string.IsNullOrWhiteSpace(AppTipoTransporte.Text) ? rate.Type : AppTipoTransporte.Text;
        AppDejada.Text = rate.Payout.ToString(CultureInfo.InvariantCulture);
    }
    private void AppGafeteEntry_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitAppGafete(keepFocus: true);
        e.Handled = true;
    }
    private void AppGafeteEntry_LostFocus(object sender, RoutedEventArgs e) => CommitAppGafete(keepFocus: false);
    private void FocusAppGafete_Click(object sender, RoutedEventArgs e) => AppGafeteEntry.Focus();
    private void RemoveAppGafete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string gafete) _appGafetes.Remove(gafete);
        RefreshAppGafeteChips();
        AppGafeteEntry.Focus();
    }
    private void CommitAppGafete(bool keepFocus = false)
    {
        _appGafeteAutoCommitTimer.Stop();
        _appGafeteAutoCommitStart = null;
        _appGafeteAutoCommitLastChange = null;
        _appGafeteAutoCommitChangeCount = 0;

        var value = AppGafeteEntry.Text.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(value) && !_appGafetes.Contains(value, StringComparer.OrdinalIgnoreCase))
            _appGafetes.Add(value);

        AppGafeteEntry.Clear();
        RefreshAppGafeteChips();
        if (keepFocus)
            AppGafeteEntry.Focus();
    }
    private void RefreshAppGafeteChips()
    {
        AppGafete.Text = string.Join(", ", _appGafetes);
        AppGafeteChips.ItemsSource = null;
        AppGafeteChips.ItemsSource = _appGafetes.ToArray();
    }
    private void EditBadge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LocalBadge badge) return;
        BadgeStaff.Text = badge.Staff;
        BadgeNumber.Text = badge.Number;
        BadgeOperationFolio.Text = badge.OperationFolio;
    }
    private async void RefreshBadges_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadBadgesAsync);
    private async void ResetBadges_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        BadgeStart.SelectedDate = DateTime.Today;
        BadgeEnd.SelectedDate = DateTime.Today;
        BadgeStaffSearch.Clear();
        BadgeSearch.Clear();
        BadgeOperationSearch.Clear();
        ClearScannedBadges();
        await LoadBadgesAsync();
    });
    private void NewBadge_Click(object sender, RoutedEventArgs e)
    {
        BadgeStaff.Clear();
        BadgeNumber.Clear();
        BadgeOperationFolio.Clear();
        BadgeBulkScan.Clear();
        BadgesGrid.SelectedItem = null;
        ClearScannedBadges();
    }
    private async void RefreshRegistro_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadRegistroAsync);
    private async void SaveRegistro_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadRegistroAsync);
    private async void SetRegistroToday_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async () =>
        {
            RegistroStart.SelectedDate = DateTime.Today;
            RegistroEnd.SelectedDate = DateTime.Today;
            await LoadRegistroAsync();
        });
    }
    private void OpenRegistroReportes_Click(object sender, RoutedEventArgs e) => ApplySelectedModule("Reportes");
    private void OpenRegistroComisiones_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            ApplySelectedModule("Reportes");
            ReportsTabControl.SelectedIndex = 1;
            _ = RunAsync(LoadReportsAsync);
            return;
        }

        var window = new PosWindow(_database, _user, _branchCode, "Comisiones") { Owner = this };
        window.ShowDialog();
    }
    private void OpenRegistroCierre_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            WebDialogWindow.Show(this, "Cortes no tiene una fuente equivalente de solo lectura para Casco Viejo en esta ventana. Se evita abrir Plaza 28.", "Control Taxi", "!");
            return;
        }

        var window = new PosWindow(_database, _user, _branchCode, "Cortes") { Owner = this };
        window.ShowDialog();
    }
    private async Task LoadRegistroAsync()
    {
        _windowCts.Token.ThrowIfCancellationRequested();
        RegistroGrid.SelectedItem = null;
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            var cascoData = await CascoOperationsDataService.LoadAsync(
                _currentBranch,
                _branchCode,
                password,
                RegistroSearch.Text,
                RegistroStart.SelectedDate ?? DateTime.Today,
                RegistroEnd.SelectedDate ?? RegistroStart.SelectedDate ?? DateTime.Today,
                _windowCts.Token);
            if (_isClosing || _windowCts.IsCancellationRequested)
                return;
            Debug.WriteLine($"[LoadRegistroAsync] RegistroGrid source | branch={cascoData.BranchCode} | provider={cascoData.Provider} | count={cascoData.RegistroRows.Count} | firstFolioOriginal={cascoData.AppGridRows.FirstOrDefault()?.FolioOriginal ?? ""} | firstFolioLocal={cascoData.AppGridRows.FirstOrDefault()?.FolioLocal ?? ""} | firstTaxista={cascoData.RegistroRows.FirstOrDefault()?.Taxista ?? ""} | firstSitio={cascoData.RegistroRows.FirstOrDefault()?.Sitio ?? ""}");
            _registroRows.Clear();
            foreach (var row in cascoData.RegistroRows)
                _registroRows.Add(row);
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isClosing || _windowCts.IsCancellationRequested)
                    return;
                Debug.WriteLine($"[Dispatcher][LoadRegistroAsync] RegistroGrid count={_registroRows.Count}; firstFolioOriginal={cascoData.AppGridRows.FirstOrDefault()?.FolioOriginal ?? ""}; firstFolioLocal={cascoData.AppGridRows.FirstOrDefault()?.FolioLocal ?? ""}; firstTaxista={_registroRows.FirstOrDefault()?.Taxista ?? ""}; firstSitio={_registroRows.FirstOrDefault()?.Sitio ?? ""}");
            }));
            if (_registroRows.Count > 0)
            {
                RegistroGrid.SelectedIndex = 0;
                ShowRegistroSelection(_registroRows[0]);
            }
            else
            {
                ClearRegistroSelection();
            }
            return;
        }

        RegistroStart.SelectedDate ??= DateTime.Today;
        RegistroEnd.SelectedDate ??= RegistroStart.SelectedDate;
        var relations = await _operations.GetRelationsAsync(RegistroSearch.Text, RegistroStart.SelectedDate, RegistroEnd.SelectedDate, siteName: GetCurrentSiteName());
        if (_isClosing || _windowCts.IsCancellationRequested)
            return;
        _registroRows.Clear();
        foreach (var row in relations.Select(MapRegistroRow))
            _registroRows.Add(row);
        if (_registroRows.Count > 0)
        {
            RegistroGrid.SelectedIndex = 0;
            ShowRegistroSelection(_registroRows[0]);
        }
        else
        {
            ClearRegistroSelection();
        }
    }
    private async Task LoadBadgesAsync()
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
            var cascoBadges = await CascoOperationsDataService.LoadBadgesAsync(
                _currentBranch,
                _branchCode,
                password,
                BadgeStart.SelectedDate,
                BadgeEnd.SelectedDate,
                BadgeStaffSearch.Text,
                BadgeSearch.Text,
                BadgeOperationSearch.Text,
                _windowCts.Token);
            foreach (var badge in cascoBadges.Badges)
                badge.BulkSelected = false;
            FillBadgeVendorTeams(cascoBadges.Badges);
            BadgesGrid.ItemsSource = cascoBadges.Badges;
            _scannedBadges.Clear();
            RefreshBadgeBulkState();
            return;
        }

        BadgeStart.SelectedDate ??= DateTime.Today;
        BadgeEnd.SelectedDate ??= BadgeStart.SelectedDate;
        var badges = await _operations.GetBadgesAsync(BadgeStart.SelectedDate, BadgeEnd.SelectedDate, BadgeStaffSearch.Text, BadgeSearch.Text, BadgeOperationSearch.Text);
        foreach (var badge in badges)
            badge.BulkSelected = false;
        FillBadgeVendorTeams(badges);
        BadgesGrid.ItemsSource = badges;
        _scannedBadges.Clear();
        RefreshBadgeBulkState();
    }

    private void SetRelationSearchStatus(string text, string colorHex)
    {
        if (RelationSearchStatusText is null || RelationSearchStatusBadge is null)
            return;
        RelationSearchStatusText.Text = text;
        RelationSearchStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
    }

    private void ShowRelationsSkeleton()
    {
        if (RelationsSkeleton is null) return;
        RelationsSkeletonRows.ItemsSource = Enumerable.Range(0, 6).ToArray();
        RelationsEmptyState.Visibility = Visibility.Collapsed;
        RelationsSkeleton.Visibility = Visibility.Visible;
    }

    private void HideRelationsSkeleton()
    {
        if (RelationsSkeleton is null) return;
        RelationsSkeleton.Visibility = Visibility.Collapsed;
        RelationsSkeletonRows.ItemsSource = null;
    }

    // Nunca dejar la tabla en blanco sin explicacion: el texto dice si el vacio viene del
    // buscador o del rango de fechas.
    private void ApplyRelationsEmptyState(int rowCount)
    {
        if (RelationsEmptyState is null) return;
        if (rowCount > 0)
        {
            RelationsEmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        var search = RelationSearch.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search) && RelationStart.SelectedDate is { } fromSearch && RelationEnd.SelectedDate is { } toSearch)
        {
            // El filtro compara contra la fecha del VIAJE, no la fecha en que se capturo en el
            // sistema. Un viaje capturado tarde (ej. registrado hoy pero ocurrido ayer) no
            // aparece si se busca solo por hoy, y sin esta aclaracion parece que el folio no
            // existe. Detectado el 2026-08-24 con el folio 4622.
            RelationsEmptyStateDetail.Text = fromSearch.Date == toSearch.Date
                ? $"No hay viajes de \"{search}\" con fecha de viaje el {fromSearch:dd/MM/yyyy}. Si se capturo tarde, prueba con un rango de fechas mas amplio: el filtro usa la fecha del viaje, no la fecha de captura."
                : $"No hay viajes de \"{search}\" con fecha de viaje entre el {fromSearch:dd/MM/yyyy} y el {toSearch:dd/MM/yyyy}.";
        }
        else if (!string.IsNullOrWhiteSpace(search))
        {
            RelationsEmptyStateDetail.Text = $"No hay viajes que coincidan con \"{search}\".";
        }
        else if (RelationStart.SelectedDate is { } from && RelationEnd.SelectedDate is { } to)
        {
            RelationsEmptyStateDetail.Text = from.Date == to.Date
                ? $"No hay viajes registrados el {from:dd/MM/yyyy}."
                : $"No hay viajes registrados entre el {from:dd/MM/yyyy} y el {to:dd/MM/yyyy}.";
        }
        else
        {
            RelationsEmptyStateDetail.Text = "No hay viajes para el filtro o rango de fechas seleccionado.";
        }

        RelationsEmptyState.Visibility = Visibility.Visible;
    }

    private async Task LoadRelationsAsync()
    {
        _windowCts.Token.ThrowIfCancellationRequested();
        SetRelationSearchStatus("Buscando...", "#F2A93B");
        ShowRelationsSkeleton();
        var autoSelectSingleResult = _autoSelectSingleRelationSearchResult;
        _autoSelectSingleRelationSearchResult = false;
        var currentBranchCode = _currentBranch?.Code ?? _branchCode;
        if (!string.Equals(currentBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            NormalizeRelationDateRange();
        }

        var keepApp = RelationAppFolio.Text;
        var keepOperation = RelationOperationFolio.Text;
        var keepPos = RelationPosFolio.Text;
        _relationDetailsCache.Clear();
        IReadOnlyList<LocalRelation> rows;
        try
        {
            if (string.Equals(currentBranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            {
                var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
                rows = await GetCascoRelationsDataAsync();
                Debug.WriteLine($"[LoadRelationsAsync] branch=CV provider=CascoReadOnlyDataProvider count={rows.Count} firstFolioOriginal={rows.FirstOrDefault()?.OperationFolio ?? ""} firstFolioLocal={rows.FirstOrDefault()?.AppFolio ?? ""} firstTaxista={rows.FirstOrDefault()?.Driver ?? ""} firstSitio={rows.FirstOrDefault()?.Site ?? ""}");
            }
            else
            {
                rows = await _operations.GetRelationsAsync(RelationSearch.Text, RelationStart.SelectedDate, RelationEnd.SelectedDate, includeFinancialDetails: true, siteName: GetCurrentSiteName());
            }
        }
        catch
        {
            SetRelationSearchStatus("Error al buscar", "#D9534F");
            HideRelationsSkeleton();
            throw;
        }

        HideRelationsSkeleton();
        if (_isClosing || _windowCts.IsCancellationRequested)
            return;

        rows = ApplyRelationSearchFilter(rows);
        foreach (var row in rows.Where(x => !string.IsNullOrWhiteSpace(x.OperationFolio) || !string.IsNullOrWhiteSpace(x.AppFolio)).Take(5))
        {
            Debug.WriteLine($"[OperationsWindow] LoadRelationsAsync grid row folio={row.AppFolio}/{row.OperationFolio} payoutStatus={row.PayoutStatus} payoutDate={row.PayoutDate} payoutTicket={row.PayoutTicket} payoutUser={row.PayoutUser}");
        }
        RelationsGrid.ItemsSource = rows;
        ApplyRelationsEmptyState(rows.Count);
        SetRelationSearchStatus(
            rows.Count == 0 ? "Sin resultados" : $"{rows.Count} resultado{(rows.Count == 1 ? "" : "s")}",
            rows.Count == 0 ? "#8A93A6" : "#0F4AB6");
        var selected = rows.FirstOrDefault(x =>
            (!string.IsNullOrWhiteSpace(keepApp) && string.Equals(x.AppFolio, keepApp, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(keepOperation) && string.Equals(x.OperationFolio, keepOperation, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(keepPos) && string.Equals(x.PosFolio, keepPos, StringComparison.OrdinalIgnoreCase)));
        if (selected is null && autoSelectSingleResult && rows.Count == 1)
            selected = rows[0];

        if (selected is not null)
        {
            RelationsGrid.SelectedItem = selected;
            LoadRelationIntoForm(selected);
            RelationsGrid.ScrollIntoView(selected);
            if (autoSelectSingleResult)
                RelationDriver.Focus();
        }
    }

    private IReadOnlyList<LocalRelation> ApplyRelationSearchFilter(IReadOnlyList<LocalRelation> rows)
    {
        var searchTerm = (RelationSearch.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(searchTerm))
            return rows;

        var normalized = searchTerm.Trim().ToLowerInvariant();
        return rows.Where(relation =>
        {
            var haystack = string.Join(" ",
                relation.AppFolio,
                relation.OperationFolio,
                relation.PosFolio,
                relation.DisplayLocalFolio,
                relation.Driver,
                relation.Badge,
                relation.Hotel,
                relation.Origin,
                relation.Site,
                relation.Unit,
                relation.Phone,
                relation.Vendor,
                relation.Nationality,
                relation.TransportType,
                relation.PaymentMethod,
                relation.Notes)
                .ToLowerInvariant();
            return haystack.Contains(normalized, StringComparison.OrdinalIgnoreCase);
        }).ToArray();
    }

    private async Task<LocalRelation> GetRelationDetailsAsync(LocalRelation relation)
    {
        var key = FirstFilled(relation.OperationFolio, relation.AppFolio, relation.PosFolio, relation.Id.ToString(CultureInfo.InvariantCulture));
        if (_relationDetailsCache.TryGetValue(key, out var cached))
            return cached;
        LocalRelation detailed;
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            detailed = relation;
        }
        else
        {
            detailed = await _operations.EnrichRelationAsync(relation);
        }

        _relationDetailsCache[key] = detailed;
        return detailed;
    }

    private bool CanOwnChildWindow() =>
        IsLoaded
        && !Equals(PresentationSource.FromVisual(this), null)
        && Visibility == Visibility.Visible;

    private static string BuildRelationSalePreview(LocalRelation relation)
    {
        var lines = new List<string>
        {
            "CONTROL TAXI",
            "DETALLE DE VENTA",
            new string('-', 56),
            $"Folio app: {FirstFilled(relation.AppFolio, "-")}",
            $"Folio operacion: {FirstFilled(relation.OperationFolio, "-")}",
            $"Ticket POS: {FirstFilled(relation.PosFolio, "-")}",
            $"Fecha: {FirstFilled(relation.DateText, "-")}",
            $"Taxista: {FirstFilled(relation.Driver, relation.Vendor, "-")}",
            $"Gafete: {FirstFilled(relation.Badge, "-")}",
            $"Hotel: {FirstFilled(relation.Hotel, "-")}",
            $"Transporte: {FirstFilled(relation.TransportType, "-")}",
            $"Forma de pago: {FirstFilled(relation.PaymentMethod, "SIN PAGO POS")}",
            $"Moneda: {FirstFilled(relation.Currency, "-")}",
            new string('-', 56),
            $"Venta real: {relation.Sale:C2}",
            $"Dejada: {(relation.Payout ?? 0m):C2}",
            $"Comision: {relation.Commission:C2}",
            $"Pago comision: {relation.CommissionPaid:C2}",
            string.Empty,
            "Detalle de tickets POS",
            string.IsNullOrWhiteSpace(relation.SaleDetail) ? "SIN DETALLE DE TICKETS" : relation.SaleDetail
        };
        return string.Join(Environment.NewLine, lines);
    }
    private static LocalRegistroDiarioRow MapRegistroRow(LocalRelation row)
    {
        var sale = row.Sale;
        var isCard = (row.PaymentMethod ?? string.Empty).Contains("TARJ", StringComparison.OrdinalIgnoreCase)
            || (row.PaymentMethod ?? string.Empty).Contains("CARD", StringComparison.OrdinalIgnoreCase)
            || (row.PaymentMethod ?? string.Empty).Contains("AMEX", StringComparison.OrdinalIgnoreCase);
        var tickets = SplitTickets(row.PosFolio);
        var date = ParseRegistroDate(row.DateText);
        return new LocalRegistroDiarioRow(
            string.IsNullOrWhiteSpace(row.OperationFolio) ? row.AppFolio : row.OperationFolio,
            row.AppFolio,
            string.IsNullOrWhiteSpace(row.Source) ? "SISTEMA/POS" : row.Source,
            string.IsNullOrWhiteSpace(row.SourceUser) ? "SIN USUARIO" : row.SourceUser,
            row.PosFolio,
            row.Badge,
            tickets.Length == 0 ? 1 : tickets.Length,
            row.Hotel,
            FirstFilled(row.Destination, row.Site, row.Origin, row.Hotel),
            date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? ExtractDate(row.DateText),
            date?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? ExtractTime(row.DateText),
            row.Passengers,
            string.IsNullOrWhiteSpace(row.Driver) ? row.Vendor : row.Driver,
            row.Nationality,
            row.TransportType,
            row.Origin,
            row.Site,
            row.Destination,
            row.Unit,
            row.Plates,
            row.Phone,
            row.Notes,
            sale,
            isCard ? 0m : sale,
            isCard ? sale : 0m,
            row.SellerBadges);
    }
    private void RegistroGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RegistroGrid.SelectedItem is LocalRegistroDiarioRow row) ShowRegistroSelection(row);
    }
    private void ShowRegistroSelection(LocalRegistroDiarioRow row)
    {
        RegistroSelectedTaxista.Text = row.Taxista;
        RegistroSelectedNo.Text = row.Gafete;
        RegistroSelectedFecha.Text = row.FechaTexto;
        RegistroSelectedPax.Text = row.Pax.ToString(CultureInfo.InvariantCulture);
        RegistroSelectedFolioHeader.Text = row.FolioOperacion;
        RegistroDetailGafete.Text = row.Gafete;
        RegistroDetailVendedores.Text = string.IsNullOrWhiteSpace(row.Vendedores) ? "Sin captura de vendedores" : row.Vendedores;
        RegistroDetailFuente.Text = row.Fuente;
        RegistroDetailUsuarioOrigen.Text = row.UsuarioOrigen;
        RegistroDetailHotel.Text = row.Hotel;
        RegistroDetailLlegada.Text = row.LlegadaSucursal;
        RegistroDetailOrigen.Text = row.Origen;
        RegistroDetailSitio.Text = row.Sitio;
        RegistroDetailDestino.Text = row.Destino;
        RegistroDetailUnidad.Text = row.Unidad;
        RegistroDetailPlacas.Text = row.Placas;
        RegistroDetailTelefono.Text = row.Telefono;
        RegistroDetailNacionalidad.Text = row.Nacionalidad;
        RegistroSelectedGrid.ItemsSource = new[] { row };
        RegistroPaymentGrid.ItemsSource = new[]
        {
            new LocalTicketLine("Efectivo", row.Efectivo.ToString("C2", CultureInfo.CurrentCulture)),
            new LocalTicketLine("Tarjetas", row.Tarjeta.ToString("C2", CultureInfo.CurrentCulture)),
            new LocalTicketLine("Total", row.Total.ToString("C2", CultureInfo.CurrentCulture))
        };
    }
    private void ClearRegistroSelection()
    {
        RegistroSelectedTaxista.Clear();
        RegistroSelectedNo.Clear();
        RegistroSelectedFecha.Clear();
        RegistroSelectedPax.Clear();
        RegistroSelectedFolioHeader.Text = string.Empty;
        RegistroDetailGafete.Text = string.Empty;
        RegistroDetailVendedores.Text = string.Empty;
        RegistroDetailFuente.Text = string.Empty;
        RegistroDetailUsuarioOrigen.Text = string.Empty;
        RegistroDetailHotel.Text = string.Empty;
        RegistroDetailLlegada.Text = string.Empty;
        RegistroDetailOrigen.Text = string.Empty;
        RegistroDetailSitio.Text = string.Empty;
        RegistroDetailDestino.Text = string.Empty;
        RegistroDetailUnidad.Text = string.Empty;
        RegistroDetailPlacas.Text = string.Empty;
        RegistroDetailTelefono.Text = string.Empty;
        RegistroDetailNacionalidad.Text = string.Empty;
        RegistroSelectedGrid.ItemsSource = null;
        RegistroPaymentGrid.ItemsSource = null;
    }
    private static string[] SplitTickets(string? value) =>
        (value ?? string.Empty)
            .Split(new[] { ',', ';', '/', '|', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    private static DateTime? ParseRegistroDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var current)
        || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out current)
            ? current
            : null;
    private static string FirstFilled(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;
    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string ExtractDate(string? value)
    {
        var date = ParseRegistroDate(value);
        return date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
    }
    private static string ExtractTime(string? value)
    {
        var date = ParseRegistroDate(value);
        return date?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    }
    private async void ExportRegistroCsv_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "registro_diario.csv" };
        if (dialog.ShowDialog() == true) await _output.ExportCsvAsync(_registroRows.ToArray(), dialog.FileName);
    });
    private async void ExportRegistroPdf_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = "registro_diario.pdf" };
        if (dialog.ShowDialog() == true) await _output.ExportSimplePdfAsync("Registro Diario", _registroRows.ToArray(), dialog.FileName);
    });
    private async void CalculateReport_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadReportsAsync);
    private async void ClearReport_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        ReportStart.SelectedDate = DateTime.Today;
        ReportEnd.SelectedDate = DateTime.Today;
        await LoadReportsAsync();
    });
    private async void ExportReport_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = ReportsTabControl.SelectedIndex == 1 ? "pagos_comisiones.csv" : "operaciones.csv" };
        if (dialog.ShowDialog() != true) return;
        if (ReportsTabControl.SelectedIndex == 1) await _output.ExportCsvAsync((await GetCommissionPaymentPreviewRowsAsync()).ToArray(), dialog.FileName);
        else await _output.ExportCsvAsync((await GetOperationPreviewRowsAsync()).ToArray(), dialog.FileName);
    });
    private async void PreviewReport_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadReportsAsync);
    private async void ExportReportPdf_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = ReportsTabControl.SelectedIndex == 1 ? "pagos_comisiones.pdf" : "reporte_operativo.pdf" };
        if (dialog.ShowDialog() != true) return;
        if (ReportsTabControl.SelectedIndex == 1) await _output.ExportSimplePdfAsync("Pagos de comisiones", await GetCommissionPaymentPreviewRowsAsync(), dialog.FileName);
        else await _output.ExportSimplePdfAsync("Operaciones del dia", await GetOperationPreviewRowsAsync(), dialog.FileName);
    });
    private async void ExportOperationalExcel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var (start, end) = GetReportRange();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"control_dejadas_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx" };
        if (dialog.ShowDialog() != true) return;

        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await _output.ExportControlDejadasWorkbookAsync(
                start,
                end,
                await GetReportWorkbookRelationsAsync(),
                dialog.FileName);
            return;
        }

        await _output.ExportControlDejadasWorkbookAsync(
            start,
            end,
            await GetReportWorkbookRelationsAsync(),
            dialog.FileName);
    });
    private async void ExportConcentratedExcel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var (start, end) = GetReportRange();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"concentrado_general_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx" };
        if (dialog.ShowDialog() != true) return;

        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await _output.ExportConcentradoWorkbookAsync(
                start,
                end,
                await GetReportWorkbookRelationsAsync(),
                Array.Empty<LocalCuadreResumenRow>(),
                dialog.FileName);
            return;
        }

        await _output.ExportConcentradoWorkbookAsync(
            start,
            end,
            await GetReportWorkbookRelationsAsync(),
            await _pos.GetCamionesResumenAsync(ReportStart.SelectedDate, ReportEnd.SelectedDate),
            dialog.FileName,
            await _pos.GetCommissionBrowserRowsAsync(null, start, end));
    });
    private async void ExportCuadreExcel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var (start, end) = GetReportRange();
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"cuadre_final_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx" };
        if (dialog.ShowDialog() != true)
            return;

        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await _output.ExportCuadreWorkbookAsync(
                start,
                end,
                await GetReportWorkbookRelationsAsync(),
                await GetReportWorkbookCommissionsAsync(),
                Array.Empty<LocalCut>(),
                Array.Empty<LocalCuadreResumenRow>(),
                dialog.FileName);
            return;
        }

        // Los datos se guardan en variables y no se piden dos veces: el Excel y la publicacion en
        // Hoka tienen que salir del mismo material, o los dos reportes terminarian discrepando.
        var authoritativeCommissionRows = await _pos.GetCommissionBrowserRowsAsync(null, start, end);
        var relations = await GetReportWorkbookRelationsAsync();
        var commissions = MapReportWorkbookCommissions(authoritativeCommissionRows);
        var cuts = (await _pos.GetCutsAsync())
            .Where(x => x.Date.Date >= start.Date && x.Date.Date <= end.Date)
            .ToArray();
        var camiones = await _pos.GetCamionesResumenAsync(ReportStart.SelectedDate, ReportEnd.SelectedDate);

        await _output.ExportCuadreWorkbookAsync(
            start,
            end,
            relations,
            commissions,
            cuts,
            camiones,
            dialog.FileName,
            authoritativeCommissionRows);

        var publicacion = await CuadrePushService.PublicarAsync(
            _output, start, end, relations, commissions, cuts, camiones,
            _currentBranch?.Code ?? _branchCode, authoritativeCommissionRows);

        if (!publicacion.Ok)
            WebDialogWindow.Show(this, "El Excel se guardo bien, pero no se pudo publicar en Hoka. " + publicacion.Mensaje, "Control Taxi", "!");
    });
    private async void ExportBadgesCsv_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            WebDialogWindow.Show(this, "Gafetes no tiene una fuente equivalente de solo lectura para Casco Viejo. Se evita exportar datos de Plaza 28.", "Control Taxi", "!");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "gafetes.csv" };
        if (dialog.ShowDialog() == true) await _output.ExportCsvAsync(await _operations.GetBadgesAsync(BadgeStart.SelectedDate, BadgeEnd.SelectedDate, BadgeStaffSearch.Text, BadgeSearch.Text, BadgeOperationSearch.Text), dialog.FileName);
    });
    private void PrintBadges_Click(object sender, RoutedEventArgs e)
    {
        var content = string.Join(Environment.NewLine, BadgesGrid.Items.OfType<LocalBadge>().Take(100).Select(x => $"{x.Number} | {x.Staff} | {x.OperationFolio} | {x.Status}"));
        if (string.IsNullOrWhiteSpace(content)) { WebDialogWindow.Show(this, "No hay gafetes para imprimir.", "Control Taxi", "!"); return; }
        _output.PrintText("Relacion de gafetes", content);
    }
    private async void OpenReportSearch_Click(object sender, RoutedEventArgs e)
    {
        ReportsTabControl.SelectedIndex = 0;
        await RunAsync(LoadReportsAsync);
    }
    private async void OpenReportMovementsModule_Click(object sender, RoutedEventArgs e)
    {
        ReportsTabControl.SelectedIndex = 0;
        await RunAsync(LoadReportsAsync);
    }
    private void OpenReportCommissionsModule_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            ReportsTabControl.SelectedIndex = 1;
            UpdateReportDashboard(
                ReportOperationsGrid.Items.OfType<LocalOperationsPreviewRow>().ToArray(),
                ReportCommissionPaymentsGrid.Items.OfType<LocalCommissionPaymentPreviewRow>().ToArray(),
                Array.Empty<LocalCut>());
            return;
        }

        var window = new PosWindow(_database, _user, _branchCode, "Comisiones") { Owner = this };
        window.ShowDialog();
    }
    private void OpenReportCutsModule_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            WebDialogWindow.Show(this, "Cortes no tiene una fuente equivalente de solo lectura para Casco Viejo en esta ventana. Se evita abrir Plaza 28.", "Control Taxi", "!");
            return;
        }

        var window = new PosWindow(_database, _user, _branchCode, "Cortes") { Owner = this };
        window.ShowDialog();
    }
    private void OpenReportPaymentsModule_Click(object sender, RoutedEventArgs e)
    {
        ReportsTabControl.SelectedIndex = 1;
        UpdateReportDashboard(
            ReportOperationsGrid.Items.OfType<LocalOperationsPreviewRow>().ToArray(),
            ReportCommissionPaymentsGrid.Items.OfType<LocalCommissionPaymentPreviewRow>().ToArray(),
            Array.Empty<LocalCut>());
    }
    private void PrintReport_Click(object sender, RoutedEventArgs e)
    {
        var active = ReportPrintPreviewText.Text;
        if (string.IsNullOrWhiteSpace(active)) { WebDialogWindow.Show(this, "Calcula primero el reporte.", "Control Taxi", "!"); return; }
        _output.PrintText("Centro de reportes", active);
    }
    private async Task LoadReportsAsync()
    {
        NormalizeReportDateRange();
        var isCascoBranch = string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase);
        var operationRows = (await GetOperationPreviewRowsAsync()).ToArray();
        var paymentRows = (await GetCommissionPaymentPreviewRowsAsync()).ToArray();
        var cuts = isCascoBranch
            ? Array.Empty<LocalCut>()
            : (await _pos.GetCutsAsync())
                .Where(x => x.Date.Date >= (ReportStart.SelectedDate ?? DateTime.Today).Date && x.Date.Date <= (ReportEnd.SelectedDate ?? ReportStart.SelectedDate ?? DateTime.Today).Date)
                .ToArray();
        ReportOperationsGrid.ItemsSource = operationRows;
        ReportCommissionPaymentsGrid.ItemsSource = paymentRows;
        ReportHeroPeriodText.Text = $"{(ReportStart.SelectedDate ?? DateTime.Today):dd/MM/yyyy} al {(ReportEnd.SelectedDate ?? ReportStart.SelectedDate ?? DateTime.Today):dd/MM/yyyy}";
        UpdateReportDashboard(operationRows, paymentRows, cuts);
    }
    private async Task<IReadOnlyList<LocalOperationsPreviewRow>> GetOperationPreviewRowsAsync()
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            return (await GetCascoReportDataAsync()).OperationRows.ToArray();
        }

        return (await _operations.GetOperationsReportPreviewAsync(
            ReportStart.SelectedDate,
            ReportEnd.SelectedDate,
            GetCurrentSiteName())).ToArray();
    }
    private async Task<IReadOnlyList<LocalCommissionPaymentPreviewRow>> GetCommissionPaymentPreviewRowsAsync()
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            return (await GetCascoReportDataAsync()).PaymentRows.ToArray();
        }

        return (await _operations.GetCommissionPaymentsReportAsync(ReportStart.SelectedDate, ReportEnd.SelectedDate, GetCurrentSiteName())).ToArray();
    }

    private async Task<CascoReportCenterLoadResult> GetCascoReportDataAsync()
    {
        var start = (ReportStart.SelectedDate ?? DateTime.Today).Date;
        var end = (ReportEnd.SelectedDate ?? ReportStart.SelectedDate ?? DateTime.Today).Date;
        if (end < start)
            (start, end) = (end, start);

        var siteName = GetCurrentSiteName() ?? string.Empty;
        if (_cascoReportCache is not null
            && _cascoReportCacheStart == start
            && _cascoReportCacheEnd == end
            && string.Equals(_cascoReportCacheSiteName, siteName, StringComparison.OrdinalIgnoreCase))
        {
            return _cascoReportCache;
        }

        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        var result = await CascoOperationsDataService.LoadActiveReportAsync(
            _currentBranch,
            _branchCode,
            password,
            async (rangeStart, rangeEnd, resolvedSiteName, cancellationToken) =>
                (await _operations.GetOperationsReportPreviewAsync(rangeStart, rangeEnd, resolvedSiteName)).ToArray(),
            async (rangeStart, rangeEnd, resolvedSiteName, cancellationToken) =>
                (await _operations.GetCommissionPaymentsReportAsync(rangeStart, rangeEnd, resolvedSiteName)).ToArray(),
            start,
            end,
            GetCurrentSiteName(),
            _windowCts.Token);

        _cascoReportCache = result;
        _cascoReportCacheStart = start;
        _cascoReportCacheEnd = end;
        _cascoReportCacheSiteName = siteName;
        return result;
    }

    private async Task WarmCascoReportCacheAsync()
    {
        if (_isClosing || _windowCts.IsCancellationRequested || !IsCascoBranch())
            return;

        try
        {
            await GetCascoReportDataAsync();
        }
        catch (OperationCanceledException) when (_isClosing || _windowCts.IsCancellationRequested)
        {
        }
        catch
        {
            // La precarga es opcional; no debe interrumpir la UI.
        }
    }

    private void InvalidateCascoRelationCache()
    {
        _cascoRelationCache = null;
        _cascoRelationCacheSiteName = null;
        _cascoRelationCacheStart = null;
        _cascoRelationCacheEnd = null;
        _cascoRelationCacheSearch = null;
    }

    private async Task<IReadOnlyList<LocalRelation>> GetCascoRelationsDataAsync()
    {
        var siteName = GetCurrentSiteName() ?? _currentBranch?.SiteName ?? string.Empty;
        var start = (RelationStart.SelectedDate ?? DateTime.Today).Date;
        var end = (RelationEnd.SelectedDate ?? RelationStart.SelectedDate ?? DateTime.Today).Date;
        var search = (RelationSearch.Text ?? string.Empty).Trim();

        if (_cascoRelationCache is not null
            && _cascoRelationCacheStart == start
            && _cascoRelationCacheEnd == end
            && string.Equals(_cascoRelationCacheSiteName, siteName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_cascoRelationCacheSearch, search, StringComparison.OrdinalIgnoreCase))
        {
            return _cascoRelationCache;
        }

        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        var rows = await CascoOperationsDataService.LoadRelationsAsync(
            _currentBranch,
            _branchCode,
            password,
            search,
            start,
            end,
            _windowCts.Token);

        _cascoRelationCache = rows;
        _cascoRelationCacheStart = start;
        _cascoRelationCacheEnd = end;
        _cascoRelationCacheSiteName = siteName;
        _cascoRelationCacheSearch = search;
        return rows;
    }

    private async Task WarmCascoRelationCacheAsync()
    {
        if (_isClosing || _windowCts.IsCancellationRequested || !IsCascoBranch())
            return;

        try
        {
            await GetCascoRelationsDataAsync();
        }
        catch (OperationCanceledException) when (_isClosing || _windowCts.IsCancellationRequested)
        {
        }
        catch
        {
            // La precarga es opcional; no debe interrumpir la UI.
        }
    }
    private async Task ExportWorkbookSheetAsync<T>(string fileName, string sheetName, IEnumerable<T> rows)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = fileName };
        if (dialog.ShowDialog() == true) await _output.ExportOpenXmlWorkbookAsync(sheetName, rows, dialog.FileName);
    }

    private (DateTime Start, DateTime End) GetReportRange()
    {
        var start = ReportStart.SelectedDate ?? DateTime.Today;
        var end = ReportEnd.SelectedDate ?? start;
        if (end < start)
            (start, end) = (end, start);
        return (start.Date, end.Date);
    }

    private async Task<IReadOnlyList<LocalRelation>> GetReportWorkbookRelationsAsync()
    {
        return MapReportWorkbookRelations(await GetOperationPreviewRowsAsync());
    }

    private async Task<IReadOnlyList<LocalCommission>> GetReportWorkbookCommissionsAsync()
    {
        if (string.Equals(_currentBranch?.Code ?? _branchCode, "CV", StringComparison.OrdinalIgnoreCase))
            return MapReportWorkbookCommissions(await GetCommissionPaymentPreviewRowsAsync());

        return await _pos.GetCommissionsAsync();
    }

    /// <summary>
    /// Cuando una llegada la atendieron varios vendedores, cada gafete trae el suyo en su
    /// fila; aqui se les agrega "Con FULANO (gafete)" para que se vea que van juntos sin
    /// tener que buscar el folio en las demas filas. Solo agrupa gafetes ocupados: los
    /// regresados ya no forman equipo con nadie.
    /// </summary>
    private static void FillBadgeVendorTeams(IEnumerable<LocalBadge> badges)
    {
        var groups = badges
            .Where(x => x.CanBulkReturn && !string.IsNullOrWhiteSpace(x.OperationFolio))
            .GroupBy(x => x.OperationFolio.Trim(), StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var members = group.ToList();
            if (members.Count < 2)
                continue;
            foreach (var badge in members)
            {
                var others = members
                    .Where(x => !ReferenceEquals(x, badge))
                    .Select(x => string.IsNullOrWhiteSpace(x.Vendor)
                        ? $"gafete {x.Number}"
                        : $"{x.Vendor.Trim()} ({x.Number})")
                    .ToList();
                badge.VendorTeam = others.Count == 0 ? string.Empty : "Con " + string.Join(", ", others);
            }
        }
    }

    private static IReadOnlyList<LocalRelation> MapReportWorkbookRelations(IEnumerable<LocalOperationsPreviewRow> rows)
    {
        return rows.Select((row, index) => new LocalRelation(
            index + 1,
            row.FolioLocal,
            row.FolioOriginal,
            string.Empty,
            row.Gafete,
            row.Taxista,
            row.Vendedor,
            row.Dejada,
            row.Notas,
            Source: "Casco Reporte",
            SourceUser: row.UsuarioPago,
            DateText: row.Fecha,
            Hotel: row.Hotel,
            Origin: row.Origen,
            Site: row.Sitio,
            Destination: row.Destino,
            Unit: row.Unidad,
            Plates: row.Placas,
            Phone: string.Empty,
            Nationality: string.Empty,
            TransportType: row.TipoServicio,
            Sale: row.Importe,
            Commission: row.Comision,
            CommissionPaid: row.Pago,
            PaymentMethod: string.Empty,
            PayoutStatus: row.Estatus,
            CommissionStatus: row.Estatus,
            PayoutTicket: row.TicketPago,
            TaxistaId: string.Empty,
            PayoutUser: row.UsuarioPago,
            PayoutDate: row.FechaPago,
            PayoutPaid: row.Pago,
            Passengers: row.Pax,
            SaleDetail: string.Empty,
            Currency: "MXN",
            RemotePaymentMethod: string.Empty,
            PaymentsJson: string.Empty,
            TotalAmount: row.Importe,
            CashAmount: 0m,
            CardAmount: 0m,
            DollarsAmount: 0m,
            ExchangeRate: 0m,
            AdultPassengers: row.Adulto,
            YouthPassengers: row.Joven,
            ChildPassengers: row.Nino)).ToArray();
    }

    // La pestana "comisiones" del Excel de Cuadre traia GetCommissionsAsync() (tabla local
    // LocalComisiones), que solo se llena cuando alguien le da PAGAR. Cualquier folio pendiente
    // -la mayoria, un dia normal- nunca aparecia ahi, aunque la pantalla de Comisiones en vivo
    // si lo mostrara. Ahora usa la misma fuente autoritativa que la pantalla, ya filtrada por
    // fecha. Confirmado 2026-08-27: el 27/08 tenia 5 comisiones pendientes en pantalla y 0 en
    // este reporte antes del cambio.
    private static IReadOnlyList<LocalCommission> MapReportWorkbookCommissions(IEnumerable<LocalCommissionBrowserRow> rows)
    {
        return rows.Select((row, index) => new LocalCommission(
            index + 1,
            row.Folio,
            row.SaleFolio,
            row.Gafete,
            row.Nombre,
            row.Fecha,
            row.VentaTotal,
            row.PagoComision,
            row.Pagado,
            row.Saldo,
            string.IsNullOrWhiteSpace(row.Estatus) ? "PENDIENTE" : row.Estatus)).ToArray();
    }

    private static IReadOnlyList<LocalCommission> MapReportWorkbookCommissions(IEnumerable<LocalCommissionPaymentPreviewRow> rows)
    {
        return rows.Select((row, index) => new LocalCommission(
            index + 1,
            row.Folio,
            row.FolioLocal,
            row.Gafete,
            row.Taxista,
            TryParsePreviewDate(row.FechaPago),
            row.Importe,
            row.Comision,
            row.Pago,
            Math.Max(row.Comision - row.Pago, 0m),
            string.IsNullOrWhiteSpace(row.Estatus) ? "PENDIENTE" : row.Estatus)).ToArray();
    }

    private static DateTime TryParsePreviewDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTime.Today;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant))
            return invariant;
        if (DateTime.TryParse(value, new CultureInfo("es-MX"), DateTimeStyles.AllowWhiteSpaces, out var mexican))
            return mexican;
        return DateTime.TryParse(value, out var generic) ? generic : DateTime.Today;
    }

    private void NormalizeRelationDateRange()
    {
        var start = ResolveOptionalDatePickerDate(RelationStart);
        var end = ResolveOptionalDatePickerDate(RelationEnd);
        if (!start.HasValue && !end.HasValue)
        {
            start = DateTime.Today;
            end = DateTime.Today;
        }
        else
        {
            start ??= end;
            end ??= start;
        }

        if (start.HasValue)
            RelationStart.SelectedDate = start.Value.Date;
        if (end.HasValue)
            RelationEnd.SelectedDate = end.Value.Date;
    }

    private void NormalizeReportDateRange()
    {
        var start = ResolveOptionalDatePickerDate(ReportStart);
        var end = ResolveOptionalDatePickerDate(ReportEnd);
        if (!start.HasValue && !end.HasValue)
        {
            start = DateTime.Today;
            end = DateTime.Today;
        }
        else
        {
            start ??= end;
            end ??= start;
        }

        if (start.HasValue)
            ReportStart.SelectedDate = start.Value.Date;
        if (end.HasValue)
            ReportEnd.SelectedDate = end.Value.Date;
    }

    private static DateTime? ResolveOptionalDatePickerDate(DatePicker picker)
    {
        if (picker.SelectedDate.HasValue)
            return picker.SelectedDate.Value.Date;

        var raw = (picker.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTime.TryParseExact(raw, "dd/MM/yyyy", new CultureInfo("es-MX"), DateTimeStyles.None, out var exact))
            return exact.Date;
        if (DateTime.TryParse(raw, new CultureInfo("es-MX"), DateTimeStyles.AllowWhiteSpaces, out var mexican))
            return mexican.Date;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant))
            return invariant.Date;
        return DateTime.TryParse(raw, out var generic) ? generic.Date : null;
    }
    private void ReportsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ReportOperationsGrid is null || ReportCommissionPaymentsGrid is null) return;
        if (!ReferenceEquals(sender, ReportsTabControl) || !ReferenceEquals(e.Source, ReportsTabControl))
            return;
        UpdateReportDashboard(
            ReportOperationsGrid.Items.OfType<LocalOperationsPreviewRow>().ToArray(),
            ReportCommissionPaymentsGrid.Items.OfType<LocalCommissionPaymentPreviewRow>().ToArray(),
            Array.Empty<LocalCut>());
    }
    private void UpdateReportDashboard(IReadOnlyList<LocalOperationsPreviewRow> operationRows, IReadOnlyList<LocalCommissionPaymentPreviewRow> paymentRows, IReadOnlyList<LocalCut> cuts)
    {
        var isPayments = ReportsTabControl.SelectedIndex == 1;
        var distinctTaxistas = operationRows
            .Select(x => x.Taxista)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ReportOperationsGrid.Visibility = isPayments ? Visibility.Collapsed : Visibility.Visible;
        ReportCommissionPaymentsGrid.Visibility = isPayments ? Visibility.Visible : Visibility.Collapsed;
        ReportHeroTitleText.Text = isPayments ? "Pagos de comisiones" : "Operaciones del dia";
        ReportPreviewHeaderText.Text = isPayments ? "Reporte de pagos generados" : "Reporte de operaciones total";
        ReportPreviewMetaText.Text = isPayments
            ? paymentRows.Sum(x => x.Pago).ToString("C2", CultureInfo.CurrentCulture)
            : string.Join(", ", operationRows.Select(x => x.Sitio).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("Sin referencia"));

        ReportMovementsLabelText.Text = isPayments ? "PAGOS" : "MOVIMIENTOS";
        ReportMovementsText.Text = (isPayments ? paymentRows.Count : operationRows.Count).ToString("N0", CultureInfo.InvariantCulture);
        ReportPaxLabelText.Text = isPayments ? "PAGADO" : "PAX ADULTOS";
        ReportPaxText.Text = isPayments
            ? paymentRows.Sum(x => x.Pago).ToString("C2", CultureInfo.CurrentCulture)
            : operationRows.Sum(x => x.Pax).ToString("N0", CultureInfo.InvariantCulture);
        ReportAmountLabelText.Text = isPayments ? "COMISION" : "IMPORTE";
        ReportAmountText.Text = (isPayments
            ? paymentRows.Sum(x => x.Comision)
            : operationRows.Sum(x => x.Importe)).ToString("C2", CultureInfo.CurrentCulture);
        ReportDriverLabelText.Text = isPayments ? "SALDO" : "TAXISTA / GUIA";
        ReportDriverText.Text = isPayments
            ? paymentRows.Sum(x => x.Comision - x.Pago).ToString("C2", CultureInfo.CurrentCulture)
            : distinctTaxistas.Length switch
            {
                0 => "Sin movimientos",
                1 => distinctTaxistas[0],
                _ => $"{distinctTaxistas.Length:N0} taxistas"
            };

        ReportResult.Text = isPayments
            ? $"Pagos: {paymentRows.Count:N0}  Pagado: {paymentRows.Sum(x => x.Pago):C2}  Comision: {paymentRows.Sum(x => x.Comision):C2}  Saldo: {paymentRows.Sum(x => x.Comision - x.Pago):C2}"
            : $"Movimientos: {operationRows.Count:N0}  PAX: {operationRows.Sum(x => x.Pax):N0}  Dejadas: {operationRows.Sum(x => x.Dejada):C2}  Importe: {operationRows.Sum(x => x.Importe):C2}  Comision: {operationRows.Sum(x => x.Comision):C2}  Pago: {operationRows.Sum(x => x.Pago):C2}  Cierres: {cuts.Count:N0}";
        ReportPrintPreviewText.Text = BuildReportPrintablePreview(operationRows, paymentRows, isPayments);
    }

    private static string BuildReportPrintablePreview(IReadOnlyList<LocalOperationsPreviewRow> operationRows, IReadOnlyList<LocalCommissionPaymentPreviewRow> paymentRows, bool isPayments)
    {
        var lines = new List<string>
        {
            isPayments ? "** REPORTE DE PAGOS DE COMISIONES **" : "** REPORTE DE OPERACIONES TOTAL **",
            string.Empty
        };

        if (isPayments)
        {
            lines.Add("folio original | folio local | fecha pago | taxista | gafete | transporte | dejada | importe | pago | estatus | usuario | ticket | sitio");
            lines.AddRange(paymentRows.Select(x => $"{x.Folio} | {x.FolioLocal} | {x.FechaPago} | {x.Taxista} | {x.Gafete} | {x.Transporte} | {x.Dejada:0.00} | {x.Importe:0.00} | {x.Pago:0.00} | {x.Estatus} | {x.UsuarioPago} | {x.TicketPago} | {x.Sitio}"));
        }
        else
        {
            lines.Add("folio original | folio local | fecha | taxista | gafete | pax | hotel | origen | destino | sucursal | unidad | placas | tipo servicio | dejada | importe | comision | estado pago | fecha pago | usuario pago | ticket pago | notas");
            lines.AddRange(operationRows.Select(x => $"{x.FolioOriginal} | {x.FolioLocal} | {x.Fecha} | {x.Taxista} | {x.Gafete} | {x.Pax} | {x.Hotel} | {x.Origen} | {x.Destino} | {x.Sitio} | {x.Unidad} | {x.Placas} | {x.TipoServicio} | {x.Dejada:0.00} | {x.Importe:0.00} | {x.Comision:0.00} | {x.Estatus} | {x.FechaPago} | {x.UsuarioPago} | {x.TicketPago} | {x.Notas}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        _windowReady = false;
        Tabs.SelectionChanged -= Tabs_SelectionChanged;
        _driverSearchCts?.Cancel();
        _transportSearchCts?.Cancel();
        _windowCts.Cancel();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _driverSearchCts?.Dispose();
        _transportSearchCts?.Dispose();
        _windowCts.Dispose();
        base.OnClosed(e);
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_isClosing || _windowCts.IsCancellationRequested)
            return;

        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_isClosing || _windowCts.IsCancellationRequested)
        {
        }
        catch (TaskCanceledException) when (_isClosing || _windowCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (_isClosing)
                return;

            await _errors.LogAsync(_user, "Operaciones", "Error de pantalla", ex);
            if (!_isClosing)
                WebDialogWindow.Show(this, "No se pudo completar la operacion. " + ex.Message, "Control Taxi", "!");
        }
    }
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static decimal? NumberOrNull(string value) => string.IsNullOrWhiteSpace(value) ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
    private static int? IntOrNull(string value) => string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, CultureInfo.InvariantCulture);
    private static long? Value(object? value) => value is long id ? id : value is int number ? number : null;
    private static string SelectedText(System.Windows.Controls.ComboBox comboBox) => ((System.Windows.Controls.ComboBoxItem)comboBox.SelectedItem).Content.ToString() ?? string.Empty;
}
