using ControlTaxiDesktop.Tools.CascoSync.Services;

namespace ControlTaxiDesktop.Tools.CascoSync.Models;

public sealed class CascoDryRunResult
{
    public required string Server { get; init; }
    public required string Database { get; init; }
    public int LocalRowsFound { get; init; }
    public int LocalRowsMissing { get; init; }
    public int InsertProposedCount { get; init; }
    public int UpdateProposedCount { get; init; }
    public int SinCambiosCount { get; init; }
    public int RealDiscrepanciesCount { get; init; }
    public int ErrorCount { get; init; }
    public int RecordsWithDiscrepancies { get; init; }
    public int TotalFieldsWithDiscrepancies { get; init; }
    public IReadOnlyList<CascoDryRunRow> Rows { get; init; } = Array.Empty<CascoDryRunRow>();
    public required CascoSchemaSnapshot Schema { get; init; }
}

public sealed record CascoDryRunFieldDiscrepancy(
    string FieldName,
    string RemoteValue,
    string LocalValue,
    string RemoteType,
    string SqlType,
    string Reason,
    bool Ignored);

public sealed record CascoDryRunRow(
    string RecordId,
    string DriverName,
    string Site,
    string Date,
    string Action,
    IReadOnlyList<string> Discrepancies,
    IReadOnlyList<CascoDryRunFieldDiscrepancy> FieldDiscrepancies);
