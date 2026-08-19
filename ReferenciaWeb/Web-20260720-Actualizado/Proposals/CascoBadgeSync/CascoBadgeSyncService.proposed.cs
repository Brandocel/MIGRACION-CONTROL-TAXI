using Microsoft.Data.SqlClient;

namespace ControlTaxiWeb.Proposals.CascoBadgeSync;

public interface ICascoBadgeSyncService
{
    Task<CascoBadgeSyncResult> UpsertAsync(
        string branchCode,
        IReadOnlyList<CascoBadgeSyncItem> items,
        CancellationToken cancellationToken = default);
}

public sealed class CascoBadgeSyncService : ICascoBadgeSyncService
{
    private const int MaxBatchSize = 200;
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase) { "A", "R", "S" };
    private readonly string _connectionString;

    public CascoBadgeSyncService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<CascoBadgeSyncResult> UpsertAsync(
        string branchCode,
        IReadOnlyList<CascoBadgeSyncItem> items,
        CancellationToken cancellationToken = default)
    {
        var result = new CascoBadgeSyncResult { Received = items.Count };

        if (!string.Equals(branchCode, "CV", StringComparison.Ordinal))
            throw new CascoBadgeSyncHttpException(400, "branchCode debe ser CV.");

        if (items.Count == 0)
            throw new CascoBadgeSyncHttpException(400, "gafetes no puede ir vacio.");

        if (items.Count > MaxBatchSize)
            throw new CascoBadgeSyncHttpException(400, $"El lote excede el maximo permitido de {MaxBatchSize}.");

        var duplicateKeys = items
            .GroupBy(item => $"{item.BadgeId.Trim().ToUpperInvariant()}|{item.Cycle}")
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateKeys.Count > 0)
            throw new CascoBadgeSyncHttpException(422, $"Hay gafetes duplicados en el mismo payload: {string.Join(", ", duplicateKeys)}.");

        var rows = new List<CascoBadgeSyncRow>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.BadgeId))
                throw new CascoBadgeSyncHttpException(422, "badgeId es obligatorio.");

            if (!ValidStatuses.Contains(item.Status.Trim()))
                throw new CascoBadgeSyncHttpException(422, $"status invalido para {item.BadgeId}: {item.Status}.");

            if (item.Cycle < 1)
                throw new CascoBadgeSyncHttpException(422, $"cycle invalido para {item.BadgeId}: {item.Cycle}.");

            if (!item.TryParseCreatedAt(out var createdAt))
                throw new CascoBadgeSyncHttpException(422, $"createdAt invalido para {item.BadgeId}: {item.CreatedAtRaw}.");

            rows.Add(new CascoBadgeSyncRow
            {
                BranchCode = branchCode,
                BadgeId = item.BadgeId.Trim(),
                Barcode = string.IsNullOrWhiteSpace(item.Barcode) ? item.BadgeId.Trim() : item.Barcode.Trim(),
                Status = item.Status.Trim().ToUpperInvariant(),
                Cycle = item.Cycle,
                TaxistaId = item.TaxistaId,
                TaxistaName = item.TaxistaName?.Trim() ?? string.Empty,
                CreatedAt = createdAt
            });
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var row in rows)
            {
                await using var selectCommand = new SqlCommand(
                    """
                    SELECT status, barcode, taxista_id, taxista_name, created_at
                    FROM dbo.casco_gafetes
                    WHERE branch_code = @branchCode AND badge_id = @badgeId AND cycle = @cycle;
                    """,
                    connection,
                    (SqlTransaction)transaction);

                selectCommand.Parameters.AddWithValue("@branchCode", row.BranchCode);
                selectCommand.Parameters.AddWithValue("@badgeId", row.BadgeId);
                selectCommand.Parameters.AddWithValue("@cycle", row.Cycle);

                await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
                var exists = await reader.ReadAsync(cancellationToken);
                string? currentStatus = null;
                string? currentBarcode = null;
                int? currentTaxistaId = null;
                string? currentTaxistaName = null;
                DateTimeOffset? currentCreatedAt = null;

                if (exists)
                {
                    currentStatus = reader.IsDBNull(0) ? null : reader.GetString(0);
                    currentBarcode = reader.IsDBNull(1) ? null : reader.GetString(1);
                    currentTaxistaId = reader.IsDBNull(2) ? null : reader.GetInt32(2);
                    currentTaxistaName = reader.IsDBNull(3) ? null : reader.GetString(3);
                    currentCreatedAt = reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4));
                }

                await reader.CloseAsync();

                if (!exists)
                {
                    await using var insertCommand = new SqlCommand(
                        """
                        INSERT INTO dbo.casco_gafetes
                        (
                            branch_code, badge_id, barcode, status, cycle,
                            taxista_id, taxista_name, created_at, updated_at
                        )
                        VALUES
                        (
                            @branchCode, @badgeId, @barcode, @status, @cycle,
                            @taxistaId, @taxistaName, @createdAt, SYSUTCDATETIME()
                        );
                        """,
                        connection,
                        (SqlTransaction)transaction);

                    AddCommonParameters(insertCommand, row);
                    await insertCommand.ExecuteNonQueryAsync(cancellationToken);
                    result.Inserted++;
                    continue;
                }

                var changed =
                    !string.Equals(currentStatus, row.Status, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(currentBarcode ?? string.Empty, row.Barcode, StringComparison.OrdinalIgnoreCase) ||
                    currentTaxistaId != row.TaxistaId ||
                    !string.Equals(currentTaxistaName ?? string.Empty, row.TaxistaName, StringComparison.Ordinal) ||
                    currentCreatedAt != row.CreatedAt;

                if (!changed)
                {
                    result.Unchanged++;
                    continue;
                }

                await using var updateCommand = new SqlCommand(
                    """
                    UPDATE dbo.casco_gafetes
                    SET
                        barcode = @barcode,
                        status = @status,
                        taxista_id = @taxistaId,
                        taxista_name = @taxistaName,
                        created_at = @createdAt,
                        updated_at = SYSUTCDATETIME()
                    WHERE branch_code = @branchCode
                      AND badge_id = @badgeId
                      AND cycle = @cycle;
                    """,
                    connection,
                    (SqlTransaction)transaction);

                AddCommonParameters(updateCommand, row);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                result.Updated++;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void AddCommonParameters(SqlCommand command, CascoBadgeSyncRow row)
    {
        command.Parameters.AddWithValue("@branchCode", row.BranchCode);
        command.Parameters.AddWithValue("@badgeId", row.BadgeId);
        command.Parameters.AddWithValue("@barcode", row.Barcode);
        command.Parameters.AddWithValue("@status", row.Status);
        command.Parameters.AddWithValue("@cycle", row.Cycle);
        command.Parameters.AddWithValue("@taxistaId", (object?)row.TaxistaId ?? DBNull.Value);
        command.Parameters.AddWithValue("@taxistaName", row.TaxistaName);
        command.Parameters.AddWithValue("@createdAt", row.CreatedAt.UtcDateTime);
    }
}

public sealed class CascoBadgeSyncHttpException : Exception
{
    public CascoBadgeSyncHttpException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
