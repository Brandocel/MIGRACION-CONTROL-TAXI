using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed record CascoBadgeReturnDiagnostic(
    string Number,
    bool Exists,
    string Staff,
    string OperationFolio,
    string LocalFolio,
    string Unit,
    string Phone,
    string CurrentStatus,
    string SourceTable,
    string StateColumn,
    string ReturnedAtColumn,
    string UserColumn,
    string ProposedAction,
    bool CanReturn,
    int Plaza28Count,
    string ParameterizedSql);

public sealed class CascoBadgeProvider
{
    private readonly BranchConfiguration _configuration;

    public CascoBadgeProvider(BranchConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task<IReadOnlyList<LocalBadge>> GetBadgesAsync(
        string sqlPassword,
        DateTime? start,
        DateTime? end,
        string? staffSearch,
        string? badgeSearch,
        string? operationSearch)
    {
        await using var connection = await OpenConnectionAsync(sqlPassword);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH badge_state AS
            (
                SELECT
                    CONVERT(nvarchar(50), g.gafete) AS Numero,
                    MAX(CASE WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 1 ELSE 0 END) AS HasActive
                FROM dbo.gafete g
                WHERE COALESCE(g.gafete, '') <> ''
                GROUP BY CONVERT(nvarchar(50), g.gafete)
            ),
            ranked AS
            (
                SELECT
                    ROW_NUMBER() OVER (ORDER BY COALESCE(g.hora, g.fecha) DESC, CONVERT(nvarchar(50), g.gafete) DESC) AS Id,
                    CONVERT(nvarchar(50), g.gafete) AS Numero,
                    CONVERT(nvarchar(50), g.matricula) AS Staff,
                    CONVERT(nvarchar(50), g.folioperacion) AS FolioOperacion,
                    g.fecha AS FechaEntrega,
                    COALESCE(g.hora, g.fecha) AS FechaActividad,
                    CASE
                        WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 'OCUPADO'
                        WHEN UPPER(COALESCE(g.venta, '')) = 'S' THEN 'SUSPENDIDO'
                        WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN 'LIBRE'
                        ELSE COALESCE(g.venta, '')
                    END AS Estatus,
                    CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN g.hora ELSE NULL END AS Regreso,
                    ROW_NUMBER() OVER (
                        PARTITION BY CONVERT(nvarchar(50), g.gafete)
                        ORDER BY
                            CASE
                                WHEN COALESCE(bs.HasActive, 0) = 1 THEN
                                    CASE UPPER(COALESCE(g.venta, ''))
                                        WHEN 'A' THEN 0
                                        WHEN 'S' THEN 1
                                        WHEN 'R' THEN 2
                                        ELSE 3
                                    END
                                ELSE
                                    CASE UPPER(COALESCE(g.venta, ''))
                                        WHEN 'R' THEN 0
                                        WHEN 'S' THEN 1
                                        WHEN 'A' THEN 2
                                        ELSE 3
                                    END
                            END,
                            COALESCE(g.hora, g.fecha) DESC
                    ) AS rn
                FROM dbo.gafete g
                LEFT JOIN badge_state bs
                  ON bs.Numero = CONVERT(nvarchar(50), g.gafete)
                WHERE COALESCE(g.gafete, '') <> ''
                  AND (
                        (@inicio IS NULL AND @finSiguiente IS NULL)
                     OR (g.fecha IS NOT NULL AND (@inicio IS NULL OR g.fecha >= @inicio) AND (@finSiguiente IS NULL OR g.fecha < @finSiguiente))
                     OR (g.fecha IS NULL AND g.hora IS NOT NULL AND (@inicio IS NULL OR g.hora >= @inicio) AND (@finSiguiente IS NULL OR g.hora < @finSiguiente))
                  )
            )
            SELECT TOP (500)
                r.Id,
                r.Numero,
                r.Estatus,
                CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE r.Staff END AS Staff,
                r.FolioOperacion,
                CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(a.unidad, '') END AS Unidad,
                CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') END AS Telefono,
                CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(a.nacionalidad, '') END AS Nacionalidad,
                COALESCE(CONVERT(nvarchar(30), r.FechaEntrega, 120), '') AS FechaEntrega,
                COALESCE(CONVERT(nvarchar(30), r.Regreso, 120), '') AS Regreso
            FROM ranked r
            OUTER APPLY
            (
                SELECT TOP (1)
                    a.unidad,
                    a.telefono_taxista,
                    a.telefono_contacto,
                    a.nacionalidad
                FROM dbo.AppMovilRegistro a
                WHERE UPPER(COALESCE(r.Estatus, '')) <> 'LIBRE'
                  AND a.sitio = @sitio
                  AND (
                        UPPER(COALESCE(a.folio_app, '')) = UPPER(r.FolioOperacion)
                     OR UPPER(COALESCE(a.folio_app_original, '')) = UPPER(r.FolioOperacion)
                     OR (ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(COALESCE(r.FolioOperacion, '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, r.FolioOperacion))
                     OR (ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(COALESCE(r.FolioOperacion, '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, r.FolioOperacion))
                     OR ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(a.folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
                        LIKE '%,' + r.Numero + ',%'
                  )
                ORDER BY
                    CASE
                        WHEN UPPER(COALESCE(a.folio_app, '')) = UPPER(r.FolioOperacion) THEN 0
                        WHEN UPPER(COALESCE(a.folio_app_original, '')) = UPPER(r.FolioOperacion) THEN 1
                        ELSE 10
                    END,
                    a.fecha_operacion DESC
            ) a
            WHERE r.rn = 1
              AND (@staff IS NULL OR UPPER(r.Staff) LIKE UPPER(@staff))
              AND (@badge IS NULL OR UPPER(r.Numero) LIKE UPPER(@badge))
              AND (@folio IS NULL OR UPPER(r.FolioOperacion) LIKE UPPER(@folio))
            ORDER BY
                CASE WHEN r.FechaActividad IS NULL THEN 1 ELSE 0 END,
                r.FechaActividad DESC,
                r.Numero DESC;
            """;
        command.Parameters.AddWithValue("@sitio", _configuration.SiteName);
        command.Parameters.AddWithValue("@inicio", start?.Date ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@finSiguiente", end?.Date.AddDays(1) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@staff", string.IsNullOrWhiteSpace(staffSearch) ? (object)DBNull.Value : "%" + staffSearch.Trim() + "%");
        command.Parameters.AddWithValue("@badge", string.IsNullOrWhiteSpace(badgeSearch) ? (object)DBNull.Value : "%" + badgeSearch.Trim() + "%");
        command.Parameters.AddWithValue("@folio", string.IsNullOrWhiteSpace(operationSearch) ? (object)DBNull.Value : "%" + operationSearch.Trim() + "%");

        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<LocalBadge>();
        while (await reader.ReadAsync())
        {
            result.Add(new LocalBadge(
                reader.GetInt64(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                null,
                reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                reader.IsDBNull(7) ? string.Empty : reader.GetString(7)));
        }

        return result;
    }

    public async Task<LocalBadge?> FindBadgeAsync(string sqlPassword, string badge)
    {
        var normalized = Require(badge, "El gafete");
        var provider = new CascoReadOnlyDataProvider(_configuration);
        var row = (await provider.GetDetailedRecordsByBadgeAsync(sqlPassword, normalized)).FirstOrDefault();
        if (row is null)
            return null;

        var resolvedBadge = ResolveBadgeFromList(row.Badge, normalized);
        return new LocalBadge(
            1,
            resolvedBadge,
            "ASIGNADO",
            long.TryParse(row.TaxistaId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var driverId) ? driverId : null,
            row.OperationDate,
            string.Empty,
            row.DriverName,
            string.IsNullOrWhiteSpace(row.OriginalFolio) ? row.FolioControl : row.OriginalFolio,
            row.Unit,
            row.Phone,
            row.Nationality,
            row.FolioControl);
    }

    public async Task<CascoBadgeReturnDiagnostic> GetReturnDiagnosticAsync(string sqlPassword, string number)
    {
        var badge = await FindBadgeAsync(sqlPassword, number);
        var provider = new CascoReadOnlyDataProvider(_configuration);
        var controlStates = await provider.GetBadgeControlStatesAsync(sqlPassword);
        var currentState = FindMatchingControlState(controlStates, badge?.Number, badge?.OperationFolio, badge?.LocalFolio);
        var canReturn = badge is not null && !string.Equals(currentState?.RawStatus, "R", StringComparison.OrdinalIgnoreCase);
        return new CascoBadgeReturnDiagnostic(
            number.Trim(),
            badge is not null,
            badge?.Staff ?? string.Empty,
            badge?.OperationFolio ?? string.Empty,
            badge?.LocalFolio ?? string.Empty,
            badge?.Unit ?? string.Empty,
            badge?.Phone ?? string.Empty,
            currentState?.Status ?? badge?.Status ?? string.Empty,
            "dbo.gafete",
            "venta",
            "hora",
            "usuario",
            canReturn
                ? currentState is null
                    ? "INSERTAR R"
                    : "A/S -> R"
                : "SIN CAMBIO",
            canReturn,
            0,
            BuildDiagnosticSqlPreview(currentState is null));
    }

    public async Task<bool> ReturnBadgeAsync(string sqlPassword, string number, string? operationFolio, string user)
    {
        var badge = Require(number, "El gafete");
        var normalizedOperation = string.IsNullOrWhiteSpace(operationFolio) ? null : operationFolio.Trim();
        var folioNumero = long.TryParse(normalizedOperation, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFolio)
            ? parsedFolio
            : (long?)null;
        var now = DateTime.Now;

        await using var connection = await OpenConnectionAsync(sqlPassword);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var affected = await ReturnBadgeInternalAsync(connection, transaction, badge, normalizedOperation, folioNumero, now, user);
            await transaction.CommitAsync();
            return affected > 0;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(int Updated, int NotUpdated)> ReturnBadgesAsync(string sqlPassword, IEnumerable<LocalBadgeSelection> selections, string user)
    {
        var updated = 0;
        var notUpdated = 0;
        var prepared = selections
            .Where(x => !string.IsNullOrWhiteSpace(x.Number))
            .SelectMany(x => SplitBadgeNumbers(x.Number)
                .Select(number => new LocalBadgeSelection(number, x.OperationFolio)))
            .GroupBy(x => $"{x.Number.Trim().ToUpperInvariant()}|{(x.OperationFolio ?? string.Empty).Trim().ToUpperInvariant()}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

        await using var connection = await OpenConnectionAsync(sqlPassword);
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            foreach (var selection in prepared)
            {
                var normalizedOperation = string.IsNullOrWhiteSpace(selection.OperationFolio) ? null : selection.OperationFolio.Trim();
                var folioNumero = long.TryParse(normalizedOperation, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFolio)
                    ? parsedFolio
                    : (long?)null;
                var affected = await ReturnBadgeInternalAsync(connection, transaction, selection.Number, normalizedOperation, folioNumero, DateTime.Now, user);
                if (affected > 0) updated++;
                else notUpdated++;
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return (updated, notUpdated);
    }

    private async Task<int> ReturnBadgeInternalAsync(
        SqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        string badge,
        string? normalizedOperation,
        long? folioNumero,
        DateTime now,
        string user)
    {
        var sourceRow = await LoadSourceRowAsync(connection, (SqlTransaction)transaction, badge, normalizedOperation, folioNumero);
        if (sourceRow is null)
            return 0;

        var existingState = await LoadCurrentControlStateAsync(connection, (SqlTransaction)transaction, badge, normalizedOperation, folioNumero);
        if (string.Equals(existingState?.RawStatus, "R", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (existingState is null)
            return await InsertReturnedControlRowAsync(connection, (SqlTransaction)transaction, sourceRow, folioNumero, now, user);

        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = """
            ;WITH target_assignment AS
            (
                SELECT TOP (1)
                    CONVERT(nvarchar(50), folioperacion) AS FolioTexto,
                    CASE
                        WHEN ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
                            THEN CONVERT(bigint, folioperacion)
                        ELSE NULL
                    END AS FolioNumero
                FROM dbo.gafete
                WHERE CONVERT(nvarchar(50), gafete) = @gafeteText
                  AND (
                        @folioOperacion IS NULL
                     OR CONVERT(nvarchar(50), folioperacion) = @folioOperacionTexto
                     OR (
                            ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
                        AND CONVERT(bigint, folioperacion) = @folioOperacionNumero
                        )
                      )
                  AND UPPER(COALESCE(venta, '')) IN ('A', 'S')
                ORDER BY COALESCE(hora, fecha) DESC
            )
            UPDATE g
            SET venta = 'R',
                hora = @horaRegreso,
                movimiento = 'REGRESO',
                usuario = @usuario
            FROM dbo.gafete g
            CROSS JOIN target_assignment a
            WHERE CONVERT(nvarchar(50), g.gafete) = @gafeteText
              AND UPPER(COALESCE(g.venta, '')) IN ('A', 'S')
              AND (
                    CONVERT(nvarchar(50), g.folioperacion) = a.FolioTexto
                 OR (
                        a.FolioNumero IS NOT NULL
                    AND ISNUMERIC(CONVERT(nvarchar(50), g.folioperacion)) = 1
                    AND CONVERT(bigint, g.folioperacion) = a.FolioNumero
                    )
                  );
            """;
        command.Parameters.AddWithValue("@gafeteText", badge);
        command.Parameters.AddWithValue("@folioOperacion", normalizedOperation is null ? DBNull.Value : normalizedOperation);
        command.Parameters.AddWithValue("@folioOperacionTexto", normalizedOperation ?? string.Empty);
        command.Parameters.AddWithValue("@folioOperacionNumero", folioNumero.HasValue ? folioNumero.Value : DBNull.Value);
        command.Parameters.AddWithValue("@horaRegreso", now);
        command.Parameters.AddWithValue("@usuario", string.IsNullOrWhiteSpace(user) ? string.Empty : user.Trim());
        var affected = await command.ExecuteNonQueryAsync();

        if (affected > 0)
        {
            await using var suspendCommand = connection.CreateCommand();
            suspendCommand.Transaction = (SqlTransaction)transaction;
            suspendCommand.CommandText = """
                UPDATE g
                SET venta = 'S',
                    hora = @horaRegreso,
                    movimiento = 'SUSPENDIDO',
                    usuario = @usuario
                FROM dbo.gafete g
                WHERE CONVERT(nvarchar(50), g.gafete) = @gafeteText
                  AND UPPER(COALESCE(g.venta, '')) = 'A'
                  AND (
                        @folioOperacion IS NULL
                     OR NOT (
                            CONVERT(nvarchar(50), g.folioperacion) = @folioOperacionTexto
                         OR (
                                @folioOperacionNumero IS NOT NULL
                            AND ISNUMERIC(CONVERT(nvarchar(50), g.folioperacion)) = 1
                            AND CONVERT(bigint, g.folioperacion) = @folioOperacionNumero
                            )
                          )
                      );
                """;
            suspendCommand.Parameters.AddWithValue("@gafeteText", badge);
            suspendCommand.Parameters.AddWithValue("@folioOperacion", normalizedOperation is null ? DBNull.Value : normalizedOperation);
            suspendCommand.Parameters.AddWithValue("@folioOperacionTexto", normalizedOperation ?? string.Empty);
            suspendCommand.Parameters.AddWithValue("@folioOperacionNumero", folioNumero.HasValue ? folioNumero.Value : DBNull.Value);
            suspendCommand.Parameters.AddWithValue("@horaRegreso", now);
            suspendCommand.Parameters.AddWithValue("@usuario", string.IsNullOrWhiteSpace(user) ? string.Empty : user.Trim());
            await suspendCommand.ExecuteNonQueryAsync();
        }

        return affected;
    }

    private static async Task<CascoBadgeControlState?> LoadCurrentControlStateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string badge,
        string? normalizedOperation,
        long? folioNumero)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TOP (1)
                CONVERT(nvarchar(50), g.gafete) AS Badge,
                CONVERT(nvarchar(50), g.folioperacion) AS OperationFolio,
                UPPER(COALESCE(g.venta, '')) AS RawStatus,
                COALESCE(CONVERT(nvarchar(30), COALESCE(g.hora, g.fecha), 120), '') AS ActivityDate
            FROM dbo.gafete g
            WHERE CONVERT(nvarchar(50), g.gafete) = @gafeteText
              AND (
                    @folioOperacion IS NULL
                 OR CONVERT(nvarchar(50), g.folioperacion) = @folioOperacionTexto
                 OR (
                        @folioOperacionNumero IS NOT NULL
                    AND ISNUMERIC(CONVERT(nvarchar(50), g.folioperacion)) = 1
                    AND CONVERT(bigint, g.folioperacion) = @folioOperacionNumero
                    )
                  )
            ORDER BY COALESCE(g.hora, g.fecha) DESC;
            """;
        command.Parameters.AddWithValue("@gafeteText", badge);
        command.Parameters.AddWithValue("@folioOperacion", normalizedOperation is null ? DBNull.Value : normalizedOperation);
        command.Parameters.AddWithValue("@folioOperacionTexto", normalizedOperation ?? string.Empty);
        command.Parameters.AddWithValue("@folioOperacionNumero", folioNumero.HasValue ? folioNumero.Value : DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new CascoBadgeControlState(
            reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty,
            reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty);
    }

    private async Task<CascoReturnSourceRow?> LoadSourceRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string badge,
        string? normalizedOperation,
        long? folioNumero)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TOP (1)
                COALESCE(CAST(id_catalogo AS int), 0) AS TaxistaId,
                COALESCE(CAST(folio_gafete AS nvarchar(50)), '') AS Badge,
                COALESCE(CAST(folio_app_original AS nvarchar(60)), '') AS FolioOriginal,
                COALESCE(CAST(folio_app AS nvarchar(60)), '') AS FolioControl,
                COALESCE(fecha_operacion, fecha_creacion, SYSDATETIME()) AS ActivityDate
            FROM dbo.AppMovilRegistro
            WHERE sitio = @sitio
              AND (
                    LTRIM(RTRIM(COALESCE(folio_gafete, ''))) = @gafeteText
                 OR ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
                    LIKE '%,' + @gafeteText + ',%'
                  )
              AND (
                    @folioOperacion IS NULL
                 OR UPPER(COALESCE(folio_app_original, '')) = UPPER(@folioOperacionTexto)
                 OR UPPER(COALESCE(folio_app, '')) = UPPER(@folioOperacionTexto)
                 OR (
                        @folioOperacionNumero IS NOT NULL
                    AND (
                           (ISNUMERIC(COALESCE(folio_app_original, '')) = 1 AND CONVERT(bigint, folio_app_original) = @folioOperacionNumero)
                        OR (ISNUMERIC(COALESCE(folio_app, '')) = 1 AND CONVERT(bigint, folio_app) = @folioOperacionNumero)
                    )
                  )
              )
            ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;
            """;
        command.Parameters.AddWithValue("@sitio", _configuration.SiteName);
        command.Parameters.AddWithValue("@gafeteText", badge);
        command.Parameters.AddWithValue("@folioOperacion", normalizedOperation is null ? DBNull.Value : normalizedOperation);
        command.Parameters.AddWithValue("@folioOperacionTexto", normalizedOperation ?? string.Empty);
        command.Parameters.AddWithValue("@folioOperacionNumero", folioNumero.HasValue ? folioNumero.Value : DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        var originalFolio = reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? string.Empty;
        var controlFolio = reader.IsDBNull(3) ? string.Empty : Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? string.Empty;
        var parsedFolio = ResolveNumericFolio(normalizedOperation, originalFolio, controlFolio);
        return new CascoReturnSourceRow(
            reader.IsDBNull(0) ? (int?)null : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            badge,
            originalFolio,
            controlFolio,
            parsedFolio,
            reader.IsDBNull(4) ? DateTime.Now : Convert.ToDateTime(reader.GetValue(4), CultureInfo.InvariantCulture));
    }

    private static async Task<int> InsertReturnedControlRowAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CascoReturnSourceRow sourceRow,
        long? folioNumero,
        DateTime now,
        string user)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dbo.gafete
            (
                matricula,
                gafete,
                fecha,
                venta,
                hora,
                folioperacion,
                movimiento,
                usuario
            )
            VALUES
            (
                @matricula,
                @gafeteNumero,
                @fechaOperacion,
                'R',
                @horaRegreso,
                @folioOperacionNumero,
                'REGRESO',
                @usuario
            );
            """;
        command.Parameters.AddWithValue("@matricula", string.IsNullOrWhiteSpace(sourceRow.OriginalFolio) ? (object)DBNull.Value : sourceRow.OriginalFolio);
        command.Parameters.AddWithValue("@gafeteNumero", int.TryParse(sourceRow.Badge, NumberStyles.Integer, CultureInfo.InvariantCulture, out var badgeNumber) ? badgeNumber : (object)DBNull.Value);
        command.Parameters.AddWithValue("@fechaOperacion", sourceRow.ActivityDate);
        command.Parameters.AddWithValue("@horaRegreso", now);
        command.Parameters.AddWithValue("@folioOperacionNumero", sourceRow.OperationNumber.HasValue ? sourceRow.OperationNumber.Value : folioNumero.HasValue ? folioNumero.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("@usuario", string.IsNullOrWhiteSpace(user) ? string.Empty : user.Trim());
        return await command.ExecuteNonQueryAsync();
    }

    private static long? ResolveNumericFolio(string? preferred, string? original, string? control)
    {
        foreach (var candidate in new[] { preferred, original, control })
        {
            if (long.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
        }

        return null;
    }

    private static string ResolveBadgeFromList(string? badgeList, string requestedBadge)
    {
        var requested = Require(requestedBadge, "El gafete");
        var badges = SplitBadgeNumbers(badgeList).ToArray();
        return badges.FirstOrDefault(x => string.Equals(x, requested, StringComparison.OrdinalIgnoreCase))
            ?? badges.FirstOrDefault()
            ?? requested;
    }

    private static IEnumerable<string> SplitBadgeNumbers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var token in value.Split([',', ';', '/', '|', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(token))
                yield return token.Trim();
        }
    }

    private static CascoBadgeControlState? FindMatchingControlState(
        IReadOnlyList<CascoBadgeControlState> states,
        string? badge,
        string? operationFolio,
        string? localFolio)
    {
        if (string.IsNullOrWhiteSpace(badge))
            return null;

        return states.FirstOrDefault(state =>
            string.Equals(state.Badge, badge.Trim(), StringComparison.OrdinalIgnoreCase)
            && (
                MatchFolio(state.OperationFolio, operationFolio)
                || MatchFolio(state.OperationFolio, localFolio)
            ))
            ?? states.FirstOrDefault(state =>
                string.Equals(state.Badge, badge.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchFolio(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftValue)
            && long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightValue)
            && leftValue == rightValue;
    }

    private static string BuildDiagnosticSqlPreview(bool isInsert) =>
        isInsert
            ? """
              INSERT INTO dbo.gafete (matricula, gafete, fecha, venta, hora, folioperacion, movimiento, usuario)
              VALUES (@matricula, @gafeteNumero, @fechaOperacion, 'R', @horaRegreso, @folioOperacionNumero, 'REGRESO', @usuario);
              """
            : """
              UPDATE dbo.gafete
              SET venta = 'R',
                  hora = @horaRegreso,
                  movimiento = 'REGRESO',
                  usuario = @usuario
              WHERE gafete = @gafeteText
                AND folioperacion = @folioOperacion
                AND venta IN ('A', 'S');
              """;

    private async Task<SqlConnection> OpenConnectionAsync(string sqlPassword)
    {
        if (string.IsNullOrWhiteSpace(sqlPassword))
            throw new InvalidOperationException("La contrasena SQL debe proporcionarse mediante la variable de entorno CASCO_SQL_PASSWORD.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = _configuration.SqlServer,
            InitialCatalog = _configuration.Database,
            UserID = CascoSqlIdentity.ResolveUser(_configuration),
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 30
        };

        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string Require(string value, string label) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"{label} es obligatorio.");

    private sealed record CascoReturnSourceRow(
        int? TaxistaId,
        string Badge,
        string OriginalFolio,
        string ControlFolio,
        long? OperationNumber,
        DateTime ActivityDate);
}
