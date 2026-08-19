namespace ControlTaxiDesktop.Navieras.Domain;

public sealed class NavierasApiOptions
{
    public const string DefaultBaseUrl = "https://lightyellow-porpoise-679527.hostingersite.com/navieras-desktop/";
    public const string DefaultDesktopLoginPath = "auth/login";

    public string BaseUrl { get; set; } = DefaultBaseUrl;
    public string DesktopLoginPath { get; set; } = DefaultDesktopLoginPath;
}
