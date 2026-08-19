namespace ControlTaxiDesktop.Models;

public sealed record DesktopSession(string UserName, string Role, IReadOnlySet<string> Permissions, bool IsTestDatabase, string BranchCode);
