using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Codigo de barras Code 128 (juego B), dibujado con rectangulos de WPF.
///
/// POR QUE PROPIO Y NO UNA LIBRERIA
/// Las maquinas de Casco Viejo no tienen gafetes fisicos y el sistema tiene que imprimirlos. El
/// escritorio se publica self-contained y sin acceso a internet en la sucursal, asi que el codigo
/// se genera aqui en vez de agregar un paquete.
///
/// POR QUE JUEGO B Y NO C
/// El juego C comprime pares de digitos y haria el codigo mas corto, pero los numeros de gafete
/// son de 3 o 4 caracteres: con el juego B el simbolo completo mide menos de 200 puntos y cabe de
/// sobra en un ticket de 80 mm. Un solo juego es mucho mas facil de verificar.
///
/// Se dibuja con <see cref="Rectangle"/> y no con una imagen: al imprimir sale con la resolucion
/// de la impresora (una imagen de pantalla se veria borrosa y el lector fallaria).
/// </summary>
public static class Code128Barcode
{
    /// <summary>
    /// Los 107 simbolos del Code 128. Cada renglon son los seis anchos (barra, espacio, barra,
    /// espacio, barra, espacio) en modulos. El ultimo, el de paro, lleva siete.
    /// </summary>
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312",
        "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
        "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131",
        "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
        "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321",
        "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
        "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114",
        "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112",
        "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412",
        "211214", "211232", "2331112"
    ];

    /// <summary>Codigo de arranque del juego B.</summary>
    private const int StartCodeB = 104;

    /// <summary>Codigo de paro.</summary>
    private const int StopCode = 106;

    /// <summary>
    /// Deja el texto como lo acepta el juego B: solo ASCII imprimible (32 a 126). Lo demas se
    /// quita, porque un caracter fuera de rango produciria un codigo que el lector no entiende.
    /// </summary>
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (character >= ' ' && character <= '~')
                builder.Append(character);
        }
        return builder.ToString();
    }

    /// <summary>Anchos de barra y espacio, en modulos, listos para dibujar.</summary>
    public static IReadOnlyList<int> BuildModuleWidths(string text)
    {
        var clean = Sanitize(text);
        if (clean.Length == 0) throw new ArgumentException("El codigo de barras no puede ir vacio.", nameof(text));

        var codes = new List<int> { StartCodeB };
        foreach (var character in clean)
            codes.Add(character - 32);

        // Digito verificador: arranque + cada valor por su posicion, modulo 103.
        var checksum = StartCodeB;
        for (var i = 1; i < codes.Count; i++)
            checksum += codes[i] * i;
        codes.Add(checksum % 103);
        codes.Add(StopCode);

        var widths = new List<int>();
        foreach (var code in codes)
        {
            foreach (var digit in Patterns[code])
                widths.Add(digit - '0');
        }
        return widths;
    }

    /// <summary>
    /// Dibuja el codigo. El primer ancho es barra, el siguiente espacio, y asi. Lleva la zona
    /// muda de 10 modulos a cada lado que pide la norma: sin ella muchos lectores no enganchan.
    /// </summary>
    public static FrameworkElement CreateVisual(string text, double moduleWidth = 1.6, double height = 58)
    {
        const int QuietZoneModules = 10;
        var widths = BuildModuleWidths(text);
        var totalModules = widths.Sum() + QuietZoneModules * 2;

        var canvas = new Canvas
        {
            Width = totalModules * moduleWidth,
            Height = height,
            Background = Brushes.White,
            SnapsToDevicePixels = true
        };

        var x = QuietZoneModules * moduleWidth;
        var isBar = true;
        foreach (var modules in widths)
        {
            var width = modules * moduleWidth;
            if (isBar)
            {
                var bar = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = Brushes.Black,
                    SnapsToDevicePixels = true
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, 0);
                canvas.Children.Add(bar);
            }
            x += width;
            isBar = !isBar;
        }

        return canvas;
    }
}
