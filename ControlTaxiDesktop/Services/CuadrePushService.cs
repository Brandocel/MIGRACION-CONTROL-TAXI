using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Sube el cuadre ya calculado a la API de Hostinger, para que Hoka Solutions pueda mostrarlo
/// y descargarlo sin conectarse al SQL Server de Plaza 28.
///
/// Los dos sistemas viven en servidores distintos: el cuadre se calcula contra SQL Server dentro
/// de la tienda, y Hoka corre en el servidor REYNA. Abrir un tunel entre los dos dejaria el
/// reporte de Hoka dependiendo de que la red de la tienda este arriba. En cambio la API de
/// Hostinger ya es el puente que usan las dejadas y los camiones, asi que el cuadre viaja por el
/// mismo tubo y no hace falta abrir nada nuevo.
///
/// El envio nunca debe tumbar la exportacion del Excel: si Hostinger no contesta, el usuario ya
/// tiene su archivo y el cuadre se vuelve a empujar en la siguiente exportacion.
/// </summary>
public sealed class CuadrePushService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private readonly string _baseUrl;
    private readonly string _syncToken;

    public CuadrePushService(string baseUrl, string syncToken)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("No hay URL de Hostinger configurada para subir el cuadre.");
        if (string.IsNullOrWhiteSpace(syncToken))
            throw new InvalidOperationException("No hay token de sincronizacion para subir el cuadre.");

        _baseUrl = baseUrl.Trim().TrimEnd('/') + "/";
        _syncToken = syncToken.Trim();
    }

    /// <summary>
    /// Arma el servicio leyendo la misma configuracion que ya usa el sincronizador, para que el
    /// token viva en un solo lugar y no haya dos valores que mantener sincronizados a mano.
    /// </summary>
    public static CuadrePushService ForBranch(string branchCode)
    {
        var esCasco = string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase);
        var configuracion = LeerConfiguracionSincronizador(esCasco
            ? Path.Combine("SyncTaxi", "sync.casco.config.json")
            : Path.Combine("SyncTaxi_Plaza28", "sync.plaza28.config.json"));

        var baseUrl = configuracion.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = new BranchConfigurationService().GetBranch(esCasco ? "CV" : "P28").ApiBaseUrl;

        return new CuadrePushService(baseUrl, configuracion.Token);
    }

    /// <summary>
    /// Arma el cuadre y lo sube, en un solo paso. Existe para que las dos pantallas que exportan
    /// el Excel (Operaciones y POS) publiquen con una sola linea y no se separen con el tiempo.
    /// Nunca lanza: el resultado dice si se pudo o no.
    /// </summary>
    public static async Task<CuadrePushResult> PublicarAsync(
        DesktopOutputService output,
        DateTime start,
        DateTime end,
        IReadOnlyList<LocalRelation> rows,
        IReadOnlyList<LocalCommission> commissions,
        IReadOnlyList<LocalCut> cuts,
        IReadOnlyList<LocalCuadreResumenRow> camiones,
        string branchCode,
        IReadOnlyList<LocalCommissionBrowserRow>? authoritativeCommissions = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = output.BuildCuadrePayload(start, end, rows, commissions, cuts, camiones, branchCode, authoritativeCommissions);
            return await ForBranch(branchCode).PushAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new CuadrePushResult(false, "No se pudo preparar el cuadre para Hoka: " + ex.Message, 0, 0);
        }
    }

    /// <summary>
    /// Manda el cuadre. Devuelve el resultado en vez de lanzar excepcion cuando la API responde
    /// mal, porque quien llama esta exportando un Excel y no queremos que un problema de red le
    /// borre el trabajo.
    /// </summary>
    public async Task<CuadrePushResult> PushAsync(CuadrePayload payload, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "sync/push-cuadre")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Sync-Token", _syncToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var cuerpo = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new CuadrePushResult(
                    false,
                    $"La API respondio HTTP {(int)response.StatusCode}: {Recortar(cuerpo)}",
                    0,
                    0);
            }

            return new CuadrePushResult(
                true,
                "Cuadre publicado en Hoka.",
                payload.Resumen.Count,
                payload.Dejadas.Count);
        }
        catch (Exception ex)
        {
            return new CuadrePushResult(false, "No se pudo subir el cuadre: " + ex.Message, 0, 0);
        }
    }

    /// <summary>
    /// Lee ApiBaseUrl y SyncToken del archivo de configuracion del sincronizador que va junto al
    /// ejecutable. Si el archivo no esta o esta mal formado, regresa vacios y quien llama decide.
    /// </summary>
    private static (string BaseUrl, string Token) LeerConfiguracionSincronizador(string rutaRelativa)
    {
        try
        {
            var ruta = Path.Combine(AppContext.BaseDirectory, rutaRelativa);
            if (!File.Exists(ruta))
                return (string.Empty, string.Empty);

            using var documento = JsonDocument.Parse(File.ReadAllText(ruta));
            var raiz = documento.RootElement;

            var baseUrl = raiz.TryGetProperty("ApiBaseUrl", out var url) ? url.GetString() ?? string.Empty : string.Empty;
            var token = raiz.TryGetProperty("SyncToken", out var valor) ? valor.GetString() ?? string.Empty : string.Empty;

            return (baseUrl, token);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string Recortar(string texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? "(respuesta vacia)"
            : texto.Length <= 300 ? texto.Trim() : texto[..300].Trim() + "...";
}
