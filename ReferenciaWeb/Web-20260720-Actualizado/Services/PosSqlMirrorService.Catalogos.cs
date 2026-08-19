using System.Globalization;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        public async Task<bool> SaveTransporteAsync(string clave, string nombre, decimal minimo, decimal maximo, decimal comision, decimal descEfectivo, decimal descTarjeta, decimal descAmex, string usuario)
        {
            var normalizedClave = Normalize(clave);
            var normalizedNombre = Normalize(nombre);
            if (normalizedClave == null || normalizedNombre == null || minimo > maximo)
                return false;

            var transporte = await _posContext.Transportes
                .FirstOrDefaultAsync(x => x.Tipo == normalizedClave);
            if (transporte == null)
            {
                transporte = new PosTransporte
                {
                    Tipo = normalizedClave,
                    Moneda = string.Empty,
                    Impuestos = 0,
                    Dejada = 0,
                    Dpto = string.Empty
                };
                await _posContext.Transportes.AddAsync(transporte);
            }

            transporte.Nombre = normalizedNombre;
            transporte.Minimo = (float)minimo;
            transporte.Maximo = (float)maximo;
            transporte.Comision = (float)comision;
            transporte.Efectivo = Convert.ToInt32(descEfectivo);
            transporte.Tarjeta = Convert.ToInt32(descTarjeta);
            transporte.Amexco = Convert.ToInt32(descAmex);

            await _posContext.SaveChangesAsync();
            await InsertPosAuditoriaAsync(usuario, "Transportes", "Guardar", normalizedClave, $"Transporte {normalizedNombre}",
                BuildAuditJson(("Clave", normalizedClave), ("Nombre", normalizedNombre), ("Minimo", minimo), ("Maximo", maximo), ("Comision", comision), ("DescEfectivo", descEfectivo), ("DescTarjeta", descTarjeta), ("DescAmex", descAmex)));
            return true;
        }

        public async Task<bool> SaveGuiaAsync(string clave, string nombre, string telefono, decimal comision, string estatus, string usuario)
        {
            var normalizedClave = Normalize(clave);
            if (normalizedClave == null || !int.TryParse(normalizedClave, NumberStyles.Integer, CultureInfo.InvariantCulture, out var matricula))
                return false;

            var guia = await _posContext.DeptoGuias.FirstOrDefaultAsync(x => x.Matricula == matricula);
            if (guia == null)
            {
                guia = new PosDeptoGuia { Depto = 10, Matricula = matricula };
                await _posContext.DeptoGuias.AddAsync(guia);
            }

            guia.Porcentaje = Convert.ToInt32(comision);
            await _posContext.SaveChangesAsync();
            await InsertPosAuditoriaAsync(usuario, "Guias", "Guardar", normalizedClave, $"Guia matricula {normalizedClave}",
                BuildAuditJson(("Matricula", matricula), ("Nombre", nombre), ("Telefono", telefono), ("Comision", comision), ("Estatus", estatus)));
            return true;
        }

        public async Task<bool> SaveTaxistaAsync(string clave, string nombre, string telefono, string unidad, string placas, string estatus, string usuario)
        {
            var normalizedClave = Normalize(clave);
            var normalizedNombre = Normalize(nombre);
            if (normalizedClave == null || normalizedNombre == null || !long.TryParse(normalizedClave, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idTaxi))
                return false;

            var taxista = await _posContext.Taxistas.FirstOrDefaultAsync(x => x.IdTaxi == idTaxi);
            if (taxista == null)
            {
                taxista = new PosCataxi { IdTaxi = idTaxi };
                await _posContext.Taxistas.AddAsync(taxista);
            }

            taxista.Nombre = normalizedNombre;
            taxista.Telefono = telefono?.Trim() ?? string.Empty;
            taxista.Tipo = string.IsNullOrWhiteSpace(unidad) ? "TAXI" : unidad.Trim();
            taxista.Activo = string.Equals(estatus, "Inactivo", StringComparison.OrdinalIgnoreCase) ? "N" : "S";

            await _posContext.SaveChangesAsync();
            await InsertPosAuditoriaAsync(usuario, "Taxistas", "Guardar", normalizedClave, $"Taxista {normalizedNombre}",
                BuildAuditJson(("Clave", normalizedClave), ("Nombre", normalizedNombre), ("Telefono", telefono), ("Unidad", unidad), ("Placas", placas), ("Estatus", estatus)));
            return true;
        }

        public async Task<PosCatalogoViewModel?> TryGetTransportesAsync()
        {
            try
            {
                var posRows = await _posContext.Transportes
                    .AsNoTracking()
                    .OrderBy(x => x.Nombre)
                    .Take(200)
                    .ToListAsync();
                if (posRows.Count > 0)
                {
                    return new PosCatalogoViewModel
                    {
                        Titulo = "TIPOS DE TRANSPORTE",
                        Descripcion = "Datos cargados desde POS transporte",
                        Columnas = new List<string> { "CLAVE", "NOMBRE", "MINIMO", "MAXIMO", "COMISION", "EFECTIVO", "TARJETA", "AMEX", "MONEDA" },
                        Filas = posRows.Select(x => new List<string>
                        {
                            x.Tipo,
                            x.Nombre ?? string.Empty,
                            ToDecimal(x.Minimo).ToString("N2", CultureInfo.InvariantCulture),
                            ToDecimal(x.Maximo).ToString("N2", CultureInfo.InvariantCulture),
                            ToDecimal(x.Comision).ToString("N4", CultureInfo.InvariantCulture),
                            ToDecimal(x.Efectivo).ToString("N4", CultureInfo.InvariantCulture),
                            ToDecimal(x.Tarjeta).ToString("N4", CultureInfo.InvariantCulture),
                            ToDecimal(x.Amexco).ToString("N4", CultureInfo.InvariantCulture),
                            x.Moneda ?? string.Empty
                        }).ToList()
                    };
                }
                return new PosCatalogoViewModel { Titulo = "TIPOS DE TRANSPORTE" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Transportes desde POS.");
                return null;
            }
        }

        public async Task<PosCatalogoViewModel?> TryGetGuiasAsync()
        {
            try
            {
                var deptos = await _posContext.DeptoGuias
                    .AsNoTracking()
                    .OrderBy(x => x.Matricula)
                    .Take(200)
                    .ToListAsync();
                var matriculas = deptos.Select(x => x.Matricula).ToHashSet();
                var empleados = await _posContext.Empleados
                    .AsNoTracking()
                    .ToListAsync();
                empleados = empleados
                    .Where(x => x.CajeroId.HasValue && matriculas.Contains(x.CajeroId.Value))
                    .ToList();
                var nombres = empleados
                    .Where(x => x.CajeroId.HasValue)
                    .GroupBy(x => x.CajeroId!.Value)
                    .ToDictionary(x => x.Key, x => x.First().CajeroNombre ?? string.Empty);

                if (deptos.Count > 0)
                {
                    return new PosCatalogoViewModel
                    {
                        Titulo = "GUIAS",
                        Descripcion = "Datos cargados desde POS deptoguia",
                        Columnas = new List<string> { "MATRICULA", "NOMBRE", "DEPTO", "PORCENTAJE" },
                        Filas = deptos.Select(x => new List<string>
                        {
                            x.Matricula.ToString(CultureInfo.InvariantCulture),
                            nombres.TryGetValue(x.Matricula, out var nombre) ? nombre : string.Empty,
                            x.Depto.ToString(CultureInfo.InvariantCulture),
                            ToDecimal(x.Porcentaje).ToString("N0", CultureInfo.InvariantCulture)
                        }).ToList()
                    };
                }
                return new PosCatalogoViewModel { Titulo = "GUIAS" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Guias desde POS.");
                return null;
            }
        }

        public async Task<PosCatalogoViewModel?> TryGetTaxistasAsync(string? busqueda = null, int pagina = 1, int tamanoPagina = 50)
        {
            try
            {
                var fuente = new List<TaxistaCatalogoRow>();
                fuente.AddRange((await _posContext.Taxistas
                    .AsNoTracking()
                    .Where(x => x.Nombre != null && x.Nombre.Trim() != string.Empty)
                    .ToListAsync())
                    .Select(x => new TaxistaCatalogoRow(
                        x.IdTaxi.ToString(CultureInfo.InvariantCulture),
                        CleanTaxiName(x.Nombre),
                        x.Tipo ?? string.Empty,
                        string.Empty,
                        x.Telefono ?? string.Empty,
                        string.Equals(x.Activo, "N", StringComparison.OrdinalIgnoreCase) ? "Inactivo" : "Activo",
                        3,
                        null)));

                fuente.AddRange((await _posContext.Dejadas
                    .AsNoTracking()
                    .Where(x => x.IdTaxi.HasValue && x.IdTaxi.Value > 0 && x.NombreVendedor != null && x.NombreVendedor.Trim() != string.Empty)
                    .OrderByDescending(x => x.Fecha)
                    .Take(2000)
                    .ToListAsync())
                    .Select(x => new TaxistaCatalogoRow(
                        x.IdTaxi!.Value.ToString(CultureInfo.InvariantCulture),
                        CleanTaxiName(x.NombreVendedor),
                        x.TipoTransporte ?? string.Empty,
                        x.Unidad ?? string.Empty,
                        x.Telefono ?? string.Empty,
                        "Activo",
                        2,
                        x.Fecha)));

                fuente.AddRange((await _posContext.AppMovilRegistros
                    .AsNoTracking()
                    .Where(x => x.IdCatalogo.HasValue && x.IdCatalogo.Value > 0 && x.VendedorNombre != null && x.VendedorNombre.Trim() != string.Empty)
                    .OrderByDescending(x => x.FechaOperacion)
                    .Take(2000)
                    .ToListAsync())
                    .Select(x => new TaxistaCatalogoRow(
                        x.IdCatalogo!.Value.ToString(CultureInfo.InvariantCulture),
                        CleanTaxiName(x.VendedorNombre),
                        FirstText(x.TipoOperacion, x.Unidad),
                        FirstText(x.Placas, x.Unidad),
                        x.TelefonoTaxista ?? string.Empty,
                        "Activo",
                        1,
                        x.FechaOperacion)));

                var normalizedSearch = Normalize(busqueda);
                var filtered = fuente
                    .Where(x => !string.IsNullOrWhiteSpace(x.Nombre))
                    .GroupBy(x => x.Clave, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.OrderBy(y => y.Prioridad).ThenByDescending(y => y.Fecha).First())
                    .Where(x => normalizedSearch == null
                        || TextMatches(x.Clave, normalizedSearch)
                        || TextMatches(x.Nombre, normalizedSearch)
                        || TextMatches(x.Unidad, normalizedSearch)
                        || TextMatches(x.Placas, normalizedSearch)
                        || TextMatches(x.Telefono, normalizedSearch))
                    .OrderBy(x => x.Nombre)
                    .ThenBy(x => x.Clave)
                    .ToList();
                var total = filtered.Count;
                var pageSize = Math.Clamp(tamanoPagina, 10, 200);
                var page = Math.Max(pagina, 1);
                var totalPages = Math.Max((int)Math.Ceiling(total / (double)pageSize), 1);
                if (page > totalPages)
                    page = totalPages;
                var rows = filtered
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
                if (rows.Count == 0)
                {
                    return new PosCatalogoViewModel
                    {
                        Titulo = "TAXISTAS",
                        Descripcion = "Catalogo de taxistas",
                        Columnas = new List<string> { "CLAVE", "NOMBRE", "UNIDAD", "PLACAS", "TELEFONO", "ESTATUS" },
                        Busqueda = busqueda ?? string.Empty,
                        Pagina = page,
                        TotalPaginas = totalPages,
                        TotalFilas = total
                    };
                }

                return new PosCatalogoViewModel
                {
                    Titulo = "TAXISTAS",
                    Descripcion = "Catalogo de taxistas",
                    Columnas = new List<string> { "CLAVE", "NOMBRE", "UNIDAD", "PLACAS", "TELEFONO", "ESTATUS" },
                    Busqueda = busqueda ?? string.Empty,
                    Pagina = page,
                    TotalPaginas = totalPages,
                    TotalFilas = total,
                    Filas = rows.Select(row => new List<string>
                    {
                        row.Clave,
                        row.Nombre,
                        row.Unidad,
                        row.Placas,
                        row.Telefono,
                        row.Estatus
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar Taxistas desde POS.");
                return null;
            }
        }

        private sealed record TaxistaCatalogoRow(
            string Clave,
            string Nombre,
            string Unidad,
            string Placas,
            string Telefono,
            string Estatus,
            int Prioridad,
            DateTime? Fecha);

        private static string CleanTaxiName(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("|", string.Empty, StringComparison.Ordinal).Trim();
    }
}
