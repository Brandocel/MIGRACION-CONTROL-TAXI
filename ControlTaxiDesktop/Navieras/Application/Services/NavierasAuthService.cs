using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class NavierasAuthService(
    INavierasApiClient apiClient,
    INavierasSessionRepository sessionRepository,
    NavierasApiOptions options) : INavierasAuthService
{
    public string CurrentBaseUrl => options.BaseUrl;

    public async Task<NavierasUser> LoginAsync(string username, string password, string? baseUrl = null, CancellationToken cancellationToken = default)
    {
        options.BaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? NavierasApiOptions.DefaultBaseUrl
            : baseUrl.Trim();

        var loginPath = string.IsNullOrWhiteSpace(options.DesktopLoginPath)
            ? NavierasApiOptions.DefaultDesktopLoginPath
            : options.DesktopLoginPath.Trim().TrimStart('/');

        using var response = await apiClient.PostAsync(
            loginPath,
            new { username = username.Trim(), password },
            authenticated: false,
            cancellationToken: cancellationToken);

        var data = response.RootElement.GetProperty("data");
        var token = NavierasJsonMapper.GetString(data, "accessToken", "token");
        if (string.IsNullOrWhiteSpace(token))
            throw new NavierasApiException("La API de Navieras Desktop no devolvio un token de acceso.");

        var loginUser = NavierasJsonMapper.MapUser(data);
        await sessionRepository.SaveAsync(token, loginUser, options.BaseUrl, cancellationToken);

        // Flujo requerido: despues del login, validar y actualizar la sesion con auth/me.
        using var meResponse = await apiClient.GetAsync("auth/me", cancellationToken);
        var meData = meResponse.RootElement.GetProperty("data");
        var meUser = NavierasJsonMapper.MapUser(meData);
        await sessionRepository.SaveAsync(token, meUser, options.BaseUrl, cancellationToken);

        return meUser;
    }

    public async Task<NavierasUser?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var session = await sessionRepository.GetAsync(cancellationToken);
        if (session is null)
            return null;

        options.BaseUrl = session.Value.BaseUrl;
        try
        {
            using var response = await apiClient.GetAsync("auth/me", cancellationToken);
            var user = NavierasJsonMapper.MapUser(response.RootElement.GetProperty("data"));
            await sessionRepository.SaveAsync(session.Value.AccessToken, user, session.Value.BaseUrl, cancellationToken);
            return user;
        }
        catch (NavierasApiException ex) when (ex.StatusCode == 401)
        {
            await sessionRepository.ClearAsync(cancellationToken);
            return null;
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var _ = await apiClient.PostAsync("auth/logout", cancellationToken: cancellationToken);
        }
        catch (NavierasApiException)
        {
        }
        await sessionRepository.ClearAsync(cancellationToken);
    }

    public async Task ChangePasswordAsync(string userId, string currentPassword, string newPassword, string confirmPassword, CancellationToken cancellationToken = default)
    {
        using var _ = await apiClient.PostAsync(
            "auth/change-password",
            new
            {
                userId = int.TryParse(userId, out var value) ? value : 0,
                currentPassword,
                newPassword,
                confirmPassword
            },
            cancellationToken: cancellationToken);
    }
}
