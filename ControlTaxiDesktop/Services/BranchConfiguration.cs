namespace ControlTaxiDesktop.Services;

public sealed record BranchConfiguration(
    string Code,
    string Name,
    string SqlServer,
    string Database,
    string SiteName,
    string ApiBaseUrl,
    bool IsReadOnly)
{
    public string CompuadmoDatabase { get; init; } = string.Empty;
    public string JoyeriaDatabase { get; init; } = string.Empty;
    public string SqlUser { get; init; } = "sa";
}
