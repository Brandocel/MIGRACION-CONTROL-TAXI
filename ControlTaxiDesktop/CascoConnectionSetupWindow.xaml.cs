using System.Windows;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;

namespace ControlTaxiDesktop;

public partial class CascoConnectionSetupWindow : Window
{
    private readonly BranchConfiguration _branch;

    public CascoConnectionSetupWindow(BranchConfiguration branch, CascoSqlCredential? currentCredential = null)
    {
        _branch = branch;
        InitializeComponent();

        var model = currentCredential ?? CascoCredentialStore.CreateDefault(branch);
        ServerTextBox.Text = model.SqlServer;
        UserTextBox.Text = model.SqlUser;
        DatabaseTextBox.Text = model.Database;
        BranchTextBox.Text = model.BranchCode;
    }

    private CascoSqlCredential BuildCredential() =>
        new(
            ServerTextBox.Text.Trim(),
            UserTextBox.Text.Trim(),
            PasswordTextBox.Password,
            DatabaseTextBox.Text.Trim(),
            BranchTextBox.Text.Trim());

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WebTextBrush");
            StatusTextBlock.Text = "Probando conexion...";
            await CascoCredentialStore.TestConnectionAsync(BuildCredential());
            StatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WebGreenBrush");
            StatusTextBlock.Text = "Conexion correcta con SQL Server de Casco.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WebRedBrush");
            StatusTextBlock.Text = "No se pudo conectar: " + ex.Message;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var credential = BuildCredential();
            if (string.IsNullOrWhiteSpace(credential.SqlServer)
                || string.IsNullOrWhiteSpace(credential.SqlUser)
                || string.IsNullOrWhiteSpace(credential.SqlPassword)
                || string.IsNullOrWhiteSpace(credential.Database)
                || !string.Equals(credential.BranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            {
                StatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WebRedBrush");
                StatusTextBlock.Text = "Completa servidor, usuario, contrasena y confirma la sucursal CV.";
                return;
            }

            StatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WebTextBrush");
            StatusTextBlock.Text = "Validando y guardando...";
            await CascoCredentialStore.TestConnectionAsync(credential);
            CascoCredentialStore.Save(credential);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("WebRedBrush");
            StatusTextBlock.Text = "No se pudo guardar la configuracion: " + ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
