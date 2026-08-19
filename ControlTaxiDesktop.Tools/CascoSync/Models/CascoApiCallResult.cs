namespace ControlTaxiDesktop.Tools.CascoSync.Models;

public sealed class CascoApiCallResult
{
    public required string Url { get; init; }
    public int HttpStatusCode { get; init; }
    public string? ContentType { get; init; }
    public long ResponseTimeMs { get; init; }
    public IReadOnlyList<CascoTripRecord> Records { get; init; } = Array.Empty<CascoTripRecord>();
}
