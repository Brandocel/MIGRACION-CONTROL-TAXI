using System.Globalization;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
public async Task<PosGastosViewModel?> TryGetGastosAsync(string? folioOperacion = null)
        {
            try
            {
                var normalized = Normalize(folioOperacion);
                if (normalized != null)
                    normalized = await ResolveFolioOperacionFromRelacionEfAsync(normalized);

                var posRows = await GetGastosRowsFromEfAsync(normalized);
                if (posRows.Count > 0)
                {
                    return new PosGastosViewModel
                    {
                        FolioOperacion = Normalize(folioOperacion) ?? string.Empty,
                        ImporteCaptura = posRows.First().Importe,
                        TotalDia = posRows.Sum(x => x.Importe),
                        Filas = posRows.Select(x => new List<string>
                        {
                            FormatDate(x.Fecha),
                            x.Concepto,
                            x.Importe.ToString("N2", CultureInfo.InvariantCulture),
                            x.Observaciones,
                            "APLICADO"
                        }).ToList()
                    };
                }
                return new PosGastosViewModel
                {
                    FolioOperacion = Normalize(folioOperacion) ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Gastos desde POS.");
                return new PosGastosViewModel { FolioOperacion = Normalize(folioOperacion) ?? string.Empty };
            }
        }

        private async Task<List<PosGastoEfRow>> GetGastosRowsFromEfAsync(string? normalized)
        {
            if (normalized == null)
            {
                var today = DateTime.Today;
                var latestDate = await _posContext.MovOperaciones
                    .AsNoTracking()
                    .Where(x => x.TotalGastos.HasValue && x.TotalGastos.Value > 0f && x.Fecha.HasValue && x.Fecha.Value.Date == today)
                    .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date)
                    ?? await _posContext.MovOperaciones
                        .AsNoTracking()
                        .Where(x => x.TotalGastos.HasValue && x.TotalGastos.Value > 0f && x.Fecha.HasValue)
                        .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date);
                if (!latestDate.HasValue)
                    return new List<PosGastoEfRow>();

                return await _posContext.MovOperaciones
                    .AsNoTracking()
                    .Where(x => x.TotalGastos.HasValue && x.TotalGastos.Value > 0f && x.Fecha.HasValue && x.Fecha.Value.Date == latestDate.Value)
                    .OrderByDescending(x => x.Fecha)
                    .Take(200)
                    .Select(x => new PosGastoEfRow(
                        x.Fecha,
                        "Folio " + (x.FolioOperacion.HasValue ? x.FolioOperacion.Value.ToString() : string.Empty),
                        ToDecimal(x.TotalGastos),
                        x.FolioSoluone ?? string.Empty))
                    .ToListAsync();
            }

            var movimientos = await _posContext.MovOperaciones.AsNoTracking()
                .Where(x => x.TotalGastos.HasValue && x.TotalGastos.Value > 0f)
                .OrderByDescending(x => x.Fecha)
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
                select new PosGastoEfRow(
                    m.Fecha,
                    "Folio " + (m.FolioOperacion?.ToString() ?? string.Empty),
                    ToDecimal(m.TotalGastos),
                    m.FolioSoluone ?? string.Empty);

            return query.Take(200).ToList();
        }

        private sealed record PosGastoEfRow(DateTime? Fecha, string Concepto, decimal Importe, string Observaciones);



public async Task<bool> UpdateGastoAsync(string folioOperacion, decimal importe, string concepto, string observaciones, string usuario)
        {
            if (importe <= 0 || string.IsNullOrWhiteSpace(concepto))
                return false;

            var normalized = Normalize(folioOperacion);
            if (normalized == null)
                return false;

            normalized = await ResolveFolioOperacionFromRelacionEfAsync(normalized);
            if (!long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folio))
                return false;

            var conceptoFinal = string.IsNullOrWhiteSpace(concepto) ? "GASTO POS" : concepto.Trim();
            var observacionesFinal = string.IsNullOrWhiteSpace(observaciones) ? string.Empty : observaciones.Trim();
            if (!string.IsNullOrWhiteSpace(folioOperacion))
                observacionesFinal = string.IsNullOrWhiteSpace(observacionesFinal) ? $"Folio {folioOperacion}" : $"{observacionesFinal} | Folio {folioOperacion}";

            var target = await _posContext.MovOperaciones
                .AsNoTracking()
                .Where(x => x.FolioOperacion == folio)
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FolioSoluone)
                .FirstOrDefaultAsync();
            if (target == null)
                return false;

            var importeFloat = (float)importe;
            var affected = await _posContext.MovOperaciones
                .Where(x => x.FolioOperacion == target.FolioOperacion
                    && x.Fecha == target.Fecha
                    && x.FolioSoluone == target.FolioSoluone)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.TotalGastos, x => (x.TotalGastos ?? 0f) + importeFloat));
            if (affected > 0)
                await InsertPosAuditoriaAsync(usuario, "Gastos", "Guardar", normalized ?? string.Empty, $"Gasto {conceptoFinal} por {importe:N2}. {observacionesFinal}",
                    BuildAuditJson(("FolioOperacion", normalized), ("Importe", importe), ("Concepto", conceptoFinal), ("Observaciones", observacionesFinal)));
            return affected > 0;
        }

        private async Task<string> ResolveFolioOperacionFromRelacionEfAsync(string normalized)
        {
            var relaciones = await _posContext.RelacionesTicketTaxista
                .AsNoTracking()
                .Where(x => x.FolioOperacion != null && x.FolioOperacion != string.Empty)
                .OrderByDescending(x => x.FechaActualizacion)
                .Take(1000)
                .ToListAsync();
            var folioApps = relaciones
                .Select(x => x.FolioApp ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var controles = await _posContext.AppMovilFolioControl
                .AsNoTracking()
                .ToListAsync();
            controles = controles
                .Where(x => x.FolioAppOriginal != null && folioApps.Contains(x.FolioAppOriginal))
                .ToList();
            var controlMap = controles
                .GroupBy(x => x.FolioAppOriginal ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().FolioControl ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var match = relaciones.FirstOrDefault(x =>
                TextMatches(x.FolioOperacion, normalized) ||
                TextMatches(x.FolioPos, normalized) ||
                TextMatches(x.FolioApp, normalized) ||
                TextMatches(x.TaxistaNombre, normalized) ||
                TextMatches(x.Gafete, normalized) ||
                (x.FolioApp != null && controlMap.TryGetValue(x.FolioApp, out var control) && TextMatches(control, normalized)));

            return string.IsNullOrWhiteSpace(match?.FolioOperacion)
                ? normalized
                : match.FolioOperacion!;
        }


    }
}

