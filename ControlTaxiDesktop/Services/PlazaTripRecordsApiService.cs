using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlTaxiDesktop.Services;

public sealed class PlazaTripRecordsApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _baseUrl;

    public PlazaTripRecordsApiService(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Plaza 28 no tiene URL de Hostinger configurada.");

        _baseUrl = baseUrl.Trim().TrimEnd('/') + "/";
        if (_baseUrl.Contains("/casco-api/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Seguridad de sucursal: Plaza 28 no puede apuntar a /casco-api/.");
    }

    public async Task<bool> MarkTripPayoutPaidAsync(string recordId, string paidBy, CancellationToken cancellationToken = default)
    {
        var normalizedRecordId = (recordId ?? string.Empty).Trim();
        var normalizedUser = string.IsNullOrWhiteSpace(paidBy) ? "ControlTaxi" : paidBy.Trim();
        if (string.IsNullOrWhiteSpace(normalizedRecordId))
            throw new InvalidOperationException("Se requiere el folio para marcar el pago en Plaza 28.");

        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/taxis/registros/{Uri.EscapeDataString(normalizedRecordId)}/pagar")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { user = normalizedUser }, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 409)
            return true;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Hostinger rechazo el pago Plaza 28 folio {normalizedRecordId}. HTTP {(int)response.StatusCode}: {content}");
    }

    public async Task UpdateTripRecordAsync(CascoTripRecordUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedRecordId = (request.RecordId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRecordId))
            throw new InvalidOperationException("Se requiere el folio para actualizar Hostinger.");

        using var client = CreateClient();
        using var response = await client.PutAsync(
            $"api/taxis/registros/{Uri.EscapeDataString(normalizedRecordId)}",
            BuildJsonContent(request),
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            CascoTripRecordUpdateResponse? saved = null;
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    saved = JsonSerializer.Deserialize<CascoTripRecordUpdateResponse>(content, JsonOptions);
                }
                catch (JsonException)
                {
                    saved = null;
                }
            }

            EnsureHostingerConfirmedUpdate(request, saved);
            return;
        }

        throw new InvalidOperationException(
            $"Hostinger rechazo la actualizacion de Plaza 28 folio {normalizedRecordId}. HTTP {(int)response.StatusCode}: {content}");
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static StringContent BuildJsonContent(CascoTripRecordUpdateRequest request)
    {
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    private static void EnsureHostingerConfirmedUpdate(CascoTripRecordUpdateRequest request, CascoTripRecordUpdateResponse? saved)
    {
        if (saved is null)
            return;

        var expectedRecordId = (request.RecordId ?? string.Empty).Trim();
        var returnedRecordId = (saved.RecordId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(returnedRecordId))
            return;

        if (!string.Equals(expectedRecordId, returnedRecordId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Hostinger respondio OK, pero devolvio un folio distinto en Plaza 28. Esperado '{expectedRecordId}', recibido '{returnedRecordId}'.");
        }
    }
}
