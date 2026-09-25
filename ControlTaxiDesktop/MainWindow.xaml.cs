using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Navieras;
using ControlTaxiDesktop.Navieras.Views;
using ControlTaxiDesktop.Services;

namespace ControlTaxiDesktop;

public partial class MainWindow : Window
{
    private readonly LocalDatabase _database = new();
    private readonly LocalUserRepository _users;
    private readonly LocalErrorLogger _errors;
    private readonly BranchConfigurationService _branchService = new();
    private readonly string[] _startupArgs = Environment.GetCommandLineArgs();
    private DesktopSession? _session;

    private static readonly Module[] Modules =
    [
        new("Registro diario", "RegistroDiario", "Altas y consulta de registros."),
        new("Registro aplicacion movil", "RegistroDiario", "Registros, taxistas, tarifas, hoteles y gafetes."),
        new("Ventas", "Comisiones", "Ventas locales, productos, cobros y folios."),
        new("Comisiones", "Comisiones", "Calculos, abonos y liquidaciones."),
        new("Configuracion de comisiones", "ConfiguracionComisiones", "Catalogos, vigencias, auditoria y simulador de comisiones."),
        new("Transportes", "Transportes", "Catalogo de transportes."),
        new("Taxistas", "Taxistas", "Catalogo de taxistas."),
        new("Gafetes", "Gafetes", "Asignacion, devolucion y consulta de gafetes."),
        new("Relaciones", "Relaciones", "Relacion ticket-taxista y dejadas."),
        new("Gastos", "Gastos", "Registro y consulta de gastos."),
        new("Cortes", "Cortes", "Calculo y cierre de cortes."),
        new("Reportes", "Reportes", "Reportes y exportaciones."),
        new("Usuarios", "Usuarios", "Usuarios, permisos y autenticacion local."),
        new("Navieras", "Navieras", "Control integral de servicios, folios, catálogos y administracion de navieras."),
        new("Portal", "Reportes", "Dashboard, operaciones, comisiones, vendedores, productos, catalogos y alta local.")
    ];

    public MainWindow()
    {
        _users = new LocalUserRepository(_database);
        _errors = new LocalErrorLogger(_database);
        InitializeComponent();
        ApplyWindowBounds();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();
            await _users.EnsureTestUserAsync();
            InitializeBranchSelector();
            LoginMessage.Text = _database.IsTestDatabase
                ? "Base temporal local activa. Acceso de prueba: admin / admin."
                : "Sistema listo. Selecciona Plaza 28 e inicia sesion.";
            await TryAutoLoginFromArgsAsync();
        }
        catch (Exception ex)
        {
            await _errors.LogAsync("Sistema", "Inicio", "Inicializar base local", ex);
            LoginMessage.Text = "No se pudo inicializar la base local. Revisa permisos de escritura en la carpeta de la aplicacion.";
            WebDialogWindow.Show(this, LoginMessage.Text + Environment.NewLine + ex.Message, "Control Taxi", "!");
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e) => await LoginAsync();

    private void NavierasShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        var authService = new NavierasModuleBootstrapper().CreateAuthService();
        var login = new NavierasLoginWindow(authService) { Owner = this };
        if (login.ShowDialog() == true && login.AuthenticatedUser is not null)
            new NavierasWindow { Owner = this }.ShowDialog();
    }

    private async void PasswordTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await LoginAsync();
    }

    private async Task LoginAsync()
    {
        try
        {
            LoginMessage.Text = string.Empty;
            var result = await _users.AuthenticateAsync(UserNameTextBox.Text, PasswordTextBox.Password, SelectedBranchCode());
            if (!result.Success)
            {
                LoginMessage.Text = result.FailureReason switch
                {
                    LocalUserRepository.AuthenticationFailureReason.UserNotFound => "Usuario no encontrado.",
                    LocalUserRepository.AuthenticationFailureReason.UserInactive => "Usuario inactivo.",
                    LocalUserRepository.AuthenticationFailureReason.IncorrectPassword => "Contrasena incorrecta.",
                    LocalUserRepository.AuthenticationFailureReason.DatabaseStructureError => "Error de estructura de la base local.",
                    LocalUserRepository.AuthenticationFailureReason.BranchNotAllowed => "Tu usuario no tiene acceso a la sucursal seleccionada.",
                    LocalUserRepository.AuthenticationFailureReason.RemoteServerUnavailable => "No se pudo conectar al servidor SQL de la sucursal seleccionada. Revisa la configuracion cifrada de conexion.",
                    _ => "Usuario o contrasena incorrectos."
                };
                return;
            }

            _session = result.Session;
            var session = _session!;
            ModulesList.ItemsSource = Modules.Where(x => session.Permissions.Contains(x.Permission)).ToArray();
            LoginPanel.Visibility = Visibility.Collapsed;
            DesktopPanel.Visibility = Visibility.Visible;
            UserStatus.Text = $"{session.UserName} - {session.Role} - Sucursal: {FormatBranchName(session.BranchCode)}";
            DatabaseStatus.Text = session.IsTestDatabase ? "Base temporal local" : $"Sucursal: {FormatBranchName(session.BranchCode)}";
            RefreshModuleTiles();
            ModulesList.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            await _errors.LogAsync(UserNameTextBox.Text, "Login", "Autenticar", ex);
            LoginMessage.Text = "No se pudo iniciar sesion. " + ex.Message;
        }
    }

    private async Task TryAutoLoginFromArgsAsync()
    {
        var user = GetStartupArgValue("--auto-user");
        var password = GetStartupArgValue("--auto-password");
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))
            return;

        UserNameTextBox.Text = user;
        PasswordTextBox.Password = password;
        await LoginAsync();

        var moduleName = GetStartupArgValue("--open-module");
        if (_session is null || string.IsNullOrWhiteSpace(moduleName))
            return;

        var module = Modules.FirstOrDefault(x => string.Equals(x.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module is not null && _session.Permissions.Contains(module.Permission))
            _ = Dispatcher.BeginInvoke(new Action(() => OpenModule(module)));
    }

    private string? GetStartupArgValue(string key)
    {
        for (var i = 0; i < _startupArgs.Length - 1; i++)
        {
            if (string.Equals(_startupArgs[i], key, StringComparison.OrdinalIgnoreCase))
                return _startupArgs[i + 1];
        }

        return null;
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _session = null;
        PasswordTextBox.Clear();
        LoginPanel.Visibility = Visibility.Visible;
        DesktopPanel.Visibility = Visibility.Collapsed;
        UserNameTextBox.Focus();
    }

    private void ChecklistButton_Click(object sender, RoutedEventArgs e)
    {
        new ChecklistValidacionFinal { Owner = this }.ShowDialog();
    }

    private void RefreshModuleTiles()
    {
        if (_session is null)
            return;

        foreach (var button in FindVisualChildren<Button>(DesktopPanel))
        {
            if (button.Tag is not string moduleName)
                continue;

            var module = Modules.FirstOrDefault(x => string.Equals(x.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            if (module is null)
                continue;

            if (string.Equals(module.Name, "Navieras", StringComparison.OrdinalIgnoreCase))
            {
                button.Visibility = Visibility.Visible;
                continue;
            }

            button.Visibility = _session.Permissions.Contains(module.Permission) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ModulesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModulesList.SelectedItem is not Module module)
            return;

        PageTitle.Text = module.Name;
        PageDescription.Text = module.Description;
        PageDetail.Text = "Haz doble clic en el modulo para abrir su pantalla local. No se redirige al sistema web ni realiza llamadas de red.";
    }

    private void ModulesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_session is null || ModulesList.SelectedItem is not Module module)
            return;

        OpenModule(module);
    }

    private void ModuleTile_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || sender is not Button button || button.Tag is not string moduleName)
            return;

        var module = Modules.FirstOrDefault(x => string.Equals(x.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (module is null)
        {
            WebDialogWindow.Show(this, "Modulo no encontrado.", "Control Taxi", "!");
            return;
        }

        if (!_session.Permissions.Contains(module.Permission) && !string.Equals(module.Name, "Navieras", StringComparison.OrdinalIgnoreCase))
        {
            WebDialogWindow.Show(this, "Tu usuario no tiene permiso para abrir este modulo.", "Control Taxi", "!");
            return;
        }

        OpenModule(module);
    }

    private void OpenModule(Module module)
    {
        if (_session is null)
            return;

        var userName = _session.UserName;
        var branchCode = _session.BranchCode;
        if (module.Name is "Usuarios")
            ShowModule(new UserAdminWindow(_database, branchCode));
        else if (module.Name is "Navieras")
        {
            var authService = new NavierasModuleBootstrapper().CreateAuthService();
            var login = new NavierasLoginWindow(authService) { Owner = this };
            if (login.ShowDialog() == true && login.AuthenticatedUser is not null)
                ShowModule(new NavierasWindow());
        }
        else if (module.Name is "Portal")
            ShowModule(new PortalWindow(_database, userName));
        else if (module.Name is "Configuracion de comisiones")
            ShowModule(new CommissionSettingsWindow(_database, userName, _session.Permissions.Contains("ConfiguracionComisiones"), branchCode));
        else if (module.Name is "Ventas" or "Comisiones" or "Cortes" or "Reportes")
            ShowModule(new PosWindow(_database, userName, branchCode, module.Name));
        else
            ShowModule(new OperationsWindow(_database, userName, branchCode, module.Name));
    }

    /// <summary>
    /// Abre un modulo ocupando toda la pantalla y escondiendo el menu mientras dura.
    /// Se siente como si el menu se transformara en el modulo y regresara al cerrarlo, en vez
    /// de apilar una segunda ventana chica encima de la primera.
    /// </summary>
    private void ShowModule(Window window)
    {
        window.Owner = this;
        window.WindowState = WindowState.Maximized;
        Hide();
        try
        {
            window.ShowDialog();
        }
        finally
        {
            // El finally garantiza que el menu vuelva aunque el modulo truene al abrir;
            // si no, la app se quedaria sin ninguna ventana visible.
            Show();
            WindowState = WindowState.Maximized;
            Activate();
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static string FormatBranchName(string branchCode) => branchCode switch
    {
        "CV" => "Casco Viejo",
        "ALL" => "Ambas",
        _ => "Plaza 28"
    };

    private void InitializeBranchSelector()
    {
        if (BranchSelector is null)
            return;

        BranchSelector.ItemsSource = _branchService.GetAllBranches()
            .Where(x => !string.Equals(x.Code, "ALL", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Code)
            .ToArray();
        BranchSelector.SelectedValue = DefaultLoginBranchCode();
        if (BranchSelector.SelectedItem is null)
            BranchSelector.SelectedIndex = 0;
    }

    private string SelectedBranchCode() => BranchSelector?.SelectedValue?.ToString()?.Trim().ToUpperInvariant() switch
    {
        "P28" => "P28",
        "CV" => "CV",
        _ => DefaultLoginBranchCode()
    };

    private static string DefaultLoginBranchCode()
    {
        foreach (var path in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var normalized = path.Replace('\\', '/').ToUpperInvariant();
            if (normalized.Contains("RELEASE-CONTROLTAXI-PLAZA28-PRODUCCION")
                || normalized.Contains("PLAZA28")
                || normalized.Contains("PLAZA 28")
                || normalized.Contains("DESKTOP TAXIS")
                || normalized.Contains("SYNCTAXI_PLAZA28"))
            {
                return "P28";
            }

            if (normalized.Contains("RELEASE-CONTROLTAXI-CASCO-PRODUCCION")
                || normalized.Contains("CASCO NUEVO")
                || normalized.Contains("CASCO VIEJO"))
            {
                return "CV";
            }
        }

        return "P28";
    }

    private void ApplyWindowBounds()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Manual;
        ShowInTaskbar = true;

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1260, Math.Max(980, workArea.Width - 80));
        Height = Math.Min(820, Math.Max(660, workArea.Height - 80));
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private sealed record Module(string Name, string Permission, string Description);
}
