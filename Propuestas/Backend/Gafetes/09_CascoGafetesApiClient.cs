using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

/// <summary>
/// Cliente propuesto para enviar gafetes a la nueva ruta: POST /api/gafetes/sync
/// 
/// ESTADO: PREPARADO SIN ACTIVAR
/// Este cliente está listo para usar pero NO debe activarse hasta que el backend
/// esté desplegado en Hostinger.
/// 
/// USO FUTURO:
/// await cascoGafetesClient.SyncGafeteAsync("2121", cancellationToken);
/// </summary>
public interface ICascoGafetesApiClient
{
    /// <summary>
    /// Sincroniza un gafete individual.
    /// </summary>
    Task<CascoGafeteSyncResponse?> SyncGafeteAsync(string badgeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sincroniza múltiples gafetes.
    /// </summary>
    Task<CascoGafeteSyncResponse?> SyncMultipleGafetesAsync(
        CascoBadgeSyncPayload payload,
        CancellationToken cancellationToken = default);
}

public sealed class CascoGafetesApiClient : ICascoGafetesApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiBaseUrl;
    private readonly string _branchCode;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public CascoGafetesApiClient(
        HttpClient httpClient,
        string apiBaseUrl,
        string branchCode = "CV")
    {
        _httpClient = httpClient;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _branchCode = branchCode;
    }

    public async Task<CascoGafeteSyncResponse?> SyncGafeteAsync(string badgeId, CancellationToken cancellationToken = default)
    {
        // Este es un placeholder. En la ruta real, se llamaría con los datos del gafete desde la base de datos.
        throw new NotImplementedException("Use SyncMultipleGafetesAsync con el payload completo");
    }

    public async Task<CascoGafeteSyncResponse?> SyncMultipleGafetesAsync(
        CascoBadgeSyncPayload payload,
        CancellationToken cancellationToken = default)
    {
        // Construir request
        var request = BuildSyncGafetesRequest(payload);

        // URL de sincronización
        var url = $"{_apiBaseUrl}/api/gafetes/sync";

        // Serializar payload
        var jsonContent = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };

            // Agregar headers de autenticación (adaptar según tu implementación)
            // httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<CascoGafeteSyncResponse>(
                    responseContent,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return result;
            }
            else
            {
                // Intentar deserializar como error response
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<CascoGafeteSyncResponse>(
                        responseContent,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    return errorResponse;
                }
                catch
                {
                    return new CascoGafeteSyncResponse
                    {
                        Success = false,
                        Error = $"Error HTTP {response.StatusCode}: {responseContent}",
                        ProcessedAt = DateTime.UtcNow
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return new CascoGafeteSyncResponse
            {
                Success = false,
                Error = $"Error de conexión: {ex.Message}",
                ProcessedAt = DateTime.UtcNow
            };
        }
    }

    private object BuildSyncGafetesRequest(CascoBadgeSyncPayload payload)
    {
        // Usar el formato propuesto (NO mkt2_gafetes)
        return new
        {
            branchCode = _branchCode,
            gafetes = payload.Gafetes
        };
    }
}

/// <summary>
/// Payload de sincronización de gafetes (del cliente local).
/// </summary>
public class CascoBadgeSyncPayload
{
    public required List<object> Gafetes { get; set; }
}

/// <summary>
/// Respuesta de sincronización de gafetes.
/// </summary>
public class CascoGafeteSyncResponse
{
    public bool Success { get; set; }
    public string? BranchCode { get; set; }
    public int Received { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? Error { get; set; }
    public int? ErrorIndex { get; set; }
    public DateTime ProcessedAt { get; set; }
}

/// <summary>
/// Extensión propuesta para CascoBadgeSyncService para usar el nuevo cliente.
/// 
/// INSTRUCCIONES DE ACTIVACIÓN:
/// 1. Descomentar el código siguiente
/// 2. Registrar ICascoGafetesApiClient en DI
/// 3. Reemplazar la lógica de envío en CascoBadgeSyncService.RunWatchCycleAsync()
/// 4. Actualizar tests
/// 5. Desplegar en Hostinger
/// 6. Activar POST real
/// </summary>

/*

// En CascoBadgeSyncService.cs (método existente RunWatchCycleAsync):

private async Task<bool> TrySendGafeteAsync(
    CascoTripRecord row,
    ICascoGafetesApiClient gafetesClient,
    CancellationToken cancellationToken)
{
    try
    {
        var payload = new
        {
            mkt2_gafetes = new[]
            {
                new
                {
                    badgeId = row.BadgeId,
                    barcode = row.Barcode,
                    status = row.Status,
                    cycle = row.Cycle,
                    taxistaId = row.TaxistaId,
                    taxistaName = row.TaxistaName,
                    createdAt = row.CreatedAt.ToString("s", CultureInfo.InvariantCulture)
                }
            }
        };

        var gafetes = new List<object> { payload.mkt2_gafetes[0] };
        var syncPayload = new CascoBadgeSyncPayload { Gafetes = gafetes };

        var response = await gafetesClient.SyncMultipleGafetesAsync(syncPayload, cancellationToken);

        if (response?.Success ?? false)
        {
            return true;
        }
        else
        {
            WriteLog("ERROR", "envio-api", $"Error en respuesta: {response?.Error}");
            return false;
        }
    }
    catch (Exception ex)
    {
        WriteLog("ERROR", "envio-api", $"Excepción: {ex.Message}");
        return false;
    }
}

*/
