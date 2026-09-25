using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;

namespace ControlTaxiDesktop;

public partial class UserAdminWindow : Window
{
    private readonly LocalUserRepository _users;
    private readonly LocalErrorLogger _errors;
    private readonly BranchConfigurationService _branches = new();
    private readonly string _branchCode;
    private bool _isInitializing;

    public UserAdminWindow(LocalDatabase database, string branchCode = "P28")
    {
        try
        {
            _isInitializing = true;
            _branchCode = NormalizeBranchCode(branchCode);
            _users = new LocalUserRepository(database);
            _errors = new LocalErrorLogger(database);
            InitializeComponent();
            // AutorizarPagoComision NO se marca por defecto: es una facultad restringida (solo
            // Ester, por ejemplo), y marcarla en automatico en cada usuario nuevo anularia el
            // control de "el pago dejo de ser libre" que se acaba de construir.
            PermissionsList.ItemsSource = LocalUserRepository.AllModules.Select(x => new CheckBox
            {
                Content = x,
                IsChecked = !string.Equals(x, "AutorizarPagoComision", StringComparison.OrdinalIgnoreCase),
                Style = (Style)FindResource("PermissionCheck")
            }).ToArray();
            InitializeBranchOptions();
            UserRole.SelectedIndex = 0;
            UserStatus.SelectedIndex = 0;
            _isInitializing = false;
            Loaded += async (_, _) => await RunAsync(RefreshAsync);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Error Usuarios", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private async Task RefreshAsync() => UsersGrid.ItemsSource = await _users.GetUsersAsync(_branchCode);

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await _errors.LogAsync("Sistema", "Usuarios", "Error de pantalla", ex);
            MessageBox.Show(ex.ToString(), "Error Usuarios", MessageBoxButton.OK, MessageBoxImage.Error);
            WebDialogWindow.Show(this, "No se pudo cargar Usuarios. " + ex.Message, "Control Taxi", "!");
        }
    }

    private async void SaveUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var permissions = PermissionsList.Items.OfType<CheckBox>()
                .Where(x => x.IsChecked == true)
                .Select(x => x.Content?.ToString() ?? string.Empty);
            var userName = (UserName.Text ?? string.Empty).Trim();
            var role = SelectedText(UserRole);
            var status = SelectedText(UserStatus);
            var branchCode = SelectedValue(UserBranchCode);
            if (string.IsNullOrWhiteSpace(userName))
            {
                WebDialogWindow.Show(this, "El usuario es obligatorio.", "Control Taxi", "!");
                UserName.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(role))
            {
                WebDialogWindow.Show(this, "El rol es obligatorio.", "Control Taxi", "!");
                return;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                WebDialogWindow.Show(this, "El estatus es obligatorio.", "Control Taxi", "!");
                return;
            }

            if (string.IsNullOrWhiteSpace(branchCode))
            {
                branchCode = "P28";
            }

            if (string.Equals(branchCode, "ALL", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(role, "Administrador", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase))
            {
                WebDialogWindow.Show(this, "Solo Administrador o Supervisor puede asignar la sucursal Ambas.", "Control Taxi", "!");
                return;
            }

            var isNew = UsersGrid.SelectedItem is not LocalUserRow;
            if (isNew && string.IsNullOrWhiteSpace(UserPassword.Password))
            {
                WebDialogWindow.Show(this, "La contraseña es obligatoria para un usuario nuevo.", "Control Taxi", "!");
                UserPassword.Focus();
                return;
            }

            await _users.SaveUserAsync(userName, UserPassword.Password, role, status, branchCode, permissions, _branchCode);
            UserPassword.Clear();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _errors.LogAsync(UserName.Text, "Usuarios", "Guardar usuario", ex);
            WebDialogWindow.Show(this, "No se pudo guardar el usuario. " + ex.Message, "Control Taxi", "!");
        }
    }

    private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not LocalUserRow row)
            return;

        UserName.Text = row.UserName;
        UserPassword.Clear();
        SelectCombo(UserRole, row.Role);
        SelectCombo(UserStatus, row.Status);
        SelectComboByValue(UserBranchCode, NormalizeBranchCode(row.BranchCode));
        UpdateBranchOptions();
        var permissions = row.Permissions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var check in PermissionsList.Items.OfType<CheckBox>())
            check.IsChecked = permissions.Contains(check.Content?.ToString() ?? string.Empty);
    }

    private void NewUser_Click(object sender, RoutedEventArgs e)
    {
        UsersGrid.SelectedItem = null;
        UserName.Clear();
        UserPassword.Clear();
        UserRole.SelectedIndex = 0;
        UserStatus.SelectedIndex = 0;
        SelectComboByValue(UserBranchCode, "P28");
        UpdateBranchOptions();
        foreach (var check in PermissionsList.Items.OfType<CheckBox>())
            check.IsChecked = !string.Equals(Convert.ToString(check.Content), "AutorizarPagoComision", StringComparison.OrdinalIgnoreCase);
        UserName.Focus();
    }

    private void UserRole_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing)
            return;

        UpdateBranchOptions();
    }

    private bool CanAssignAllBranch(string role) =>
        string.Equals(role, "Administrador", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase);

    private void UpdateBranchOptions()
    {
        if (_isInitializing)
            return;

        if (UserRole is null || UserBranchCode is null)
            return;

        if (UserBranchCode.ItemsSource is not IEnumerable<BranchConfiguration> branches)
            return;

        var selectedRole = SelectedText(UserRole);
        var allowAll = CanAssignAllBranch(selectedRole);
        var currentBranch = NormalizeBranchCode(UserBranchCode.SelectedValue?.ToString());

        UserBranchCode.ItemsSource = branches.Select(b => b.Code == "ALL"
            ? b with { Name = b.Name }
            : b).ToArray();

        if (!allowAll && string.Equals(currentBranch, "ALL", StringComparison.OrdinalIgnoreCase))
            SelectComboByValue(UserBranchCode, "P28");

        if (!allowAll && string.IsNullOrWhiteSpace(SelectedValue(UserBranchCode)))
            SelectComboByValue(UserBranchCode, "P28");

        if (!allowAll)
        {
            foreach (var item in UserBranchCode.Items)
            {
                if (item is BranchConfiguration branch && string.Equals(branch.Code, "ALL", StringComparison.OrdinalIgnoreCase))
                {
                    var comboBoxItem = UserBranchCode.ItemContainerGenerator.ContainerFromItem(item) as ComboBoxItem;
                    if (comboBoxItem is not null)
                        comboBoxItem.IsEnabled = false;
                }
            }
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private static string SelectedText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is not ComboBoxItem item)
            return string.Empty;

        return item.Content?.ToString() ?? string.Empty;
    }

    private static string SelectedValue(ComboBox comboBox) => comboBox.SelectedValue?.ToString() ?? string.Empty;

    private static void SelectCombo(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is not ComboBoxItem comboBoxItem)
                continue;

            if (!string.Equals(comboBoxItem.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                continue;

            comboBox.SelectedItem = comboBoxItem;
            return;
        }
    }

    private void InitializeBranchOptions()
    {
        var branches = _branches.GetAllBranches()?.OrderBy(b => b.Code).ToArray() ?? Array.Empty<BranchConfiguration>();
        UserBranchCode.ItemsSource = branches;
        if (branches.Length > 0)
            SelectComboByValue(UserBranchCode, "P28");
    }

    private static void SelectComboByValue(ComboBox comboBox, string value)
    {
        var normalized = NormalizeBranchCode(value);
        foreach (var item in comboBox.Items)
        {
            if (item is BranchConfiguration branch && string.Equals(branch.Code, normalized, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = branch;
                return;
            }
        }
        if (comboBox.Items.Count > 0)
            comboBox.SelectedIndex = 0;
    }

    private static string NormalizeBranchCode(string? branchCode)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            return "P28";

        return branchCode.Trim().ToUpperInvariant() switch
        {
            "P28" => "P28",
            "CV" => "CV",
            "ALL" => "ALL",
            _ => "P28",
        };
    }
}
