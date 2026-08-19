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

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var cleanText = _content.TrimEnd('\r', '\n', ' ');
        var contentLines = cleanText.Replace("\r", string.Empty).Split('\n');
        var paragraph = new Paragraph(new Run(cleanText))
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };

        var document = new FlowDocument(paragraph)
        {
            PagePadding = new Thickness(10, 10, 10, 4),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 11,
            PageWidth = 320,
            PageHeight = Math.Max(40, contentLines.Length * 14 + 14),
            ColumnWidth = 300
        };

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() == true)
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Ticket dejada");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
