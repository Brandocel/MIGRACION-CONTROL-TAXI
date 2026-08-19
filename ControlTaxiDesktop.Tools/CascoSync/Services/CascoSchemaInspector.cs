using System.Data;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

public sealed class CascoSchemaInspector
{
    private readonly string _connectionString;

    public CascoSchemaInspector(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<CascoSchemaSnapshot> InspectAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var appMovilRegistroColumns = await ReadColumnsAsync(connection, "dbo", "AppMovilRegistro", cancellationToken);
        var appMovilFolioControlColumns = await ReadColumnsAsync(connection, "dbo", "AppMovilFolioControl", cancellationToken);

        return new CascoSchemaSnapshot
        {
            AppMovilRegistroColumns = appMovilRegistroColumns,
            AppMovilFolioControlColumns = appMovilFolioControlColumns
        };
    }

    private static async Task<IReadOnlyList<string>> ReadColumnsAsync(SqlConnection connection, string schema, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
ORDER BY ORDINAL_POSITION;";
        command.Parameters.AddWithValue("@schema", schema);
        command.Parameters.AddWithValue("@table", table);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
            names.Add(reader.GetString(0));
        return names;
    }
}

public sealed class CascoSchemaSnapshot
{
    public IReadOnlyList<string> AppMovilRegistroColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AppMovilFolioControlColumns { get; init; } = Array.Empty<string>();
}
