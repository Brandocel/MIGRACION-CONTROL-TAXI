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
public async Task<PosPagosViewModel?> TryGetPagosAsync(string? folioOperacion = null)
        {
            try
            {
                var normalized = Normalize(folioOperacion);
                if (normalized != null)
                {
                    var setupConnection = _dbContext.Database.GetDbConnection();
                    if (setupConnection.State != ConnectionState.Open)
                        await setupConnection.OpenAsync();
                    await EnsureRelacionesTicketTaxistaTableAsync(setupConnection);
                    normalized = await ResolveFolioOperacionFromRelacionAsync(setupConnection, normalized);
                }
                var posRows = await GetPagosRowsFromEfAsync(normalized);
                if (posRows.Count > 0)
                {
                    var pagos = new List<PosPagoRowViewModel>();
                    foreach (var row in posRows)
                    {
                        var pago = row.Pago;
                        var referencia = row.Referencia;
                        var fechaPago = FormatDate(row.FechaPago);
                        pagos.Add(new PosPagoRowViewModel
                        {
                            Fecha = string.IsNullOrWhiteSpace(fechaPago) ? FormatDate(row.Fecha) : fechaPago,
                            FormaPago = "COMISION",
                            Importe = pago,
                            Referencia = referencia,
                            Estatus = pago > 0 ? "APLICADO" : row.Comision > 0 ? "PENDIENTE" : "SIN COMISION"
                        });
                    }

                    return new PosPagosViewModel
                    {
                        FolioOperacion = posRows.First().Folio,
                        Ticket = posRows.First().Referencia,
                        Fecha = FormatDate(posRows.First().Fecha),
                        TotalVenta = posRows.Sum(x => x.Total),
                        TotalComision = posRows.Sum(x => x.Comision),
                        TotalPagado = pagos.Sum(x => x.Importe),
                        Pagos = pagos
                    };
                }
                return new PosPagosViewModel
                {
                    FolioOperacion = normalized ?? string.Empty,
                    Fecha = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Pagos desde POS.");
                return new PosPagosViewModel
                {
                    FolioOperacion = Normalize(folioOperacion) ?? string.Empty,
                    Fecha = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                };
            }
        }

        private async Task<List<PosPagoEfRow>> GetPagosRowsFromEfAsync(string? normalized)
        {
            if (normalized == null)
            {
                var today = DateTime.Today;
                var latestDate = await _posContext.MovOperaciones
                    .AsNoTracking()
                    .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == today)
                    .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date)
                    ?? await _posContext.MovOperaciones
                        .AsNoTracking()
                        .Where(x => x.Fecha.HasValue)
                        .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date);
                if (!latestDate.HasValue)
                    return new List<PosPagoEfRow>();

                return await _posContext.MovOperaciones
                    .AsNoTracking()
                    .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == latestDate.Value)
                    .OrderByDescending(x => x.Fecha)
                    .ThenByDescending(x => x.FolioOperacion)
                    .Take(80)
                    .Select(x => new PosPagoEfRow(
                        x.FolioOperacion.HasValue ? x.FolioOperacion.Value.ToString() : string.Empty,
                        x.Fecha,
                        x.FolioSoluone ?? string.Empty,
                        ToDecimal(x.TotalJoyeria) + ToDecimal(x.TotalCompra),
                        ToDecimal(x.Comision),
                        ToDecimal(x.Pago),
                        x.FechaPago))
                    .ToListAsync();
            }

            var movimientos = await _posContext.MovOperaciones.AsNoTracking()
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FolioOperacion)
                .Take(1000)
                .ToListAsync();
            var operaciones = await _posContext.Operaciones.AsNoTracking().Take(1000).ToListAsync();
            var relaciones = await _posContext.RelacionesTicketTaxista.AsNoTracking().Take(1000).ToListAsync();
            var controles = await _posContext.AppMovilFolioControl.AsNoTracking().Take(1000).ToListAsync();

            var query =
                from m in movimientos
                join o in operaciones on m.FolioOperacion equals o.Folio into ops
                from o in ops.DefaultIfEmpty()
                join r in relaciones on (m.FolioOperacion?.ToString() ?? string.Empty) equals (r.FolioOperacion ?? string.Empty) into rels
                from r in rels.DefaultIfEmpty()
                join c in controles on (r?.FolioApp ?? string.Empty) equals c.FolioAppOriginal into ctrls
                from c in ctrls.DefaultIfEmpty()
                where TextMatches(m.FolioOperacion?.ToString(), normalized)
                   || TextMatches(m.FolioSoluone, normalized)
                   || TextMatches(r?.FolioPos, normalized)
                   || TextMatches(r?.TaxistaNombre, normalized)
                   || TextMatches(r?.Gafete, normalized)
                   || TextMatches(r?.FolioApp, normalized)
                   || TextMatches(c?.FolioControl, normalized)
                   || TextMatches(o?.StaffNombre, normalized)
                   || TextMatches(o?.Hotel, normalized)
                select new PosPagoEfRow(
                    m.FolioOperacion?.ToString() ?? string.Empty,
                    m.Fecha,
                    m.FolioSoluone ?? string.Empty,
                    ToDecimal(m.TotalJoyeria) + ToDecimal(m.TotalCompra),
                    ToDecimal(m.Comision),
                    ToDecimal(m.Pago),
                    m.FechaPago);

            return query.Take(80).ToList();
        }

        private sealed record PosPagoEfRow(string Folio, DateTime? Fecha, string Referencia, decimal Total, decimal Comision, decimal Pago, DateTime? FechaPago);



public async Task<bool> RegisterPagoAsync(string folioOperacion, decimal importe, string usuario)
        {
            var normalized = Normalize(folioOperacion);
            if (normalized == null)
                return false;

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsureRelacionesTicketTaxistaTableAsync(connection);
            normalized = await ResolveFolioOperacionFromRelacionAsync(connection, normalized);
            if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folio))
                return false;

            var target = await _posContext.MovOperaciones
                .AsNoTracking()
                .Where(x => x.FolioOperacion.HasValue
                    && x.FolioOperacion.Value == folio)
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FolioSoluone)
                .FirstOrDefaultAsync();
            if (target == null)
                return false;

            var comisionActual = ToDecimal(target.Comision);
            var pagoActual = ToDecimal(target.Pago);
            var pendiente = comisionActual - pagoActual;
            if (pendiente <= 0)
                return false;

            var importeAplicar = importe > 0 ? Math.Min(importe, pendiente) : pendiente;
            var pagoFinal = (float)(pagoActual + importeAplicar);
            var affected = await _posContext.MovOperaciones
                .Where(x => x.FolioOperacion == target.FolioOperacion
                    && x.Fecha == target.Fecha
                    && x.FolioSoluone == target.FolioSoluone)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Pago, pagoFinal)
                    .SetProperty(x => x.FechaPago, DateTime.Now));
            if (affected > 0)
                await InsertPosAuditoriaAsync(usuario, "Pagos", "Guardar", normalized ?? string.Empty, $"Pago de comision registrado por {importeAplicar:N2}",
                    BuildAuditJson(("FolioOperacion", normalized), ("Importe", importeAplicar), ("Comision", comisionActual), ("PagoAnterior", pagoActual), ("PagoFinal", pagoFinal)));
            return affected > 0;
        }


    }
}

