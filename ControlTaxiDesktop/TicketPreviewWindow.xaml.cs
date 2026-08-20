using ControlTaxiDesktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ControlTaxiDesktop;

public partial class TicketPreviewWindow : Window
{
    private readonly string _content;
    private readonly DesktopOutputService _output = new();

    public TicketPreviewWindow(string content)
    {
        InitializeComponent();
        _content = content;
        TicketText.Text = content;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Ticket texto (*.txt)|*.txt",
            FileName = $"ticket_dejada_{DateTime.Now:yyyyMMddHHmmss}.txt"
        };
        if (dialog.ShowDialog() == true)
            await _output.ExportTextAsync(_content, dialog.FileName);
    }

    // La logica de impresion se movio a Services/TicketPrinting para compartirla con el panel
    // lateral de Comisiones.
    private void Print_Click(object sender, RoutedEventArgs e) => TicketPrinting.Print(_content, "Ticket dejada");

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
