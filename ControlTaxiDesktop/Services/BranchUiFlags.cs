using System.Windows;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Banderas de pantalla que dependen de la sucursal y que la tabla de viajes necesita dentro de
/// sus plantillas de renglon.
///
/// Va aqui, y no como propiedad de la ventana, porque los botones de un DataTemplate no ven a la
/// ventana: su contexto de datos es el renglon. Se prende al abrir la ventana, ANTES de cargar el
/// XAML, para que el primer renglon que se dibuje ya la lea bien.
/// </summary>
public static class BranchUiFlags
{
    /// <summary>Casco Viejo imprime los gafetes porque no tiene fisicos.</summary>
    public static bool BadgePrinting { get; private set; }

    public static Visibility BadgePrintingVisibility => BadgePrinting ? Visibility.Visible : Visibility.Collapsed;

    public static void SetBadgePrinting(bool enabled) => BadgePrinting = enabled;
}
