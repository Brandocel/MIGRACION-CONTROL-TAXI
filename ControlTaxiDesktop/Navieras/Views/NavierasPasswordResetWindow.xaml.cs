using System.Windows;

namespace ControlTaxiDesktop.Navieras.Views;

public partial class NavierasPasswordResetWindow : Window
{
    public NavierasPasswordResetWindow()
    {
        InitializeComponent();
    }

    public string NewPassword => NewPasswordTextBox.Password;
    public string ConfirmPassword => ConfirmPasswordTextBox.Password;

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
