using System.Globalization;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Services;

public sealed class LocalOperationsRepository(LocalDatabase database)
{
    private readonly LocalSqlServerSource? _sqlSource = LocalSqlServerSource.TryLoad();
    private readonly CommissionSettingsRepository _commissionSettings = new(database);
    public Task<IReadOnlyList<LocalRate>> GetRatesAsync() => ReadAsync("SELECT Id, Tipo, Nombre, Dejada, Minimo, Maximo, Activa FROM LocalTarifas ORDER BY Tipo, Nombre;", row => new LocalRate(row.GetInt64(0), row.GetString(1), row.GetString(2), Decimal(row, 3), Decimal(row, 4), Decimal(row, 5), row.GetInt64(6) == 1));
    public Task<IReadOnlyList<LocalHotel>> GetHotelsAsync() => ReadAsync("SELECT Id, Nombre, Activo FROM LocalHoteles ORDER BY Nombre;", row => new LocalHotel(row.GetInt64(0), row.GetString(1), row.GetInt64(2) == 1));
    public async Task<IReadOnlyList<LocalDriver>> GetDriversAsync()
    {
        var local = (await ReadAsync("SELECT Id, Clave, Nombre, Telefono, Placas, Modelo, Unidad, TipoServicio, Estatus FROM LocalTaxistas ORDER BY Nombre;", row => new LocalDriver(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5), row.GetString(6), row.GetString(7), row.GetString(8)))).ToList();
        var merged = local.ToDictionary(x => NormalizeCatalogKey(x.Code), x => x, StringComparer.OrdinalIgnoreCase);

        var imported = await LoadImportedDriversAsync();
        foreach (var driver in imported)
        {
            var key = NormalizeCatalogKey(driver.Code);
            if (!merged.ContainsKey(key))
                merged[key] = driver;
        }

        return merged.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    public async Task<IReadOnlyDictionary<long, IReadOnlyList<string>>> GetDriverBadgeLookupAsync()
    {
        var rows = (await ReadAsync("""
            SELECT TaxistaId, Numero
            FROM LocalGafetes
            WHERE TaxistaId IS NOT NULL
              AND COALESCE(Numero, '') <> ''
            ORDER BY Numero;
            """, row => (DriverId: row.GetInt64(0), Badge: row.GetString(1)))).ToList();

        rows.AddRange(await LoadImportedDriverBadgesAsync());

        return rows
            .GroupBy(x => x.DriverId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(x => x.Badge).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<IReadOnlyList<LocalDriver>> LoadImportedDriversAsync()
    {
        var drivers = await LoadImportedDriversFromSqliteAsync();
        if (drivers.Count > 0)
            return drivers;

        if (_sqlSource is null)
            return [];

        try
        {
            return await LoadImportedDriversFromSqlServerAsync();
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<(long DriverId, string Badge)>> LoadImportedDriverBadgesAsync()
    {
        var badges = await LoadImportedDriverBadgesFromSqliteAsync();
        if (badges.Count > 0)
            return badges;

        if (_sqlSource is null)
            return [];

        try
        {
            return await LoadImportedDriverBadgesFromSqlServerAsync();
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<LocalTransport>> LoadImportedTransportsAsync()
    {
        var transports = await LoadImportedTransportsFromSqliteAsync();
        if (transports.Count > 0)
            return transports;

        if (_sqlSource is null)
            return [];

        try
        {
            return await LoadImportedTransportsFromSqlServerAsync();
        }
        catch
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<LocalDriver>> LoadImportedDriversFromSqliteAsync()
    {
        await using var connection = database.Open();
        var hasTaxi = await HasTableAsync(connection, "mkt__dbo__cataxi");
        var hasDejadas = await HasTableAsync(connection, "mkt__dbo__dejadas");
        var hasApp = await HasTableAsync(connection, "mkt__dbo__AppMovilRegistro");
        if (!hasTaxi && !hasDejadas && !hasApp)
            return [];

        var rows = new List<CatalogDriverRow>();
        if (hasTaxi)
        {
            rows.AddRange(await ReadAsync("""
                SELECT
                  COALESCE(CAST(idtaxi AS TEXT), ''),
                  COALESCE(nombre, ''),
                  COALESCE(tipo, ''),
                  '',
                  COALESCE(telefono, ''),
                  CASE WHEN UPPER(COALESCE(activo, 'S')) = 'N' THEN 'Inactivo' ELSE 'Activo' END,
                  3,
                  ''
                FROM "mkt__dbo__cataxi"
                WHERE COALESCE(TRIM(nombre), '') <> '';
                """, row => new CatalogDriverRow(
                Text(row, 0),
                CleanTaxiName(Text(row, 1)),
                Text(row, 2),
                string.Empty,
                string.Empty,
                Text(row, 4),
                Text(row, 5),
                3,
                null)));
        }

        if (hasDejadas)
        {
            rows.AddRange(await ReadAsync("""
                SELECT
                  COALESCE(CAST(idtaxi AS TEXT), ''),
                  COALESCE(nombrevendedor, ''),
                  COALESCE(tipotransporte, ''),
                  COALESCE(unidad, ''),
                  COALESCE(telefono, ''),
                  'Activo',
                  2,
                  COALESCE(fecha, '')
                FROM "mkt__dbo__dejadas"
                WHERE COALESCE(idtaxi, 0) > 0
                  AND COALESCE(TRIM(nombrevendedor), '') <> '';
                """, row => new CatalogDriverRow(
                Text(row, 0),
                CleanTaxiName(Text(row, 1)),
                Text(row, 2),
                string.Empty,
                Text(row, 3),
                Text(row, 4),
                "Activo",
                2,
                TryParseDate(Text(row, 7)))));
        }

        if (hasApp)
        {
            rows.AddRange(await ReadAsync("""
                SELECT
                  COALESCE(CAST(id_catalogo AS TEXT), ''),
                  COALESCE(vendedor_nombre, ''),
                  COALESCE(tipo_operacion, unidad, ''),
                  COALESCE(placas, unidad, ''),
                  COALESCE(telefono_taxista, ''),
                  'Activo',
                  1,
                  COALESCE(fecha_operacion, fecha_creacion, '')
                FROM "mkt__dbo__AppMovilRegistro"
                WHERE COALESCE(id_catalogo, 0) > 0
                  AND COALESCE(TRIM(vendedor_nombre), '') <> '';
                """, row => new CatalogDriverRow(
                Text(row, 0),
                CleanTaxiName(Text(row, 1)),
                Text(row, 2),
                Text(row, 3),
                Text(row, 3),
                Text(row, 4),
                "Activo",
                1,
                TryParseDate(Text(row, 7)))));
        }

        return MapCatalogDrivers(rows);
    }

    private async Task<IReadOnlyList<LocalDriver>> LoadImportedDriversFromSqlServerAsync()
    {
        if (_sqlSource is null)
            return [];

        var rows = new List<CatalogDriverRow>();
        await using var connection = await _sqlSource.OpenPosAsync();

        rows.AddRange(await ReadSqlAsync(connection, $"""
            SELECT
              COALESCE(CAST(idtaxi AS nvarchar(60)), ''),
              COALESCE(nombre, ''),
              COALESCE(tipo, ''),
              '',
              COALESCE(telefono, ''),
              CASE WHEN UPPER(COALESCE(activo, 'S')) = 'N' THEN 'Inactivo' ELSE 'Activo' END,
              3,
              NULL
            FROM {_sqlSource.PosTable("cataxi")}
            WHERE COALESCE(LTRIM(RTRIM(nombre)), '') <> '';
            """, reader => new CatalogDriverRow(
            SqlText(reader, 0),
            CleanTaxiName(SqlText(reader, 1)),
            SqlText(reader, 2),
            string.Empty,
            string.Empty,
            SqlText(reader, 4),
            SqlText(reader, 5),
            3,
            null)));

        rows.AddRange(await ReadSqlAsync(connection, $"""
            SELECT
              COALESCE(CAST(idtaxi AS nvarchar(60)), ''),
              COALESCE(nombrevendedor, ''),
              COALESCE(tipotransporte, ''),
              COALESCE(unidad, ''),
              COALESCE(telefono, ''),
              'Activo',
              2,
              fecha
            FROM {_sqlSource.PosTable("dejadas")}
            WHERE COALESCE(idtaxi, 0) > 0
              AND COALESCE(LTRIM(RTRIM(nombrevendedor)), '') <> '';
            """, reader => new CatalogDriverRow(
            SqlText(reader, 0),
            CleanTaxiName(SqlText(reader, 1)),
            SqlText(reader, 2),
            string.Empty,
            SqlText(reader, 3),
            SqlText(reader, 4),
            "Activo",
            2,
            SqlDate(reader, 7))));

        rows.AddRange(await ReadSqlAsync(connection, $"""
            SELECT
              COALESCE(CAST(id_catalogo AS nvarchar(60)), ''),
              COALESCE(vendedor_nombre, ''),
              COALESCE(tipo_operacion, unidad, ''),
              COALESCE(placas, unidad, ''),
              COALESCE(telefono_taxista, ''),
              'Activo',
              1,
              COALESCE(fecha_operacion, fecha_creacion)
            FROM {_sqlSource.PosTable("AppMovilRegistro")}
            WHERE COALESCE(id_catalogo, 0) > 0
              AND COALESCE(LTRIM(RTRIM(vendedor_nombre)), '') <> '';
            """, reader => new CatalogDriverRow(
            SqlText(reader, 0),
            CleanTaxiName(SqlText(reader, 1)),
            SqlText(reader, 2),
            SqlText(reader, 3),
            SqlText(reader, 3),
            SqlText(reader, 4),
            "Activo",
            1,
            SqlDate(reader, 7))));

        return MapCatalogDrivers(rows);
    }

    private async Task<IReadOnlyList<(long DriverId, string Badge)>> LoadImportedDriverBadgesFromSqliteAsync()
    {
        await using var connection = database.Open();
        var rows = new List<(long DriverId, string Badge)>();
        if (await HasTableAsync(connection, "mkt__dbo__AppMovilRegistro"))
        {
            rows.AddRange(await ReadAsync("""
                SELECT DISTINCT
                  COALESCE(id_catalogo, 0),
                  COALESCE(folio_gafete, '')
                FROM "mkt__dbo__AppMovilRegistro"
                WHERE COALESCE(id_catalogo, 0) > 0
                  AND COALESCE(TRIM(folio_gafete), '') <> '';
                """, row => (row.IsDBNull(0) ? 0L : Convert.ToInt64(row.GetValue(0), CultureInfo.InvariantCulture), Text(row, 1))));
        }

        return rows
            .Where(x => x.DriverId > 0 && !string.IsNullOrWhiteSpace(x.Badge))
            .Distinct()
            .ToArray();
    }

    private async Task<IReadOnlyList<(long DriverId, string Badge)>> LoadImportedDriverBadgesFromSqlServerAsync()
    {
        if (_sqlSource is null)
            return [];

        await using var connection = await _sqlSource.OpenPosAsync();
        var rows = await ReadSqlAsync(connection, $"""
            SELECT DISTINCT
              COALESCE(id_catalogo, 0),
              COALESCE(folio_gafete, '')
            FROM {_sqlSource.PosTable("AppMovilRegistro")}
            WHERE COALESCE(id_catalogo, 0) > 0
              AND COALESCE(LTRIM(RTRIM(folio_gafete)), '') <> '';
            """, reader => (SqlInt64(reader, 0), SqlText(reader, 1)));
        return rows
            .Where(x => x.Item1 > 0 && !string.IsNullOrWhiteSpace(x.Item2))
            .Distinct()
            .Select(x => (x.Item1, x.Item2))
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalTransport>> LoadImportedTransportsFromSqliteAsync()
    {
        await using var connection = database.Open();
        if (!await HasTableAsync(connection, "mkt__dbo__transporte"))
            return [];

        return await ReadAsync("""
            SELECT
              0,
              COALESCE(tipo, ''),
              COALESCE(nombre, ''),
              COALESCE(minimo, 0),
              COALESCE(maximo, 0),
              COALESCE(comision, 0),
              COALESCE(efectivo, 0),
              COALESCE(tarjeta, 0),
              COALESCE(amexco, 0),
              1
            FROM "mkt__dbo__transporte"
            WHERE COALESCE(TRIM(tipo), '') <> '' OR COALESCE(TRIM(nombre), '') <> ''
            ORDER BY COALESCE(nombre, ''), COALESCE(tipo, '');
            """, row => new LocalTransport(
            0,
            Text(row, 1),
            Text(row, 2),
            Decimal(row, 3),
            Decimal(row, 4),
            Decimal(row, 5),
            Decimal(row, 6),
            Decimal(row, 7),
            Decimal(row, 8),
            true));
    }

    private async Task<IReadOnlyList<LocalTransport>> LoadImportedTransportsFromSqlServerAsync()
    {
        if (_sqlSource is null)
            return [];

        await using var connection = await _sqlSource.OpenPosAsync();
        return await ReadSqlAsync(connection, $"""
            SELECT
              COALESCE(tipo, ''),
              COALESCE(nombre, ''),
              COALESCE(minimo, 0),
              COALESCE(maximo, 0),
              COALESCE(comision, 0),
              COALESCE(efectivo, 0),
              COALESCE(tarjeta, 0),
              COALESCE(amexco, 0)
            FROM {_sqlSource.PosTable("transporte")}
            WHERE COALESCE(LTRIM(RTRIM(tipo)), '') <> '' OR COALESCE(LTRIM(RTRIM(nombre)), '') <> ''
            ORDER BY COALESCE(nombre, ''), COALESCE(tipo, '');
            """, reader => new LocalTransport(
            0,
            SqlText(reader, 0),
            SqlText(reader, 1),
            SqlDecimal(reader, 2),
            SqlDecimal(reader, 3),
            SqlDecimal(reader, 4),
            SqlDecimal(reader, 5),
            SqlDecimal(reader, 6),
            SqlDecimal(reader, 7),
            true));
    }

    private static IReadOnlyList<LocalDriver> MapCatalogDrivers(IEnumerable<CatalogDriverRow> rows) =>
        rows
            .Where(x => !string.IsNullOrWhiteSpace(x.Code) && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => string.Join("|",
                NormalizeCatalogKey(x.Code),
                NormalizeCatalogKey(x.Name),
                NormalizeCatalogKey(x.ServiceType),
                NormalizeCatalogKey(x.Plates),
                NormalizeCatalogKey(x.Unit)), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(x => x.Priority).ThenByDescending(x => x.Date).First())
            .Select((x, index) => new LocalDriver(index + 1, x.Code, x.Name, x.Phone, x.Plates, string.Empty, x.Unit, x.ServiceType, x.Status))
            .ToArray();

    private static string NormalizeCatalogKey(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    private static string CleanTaxiName(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace("|", string.Empty, StringComparison.Ordinal).Trim();
    private static DateTime? TryParseDate(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;

    private static string SqlText(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
    private static long SqlInt64(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0L : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static decimal SqlDecimal(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    private static DateTime? SqlDate(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static async Task<List<T>> ReadSqlAsync<T>(SqlConnection connection, string sql, Func<SqlDataReader, T> map)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
            rows.Add(map(reader));
        return rows;
    }

    private sealed record CatalogDriverRow(string Code, string Name, string ServiceType, string Plates, string Unit, string Phone, string Status, int Priority, DateTime? Date);
    public Task<IReadOnlyList<LocalBadge>> GetBadgesAsync() => GetBadgesAsync(null, null, null, null, null);

    public async Task<IReadOnlyList<LocalBadge>> GetBadgesAsync(DateTime? start, DateTime? end, string? staffSearch, string? badgeSearch, string? operationSearch)
    {
        if (_sqlSource is not null)
        {
            try
            {
                return await GetBadgesFromSqlServerAsync(start, end, staffSearch, badgeSearch, operationSearch);
            }
            catch
            {
                // Conserva el respaldo local si la consulta directa no esta disponible.
            }
        }

        await using var connection = database.Open();
        var hasImportedBadges = await HasTableAsync(connection, "mkt__dbo__gafete");
        var hasAppMovil = await HasTableAsync(connection, "mkt__dbo__AppMovilRegistro");
        var hasDejadas = await HasTableAsync(connection, "mkt__dbo__dejadas");
        if (hasImportedBadges)
        {
            var appJoin = hasAppMovil
                ? """
                LEFT JOIN "mkt__dbo__AppMovilRegistro" a
                  ON CAST(COALESCE(a.folio_app, '') AS TEXT) = r.FolioOperacion
                  OR CAST(COALESCE(a.folio_app_original, '') AS TEXT) = r.FolioOperacion
                  OR (',' || REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(a.folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') || ',') LIKE '%,' || r.Numero || ',%'
                """
                : string.Empty;
            var dejadaJoin = hasDejadas
                ? """
                LEFT JOIN "mkt__dbo__dejadas" d
                  ON CAST(COALESCE(d.folioregistro, '') AS TEXT) = r.FolioOperacion
                  OR CAST(COALESCE(d.folioregistrostr, '') AS TEXT) = r.FolioOperacion
                """
                : string.Empty;
            var unitSql = hasAppMovil && hasDejadas
                ? "CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(a.unidad, d.unidad, '') END"
                : hasAppMovil
                    ? "CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(a.unidad, '') END"
                    : hasDejadas
                        ? "CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(d.unidad, '') END"
                        : "''";
            var phoneSql = hasAppMovil && hasDejadas
                ? "CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), d.telefono, '') END"
                : hasAppMovil
                    ? "CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') END"
                    : hasDejadas
                        ? "CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(d.telefono, '') END"
                        : "''";
            var nationalitySql = hasAppMovil
                ? "CASE WHEN r.Estatus = 'LIBRE' THEN '' ELSE COALESCE(a.nacionalidad, '') END"
                : "''";
            var sql = $"""
                WITH badge_state AS (
                    SELECT
                        CAST(COALESCE(g.gafete, '') AS TEXT) AS Numero,
                        MAX(CASE WHEN UPPER(TRIM(COALESCE(g.venta, ''))) = 'A' THEN 1 ELSE 0 END) AS HasActive
                    FROM "mkt__dbo__gafete" g
                    WHERE COALESCE(g.gafete, '') <> ''
                    GROUP BY CAST(COALESCE(g.gafete, '') AS TEXT)
                ),
                ranked AS (
                    SELECT
                        g.rowid AS Id,
                        CAST(COALESCE(g.gafete, '') AS TEXT) AS Numero,
                        CAST(COALESCE(g.matricula, '') AS TEXT) AS Staff,
                        CAST(COALESCE(g.folioperacion, '') AS TEXT) AS FolioOperacion,
                        COALESCE(g.fecha, '') AS FechaEntrega,
                        COALESCE(g.hora, g.fecha, '') AS FechaActividad,
                        CASE UPPER(TRIM(COALESCE(g.venta,'')))
                            WHEN 'A' THEN 'OCUPADO'
                            WHEN 'S' THEN 'SUSPENDIDO'
                            WHEN 'R' THEN 'LIBRE'
                            ELSE COALESCE(g.venta, '')
                        END AS Estatus,
                        CASE WHEN UPPER(TRIM(COALESCE(g.venta,''))) = 'R' THEN COALESCE(g.hora, g.fecha, '') ELSE '' END AS Regreso,
                        ROW_NUMBER() OVER (
                            PARTITION BY CAST(COALESCE(g.gafete, '') AS TEXT)
                            ORDER BY
                                CASE
                                    WHEN COALESCE(bs.HasActive, 0) = 1 THEN
                                        CASE UPPER(TRIM(COALESCE(g.venta,'')))
                                            WHEN 'A' THEN 0
                                            WHEN 'S' THEN 1
                                            WHEN 'R' THEN 2
                                            ELSE 3
                                        END
                                    ELSE
                                        CASE UPPER(TRIM(COALESCE(g.venta,'')))
                                            WHEN 'R' THEN 0
                                            WHEN 'S' THEN 1
                                            WHEN 'A' THEN 2
                                            ELSE 3
                                        END
                                END,
                                COALESCE(g.hora, g.fecha, '') DESC
                        ) AS rn
                    FROM "mkt__dbo__gafete" g
                    LEFT JOIN badge_state bs
                      ON bs.Numero = CAST(COALESCE(g.gafete, '') AS TEXT)
                    WHERE COALESCE(g.gafete,'') <> ''
                      AND (
                            ($start IS NULL AND $endNext IS NULL)
                         OR (COALESCE(g.fecha, '') <> '' AND ($start IS NULL OR substr(g.fecha, 1, 10) >= $start) AND ($endNext IS NULL OR substr(g.fecha, 1, 10) < $endNext))
                         OR ((COALESCE(g.fecha, '') = '') AND COALESCE(g.hora, '') <> '' AND ($start IS NULL OR substr(g.hora, 1, 10) >= $start) AND ($endNext IS NULL OR substr(g.hora, 1, 10) < $endNext))
                         )
                )
                SELECT
                    r.Id,
                    r.Numero,
                    r.Estatus,
                    r.Staff,
                    r.FolioOperacion,
                    {unitSql} AS Unidad,
                    {phoneSql} AS Telefono,
                    {nationalitySql} AS Nacionalidad,
                    r.FechaEntrega,
                    r.Regreso
                FROM ranked r
                {appJoin}
                {dejadaJoin}
                WHERE r.rn = 1
                  AND ($staff IS NULL OR UPPER(r.Staff) LIKE UPPER($staff))
                  AND ($badge IS NULL OR UPPER(r.Numero) LIKE UPPER($badge))
                  AND ($folio IS NULL OR UPPER(r.FolioOperacion) LIKE UPPER($folio))
                ORDER BY CASE WHEN COALESCE(r.FechaActividad, '') = '' THEN 1 ELSE 0 END, r.FechaActividad DESC, r.Numero DESC
                LIMIT 500;
                """;
            var rows = (await ReadAsync(sql, row => new LocalBadge(row.GetInt64(0), Text(row, 1), Text(row, 2), null, Text(row, 8), Text(row, 9), Text(row, 3), Text(row, 4), Text(row, 5), Text(row, 6), Text(row, 7)),
                ("$start", start?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$endNext", end?.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$staff", string.IsNullOrWhiteSpace(staffSearch) ? null : "%" + staffSearch.Trim() + "%"),
                ("$badge", string.IsNullOrWhiteSpace(badgeSearch) ? null : "%" + badgeSearch.Trim() + "%"),
                ("$folio", string.IsNullOrWhiteSpace(operationSearch) ? null : "%" + operationSearch.Trim() + "%"))).ToList();

            return rows;
        }
        return await ReadAsync("""
            SELECT Id, Numero, Estatus, TaxistaId, FechaAsignacion, FechaRegreso
            FROM LocalGafetes
            WHERE ($staff IS NULL OR 1 = 1)
              AND ($badge IS NULL OR UPPER(Numero) LIKE UPPER($badge))
            ORDER BY Numero;
            """,
            row => new LocalBadge(row.GetInt64(0), row.GetString(1), row.GetString(2), row.IsDBNull(3) ? null : row.GetInt64(3), row.IsDBNull(4) ? null : row.GetString(4), row.IsDBNull(5) ? null : row.GetString(5)),
            ("$staff", string.IsNullOrWhiteSpace(staffSearch) ? null : "%" + staffSearch.Trim() + "%"),
            ("$badge", string.IsNullOrWhiteSpace(badgeSearch) ? null : "%" + badgeSearch.Trim() + "%"));
    }

    private async Task<IReadOnlyList<LocalBadge>> GetBadgesFromSqlServerAsync(DateTime? start, DateTime? end, string? staffSearch, string? badgeSearch, string? operationSearch)
    {
        if (_sqlSource is null) return [];

        await using var connection = await _sqlSource.OpenPosAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            WITH badge_state AS
            (
                SELECT
                    CONVERT(nvarchar(50), g.gafete) AS Numero,
                    MAX(CASE WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 1 ELSE 0 END) AS HasActive
                FROM {_sqlSource.PosTable("gafete")} g
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
                FROM {_sqlSource.PosTable("gafete")} g
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
                FROM {_sqlSource.PosTable("AppMovilRegistro")} a
                WHERE UPPER(COALESCE(r.Estatus, '')) <> 'LIBRE'
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
    public Task<IReadOnlyList<LocalRecord>> GetRecordsAsync() => ReadAsync("SELECT Id, Folio, Fecha, TaxistaId, HotelId, TarifaId, Gafete, Pax, Origen, Destino, Importe, MetodoPago, Notas, Usuario FROM LocalRegistros ORDER BY Fecha DESC, Id DESC;", row => new LocalRecord(row.GetInt64(0), row.GetString(1), DateTime.Parse(row.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), NullableInt64(row, 3), NullableInt64(row, 4), NullableInt64(row, 5), row.GetString(6), row.GetInt32(7), row.GetString(8), row.GetString(9), Decimal(row, 10), row.GetString(11), row.GetString(12), row.GetString(13)));
    public async Task<IReadOnlyList<LocalTransport>> GetTransportsAsync()
    {
        var local = (await ReadAsync("SELECT Id, Clave, Nombre, Minimo, Maximo, Comision, DescuentoEfectivo, DescuentoTarjeta, DescuentoAmex, Activo FROM LocalTransportes ORDER BY Nombre;", row => new LocalTransport(row.GetInt64(0), row.GetString(1), row.GetString(2), Decimal(row, 3), Decimal(row, 4), Decimal(row, 5), Decimal(row, 6), Decimal(row, 7), Decimal(row, 8), row.GetInt64(9) == 1))).ToList();
        var merged = local.ToDictionary(x => NormalizeCatalogKey(x.Code), x => x, StringComparer.OrdinalIgnoreCase);

        var imported = await LoadImportedTransportsAsync();
        foreach (var transport in imported)
        {
            var key = NormalizeCatalogKey(transport.Code);
            if (!merged.ContainsKey(key))
                merged[key] = transport;
        }

        foreach (var rule in await _commissionSettings.GetRulesAsync("TRANSPORTE", active: true, date: DateTime.Today))
        {
            var key = NormalizeCatalogKey(rule.Code);
            if (string.IsNullOrWhiteSpace(key)) continue;
            merged[key] = new LocalTransport(
                rule.Id,
                rule.Code,
                rule.Name,
                0m,
                0m,
                rule.CommissionPercent,
                rule.CashRetentionPercent,
                rule.CardRetentionPercent,
                rule.AmexRetentionPercent,
                rule.Active);
        }

        return merged.Values
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    public Task<IReadOnlyList<LocalGuide>> GetGuidesAsync() => ReadAsync("SELECT Id, Clave, Nombre, Telefono, Comision, Estatus FROM LocalGuias ORDER BY Nombre;", row => new LocalGuide(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), Decimal(row, 4), row.GetString(5)));
    public async Task<IReadOnlyList<LocalExpense>> GetExpensesAsync(DateTime? day = null)
    {
        var rows = await ReadAsync("SELECT Id, Fecha, Folio, Concepto, Importe, Notas, Estatus, Usuario FROM LocalGastos ORDER BY Fecha DESC, Id DESC;", row => new LocalExpense(row.GetInt64(0), DateTime.Parse(row.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), row.GetString(2), row.GetString(3), Decimal(row, 4), row.GetString(5), row.GetString(6), row.GetString(7)));
        return day is null
            ? rows
            : rows.Where(x => x.Date.Date == day.Value.Date).ToArray();
    }
    public async Task<IReadOnlyList<LocalAppRecordRow>> GetAppRecordsAsync(string? siteName = null)
    {
        await using var connection = database.Open();
        if (await HasTableAsync(connection, "mkt__dbo__AppMovilRegistro"))
        {
            const string sql = """
                SELECT
                  COALESCE(a.folio_app, ''),
                  COALESCE(a.folio_app_original, ''),
                  COALESCE(a.vendedor_nombre, ''),
                  COALESCE(a.hotel, ''),
                  COALESCE(a.folio_gafete, ''),
                  COALESCE(a.fecha_operacion, a.fecha_creacion, ''),
                  COALESCE(a.tipo_operacion, ''),
                  COALESCE(a.total, 0),
                  COALESCE(
                      NULLIF(a.estado_pago_dejada, ''),
                      NULLIF(a.payout_status, ''),
                      CASE
                        WHEN COALESCE(a.fecha_pago_dejada, '') <> '' OR COALESCE(a.payout_date, '') <> '' THEN 'pagado'
                        ELSE 'pendiente'
                      END
                  ),
                  COALESCE(a.usuario_movil, ''),
                  COALESCE(a.notas, '')
                FROM "mkt__dbo__AppMovilRegistro" a
                WHERE (COALESCE(a.folio_app, '') <> '' OR COALESCE(a.folio_app_original, '') <> '')
                  AND (
                        @site IS NULL
                     OR UPPER(COALESCE(a.sitio, '')) = UPPER(@site)
                     OR (UPPER(@site) = N'PLAZA 28' AND UPPER(COALESCE(a.sitio, '')) = N'TIENDA PLAZA 28')
                     OR (UPPER(@site) = N'TIENDA PLAZA 28' AND UPPER(COALESCE(a.sitio, '')) = N'PLAZA 28')
                  )
                ORDER BY COALESCE(a.fecha_operacion, a.fecha_creacion, '') DESC
                LIMIT 500;
                """;
            return await ReadAsync(sql, row => new LocalAppRecordRow(
                Text(row, 0),
                Text(row, 1),
                Text(row, 2),
                Text(row, 3),
                Text(row, 4),
                Text(row, 5),
                Text(row, 6),
                Decimal(row, 7),
                Text(row, 8),
                Text(row, 9),
                Text(row, 10)),
                ("$site", string.IsNullOrWhiteSpace(siteName) ? null : siteName.Trim()));
        }

        return await ReadAsync("""
            SELECT Folio, Folio, '', '', Gafete, Fecha, '', Importe, 'local', Usuario, Notas
            FROM LocalRegistros
            ORDER BY Fecha DESC, Id DESC
            LIMIT 200;
            """, row => new LocalAppRecordRow(
                row.GetString(0),
                row.GetString(1),
                row.GetString(2),
                row.GetString(3),
                row.GetString(4),
                row.GetString(5),
                row.GetString(6),
                Decimal(row, 7),
                row.GetString(8),
                row.GetString(9),
                row.GetString(10)));
    }
    public async Task<IReadOnlyList<LocalRelation>> GetRelationsAsync(string? search = null, DateTime? start = null, DateTime? end = null, bool includeFinancialDetails = true, string? siteName = null)
    {
        if (_sqlSource is not null)
        {
            try
            {
                return await GetRelationsFromSqlServerAsync(search, start, end, 300, includeFinancialDetails, siteName);
            }
            catch (Exception ex)
            {
                LogPlazaRelationFallback(ex);
                // Conserva el fallback SQLite si la consulta directa no esta disponible.
            }
        }

        await using var connection = database.Open();
        var imported = await GetRelationsFromImportedAppMirrorAsync(connection, search, start, end, siteName);
        if (imported.Count > 0)
            return imported;

        return await ReadAsync("SELECT Id, FolioApp, FolioOperacion, FolioPos, Gafete, Taxista, Vendedor, Dejada, Observaciones FROM LocalRelaciones ORDER BY Id DESC;", row => new LocalRelation(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5), row.GetString(6), row.IsDBNull(7) ? null : Decimal(row, 7), row.GetString(8)));
    }
    public async Task<IReadOnlyList<LocalRelation>> GetReportRelationsAsync(DateTime? start = null, DateTime? end = null, string? siteName = null)
    {
        var sqlResults = new List<LocalRelation>();
        
        if (_sqlSource is not null)
        {
            try
            {
                sqlResults.AddRange(await GetRelationsFromSqlServerAsync(null, start, end, null, true, siteName));
            }
            catch (Exception ex)
            {
                LogPlazaRelationFallback(ex);
                try
                {
                    // El reporte no debe caer al camino general con TOP 300 si falla
                    // el enriquecimiento financiero; primero conserva el rango SQL completo.
                    sqlResults.AddRange(await GetRelationsFromSqlServerAsync(null, start, end, null, false, siteName));
                }
                catch (Exception retryEx)
                {
                    LogPlazaRelationFallback(retryEx);
                    // Mantiene fallback local si SQL Server no esta disponible.
                }
            }
        }

        if (_sqlSource is not null)
        {
            try
            {
                sqlResults.AddRange(await ReadPosOnlySqlServerRelationsAsync(start, end, siteName));
            }
            catch (Exception ex)
            {
                LogPlazaRelationFallback(ex);
            }
        }

        await using var connection = database.Open();
        
        // Siempre buscar POS-only incluso si SQL Server devolvió resultados
        var posOnly = await ReadPosOnlyLocalRelationsAsync(connection, null, start, end, siteName);
        var merged = MergeRelationRows(sqlResults, posOnly);
        if (merged.Count > 0)
            return merged;

        // Fallback: leer desde LocalRelaciones si nada anterior funcionó
        return await ReadAsync("SELECT Id, FolioApp, FolioOperacion, FolioPos, Gafete, Taxista, Vendedor, Dejada, Observaciones FROM LocalRelaciones ORDER BY Id DESC;", row => new LocalRelation(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4), row.GetString(5), row.GetString(6), row.IsDBNull(7) ? null : Decimal(row, 7), row.GetString(8)));
    }

    private async Task<IReadOnlyList<LocalRelation>> GetRelationsFromImportedAppMirrorAsync(SqliteConnection connection, string? search, DateTime? start, DateTime? end, string? siteName)
    {
        if (!await HasTableAsync(connection, "mkt__dbo__AppMovilRegistro"))
            return [];

        await EnsureAppMirrorSchemaAsync(connection);

        var hasRequestedRange = start.HasValue || end.HasValue;
        var limitClause = hasRequestedRange ? string.Empty : "LIMIT 300";
        var sql = $"""
            SELECT
              rowid,
              COALESCE(folio_app, '') AS AppFolio,
              COALESCE(NULLIF(folio_app_original, ''), folio_app, '') AS OperationFolio,
              COALESCE(folio_pos, '') AS PosFolio,
              COALESCE(folio_gafete, '') AS Badge,
              COALESCE(vendedor_nombre, '') AS DriverName,
              '' AS VendorName,
              COALESCE(total, 0) AS Payout,
              COALESCE(notas, '') AS Notes,
              COALESCE(usuario_movil, '') AS SourceUser,
              COALESCE(substr(COALESCE(fecha_operacion, fecha_creacion, ''), 1, 19), '') AS DateText,
              COALESCE(hotel, '') AS Hotel,
              COALESCE(origen, '') AS Origin,
              COALESCE(sitio, '') AS Site,
              COALESCE(destino, '') AS Destination,
              COALESCE(unidad, '') AS Unit,
              COALESCE(placas, '') AS Plates,
              COALESCE(NULLIF(telefono_taxista, ''), NULLIF(telefono_contacto, ''), '') AS Phone,
              COALESCE(nacionalidad, '') AS Nationality,
              COALESCE(tipo_operacion, '') AS TransportType,
              CASE
                WHEN COALESCE(tarjeta, 0) > 0 THEN 'Tarjeta'
                WHEN COALESCE(efectivo, 0) > 0 THEN 'Efectivo'
                WHEN COALESCE(dolares, 0) > 0 THEN 'Dolares'
                ELSE COALESCE(metodo_pago, '')
              END AS PaymentMethod,
              COALESCE(
                  NULLIF(estado_pago_dejada, ''),
                  NULLIF(payout_status, ''),
                  CASE
                    WHEN COALESCE(fecha_pago_dejada, '') <> '' OR COALESCE(payout_date, '') <> '' THEN 'pagado'
                    ELSE 'pendiente'
                  END
              ) AS PayoutStatus,
              COALESCE(ticket_pago_dejada, '') AS PayoutTicket,
              COALESCE(CAST(id_catalogo AS TEXT), '') AS TaxistaId,
              COALESCE(usuario_pago_dejada, '') AS PayoutUser,
              COALESCE(
                  substr(CAST(fecha_pago_dejada AS TEXT), 1, 19),
                  substr(CAST(payout_date AS TEXT), 1, 19),
                  '') AS PayoutDate,
              COALESCE(pago_comision, 0) AS CommissionPaid,
              CASE
                WHEN COALESCE(fecha_pago_dejada, '') <> '' OR UPPER(COALESCE(estado_pago_dejada, payout_status, '')) IN ('PAGADO','PAGADA')
                THEN COALESCE(total, 0)
                ELSE 0
              END AS PayoutPaid,
              COALESCE(pax, 0) AS Passengers,
              COALESCE(adult_count, 0) AS AdultPassengers,
              COALESCE(youth_count, 0) AS YouthPassengers,
              COALESCE(minor_count, 0) AS ChildPassengers,
              COALESCE(no_show_count, 0) AS NoShowCount,
              COALESCE(total, 0) AS CashAmount,
              COALESCE(tarjeta, 0) AS CardAmount,
              COALESCE(dolares, 0) AS DollarsAmount
            FROM "mkt__dbo__AppMovilRegistro"
            WHERE COALESCE(folio_app, '') <> ''
              AND ($start IS NULL OR substr(COALESCE(fecha_operacion, fecha_creacion, ''), 1, 10) >= $start)
              AND ($end IS NULL OR substr(COALESCE(fecha_operacion, fecha_creacion, ''), 1, 10) <= $end)
              AND (
                    $site IS NULL
                 OR UPPER(COALESCE(sitio, '')) = UPPER($site)
                 OR (UPPER($site) = 'PLAZA 28' AND UPPER(COALESCE(sitio, '')) = 'TIENDA PLAZA 28')
                 OR (UPPER($site) = 'TIENDA PLAZA 28' AND UPPER(COALESCE(sitio, '')) = 'PLAZA 28')
              )
              AND (
                    $q IS NULL
                 OR COALESCE(folio_app, '') LIKE '%' || $q || '%'
                 OR COALESCE(folio_app_original, '') LIKE '%' || $q || '%'
                 OR COALESCE(folio_pos, '') LIKE '%' || $q || '%'
                 OR COALESCE(hotel, '') LIKE '%' || $q || '%'
                 OR COALESCE(origen, '') LIKE '%' || $q || '%'
                 OR COALESCE(sitio, '') LIKE '%' || $q || '%'
                 OR COALESCE(vendedor_nombre, '') LIKE '%' || $q || '%'
                 OR COALESCE(folio_gafete, '') LIKE '%' || $q || '%'
                 OR COALESCE(nacionalidad, '') LIKE '%' || $q || '%'
                 OR COALESCE(tipo_operacion, '') LIKE '%' || $q || '%'
                 OR COALESCE(CAST(id_catalogo AS TEXT), '') LIKE '%' || $q || '%'
                 OR COALESCE(notas, '') LIKE '%' || $q || '%'
              )
            ORDER BY COALESCE(fecha_operacion, fecha_creacion, '') DESC
            {limitClause};
            """;

        var appRows = await ReadAsync(sql, row =>
        {
            var paymentMethod = Text(row, 20);
            var currency = BuildRelationCurrency(paymentMethod);
            var payoutStatus = Text(row, 21);
            var commissionPaid = Decimal(row, 26);
            var cash = Decimal(row, 33);
            var card = Decimal(row, 34);
            var dollars = Decimal(row, 35);
            return new LocalRelation(
                row.GetInt64(0),
                Text(row, 1),
                Text(row, 2),
                Text(row, 3),
                Text(row, 4),
                Text(row, 5),
                Text(row, 6),
                Decimal(row, 7),
                Text(row, 8),
                "APP MOVIL",
                Text(row, 9),
                Text(row, 10),
                Text(row, 11),
                Text(row, 12),
                Text(row, 13),
                Text(row, 14),
                Text(row, 15),
                Text(row, 16),
                Text(row, 17),
                Text(row, 18),
                Text(row, 19),
                0m,
                0m,
                commissionPaid,
                paymentMethod,
                payoutStatus,
                string.Empty,
                Text(row, 22),
                Text(row, 23),
                Text(row, 24),
                Text(row, 25),
                Decimal(row, 27),
                Convert.ToInt32(row.GetValue(28), CultureInfo.InvariantCulture),
                string.Empty,
                currency,
                paymentMethod,
                string.Empty,
                Decimal(row, 7),
                cash,
                card,
                dollars,
                0m,
                string.Empty,
                Convert.ToInt32(row.GetValue(29), CultureInfo.InvariantCulture),
                Convert.ToInt32(row.GetValue(30), CultureInfo.InvariantCulture),
                Convert.ToInt32(row.GetValue(31), CultureInfo.InvariantCulture),
                row.IsDBNull(32) ? 0 : Convert.ToInt32(row.GetValue(32), CultureInfo.InvariantCulture));
        },
            ("$start", start?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$end", end?.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$site", string.IsNullOrWhiteSpace(siteName) ? null : siteName.Trim()),
            ("$q", string.IsNullOrWhiteSpace(search) ? null : search.Trim()));

        var posOnly = await ReadPosOnlyLocalRelationsAsync(connection, search, start, end, siteName);
        return MergeRelationRows(appRows, posOnly);
    }

    private async Task<IReadOnlyList<LocalRelation>> ReadPosOnlySqlServerRelationsAsync(DateTime? start, DateTime? end, string? siteName)
    {
        if (_sqlSource is null)
            return [];

        var rows = new List<LocalRelation>();
        await using (var compuadmo = await _sqlSource.OpenCompuadmoAsync())
        {
            rows.AddRange(await ReadPosOnlySqlServerRelationsFromTableAsync(
                compuadmo,
                _sqlSource.CompuadmoTable("remisioM"),
                "folioregistro",
                "folio_remision",
                start,
                end,
                siteName,
                "POS-SQL"));
        }

        await using (var joyeria = await _sqlSource.OpenJoyeriaAsync())
        {
            rows.AddRange(await ReadPosOnlySqlServerRelationsFromTableAsync(
                joyeria,
                _sqlSource.JoyeriaTable("remisioM"),
                "folio_registro",
                "COALESCE(folio_pedido, folio_factura)",
                start,
                end,
                siteName,
                "POS-SQL"));
        }

        return MergeRelationRows([], rows);
    }

    private static async Task<IReadOnlyList<LocalRelation>> ReadPosOnlySqlServerRelationsFromTableAsync(
        SqlConnection connection,
        string table,
        string folioColumn,
        string ticketColumn,
        DateTime? start,
        DateTime? end,
        string? siteName,
        string source)
    {
        var columns = await GetSqlServerColumnsAsync(connection, table);
        var driverExpression = ExistingSqlTextExpression(columns, "nombrevendedor");
        var staffExpression = ExistingSqlTextExpression(columns, "nombrestaff", "vendedor", "usuario", "cajero");
        var unitExpression = ExistingSqlTextExpression(columns, "unidad");
        var badgeExpression = ExistingSqlTextExpression(columns, "gafete");
        var hotelExpression = ExistingSqlTextExpression(columns, "hotel", "nombrealmacen", "almacen", "cliente");
        var notesExpression = ExistingSqlTextExpression(columns, "observaciones");
        var statusExpression = ExistingSqlTextExpression(columns, "estatus");
        var siteExpressions = new[] { "nombrealmacen", "hotel", "almacen" }
            .Where(columns.Contains)
            .Select(column => $"UPPER(COALESCE(CONVERT(nvarchar(max), [{column.Replace("]", "]]", StringComparison.Ordinal)}]), '')) = UPPER(@site)")
            .ToArray();

        var whereParts = new List<string>
        {
            $"(NULLIF(LTRIM(RTRIM(CAST({folioColumn} AS nvarchar(80)))), '') IS NOT NULL OR NULLIF(LTRIM(RTRIM(CAST({ticketColumn} AS nvarchar(80)))), '') IS NOT NULL)",
            $"UPPER({statusExpression}) NOT IN (N'C', N'CANCELADO', N'CANCELADA')"
        };
        if (start.HasValue)
            whereParts.Add("CONVERT(date, fecha) >= @start");
        if (end.HasValue)
            whereParts.Add("CONVERT(date, fecha) <= @end");
        if (!string.IsNullOrWhiteSpace(siteName) && siteExpressions.Length > 0)
            whereParts.Add("(" + string.Join(" OR ", siteExpressions) + ")");

        var sql = $"""
            SELECT
              CAST({folioColumn} AS nvarchar(80)) AS OperationFolio,
              CAST({ticketColumn} AS nvarchar(80)) AS Ticket,
              CAST(COALESCE(total, 0) AS decimal(18,2)) AS Total,
              COALESCE(CONVERT(nvarchar(30), fecha, 126), CONVERT(nvarchar(max), fecha), '') AS Fecha,
              {driverExpression} AS Driver,
              {staffExpression} AS Staff,
              {unitExpression} AS Unit,
              {badgeExpression} AS Badge,
              {hotelExpression} AS Hotel,
              {notesExpression} AS Notes
            FROM {table}
            WHERE {string.Join(" AND ", whereParts)};
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;
        if (start.HasValue)
            command.Parameters.AddWithValue("@start", start.Value.Date);
        if (end.HasValue)
            command.Parameters.AddWithValue("@end", end.Value.Date);
        if (!string.IsNullOrWhiteSpace(siteName) && siteExpressions.Length > 0)
            command.Parameters.AddWithValue("@site", siteName.Trim());

        var rows = new List<LocalRelation>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var operationFolio = SqlText(reader, 0);
            var ticket = SqlText(reader, 1);
            if (string.IsNullOrWhiteSpace(ticket) && string.IsNullOrWhiteSpace(operationFolio))
                continue;

            rows.Add(new LocalRelation(
                0,
                string.Empty,
                operationFolio,
                string.IsNullOrWhiteSpace(ticket) ? operationFolio : ticket,
                SqlText(reader, 7),
                SqlText(reader, 4),
                SqlText(reader, 5),
                SqlDecimal(reader, 2),
                SqlText(reader, 9),
                source,
                string.Empty,
                SqlText(reader, 3),
                SqlText(reader, 8),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                SqlText(reader, 6),
                string.Empty,
                string.Empty,
                string.Empty,
                0m,
                0m,
                0m,
                string.Empty,
                "POS",
                source,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0m,
                0,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0m,
                0m,
                0m,
                0m,
                0m,
                string.Empty,
                0,
                0,
                0,
                0));
        }

        return rows;
    }

    private async Task<IReadOnlyList<LocalRelation>> ReadPosOnlyLocalRelationsAsync(SqliteConnection connection, string? search, DateTime? start, DateTime? end, string? siteName)
    {
        var tables = new[]
        {
            (Table: "compuadmo__dbo__remisioM", FolioColumn: "folioregistro", TicketColumn: "folio_remision", Source: "POS-ONLY"),
            (Table: "joyeria__dbo__remisioM", FolioColumn: "folio_registro", TicketColumn: "COALESCE(folio_pedido, folio_factura)", Source: "POS-ONLY")
        };

        var rows = new List<LocalRelation>();
        foreach (var table in tables)
        {
            if (!await HasTableAsync(connection, table.Table))
                continue;

            var columns = await GetSqliteColumnsAsync(connection, table.Table);
            var driverExpression = ExistingTextExpression(columns, "nombrevendedor");
            var staffExpression = ExistingTextExpression(columns, "nombrestaff", "vendedor", "usuario", "cajero");
            var unitExpression = ExistingTextExpression(columns, "unidad");
            var badgeExpression = ExistingTextExpression(columns, "gafete");
            var hotelExpression = ExistingTextExpression(columns, "hotel", "nombrealmacen", "almacen", "cliente");
            var notesExpression = ExistingTextExpression(columns, "observaciones");
            var statusExpression = ExistingTextExpression(columns, "estatus");
            var siteExpressions = new[] { "nombrealmacen", "hotel", "almacen" }
                .Where(columns.Contains)
                .Select(column => $"UPPER(COALESCE(\"{EscapeSqliteIdentifier(column)}\", '')) = UPPER($site)")
                .ToArray();

            var whereParts = new List<string>();
            var parameters = new List<(string Name, object? Value)>();
            var hasRange = start.HasValue || end.HasValue;
            if (start.HasValue)
            {
                parameters.Add(("$start", start.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                whereParts.Add("substr(COALESCE(fecha, ''), 1, 10) >= $start");
            }
            if (end.HasValue)
            {
                parameters.Add(("$end", end.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                whereParts.Add("substr(COALESCE(fecha, ''), 1, 10) <= $end");
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                parameters.Add(("$q", search.Trim()));
                whereParts.Add("(CAST(" + table.FolioColumn + " AS TEXT) LIKE '%' || $q || '%' OR CAST(" + table.TicketColumn + " AS TEXT) LIKE '%' || $q || '%')");
            }
            if (!string.IsNullOrWhiteSpace(siteName))
            {
                parameters.Add(("$site", siteName.Trim()));
                if (siteExpressions.Length > 0)
                {
                    whereParts.Add("(" + string.Join(" OR ", siteExpressions) + ")");
                }
            }
            if (whereParts.Count == 0)
            {
                whereParts.Add("1 = 1");
            }

            var sql = $"""
                SELECT
                  CAST({table.FolioColumn} AS TEXT) AS OperationFolio,
                  CAST({table.TicketColumn} AS TEXT) AS Ticket,
                  COALESCE(total, 0) AS Total,
                  COALESCE(fecha, '') AS Fecha,
                  {driverExpression} AS Driver,
                  {staffExpression} AS Staff,
                  {unitExpression} AS Unit,
                  {badgeExpression} AS Badge,
                  {hotelExpression} AS Hotel,
                  {notesExpression} AS Notes,
                  {statusExpression} AS Estatus
                FROM "{table.Table}"
                WHERE {string.Join(" AND ", whereParts)}
                  AND UPPER({statusExpression}) NOT IN ('C', 'CANCELADO', 'CANCELADA');
                """;

            var candidates = await ReadAsync(sql, row =>
            {
                var operationFolio = Text(row, 0);
                var ticket = Text(row, 1);
                if (string.IsNullOrWhiteSpace(ticket) && string.IsNullOrWhiteSpace(operationFolio))
                    return null;
                var ticketValue = string.IsNullOrWhiteSpace(ticket) ? operationFolio : ticket;
                return new LocalRelation(
                    0,
                    string.Empty,
                    operationFolio,
                    ticketValue,
                    Text(row, 7),
                    Text(row, 4),
                    Text(row, 5),
                    Decimal(row, 2),
                    Text(row, 9),
                    table.Source,
                    string.Empty,
                    Text(row, 3),
                    Text(row, 8),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Text(row, 6),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0m,
                    0m,
                    0m,
                    string.Empty,
                    "POS",
                    "POS-ONLY",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0m,
                    0,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    0m,
                    0m,
                    0m,
                    0m,
                    0m,
                    string.Empty,
                    0,
                    0,
                    0,
                    0);
            }, parameters.ToArray());

            rows.AddRange(candidates.Where(x => x is not null).Cast<LocalRelation>());
        }

        return MergeRelationRows([], rows);
    }

    private static IReadOnlyList<LocalRelation> MergeRelationRows(IReadOnlyList<LocalRelation> primary, IReadOnlyList<LocalRelation> fallback)
    {
        var merged = new List<LocalRelation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in primary.Concat(fallback))
        {
            var keyParts = new[]
            {
                NormalizeRelationTicket(row.PosFolio),
                NormalizeRelationTicket(row.OperationFolio),
                NormalizeRelationTicket(row.AppFolio)
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var key = keyParts.Length > 0 ? keyParts[0] : string.Join("|", row.DateText, row.Driver, row.Hotel, row.Unit);
            if (string.IsNullOrWhiteSpace(key) || seen.Contains(key))
                continue;
            seen.Add(key);
            merged.Add(row);
        }
        return merged;
    }

    private static string NormalizeRelationTicket(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        foreach (var separator in new[] { ',', ';', '/', '|', ' ' })
            text = text.Replace(separator, ' ');
        return string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static void LogPlazaRelationFallback(Exception ex)
    {
        try
        {
            var logDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(logDir, "plaza-relation-fallback.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
        }
        catch
        {
            // El log nunca debe romper la operacion del usuario.
        }
    }

    private static void LogPlazaRelationEnrichmentFallback(Exception ex, SqlRelationRow row)
    {
        try
        {
            var logDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(logDir, "plaza-relation-fallback.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Enrichment folio={row.AppFolio}/{row.OperationFolio}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
        }
        catch
        {
            // El log nunca debe romper la operacion del usuario.
        }
    }

    public async Task<LocalRelation> EnrichRelationAsync(LocalRelation relation)
    {
        if (_sqlSource is null) return relation;

        try
        {
            await using var posConnection = await _sqlSource.OpenPosAsync();
            await using var compuConnection = await _sqlSource.OpenCompuadmoAsync();
            await using var joyeriaConnection = await _sqlSource.OpenJoyeriaAsync();
            var row = MapSqlRelationRow(relation);
            var transportCatalog = await LoadImportedTransportsAsync();
            return await BuildEnrichedRelationAsync(row, compuConnection, joyeriaConnection, transportCatalog);
        }
        catch
        {
            return relation;
        }
    }

    /// <summary>
    /// Paga la dejada de un viaje directamente en SQL Server (mkt.AppMovilRegistro):
    /// marca estado 'pagado', genera el ticket TK-{folio}-{fecha} y registra usuario/fecha.
    /// Devuelve el ticket generado y el importe pagado para mostrar confirmación e imprimir.
    /// </summary>
    public async Task<(string Ticket, decimal Amount)> PayPayoutAsync(string appFolio, string operationFolio, string user)
    {
        if (_sqlSource is null)
            throw new InvalidOperationException("No hay conexión a SQL Server para registrar el pago de la dejada.");
        var folio = (!string.IsNullOrWhiteSpace(appFolio) ? appFolio : operationFolio)?.Trim();
        if (string.IsNullOrWhiteSpace(folio))
            throw new ArgumentException("No se pudo identificar el folio del viaje.");

        await using var connection = await _sqlSource.OpenPosAsync();

        decimal amount;
        string currentStatus;
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = $"""
                SELECT TOP 1
                  COALESCE(total, 0),
                  COALESCE(
                      NULLIF(estado_pago_dejada, ''),
                      NULLIF(payout_status, ''),
                      CASE
                        WHEN COALESCE(fecha_pago_dejada, '') <> '' OR COALESCE(payout_date, '') <> '' THEN 'pagado'
                        ELSE 'pendiente'
                      END
                  )
                FROM {_sqlSource.PosTable("AppMovilRegistro")}
                WHERE folio_app = @f OR folio_app_original = @f;
                """;
            read.Parameters.AddWithValue("@f", folio);
            await using var reader = await read.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException($"No se encontró el viaje {folio} para pagar la dejada.");
            amount = Convert.ToDecimal(reader.GetValue(0), CultureInfo.InvariantCulture);
            currentStatus = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        if (string.Equals(currentStatus, "pagado", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStatus, "pagada", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La dejada de este viaje ya está pagada.");

        var ticket = "TK-" + folio + "-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = $"UPDATE {_sqlSource.PosTable("AppMovilRegistro")} SET estado_pago_dejada = 'pagado', payout_status = 'pagado', ticket_pago_dejada = @t, payout_ticket = @t, fecha_pago_dejada = SYSDATETIME(), payout_date = SYSDATETIME(), usuario_pago_dejada = @u, payout_user = @u WHERE folio_app = @f OR folio_app_original = @f;";
            update.Parameters.AddWithValue("@t", ticket);
            update.Parameters.AddWithValue("@u", string.IsNullOrWhiteSpace(user) ? "desktop" : user.Trim());
            update.Parameters.AddWithValue("@f", folio);
            await update.ExecuteNonQueryAsync();
        }

        await using (var updatePayoutRows = connection.CreateCommand())
        {
            updatePayoutRows.CommandText = $"""
                UPDATE {_sqlSource.PosTable("dejadas")}
                SET pago = total,
                    fechapago = SYSDATETIME(),
                    nombrecajero = @u
                WHERE folioregistrostr = @f
                   OR CONVERT(nvarchar(60), folioregistro) = @folioNumber;
                """;
            updatePayoutRows.Parameters.AddWithValue("@u", string.IsNullOrWhiteSpace(user) ? "desktop" : user.Trim());
            updatePayoutRows.Parameters.AddWithValue("@f", folio);
            updatePayoutRows.Parameters.AddWithValue("@folioNumber", ParseLongOrZero(folio).ToString(CultureInfo.InvariantCulture));
            await updatePayoutRows.ExecuteNonQueryAsync();
        }

        return (ticket, amount);
    }

    public async Task<IReadOnlyList<LocalOperationsPreviewRow>> GetOperationsReportPreviewAsync(DateTime? start = null, DateTime? end = null, string? siteName = null)
    {
        var relations = await GetReportRelationsAsync(start, end, siteName);
        var normalizedSite = string.IsNullOrWhiteSpace(siteName) ? string.Empty : siteName.Trim().ToUpperInvariant();
        var preferPayout = normalizedSite == "PLAZA 28" || normalizedSite == "TIENDA PLAZA 28";

        var rows = relations.Select(x =>
        {
            var importe = preferPayout ? (x.Payout ?? 0m) : (x.Sale > 0m ? x.Sale : x.Payout ?? 0m);
            return new LocalOperationsPreviewRow(
                string.IsNullOrWhiteSpace(x.Driver) ? x.Vendor : x.Driver,
                x.DateText,
                x.Passengers,
                x.Hotel,
                x.Payout ?? 0m,
                importe,
                x.Commission,
                x.CommissionPaid,
                x.OperationFolio,
                x.AppFolio,
                x.Badge,
                x.PayoutStatus,
                x.PayoutDate,
                x.PayoutUser,
                x.PayoutTicket,
                x.Site,
                x.Origin,
                x.Destination,
                x.Unit,
                x.Plates,
                x.TransportType,
                x.Notes,
                x.Vendor,
                x.AdultPassengers,
                x.YouthPassengers,
                x.ChildPassengers);
        }).ToArray();

        return ConsolidateOperationsReportRows(rows);
    }

    private static IReadOnlyList<LocalOperationsPreviewRow> ConsolidateOperationsReportRows(IReadOnlyList<LocalOperationsPreviewRow> rows)
    {
        if (rows.Count <= 1)
            return rows;

        return rows
            .GroupBy(GetOperationsReportKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToArray();
                var first = items[0];
                if (items.Length == 1)
                    return first;

                return first with
                {
                    Taxista = FirstNonEmpty(items.Select(x => x.Taxista)),
                    Fecha = FirstNonEmpty(items.Select(x => x.Fecha)),
                    Pax = first.Pax,
                    Hotel = FirstNonEmpty(items.Select(x => x.Hotel)),
                    Dejada = items.Max(x => x.Dejada),
                    Importe = items.Max(x => x.Importe),
                    Comision = items.Max(x => x.Comision),
                    Pago = items.Max(x => x.Pago),
                    FolioOriginal = FirstNonEmpty(items.Select(x => x.FolioOriginal)),
                    FolioLocal = FirstNonEmpty(items.Select(x => x.FolioLocal)),
                    Gafete = JoinDistinctBadges(items.Select(x => x.Gafete)),
                    Estatus = ResolveReportStatus(items.Select(x => x.Estatus)),
                    FechaPago = FirstNonEmpty(items.Select(x => x.FechaPago)),
                    UsuarioPago = FirstNonEmpty(items.Select(x => x.UsuarioPago)),
                    TicketPago = FirstNonEmpty(items.Select(x => x.TicketPago)),
                    Sitio = FirstNonEmpty(items.Select(x => x.Sitio)),
                    Origen = FirstNonEmpty(items.Select(x => x.Origen)),
                    Destino = FirstNonEmpty(items.Select(x => x.Destino)),
                    Unidad = FirstNonEmpty(items.Select(x => x.Unidad)),
                    Placas = FirstNonEmpty(items.Select(x => x.Placas)),
                    TipoServicio = FirstNonEmpty(items.Select(x => x.TipoServicio)),
                    Notas = FirstNonEmpty(items.Select(x => x.Notas)),
                    Vendedor = FirstNonEmpty(items.Select(x => x.Vendedor)),
                    Adulto = items.Max(x => x.Adulto),
                    Joven = items.Max(x => x.Joven),
                    Nino = items.Max(x => x.Nino)
                };
            })
            .ToArray();
    }

    private static string GetOperationsReportKey(LocalOperationsPreviewRow row)
    {
        var operationFolio = NormalizeOperationsReportKey(row.FolioOriginal);
        if (!string.IsNullOrWhiteSpace(operationFolio))
            return "op:" + operationFolio;

        var appFolio = NormalizeOperationsReportKey(row.FolioLocal);
        if (!string.IsNullOrWhiteSpace(appFolio))
            return "app:" + appFolio;

        var ticket = NormalizeOperationsReportText(row.TicketPago);
        if (!string.IsNullOrWhiteSpace(ticket))
            return "ticket:" + ticket;

        return string.Join("|",
            "fallback",
            NormalizeOperationsReportText(row.Fecha),
            NormalizeOperationsReportText(row.Taxista),
            NormalizeOperationsReportText(row.Unidad),
            NormalizeOperationsReportText(row.Hotel));
    }

    private static string NormalizeOperationsReportKey(string? value)
    {
        var text = NormalizeOperationsReportText(value);
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) && numeric > 0
            ? numeric.ToString(CultureInfo.InvariantCulture)
            : text;
    }

    private static string NormalizeOperationsReportText(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string FirstNonEmpty(IEnumerable<string?> values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string ResolveReportStatus(IEnumerable<string?> values)
    {
        var statuses = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .ToArray();
        if (statuses.Length == 0)
            return string.Empty;

        return statuses.FirstOrDefault(status =>
            status.Equals("pagado", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("pagada", StringComparison.OrdinalIgnoreCase)) ?? statuses[0];
    }

    private static string JoinDistinctBadges(IEnumerable<string?> values)
    {
        var badges = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join(", ", badges);
    }

    public async Task<IReadOnlyList<LocalCommissionPaymentPreviewRow>> GetCommissionPaymentsReportAsync(DateTime? start = null, DateTime? end = null, string? siteName = null)
    {
        if (_sqlSource is null) return [];

        await using var connection = await _sqlSource.OpenPosAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                COALESCE(NULLIF(rel.FolioOperacion, ''), NULLIF(a.folio_app_original, ''), COALESCE(a.folio_app, '')) AS Folio,
                COALESCE(CONVERT(nvarchar(30), a.fecha_pago_comision, 120), '') AS FechaPago,
                COALESCE(a.vendedor_nombre, '') AS Taxista,
                COALESCE(a.tipo_operacion, '') AS Transporte,
                COALESCE(a.comision_calculada, 0) AS Comision,
                COALESCE(a.pago_comision, 0) AS Pago,
                CASE
                    WHEN COALESCE(a.pago_comision, 0) >= COALESCE(a.comision_calculada, 0) AND COALESCE(a.comision_calculada, 0) > 0 THEN 'PAGADA'
                    WHEN COALESCE(a.pago_comision, 0) > 0 THEN 'PARCIAL'
                    ELSE 'PENDIENTE'
                END AS Estatus
            FROM {_sqlSource.PosTable("AppMovilRegistro")} a
            OUTER APPLY (
                SELECT TOP (1) r.FolioOperacion
                FROM {_sqlSource.PosTable("RelacionTicketTaxista")} r
                WHERE r.FolioApp = a.folio_app
                   OR r.FolioApp = a.folio_app_original
                   OR r.FolioOperacion = a.folio_app
                   OR r.FolioOperacion = a.folio_app_original
                ORDER BY r.Id
            ) rel
            WHERE COALESCE(a.pago_comision, 0) > 0
              AND (@inicio IS NULL OR a.fecha_pago_comision >= @inicio)
              AND (@fin IS NULL OR a.fecha_pago_comision < DATEADD(day, 1, @fin))
              AND (
                    @site IS NULL
                 OR UPPER(COALESCE(a.sitio, '')) = UPPER(@site)
                 OR (UPPER(@site) = N'PLAZA 28' AND UPPER(COALESCE(a.sitio, '')) = N'TIENDA PLAZA 28')
                 OR (UPPER(@site) = N'TIENDA PLAZA 28' AND UPPER(COALESCE(a.sitio, '')) = N'PLAZA 28')
              )
            ORDER BY a.fecha_pago_comision DESC, a.fecha_operacion DESC;
            """;
        command.Parameters.AddWithValue("@inicio", start?.Date ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@fin", end?.Date ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@site", string.IsNullOrWhiteSpace(siteName) ? (object)DBNull.Value : siteName.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<LocalCommissionPaymentPreviewRow>();
        while (await reader.ReadAsync())
        {
            rows.Add(new LocalCommissionPaymentPreviewRow(
                reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? string.Empty : reader.GetString(6)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<LocalRelation>> GetRelationsFromSqlServerAsync(string? search, DateTime? start, DateTime? end, int? maxRows, bool includeFinancialDetails, string? siteName)
    {
        if (_sqlSource is null) return [];
        var sqlSource = _sqlSource;

        await using var posConnection = await sqlSource.OpenPosAsync();
        var rows = await ReadRelationRowsAsync(sqlSource, posConnection, search, start, end, maxRows, siteName);
        if (rows.Count > 0)
        {
            var first = rows.FirstOrDefault();
            if (first is not null)
            {
                // Intentionally left blank to preserve original behavior without temporary debug tracing.
            }
        }
        if (!includeFinancialDetails)
            return rows.Select(MapBasicRelation).ToArray();

        SqlConnection? compuConnection = null;
        SqlConnection? joyeriaConnection = null;
        try
        {
            compuConnection = await sqlSource.OpenCompuadmoAsync();
            joyeriaConnection = await sqlSource.OpenJoyeriaAsync();
        }
        catch (Exception ex)
        {
            LogPlazaRelationFallback(ex);
            return rows.Select(MapBasicRelation).ToArray();
        }

        try
        {
            var transportCatalog = await LoadImportedTransportsAsync();
            return await BuildEnrichedRelationsBatchAsync(rows, compuConnection, joyeriaConnection, transportCatalog);
        }
        finally
        {
            await compuConnection.DisposeAsync();
            await joyeriaConnection.DisposeAsync();
        }
    }

    private async Task<LocalRelation> BuildEnrichedRelationAsync(SqlRelationRow row, SqlConnection compuConnection, SqlConnection joyeriaConnection, IReadOnlyList<LocalTransport> transportCatalog)
    {
        var tickets = await LoadRelationStoreTicketsAsync(compuConnection, joyeriaConnection, row.OperationFolio, row.AppFolio, row.PosFolio);
        var commissionableTickets = tickets.Where(x => !string.IsNullOrWhiteSpace(x.Ticket)).ToList();
        var posFolio = BuildRelationPosFolio(row.PosFolio, commissionableTickets);
        var ticketNumbers = commissionableTickets.Select(x => x.Ticket).ToArray();
        var payments = await LoadRelationPaymentsAsync(compuConnection, joyeriaConnection, ticketNumbers);
        var expenses = await LoadRelationExpensesAsync(compuConnection, joyeriaConnection, ticketNumbers);
        return BuildEnrichedRelation(row, commissionableTickets, posFolio, payments, expenses, ResolveRelationTransport(row.TransportType, transportCatalog));
    }

    private async Task<IReadOnlyList<LocalRelation>> BuildEnrichedRelationsBatchAsync(IReadOnlyList<SqlRelationRow> rows, SqlConnection compuConnection, SqlConnection joyeriaConnection, IReadOnlyList<LocalTransport> transportCatalog)
    {
        if (rows.Count == 0) return [];

        Dictionary<int, List<(string Ticket, decimal Total)>> ticketsByRow;
        try
        {
            ticketsByRow = await LoadRelationStoreTicketsBatchAsync(compuConnection, joyeriaConnection, rows);
        }
        catch (Exception ex)
        {
            LogPlazaRelationFallback(ex);
            return rows.Select(MapBasicRelation).ToArray();
        }

        var allTickets = ticketsByRow.Values
            .SelectMany(x => x)
            .Where(x => !string.IsNullOrWhiteSpace(x.Ticket))
            .Select(x => x.Ticket)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyDictionary<string, RelationPaymentBreakdown> payments;
        IReadOnlyDictionary<string, RelationExpenseBreakdown> expenses;
        try
        {
            payments = await LoadRelationPaymentsAsync(compuConnection, joyeriaConnection, allTickets);
            expenses = await LoadRelationExpensesAsync(compuConnection, joyeriaConnection, allTickets);
        }
        catch (Exception ex)
        {
            LogPlazaRelationFallback(ex);
            return rows.Select(MapBasicRelation).ToArray();
        }

        var result = new List<LocalRelation>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            try
            {
                ticketsByRow.TryGetValue(i, out var tickets);
                tickets ??= [];
                var commissionableTickets = tickets
                    .Where(x => !string.IsNullOrWhiteSpace(x.Ticket))
                    .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderByDescending(x => x.Total).First())
                    .ToList();
                var posFolio = BuildRelationPosFolio(row.PosFolio, commissionableTickets);
                result.Add(BuildEnrichedRelation(row, commissionableTickets, posFolio, payments, expenses, ResolveRelationTransport(row.TransportType, transportCatalog)));
            }
            catch (Exception ex)
            {
                LogPlazaRelationEnrichmentFallback(ex, row);
                result.Add(MapBasicRelation(row));
            }
        }

        return result;
    }

    private static LocalRelation BuildEnrichedRelation(SqlRelationRow row, IReadOnlyList<(string Ticket, decimal Total)> commissionableTickets, string posFolio, IReadOnlyDictionary<string, RelationPaymentBreakdown> payments, IReadOnlyDictionary<string, RelationExpenseBreakdown> expenses, LocalTransport? transportInfo)
    {
        var sale = commissionableTickets.Sum(x => x.Total);
        var ticketNumbers = commissionableTickets.Select(x => x.Ticket).ToArray();
        var commission = CalculateRelationCommission(row.TransportType, commissionableTickets, payments, expenses, row.Payout, transportInfo);
        var commissionPaid = row.CommissionPaid;
        var commissionStatus = ResolveCommissionStatus(commission, commissionPaid);
        var payoutStatus = row.PayoutStatus;
        var paymentMethod = BuildRelationPaymentMethod(row, posFolio, ticketNumbers, payments);
        var currency = BuildRelationCurrency(row.PaymentMethod);
        var saleDetail = BuildRelationSaleDetail(commissionableTickets);

        return new LocalRelation(
            row.Id,
            row.AppFolio,
            row.OperationFolio,
            posFolio,
            row.Badge,
            row.Driver,
            row.Vendor,
            row.Payout,
            row.Notes,
            row.Source,
            row.SourceUser,
            row.DateText,
            row.Hotel,
            row.Origin,
            row.Site,
            row.Destination,
            row.Unit,
            row.Plates,
            row.Phone,
            row.Nationality,
            row.TransportType,
            sale,
            commission,
            commissionPaid,
            paymentMethod,
            payoutStatus,
            commissionStatus,
            row.PayoutTicket,
            row.TaxistaId,
            row.PayoutUser,
            row.PayoutDate,
            row.PayoutPaid,
            row.Passengers,
            saleDetail,
            currency,
            AdultPassengers: row.AdultPassengers,
            YouthPassengers: row.YouthPassengers,
            ChildPassengers: row.ChildPassengers,
            NoShowCount: row.NoShowCount);
    }

    private static string BuildRelationPosFolio(string? currentPosFolio, IReadOnlyList<(string Ticket, decimal Total)> tickets)
    {
        var values = SplitRelationTokens(currentPosFolio)
            .Concat(tickets.Select(x => x.Ticket))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? string.Empty : string.Join(", ", values);
    }

    private static LocalRelation MapBasicRelation(SqlRelationRow row) =>
        new(
            row.Id,
            row.AppFolio,
            row.OperationFolio,
            row.PosFolio,
            row.Badge,
            row.Driver,
            row.Vendor,
            row.Payout,
            row.Notes,
            row.Source,
            row.SourceUser,
            row.DateText,
            row.Hotel,
            row.Origin,
            row.Site,
            row.Destination,
            row.Unit,
            row.Plates,
            row.Phone,
            row.Nationality,
            row.TransportType,
            0m,
            0m,
            row.CommissionPaid,
            row.PaymentMethod,
            row.PayoutStatus,
            string.Empty,
            row.PayoutTicket,
            row.TaxistaId,
            row.PayoutUser,
            row.PayoutDate,
            row.PayoutPaid,
            row.Passengers,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0m,
            0m,
            0m,
            0m,
            0m,
            string.Empty,
            row.AdultPassengers,
            row.YouthPassengers,
            row.ChildPassengers,
            row.NoShowCount);

    private static SqlRelationRow MapSqlRelationRow(LocalRelation relation) =>
        new(
            relation.Id,
            relation.AppFolio,
            relation.OperationFolio,
            relation.PosFolio,
            relation.Badge,
            relation.Driver,
            relation.Vendor,
            relation.Payout ?? 0m,
            relation.Notes,
            relation.Source,
            relation.SourceUser,
            relation.DateText,
            relation.Hotel,
            relation.Origin,
            relation.Site,
            relation.Destination,
            relation.Unit,
            relation.Plates,
            relation.Phone,
            relation.Nationality,
            relation.TransportType,
            relation.PaymentMethod,
            relation.PayoutStatus,
            relation.PayoutTicket,
            relation.TaxistaId,
            relation.PayoutUser,
            relation.PayoutDate,
            relation.CommissionPaid,
            relation.PayoutPaid,
            relation.Passengers,
            relation.AdultPassengers,
            relation.YouthPassengers,
            relation.ChildPassengers,
            relation.NoShowCount);

    private static string BuildRelationSaleDetail(IReadOnlyList<(string Ticket, decimal Total)> tickets)
    {
        if (tickets.Count == 0) return string.Empty;
        return string.Join(Environment.NewLine, tickets
            .OrderByDescending(x => x.Total)
            .ThenBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Ticket} {x.Total:C2}"));
    }

    private static string BuildRelationPaymentMethod(SqlRelationRow row, string? posFolio, IReadOnlyList<string> actualTickets, IReadOnlyDictionary<string, RelationPaymentBreakdown> payments)
    {
        var methods = new List<string>();
        foreach (var ticket in SplitRelationTokens(posFolio)
                     .Concat(actualTickets)
                     .Concat(SplitRelationTokens(row.PayoutTicket))
                     .Select(NormalizeRelationTicket)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (payments.TryGetValue(ticket, out var breakdown) && !string.IsNullOrWhiteSpace(breakdown.Description))
                methods.Add(breakdown.Description);
        }

        var combined = string.Join(" / ", methods
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));

        return !string.IsNullOrWhiteSpace(combined) ? combined : row.PaymentMethod;
    }

    private static string BuildRelationCurrency(string? paymentMethod)
    {
        var normalized = (paymentMethod ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Contains("DOLAR", StringComparison.OrdinalIgnoreCase) || normalized.Contains("USD", StringComparison.OrdinalIgnoreCase))
            return "USD";

        if (normalized.Contains("EFECTIVO", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("TARJETA", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("T/C", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PESO", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("MXN", StringComparison.OrdinalIgnoreCase))
        {
            return "MXN";
        }

        return string.Empty;
    }

    private static IEnumerable<string> SplitRelationTokens(string? value) =>
        (value ?? string.Empty)
            .Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<List<SqlRelationRow>> ReadRelationRowsAsync(LocalSqlServerSource sqlSource, SqlConnection connection, string? search, DateTime? start, DateTime? end, int? maxRows, string? siteName)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = maxRows.HasValue ? 45 : 120;
        var topClause = maxRows.HasValue ? $"TOP ({maxRows.Value.ToString(CultureInfo.InvariantCulture)}) " : string.Empty;
        var cteOrderClause = maxRows.HasValue ? "ORDER BY COALESCE(a.fecha_operacion, a.fecha_creacion) DESC" : string.Empty;
        var includeRelationTicketApply = maxRows.HasValue;
        var posFolioExpression = includeRelationTicketApply
            ? "COALESCE(NULLIF(LTRIM(RTRIM(a.folio_pos)), ''), COALESCE(rel.FolioPosList, ''), '')"
            : "COALESCE(a.folio_pos, '')";
        var relationTicketApplyClause = includeRelationTicketApply
            ? """
            OUTER APPLY
            (
                SELECT STUFF(
                (
                    SELECT DISTINCT ', ' + LTRIM(RTRIM(r.FolioPos))
                    FROM dbo.RelacionTicketTaxista r
                    WHERE NULLIF(LTRIM(RTRIM(COALESCE(r.FolioPos, ''))), '') IS NOT NULL
                      AND (
                            COALESCE(r.FolioOperacion, '') = COALESCE(NULLIF(a.folio_app_original, ''), a.folio_app, '')
                         OR COALESCE(r.FolioApp, '') = COALESCE(a.folio_app, '')
                      )
                    FOR XML PATH(''), TYPE
                ).value('.', 'nvarchar(max)'), 1, 2, ''
                ) AS FolioPosList
            ) rel
            """
            : string.Empty;
        var endExclusive = end?.Date.AddDays(1);
        command.CommandText = $"""
            WITH app_base AS
            (
                SELECT {topClause} a.*
                FROM {sqlSource.PosTable("AppMovilRegistro")} a
                WHERE COALESCE(a.folio_app, '') <> ''
                  AND (
                        @start IS NULL
                     OR a.fecha_operacion >= @start
                     OR (a.fecha_operacion IS NULL AND a.fecha_creacion >= @start)
                  )
                  AND (
                        @endExclusive IS NULL
                     OR a.fecha_operacion < @endExclusive
                     OR (a.fecha_operacion IS NULL AND a.fecha_creacion < @endExclusive)
                  )
                  AND (
                        @site IS NULL
                     OR a.sitio = @site
                     OR (@site = N'Plaza 28' AND a.sitio = N'Tienda Plaza 28')
                     OR (@site = N'Tienda Plaza 28' AND a.sitio = N'Plaza 28')
                  )
                  AND (
                        @q IS NULL
                     OR a.folio_app LIKE '%' + @q + '%'
                     OR a.folio_app_original LIKE '%' + @q + '%'
                     OR COALESCE(a.folio_pos, '') LIKE '%' + @q + '%'
                     OR EXISTS
                        (
                            SELECT 1
                            FROM dbo.RelacionTicketTaxista r
                            WHERE NULLIF(LTRIM(RTRIM(COALESCE(r.FolioPos, ''))), '') IS NOT NULL
                              AND (
                                    COALESCE(r.FolioOperacion, '') = COALESCE(NULLIF(a.folio_app_original, ''), a.folio_app, '')
                                 OR COALESCE(r.FolioApp, '') = COALESCE(a.folio_app, '')
                              )
                              AND COALESCE(r.FolioPos, '') LIKE '%' + @q + '%'
                        )
                     OR COALESCE(a.hotel, '') LIKE '%' + @q + '%'
                     OR COALESCE(a.vendedor_nombre, '') LIKE '%' + @q + '%'
                     OR COALESCE(a.folio_gafete, '') LIKE '%' + @q + '%'
                     OR COALESCE(a.nacionalidad, '') LIKE '%' + @q + '%'
                     OR COALESCE(a.tipo_operacion, '') LIKE '%' + @q + '%'
                     OR COALESCE(CAST(a.id_catalogo AS nvarchar(60)), '') LIKE '%' + @q + '%'
                     OR COALESCE(a.notas, '') LIKE '%' + @q + '%'
                  )
                {cteOrderClause}
            )
            SELECT
              ROW_NUMBER() OVER (ORDER BY COALESCE(a.fecha_operacion, a.fecha_creacion) DESC) AS Id,
              COALESCE(a.folio_app, '') AS AppFolio,
              COALESCE(NULLIF(a.folio_app_original, ''), a.folio_app, '') AS OperationFolio,
              {posFolioExpression} AS PosFolio,
              COALESCE(a.folio_gafete, '') AS Badge,
              COALESCE(a.vendedor_nombre, '') AS DriverName,
              COALESCE(a.seller_name, '') AS VendorName,
              COALESCE(a.total, 0) AS Payout,
              COALESCE(a.notas, '') AS Notes,
              'APP MOVIL' AS Source,
              COALESCE(a.usuario_movil, '') AS SourceUser,
              CONVERT(nvarchar(30), COALESCE(a.fecha_operacion, a.fecha_creacion), 120) AS DateText,
              COALESCE(a.hotel, '') AS Hotel,
              COALESCE(a.origen, '') AS Origen,
              COALESCE(a.sitio, '') AS Sitio,
              COALESCE(a.destino, '') AS Destino,
              COALESCE(a.unidad, '') AS Unidad,
              COALESCE(a.placas, '') AS Placas,
              COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
              COALESCE(a.nacionalidad, '') AS Nacionalidad,
              COALESCE(a.tipo_operacion, '') AS TransporteTipo,
              CASE WHEN COALESCE(a.tarjeta,0) > 0 THEN 'Tarjeta' WHEN COALESCE(a.efectivo,0) > 0 THEN 'Efectivo' WHEN COALESCE(a.dolares,0) > 0 THEN 'Dolares' ELSE '' END AS PaymentMethod,
              COALESCE(
                  NULLIF(a.estado_pago_dejada,''),
                  NULLIF(a.payout_status,''),
                  CASE WHEN COALESCE(CONVERT(nvarchar(30), a.fecha_pago_dejada, 120), CONVERT(nvarchar(30), a.payout_date, 120), '') <> '' THEN 'pagado' ELSE '' END
              ) AS PayoutStatus,
              COALESCE(a.ticket_pago_dejada, '') AS PayoutTicket,
              COALESCE(CAST(a.id_catalogo AS nvarchar(60)), '') AS TaxistaId,
              COALESCE(a.usuario_pago_dejada, '') AS PayoutUser,
              COALESCE(CONVERT(nvarchar(30), a.fecha_pago_dejada, 120), CONVERT(nvarchar(30), a.payout_date, 120), '') AS PayoutDate,
              COALESCE(a.pago_comision, 0) AS CommissionPaid,
              CASE WHEN COALESCE(a.fecha_pago_dejada, a.payout_date) IS NOT NULL OR UPPER(COALESCE(a.estado_pago_dejada, a.payout_status, '')) IN ('PAGADO','PAGADA') THEN COALESCE(a.total, 0) ELSE 0 END AS PayoutPaid,
              COALESCE(a.pax, 0) AS Passengers,
              COALESCE(a.adult_count, 0) AS AdultPassengers,
              COALESCE(a.youth_count, 0) AS YouthPassengers,
              COALESCE(a.minor_count, 0) AS ChildPassengers,
              COALESCE(a.no_show_count, 0) AS NoShowCount
            FROM app_base a
            {relationTicketApplyClause}
            ORDER BY COALESCE(a.fecha_operacion, a.fecha_creacion) DESC;
            """;
        command.Parameters.AddWithValue("@start", start?.Date ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@endExclusive", endExclusive.HasValue ? endExclusive.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("@site", string.IsNullOrWhiteSpace(siteName) ? (object)DBNull.Value : siteName.Trim());
        command.Parameters.AddWithValue("@q", string.IsNullOrWhiteSpace(search) ? (object)DBNull.Value : search.Trim());
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<SqlRelationRow>();
        while (await reader.ReadAsync())
        {
            var payoutStatus = reader.IsDBNull(21) ? string.Empty : reader.GetString(21);
            var payoutDate = reader.IsDBNull(25) ? string.Empty : reader.GetString(25);
            var payoutPaid = Convert.ToDecimal(reader.GetValue(27), CultureInfo.InvariantCulture);
            var payoutTicket = reader.IsDBNull(22) ? string.Empty : reader.GetString(22);
            var payoutUser = reader.IsDBNull(24) ? string.Empty : reader.GetString(24);
            result.Add(new SqlRelationRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetString(15),
                reader.GetString(16),
                reader.GetString(17),
                reader.GetString(18),
                reader.GetString(19),
                reader.GetString(20),
                payoutStatus,
                reader.GetString(22),
                reader.GetString(23),
                reader.GetString(24),
                reader.GetString(25),
                reader.GetString(26),
                Convert.ToDecimal(reader.GetValue(27), CultureInfo.InvariantCulture),
                Convert.ToDecimal(reader.GetValue(28), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(29), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(30), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(31), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(32), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(33), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static async Task<List<(string Ticket, decimal Total)>> LoadRelationStoreTicketsAsync(SqlConnection compuConnection, SqlConnection joyeriaConnection, string operationFolio, string appFolio, string posFolio)
    {
        var keys = new[] { operationFolio, appFolio }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new List<(string Ticket, decimal Total)>();
        result.AddRange(await ReadRelationStoreTicketsAsync(compuConnection, "folioregistro", "folio_remision", keys, posFolio));
        result.AddRange(await ReadRelationStoreTicketsAsync(joyeriaConnection, "folio_registro", "COALESCE(folio_pedido, folio_factura)", keys, posFolio));
        return result
            .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Total).First())
            .ToList();
    }

    private static async Task<Dictionary<int, List<(string Ticket, decimal Total)>>> LoadRelationStoreTicketsBatchAsync(SqlConnection compuConnection, SqlConnection joyeriaConnection, IReadOnlyList<SqlRelationRow> rows)
    {
        var result = Enumerable.Range(0, rows.Count)
            .ToDictionary(x => x, _ => new List<(string Ticket, decimal Total)>());
        var rowIndexesByKey = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var rowIndexesByTicket = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var key in new[] { rows[i].OperationFolio, rows[i].AppFolio }
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Select(x => x.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!rowIndexesByKey.TryGetValue(key, out var indexes))
                {
                    indexes = [];
                    rowIndexesByKey[key] = indexes;
                }
                indexes.Add(i);
            }

            foreach (var ticket in SplitRelationTokens(rows[i].PosFolio)
                         .Select(NormalizeRelationTicket)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!rowIndexesByTicket.TryGetValue(ticket, out var indexes))
                {
                    indexes = [];
                    rowIndexesByTicket[ticket] = indexes;
                }
                indexes.Add(i);
            }
        }

        var keys = rowIndexesByKey.Keys.ToArray();
        foreach (var item in await ReadRelationStoreTicketsByFolioBatchAsync(compuConnection, "folioregistro", "folio_remision", keys))
            AddBatchTicket(result, rowIndexesByKey, item.Key, item.Ticket, item.Total);
        foreach (var item in await ReadRelationStoreTicketsByFolioBatchAsync(joyeriaConnection, "folio_registro", "COALESCE(folio_pedido, folio_factura)", keys))
            AddBatchTicket(result, rowIndexesByKey, item.Key, item.Ticket, item.Total);

        var tickets = rowIndexesByTicket.Keys.ToArray();
        foreach (var item in await ReadRelationStoreTicketsByTicketBatchAsync(compuConnection, "folio_remision", tickets))
            AddBatchTicket(result, rowIndexesByTicket, item.LookupTicket, item.Ticket, item.Total);
        foreach (var item in await ReadRelationStoreTicketsByTicketBatchAsync(joyeriaConnection, "COALESCE(folio_pedido, folio_factura)", tickets))
            AddBatchTicket(result, rowIndexesByTicket, item.LookupTicket, item.Ticket, item.Total);

        return result.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.Total).First())
                .ToList());
    }

    private static void AddBatchTicket(Dictionary<int, List<(string Ticket, decimal Total)>> result, IReadOnlyDictionary<string, List<int>> indexesByLookup, string lookup, string ticket, decimal total)
    {
        if (!indexesByLookup.TryGetValue(lookup, out var indexes)) return;
        foreach (var index in indexes)
            result[index].Add((ticket, total));
    }

    private static async Task<List<(string Key, string Ticket, decimal Total)>> ReadRelationStoreTicketsByFolioBatchAsync(SqlConnection connection, string folioColumn, string ticketColumn, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return [];
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        var parameters = new List<string>();
        for (var i = 0; i < keys.Count; i++)
        {
            var parameter = "@keyBatch" + i.ToString(CultureInfo.InvariantCulture);
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, keys[i]);
        }

        command.CommandText = $"""
            SELECT
                CAST({folioColumn} AS nvarchar(60)) AS LookupKey,
                CAST({ticketColumn} AS nvarchar(80)) AS Ticket,
                CAST(total AS decimal(18,2)) AS Total
            FROM dbo.remisioM
            WHERE CAST({folioColumn} AS nvarchar(60)) IN ({string.Join(",", parameters)});
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<(string Key, string Ticket, decimal Total)>();
        while (await reader.ReadAsync())
        {
            var key = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var ticket = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(ticket)) continue;
            result.Add((key, ticket, Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static async Task<List<(string LookupTicket, string Ticket, decimal Total)>> ReadRelationStoreTicketsByTicketBatchAsync(SqlConnection connection, string ticketColumn, IReadOnlyList<string> tickets)
    {
        if (tickets.Count == 0) return [];
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        var parameters = new List<string>();
        for (var i = 0; i < tickets.Count; i++)
        {
            var parameter = "@ticketBatch" + i.ToString(CultureInfo.InvariantCulture);
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, tickets[i]);
        }

        command.CommandText = $"""
            SELECT
                CAST({ticketColumn} AS nvarchar(80)) AS Ticket,
                CAST(total AS decimal(18,2)) AS Total
            FROM dbo.remisioM
            WHERE CAST({ticketColumn} AS nvarchar(80)) IN ({string.Join(",", parameters)});
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<(string LookupTicket, string Ticket, decimal Total)>();
        while (await reader.ReadAsync())
        {
            var ticket = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(ticket)) continue;
            result.Add((NormalizeRelationTicket(ticket), ticket, Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static async Task<List<(string Ticket, decimal Total)>> ReadRelationStoreTicketsAsync(SqlConnection connection, string folioColumn, string ticketColumn, IReadOnlyList<string> keys, string posFolio)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        var filters = new List<string>();
        var parameters = new List<string>();
        for (var i = 0; i < keys.Count; i++)
        {
            var parameter = "@key" + i.ToString(CultureInfo.InvariantCulture);
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, keys[i]);
        }
        if (parameters.Count > 0)
            filters.Add($"CAST({folioColumn} AS nvarchar(60)) IN ({string.Join(",", parameters)})");
        if (!string.IsNullOrWhiteSpace(posFolio))
        {
            command.Parameters.AddWithValue("@pos", posFolio);
            filters.Add($"CAST({ticketColumn} AS nvarchar(80)) = @pos");
        }
        if (filters.Count == 0) return [];
        command.CommandText = $"""
            SELECT CAST({ticketColumn} AS nvarchar(80)) AS Ticket, CAST(total AS decimal(18,2)) AS Total
            FROM dbo.remisioM
            WHERE {string.Join(" OR ", filters)};
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<(string Ticket, decimal Total)>();
        while (await reader.ReadAsync())
            result.Add((reader.GetString(0), Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture)));
        return result;
    }

    private static decimal CalculateRelationCommission(string transportType, IReadOnlyList<(string Ticket, decimal Total)> tickets, IReadOnlyDictionary<string, RelationPaymentBreakdown> payments, IReadOnlyDictionary<string, RelationExpenseBreakdown> expenses, decimal payout, LocalTransport? transportInfo)
    {
        if (tickets.Count == 0) return 0m;
        var highestTicket = tickets.OrderByDescending(x => x.Total).ThenBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase).First();
        decimal total = 0m;
        foreach (var ticket in tickets)
        {
            payments.TryGetValue(ticket.Ticket, out var breakdown);
            breakdown ??= new RelationPaymentBreakdown(ticket.Total, 0m, 0m, "SIN PAGO");
            expenses.TryGetValue(ticket.Ticket, out var expense);
            var saleTotal = ticket.Total;
            var deduction = string.Equals(ticket.Ticket, highestTicket.Ticket, StringComparison.OrdinalIgnoreCase) ? payout : 0m;
            deduction += CalculateRelationExpenseDeductions(transportType, expense);
            var percentage = ResolveAuthoritativePercentage(transportType, transportInfo);
            var scale = breakdown.Total > 0m && saleTotal > 0m ? saleTotal / breakdown.Total : 1m;
            var net = CommissionPaymentRules.NetAfterRetention(breakdown.NonCard * scale, ResolveRelationDiscount(transportType, transportInfo, PaymentKind.Cash))
                + CommissionPaymentRules.NetAfterRetention(breakdown.Card * scale, ResolveRelationDiscount(transportType, transportInfo, PaymentKind.Card))
                + CommissionPaymentRules.NetAfterRetention(breakdown.Amex * scale, ResolveRelationDiscount(transportType, transportInfo, PaymentKind.Amex));
            if (breakdown.Total <= 0m) net = saleTotal;
            total += Math.Max(0m, decimal.Truncate(Math.Max(0m, net - deduction) * (percentage / 100m)));
        }
        return total;
    }

    private static decimal ResolveRelationDiscount(string? transportType, LocalTransport? transportInfo, PaymentKind kind)
    {
        var catalog = kind switch
        {
            PaymentKind.Cash => transportInfo?.CashDiscount ?? 0m,
            PaymentKind.Amex => transportInfo?.AmexDiscount ?? 0m,
            _ => transportInfo?.CardDiscount ?? 0m
        };
        if (kind == PaymentKind.Amex)
            return CommissionPaymentRules.ResolveAmexRetention(catalog);
        if (catalog > 0m)
            return CommissionPaymentRules.NormalizePercent(catalog);
        if (IsSalmoranTransport(transportType))
        {
            return kind switch
            {
                PaymentKind.Cash => 16m,
                PaymentKind.Amex => CommissionPaymentRules.AmexRetentionPercent,
                _ => CommissionPaymentRules.CardRetentionPercent
            };
        }
        if (kind == PaymentKind.Card) return CommissionPaymentRules.CardRetentionPercent;
        return CommissionPaymentRules.CashRetentionPercent;
    }

    private static decimal CalculateRelationExpenseDeductions(string? transportType, RelationExpenseBreakdown expense)
    {
        var duplicateSalmoranExpense = IsSalmoranTransport(transportType)
            && expense.GastosVarios > 0m
            && expense.GastosVarios == expense.Degustacion;
        var deductions = expense.Dejada;
        deductions += duplicateSalmoranExpense ? 0m : expense.GastosVarios;
        deductions += expense.Degustacion;
        deductions += expense.Reparacion;
        deductions += expense.Bebidas;
        deductions += expense.CajasRegalo;
        return deductions;
    }

    private static async Task<Dictionary<string, RelationPaymentBreakdown>> LoadRelationPaymentsAsync(SqlConnection compuConnection, SqlConnection joyeriaConnection, IReadOnlyList<string> tickets)
    {
        var result = new Dictionary<string, RelationPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in await ReadRelationPaymentsAsync(compuConnection, tickets)) result[pair.Key] = pair.Value;
        foreach (var pair in await ReadRelationPaymentsAsync(joyeriaConnection, tickets)) result[pair.Key] = pair.Value;
        return result;
    }

    private static async Task<Dictionary<string, RelationExpenseBreakdown>> LoadRelationExpensesAsync(SqlConnection compuConnection, SqlConnection joyeriaConnection, IReadOnlyList<string> tickets)
    {
        var result = new Dictionary<string, RelationExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in await ReadRelationExpensesAsync(compuConnection, tickets, false)) result[pair.Key] = pair.Value;
        foreach (var pair in await ReadRelationExpensesAsync(joyeriaConnection, tickets, true)) result[pair.Key] = pair.Value;
        return result;
    }

    private static async Task<Dictionary<string, RelationPaymentBreakdown>> ReadRelationPaymentsAsync(SqlConnection connection, IReadOnlyList<string> tickets)
    {
        if (tickets.Count == 0) return new Dictionary<string, RelationPaymentBreakdown>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        var parameters = new List<string>();
        for (var i = 0; i < tickets.Count; i++)
        {
            var parameter = "@ticket" + i.ToString(CultureInfo.InvariantCulture);
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, tickets[i]);
        }
        command.CommandText = $"""
            SELECT p.folio_factura AS Ticket, COALESCE(m.Nombre, '') AS PaymentName, CAST(p.total AS decimal(18,2)) AS Total
            FROM dbo.pagosM p
            LEFT JOIN dbo.monedas m ON m.moneda = p.moneda
            WHERE p.folio_factura IN ({string.Join(",", parameters)});
            """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string Ticket, string PaymentName, decimal Total)>();
        while (await reader.ReadAsync())
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1), Convert.ToDecimal(reader.GetValue(2), CultureInfo.InvariantCulture)));

        return rows.GroupBy(x => x.Ticket, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var nonCard = 0m;
                    var card = 0m;
                    var amex = 0m;
                    var description = string.Join(" / ", g.Select(x => $"{x.PaymentName} {x.Total:C2}").Distinct(StringComparer.OrdinalIgnoreCase));
                    foreach (var payment in g)
                    {
                        if (IsAmexPayment(payment.PaymentName)) amex += payment.Total;
                        else if (IsCardPayment(payment.PaymentName)) card += payment.Total;
                        else nonCard += payment.Total;
                    }
                    return new RelationPaymentBreakdown(nonCard, card, amex, description);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, RelationExpenseBreakdown>> ReadRelationExpensesAsync(SqlConnection connection, IReadOnlyList<string> tickets, bool joyeria)
    {
        if (tickets.Count == 0) return new Dictionary<string, RelationExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 5;
        var parameters = new List<string>();
        for (var i = 0; i < tickets.Count; i++)
        {
            var parameter = "@ticketExpense" + i.ToString(CultureInfo.InvariantCulture);
            parameters.Add(parameter);
            command.Parameters.AddWithValue(parameter, tickets[i]);
        }

        command.CommandText = joyeria
            ? $"""
                SELECT CAST(folio_factura AS nvarchar(80)) AS Ticket, UPPER(COALESCE(referencia, '')) AS Referencia, UPPER(COALESCE(concepto, '')) AS Concepto, CAST(COALESCE(total, 0) AS decimal(18,2)) AS Total
                FROM dbo.gastos
                WHERE CAST(folio_factura AS nvarchar(80)) IN ({string.Join(",", parameters)});
                """
            : $"""
                SELECT CAST(folio_remision AS nvarchar(80)) AS Ticket, UPPER(COALESCE(referencia, '')) AS Referencia, UPPER(COALESCE(concepto, '')) AS Concepto, CAST(COALESCE(total, 0) AS decimal(18,2)) AS Total
                FROM dbo.egresos
                WHERE CAST(folio_remision AS nvarchar(80)) IN ({string.Join(",", parameters)});
                """;
        await using var reader = await command.ExecuteReaderAsync();
        var result = new Dictionary<string, RelationExpenseBreakdown>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            var ticket = reader.GetString(0);
            var text = ((reader.IsDBNull(1) ? string.Empty : reader.GetString(1)) + " " + (reader.IsDBNull(2) ? string.Empty : reader.GetString(2))).ToUpperInvariant();
            var total = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture);
            result.TryGetValue(ticket, out var current);
            var updated = current with
            {
                Dejada = current.Dejada + (text.Contains("DEJADA", StringComparison.OrdinalIgnoreCase) ? total : 0m),
                GastosVarios = current.GastosVarios + ((text.Contains("GASTOS VARIOS", StringComparison.OrdinalIgnoreCase) || text.EndsWith(" GV", StringComparison.OrdinalIgnoreCase) || text.Contains(" GV ", StringComparison.OrdinalIgnoreCase)) ? total : 0m),
                Degustacion = current.Degustacion + (text.Contains("DEGUST", StringComparison.OrdinalIgnoreCase) ? total : 0m),
                Reparacion = current.Reparacion + (text.Contains("REPARA", StringComparison.OrdinalIgnoreCase) ? total : 0m),
                Bebidas = current.Bebidas + (LooksLikeRelationBeverage(text) ? total : 0m),
                CajasRegalo = current.CajasRegalo + ((text.Contains("CAJA", StringComparison.OrdinalIgnoreCase) || text.Contains("REGALO", StringComparison.OrdinalIgnoreCase)) ? total : 0m)
            };
            result[ticket] = updated;
        }
        return result;
    }

    private static bool LooksLikeRelationBeverage(string text) =>
        text.Contains("CERVEZA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("CORONA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("COCA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("AGUA", StringComparison.OrdinalIgnoreCase)
        || text.Contains("CANTARITO", StringComparison.OrdinalIgnoreCase)
        || text.Contains("REFRESCO", StringComparison.OrdinalIgnoreCase)
        || text.Contains("BEBIDA", StringComparison.OrdinalIgnoreCase);

    private static string ResolveCommissionStatus(decimal amount, decimal paid)
    {
        if (amount <= 0m) return "SIN CALCULAR";
        if (paid >= amount && amount > 0m) return "PAGADA";
        if (paid > 0m) return "PARCIAL";
        return "PENDIENTE";
    }

    private static decimal ResolveAuthoritativePercentage(string? transportType, LocalTransport? transportInfo = null)
    {
        var catalogPercent = CommissionPaymentRules.NormalizePercent(transportInfo?.Commission ?? 0m);
        if (catalogPercent > 0m && catalogPercent <= 100m) return catalogPercent;
        if (IsMajesticTransport(transportType)) return 8m;
        if (IsSalmoranTransport(transportType)) return 20m;
        return 10m;
    }

    private static LocalTransport? ResolveRelationTransport(string? transportType, IReadOnlyList<LocalTransport> catalog)
    {
        if (catalog.Count == 0 || string.IsNullOrWhiteSpace(transportType)) return null;
        var key = NormalizeTransportLookup(transportType);
        var match = catalog.FirstOrDefault(row =>
            string.Equals(NormalizeTransportLookup(row.Code), key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(NormalizeTransportLookup(row.Name), key, StringComparison.OrdinalIgnoreCase));
        match ??= catalog.FirstOrDefault(row =>
            key.Contains(NormalizeTransportLookup(row.Code), StringComparison.OrdinalIgnoreCase)
            || key.Contains(NormalizeTransportLookup(row.Name), StringComparison.OrdinalIgnoreCase)
            || NormalizeTransportLookup(row.Code).Contains(key, StringComparison.OrdinalIgnoreCase)
            || NormalizeTransportLookup(row.Name).Contains(key, StringComparison.OrdinalIgnoreCase));
        return match;
    }

    private static string NormalizeTransportLookup(string? value)
    {
        var text = value ?? string.Empty;
        return new string(text
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool IsMajesticTransport(string? value)
    {
        var text = value ?? string.Empty;
        return text.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TRAVEL EXPERIENCE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MAESTIC", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSalmoranTransport(string? value) => (value ?? string.Empty).Contains("SALMORAN", StringComparison.OrdinalIgnoreCase);

    private static bool IsAmexPayment(string? paymentName) => CommissionPaymentRules.IsAmexPayment(paymentName);

    private static bool IsCardPayment(string? paymentName) => CommissionPaymentRules.IsCardPayment(paymentName);

    private enum PaymentKind
    {
        Cash,
        Card,
        Amex
    }

    private sealed record SqlRelationRow(long Id, string AppFolio, string OperationFolio, string PosFolio, string Badge, string Driver, string Vendor, decimal Payout, string Notes, string Source, string SourceUser, string DateText, string Hotel, string Origin, string Site, string Destination, string Unit, string Plates, string Phone, string Nationality, string TransportType, string PaymentMethod, string PayoutStatus, string PayoutTicket, string TaxistaId, string PayoutUser, string PayoutDate, decimal CommissionPaid, decimal PayoutPaid, int Passengers, int AdultPassengers, int YouthPassengers, int ChildPassengers, int NoShowCount);
    private sealed record RelationPaymentBreakdown(decimal NonCard, decimal Card, decimal Amex, string Description)
    {
        public decimal Total => NonCard + Card + Amex;
    }
    private readonly record struct RelationExpenseBreakdown(decimal Dejada = 0m, decimal GastosVarios = 0m, decimal Degustacion = 0m, decimal Reparacion = 0m, decimal Bebidas = 0m, decimal CajasRegalo = 0m);

    public async Task<long> SaveRateAsync(LocalRate rate) => await SaveAsync("INSERT INTO LocalTarifas (Tipo, Nombre, Dejada, Minimo, Maximo, Activa) VALUES ($type,$name,$payout,$min,$max,$active) ON CONFLICT(Tipo,Nombre) DO UPDATE SET Dejada=excluded.Dejada,Minimo=excluded.Minimo,Maximo=excluded.Maximo,Activa=excluded.Activa RETURNING Id;", ("$type", rate.Type), ("$name", rate.Name), ("$payout", rate.Payout), ("$min", rate.Minimum), ("$max", rate.Maximum), ("$active", rate.Active ? 1 : 0));
    public async Task<long> SaveHotelAsync(string name) => await SaveAsync("INSERT INTO LocalHoteles (Nombre,Activo) VALUES ($name,1) ON CONFLICT(Nombre) DO UPDATE SET Activo=1 RETURNING Id;", ("$name", Require(name, "El hotel")));
    public async Task<long> SaveDriverAsync(LocalDriver driver) => await SaveAsync("INSERT INTO LocalTaxistas (Clave,Nombre,Telefono,Placas,Modelo,Unidad,TipoServicio,Estatus) VALUES ($code,$name,$phone,$plates,$model,$unit,$service,$status) ON CONFLICT(Clave) DO UPDATE SET Nombre=excluded.Nombre,Telefono=excluded.Telefono,Placas=excluded.Placas,Modelo=excluded.Modelo,Unidad=excluded.Unidad,TipoServicio=excluded.TipoServicio,Estatus=excluded.Estatus RETURNING Id;", ("$code", Require(driver.Code, "La clave")), ("$name", Require(driver.Name, "El nombre")), ("$phone", driver.Phone), ("$plates", driver.Plates), ("$model", driver.Model), ("$unit", driver.Unit), ("$service", driver.ServiceType), ("$status", driver.Status));
    public async Task<long> SaveTransportAsync(LocalTransport transport) => await SaveAsync("INSERT INTO LocalTransportes (Clave,Nombre,Minimo,Maximo,Comision,DescuentoEfectivo,DescuentoTarjeta,DescuentoAmex,Activo) VALUES ($code,$name,$min,$max,$commission,$cash,$card,$amex,$active) ON CONFLICT(Clave) DO UPDATE SET Nombre=excluded.Nombre,Minimo=excluded.Minimo,Maximo=excluded.Maximo,Comision=excluded.Comision,DescuentoEfectivo=excluded.DescuentoEfectivo,DescuentoTarjeta=excluded.DescuentoTarjeta,DescuentoAmex=excluded.DescuentoAmex,Activo=excluded.Activo RETURNING Id;", ("$code", Require(transport.Code, "La clave")), ("$name", Require(transport.Name, "El nombre")), ("$min", transport.Minimum), ("$max", transport.Maximum), ("$commission", transport.Commission), ("$cash", transport.CashDiscount), ("$card", transport.CardDiscount), ("$amex", transport.AmexDiscount), ("$active", transport.Active ? 1 : 0));
    public async Task<long> SaveGuideAsync(LocalGuide guide) => await SaveAsync("INSERT INTO LocalGuias (Clave,Nombre,Telefono,Comision,Estatus) VALUES ($code,$name,$phone,$commission,$status) ON CONFLICT(Clave) DO UPDATE SET Nombre=excluded.Nombre,Telefono=excluded.Telefono,Comision=excluded.Comision,Estatus=excluded.Estatus RETURNING Id;", ("$code", Require(guide.Code, "La clave")), ("$name", Require(guide.Name, "El nombre")), ("$phone", guide.Phone), ("$commission", guide.Commission), ("$status", string.IsNullOrWhiteSpace(guide.Status) ? "Activo" : guide.Status));
    public async Task<long> SaveExpenseAsync(LocalExpense expense)
    {
        if (expense.Amount < 0) throw new ArgumentException("El gasto no puede ser negativo.");
        return await SaveAsync("INSERT INTO LocalGastos (Fecha,Folio,Concepto,Importe,Notas,Estatus,Usuario) VALUES ($date,$folio,$concept,$amount,$notes,$status,$user) RETURNING Id;", ("$date", expense.Date.ToString("O", CultureInfo.InvariantCulture)), ("$folio", expense.Folio), ("$concept", Require(expense.Concept, "El concepto")), ("$amount", expense.Amount), ("$notes", expense.Notes), ("$status", string.IsNullOrWhiteSpace(expense.Status) ? "Activo" : expense.Status), ("$user", Require(expense.User, "El usuario")));
    }
    public async Task<long> SaveRelationAsync(LocalRelation relation)
    {
        if (_sqlSource is not null)
            return await SaveRelationToSqlServerAsync(relation);

        return await SaveAsync("INSERT INTO LocalRelaciones (FolioApp,FolioOperacion,FolioPos,Gafete,Taxista,Vendedor,Dejada,Observaciones) VALUES ($app,$operation,$pos,$badge,$driver,$vendor,$payout,$notes) RETURNING Id;", ("$app", relation.AppFolio), ("$operation", Require(relation.OperationFolio, "El folio operación")), ("$pos", relation.PosFolio), ("$badge", relation.Badge), ("$driver", relation.Driver), ("$vendor", relation.Vendor), ("$payout", relation.Payout), ("$notes", relation.Notes));
    }

    private async Task<long> SaveRelationToSqlServerAsync(LocalRelation relation)
    {
        if (_sqlSource is null)
            throw new InvalidOperationException("No hay conexion a SQL Server para guardar la relacion.");

        var operationFolio = Require(relation.OperationFolio, "El folio operacion").Trim();
        var appFolio = string.IsNullOrWhiteSpace(relation.AppFolio) ? operationFolio : relation.AppFolio.Trim();
        var badges = ParseRelationBadgeNumbers(relation.Badge).ToArray();
        if (badges.Length == 0)
            throw new InvalidOperationException("Captura al menos un gafete valido para guardar la relacion.");

        await using var connection = await _sqlSource.OpenPosAsync();
        await using var transaction = connection.BeginTransaction();
        try
        {
            var current = await ReadAppRecordForRelationAsync(connection, transaction, appFolio, operationFolio);
            var driverName = FirstText(relation.Driver, current.DriverName);
            var vendorName = FirstText(relation.Vendor);
            var hotel = FirstText(relation.Hotel, current.Hotel);
            var site = FirstText(relation.Site, current.Site);
            var unit = FirstText(relation.Unit, current.Unit);
            var plates = FirstText(relation.Plates, current.Plates);
            var phone = FirstText(relation.Phone, current.Phone);
            var nationality = FirstText(relation.Nationality, current.Nationality);
            var transport = FirstText(relation.TransportType, current.TransportType);
            var notes = relation.Notes?.Trim() ?? string.Empty;
            var payout = relation.Payout ?? current.Payout;
            var passengers = Math.Max(0, relation.AdultPassengers + relation.YouthPassengers + relation.ChildPassengers);
            var date = current.OperationDate ?? DateTime.Now;
            var user = FirstText(relation.SourceUser, "desktop");
            var badgeText = string.Join(", ", badges.Select(x => x.ToString(CultureInfo.InvariantCulture)));

            await UpsertRelationRowAsync(connection, transaction, relation, appFolio, operationFolio, badgeText, driverName, vendorName, transport, payout, notes);
            await UpdateAppRecordForRelationAsync(connection, transaction, relation, appFolio, operationFolio, relation.PosFolio, badgeText, passengers, payout, transport, nationality, notes, vendorName, unit, plates, relation.AdultPassengers, relation.YouthPassengers, relation.ChildPassengers, relation.NoShowCount);
            await UpsertPayoutRowsAsync(connection, transaction, relation, appFolio, operationFolio, badges, driverName, vendorName, hotel, site, unit, phone, nationality, transport, payout, passengers, date, user);
            await ReconcileBadgeRowsAsync(connection, transaction, relation, operationFolio, badges, driverName, unit, phone, nationality, date, user);

            transaction.Commit();
            return relation.Id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task<AppRecordForRelation> ReadAppRecordForRelationAsync(SqlConnection connection, SqlTransaction transaction, string appFolio, string operationFolio)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT TOP (1)
                COALESCE(vendedor_nombre, ''),
                COALESCE(hotel, ''),
                COALESCE(sitio, ''),
                COALESCE(unidad, ''),
                COALESCE(placas, ''),
                COALESCE(NULLIF(telefono_taxista, ''), NULLIF(telefono_contacto, ''), ''),
                COALESCE(nacionalidad, ''),
                COALESCE(tipo_operacion, ''),
                COALESCE(notas, ''),
                COALESCE(total, 0),
                COALESCE(fecha_operacion, fecha_creacion),
                COALESCE(CAST(id_catalogo AS nvarchar(60)), '')
            FROM {_sqlSource!.PosTable("AppMovilRegistro")}
            WHERE folio_app = @app
               OR folio_app_original = @app
               OR folio_app = @operation
               OR folio_app_original = @operation
            ORDER BY COALESCE(fecha_operacion, fecha_creacion) DESC;
            """;
        command.Parameters.AddWithValue("@app", appFolio);
        command.Parameters.AddWithValue("@operation", operationFolio);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException($"No se encontro el folio {operationFolio} en AppMovilRegistro.");

        return new AppRecordForRelation(
            reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            Convert.ToDecimal(reader.GetValue(9), CultureInfo.InvariantCulture),
            reader.IsDBNull(10) ? null : Convert.ToDateTime(reader.GetValue(10), CultureInfo.InvariantCulture),
            reader.IsDBNull(11) ? string.Empty : reader.GetString(11));
    }

    private async Task UpsertRelationRowAsync(SqlConnection connection, SqlTransaction transaction, LocalRelation relation, string appFolio, string operationFolio, string badgeText, string driverName, string vendorName, string transport, decimal payout, string notes)
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = $"""
            UPDATE {_sqlSource!.PosTable("RelacionTicketTaxista")}
            SET FolioOperacion = @operation,
                FolioPos = @pos,
                Gafete = @badge,
                TaxistaId = @taxistaId,
                TaxistaNombre = @driver,
                Vendedor = @vendor,
                TransporteTipo = @transport,
                Dejada = @payout,
                Observaciones = @notes,
                Usuario = @user
            WHERE FolioApp = @app OR FolioApp = @operation;
            """;
        AddRelationParameters(update, relation, appFolio, operationFolio, badgeText, driverName, vendorName, transport, payout, notes);
        var affected = await update.ExecuteNonQueryAsync();
        if (affected > 0) return;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"""
            INSERT INTO {_sqlSource.PosTable("RelacionTicketTaxista")}
                (FolioApp, FolioOperacion, FolioPos, Gafete, TaxistaId, TaxistaNombre, Vendedor, TransporteTipo, Dejada, Observaciones, Usuario)
            VALUES
                (@app, @operation, @pos, @badge, @taxistaId, @driver, @vendor, @transport, @payout, @notes, @user);
            """;
        AddRelationParameters(insert, relation, appFolio, operationFolio, badgeText, driverName, vendorName, transport, payout, notes);
        await insert.ExecuteNonQueryAsync();
    }

    private static void AddRelationParameters(SqlCommand command, LocalRelation relation, string appFolio, string operationFolio, string badgeText, string driverName, string vendorName, string transport, decimal payout, string notes)
    {
        command.Parameters.AddWithValue("@app", appFolio);
        command.Parameters.AddWithValue("@operation", operationFolio);
        command.Parameters.AddWithValue("@pos", string.IsNullOrWhiteSpace(relation.PosFolio) ? string.Empty : relation.PosFolio.Trim());
        command.Parameters.AddWithValue("@badge", badgeText);
        command.Parameters.AddWithValue("@taxistaId", string.IsNullOrWhiteSpace(relation.TaxistaId) ? string.Empty : relation.TaxistaId.Trim());
        command.Parameters.AddWithValue("@driver", driverName);
        command.Parameters.AddWithValue("@vendor", vendorName);
        command.Parameters.AddWithValue("@transport", transport);
        command.Parameters.AddWithValue("@payout", payout);
        command.Parameters.AddWithValue("@notes", notes);
        command.Parameters.AddWithValue("@user", string.IsNullOrWhiteSpace(relation.SourceUser) ? "desktop" : relation.SourceUser.Trim());
    }

    private async Task UpdateAppRecordForRelationAsync(SqlConnection connection, SqlTransaction transaction, LocalRelation relation, string appFolio, string operationFolio, string posFolio, string badgeText, int passengers, decimal payout, string transport, string nationality, string notes, string vendorName, string unit, string plates, int adultPassengers, int youthPassengers, int childPassengers, int noShowCount)
    {
        var assignments = new List<string>
        {
            "folio_pos = @pos",
            "folio_gafete = @badge",
            "vendedor_nombre = @driver",
            "id_catalogo = @taxistaId",
            "pax = @pax",
            "total = @payout",
            "tipo_operacion = @transport",
            "nacionalidad = @nationality",
            "notas = @notes"
        };

        if (await SqlColumnExistsAsync(connection, transaction, "AppMovilRegistro", "seller_name"))
            assignments.Add("seller_name = @vendor");
        if (await SqlColumnExistsAsync(connection, transaction, "AppMovilRegistro", "unidad"))
            assignments.Add("unidad = @unit");
        if (await SqlColumnExistsAsync(connection, transaction, "AppMovilRegistro", "placas"))
            assignments.Add("placas = @plates");
        if (await SqlColumnExistsAsync(connection, transaction, "AppMovilRegistro", "adult_count"))
            assignments.Add("adult_count = @adult");
        if (await SqlColumnExistsAsync(connection, transaction, "AppMovilRegistro", "youth_count"))
            assignments.Add("youth_count = @youth");
        if (await SqlColumnExistsAsync(connection, transaction, "AppMovilRegistro", "minor_count"))
            assignments.Add("minor_count = @child");
        if (await SqlColumnExistsAsync(connection, transaction, "AppMovilRegistro", "no_show_count"))
            assignments.Add("no_show_count = @noShow");

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {_sqlSource!.PosTable("AppMovilRegistro")}
            SET {string.Join("," + Environment.NewLine + "                ", assignments)}
            WHERE folio_app = @app
               OR folio_app_original = @app
               OR folio_app = @operation
               OR folio_app_original = @operation;
            """;
        command.Parameters.AddWithValue("@app", appFolio);
        command.Parameters.AddWithValue("@operation", operationFolio);
        command.Parameters.AddWithValue("@pos", string.IsNullOrWhiteSpace(posFolio) ? string.Empty : posFolio.Trim());
        command.Parameters.AddWithValue("@badge", badgeText);
        command.Parameters.AddWithValue("@driver", string.IsNullOrWhiteSpace(relation.Driver) ? string.Empty : relation.Driver.Trim());
        command.Parameters.AddWithValue("@taxistaId", ParseLongOrZero(relation.TaxistaId));
        command.Parameters.AddWithValue("@pax", passengers);
        command.Parameters.AddWithValue("@payout", payout);
        command.Parameters.AddWithValue("@transport", transport);
        command.Parameters.AddWithValue("@nationality", nationality);
        command.Parameters.AddWithValue("@notes", notes);
        command.Parameters.AddWithValue("@vendor", vendorName);
        command.Parameters.AddWithValue("@unit", unit);
        command.Parameters.AddWithValue("@plates", plates);
        command.Parameters.AddWithValue("@adult", Math.Max(0, adultPassengers));
        command.Parameters.AddWithValue("@youth", Math.Max(0, youthPassengers));
        command.Parameters.AddWithValue("@child", Math.Max(0, childPassengers));
        command.Parameters.AddWithValue("@noShow", Math.Max(0, noShowCount));
        await command.ExecuteNonQueryAsync();
    }

    private async Task UpsertPayoutRowsAsync(SqlConnection connection, SqlTransaction transaction, LocalRelation relation, string appFolio, string operationFolio, IReadOnlyList<int> badges, string driverName, string vendorName, string hotel, string site, string unit, string phone, string nationality, string transport, decimal payout, int passengers, DateTime operationDate, string user)
    {
        foreach (var badge in badges)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE {_sqlSource!.PosTable("dejadas")}
                SET nombrestaff = @driver,
                    nombrealmacen = @site,
                    fecha = @date,
                    hora = @hour,
                    nombrecajero = @user,
                    total = @payout,
                    codigorecepcion = @operation,
                    folioregistro = @operationNumber,
                    folioregistrostr = @operation,
                    unidad = @unit,
                    pax = @pax,
                    hotel = @hotel,
                    nombrevendedor = @vendor,
                    tipotransporte = @transport,
                    telefono = @phone,
                    gafete = @badgeText,
                    idtaxi = @taxistaId,
                    adl = @adult,
                    men = @youth,
                    inf = @child,
                    nacionalidad = @nationality
                WHERE (folioregistrostr = @operation OR CONVERT(nvarchar(60), folioregistro) = @operationNumberText)
                  AND CONVERT(nvarchar(50), gafete) = @badgeText;
                """;
            AddPayoutParameters(update, relation, operationFolio, badge, driverName, vendorName, hotel, site, unit, phone, nationality, transport, payout, passengers, operationDate, user);
            var affected = await update.ExecuteNonQueryAsync();
            if (affected > 0) continue;

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO {_sqlSource.PosTable("dejadas")}
                    (idstaff, nombrestaff, nombrealmacen, fecha, hora, idcajero, nombrecajero, total, codigorecepcion, folioregistro, folioregistrostr, unidad, pax, hotel, nombrevendedor, tipotransporte, telefono, gafete, idtaxi, adl, men, inf, nacionalidad)
                VALUES
                    (@taxistaIdText, @driver, @site, @date, @hour, 0, @user, @payout, @operation, @operationNumber, @operation, @unit, @pax, @hotel, @vendor, @transport, @phone, @badgeText, @taxistaId, @adult, @youth, @child, @nationality);
                """;
            AddPayoutParameters(insert, relation, operationFolio, badge, driverName, vendorName, hotel, site, unit, phone, nationality, transport, payout, passengers, operationDate, user);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static void AddPayoutParameters(SqlCommand command, LocalRelation relation, string operationFolio, int badge, string driverName, string vendorName, string hotel, string site, string unit, string phone, string nationality, string transport, decimal payout, int passengers, DateTime operationDate, string user)
    {
        var operationNumber = ParseLongOrZero(operationFolio);
        var taxistaId = (int)Math.Min(int.MaxValue, ParseLongOrZero(relation.TaxistaId));
        command.Parameters.AddWithValue("@operation", operationFolio);
        command.Parameters.AddWithValue("@operationNumber", operationNumber);
        command.Parameters.AddWithValue("@operationNumberText", operationNumber.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@driver", driverName);
        command.Parameters.AddWithValue("@vendor", vendorName);
        command.Parameters.AddWithValue("@hotel", hotel);
        command.Parameters.AddWithValue("@site", site);
        command.Parameters.AddWithValue("@unit", unit);
        command.Parameters.AddWithValue("@phone", phone);
        command.Parameters.AddWithValue("@nationality", nationality);
        command.Parameters.AddWithValue("@transport", transport);
        command.Parameters.AddWithValue("@payout", payout);
        command.Parameters.AddWithValue("@pax", passengers);
        command.Parameters.AddWithValue("@date", operationDate.Date);
        command.Parameters.AddWithValue("@hour", operationDate.ToString("HH:mm", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@user", user);
        command.Parameters.AddWithValue("@badgeText", badge.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@taxistaId", taxistaId);
        command.Parameters.AddWithValue("@taxistaIdText", string.IsNullOrWhiteSpace(relation.TaxistaId) ? string.Empty : relation.TaxistaId.Trim());
        command.Parameters.AddWithValue("@adult", relation.AdultPassengers);
        command.Parameters.AddWithValue("@youth", relation.YouthPassengers);
        command.Parameters.AddWithValue("@child", relation.ChildPassengers);
    }

    private async Task ReconcileBadgeRowsAsync(SqlConnection connection, SqlTransaction transaction, LocalRelation relation, string operationFolio, IReadOnlyList<int> badges, string driverName, string unit, string phone, string nationality, DateTime operationDate, string user)
    {
        await using (var releaseMissing = connection.CreateCommand())
        {
            releaseMissing.Transaction = transaction;
            var keepFilter = badges.Count > 0
                ? "AND CONVERT(nvarchar(50), gafete) NOT IN (" + string.Join(", ", badges.Select((_, i) => "@badge" + i.ToString(CultureInfo.InvariantCulture))) + ")"
                : string.Empty;
            releaseMissing.CommandText = $"""
                UPDATE {_sqlSource!.PosTable("gafete")}
                SET venta = 'R',
                    movimiento = 'REGRESO',
                    usuario = @user,
                    hora = SYSDATETIME()
                WHERE CONVERT(nvarchar(60), folioperacion) = @operation
                  AND UPPER(COALESCE(venta, '')) = 'A'
                  {keepFilter};
                """;
            releaseMissing.Parameters.AddWithValue("@operation", operationFolio);
            releaseMissing.Parameters.AddWithValue("@user", user);
            for (var i = 0; i < badges.Count; i++)
                releaseMissing.Parameters.AddWithValue("@badge" + i.ToString(CultureInfo.InvariantCulture), badges[i].ToString(CultureInfo.InvariantCulture));
            await releaseMissing.ExecuteNonQueryAsync();
        }

        foreach (var badge in badges)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"""
                UPDATE {_sqlSource.PosTable("gafete")}
                SET matricula = @staff,
                    fecha = @date,
                    venta = 'A',
                    hora = @activity,
                    movimiento = 'ENTREGA',
                    usuario = @user
                WHERE CONVERT(nvarchar(60), folioperacion) = @operation
                  AND gafete = @badge;
                """;
            AddBadgeParameters(update, relation, operationFolio, badge, driverName, operationDate, user);
            var affected = await update.ExecuteNonQueryAsync();
            if (affected > 0) continue;

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = $"""
                INSERT INTO {_sqlSource.PosTable("gafete")}
                    (matricula, gafete, fecha, venta, hora, folioperacion, movimiento, usuario)
                VALUES
                    (@staff, @badge, @date, 'A', @activity, @operation, 'ENTREGA', @user);
                """;
            AddBadgeParameters(insert, relation, operationFolio, badge, driverName, operationDate, user);
            await insert.ExecuteNonQueryAsync();
        }
    }

    private static void AddBadgeParameters(SqlCommand command, LocalRelation relation, string operationFolio, int badge, string driverName, DateTime operationDate, string user)
    {
        command.Parameters.AddWithValue("@staff", string.IsNullOrWhiteSpace(relation.TaxistaId) ? driverName : relation.TaxistaId.Trim());
        command.Parameters.AddWithValue("@badge", badge);
        command.Parameters.AddWithValue("@date", operationDate.Date);
        command.Parameters.AddWithValue("@activity", DateTime.Now);
        command.Parameters.AddWithValue("@operation", operationFolio);
        command.Parameters.AddWithValue("@user", user);
    }

    private static IEnumerable<int> ParseRelationBadgeNumbers(string? value)
    {
        foreach (var token in SplitRelationTokens(value))
        {
            var cleaned = token.Trim();
            if (cleaned.StartsWith("GAF", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[3..];
            if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var badge) && badge > 0)
                yield return badge;
        }
    }

    private static long ParseLongOrZero(string? value)
    {
        if (long.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;
        return 0L;
    }

    private async Task<bool> SqlColumnExistsAsync(SqlConnection connection, SqlTransaction transaction, string table, string column)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM {QuoteSqlIdentifier(_sqlSource!.PosDatabase)}.INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @schema
                  AND TABLE_NAME = @tableName
                  AND COLUMN_NAME = @columnName
            ) THEN 1 ELSE 0 END;
            """;
        command.Parameters.AddWithValue("@schema", _sqlSource.PosSchema);
        command.Parameters.AddWithValue("@tableName", table);
        command.Parameters.AddWithValue("@columnName", column);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 1;
    }

    private static string QuoteSqlIdentifier(string identifier) => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    private sealed record AppRecordForRelation(string DriverName, string Hotel, string Site, string Unit, string Plates, string Phone, string Nationality, string TransportType, string Notes, decimal Payout, DateTime? OperationDate, string TaxistaId);
    public async Task<long> SaveBadgeAsync(string number) => await SaveAsync("INSERT INTO LocalGafetes (Numero,Estatus) VALUES ($number,'Disponible') ON CONFLICT(Numero) DO UPDATE SET Numero=excluded.Numero RETURNING Id;", ("$number", Require(number, "El gafete")));
    public async Task SaveBadgeRecordAsync(string staff, string number, string? operationFolio, string user)
    {
        await using var connection = database.Open();
        if (await HasTableAsync(connection, "mkt__dbo__gafete"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "mkt__dbo__gafete" (matricula, gafete, fecha, venta, hora, folioperacion, movimiento, usuario)
                VALUES ($staff,$badge,$date,'A',$activity,$folio,'ENTREGA',$user);
                """;
            command.Parameters.AddWithValue("$staff", string.IsNullOrWhiteSpace(staff) ? string.Empty : staff.Trim());
            command.Parameters.AddWithValue("$badge", Require(number, "El gafete"));
            command.Parameters.AddWithValue("$date", DateTime.Today.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$activity", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$folio", string.IsNullOrWhiteSpace(operationFolio) ? string.Empty : operationFolio.Trim());
            command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(user) ? string.Empty : user.Trim());
            await command.ExecuteNonQueryAsync();
            return;
        }

        await SaveBadgeAsync(number);
    }

    public async Task<LocalBadge?> FindBadgeAsync(string badge)
    {
        var normalized = Require(badge, "El gafete");
        if (_sqlSource is not null)
        {
            try
            {
                var found = await FindBadgeFromSqlServerAsync(normalized);
                if (found is not null) return found;
            }
            catch
            {
                // Si SQL Server no responde, conserva el respaldo local.
            }
        }

        var rows = await GetBadgesAsync(null, null, null, normalized, null);
        return rows.FirstOrDefault(x => string.Equals(x.Number, normalized, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(x => x.Number.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<LocalBadge?> FindBadgeFromSqlServerAsync(string badge)
    {
        if (_sqlSource is null) return null;
        var normalized = Require(badge, "El gafete");
        await using var connection = await _sqlSource.OpenPosAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT TOP (1)
                CONVERT(bigint, ROW_NUMBER() OVER (ORDER BY COALESCE(g.hora, g.fecha) DESC)) AS Id,
                CONVERT(nvarchar(50), g.gafete) AS Numero,
                CASE
                    WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 'OCUPADO'
                    WHEN UPPER(COALESCE(g.venta, '')) = 'S' THEN 'SUSPENDIDO'
                    WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN 'LIBRE'
                    ELSE COALESCE(g.venta, '')
                END AS Estatus,
                CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN '' ELSE CONVERT(nvarchar(50), g.matricula) END AS Staff,
                CONVERT(nvarchar(50), g.folioperacion) AS FolioOperacion,
                CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN '' ELSE COALESCE(a.unidad, '') END AS Unidad,
                CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN '' ELSE COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') END AS Telefono,
                CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN '' ELSE COALESCE(a.nacionalidad, '') END AS Nacionalidad,
                COALESCE(CONVERT(nvarchar(30), g.fecha, 120), '') AS FechaEntrega,
                COALESCE(CONVERT(nvarchar(30), CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN g.hora END, 120), '') AS Regreso
            FROM {_sqlSource.PosTable("gafete")} g
            OUTER APPLY
            (
                SELECT TOP (1)
                    a.unidad,
                    a.telefono_taxista,
                    a.telefono_contacto,
                    a.nacionalidad
                FROM {_sqlSource.PosTable("AppMovilRegistro")} a
                WHERE UPPER(COALESCE(g.venta, '')) <> 'R'
                  AND (
                        UPPER(COALESCE(a.folio_app, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion))
                     OR UPPER(COALESCE(a.folio_app_original, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion))
                     OR (ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)))
                     OR (ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)))
                     OR ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(a.folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
                        LIKE '%,' + CONVERT(nvarchar(300), g.gafete) + ',%'
                  )
                ORDER BY a.fecha_operacion DESC
            ) a
            WHERE CONVERT(nvarchar(50), g.gafete) = @gafete
               OR CONVERT(nvarchar(50), g.gafete) LIKE @gafeteLike
               OR (@gafeteNumerico IS NOT NULL AND g.gafete = @gafeteNumerico)
            ORDER BY
                CASE WHEN CONVERT(nvarchar(50), g.gafete) = @gafete THEN 0 ELSE 1 END,
                CASE UPPER(COALESCE(g.venta, ''))
                    WHEN 'A' THEN 0
                    WHEN 'S' THEN 1
                    WHEN 'R' THEN 2
                    ELSE 3
                END,
                COALESCE(g.hora, g.fecha) DESC;
            """;
        command.Parameters.AddWithValue("@gafete", normalized);
        command.Parameters.AddWithValue("@gafeteLike", normalized + "%");
        // Si el token tiene ceros iniciales (ej. "0278"), intentar también la búsqueda
        // por el valor numérico equivalente (278) porque la columna gafete es int.
        var numericValue = long.TryParse(normalized, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? (object)parsed : DBNull.Value;
        command.Parameters.AddWithValue("@gafeteNumerico", numericValue);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new LocalBadge(
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
            reader.IsDBNull(7) ? string.Empty : reader.GetString(7));
    }

    public async Task AssignBadgeAsync(string number, long driverId, bool returnBadge)
    {
        await using var connection = database.Open(); await using var command = connection.CreateCommand();
        command.CommandText = returnBadge
            ? "UPDATE LocalGafetes SET Estatus='Disponible', TaxistaId=NULL, FechaRegreso=$date WHERE Numero=$number;"
            : "UPDATE LocalGafetes SET Estatus='Asignado', TaxistaId=$driver, FechaAsignacion=$date, FechaRegreso=NULL WHERE Numero=$number AND Estatus='Disponible';";
        command.Parameters.AddWithValue("$number", Require(number, "El gafete")); command.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        if (!returnBadge) command.Parameters.AddWithValue("$driver", driverId);
        if (await command.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException(returnBadge ? "No existe el gafete." : "El gafete no estÃ¡ disponible.");
    }

    public async Task<bool> ReturnImportedBadgeAsync(string number, string? operationFolio, string user)
    {
        if (_sqlSource is not null)
        {
            try
            {
                return await ReturnImportedBadgeFromSqlServerAsync(number, operationFolio, user);
            }
            catch
            {
                // Si el regreso directo falla, conserva el respaldo local.
            }
        }

        await using var connection = database.Open();
        if (!await HasTableAsync(connection, "mkt__dbo__gafete")) return false;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE "mkt__dbo__gafete"
            SET venta='R', hora=$activity, movimiento='REGRESO', usuario=$user
            WHERE CAST(COALESCE(gafete, '') AS TEXT) = $badge
              AND UPPER(COALESCE(venta, '')) IN ('A','S')
              AND ($folio IS NULL OR CAST(COALESCE(folioperacion, '') AS TEXT) = $folio);
            """;
        command.Parameters.AddWithValue("$activity", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(user) ? string.Empty : user.Trim());
        command.Parameters.AddWithValue("$badge", Require(number, "El gafete"));
        command.Parameters.AddWithValue("$folio", string.IsNullOrWhiteSpace(operationFolio) ? DBNull.Value : operationFolio.Trim());
        return await command.ExecuteNonQueryAsync() > 0;
    }

    private async Task<bool> ReturnImportedBadgeFromSqlServerAsync(string number, string? operationFolio, string user)
    {
        if (_sqlSource is null) return false;
        var badge = Require(number, "El gafete");
        var normalizedOperation = string.IsNullOrWhiteSpace(operationFolio) ? null : operationFolio.Trim();
        var folioNumero = long.TryParse(normalizedOperation, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFolio)
            ? parsedFolio
            : (long?)null;
        var now = DateTime.Now;

        await using var connection = await _sqlSource.OpenPosAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            ;WITH target_assignment AS
            (
                SELECT TOP (1)
                    CONVERT(nvarchar(50), folioperacion) AS FolioTexto,
                    CASE
                        WHEN ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
                            THEN CONVERT(bigint, folioperacion)
                        ELSE NULL
                    END AS FolioNumero
                FROM {_sqlSource.PosTable("gafete")}
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
                ORDER BY
                    CASE UPPER(COALESCE(venta, ''))
                        WHEN 'A' THEN 0
                        WHEN 'S' THEN 1
                        ELSE 2
                    END,
                    COALESCE(hora, fecha) DESC
            )
            UPDATE g
            SET venta = 'R',
                hora = @horaRegreso,
                movimiento = 'REGRESO',
                usuario = @usuario
            FROM {_sqlSource.PosTable("gafete")} g
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
            suspendCommand.CommandText = $"""
                UPDATE g
                SET venta = 'S',
                    hora = @horaRegreso,
                    movimiento = 'SUSPENDIDO',
                    usuario = @usuario
                FROM {_sqlSource.PosTable("gafete")} g
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

        return affected > 0;
    }

    public async Task<(int Updated, int NotUpdated)> ReturnImportedBadgesAsync(IEnumerable<string> numbers, string user)
    {
        var updated = 0;
        var notUpdated = 0;
        foreach (var number in numbers.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (await ReturnImportedBadgeAsync(number, null, user)) updated++;
            else notUpdated++;
        }
        return (updated, notUpdated);
    }

    public async Task<(int Updated, int NotUpdated)> ReturnImportedBadgesAsync(IEnumerable<LocalBadgeSelection> selections, string user)
    {
        var updated = 0;
        var notUpdated = 0;
        foreach (var selection in selections
                     .Where(x => !string.IsNullOrWhiteSpace(x.Number))
                     .GroupBy(x => $"{x.Number.Trim().ToUpperInvariant()}|{(x.OperationFolio ?? string.Empty).Trim().ToUpperInvariant()}", StringComparer.OrdinalIgnoreCase)
                     .Select(x => x.First()))
        {
            if (await ReturnImportedBadgeAsync(selection.Number, selection.OperationFolio, user)) updated++;
            else notUpdated++;
        }

        return (updated, notUpdated);
    }
    public async Task<long> SaveRecordAsync(LocalRecord record)
    {
        if (record.Passengers < 1) throw new ArgumentException("Los pasajeros deben ser al menos uno.");
        if (record.Amount < 0) throw new ArgumentException("El importe no puede ser negativo.");
        return await SaveAsync("INSERT INTO LocalRegistros (Folio,Fecha,TaxistaId,HotelId,TarifaId,Gafete,Pax,Origen,Destino,Importe,MetodoPago,Notas,Usuario) VALUES ($folio,$date,$driver,$hotel,$rate,$badge,$pax,$origin,$destination,$amount,$payment,$notes,$user) RETURNING Id;", ("$folio", Require(record.Folio, "El folio")), ("$date", record.Date.ToString("O", CultureInfo.InvariantCulture)), ("$driver", record.DriverId), ("$hotel", record.HotelId), ("$rate", record.RateId), ("$badge", record.Badge), ("$pax", record.Passengers), ("$origin", record.Origin), ("$destination", record.Destination), ("$amount", record.Amount), ("$payment", record.PaymentMethod), ("$notes", record.Notes), ("$user", Require(record.User, "El usuario")));
    }

    public async Task<string> SaveAppRecordAsync(LocalAppRecordInput input)
    {
        if (input.Passengers < 1) throw new ArgumentException("Los pasajeros deben ser al menos uno.");
        if (input.Amount < 0) throw new ArgumentException("La dejada no puede ser negativa.");

        await using var connection = database.Open();
        await EnsureAppMirrorSchemaAsync(connection);
        await using var transaction = connection.BeginTransaction();

        var originalFolio = string.IsNullOrWhiteSpace(input.OriginalFolio)
            ? "WEB" + DateTime.Now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)
            : input.OriginalFolio.Trim().ToUpperInvariant();
        var folioControl = await EnsureAppFolioControlAsync(connection, transaction, originalFolio);
        var badges = SplitBadges(input.Badge);
        var badgeText = string.Join(", ", badges);
        var operationDate = DateTime.Now;

        await ExecuteAsync(connection, transaction, """
            INSERT INTO LocalRegistros (Folio,Fecha,TaxistaId,HotelId,TarifaId,Gafete,Pax,Origen,Destino,Importe,MetodoPago,Notas,Usuario)
            VALUES ($folio,$date,$driver,$hotel,$rate,$badge,$pax,$origin,$destination,$amount,$payment,$notes,$user)
            ON CONFLICT(Folio) DO UPDATE SET
              Fecha=excluded.Fecha,
              TaxistaId=excluded.TaxistaId,
              HotelId=excluded.HotelId,
              TarifaId=excluded.TarifaId,
              Gafete=excluded.Gafete,
              Pax=excluded.Pax,
              Origen=excluded.Origen,
              Destino=excluded.Destino,
              Importe=excluded.Importe,
              MetodoPago=excluded.MetodoPago,
              Notas=excluded.Notas,
              Usuario=excluded.Usuario;
            """,
            ("$folio", folioControl),
            ("$date", operationDate.ToString("O", CultureInfo.InvariantCulture)),
            ("$driver", input.DriverId),
            ("$hotel", await ResolveHotelIdAsync(connection, transaction, input.Hotel)),
            ("$rate", input.RateId),
            ("$badge", badgeText),
            ("$pax", input.Passengers),
            ("$origin", input.Origin),
            ("$destination", input.Destination),
            ("$amount", input.Amount),
            ("$payment", "Efectivo"),
            ("$notes", input.Notes),
            ("$user", Require(input.User, "El usuario")));

        await ExecuteAsync(connection, transaction, """
            DELETE FROM "mkt__dbo__AppMovilRegistro"
            WHERE COALESCE(folio_app,'') = $folioControl OR COALESCE(folio_app_original,'') = $folioOriginal;
            """,
            ("$folioControl", folioControl),
            ("$folioOriginal", originalFolio));

        await ExecuteAsync(connection, transaction, """
            INSERT INTO "mkt__dbo__AppMovilRegistro"
            (folio_app, folio_app_original, id_catalogo, folio_gafete, folio_pos, fecha_operacion, vendedor_nombre, telefono_taxista, telefono_contacto, nacionalidad, placas, modelo_vehiculo, unidad, hotel, origen, sitio, destino, pax, tipo_operacion, total, efectivo, tarjeta, usuario_movil, notas, estado_sync, payout_status, estado_pago_dejada, fecha_pago_dejada, usuario_pago_dejada, ticket_pago_dejada, fecha_creacion)
            VALUES
            ($folioControl,$folioOriginal,$catalogId,$gafete,'',$date,$driver,$phone,$contactPhone,$nationality,$plates,$model,$unit,$hotel,$origin,$site,$destination,$pax,$transportType,$amount,$cash,0,$user,$notes,'SINCRONIZADO','pendiente','pendiente','','','',$created);
            """,
            ("$folioControl", folioControl),
            ("$folioOriginal", originalFolio),
            ("$catalogId", input.DriverId),
            ("$gafete", badgeText),
            ("$date", operationDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("$driver", input.DriverName),
            ("$phone", input.Phone),
            ("$contactPhone", input.ContactPhone),
            ("$nationality", input.Nationality),
            ("$plates", input.Plates),
            ("$model", input.Model),
            ("$unit", input.Unit),
            ("$hotel", input.Hotel),
            ("$origin", input.Origin),
            ("$site", input.Site),
            ("$destination", input.Destination),
            ("$pax", input.Passengers),
            ("$transportType", input.TransportType),
            ("$amount", input.Amount),
            ("$cash", input.Amount),
            ("$user", Require(input.User, "El usuario")),
            ("$notes", input.Notes),
            ("$created", operationDate.ToString("O", CultureInfo.InvariantCulture)));

        await ExecuteAsync(connection, transaction, "DELETE FROM \"mkt__dbo__AppMovilRegistroGafetes\" WHERE FolioApp=$folio;", ("$folio", folioControl));
        foreach (var badge in badges)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO "mkt__dbo__AppMovilRegistroGafetes" (FolioApp, IdCatalogo, FolioGafete, FechaCreacion)
                VALUES ($folio,$catalogId,$badge,$created);
                """,
                ("$folio", folioControl),
                ("$catalogId", input.DriverId),
                ("$badge", badge),
                ("$created", operationDate.ToString("O", CultureInfo.InvariantCulture)));
        }

        await transaction.CommitAsync();
        return folioControl;
    }

    public async Task<LocalReport> GetReportAsync(DateTime start, DateTime end)
    {
        await using var connection = database.Open(); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), COALESCE(SUM(Importe),0), COALESCE(SUM(CASE WHEN upper(MetodoPago)='TARJETA' THEN 0 ELSE Importe END),0), COALESCE(SUM(CASE WHEN upper(MetodoPago)='TARJETA' THEN Importe ELSE 0 END),0) FROM LocalRegistros WHERE Fecha >= $start AND Fecha < $end;";
        command.Parameters.AddWithValue("$start", start.Date.ToString("O", CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("$end", end.Date.AddDays(1).ToString("O", CultureInfo.InvariantCulture));
        await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
        return new LocalReport(start.Date, end.Date, reader.GetInt32(0), Decimal(reader, 1), Decimal(reader, 2), Decimal(reader, 3));
    }

    private Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Func<SqliteDataReader, T> map)
        => ReadAsync(sql, map, Array.Empty<(string Name, object? Value)>());

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
    {
        await using var connection = database.Open();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync())
        {
            result.Add(map(reader));
        }

        return result;
    }
    private async Task<long> SaveAsync(string sql, params (string Name, object? Value)[] parameters)
    { await using var connection = database.Open(); await using var command = connection.CreateCommand(); command.CommandText = sql; foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value); return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture); }
    private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync();
    }
    private static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteScalarAsync();
    }
    private static async Task EnsureAppMirrorSchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS "mkt__dbo__AppMovilFolioControl" (
              FolioAppOriginal TEXT NOT NULL PRIMARY KEY,
              FolioControl TEXT NOT NULL,
              FechaCreacion TEXT NOT NULL DEFAULT '');
            CREATE UNIQUE INDEX IF NOT EXISTS idx_appmovil_foliocontrol_control ON "mkt__dbo__AppMovilFolioControl"(FolioControl);
            CREATE TABLE IF NOT EXISTS "mkt__dbo__AppMovilRegistroGafetes" (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              FolioApp TEXT NOT NULL,
              IdCatalogo INTEGER NULL,
              FolioGafete TEXT NOT NULL,
              FechaCreacion TEXT NOT NULL DEFAULT '');
            CREATE UNIQUE INDEX IF NOT EXISTS idx_appmovil_registrogafetes_unique ON "mkt__dbo__AppMovilRegistroGafetes"(FolioApp, FolioGafete);
            CREATE TABLE IF NOT EXISTS "mkt__dbo__AppMovilRegistro" (
              folio_app TEXT,
              folio_app_original TEXT,
              id_catalogo INTEGER NULL,
              folio_gafete TEXT,
              folio_pos TEXT,
              fecha_operacion TEXT,
              vendedor_nombre TEXT,
              telefono_taxista TEXT,
              telefono_contacto TEXT,
              nacionalidad TEXT,
              placas TEXT,
              modelo_vehiculo TEXT,
              unidad TEXT,
              hotel TEXT,
              origen TEXT,
              sitio TEXT,
              destino TEXT,
              pax INTEGER,
              adult_count INTEGER NOT NULL DEFAULT 0,
              youth_count INTEGER NOT NULL DEFAULT 0,
              minor_count INTEGER NOT NULL DEFAULT 0,
              tipo_operacion TEXT,
              total REAL,
              efectivo REAL,
              tarjeta REAL,
              dolares REAL,
              metodo_pago TEXT,
              usuario_movil TEXT,
              notas TEXT,
              estado_sync TEXT,
              payout_status TEXT,
              estado_pago_dejada TEXT,
              fecha_pago_dejada TEXT,
              usuario_pago_dejada TEXT,
              ticket_pago_dejada TEXT,
              pago_comision REAL NOT NULL DEFAULT 0,
              fecha_pago_comision TEXT,
              fecha_creacion TEXT);
            CREATE INDEX IF NOT EXISTS idx_appmovil_registro_folio_app ON "mkt__dbo__AppMovilRegistro"(folio_app);
            CREATE INDEX IF NOT EXISTS idx_appmovil_registro_folio_original ON "mkt__dbo__AppMovilRegistro"(folio_app_original);
            """;
        await command.ExecuteNonQueryAsync();

        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "adult_count", "INTEGER NOT NULL DEFAULT 0");
        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "youth_count", "INTEGER NOT NULL DEFAULT 0");
        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "minor_count", "INTEGER NOT NULL DEFAULT 0");
        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "no_show_count", "INTEGER NOT NULL DEFAULT 0");
        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "dolares", "REAL NOT NULL DEFAULT 0");
        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "metodo_pago", "TEXT");
        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "pago_comision", "REAL NOT NULL DEFAULT 0");
        await EnsureSqliteColumnAsync(connection, "mkt__dbo__AppMovilRegistro", "fecha_pago_comision", "TEXT");
    }
    private static async Task<string> EnsureAppFolioControlAsync(SqliteConnection connection, SqliteTransaction transaction, string originalFolio)
    {
        var existing = Convert.ToString(await ScalarAsync(connection, transaction, """
            SELECT FolioControl
            FROM "mkt__dbo__AppMovilFolioControl"
            WHERE FolioAppOriginal=$folio;
            """, ("$folio", originalFolio)), CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(existing))
            return existing.Trim();

        if (!originalFolio.StartsWith("WEB", StringComparison.OrdinalIgnoreCase))
        {
            var used = Convert.ToInt64(await ScalarAsync(connection, transaction, """
                SELECT COUNT(*)
                FROM "mkt__dbo__AppMovilRegistro"
                WHERE COALESCE(folio_app,'')=$folio OR COALESCE(folio_app_original,'')=$folio;
                """, ("$folio", originalFolio)), CultureInfo.InvariantCulture);
            if (used == 0)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO "mkt__dbo__AppMovilFolioControl" (FolioAppOriginal, FolioControl, FechaCreacion)
                    VALUES ($original,$control,$created);
                    """,
                    ("$original", originalFolio),
                    ("$control", originalFolio),
                    ("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
                return originalFolio;
            }
        }

        var maxValue = Convert.ToInt32(await ScalarAsync(connection, transaction, """
            SELECT COALESCE(MAX(
                CASE
                  WHEN UPPER(TRIM(COALESCE(FolioTexto,''))) LIKE 'WEB%' AND length(TRIM(COALESCE(FolioTexto,''))) >= 4
                    THEN CAST(SUBSTR(TRIM(FolioTexto), 4) AS INTEGER)
                  WHEN UPPER(TRIM(COALESCE(FolioTexto,''))) LIKE 'AP%' AND length(TRIM(COALESCE(FolioTexto,''))) >= 3
                    THEN CAST(SUBSTR(TRIM(FolioTexto), 3) AS INTEGER)
                  WHEN TRIM(COALESCE(FolioTexto,'')) GLOB '[0-9]*'
                    THEN CAST(TRIM(FolioTexto) AS INTEGER)
                  ELSE 0
                END), 0)
            FROM (
              SELECT FolioControl AS FolioTexto FROM "mkt__dbo__AppMovilFolioControl"
              UNION ALL
              SELECT FolioAppOriginal FROM "mkt__dbo__AppMovilFolioControl"
              UNION ALL
              SELECT folio_app FROM "mkt__dbo__AppMovilRegistro"
              UNION ALL
              SELECT folio_app_original FROM "mkt__dbo__AppMovilRegistro"
            );
            """), CultureInfo.InvariantCulture);
        if (originalFolio.StartsWith("WEB", StringComparison.OrdinalIgnoreCase) && maxValue < 100)
            maxValue = 100;

        var next = maxValue + 1;
        var folioControl = next.ToString("0000", CultureInfo.InvariantCulture);
        while (Convert.ToInt64(await ScalarAsync(connection, transaction, """
            SELECT COUNT(*)
            FROM (
              SELECT FolioControl AS FolioTexto FROM "mkt__dbo__AppMovilFolioControl"
              UNION ALL
              SELECT FolioAppOriginal FROM "mkt__dbo__AppMovilFolioControl"
              UNION ALL
              SELECT folio_app FROM "mkt__dbo__AppMovilRegistro"
              UNION ALL
              SELECT folio_app_original FROM "mkt__dbo__AppMovilRegistro"
            )
            WHERE COALESCE(FolioTexto,'')=$folio;
            """, ("$folio", folioControl)), CultureInfo.InvariantCulture) > 0)
        {
            next++;
            folioControl = next.ToString("0000", CultureInfo.InvariantCulture);
        }

        await ExecuteAsync(connection, transaction, """
            INSERT INTO "mkt__dbo__AppMovilFolioControl" (FolioAppOriginal, FolioControl, FechaCreacion)
            VALUES ($original,$control,$created);
            """,
            ("$original", originalFolio),
            ("$control", folioControl),
            ("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
        return folioControl;
    }
    private static IReadOnlyList<string> SplitBadges(string value) =>
        value.Split([',', ';', '/', '|', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    private static async Task<long?> ResolveHotelIdAsync(SqliteConnection connection, SqliteTransaction transaction, string hotel)
    {
        if (string.IsNullOrWhiteSpace(hotel))
            return null;
        var value = await ScalarAsync(connection, transaction, """
            SELECT Id
            FROM LocalHoteles
            WHERE UPPER(TRIM(Nombre)) = UPPER(TRIM($name))
            LIMIT 1;
            """, ("$name", hotel.Trim()));
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
    private static decimal Decimal(SqliteDataReader row, int index) => row.IsDBNull(index) ? 0m : Convert.ToDecimal(row.GetValue(index), CultureInfo.InvariantCulture);
    private static decimal? NullableDecimal(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : Convert.ToDecimal(row.GetValue(index), CultureInfo.InvariantCulture);
    private static string Text(SqliteDataReader row, int index) => row.IsDBNull(index) ? string.Empty : Convert.ToString(row.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
    private static long? NullableInt64(SqliteDataReader row, int index) => row.IsDBNull(index) ? null : row.GetInt64(index);
    private static string Require(string value, string label) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"{label} es obligatorio.");
    private static async Task EnsureSqliteColumnAsync(SqliteConnection connection, string table, string column, string definition)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = $"PRAGMA table_info(\"{EscapeSqliteIdentifier(table)}\");";
        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(Text(reader, 1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{EscapeSqliteIdentifier(table)}\" ADD COLUMN \"{EscapeSqliteIdentifier(column)}\" {definition};";
        await alter.ExecuteNonQueryAsync();
    }

    private static string EscapeSqliteIdentifier(string value) => value.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<HashSet<string>> GetSqliteColumnsAsync(SqliteConnection connection, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{EscapeSqliteIdentifier(table)}\");";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = Text(reader, 1);
            if (!string.IsNullOrWhiteSpace(name))
                columns.Add(name);
        }

        return columns;
    }

    private static string ExistingTextExpression(IReadOnlySet<string> columns, params string[] names)
    {
        var existing = names
            .Where(columns.Contains)
            .Select(name => $"\"{EscapeSqliteIdentifier(name)}\"")
            .ToArray();
        return existing.Length == 0 ? "''" : $"COALESCE({string.Join(", ", existing)}, '')";
    }

    private static async Task<HashSet<string>> GetSqlServerColumnsAsync(SqlConnection connection, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TOP (0) * FROM {table};";
        await using var reader = await command.ExecuteReaderAsync();
        for (var index = 0; index < reader.FieldCount; index++)
        {
            columns.Add(reader.GetName(index));
        }

        return columns;
    }

    private static string ExistingSqlTextExpression(IReadOnlySet<string> columns, params string[] names)
    {
        var existing = names
            .Where(columns.Contains)
            .Select(name => $"CONVERT(nvarchar(max), [{name.Replace("]", "]]", StringComparison.Ordinal)}])")
            .ToArray();
        return existing.Length == 0 ? "N''" : $"COALESCE({string.Join(", ", existing)}, N'')";
    }
}
