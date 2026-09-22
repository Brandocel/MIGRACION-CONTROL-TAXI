using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Lee el resumen de camiones (AUTOCAR, MAYA CARIBE, TURICUN) desde la API de Hostinger.
///
/// La app de camiones (VB6) escribe directo en la base MySQL de Hostinger, asi que ahi los
/// registros estan al momento. La copia en SQL Server (dbo.registroscamiones) llega tarde,
/// y por eso el cuadre mostraba llegadas incompletas: el corte del dia se sacaba antes de que
/// la copia se pusiera al corriente.
///
/// El escritorio NO se conecta directo a esa MySQL: la consulta pasa por la API para que la
/// contrasena viva solo en el servidor y no en cada maquina de la tienda.
/// </summary>
public sealed class PlazaCamionesApiService
{
    // AllowReadingFromString: PHP/PDO regresa columnas numericas de MySQL (SUM/agregados) como
    // texto en el JSON (ej. "pax":"264" en vez de "pax":264) por como PDO tipa los resultados.
    // Sin esto, Deserialize truena en silencio (el catch de arriba lo traga), el resumen de la
    // API se descarta completo, y el cuadre cae al SQL Server viejo que no tiene camiones desde
    // junio 2026 -- por eso el cuadre de camiones salia siempre en 0 aunque el API respondiera
    // bien (confirmado 2026-08-29: curl directo daba datos correctos, pero el escritorio no).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly string _baseUrl;

    public PlazaCamionesApiService(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Plaza 28 no tiene URL de Hostinger configurada.");

        _baseUrl = baseUrl.Trim().TrimEnd('/') + "/";
        if (_baseUrl.Contains("/casco-api/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Seguridad de sucursal: Plaza 28 no puede apuntar a /casco-api/.");
    }

    /// <summary>
    /// Resumen por empresa en el rango dado. Las fechas son inclusivas; si no se dan, la API
    /// responde el dia de hoy.
    /// </summary>
    public async Task<IReadOnlyList<LocalCuadreResumenRow>> GetResumenAsync(
        DateTime? start,
        DateTime? end,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        if (start is not null)
            query.Add("desde=" + start.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (end is not null)
            query.Add("hasta=" + end.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var url = "api/pos/camiones" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));

        using var client = CreateClient();
        using var response = await client.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"La API de camiones respondio {(int)response.StatusCode}: {Recortar(body)}");

        var filas = JsonSerializer.Deserialize<CamionResumenDto[]>(body, JsonOptions) ?? [];
        return filas
            .Where(x => !string.IsNullOrWhiteSpace(x.Empresa))
            // Mismo orden y mismas columnas que la consulta de SQL Server: el Neto del cuadre
            // para camiones es la dejada.
            .Select(x => new LocalCuadreResumenRow(
                x.Empresa.Trim().ToUpperInvariant(),
                x.Pax,
                x.Entraron,
                x.Salieron,
                x.Unidades,
                x.Dejada,
                0m,
                0m,
                0m,
                x.Dejada))
            .ToArray();
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(_baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string Recortar(string texto) =>
        texto.Length <= 200 ? texto : texto[..200] + "...";

    private sealed record CamionResumenDto(
        string Empresa,
        int Pax,
        int Entraron,
        int Salieron,
        int Unidades,
        decimal Dejada);
}
