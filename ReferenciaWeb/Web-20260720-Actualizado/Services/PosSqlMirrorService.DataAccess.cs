using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
private async Task<List<Dictionary<string, object?>>> ReadRowsFromSqlAsync(string sql)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandTimeout = 180;
            command.CommandText = ResolveSqlObjectNames(sql);
            return await ReadRowsAsync(command);
        }





private async Task<List<Dictionary<string, object?>>> ReadRowsFromSqlAsyncWithParam(
            string sql,
            params (string Name, object? Value)[] parameters)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandTimeout = 180;
            command.CommandText = ResolveSqlObjectNames(sql);
            foreach (var parameter in parameters)
            {
                AddParameter(command, parameter.Name, parameter.Value);
            }
            return await ReadRowsAsync(command);
        }





private static async Task<HashSet<string>> GetColumnsAsync(DbConnection connection, string table)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 180;
            command.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table";
            AddParameter(command, "@table", table);

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await ExecuteReaderAsync(command);
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }





private static async Task<string?> FindSingleValueAsync(DbConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 180;
            command.CommandText = ResolveSqlObjectNames(sql);
            foreach (var parameter in parameters)
            {
                AddParameter(command, parameter.Name, parameter.Value);
            }

            var result = await ExecuteScalarAsync(command);
            return result == null || result == DBNull.Value ? null : Convert.ToString(result, CultureInfo.InvariantCulture);
        }





private async Task<List<Dictionary<string, object?>>> ReadTopRowsAsync(string tableName, int top)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandTimeout = 180;
            command.CommandText = ResolveSqlObjectNames($"SELECT TOP ({top}) * FROM [{tableName}]");
            return await ReadRowsAsync(command);
        }





private static async Task<List<Dictionary<string, object?>>> ReadRowsAsync(DbCommand command)
        {
            command.CommandText = ResolveSqlObjectNames(command.CommandText);
            var result = new List<Dictionary<string, object?>>();
            await using var reader = await ExecuteReaderAsync(command);
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
                }
                result.Add(row);
            }

            return result;
        }





private static void AddParameter(DbCommand command, string name, object? value)
        {
            command.CommandText = ResolveSqlObjectNames(command.CommandText);
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }





private static async Task<int> ExecuteNonQueryAsync(DbCommand command)
        {
            command.CommandText = ResolveSqlObjectNames(command.CommandText);
            return await command.ExecuteNonQueryAsync();
        }





private static async Task<object?> ExecuteScalarAsync(DbCommand command)
        {
            command.CommandText = ResolveSqlObjectNames(command.CommandText);
            return await command.ExecuteScalarAsync();
        }





private static async Task<DbDataReader> ExecuteReaderAsync(DbCommand command)
        {
            command.CommandText = ResolveSqlObjectNames(command.CommandText);
            return await command.ExecuteReaderAsync();
        }





private static string ResolveSqlObjectNames(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                return sql;

            return sql;
        }





private static string PosTable(string table) =>
            $"{QuoteSqlIdentifier(_externalDatabaseName)}.{QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier(table)}";





private static string AppTable(string table) =>
            $"{QuoteSqlIdentifier(_appDatabaseName)}.{QuoteSqlIdentifier(_appSchemaName)}.{QuoteSqlIdentifier(table)}";





private static string QuoteSqlIdentifier(string value) => $"[{CleanSqlIdentifier(value, "SqlName")}]";





private static string CleanSqlLiteral(string? value) =>
            CleanSqlIdentifier(value, "SqlName").Replace("'", "''", StringComparison.Ordinal);





private static string CleanSqlIdentifier(string? value, string fallback)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return fallback;

            var builder = new StringBuilder(trimmed.Length);
            foreach (var c in trimmed)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    builder.Append(c);
            }

            return builder.Length == 0 ? fallback : builder.ToString();
        }





private static bool TextMatches(string? value, string normalized) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Contains(normalized, StringComparison.OrdinalIgnoreCase);





private static string FormatDate(DateTime? value) =>
            value.HasValue ? value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty;





private async Task<string?> FindTableAsync(params string[] candidates)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            foreach (var candidate in candidates)
            {
                var exact = await FindSingleValueAsync(connection,
                    "SELECT TOP 1 TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND LOWER(TABLE_NAME)=@name",
                    ("@name", candidate.ToLowerInvariant()));
                if (!string.IsNullOrWhiteSpace(exact))
                    return exact;

                var like = await FindSingleValueAsync(connection,
                    "SELECT TOP 1 TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE='BASE TABLE' AND LOWER(TABLE_NAME) LIKE @name ORDER BY TABLE_NAME",
                    ("@name", $"%{candidate.ToLowerInvariant()}%"));
                if (!string.IsNullOrWhiteSpace(like))
                    return like;
            }

            return null;
        }




    }
}

