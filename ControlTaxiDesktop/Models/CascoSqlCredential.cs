namespace ControlTaxiDesktop.Models;

public sealed record CascoSqlCredential(
    string SqlServer,
    string SqlUser,
    string SqlPassword,
    string Database,
    string BranchCode);
