using System.Data;
using System.Data.Common;
using System.Globalization;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        public async Task<bool> InsertGafeteAsync(int matricula, string gafete, long? folioOperacion, string movimiento, string usuario)
        {
            var gafetes = SplitGafeteNumbers(gafete)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (matricula <= 0 || gafetes.Count == 0)
                return false;

            var movimientoFinal = string.IsNullOrWhiteSpace(movimiento) ? "A" : movimiento.Trim()[0].ToString();
            var connection = _posContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            var now = DateTime.Now;
            var fechaBase = now.Date;
            foreach (var token in gafetes)
            {
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
                    continue;

                if (string.Equals(movimientoFinal, "A", StringComparison.OrdinalIgnoreCase))
                {
                    await SuspendOtherActiveBadgeAssignmentsAsync(
                        connection,
                        numero.ToString(CultureInfo.InvariantCulture),
                        folioOperacion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                        folioOperacion,
                        fechaBase,
                        now);
                }

                await _posContext.Gafetes.AddAsync(new PosGafete
                {
                    Matricula = matricula,
                    Numero = numero,
                    Fecha = DateTime.Today,
                    Venta = movimientoFinal,
                    Hora = DateTime.Now,
                    FolioOperacion = folioOperacion
                });
            }
            await _posContext.SaveChangesAsync();

            if (folioOperacion.HasValue)
                await SyncGafetesOperacionAsync(folioOperacion.Value, usuario);

            await InsertPosAuditoriaAsync(usuario, "Gafetes", "Guardar", string.Join(", ", gafetes), $"Gafete asignado a staff {matricula}",
                BuildAuditJson(("Matricula", matricula), ("Gafete", string.Join(", ", gafetes)), ("FolioOperacion", folioOperacion), ("Movimiento", movimientoFinal)));
            return true;
        }

        public async Task<bool> UpdateGafeteAsync(
            int? matriculaOriginal,
            string? gafeteOriginal,
            long? folioOperacionOriginal,
            int matricula,
            string gafete,
            long? folioOperacion,
            string movimiento,
            string usuario)
        {
            var gafetes = SplitGafeteNumbers(gafete)
                .Select(NormalizeBadgeToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            var gafetesOriginales = SplitGafeteNumbers(gafeteOriginal ?? string.Empty)
                .Select(NormalizeBadgeToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (matricula <= 0 || gafetes.Count == 0)
                return false;

            var movimientoFinal = string.IsNullOrWhiteSpace(movimiento) ? "A" : movimiento.Trim()[0].ToString();
            var folioOperacionFinal = folioOperacion ?? folioOperacionOriginal;
            var folioOperacionTexto = folioOperacionFinal?.ToString(CultureInfo.InvariantCulture) ?? folioOperacionOriginal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            var connection = _posContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            var now = DateTime.Now;
            var fechaBase = now.Date;
            var targetMatricula = matriculaOriginal.GetValueOrDefault(matricula);
            var originalTokens = gafetesOriginales
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var newTokens = gafetes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targetMatricula <= 0 || string.IsNullOrWhiteSpace(folioOperacionTexto) || originalTokens.Count == 0 || newTokens.Count == 0)
                return false;

            async Task<int> UpdateBadgeAsync(string originalBadge, string newBadge, bool includeMatricula)
            {
                await using var update = connection.CreateCommand();
                update.CommandText = $@"
UPDATE TOP (1) {PosTable("gafete")}
SET gafete = @gafeteNuevo,
    fecha = CONVERT(smalldatetime, @fechaBase),
    hora = @hora,
    venta = 'A',
    folioperacion = @folioOperacion
WHERE (
        CONVERT(nvarchar(50), folioperacion) = @folioOperacion
     OR (
            ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
        AND ISNUMERIC(@folioOperacion) = 1
        AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folioOperacion)
        )
      )
  AND CONVERT(nvarchar(50), gafete) = @gafeteOriginal
  AND (UPPER(COALESCE(venta, '')) = 'A' OR UPPER(COALESCE(venta, '')) = 'S')"
                + (includeMatricula
                    ? @"
  AND CONVERT(nvarchar(50), matricula) = CONVERT(nvarchar(50), @matriculaOriginal);"
                    : ";");
                AddParameter(update, "@folioOperacion", folioOperacionTexto);
                if (includeMatricula)
                    AddParameter(update, "@matriculaOriginal", targetMatricula);
                AddParameter(update, "@gafeteOriginal", originalBadge);
                AddParameter(update, "@gafeteNuevo", newBadge);
                AddParameter(update, "@fechaBase", fechaBase);
                AddParameter(update, "@hora", now);
                return await ExecuteNonQueryAsync(update);
            }

            async Task<int> SuspendBadgeAsync(string oldBadge, bool includeMatricula)
            {
                await using var suspend = connection.CreateCommand();
                suspend.CommandText = $@"
UPDATE TOP (1) {PosTable("gafete")}
SET venta = 'S',
    fecha = CONVERT(smalldatetime, @fechaBase),
    hora = @hora
WHERE (
        CONVERT(nvarchar(50), folioperacion) = @folioOperacion
     OR (
            ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
        AND ISNUMERIC(@folioOperacion) = 1
        AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folioOperacion)
        )
      )
  AND CONVERT(nvarchar(50), gafete) = @gafete
  AND (UPPER(COALESCE(venta, '')) = 'A' OR UPPER(COALESCE(venta, '')) = 'S')"
                + (includeMatricula
                    ? @"
  AND CONVERT(nvarchar(50), matricula) = CONVERT(nvarchar(50), @matriculaOriginal);"
                    : ";");
                AddParameter(suspend, "@folioOperacion", folioOperacionTexto);
                if (includeMatricula)
                    AddParameter(suspend, "@matriculaOriginal", targetMatricula);
                AddParameter(suspend, "@gafete", oldBadge);
                AddParameter(suspend, "@fechaBase", fechaBase);
                AddParameter(suspend, "@hora", now);
                return await ExecuteNonQueryAsync(suspend);
            }

            async Task InsertAddedBadgeAsync(string newBadge)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = $@"
INSERT INTO {PosTable("gafete")} (matricula, gafete, fecha, venta, hora, folioperacion)
SELECT @matricula, @gafete, CONVERT(smalldatetime, @fechaBase), @movimiento, @hora, @folioOperacionNumero
WHERE NOT EXISTS (
    SELECT 1
    FROM {PosTable("gafete")}
    WHERE (
            CONVERT(nvarchar(50), folioperacion) = @folioOperacion
         OR (
                ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
            AND ISNUMERIC(@folioOperacion) = 1
            AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folioOperacion)
            )
          )
      AND CONVERT(nvarchar(50), gafete) = @gafete
      AND UPPER(COALESCE(venta, '')) = 'A'
);";
                AddParameter(insert, "@matricula", matricula);
                AddParameter(insert, "@gafete", newBadge);
                AddParameter(insert, "@fechaBase", fechaBase);
                AddParameter(insert, "@movimiento", movimientoFinal);
                AddParameter(insert, "@hora", now);
                AddParameter(insert, "@folioOperacion", folioOperacionTexto);
                AddParameter(insert, "@folioOperacionNumero", folioOperacionFinal);
                await ExecuteNonQueryAsync(insert);
            }

            var changeCount = Math.Min(originalTokens.Count, newTokens.Count);
            for (var index = 0; index < changeCount; index++)
            {
                var originalBadge = originalTokens[index];
                var newBadge = newTokens[index];
                if (string.Equals(originalBadge, newBadge, StringComparison.OrdinalIgnoreCase))
                    continue;

                var affected = await UpdateBadgeAsync(originalBadge, newBadge, includeMatricula: true);
                if (affected == 0)
                    await UpdateBadgeAsync(originalBadge, newBadge, includeMatricula: false);
            }

            for (var index = changeCount; index < originalTokens.Count; index++)
            {
                var oldBadge = originalTokens[index];
                var affected = await SuspendBadgeAsync(oldBadge, includeMatricula: true);
                if (affected == 0)
                    await SuspendBadgeAsync(oldBadge, includeMatricula: false);
            }

            for (var index = changeCount; index < newTokens.Count; index++)
            {
                await InsertAddedBadgeAsync(newTokens[index]);
            }

            foreach (var newBadge in newTokens)
            {
                await SuspendOtherActiveBadgeAssignmentsAsync(
                    connection,
                    newBadge,
                    folioOperacionTexto,
                    folioOperacionFinal,
                    fechaBase,
                    now);
            }

            await SyncGafetesOperacionAsync(folioOperacionFinal ?? folioOperacionOriginal ?? 0, usuario);

            await InsertPosAuditoriaAsync(usuario, "Gafetes", "Editar", string.Join(", ", gafetes), $"Gafete editado para staff {matricula}",
                BuildAuditJson(
                    ("MatriculaOriginal", matriculaOriginal),
                    ("GafeteOriginal", string.Join(", ", gafetesOriginales)),
                    ("FolioOperacionOriginal", folioOperacionOriginal),
                    ("MatriculaNueva", matricula),
                    ("GafeteNuevo", string.Join(", ", gafetes)),
                    ("FolioOperacionNuevo", folioOperacion),
                    ("Movimiento", movimientoFinal)));
            return true;
        }

        public async Task<bool> MarcarRegresoGafeteAsync(string gafete, long? folioOperacion, string usuario)
        {
            var gafeteText = NormalizeBadgeToken(Normalize(gafete) ?? string.Empty);
            if (string.IsNullOrWhiteSpace(gafeteText))
                return false;

            var now = DateTime.Now;
            var connection = _posContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            // Mark every active row for the selected badge/folio as returned, preserving
            // the original folio and matricula so the history remains visible and the
            // synchronizer can respect the latest movement.
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
;WITH target_assignment AS
(
    SELECT TOP (1)
        CONVERT(nvarchar(50), folioperacion) AS FolioTexto,
        CASE
            WHEN ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
                THEN CONVERT(bigint, folioperacion)
            ELSE NULL
        END AS FolioNumero
    FROM {PosTable("gafete")}
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
FROM {PosTable("gafete")} g
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
      );";
            AddParameter(command, "@gafeteText", gafeteText);
            AddParameter(command, "@folioOperacion", folioOperacion);
            AddParameter(command, "@folioOperacionTexto", folioOperacion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            AddParameter(command, "@folioOperacionNumero", folioOperacion);
            AddParameter(command, "@horaRegreso", now);
            AddParameter(command, "@usuario", SafeText(usuario, 160));
            var affected = await ExecuteNonQueryAsync(command);

            if (affected > 0)
            {
                await using var suspendOthers = connection.CreateCommand();
                suspendOthers.CommandText = $@"
UPDATE g
SET venta = 'S',
    hora = @horaRegreso,
    movimiento = 'SUSPENDIDO',
    usuario = @usuario
FROM {PosTable("gafete")} g
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
      );";
                AddParameter(suspendOthers, "@gafeteText", gafeteText);
                AddParameter(suspendOthers, "@folioOperacion", folioOperacion);
                AddParameter(suspendOthers, "@folioOperacionTexto", folioOperacion?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                AddParameter(suspendOthers, "@folioOperacionNumero", folioOperacion);
                AddParameter(suspendOthers, "@horaRegreso", now);
                AddParameter(suspendOthers, "@usuario", SafeText(usuario, 160));
                await ExecuteNonQueryAsync(suspendOthers);
            }

            if (affected > 0 && folioOperacion.HasValue)
            {
                try { await SyncGafetesOperacionAsync(folioOperacion.Value, usuario); }
                catch (Exception ex) { _logger.LogWarning(ex, "Sync de gafetes fallo para folio {Folio}", folioOperacion.Value); }
            }

            try { await InsertPosAuditoriaAsync(usuario, "Gafetes", "Regreso", gafeteText, "Gafete devuelto",
                BuildAuditJson(("Gafete", gafeteText), ("FolioOperacion", folioOperacion))); }
            catch { /* auditoria no crítica */ }
            return affected > 0;
        }

        public async Task<(int Actualizados, int NoActualizados)> MarcarRegresoGafetesAsync(
            string gafetes,
            long? folioOperacion,
            string usuario)
        {
            var lista = SplitGafeteNumbers(gafetes)
                .Select(NormalizeBadgeToken)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var actualizados = 0;
            foreach (var gafete in lista)
            {
                if (await MarcarRegresoGafeteAsync(gafete, folioOperacion, usuario))
                    actualizados++;
            }

            return (actualizados, lista.Count - actualizados);
        }



public async Task<PosGafetesViewModel?> TryGetGafetesAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, bool soloSinFecha = false)
        {
            try
            {
                var inicio = fechaInicio?.Date ?? DateTime.Today;
                var fin = fechaFin?.Date ?? inicio;
                var rows = await GetGafetesRowsFromSqlAsync(inicio, fin, soloSinFecha);
                if (rows.Count == 0)
                    return new PosGafetesViewModel { FechaInicio = inicio, FechaFin = fin };

                var model = new PosGafetesViewModel
                {
                    FechaInicio = inicio,
                    FechaFin = fin
                };
                foreach (var row in rows)
                {
                    model.Filas.Add(new List<string>
                    {
                        row.Numero,
                        row.Staff,
                        row.FolioOperacion,
                        row.Unidad,
                        row.Telefono,
                        row.Nacionalidad,
                        row.FechaEntrega.HasValue ? FormatDate(row.FechaEntrega) : "SIN FECHA",
                        row.Estatus,
                        row.Regreso
                    });
                }

                return model;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Gafetes desde POS.");
                return null;
            }
        }

        public async Task<ControlTaxiWeb.Models.PosTriton.PosGafeteRowViewModel?> TryFindGafeteAsync(string gafete)
        {
            try
            {
                var normalized = NormalizeBadgeToken(Normalize(gafete) ?? string.Empty);
                if (string.IsNullOrWhiteSpace(normalized))
                    return null;

                var fastRow = await FindGafeteCoreAsync(normalized);
                if (fastRow != null)
                    return fastRow;

                var rowsExact = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1)
    CONVERT(nvarchar(50), g.gafete) AS Numero,
    CONVERT(nvarchar(50), g.matricula) AS Staff,
    CONVERT(nvarchar(50), g.folioperacion) AS FolioOperacion,
    CASE
        WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 'OCUPADO'
        WHEN UPPER(COALESCE(g.venta, '')) = 'S' THEN 'SUSPENDIDO'
        WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN 'LIBRE'
        ELSE COALESCE(g.venta, '')
    END AS Estatus,
    CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN g.hora ELSE NULL END AS Regreso,
    COALESCE(a.unidad, '') AS Unidad,
    COALESCE(a.telefono_taxista, '') AS Telefono,
    COALESCE(a.nacionalidad, '') AS Nacionalidad
FROM {PosTable("gafete")} g
OUTER APPLY (
    SELECT TOP (1)
        unidad,
        telefono_taxista,
        nacionalidad
    FROM {PosTable("AppMovilRegistro")} a
    WHERE UPPER(COALESCE(g.venta, '')) <> 'R'
      AND (
            UPPER(COALESCE(a.folio_app, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion))
         OR UPPER(COALESCE(a.folio_app_original, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion))
         OR (ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)))
         OR (ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)))
         OR ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(a.folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
            LIKE '%,' + CONVERT(nvarchar(300), g.gafete) + ',%'
          )
    ORDER BY
        CASE
            WHEN UPPER(COALESCE(a.folio_app, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion)) THEN 0
            WHEN UPPER(COALESCE(a.folio_app_original, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion)) THEN 1
            WHEN ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)) THEN 2
            WHEN ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)) THEN 3
            ELSE 10
        END,
        fecha_operacion DESC
) a
WHERE CONVERT(nvarchar(50), g.gafete) = @gafeteExact
ORDER BY
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM {PosTable("gafete")} g2
            WHERE CONVERT(nvarchar(50), g2.gafete) = CONVERT(nvarchar(50), g.gafete)
              AND UPPER(COALESCE(g2.venta, '')) = 'A'
        ) THEN
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
    COALESCE(g.hora, g.fecha) DESC",
                    ("@gafeteExact", normalized));

                var rowExact = rowsExact.FirstOrDefault();
                if (rowExact != null)
                {
                    var estatus = PickText(rowExact, "Estatus");
                    var esLibre = string.Equals(estatus, "LIBRE", StringComparison.OrdinalIgnoreCase);
                    return new ControlTaxiWeb.Models.PosTriton.PosGafeteRowViewModel
                    {
                        Numero = PickText(rowExact, "Numero"),
                        Staff = esLibre ? string.Empty : PickText(rowExact, "Staff"),
                        FolioOperacion = PickText(rowExact, "FolioOperacion"),
                        Estatus = estatus,
                        Regreso = ToDate(PickObject(rowExact, "Regreso")),
                        Unidad = esLibre ? string.Empty : PickText(rowExact, "Unidad"),
                        Telefono = esLibre ? string.Empty : PickText(rowExact, "Telefono"),
                        Nacionalidad = esLibre ? string.Empty : PickText(rowExact, "Nacionalidad")
                    };
                }

                // Fallback: do a LIKE search (use prefix match to be faster)
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1)
    CONVERT(nvarchar(50), g.gafete) AS Numero,
    CONVERT(nvarchar(50), g.matricula) AS Staff,
    CONVERT(nvarchar(50), g.folioperacion) AS FolioOperacion,
    CASE
        WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 'OCUPADO'
        WHEN UPPER(COALESCE(g.venta, '')) = 'S' THEN 'SUSPENDIDO'
        WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN 'LIBRE'
        ELSE COALESCE(g.venta, '')
    END AS Estatus,
    CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN g.hora ELSE NULL END AS Regreso,
    COALESCE(a.unidad, '') AS Unidad,
    COALESCE(a.telefono_taxista, '') AS Telefono,
    COALESCE(a.nacionalidad, '') AS Nacionalidad
FROM {PosTable("gafete")} g
OUTER APPLY (
    SELECT TOP (1)
        unidad,
        telefono_taxista,
        nacionalidad
    FROM {PosTable("AppMovilRegistro")} a
    WHERE UPPER(COALESCE(g.venta, '')) <> 'R'
      AND (
            UPPER(COALESCE(a.folio_app, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion))
         OR UPPER(COALESCE(a.folio_app_original, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion))
         OR (ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)))
         OR (ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)))
         OR ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(a.folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
            LIKE '%,' + CONVERT(nvarchar(300), g.gafete) + ',%'
          )
    ORDER BY
        CASE
            WHEN UPPER(COALESCE(a.folio_app, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion)) THEN 0
            WHEN UPPER(COALESCE(a.folio_app_original, '')) = UPPER(CONVERT(nvarchar(60), g.folioperacion)) THEN 1
            WHEN ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)) THEN 2
            WHEN ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(COALESCE(CONVERT(nvarchar(60), g.folioperacion), '')) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, CONVERT(nvarchar(60), g.folioperacion)) THEN 3
            ELSE 10
        END,
        fecha_operacion DESC
) a
WHERE CONVERT(nvarchar(50), g.gafete) LIKE @gafeteLike
ORDER BY
    CASE
        WHEN EXISTS (
            SELECT 1
            FROM {PosTable("gafete")} g2
            WHERE CONVERT(nvarchar(50), g2.gafete) = CONVERT(nvarchar(50), g.gafete)
              AND UPPER(COALESCE(g2.venta, '')) = 'A'
        ) THEN
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
    COALESCE(g.hora, g.fecha) DESC",
                    ("@gafeteLike", $"{normalized}%"));

                var row = rows.FirstOrDefault();
                if (row == null)
                    return await FindGafeteCoreAsync(normalized);

                var estatusFallback = PickText(row, "Estatus");
                var esLibreFallback = string.Equals(estatusFallback, "LIBRE", StringComparison.OrdinalIgnoreCase);
                return new ControlTaxiWeb.Models.PosTriton.PosGafeteRowViewModel
                {
                    Numero = PickText(row, "Numero"),
                    Staff = esLibreFallback ? string.Empty : PickText(row, "Staff"),
                    FolioOperacion = PickText(row, "FolioOperacion"),
                    Estatus = estatusFallback,
                    Regreso = ToDate(PickObject(row, "Regreso")),
                    Unidad = esLibreFallback ? string.Empty : PickText(row, "Unidad"),
                    Telefono = esLibreFallback ? string.Empty : PickText(row, "Telefono"),
                    Nacionalidad = esLibreFallback ? string.Empty : PickText(row, "Nacionalidad")
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible buscar gafete {Gafete} con datos de app movil; intentando busqueda directa.", gafete);
                try
                {
                    var normalized = NormalizeBadgeToken(Normalize(gafete) ?? string.Empty);
                    return string.IsNullOrWhiteSpace(normalized) ? null : await FindGafeteCoreAsync(normalized);
                }
                catch (Exception ex2)
                {
                    _logger.LogWarning(ex2, "No fue posible buscar gafete {Gafete}.", gafete);
                    return null;
                }
            }
        }

        // Busqueda directa contra la tabla de gafetes (sin joins fragiles). Se usa como
        // respaldo para que un gafete escaneado/escrito siempre se encuentre aunque el
        // enriquecimiento con AppMovil falle o no devuelva nada.
        private async Task<ControlTaxiWeb.Models.PosTriton.PosGafeteRowViewModel?> FindGafeteCoreAsync(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1)
    CONVERT(nvarchar(50), g.gafete) AS Numero,
    CONVERT(nvarchar(50), g.matricula) AS Staff,
    CONVERT(nvarchar(50), g.folioperacion) AS FolioOperacion,
    CASE
        WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 'OCUPADO'
        WHEN UPPER(COALESCE(g.venta, '')) = 'S' THEN 'SUSPENDIDO'
        WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN 'LIBRE'
        ELSE COALESCE(g.venta, '')
    END AS Estatus,
    CASE WHEN UPPER(COALESCE(g.venta, '')) = 'R' THEN g.hora ELSE NULL END AS Regreso
FROM {PosTable("gafete")} g
WHERE CONVERT(nvarchar(50), g.gafete) = @gafete
   OR CONVERT(nvarchar(50), g.gafete) LIKE @gafeteLike
ORDER BY
    CASE WHEN CONVERT(nvarchar(50), g.gafete) = @gafete THEN 0 ELSE 1 END,
    CASE UPPER(COALESCE(g.venta, ''))
        WHEN 'A' THEN 0
        WHEN 'S' THEN 1
        WHEN 'R' THEN 2
        ELSE 3
    END,
    COALESCE(g.hora, g.fecha) DESC",
                ("@gafete", normalized),
                ("@gafeteLike", $"{normalized}%"));

            var row = rows.FirstOrDefault();
            if (row == null)
                return null;

            var estatus = PickText(row, "Estatus");
            var esLibre = string.Equals(estatus, "LIBRE", StringComparison.OrdinalIgnoreCase);
            var model = new ControlTaxiWeb.Models.PosTriton.PosGafeteRowViewModel
            {
                Numero = PickText(row, "Numero"),
                Staff = esLibre ? string.Empty : PickText(row, "Staff"),
                FolioOperacion = PickText(row, "FolioOperacion"),
                Estatus = estatus,
                Regreso = ToDate(PickObject(row, "Regreso"))
            };

            return model;
        }

        private async Task<List<PosGafeteEfRow>> GetGafetesRowsFromSqlAsync(DateTime? fechaInicio, DateTime? fechaFin, bool soloSinFecha)
        {
            try
            {
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
WITH badge_state AS
(
    SELECT
        CONVERT(nvarchar(50), g.gafete) AS Numero,
        MAX(CASE WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 1 ELSE 0 END) AS HasActive
    FROM {PosTable("gafete")} g
    GROUP BY CONVERT(nvarchar(50), g.gafete)
),
ranked AS
(
    SELECT
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
            PARTITION BY
                CONVERT(nvarchar(50), g.gafete)
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
    FROM {PosTable("gafete")} g
    LEFT JOIN badge_state bs
        ON bs.Numero = CONVERT(nvarchar(50), g.gafete)
    WHERE (
            (@inicio IS NULL AND @finSiguiente IS NULL)
         OR (g.fecha IS NOT NULL AND (@inicio IS NULL OR g.fecha >= @inicio) AND (@finSiguiente IS NULL OR g.fecha < @finSiguiente))
         OR (g.fecha IS NULL AND g.hora IS NOT NULL AND (@inicio IS NULL OR g.hora >= @inicio) AND (@finSiguiente IS NULL OR g.hora < @finSiguiente))
         OR (@soloSinFecha = 1 AND g.fecha IS NULL AND g.hora IS NULL)
      )
)
SELECT TOP (120)
    Numero,
    Staff,
    FolioOperacion,
    FechaEntrega,
    FechaActividad,
    Estatus,
    Regreso
FROM ranked
WHERE rn = 1
ORDER BY
    CASE WHEN FechaActividad IS NULL THEN 1 ELSE 0 END,
    FechaActividad DESC,
    Numero DESC",
                    ("@inicio", fechaInicio?.Date),
                    ("@finSiguiente", fechaFin?.Date.AddDays(1)),
                    ("@soloSinFecha", soloSinFecha ? 1 : 0));

                var result = MapGafeteRows(rows.Cast<IReadOnlyDictionary<string, object?>>());

                if (result.Count == 0 && !soloSinFecha && (fechaInicio.HasValue || fechaFin.HasValue))
                {
                    var recentRows = await ReadRowsFromSqlAsyncWithParam($@"
WITH badge_state AS
(
    SELECT
        CONVERT(nvarchar(50), g.gafete) AS Numero,
        MAX(CASE WHEN UPPER(COALESCE(g.venta, '')) = 'A' THEN 1 ELSE 0 END) AS HasActive
    FROM {PosTable("gafete")} g
    GROUP BY CONVERT(nvarchar(50), g.gafete)
),
ranked AS
(
    SELECT
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
    FROM {PosTable("gafete")} g
    LEFT JOIN badge_state bs
        ON bs.Numero = CONVERT(nvarchar(50), g.gafete)
    WHERE UPPER(COALESCE(g.venta, '')) IN ('A', 'S')
      AND COALESCE(g.hora, g.fecha) >= DATEADD(day, -3, @fechaReferencia)
)
SELECT TOP (120)
    Numero,
    Staff,
    FolioOperacion,
    FechaEntrega,
    FechaActividad,
    Estatus,
    Regreso
FROM ranked
WHERE rn = 1
ORDER BY FechaActividad DESC, Numero DESC",
                        ("@fechaReferencia", (fechaFin ?? fechaInicio ?? DateTime.Today).Date.AddDays(1)));

                    result = MapGafeteRows(recentRows.Cast<IReadOnlyDictionary<string, object?>>());
                }

                await EnrichGafetesFromAppMovilAsync(result);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar gafetes desde dbo.gafete.");
                return new List<PosGafeteEfRow>();
            }
        }

        private static List<PosGafeteEfRow> MapGafeteRows(IEnumerable<IReadOnlyDictionary<string, object?>> rows) =>
            rows.Select(row =>
            {
                var estatus = PickText(row, "Estatus");
                var esLibre = string.Equals(estatus, "LIBRE", StringComparison.OrdinalIgnoreCase);
                return new PosGafeteEfRow(
                    PickText(row, "Numero"),
                    esLibre ? string.Empty : FirstText(PickText(row, "Staff"), "SIN STAFF"),
                    PickText(row, "FolioOperacion"),
                    ToDate(PickObject(row, "FechaEntrega")),
                    estatus,
                    PickDate(row, "Regreso"),
                    string.Empty,
                    string.Empty,
                    string.Empty);
            }).ToList();

        private async Task SuspendOtherActiveBadgeAssignmentsAsync(
            DbConnection connection,
            string gafete,
            string folioOperacionTexto,
            long? folioOperacionNumero,
            DateTime fechaBase,
            DateTime fechaHora)
        {
            if (string.IsNullOrWhiteSpace(gafete))
                return;

            await using var suspend = connection.CreateCommand();
            suspend.CommandText = $@"
UPDATE {PosTable("gafete")}
SET venta = 'S',
    fecha = CONVERT(smalldatetime, @fechaBase),
    hora = @fechaHora
WHERE CONVERT(nvarchar(50), gafete) = @gafete
  AND UPPER(COALESCE(venta, '')) = 'A'
  AND (
        @folioOperacionTexto = ''
     OR NOT (
            CONVERT(nvarchar(50), folioperacion) = @folioOperacionTexto
         OR (
                @folioOperacionNumero IS NOT NULL
            AND ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
            AND CONVERT(bigint, folioperacion) = @folioOperacionNumero
            )
          )
      );";
            AddParameter(suspend, "@gafete", gafete);
            AddParameter(suspend, "@folioOperacionTexto", folioOperacionTexto);
            AddParameter(suspend, "@folioOperacionNumero", folioOperacionNumero);
            AddParameter(suspend, "@fechaBase", fechaBase);
            AddParameter(suspend, "@fechaHora", fechaHora);
            await ExecuteNonQueryAsync(suspend);
        }

        private async Task EnrichGafetesFromAppMovilAsync(List<PosGafeteEfRow> rows)
        {
            var pairs = rows
                .Where(x => !string.Equals(x.Estatus, "LIBRE", StringComparison.OrdinalIgnoreCase))
                .Select(x => new
                {
                    Gafete = Normalize(x.Numero) ?? string.Empty,
                    FolioOperacion = Normalize(x.FolioOperacion) ?? string.Empty
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Gafete))
                .DistinctBy(x => $"{x.Gafete}|{x.FolioOperacion}", StringComparer.OrdinalIgnoreCase)
                .Take(60)
                .ToList();
            if (pairs.Count == 0)
                return;

            try
            {
                var values = string.Join(",", pairs.Select((_, index) => $"(@g{index}, @f{index})"));
                var parameters = pairs
                    .SelectMany((value, index) => new[]
                    {
                        ($"@g{index}", (object?)value.Gafete),
                        ($"@f{index}", (object?)value.FolioOperacion)
                    })
                    .ToArray();
                var appRows = await ReadRowsFromSqlAsyncWithParam($@"
WITH Requested(Gafete, FolioOperacion) AS (
    SELECT v.Gafete, v.FolioOperacion
    FROM (VALUES {values}) v(Gafete, FolioOperacion)
)
SELECT
    r.Gafete,
    r.FolioOperacion,
    COALESCE(a.unidad, '') AS Unidad,
    COALESCE(NULLIF(a.telefono_taxista, ''), NULLIF(a.telefono_contacto, ''), '') AS Telefono,
    COALESCE(a.nacionalidad, '') AS Nacionalidad
FROM Requested r
OUTER APPLY (
    SELECT TOP (1)
        unidad,
        telefono_taxista,
        telefono_contacto,
        nacionalidad
    FROM {PosTable("AppMovilRegistro")} a
    WHERE (
            r.FolioOperacion <> ''
        AND (
                UPPER(COALESCE(a.folio_app, '')) = UPPER(r.FolioOperacion)
             OR UPPER(COALESCE(a.folio_app_original, '')) = UPPER(r.FolioOperacion)
             OR (ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(r.FolioOperacion) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, r.FolioOperacion))
             OR (ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(r.FolioOperacion) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, r.FolioOperacion))
            )
          )
       OR (
            ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(a.folio_gafete, ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
            LIKE '%,' + r.Gafete + ',%'
          )
    ORDER BY
        CASE
            WHEN r.FolioOperacion <> '' AND UPPER(COALESCE(a.folio_app, '')) = UPPER(r.FolioOperacion) THEN 0
            WHEN r.FolioOperacion <> '' AND UPPER(COALESCE(a.folio_app_original, '')) = UPPER(r.FolioOperacion) THEN 1
            WHEN r.FolioOperacion <> '' AND ISNUMERIC(COALESCE(a.folio_app, '')) = 1 AND ISNUMERIC(r.FolioOperacion) = 1 AND CONVERT(bigint, a.folio_app) = CONVERT(bigint, r.FolioOperacion) THEN 2
            WHEN r.FolioOperacion <> '' AND ISNUMERIC(COALESCE(a.folio_app_original, '')) = 1 AND ISNUMERIC(r.FolioOperacion) = 1 AND CONVERT(bigint, a.folio_app_original) = CONVERT(bigint, r.FolioOperacion) THEN 3
            ELSE 10
        END,
        fecha_operacion DESC
) a;",
                    parameters);

                var data = appRows
                    .GroupBy(
                        x => $"{Normalize(PickText(x, "Gafete")) ?? string.Empty}|{Normalize(PickText(x, "FolioOperacion")) ?? string.Empty}",
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        x => x.Key,
                        x => x.First(),
                        StringComparer.OrdinalIgnoreCase);

                for (var index = 0; index < rows.Count; index++)
                {
                    var key = $"{Normalize(rows[index].Numero) ?? string.Empty}|{Normalize(rows[index].FolioOperacion) ?? string.Empty}";
                    if (!data.TryGetValue(key, out var row))
                        continue;

                    rows[index] = rows[index] with
                    {
                        Unidad = PickText(row, "Unidad"),
                        Telefono = PickText(row, "Telefono"),
                        Nacionalidad = PickText(row, "Nacionalidad")
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible enriquecer gafetes con datos de app movil.");
            }
        }


        private sealed record PosGafeteEfRow(string Numero, string Staff, string FolioOperacion, DateTime? FechaEntrega, string Estatus, string Regreso, string Unidad, string Telefono, string Nacionalidad);


    }
}
