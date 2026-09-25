using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ControlTaxiDesktop;

/// <summary>
/// Vista previa de los talones de gafete antes de mandarlos a la impresora.
///
/// El documento se arma DOS veces (una para ver y otra para imprimir) a proposito: un
/// FlowDocument no puede estar en dos lugares al mismo tiempo, y si se le quita al visor para
/// imprimirlo la ventana se queda en blanco.
/// </summary>
public partial class BadgeTicketPreviewWindow : Window
{
    private readonly Func<FlowDocument> _builder;

    public BadgeTicketPreviewWindow(Func<FlowDocument> builder, string? encabezado = null)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(encabezado))
            HeaderText.Text = encabezado;
        TicketViewer.Document = _builder();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var document = _builder();
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, "Gafetes del viaje");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
