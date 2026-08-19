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
private async Task<List<CompuadmoRemisionHeader>> FindRemisionesAsync(string? folioOperacion, int take, bool track = false)
        {
            var normalized = Normalize(folioOperacion);
            IQueryable<CompuadmoRemisionHeader> query = _dbContext.Remisiones;
            if (!track)
                query = query.AsNoTracking();

            if (normalized == null)
            {
                var today = DateTime.Today;
                query = query.Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == today);
            }
            else
            {
                query = query.Where(x =>
                    x.FolioOperacion.ToString() == normalized ||
                    x.FolioRemision == normalized ||
                    x.FolioFactura == normalized ||
                    x.FolioRegistro.ToString() == normalized);
            }

            return await query
                .OrderByDescending(x => x.Fecha)
                .ThenByDescending(x => x.FolioRemision)
                .Take(take)
                .ToListAsync();
        }





private static string FormatFolioOperacion(CompuadmoRemisionHeader row)
        {
            if (row.FolioOperacion.HasValue && row.FolioOperacion.Value > 0)
                return row.FolioOperacion.Value.ToString(CultureInfo.InvariantCulture);

            return FirstText(row.FolioRemision, row.FolioFactura, row.FolioRegistro?.ToString(CultureInfo.InvariantCulture));
        }





private static string ResolveFormaPago(decimal efectivo, decimal tarjeta, decimal dolares)
        {
            var active = new List<string>();
            if (efectivo > 0) active.Add("EFECTIVO");
            if (tarjeta > 0) active.Add("TARJETA");
            if (dolares > 0) active.Add("DOLARES");
            return active.Count == 0 ? "SIN PAGO" : string.Join(" / ", active);
        }





private static int BuildFolioOperacion(DateTime saleDate)
        {
            var raw = saleDate.ToString("ddHHmmss", CultureInfo.InvariantCulture);
            return int.TryParse(raw, out var parsed) ? parsed : 0;
        }





private static string UpsertNoteValue(string? notes, string key, string value)
        {
            var marker = $"{key}:";
            var parts = (notes ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !x.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                .ToList();
            parts.Add($"{marker}{value}");
            return string.Join(" | ", parts);
        }





private static int ExtractPassengerCount(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return 0;

            const string marker = "PAX:";
            var start = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return 0;

            var value = new string(notes[(start + marker.Length)..].TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }




    }
}
