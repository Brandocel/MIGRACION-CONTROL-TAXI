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
private async Task<int> EnsurePosStaffAsync(
            DbConnection connection,
            DbTransaction transaction,
            string clave,
            string nombre,
            string usuario)
        {
            var normalizedClave = string.IsNullOrWhiteSpace(clave) ? "WEB" : clave.Trim();
            var existing = await _appContext.StaffVendedores
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Clave == normalizedClave);
            if (existing != null)
                return existing.Id;

            var staff = new AppStaffVendedor
            {
                Clave = normalizedClave,
                Nombre = string.IsNullOrWhiteSpace(nombre) ? normalizedClave : nombre.Trim(),
                Telefono = string.Empty,
                Estatus = "Activo"
            };
            await _appContext.StaffVendedores.AddAsync(staff);
            await _appContext.SaveChangesAsync();
            return staff.Id;
        }





private async Task InsertPosPagoAsync(
            DbConnection connection,
            DbTransaction transaction,
            int ventaId,
            string formaPago,
            string moneda,
            decimal tipoCambio,
            decimal importe,
            string referencia)
        {
            await _appContext.Pagos.AddAsync(new AppPago
            {
                VentaId = ventaId,
                FormaPago = formaPago,
                Moneda = moneda,
                TipoCambio = tipoCambio,
                Importe = importe,
                Referencia = referencia,
                Estatus = "Aplicado"
            });
            await _appContext.SaveChangesAsync();
        }





private async Task SaveOperacionBeneficiarioAsync(
            DbConnection connection,
            DbTransaction transaction,
            string folioOperacion,
            PosVentasViewModel model,
            string staffClave)
        {
            var row = await _appContext.PosOperacionBeneficiarios
                .FirstOrDefaultAsync(x => x.FolioOperacion == folioOperacion);
            if (row == null)
            {
                row = new AppOperacionBeneficiario { FolioOperacion = folioOperacion };
                await _appContext.PosOperacionBeneficiarios.AddAsync(row);
            }

            row.StaffClave = SafeText(staffClave, 20);
            row.StaffNombre = SafeText(model.Vendedor, 100);
            row.TransporteTipo = SafeText(string.IsNullOrWhiteSpace(model.TransporteTipo) ? "WEB" : model.TransporteTipo, 10);
            row.GuiaMatricula = model.GuiaMatricula;
            row.TaxistaId = model.TaxistaId;
            row.TaxistaNombre = SafeText(model.TaxistaNombre, 80);
            row.Usuario = SafeText(model.Usuario, 50);
            row.Fecha = DateTime.UtcNow;
            await _appContext.SaveChangesAsync();
        }





private async Task<Dictionary<string, string>> LoadVendorMapAsync()
        {
            var rows = await _dbContext.Vendors.AsNoTracking()
                .Where(x => x.VendorKey != null && x.VendorKey.Trim() != string.Empty)
                .Select(x => new { Key = x.VendorKey!.Trim(), Name = x.Name ?? string.Empty })
                .ToListAsync();

            return rows
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Name, StringComparer.OrdinalIgnoreCase);
        }





private async Task<string> ResolveVendorKeyAsync(string staff)
        {
            var normalized = Normalize(staff);
            if (normalized == null)
                return string.Empty;
            if (normalized.Length <= 4)
                return normalized;

            var row = await _dbContext.Vendors.AsNoTracking()
                .Where(x => x.Name != null && EF.Functions.Like(x.Name, $"%{normalized}%"))
                .Select(x => x.VendorKey)
                .FirstOrDefaultAsync();

            return row?.Trim() ?? string.Empty;
        }





private sealed class VentaRelacionInfo
        {
            public string FolioControl { get; set; } = string.Empty;
            public string FolioApp { get; set; } = string.Empty;
            public string Gafete { get; set; } = string.Empty;
            public long TaxistaId { get; set; }
            public string TaxistaNombre { get; set; } = string.Empty;
            public string TransporteTipo { get; set; } = string.Empty;
            public string Hotel { get; set; } = string.Empty;
            public int Pax { get; set; }
            public decimal Dejada { get; set; }
            public DateTime? FechaRegistro { get; set; }
            public string FolioOperacion { get; set; } = string.Empty;
            public string FolioPos { get; set; } = string.Empty;
        }





private static async Task<VentaRelacionInfo?> ResolveVentaRelacionAsync(
            DbConnection connection,
            DbTransaction? transaction,
            string? folioControl,
            string? folioApp,
            string? taxistaNombre,
            string? gafete)
        {
            var folioControlNorm = Normalize(folioControl);
            var folioAppNorm = Normalize(folioApp);
            var taxistaNorm = Normalize(taxistaNombre);
            var gafeteNorm = Normalize(gafete);
            if (folioControlNorm == null && folioAppNorm == null && gafeteNorm == null)
                return null;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
SELECT TOP (1)
    COALESCE(c.FolioControl, v.id_registro, '') AS FolioControl,
    v.id_registro AS FolioApp,
    COALESCE(NULLIF(v.folio_gafete, ''), NULLIF(r.Gafete, ''), '') AS Gafete,
    CASE
        WHEN COALESCE(v.id_catalogo, 0) > 0
         AND (
                COALESCE(r.TaxistaId, 0) = 0
             OR CONVERT(nvarchar(30), COALESCE(r.TaxistaId, 0)) = NULLIF(LTRIM(RTRIM(v.folio_gafete)), '')
         )
            THEN v.id_catalogo
        ELSE COALESCE(r.TaxistaId, v.id_catalogo, 0)
    END AS TaxistaId,
    CASE
        WHEN COALESCE(v.id_catalogo, 0) > 0
         AND (
                COALESCE(r.TaxistaId, 0) = 0
             OR CONVERT(nvarchar(30), COALESCE(r.TaxistaId, 0)) = NULLIF(LTRIM(RTRIM(v.folio_gafete)), '')
             OR NULLIF(LTRIM(RTRIM(r.TaxistaNombre)), '') IS NULL
         )
            THEN COALESCE(v.nombre_taxista, '')
        ELSE COALESCE(NULLIF(r.TaxistaNombre, ''), v.nombre_taxista, '')
    END AS TaxistaNombre,
    COALESCE(NULLIF(r.TransporteTipo, ''), v.tipo_servicio, '') AS TransporteTipo,
    COALESCE(v.hotel, '') AS Hotel,
    COALESCE(v.numero_personas, 1) AS Pax,
    v.fecha_registro AS FechaRegistro,
    COALESCE(r.FolioOperacion, '') AS FolioOperacion,
    COALESCE(r.FolioPos, '') AS FolioPos,
    CASE WHEN ISNUMERIC(v.costo_viaje) = 1 THEN CAST(v.costo_viaje AS decimal(18,2)) ELSE 0 END AS Dejada
FROM {PosTable("vw_AppMovilRegistrosViajes")} v
LEFT JOIN {PosTable("AppMovilRegistro")} a
    ON a.folio_app = v.id_registro
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioControl = v.id_registro OR c.FolioAppOriginal = v.id_registro OR c.FolioAppOriginal = a.folio_app_original
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioApp = v.id_registro OR r.FolioApp = a.folio_app_original
WHERE (@folioControl <> '' AND UPPER(COALESCE(c.FolioControl, v.id_registro, '')) = UPPER(@folioControl))
   OR (@folioApp <> '' AND UPPER(v.id_registro) = UPPER(@folioApp))
   OR (@folioControl <> '' AND UPPER(COALESCE(r.FolioOperacion, '')) = UPPER(@folioControl))
   OR (@folioControl <> '' AND UPPER(COALESCE(r.FolioPos, '')) = UPPER(@folioControl))
   OR (@folioApp <> '' AND UPPER(COALESCE(r.FolioOperacion, '')) = UPPER(@folioApp))
   OR (@folioApp <> '' AND UPPER(COALESCE(r.FolioPos, '')) = UPPER(@folioApp))
   OR (@gafete <> '' AND (
             ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(NULLIF(v.folio_gafete, ''), NULLIF(r.Gafete, ''), ''), ' ', ''), ';', ','), '/', ','), '|', ',') + ','
             LIKE '%,' + REPLACE(@gafete, ' ', '') + ',%'
        ))
ORDER BY
    CASE
        WHEN @folioControl <> '' AND UPPER(COALESCE(c.FolioControl, v.id_registro, '')) = UPPER(@folioControl) THEN 0
        WHEN @folioApp <> '' AND UPPER(v.id_registro) = UPPER(@folioApp) THEN 1
        WHEN @folioControl <> '' AND UPPER(COALESCE(r.FolioOperacion, '')) = UPPER(@folioControl) THEN 2
        WHEN @folioControl <> '' AND UPPER(COALESCE(r.FolioPos, '')) = UPPER(@folioControl) THEN 3
        WHEN @folioApp <> '' AND UPPER(COALESCE(r.FolioOperacion, '')) = UPPER(@folioApp) THEN 4
        WHEN @folioApp <> '' AND UPPER(COALESCE(r.FolioPos, '')) = UPPER(@folioApp) THEN 5
        WHEN @gafete <> '' AND (
                ',' + REPLACE(REPLACE(REPLACE(REPLACE(COALESCE(NULLIF(v.folio_gafete, ''), NULLIF(r.Gafete, ''), ''), ' ', ''), ';', ','), '/', ','), '|', ',')
                + ','
                LIKE '%,' + REPLACE(@gafete, ' ', '') + ',%'
            ) THEN 6
        ELSE 7
    END,
    v.fecha_registro DESC";
            AddParameter(command, "@folioControl", folioControlNorm ?? string.Empty);
            AddParameter(command, "@folioApp", folioAppNorm ?? string.Empty);
            AddParameter(command, "@taxista", taxistaNorm ?? string.Empty);
            AddParameter(command, "@gafete", gafeteNorm ?? string.Empty);
            var rows = await ReadRowsAsync(command);
            var row = rows.FirstOrDefault();
            if (row == null)
                return null;

            return new VentaRelacionInfo
            {
                FolioControl = PickText(row, "FolioControl"),
                FolioApp = PickText(row, "FolioApp"),
                Gafete = PickText(row, "Gafete"),
                TaxistaId = Convert.ToInt64(ToDecimal(PickObject(row, "TaxistaId")), CultureInfo.InvariantCulture),
                TaxistaNombre = PickText(row, "TaxistaNombre"),
                TransporteTipo = PickText(row, "TransporteTipo"),
                Hotel = PickText(row, "Hotel"),
                Pax = Math.Max(PickInt(row, "Pax"), 1),
                Dejada = ToDecimal(PickObject(row, "Dejada")),
                FechaRegistro = ToDate(PickObject(row, "FechaRegistro")),
                FolioOperacion = PickText(row, "FolioOperacion"),
                FolioPos = PickText(row, "FolioPos")
            };
        }





private static async Task LinkRelacionTicketTaxistaAsync(
            DbConnection connection,
            DbTransaction transaction,
            VentaRelacionInfo relacion,
            string folioOperacion,
            string folioPos,
            PosVentasViewModel model,
            string usuario,
            decimal totalVenta)
        {
            if (string.IsNullOrWhiteSpace(relacion.FolioApp))
                return;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
IF EXISTS (SELECT 1 FROM {PosTable("RelacionTicketTaxista")} WHERE FolioApp = @folioApp)
BEGIN
    UPDATE {PosTable("RelacionTicketTaxista")}
    SET FolioOperacion = @folioOperacion,
        FolioPos = @folioPos,
        Gafete = @gafete,
        TaxistaId = @taxistaId,
        TaxistaNombre = @taxistaNombre,
        TransporteTipo = @transporte,
        Usuario = @usuario,
        FechaActualizacion = SYSUTCDATETIME()
    WHERE FolioApp = @folioApp;
END
ELSE
BEGIN
    INSERT INTO {PosTable("RelacionTicketTaxista")}
        (FolioApp, FolioOperacion, FolioPos, Gafete, TaxistaId, TaxistaNombre, TransporteTipo, Observaciones, Usuario)
    VALUES
        (@folioApp, @folioOperacion, @folioPos, @gafete, @taxistaId, @taxistaNombre, @transporte, '', @usuario);
END";
            AddParameter(command, "@folioApp", relacion.FolioApp);
            AddParameter(command, "@folioOperacion", folioOperacion);
            AddParameter(command, "@folioPos", folioPos);
            AddParameter(command, "@gafete", SafeText(FirstText(model.Gafete, relacion.Gafete), 30));
            AddParameter(command, "@taxistaId", model.TaxistaId > 0 ? model.TaxistaId : relacion.TaxistaId);
            AddParameter(command, "@taxistaNombre", SafeText(FirstText(model.TaxistaNombre, relacion.TaxistaNombre), 150));
            AddParameter(command, "@transporte", SafeText(FirstText(model.TransporteTipo, relacion.TransporteTipo), 20));
            AddParameter(command, "@usuario", SafeText(usuario, 50));
            await ExecuteNonQueryAsync(command);

            await UpsertMkt2DejadaFromRelacionAsync(
                connection,
                transaction,
                relacion.FolioApp,
                folioOperacion,
                folioPos,
                FirstText(model.Gafete, relacion.Gafete),
                FirstText(relacion.Gafete, model.Gafete),
                model.TaxistaId > 0 ? model.TaxistaId : relacion.TaxistaId,
                FirstText(model.TaxistaNombre, relacion.TaxistaNombre),
                FirstText(model.TransporteTipo, relacion.TransporteTipo),
                totalVenta);
        }





        private static async Task UpsertMkt2DejadaFromRelacionAsync(
            DbConnection connection,
            DbTransaction transaction,
            string folioApp,
            string folioOperacion,
            string folioPos,
            string gafete,
            string originalGafete,
            long taxistaId,
            string taxistaNombre,
            string transporteTipo,
            decimal totalVenta)
        {
            if (string.IsNullOrWhiteSpace(folioApp) || string.IsNullOrWhiteSpace(folioOperacion))
                return;

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
DECLARE @folioBigint BIGINT;

IF ISNUMERIC(@folioOperacion) = 1
BEGIN
    SET @folioBigint = CONVERT(BIGINT, @folioOperacion);
END

UPDATE {PosTable("dejadas")}
SET gafete = CASE WHEN NULLIF(@gafete, '') IS NULL THEN gafete ELSE @gafete END,
    idtaxi = CASE WHEN @taxistaId > 0 THEN CONVERT(int, @taxistaId) ELSE idtaxi END,
    nombrevendedor = CASE WHEN NULLIF(@taxistaNombre, '') IS NULL THEN nombrevendedor ELSE @taxistaNombre END,
    tipotransporte = CASE WHEN NULLIF(@transporte, '') IS NULL THEN tipotransporte ELSE @transporte END,
    totalventa = CASE WHEN @totalVenta > 0 THEN CONVERT(real, @totalVenta) ELSE totalventa END
WHERE ( @folioBigint IS NOT NULL AND folioregistro = @folioBigint )
   OR folioregistrostr = @folioOperacion;

INSERT INTO {PosTable("dejadas")}
    (idstaff, nombrestaff, nombrealmacen, idalmacen, fecha, hora, idcajero, nombrecajero, total, codigorecepcion, folioregistro, folioregistrostr, unidad, pax, hotel, nombrevendedor, tipotransporte, telefono, horaentrada, horasalida, totalventa, comision, pago, totalefectivo, totaltarjeta, totalgastos, gafete, idtaxi, adl, men, inf)
SELECT
    0,
    '',
    '',
    0,
    CONVERT(smalldatetime, v.fecha_registro),
    CONVERT(nvarchar(20), CAST(v.fecha_registro AS time), 100),
    0,
    '',
    CASE WHEN ISNUMERIC(v.costo_viaje) = 1 THEN CONVERT(real, v.costo_viaje) ELSE 0 END,
    @folioPos,
    COALESCE(@folioBigint, 0),
    @folioOperacion,
    COALESCE(NULLIF(v.unidad, ''), NULLIF(v.placas, ''), ''),
    COALESCE(v.numero_personas, 1),
    COALESCE(v.hotel, ''),
    COALESCE(NULLIF(@taxistaNombre, ''), v.nombre_taxista, ''),
    COALESCE(NULLIF(@transporte, ''), v.tipo_servicio, ''),
    COALESCE(v.telefono_taxista, ''),
    '',
    '',
    CASE WHEN @totalVenta > 0 THEN CONVERT(real, @totalVenta) ELSE 0 END,
    0,
    0,
    0,
    0,
    0,
    COALESCE(NULLIF(@gafete, ''), v.folio_gafete, ''),
    CASE WHEN @taxistaId > 0 THEN CONVERT(int, @taxistaId) ELSE COALESCE(v.id_catalogo, 0) END,
    COALESCE(v.numero_personas, 1),
    0,
    0
FROM {PosTable("AppMovilRegistro")} a
INNER JOIN {PosTable("vw_AppMovilRegistrosViajes")} v
    ON v.id_registro = a.folio_app
WHERE (a.folio_app = @folioApp OR a.folio_app_original = @folioApp)
  AND NOT EXISTS (
      SELECT 1
      FROM {PosTable("dejadas")}
      WHERE ( @folioBigint IS NOT NULL AND folioregistro = @folioBigint )
         OR folioregistrostr = @folioOperacion
  );
";
            AddParameter(command, "@folioApp", folioApp);
            AddParameter(command, "@folioOperacion", folioOperacion);
            AddParameter(command, "@folioPos", SafeText(folioPos, 100));
            AddParameter(command, "@gafete", SafeText(gafete, 10));
            AddParameter(command, "@taxistaId", taxistaId);
            AddParameter(command, "@taxistaNombre", SafeText(taxistaNombre, 100));
            AddParameter(command, "@transporte", SafeText(transporteTipo, 10));
            AddParameter(command, "@totalVenta", totalVenta);
            await ExecuteNonQueryAsync(command);

            await InsertGafeteMovementsFromRelacionAsync(connection, transaction, folioApp, folioOperacion, gafete, originalGafete, taxistaId);
        }

        private static async Task InsertGafeteMovementsFromRelacionAsync(
            DbConnection connection,
            DbTransaction transaction,
            string folioApp,
            string folioOperacion,
            string gafetes,
            string originalGafetes,
            long taxistaId)
        {
            if (string.IsNullOrWhiteSpace(folioApp) || string.IsNullOrWhiteSpace(folioOperacion))
                return;

            var gafeteNumbers = SplitGafeteNumbers(gafetes)
                .Select(NormalizeBadgeToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
            if (gafeteNumbers.Count == 0)
                return;

            var now = DateTime.Now;
            var fechaBase = now.Date;
            var existingBadgeNumbers = new List<string>();

            await using (var existing = connection.CreateCommand())
            {
                existing.Transaction = transaction;
                existing.CommandText = $@"
SELECT CONVERT(nvarchar(50), gafete) AS Gafete
FROM {PosTable("gafete")}
WHERE (
        CONVERT(nvarchar(50), folioperacion) = @folioOperacion
     OR (
            ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
        AND ISNUMERIC(@folioOperacion) = 1
        AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folioOperacion)
        )
      )
  AND UPPER(COALESCE(venta, '')) = 'A'
ORDER BY COALESCE(hora, fecha) DESC, CONVERT(nvarchar(50), gafete) DESC;";
                AddParameter(existing, "@folioOperacion", folioOperacion);
                var rows = await ReadRowsAsync(existing);
                existingBadgeNumbers = rows
                    .Select(row => NormalizeBadgeToken(PickText(row, "Gafete")))
                    .Where(row => !string.IsNullOrWhiteSpace(row))
                    .ToList();
            }

            var originalTokens = SplitGafeteNumbers(originalGafetes)
                .Select(NormalizeBadgeToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (originalTokens.Count == 0)
                originalTokens = existingBadgeNumbers
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (originalTokens.Count == 0)
                return;

            var newTokens = gafeteNumbers
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var removedTokens = originalTokens
                .Where(token => !newTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var addedTokens = newTokens
                .Where(token => !originalTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                .ToList();
            var replaceCount = Math.Min(removedTokens.Count, addedTokens.Count);

            for (var index = 0; index < replaceCount; index++)
            {
                var existingBadge = removedTokens[index];
                var newBadge = addedTokens[index];

                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = $@"
UPDATE {PosTable("gafete")}
SET gafete = @gafeteNuevo,
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
  AND CONVERT(nvarchar(50), gafete) = @gafeteOriginal
  AND UPPER(COALESCE(venta, '')) = 'A';";
                AddParameter(update, "@folioOperacion", folioOperacion);
                AddParameter(update, "@gafeteOriginal", existingBadge);
                AddParameter(update, "@gafeteNuevo", newBadge);
                AddParameter(update, "@fechaBase", fechaBase);
                AddParameter(update, "@hora", now);
                await ExecuteNonQueryAsync(update);
            }

            foreach (var oldBadge in removedTokens.Skip(replaceCount))
            {
                await using var suspend = connection.CreateCommand();
                suspend.Transaction = transaction;
                suspend.CommandText = $@"
UPDATE {PosTable("gafete")}
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
  AND UPPER(COALESCE(venta, '')) = 'A';";
                AddParameter(suspend, "@folioOperacion", folioOperacion);
                AddParameter(suspend, "@gafete", oldBadge);
                AddParameter(suspend, "@fechaBase", fechaBase);
                AddParameter(suspend, "@hora", now);
                await ExecuteNonQueryAsync(suspend);
            }

            foreach (var staleBadge in existingBadgeNumbers
                         .Where(token => !newTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
                         .Where(token => !removedTokens.Contains(token, StringComparer.OrdinalIgnoreCase)))
            {
                await using var suspend = connection.CreateCommand();
                suspend.Transaction = transaction;
                suspend.CommandText = $@"
UPDATE {PosTable("gafete")}
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
  AND UPPER(COALESCE(venta, '')) IN ('A', 'S');";
                AddParameter(suspend, "@folioOperacion", folioOperacion);
                AddParameter(suspend, "@gafete", staleBadge);
                AddParameter(suspend, "@fechaBase", fechaBase);
                AddParameter(suspend, "@hora", now);
                await ExecuteNonQueryAsync(suspend);
            }

            foreach (var newBadge in addedTokens.Skip(replaceCount))
            {
                await using (var suspendOtherActive = connection.CreateCommand())
                {
                    suspendOtherActive.Transaction = transaction;
                    suspendOtherActive.CommandText = $@"
UPDATE {PosTable("gafete")}
SET venta = 'S',
    fecha = CONVERT(smalldatetime, @fechaBase),
    hora = @hora
WHERE CONVERT(nvarchar(50), gafete) = @gafete
  AND UPPER(COALESCE(venta, '')) = 'A'
  AND NOT (
        CONVERT(nvarchar(50), folioperacion) = @folioOperacion
     OR (
            ISNUMERIC(CONVERT(nvarchar(50), folioperacion)) = 1
        AND ISNUMERIC(@folioOperacion) = 1
        AND CONVERT(bigint, folioperacion) = CONVERT(bigint, @folioOperacion)
        )
      );";
                    AddParameter(suspendOtherActive, "@fechaBase", fechaBase);
                    AddParameter(suspendOtherActive, "@hora", now);
                    AddParameter(suspendOtherActive, "@gafete", newBadge);
                    AddParameter(suspendOtherActive, "@folioOperacion", folioOperacion);
                    await ExecuteNonQueryAsync(suspendOtherActive);
                }

                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $@"
INSERT INTO {PosTable("gafete")} (matricula, gafete, fecha, venta, hora, folioperacion)
SELECT @taxistaId, @gafete, CONVERT(smalldatetime, @fechaBase), 'A', @hora, @folioOperacionNumero
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
                AddParameter(insert, "@taxistaId", taxistaId);
                AddParameter(insert, "@gafete", newBadge);
                AddParameter(insert, "@fechaBase", fechaBase);
                AddParameter(insert, "@hora", now);
                AddParameter(insert, "@folioOperacion", folioOperacion);
                AddParameter(insert, "@folioOperacionNumero", long.TryParse(folioOperacion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFolio) ? parsedFolio : null);
                await ExecuteNonQueryAsync(insert);
            }
        }

        private static IEnumerable<string> SplitGafeteNumbers(string value)
        {
            var current = new List<char>();
            foreach (var ch in value ?? string.Empty)
            {
                if (char.IsDigit(ch))
                {
                    current.Add(ch);
                    continue;
                }

                if (current.Count > 0)
                {
                    yield return new string(current.ToArray());
                    current.Clear();
                }
            }

            if (current.Count > 0)
                yield return new string(current.ToArray());
        }

        private static string NormalizeBadgeToken(string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero))
                return string.Empty;

            return numero.ToString(CultureInfo.InvariantCulture);
        }





private async Task<ProductRow?> ResolveProductForSaleAsync(PosVentasViewModel model)
        {
            if (model.ProductoId > 0)
            {
                var byId = await _dbContext.Products.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.ProductId == model.ProductoId);
                if (byId != null)
                    return byId;
            }

            var normalized = Normalize(model.ProductoBusqueda);
            if (normalized == null)
                return null;

            return await _dbContext.Products.AsNoTracking()
                .Where(x => (x.Active ?? "S").ToUpper() != "N")
                .OrderBy(x => x.Name)
                .FirstOrDefaultAsync(x =>
                    x.Barcode == normalized ||
                    x.ProductId.ToString() == normalized ||
                    EF.Functions.Like(x.Name ?? string.Empty, $"%{normalized}%"));
        }





private static decimal ResolveTax(decimal baseAmount, decimal iva)
        {
            if (iva <= 0)
                return 0m;

            return iva > 1m ? baseAmount * (iva / 100m) : baseAmount * iva;
        }





private static decimal ResolveProductPrice(ProductRow product)
        {
            var primary = ToDecimal(product.Price1);
            return primary > 0 ? primary : ToDecimal(product.PublicPrice);
        }





private static void AddVentaDetalle(List<PosVentaDetalleViewModel> detalle, string departamento, decimal importe)
        {
            if (importe <= 0)
                return;

            detalle.Add(new PosVentaDetalleViewModel
            {
                Cantidad = 1,
                Producto = departamento,
                Departamento = departamento,
                Precio = importe,
                Importe = importe
            });
        }




    }
}
