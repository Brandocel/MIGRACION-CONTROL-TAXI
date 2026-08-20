using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Impresion de tickets en texto plano. Vivia dentro de TicketPreviewWindow; se extrajo aqui
/// para que el panel lateral de Comisiones pueda imprimir el mismo ticket sin tener que abrir
/// esa ventana, y para no terminar con dos copias de la logica de paginado.
/// </summary>
public static class TicketPrinting
{
    public static void Print(string content, string jobName)
    {
        var cleanText = content.TrimEnd('\r', '\n', ' ');
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
            dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
    }
}
