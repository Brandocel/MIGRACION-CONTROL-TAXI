using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ControlTaxiDesktop.Services;

public sealed class CascoTaxistasApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new FlexibleBooleanJsonConverter(),
            new FlexibleDecimalJsonConverter(),
            new FlexibleIntJsonConverter(),
            new FlexibleLongJsonConverter()
        }
    };

    private readonly string _baseUrl;
    private readonly string _branchCode;

    public CascoTaxistasApiService(string? baseUrl = null)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://lightyellow-porpoise-679527.hostingersite.com/casco-api/"
            : baseUrl.Trim();

        if (!normalizedBaseUrl.EndsWith("/", StringComparison.Ordinal))
            normalizedBaseUrl += "/";

        _baseUrl = normalizedBaseUrl;
        _branchCode = ResolveBranchCode(_baseUrl);

        if (!Uri.TryCreate(_baseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("La URL de Hostinger del catalogo no es valida.");
    }

    public async Task<IReadOnlyList<CascoTaxistaRecord>> GetTaxistasAsync(
        string? query = null,
        int maxRecords = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var results = new List<CascoTaxistaRecord>();
        var page = 1;
        const int pageSize = 100;
        var normalizedQuery = string.IsNullOrWhiteSpace(query) ? string.Empty : query.Trim();

        while (true)
        {
            var url = BuildApiPath($"api/taxis/taxistas?page={page}&pageSize={pageSize}");
            if (!string.IsNullOrWhiteSpace(normalizedQuery))
                url += "&query=" + Uri.EscapeDataString(normalizedQuery);

            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<CascoTaxistasPageResponse>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("Hostinger devolvio una respuesta vacia para taxistas.");

            if (payload.Items is { Count: > 0 })
            {
                var remaining = maxRecords - results.Count;
                if (remaining <= 0)
                    break;

                results.AddRange(payload.Items.Take(remaining));
            }

            if (results.Count >= payload.Total || results.Count >= maxRecords || payload.Items.Count == 0)
                break;

            page++;
        }

        return results;
    }

    public async Task<CascoTaxistaRecord> CreateTaxistaAsync(CascoTaxistaUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(BuildApiPath("api/taxis/taxistas"), BuildJsonContent(request), cancellationToken);
        await EnsureSuccessWithBodyAsync(response, cancellationToken);
        return await DeserializeAsync<CascoTaxistaRecord>(response, cancellationToken);
    }

    public async Task<CascoTaxistaRecord> UpdateTaxistaAsync(long catalogId, CascoTaxistaUpsertRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.PutAsync(BuildApiPath($"api/taxis/taxistas/{catalogId}"), BuildJsonContent(request), cancellationToken);
        await EnsureSuccessWithBodyAsync(response, cancellationToken);
        return await DeserializeAsync<CascoTaxistaRecord>(response, cancellationToken);
    }

    public async Task DeleteTaxistaAsync(long catalogId, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.DeleteAsync(BuildApiPath($"api/taxis/taxistas/{catalogId}"), cancellationToken);
        await EnsureSuccessWithBodyAsync(response, cancellationToken);
    }

    public async Task<CascoTransportOptions> GetTransportOptionsAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(BuildApiPath("api/taxis/options"), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await DeserializeAsync<CascoTransportOptions>(response, cancellationToken);
    }

    public async Task<CascoTransportUpdateResponse> UpdateTransportOptionAsync(CascoTransportUpdateRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.PutAsync(BuildApiPath("api/taxis/options"), BuildJsonContent(request), cancellationToken);
        await EnsureSuccessWithBodyAsync(response, cancellationToken);
        return await DeserializeAsync<CascoTransportUpdateResponse>(response, cancellationToken);
    }

    private string BuildApiPath(string relativePath)
    {
        var separator = relativePath.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return relativePath + separator + "branchCode=" + Uri.EscapeDataString(_branchCode);
    }

    private static string ResolveBranchCode(string baseUrl)
    {
        return baseUrl.Contains("/casco-api/", StringComparison.OrdinalIgnoreCase)
            ? "CV"
            : "28";
    }

    private HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static StringContent BuildJsonContent<T>(T request)
    {
        var payload = JsonSerializer.Serialize(request, JsonOptions);
        return new StringContent(payload, Encoding.UTF8, "application/json");
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Hostinger devolvio JSON vacio.");
    }

    private static async Task EnsureSuccessWithBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
            body = response.ReasonPhrase ?? "Sin detalle";

        throw new InvalidOperationException($"Hostinger respondio {(int)response.StatusCode}: {body}");
    }
}

public sealed record CascoTaxistaRecord(
    long CatalogId,
    string BadgeId,
    string DriverName,
    string PhoneNumber,
    string Plate,
    string VehicleModel,
    string UnitNumber,
    string ServiceType,
    string Site,
    string Hotel,
    string Notes,
    bool Active);

public sealed record CascoTaxistaUpsertRequest(
    [property: JsonPropertyName("badgeId")]
    string BadgeId,
    [property: JsonPropertyName("driverName")]
    string DriverName,
    [property: JsonPropertyName("phoneNumber")]
    string PhoneNumber,
    [property: JsonPropertyName("plate")]
    string Plate,
    [property: JsonPropertyName("vehicleModel")]
    string VehicleModel,
    [property: JsonPropertyName("unitNumber")]
    string UnitNumber,
    [property: JsonPropertyName("serviceType")]
    string ServiceType,
    [property: JsonPropertyName("site")]
    string Site,
    [property: JsonPropertyName("hotel")]
    string Hotel,
    [property: JsonPropertyName("notes")]
    string Notes,
    [property: JsonPropertyName("active")]
    [property: JsonConverter(typeof(BooleanAsNumberJsonConverter))]
    bool Active);

public sealed class CascoTaxistasPageResponse
{
    public List<CascoTaxistaRecord> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
}

public sealed class CascoTransportOptions
{
    public IReadOnlyList<string> Hotels { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ServiceTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Sites { get; init; } = Array.Empty<string>();
}

public sealed record CascoTransportUpdateRequest(
    [property: JsonPropertyName("origin")]
    string Origin,
    [property: JsonPropertyName("currentValue")]
    string CurrentValue,
    [property: JsonPropertyName("newValue")]
    string NewValue);

public sealed record CascoTransportUpdateResponse(
    string Origin,
    string PreviousValue,
    string NewValue,
    int Updated);

public sealed class FlexibleBooleanJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.TryGetInt32(out var number) && number != 0,
            JsonTokenType.String => ParseString(reader.GetString()),
            _ => false
        };
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);

    private static bool ParseString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        return normalized.Equals("1", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("true", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("activo", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("active", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("si", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("sí", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class BooleanAsNumberJsonConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number => reader.TryGetInt32(out var number) && number != 0,
            JsonTokenType.String => !string.IsNullOrWhiteSpace(reader.GetString()) && reader.GetString() != "0",
            _ => false
        };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value ? 1 : 0);
}

public sealed class FlexibleDecimalJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDecimal(out var number))
            return number;

        if (reader.TokenType == JsonTokenType.String
            && decimal.TryParse(reader.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        return 0m;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

public sealed class FlexibleIntJsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            return number;

        if (reader.TokenType == JsonTokenType.String
            && int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

public sealed class FlexibleLongJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
            return number;

        if (reader.TokenType == JsonTokenType.String
            && long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var textNumber))
        {
            return textNumber;
        }

        return 0L;
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}
