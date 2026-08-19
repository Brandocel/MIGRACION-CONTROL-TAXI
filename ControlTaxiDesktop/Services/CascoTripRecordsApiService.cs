using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlTaxiDesktop.Services;

public sealed class CascoTripRecordsApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _baseUrl;

    public CascoTripRecordsApiService(string? baseUrl = null)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://lightyellow-porpoise-679527.hostingersite.com/casco-api/"
            : baseUrl.Trim().TrimEnd('/') + "/";

        if (!_baseUrl.Contains("/casco-api/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La URL de Casco debe contener /casco-api/.");
    }

    public async Task<bool> MarkTripPayoutPaidAsync(string recordId, string paidBy, CancellationToken cancellationToken = default)
    {
        var normalizedRecordId = (recordId ?? string.Empty).Trim();
        var normalizedUser = (paidBy ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRecordId))
            throw new InvalidOperationException("Se requiere el folio original para marcar el pago remoto.");

        if (string.IsNullOrWhiteSpace(normalizedUser))
            normalizedUser = "ControlTaxi";

        using var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/taxis/registros/{Uri.EscapeDataString(normalizedRecordId)}/pagar")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { user = normalizedUser }, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
            return true;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            $"Hostinger rechazo el pago remoto del folio {normalizedRecordId}. HTTP {(int)response.StatusCode}: {content}");
    }

    public async Task UpdateTripRecordAsync(CascoTripRecordUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedRecordId = (request.RecordId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRecordId))
            throw new InvalidOperationException("Se requiere el folio original para actualizar Hostinger.");

        using var client = CreateClient();
        using var response = await client.PutAsync(
            $"api/taxis/registros/{Uri.EscapeDataString(normalizedRecordId)}",
            BuildJsonContent(request),
            cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var saved = JsonSerializer.Deserialize<CascoTripRecordUpdateResponse>(content, JsonOptions);
            EnsureHostingerConfirmedUpdate(request, saved);
            return;
        }

        throw new InvalidOperationException(
            $"Hostinger rechazo la actualizacion del folio {normalizedRecordId}. HTTP {(int)response.StatusCode}: {content}");
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
            throw new InvalidOperationException($"Hostinger actualizo el folio {request.RecordId}, pero no devolvio confirmacion JSON valida.");

        var mismatches = new List<string>();
        CompareText(mismatches, "folio", request.RecordId, saved.RecordId, required: true);
        CompareBadgeList(mismatches, request.BadgeId, saved.BadgeId);
        CompareNullableInt(mismatches, "catalogId", request.CatalogId, saved.CatalogId);
        CompareText(mismatches, "vendedor", request.SellerName, saved.SellerName);
        CompareText(mismatches, "taxista", request.DriverName, saved.DriverName);
        CompareText(mismatches, "transporte", request.ServiceType, saved.ServiceType);
        CompareText(mismatches, "forma de pago", request.PaymentMethod, saved.PaymentMethod);
        CompareInt(mismatches, "adultos", request.AdultCount, saved.AdultCount);
        CompareInt(mismatches, "jovenes", request.YouthCount, saved.YouthCount);
        CompareInt(mismatches, "menores", request.MinorCount, saved.MinorCount);
        CompareInt(mismatches, "pax", request.PassengerCount, saved.PassengerCount);
        CompareDecimal(mismatches, "dejada", request.TripCost, saved.TripCost);

        if (mismatches.Count > 0)
        {
            throw new InvalidOperationException(
                $"Hostinger respondio OK, pero no confirmo la actualizacion del folio {request.RecordId}: {string.Join(", ", mismatches)}.");
        }
    }

    private static void CompareText(List<string> mismatches, string field, string? expected, string? actual, bool required = false)
    {
        var expectedText = (expected ?? string.Empty).Trim();
        var actualText = (actual ?? string.Empty).Trim();
        if (!required && string.IsNullOrWhiteSpace(expectedText))
            return;

        if (!string.Equals(expectedText, actualText, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"{field} esperado '{expectedText}' recibido '{actualText}'");
    }

    private static void CompareBadgeList(List<string> mismatches, string? expected, string? actual)
    {
        var expectedText = NormalizeBadgeList(expected);
        if (string.IsNullOrWhiteSpace(expectedText))
            return;

        var actualText = NormalizeBadgeList(actual);
        if (!string.Equals(expectedText, actualText, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"gafetes esperados '{expectedText}' recibidos '{actualText}'");
    }

    private static string NormalizeBadgeList(string? value) =>
        string.Join(
            ",",
            (value ?? string.Empty)
                .Split(new[] { ',', ';', '|', '/', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .OrderBy(part => part, StringComparer.OrdinalIgnoreCase));

    private static void CompareInt(List<string> mismatches, string field, int expected, int? actual)
    {
        if (actual is null || actual.Value != expected)
            mismatches.Add($"{field} esperado {expected} recibido {(actual?.ToString() ?? "sin dato")}");
    }

    private static void CompareNullableInt(List<string> mismatches, string field, int? expected, int? actual)
    {
        if (expected is null)
            return;

        if (actual is null || actual.Value != expected.Value)
            mismatches.Add($"{field} esperado {expected.Value} recibido {(actual?.ToString() ?? "sin dato")}");
    }

    private static void CompareDecimal(List<string> mismatches, string field, decimal expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(actual))
            return;

        if (!decimal.TryParse(actual, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var actualDecimal) &&
            !decimal.TryParse(actual, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out actualDecimal))
        {
            mismatches.Add($"{field} esperado {expected:0.##} recibido '{actual}'");
            return;
        }

        if (Math.Round(actualDecimal, 2) != Math.Round(expected, 2))
            mismatches.Add($"{field} esperado {expected:0.##} recibido {actualDecimal:0.##}");
    }
}

public sealed record CascoTripRecordUpdateRequest(
    string RecordId,
    string? BadgeId,
    string? DriverName,
    string? Nationality,
    string? UnitNumber,
    string? Hotel,
    string? Origin,
    string? Site,
    string? Destination,
    string? SellerName,
    int AdultCount,
    int YouthCount,
    int MinorCount,
    int NoShowCount,
    int PassengerCount,
    string? ServiceType,
    decimal TripCost,
    string? PaymentMethod,
    string? Notes,
    string? AssignedBranch,
    string? AssignedBranchCode,
    string? AssignedBranchName,
    int? CatalogId = null);

public sealed record CascoTripRecordUpdateResponse(
    string? RecordId,
    int? CatalogId,
    string? BadgeId,
    string? DriverName,
    string? SellerName,
    int? AdultCount,
    int? YouthCount,
    int? MinorCount,
    int? PassengerCount,
    string? ServiceType,
    string? TripCost,
    string? PaymentMethod);
