using System.Windows;

namespace ControlTaxiDesktop;

public partial class WebDialogWindow : Window
{
    public WebDialogWindow(string title, string message, string icon = "!")
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;
        IconText.Text = icon;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    public static void Show(Window? owner, string message, string title = "Control Taxi", string icon = "!")
    {
        var dialog = new WebDialogWindow(title, message, icon);
        if (owner is not null) dialog.Owner = owner;
        dialog.ShowDialog();
    }

    public static bool Confirm(
        Window? owner,
        string message,
        string title = "Control Taxi",
        string icon = "?",
        string acceptText = "ACEPTAR",
        string cancelText = "CANCELAR")
    {
        var dialog = new WebDialogWindow(title, message, icon);
        dialog.OkButtonElement.Content = acceptText;
        dialog.CancelButtonElement.Content = cancelText;
        dialog.CancelButtonElement.Visibility = Visibility.Visible;
        if (owner is not null) dialog.Owner = owner;
        return dialog.ShowDialog() == true;
    }
}
