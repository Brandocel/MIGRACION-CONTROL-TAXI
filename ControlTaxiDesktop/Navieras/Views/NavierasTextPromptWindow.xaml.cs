using System.Windows;

namespace ControlTaxiDesktop.Navieras.Views;

public partial class NavierasTextPromptWindow : Window
{
    public NavierasTextPromptWindow(string title, string message, string value = "")
    {
        InitializeComponent();
        PromptTitleTextBlock.Text = title;
        PromptMessageTextBlock.Text = message;
        ValueTextBox.Text = value;
    }

    public string ResultText => ValueTextBox.Text.Trim();

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
