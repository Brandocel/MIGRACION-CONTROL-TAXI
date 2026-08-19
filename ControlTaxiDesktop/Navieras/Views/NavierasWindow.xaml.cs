using System.Windows;
using System.Windows.Controls;
using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Application.Services;
using ControlTaxiDesktop.Navieras.Domain;
using ControlTaxiDesktop.Navieras.ViewModels;

namespace ControlTaxiDesktop.Navieras.Views;

public partial class NavierasWindow : Window
{
    private readonly NavierasModuleBootstrapper _bootstrapper = new();
    private readonly INavierasAuthService _authService;
    private readonly IServicesService _servicesService;
    private readonly IFoliosService _foliosService;
    private readonly ICatalogsService _catalogsService;
    private readonly IUsersService _usersService;
    private readonly IRolesService _rolesService;
    private readonly NavierasWindowViewModel _viewModel;
    private NavierasUser? _currentUser;

    public NavierasWindow()
    {
        _authService = _bootstrapper.CreateAuthService();
        _servicesService = _bootstrapper.CreateServicesService();
        _foliosService = _bootstrapper.CreateFoliosService();
        _catalogsService = _bootstrapper.CreateCatalogsService();
        _usersService = _bootstrapper.CreateUsersService();
        _rolesService = _bootstrapper.CreateRolesService();
        _viewModel = new NavierasWindowViewModel(
            _bootstrapper.CreateDashboardService(),
            _servicesService,
            _foliosService,
            _bootstrapper.CreateTimelineService(),
            _catalogsService,
            _usersService,
            _rolesService,
            _bootstrapper.CreateReportsService());
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += NavierasWindow_Loaded;
    }

    private async void NavierasWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _currentUser = await _authService.RestoreAsync();
        }
        catch
        {
            _currentUser = null;
        }

        if (_currentUser is null)
        {
            var login = new NavierasLoginWindow(_authService) { Owner = this };
            if (login.ShowDialog() != true || login.AuthenticatedUser is null)
            {
                Close();
                return;
            }

            _currentUser = login.AuthenticatedUser;
        }

        _viewModel.SetSession(_currentUser, _authService.CurrentBaseUrl);
        SessionTextBlock.Text = _viewModel.SessionText;
        ApplySessionPermissions(_currentUser);
        CatalogTypeComboBox.SelectedIndex = 0;
        PassengerGroupingComboBox.SelectedIndex = 0;
        FromDatePicker.SelectedDate = DateTime.Today;
        ToDatePicker.SelectedDate = DateTime.Today;
        await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            ApplyReportFiltersFromUi();
            await _viewModel.LoadAllAsync(SelectedCatalogType());
            BindViewModel();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private void BindViewModel()
    {
        DashboardBoatsTextBlock.Text = _viewModel.Dashboard.Boats.ToString();
        DashboardServicesTextBlock.Text = _viewModel.Dashboard.Services.ToString();
        DashboardPassengersTextBlock.Text = _viewModel.Dashboard.Passengers.ToString();
        DashboardInOperationTextBlock.Text = _viewModel.Dashboard.InOperation.ToString();
        DashboardFinishedTextBlock.Text = _viewModel.Dashboard.Finished.ToString();
        DashboardDelayedTextBlock.Text = _viewModel.Dashboard.Delayed.ToString();

        DashboardServicesGrid.ItemsSource = _viewModel.Services.Services.Take(12).ToArray();
        QuickActionsGrid.ItemsSource = _viewModel.Dashboard.QuickActions;
        UpcomingArrivalsGrid.ItemsSource = _viewModel.Dashboard.UpcomingArrivals;
        ShipsOfDayGrid.ItemsSource = _viewModel.Dashboard.ShipsOfDay;
        DockMapGrid.ItemsSource = _viewModel.Dashboard.DockMap;
        IndicatorsGrid.ItemsSource = _viewModel.Dashboard.Indicators;
        ServicesGrid.ItemsSource = _viewModel.Services.Services;
        TimelineGrid.ItemsSource = _viewModel.Operations.Timeline;
        FoliosGrid.ItemsSource = _viewModel.Operations.Folios;
        FolioServiceComboBox.ItemsSource = _viewModel.Services.Services;
        FolioServiceComboBox.SelectedIndex = _viewModel.Services.Services.Count > 0 ? 0 : -1;
        CatalogGrid.ItemsSource = _viewModel.Catalogs.CatalogItems;
        CatalogDetailGrid.ItemsSource = _viewModel.Catalogs.CatalogItems;
        DailyReportGrid.ItemsSource = _viewModel.Reports.DailyReports;
        CompanyReportGrid.ItemsSource = _viewModel.Reports.CompanyReports;
        ShipReportGrid.ItemsSource = _viewModel.Reports.ShipReports;
        GuideReportGrid.ItemsSource = _viewModel.Reports.GuideReports;
        CaptainReportGrid.ItemsSource = _viewModel.Reports.CaptainReports;
        PassengerReportGrid.ItemsSource = _viewModel.Reports.PassengerReports;
        PunctualityReportGrid.ItemsSource = _viewModel.Reports.PunctualityReports;
        BraceletReportGrid.ItemsSource = _viewModel.Reports.BraceletReports;
        ExportPreviewTextBox.Text = _viewModel.Reports.ExportPreviewText;
        UsersGrid.ItemsSource = _viewModel.Administration.Users;
        RolesGrid.ItemsSource = _viewModel.Administration.Roles;
        ReportStatusTextBlock.Text = _viewModel.Reports.ReportStatus;
        StatusTextBlock.Text = _viewModel.StatusText;
    }

    private void ApplyReportFiltersFromUi()
    {
        _viewModel.Reports.FromDate = DateOnly.FromDateTime(FromDatePicker.SelectedDate ?? DateTime.Today);
        _viewModel.Reports.ToDate = DateOnly.FromDateTime(ToDatePicker.SelectedDate ?? DateTime.Today);
        _viewModel.Reports.PassengerGrouping = ((PassengerGroupingComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "service").Trim();
    }

    private NavierasServiceSummary? SelectedService() => ServicesGrid.SelectedItem as NavierasServiceSummary;
    private NavierasOperationFolio? SelectedFolio() => FoliosGrid.SelectedItem as NavierasOperationFolio;
    private NavierasCatalogItem? SelectedCatalogItem() => CatalogGrid.SelectedItem as NavierasCatalogItem;
    private NavierasEditableUser? SelectedUser() => UsersGrid.SelectedItem as NavierasEditableUser;
    private NavierasRole? SelectedRole() => RolesGrid.SelectedItem as NavierasRole;

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var tabIndex))
            MainTabControl.SelectedIndex = tabIndex;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAllAsync();

    private async void RefreshReportsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyReportFiltersFromUi();
            await _viewModel.LoadReportsAsync();
            BindViewModel();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        await _authService.LogoutAsync();
        _currentUser = null;

        var login = new NavierasLoginWindow(_authService) { Owner = this };
        if (login.ShowDialog() != true || login.AuthenticatedUser is null)
        {
            Close();
            return;
        }

        _currentUser = login.AuthenticatedUser;
        _viewModel.SetSession(_currentUser, _authService.CurrentBaseUrl);
        SessionTextBlock.Text = _viewModel.SessionText;
        ApplySessionPermissions(_currentUser);

        await LoadAllAsync();
    }

    private void ApplySessionPermissions(NavierasUser user)
    {
        var permissions = user.Permissions;
        var canLogout = permissions.Contains("desktop.auth.logout");
        var canRefresh = permissions.Contains("desktop.health") || permissions.Contains("desktop.auth.me");

        LogoutButton.IsEnabled = canLogout;
        RefreshButton.IsEnabled = canRefresh;
        ChangePasswordButton.IsEnabled = false;
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var currentDialog = new NavierasTextPromptWindow("Contrasena actual", "Escribe la contrasena actual.") { Owner = this };
        if (currentDialog.ShowDialog() != true)
            return;

        var newDialog = new NavierasPasswordResetWindow { Owner = this };
        if (newDialog.ShowDialog() != true)
            return;

        try
        {
            await _authService.ChangePasswordAsync(_currentUser.UserId, currentDialog.ResultText, newDialog.NewPassword, newDialog.ConfirmPassword);
            StatusTextBlock.Text = "Contrasena actualizada correctamente.";
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void NewServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var dialog = new NavierasServiceEditorWindow(_viewModel.Catalogs.Companies, _viewModel.Catalogs.Boats, _viewModel.Catalogs.Docks, _viewModel.Catalogs.Guides, _viewModel.Catalogs.Captains) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Request is null)
            return;

        try
        {
            await _servicesService.CreateServiceAsync(dialog.Request, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void EditServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var service = SelectedService();
        if (service is null)
            return;

        var dialog = new NavierasServiceEditorWindow(_viewModel.Catalogs.Companies, _viewModel.Catalogs.Boats, _viewModel.Catalogs.Docks, _viewModel.Catalogs.Guides, _viewModel.Catalogs.Captains, service) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Request is null)
            return;

        try
        {
            await _servicesService.UpdateServiceAsync(dialog.Request, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void RescheduleServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var service = SelectedService();
        if (service is null)
            return;

        var dialog = new NavierasServiceEditorWindow(_viewModel.Catalogs.Companies, _viewModel.Catalogs.Boats, _viewModel.Catalogs.Docks, _viewModel.Catalogs.Guides, _viewModel.Catalogs.Captains, service) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Request is null)
            return;

        try
        {
            await _servicesService.RescheduleServiceAsync(dialog.Request, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void RegisterArrivalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var service = SelectedService();
        if (service is null)
            return;

        var dialog = new NavierasArrivalWindow(_viewModel.Catalogs.Docks, _viewModel.Catalogs.Guides, _viewModel.Catalogs.Captains, service) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Request is null)
            return;

        try
        {
            await _servicesService.RegisterArrivalAsync(dialog.Request, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void RegisterDepartureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var service = SelectedService();
        if (service is null)
            return;

        var dialog = new NavierasDepartureWindow(service) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Request is null)
            return;

        try
        {
            await _servicesService.RegisterDepartureAsync(dialog.Request, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void ValidateBraceletButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var service = SelectedService();
        if (service is null)
            return;

        var dialog = new NavierasTextPromptWindow("Validar brazalete", "Escribe o confirma el folio del brazalete.", service.BraceletFolio ?? string.Empty) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _servicesService.ValidateBraceletAsync(service.ServiceId, dialog.ResultText, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void FinalizeServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var service = SelectedService();
        if (service is null)
            return;

        if (MessageBox.Show(this, $"Se finalizara el servicio {service.ServiceFolio}.", "Navieras", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await _servicesService.FinalizeServiceAsync(service.ServiceId, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void CancelServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var service = SelectedService();
        if (service is null)
            return;

        var dialog = new NavierasTextPromptWindow("Cancelar servicio", "Indica el motivo de cancelacion.") { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _servicesService.CancelServiceAsync(service.ServiceId, dialog.ResultText, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void SaveFoliosButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null || FolioServiceComboBox.SelectedItem is not NavierasServiceSummary service)
            return;

        var folios = FolioBatchTextBox.Text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (folios.Length == 0)
            return;

        try
        {
            await _foliosService.SaveOperationFoliosBatchAsync(service.ServiceId, service.BoatId, service.CompanyId, service.OperationDate, folios, _currentUser.Username);
            FolioBatchTextBox.Clear();
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void DeleteFolioButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var folio = SelectedFolio();
        if (folio is null)
            return;

        try
        {
            await _foliosService.DeleteOperationFolioAsync(folio.FolioId, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void ValidateFoliosButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null || FolioServiceComboBox.SelectedItem is not NavierasServiceSummary service)
            return;

        var folios = FolioBatchTextBox.Text
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (folios.Length == 0)
            return;

        try
        {
            var validated = await _foliosService.ValidateOperationFoliosAsync(service.ServiceId, service.BoatId, service.CompanyId, service.OperationDate, folios, _currentUser.Username);
            StatusTextBlock.Text = validated.Count == 0
                ? "La API no devolvio folios validados para el lote."
                : $"Folios validados por API: {validated.Count}.";
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void CatalogTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        try
        {
            await _viewModel.ReloadCatalogAsync(SelectedCatalogType());
            BindViewModel();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void SaveCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var selected = SelectedCatalogItem();
        var type = SelectedCatalogType();
        var codeDialog = new NavierasTextPromptWindow("Clave", $"Captura la clave para {type}.", selected?.Code ?? string.Empty) { Owner = this };
        if (codeDialog.ShowDialog() != true)
            return;

        var nameDialog = new NavierasTextPromptWindow("Nombre", $"Captura el nombre para {type}.", selected?.Name ?? string.Empty) { Owner = this };
        if (nameDialog.ShowDialog() != true)
            return;

        string? secondary = null;
        string? parentId = null;

        if (string.Equals(type, "boats", StringComparison.OrdinalIgnoreCase))
        {
            var parentDialog = new NavierasTextPromptWindow("Compania", "Escribe el ID de la compania para el barco.", selected?.ParentId ?? string.Empty) { Owner = this };
            if (parentDialog.ShowDialog() != true)
                return;

            parentId = parentDialog.ResultText;
            var vesselDialog = new NavierasTextPromptWindow("Tipo embarcacion", "Opcional: tipo de embarcacion.", selected?.Subtitle ?? string.Empty) { Owner = this };
            if (vesselDialog.ShowDialog() == true)
                secondary = vesselDialog.ResultText;
        }
        else if (type is "guides" or "docks")
        {
            var secondaryDialog = new NavierasTextPromptWindow(type == "guides" ? "Numero de guia" : "Zona / color", "Captura el valor complementario.", selected?.Subtitle ?? string.Empty) { Owner = this };
            if (secondaryDialog.ShowDialog() == true)
                secondary = secondaryDialog.ResultText;
        }

        try
        {
            await _catalogsService.SaveCatalogItemAsync(new NavierasCatalogUpsertRequest(type, selected?.Id, codeDialog.ResultText, nameDialog.ResultText, parentId, secondary), _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void ToggleCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var item = SelectedCatalogItem();
        if (item is null)
            return;

        try
        {
            await _catalogsService.SetCatalogItemActiveAsync(item.Type, item.Id, !item.IsActive, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void SaveUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var selected = SelectedUser();
        var usernameDialog = new NavierasTextPromptWindow("Usuario", "Nombre de usuario.", selected?.User.Username ?? string.Empty) { Owner = this };
        if (usernameDialog.ShowDialog() != true)
            return;

        var nameDialog = new NavierasTextPromptWindow("Nombre", "Nombre completo.", selected?.User.FullName ?? string.Empty) { Owner = this };
        if (nameDialog.ShowDialog() != true)
            return;

        var emailDialog = new NavierasTextPromptWindow("Correo", "Correo electronico opcional.", string.Empty) { Owner = this };
        if (emailDialog.ShowDialog() != true)
            return;

        var roleDialog = new NavierasTextPromptWindow("Rol", "ID del rol para el usuario.", _viewModel.Administration.Roles.FirstOrDefault(x => x.Name == selected?.User.Role)?.RoleId ?? string.Empty) { Owner = this };
        if (roleDialog.ShowDialog() != true)
            return;

        var passwordDialog = new NavierasTextPromptWindow("Contrasena", "Contrasena inicial o nueva. Deja vacio para mantenerla.", string.Empty) { Owner = this };
        passwordDialog.ShowDialog();

        try
        {
            await _usersService.SaveUserAsync(
                new NavierasUserUpsertRequest(
                    selected?.User.UserId,
                    usernameDialog.ResultText,
                    nameDialog.ResultText,
                    emailDialog.ResultText,
                    passwordDialog.ResultText,
                    roleDialog.ResultText,
                    selected?.IsActive ?? true,
                    selected?.IsBlocked ?? false),
                _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void ToggleUserBlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var user = SelectedUser();
        if (user is null)
            return;

        try
        {
            await _usersService.SetUserBlockedAsync(user.User.UserId, !user.IsBlocked, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void ResetUserPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var user = SelectedUser();
        if (user is null)
            return;

        var dialog = new NavierasPasswordResetWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _usersService.ResetUserPasswordAsync(user.User.UserId, dialog.NewPassword, dialog.ConfirmPassword, _currentUser.Username);
            StatusTextBlock.Text = $"Contrasena restablecida para {user.User.Username}.";
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void SaveRoleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var selected = SelectedRole();
        var keyDialog = new NavierasTextPromptWindow("Clave", "Clave del rol.", selected?.Key ?? string.Empty) { Owner = this };
        if (keyDialog.ShowDialog() != true)
            return;

        var nameDialog = new NavierasTextPromptWindow("Nombre", "Nombre del rol.", selected?.Name ?? string.Empty) { Owner = this };
        if (nameDialog.ShowDialog() != true)
            return;

        var descriptionDialog = new NavierasTextPromptWindow("Permisos", "Captura las claves de permisos separadas por coma.", selected is null ? string.Empty : string.Join(", ", selected.Permissions)) { Owner = this };
        if (descriptionDialog.ShowDialog() != true)
            return;

        var permissions = descriptionDialog.ResultText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            await _rolesService.SaveRoleAsync(new NavierasRoleUpsertRequest(selected?.RoleId, keyDialog.ResultText, nameDialog.ResultText, string.Empty, selected?.IsActive ?? true, permissions), _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private async void ToggleRoleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var role = SelectedRole();
        if (role is null)
            return;

        try
        {
            await _rolesService.SetRoleActiveAsync(role.RoleId, !role.IsActive, _currentUser.Username);
            await LoadAllAsync();
        }
        catch (Exception ex)
        {
            HandleException(ex);
        }
    }

    private void PassengerGroupingComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            _viewModel.Reports.PassengerGrouping = ((PassengerGroupingComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "service").Trim();
    }

    private string SelectedCatalogType()
        => ((CatalogTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "companies").Trim();

    private void HandleException(Exception ex)
    {
        var message = ex switch
        {
            NavierasApiException { StatusCode: 401 } => "401: la sesion de Navieras expiro o no esta autorizada.",
            NavierasApiException { StatusCode: 403 } => "403: el usuario no tiene permiso para esta accion en Navieras.",
            NavierasApiException { StatusCode: 404 } => "404: el recurso solicitado no existe en la API de Navieras.",
            NavierasApiException { StatusCode: 409 } => "409: la API reporto un conflicto de estado o datos duplicados.",
            NavierasApiException { StatusCode: 422 } => "422: la API rechazo la solicitud por validaciones de negocio.",
            NavierasApiException { StatusCode: 500 } => "500: la API de Navieras devolvio un error interno.",
            _ => ex.Message
        };

        StatusTextBlock.Text = message;
        ReportStatusTextBlock.Text = message;
        MessageBox.Show(this, message, "Navieras", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
