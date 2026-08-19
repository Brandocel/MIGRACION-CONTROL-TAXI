namespace ControlTaxiDesktop.Navieras.Domain;

public sealed record NavierasUser(
    string UserId,
    string Username,
    string FullName,
    string Role,
    IReadOnlySet<string> Permissions,
    bool IsActive,
    bool IsBlocked);
