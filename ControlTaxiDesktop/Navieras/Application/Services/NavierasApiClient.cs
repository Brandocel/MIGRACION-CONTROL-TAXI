using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class NavierasApiClient(
    HttpClient httpClient,
    INavierasSessionRepository sessionRepository,
    NavierasApiOptions options) : INavierasApiClient
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken = default)
        => await SendAsync(HttpMethod.Get, path, null, true, cancellationToken);

    public async Task<JsonDocument> PostAsync(string path, object? body = null, bool authenticated = true, CancellationToken cancellationToken = default)
        => await SendAsync(HttpMethod.Post, path, body, authenticated, cancellationToken);

    public async Task<JsonDocument> PutAsync(string path, object? body = null, CancellationToken cancellationToken = default)
        => await SendAsync(HttpMethod.Put, path, body, true, cancellationToken);

    public async Task<JsonDocument> DeleteAsync(string path, CancellationToken cancellationToken = default)
        => await SendAsync(HttpMethod.Delete, path, null, true, cancellationToken);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body, bool authenticated, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (authenticated)
        {
            var session = await sessionRepository.GetAsync(cancellationToken)
                ?? throw new NavierasApiException("No hay una sesion activa de Navieras.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new NavierasApiException("No fue posible conectar con la API de Navieras: " + ex.Message);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            content = "{\"data\":null}";

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            throw new NavierasApiException("La API de Navieras devolvio una respuesta invalida.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = document.RootElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
            throw new NavierasApiException(message ?? $"La API de Navieras devolvio un error {(int)response.StatusCode}.", (int)response.StatusCode);
        }

        return document;
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? NavierasApiOptions.DefaultBaseUrl : options.BaseUrl.Trim();
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
            baseUrl += "/";
        var normalizedPath = path.TrimStart('/');
        return new Uri(new Uri(baseUrl, UriKind.Absolute), normalizedPath);
    }
}
