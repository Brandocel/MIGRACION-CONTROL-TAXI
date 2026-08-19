using System.Globalization;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
public async Task<PosCorteViewModel?> TryGetCorteAsync(DateTime? fecha = null)
        {
            try
            {
                var fechaCorte = fecha?.Date ?? await GetLatestCorteDateFromEfAsync();
                if (!fechaCorte.HasValue)
                    return new PosCorteViewModel();

                var targetDate = fechaCorte.Value.Date;
                var movimientos = await _posContext.MovOperaciones
                    .AsNoTracking()
                    .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == targetDate)
                    .ToListAsync();
                if (movimientos.Count == 0)
                    return new PosCorteViewModel();

                var transportes = (await _posContext.Transportes.AsNoTracking().ToListAsync())
                    .SelectMany(x => GetTransportLookupKeys(x.Tipo, x.Nombre).Select(key => new { key, transporte = x }))
                    .GroupBy(x => x.key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First().transporte, StringComparer.OrdinalIgnoreCase);

                var efectivoPos = movimientos.Sum(x => ToDecimal(x.TotalEfectivo));
                var tarjetaPos = movimientos.Sum(x => ToDecimal(x.TotalTarjeta));
                var totalPos = movimientos.Sum(x =>
                    ToDecimal(x.TotalJoyeria) +
                    ToDecimal(x.TotalCompra) +
                    ToDecimal(x.TotalArtesania) +
                    ToDecimal(x.TotalLicor) +
                    ToDecimal(x.TotalFarmacia));
                if (totalPos > 0)
                {
                    return new PosCorteViewModel
                    {
                        Fecha = targetDate,
                        Efectivo = efectivoPos,
                        Tarjeta = tarjetaPos,
                        Amex = 0m,
                        Gastos = movimientos.Sum(x => ToDecimal(x.TotalGastos)),
                        Comisiones = movimientos.Sum(x => CalculateCorteComision(x, transportes)),
                        TotalDia = totalPos,
                        Diferencia = totalPos - (efectivoPos + tarjetaPos),
                        Movimientos = movimientos.Count,
                        Cerrado = await IsCorteClosedAsync(targetDate)
                    };
                }
                return new PosCorteViewModel();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Corte desde POS.");
                return new PosCorteViewModel();
            }
        }

        private async Task<DateTime?> GetLatestCorteDateFromEfAsync()
        {
            var today = DateTime.Today;
            var todayDate = await _posContext.MovOperaciones
                .AsNoTracking()
                .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == today)
                .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date);
            if (todayDate.HasValue)
                return todayDate.Value;

            return await _posContext.MovOperaciones
                .AsNoTracking()
                .Where(x => x.Fecha.HasValue)
                .MaxAsync(x => (DateTime?)x.Fecha!.Value.Date);
        }

        private static decimal CalculateCorteComision(PosMovOperacion movimiento, IReadOnlyDictionary<string, PosTransporte> transportes)
        {
            transportes.TryGetValue(NormalizeKey(movimiento.TransporteTipo), out var transporte);
            transporte ??= ResolveTransportFromCatalog(transportes, movimiento.TransporteTipo);
            var totalVenta = ToDecimal(movimiento.TotalJoyeria) + ToDecimal(movimiento.TotalCompra);
            var dejada = ToDecimal(movimiento.Dejada);
            var bebidas = ToDecimal(movimiento.TotalLicor);
            var degustacion = ToDecimal(movimiento.TotalGastos);
            var descuentoBase = ToDecimal(movimiento.TotalTarjeta) > 0
                ? ToDecimal(transporte?.Tarjeta)
                : ToDecimal(transporte?.Efectivo);
            var descuento = descuentoBase / 100m;
            var porcentaje = ToDecimal(transporte?.Comision) / 100m;
            var baseComision = descuento == 0m
                ? (totalVenta - dejada - bebidas - degustacion) * porcentaje
                : ((totalVenta - (totalVenta * descuento)) - dejada - bebidas - degustacion) * porcentaje;
            return Math.Truncate(baseComision);
        }

        private static string NormalizeKey(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();



        public async Task<bool> CloseCorteAsync(string usuario, DateTime fecha)
        {
            var corte = await TryGetCorteAsync(fecha) ?? new PosCorteViewModel();
            var targetDate = corte.Fecha.Date;
            var row = await _appContext.Cortes.FirstOrDefaultAsync(x => x.Fecha == targetDate);
            if (row == null)
            {
                row = new AppCorte { Fecha = targetDate };
                await _appContext.Cortes.AddAsync(row);
            }

            row.Usuario = string.IsNullOrWhiteSpace(usuario) ? "WEB" : usuario.Trim();
            row.Efectivo = corte.Efectivo;
            row.Tarjeta = corte.Tarjeta;
            row.Total = corte.TotalDia;
            row.Diferencia = corte.Diferencia;
            row.Estatus = "Cerrado";
            row.FechaCierre = DateTime.UtcNow;
            await _appContext.SaveChangesAsync();
            await InsertPosAuditoriaAsync(usuario, "Cortes", "Cerrar", corte.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Corte cerrado y bloqueado",
                BuildAuditJson(("Fecha", corte.Fecha.Date), ("Efectivo", corte.Efectivo), ("Tarjeta", corte.Tarjeta), ("TotalDia", corte.TotalDia), ("Diferencia", corte.Diferencia)));
            return true;
        }


    }
}

