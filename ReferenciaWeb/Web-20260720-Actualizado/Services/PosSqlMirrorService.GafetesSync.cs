using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        private async Task SyncGafetesOperacionAsync(long folioOperacion, string usuario)
        {
            var normalizedFolio = Normalize(folioOperacion.ToString()) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedFolio))
                return;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await EnsureAppMovilMirrorSchemaAsync(connection);
            var badges = await GetActiveBadgeTokensForOperacionAsync(connection, normalizedFolio);
            var badgeText = string.Join(", ", badges);
            var relatedTrips = await GetRelatedTripRowsForOperacionAsync(connection, normalizedFolio);
            var badgeStates = await GetBadgeStatesForOperacionAsync(connection, normalizedFolio, relatedTrips);
            if (relatedTrips.Count == 0)
                return;

            foreach (var trip in relatedTrips)
            {
                await UpdateLocalTripBadgesAsync(connection, trip, badgeText, badges);
                var remoteRecordId = !string.IsNullOrWhiteSpace(trip.FolioAppOriginal)
                    ? trip.FolioAppOriginal
                    : trip.FolioApp;
                if (!string.IsNullOrWhiteSpace(remoteRecordId))
                {
                    try
                    {
                        await _appTaxiApi.UpdateTripBadgesAsync(remoteRecordId, badges);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudieron reflejar gafetes del folio {FolioOperacion} hacia la API movil.", normalizedFolio);
                    }
                }
            }

            if (badgeStates.Count > 0)
            {
                try
                {
                    await _appTaxiApi.PushBadgeStatesAsync(badgeStates);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudieron reflejar estados de gafetes del folio {FolioOperacion} hacia la API movil.", normalizedFolio);
                }
            }
        }

        private async Task<List<string>> GetActiveBadgeTokensForOperacionAsync(DbConnection connection, string folioOperacion)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
;WITH movimientos AS
(
    SELECT
        CONVERT(nvarchar(20), gafete) AS Gafete,
        UPPER(COALESCE(venta, '')) AS Venta,
        ROW_NUMBER() OVER
        (
            PARTITION BY CONVERT(nvarchar(20), gafete)
            ORDER BY
                COALESCE(hora, fecha) DESC,
                CASE UPPER(COALESCE(venta, ''))
                    WHEN 'R' THEN 0
                    WHEN 'S' THEN 1
                    WHEN 'A' THEN 2
                    ELSE 3
                END
        ) AS rn
    FROM {PosTable("gafete")}
    WHERE (
            CONVERT(nvarchar(50), folioperacion) = @folioOperacion
         OR (
                ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
            AND ISNUMERIC(@folioOperacion) = 1
            AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folioOperacion)
            )
          )
)
SELECT Gafete
FROM movimientos
WHERE rn = 1
  AND Venta = 'A'
ORDER BY Gafete;";
            AddParameter(command, "@folioOperacion", folioOperacion);
            var rows = await ReadRowsAsync(command);
            return rows
                .Select(x => PickText(x, "Gafete"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<RelatedTripBadgeRow>> GetRelatedTripRowsForOperacionAsync(DbConnection connection, string folioOperacion)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
WITH trip_candidates AS
(
    SELECT
        COALESCE(NULLIF(a.folio_app, ''), NULLIF(r.FolioApp, ''), '') AS FolioApp,
        COALESCE(NULLIF(a.folio_app_original, ''), NULLIF(r.FolioApp, ''), '') AS FolioAppOriginal,
        COALESCE(a.id_catalogo, 0) AS CatalogId
    FROM {PosTable("RelacionTicketTaxista")} r
    LEFT JOIN {PosTable("AppMovilRegistro")} a
        ON a.folio_app = r.FolioApp OR a.folio_app_original = r.FolioApp
    WHERE UPPER(COALESCE(r.FolioOperacion, '')) = UPPER(@folioOperacion)
       OR (
            ISNUMERIC(COALESCE(r.FolioOperacion, '')) = 1
        AND ISNUMERIC(@folioOperacion) = 1
        AND CONVERT(bigint, r.FolioOperacion) = CONVERT(bigint, @folioOperacion)
          )

    UNION ALL

    SELECT
        COALESCE(NULLIF(a.folio_app, ''), '') AS FolioApp,
        COALESCE(NULLIF(a.folio_app_original, ''), '') AS FolioAppOriginal,
        COALESCE(a.id_catalogo, 0) AS CatalogId
    FROM {PosTable("AppMovilRegistro")} a
    LEFT JOIN {PosTable("AppMovilFolioControl")} c
        ON c.FolioControl = a.folio_app
        OR c.FolioControl = a.folio_app_original
        OR c.FolioAppOriginal = a.folio_app
        OR c.FolioAppOriginal = a.folio_app_original
    WHERE
           UPPER(COALESCE(a.folio_app, '')) = UPPER(@folioOperacion)
        OR UPPER(COALESCE(a.folio_app_original, '')) = UPPER(@folioOperacion)
        OR UPPER(COALESCE(c.FolioControl, '')) = UPPER(@folioOperacion)
        OR UPPER(COALESCE(c.FolioAppOriginal, '')) = UPPER(@folioOperacion)
        OR (
               ISNUMERIC(COALESCE(a.folio_app, '')) = 1
           AND ISNUMERIC(@folioOperacion) = 1
           AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, @folioOperacion)
           )
        OR (
               ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1
           AND ISNUMERIC(@folioOperacion) = 1
           AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, @folioOperacion)
           )
        OR (
               ISNUMERIC(COALESCE(c.FolioControl, '')) = 1
           AND ISNUMERIC(@folioOperacion) = 1
           AND CONVERT(bigint, c.FolioControl) = CONVERT(bigint, @folioOperacion)
           )
        OR (
               ISNUMERIC(COALESCE(c.FolioAppOriginal, '')) = 1
           AND ISNUMERIC(@folioOperacion) = 1
           AND CONVERT(bigint, c.FolioAppOriginal) = CONVERT(bigint, @folioOperacion)
           )
)
SELECT DISTINCT FolioApp, FolioAppOriginal, CatalogId
FROM trip_candidates
WHERE NULLIF(FolioApp, '') IS NOT NULL
   OR NULLIF(FolioAppOriginal, '') IS NOT NULL;";
            AddParameter(command, "@folioOperacion", folioOperacion);
            var rows = await ReadRowsAsync(command);
            return rows
                .Select(x => new RelatedTripBadgeRow(PickText(x, "FolioApp"), PickText(x, "FolioAppOriginal"), PickInt(x, "CatalogId")))
                .Where(x => !string.IsNullOrWhiteSpace(x.FolioApp) || !string.IsNullOrWhiteSpace(x.FolioAppOriginal))
                .GroupBy(x => $"{x.FolioApp}|{x.FolioAppOriginal}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private async Task<List<Interfaces.AppTaxiBadgeState>> GetBadgeStatesForOperacionAsync(
            DbConnection connection,
            string folioOperacion,
            IReadOnlyCollection<RelatedTripBadgeRow> relatedTrips)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
;WITH movimientos AS
(
    SELECT
        CONVERT(nvarchar(20), gafete) AS Gafete,
        UPPER(COALESCE(venta, '')) AS Venta,
        ROW_NUMBER() OVER
        (
            PARTITION BY CONVERT(nvarchar(20), gafete)
            ORDER BY
                COALESCE(hora, fecha) DESC,
                CASE UPPER(COALESCE(venta, ''))
                    WHEN 'R' THEN 0
                    WHEN 'S' THEN 1
                    WHEN 'A' THEN 2
                    ELSE 3
                END
        ) AS rn
    FROM {PosTable("gafete")}
    WHERE (
            CONVERT(nvarchar(50), folioperacion) = @folioOperacion
         OR (
                ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
            AND ISNUMERIC(@folioOperacion) = 1
            AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folioOperacion)
            )
          )
)
SELECT Gafete, Venta
FROM movimientos
WHERE rn = 1
  AND NULLIF(LTRIM(RTRIM(Gafete)), '') IS NOT NULL
ORDER BY Gafete;";
            AddParameter(command, "@folioOperacion", folioOperacion);
            var rows = await ReadRowsAsync(command);

            var assignedTrip = relatedTrips.FirstOrDefault(x => x.CatalogId > 0);
            var items = new List<Interfaces.AppTaxiBadgeState>();
            foreach (var row in rows)
            {
                var badge = PickText(row, "Gafete");
                var venta = PickText(row, "Venta").ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(badge))
                    continue;

                var isAssigned = venta == "A";
                var remoteStatus = venta switch
                {
                    "A" => "Asignado",
                    "R" => "R",
                    "S" => "S",
                    _ => "Disponible"
                };
                items.Add(new Interfaces.AppTaxiBadgeState(
                    badge,
                    remoteStatus,
                    1,
                    isAssigned ? assignedTrip?.CatalogId : null,
                    string.Empty));
            }

            return items
                .GroupBy(x => $"{x.BadgeId}|{x.Cycle}", StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
        }

        private async Task UpdateLocalTripBadgesAsync(DbConnection connection, RelatedTripBadgeRow trip, string badgeText, IReadOnlyCollection<string> badges)
        {
            await using (var update = connection.CreateCommand())
            {
                update.CommandText = $@"
UPDATE {PosTable("AppMovilRegistro")}
SET folio_gafete = @gafetes
WHERE folio_app = @folioApp
   OR folio_app_original = @folioApp
   OR (@folioAppOriginal <> '' AND (folio_app = @folioAppOriginal OR folio_app_original = @folioAppOriginal));";
                AddParameter(update, "@gafetes", SafeText(badgeText, 300));
                AddParameter(update, "@folioApp", trip.FolioApp);
                AddParameter(update, "@folioAppOriginal", trip.FolioAppOriginal);
                await ExecuteNonQueryAsync(update);
            }

            await using (var relation = connection.CreateCommand())
            {
                relation.CommandText = $@"
UPDATE {PosTable("RelacionTicketTaxista")}
SET Gafete = @gafetes,
    FechaActualizacion = SYSDATETIME()
WHERE FolioApp = @folioApp
   OR (@folioAppOriginal <> '' AND FolioApp = @folioAppOriginal);";
                AddParameter(relation, "@gafetes", SafeText(badgeText, 300));
                AddParameter(relation, "@folioApp", trip.FolioApp);
                AddParameter(relation, "@folioAppOriginal", trip.FolioAppOriginal);
                await ExecuteNonQueryAsync(relation);
            }

            if (badges.Count == 0)
                return;

            await using var insert = connection.CreateCommand();
            insert.CommandText = $@"
INSERT INTO {PosTable("AppMovilRegistroGafetes")} (FolioApp, IdCatalogo, FolioGafete)
SELECT @folioApp, @catalogId, @gafete
WHERE NOT EXISTS (
    SELECT 1
    FROM {PosTable("AppMovilRegistroGafetes")}
    WHERE FolioApp = @folioApp
      AND UPPER(LTRIM(RTRIM(FolioGafete))) = UPPER(LTRIM(RTRIM(@gafete)))
);";
            AddParameter(insert, "@folioApp", trip.FolioApp);
            AddParameter(insert, "@catalogId", trip.CatalogId > 0 ? trip.CatalogId : null);
            var badgeParameter = insert.CreateParameter();
            badgeParameter.ParameterName = "@gafete";
            insert.Parameters.Add(badgeParameter);

            foreach (var badge in badges)
            {
                badgeParameter.Value = SafeText(badge, 20);
                await ExecuteNonQueryAsync(insert);
            }
        }

        private sealed record RelatedTripBadgeRow(string FolioApp, string FolioAppOriginal, int CatalogId);
    }
}
