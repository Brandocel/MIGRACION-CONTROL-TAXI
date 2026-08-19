using System.Windows;
using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Views;

public partial class NavierasLoginWindow : Window
{
    private readonly INavierasAuthService _authService;

    public NavierasLoginWindow(INavierasAuthService authService)
    {
        _authService = authService;
        InitializeComponent();
        BaseUrlTextBox.Text = _authService.CurrentBaseUrl;
        UserNameTextBox.Text = "Reyna";
    }

    public NavierasUser? AuthenticatedUser { get; private set; }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorTextBlock.Text = string.Empty;
            var baseUrl = BaseUrlTextBox.Text?.Trim();
            AuthenticatedUser = await _authService.LoginAsync(
                string.IsNullOrWhiteSpace(UserNameTextBox.Text) ? "Reyna" : UserNameTextBox.Text,
                PasswordTextBox.Password,
                string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = ex.Message;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
