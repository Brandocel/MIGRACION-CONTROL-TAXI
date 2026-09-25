using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ControlTaxiDesktop.Services;

/// <summary>Un talon impreso: el gafete de un vendedor para un viaje.</summary>
public sealed record BadgeTicket(
    string Numero,
    string Vendedor,
    string Taxista,
    string Unidad,
    string FolioApp,
    string FolioOperacion,
    string Fecha,
    string Hotel,
    int Pax);

/// <summary>
/// Talones de gafete con codigo de barras para Casco Viejo.
///
/// POR QUE EXISTE
/// En Plaza 28 el vendedor trae un gafete fisico con codigo de barras y ese numero es el que
/// amarra la venta con el conductor. En Casco Viejo no hay gafetes fisicos, asi que el mismo
/// numero se imprime en un talon de 80 mm y se lee con el mismo escaner.
///
/// El numero NO se inventa aqui: es el gafete que ya trae asignado el viaje. Imprimir no cambia
/// nada en la base, solo saca en papel lo que ya esta capturado.
/// </summary>
public static class BadgeTicketBuilder
{
    /// <summary>Ancho util de un ticket de 80 mm, en unidades de WPF (1/96 de pulgada).</summary>
    private const double TicketWidth = 300;

    private const double ContentWidth = 272;

    /// <summary>
    /// Saca los pares vendedor/gafete del texto que ya arma el sistema
    /// ("JUAN PEREZ - Gafete 207 | ANA - Gafete 258"). Si un renglon viene sin numero se ignora:
    /// un talon sin gafete no sirve para escanear.
    /// </summary>
    public static IReadOnlyList<(string Vendedor, string Gafete)> ParseSellerBadges(string? sellerBadges)
    {
        if (string.IsNullOrWhiteSpace(sellerBadges)) return [];

        var result = new List<(string, string)>();
        foreach (var part in sellerBadges.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var texto = part.Trim();
            if (texto.Length == 0) continue;

            var vendedor = string.Empty;
            var gafete = string.Empty;
            var marca = texto.IndexOf("Gafete", StringComparison.OrdinalIgnoreCase);
            if (marca >= 0)
            {
                gafete = texto[(marca + "Gafete".Length)..].Trim();
                vendedor = texto[..marca].TrimEnd('-', ' ', '\t');
            }
            else
            {
                vendedor = texto;
            }

            // Un mismo vendedor puede traer varios gafetes en el mismo renglon ("Gafete 207, 258"):
            // cada numero lleva su propio talon, porque cada uno se escanea por separado.
            foreach (var numero in BadgeSelectionWorkflow.SplitScanValues(gafete))
            {
                var limpio = Code128Barcode.Sanitize(numero);
                if (limpio.Length == 0) continue;
                result.Add((vendedor.Trim(), limpio));
            }
        }

        return result;
    }

    public static FlowDocument Build(IReadOnlyList<BadgeTicket> tickets, string sucursal)
    {
        if (tickets is null || tickets.Count == 0)
            throw new InvalidOperationException("No hay gafetes que imprimir en este viaje.");

        var document = new FlowDocument
        {
            PagePadding = new Thickness(8, 10, 8, 10),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PageWidth = TicketWidth,
            ColumnWidth = ContentWidth,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        for (var i = 0; i < tickets.Count; i++)
        {
            foreach (var block in BuildTicketBlocks(tickets[i], sucursal, i + 1, tickets.Count))
                document.Blocks.Add(block);

            // Corte entre talones: cada vendedor se lleva el suyo.
            if (i < tickets.Count - 1)
                document.Blocks.Add(Linea("- - - - - - - - - - - - - - - - - - - -", 11, FontWeights.Normal, 10, 10));
        }

        return document;
    }

    private static IEnumerable<Block> BuildTicketBlocks(BadgeTicket ticket, string sucursal, int numero, int total)
    {
        yield return Linea(sucursal.ToUpperInvariant(), 13, FontWeights.Bold, 0, 0);
        yield return Linea("GAFETE DE VENDEDOR", 11, FontWeights.Normal, 0, 6);
        yield return Linea(ticket.Numero, 34, FontWeights.Bold, 0, 4);

        var barcode = new BlockUIContainer(new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = Code128Barcode.CreateVisual(ticket.Numero)
        })
        {
            Margin = new Thickness(0)
        };
        yield return barcode;

        yield return Linea(ticket.Numero, 11, FontWeights.Normal, 0, 8);

        yield return Dato("Vendedor", ticket.Vendedor);
        yield return Dato("Taxista", ticket.Taxista);
        yield return Dato("Unidad", ticket.Unidad);
        yield return Dato("Hotel", ticket.Hotel);
        yield return Dato("Folio", string.IsNullOrWhiteSpace(ticket.FolioApp) ? ticket.FolioOperacion : ticket.FolioApp);
        yield return Dato("Fecha", ticket.Fecha);
        yield return Dato("Pax", ticket.Pax > 0 ? ticket.Pax.ToString(CultureInfo.CurrentCulture) : string.Empty);

        if (total > 1)
            yield return Linea($"Talon {numero} de {total}", 10, FontWeights.Normal, 6, 0);
    }

    private static Paragraph Linea(string texto, double tamano, FontWeight peso, double arriba, double abajo) =>
        new(new Run(texto))
        {
            FontSize = tamano,
            FontWeight = peso,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, arriba, 0, abajo)
        };

    private static Paragraph Dato(string etiqueta, string? valor)
    {
        var parrafo = new Paragraph
        {
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 2),
            TextAlignment = TextAlignment.Left
        };
        parrafo.Inlines.Add(new Run(etiqueta + ": ") { FontWeight = FontWeights.Bold });
        parrafo.Inlines.Add(new Run(string.IsNullOrWhiteSpace(valor) ? "-" : valor.Trim()));
        return parrafo;
    }
}
