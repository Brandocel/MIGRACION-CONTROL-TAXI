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
public async Task<PosVentasViewModel?> TryGetVentasAsync(string? productoBusqueda = null)
        {
            try
            {
                var normalized = Normalize(productoBusqueda);
                if (normalized == null)
                {
                    return new PosVentasViewModel
                    {
                        ProductoBusqueda = string.Empty,
                        TipoCambio = "19.50",
                        Cantidad = 1,
                        Pax = 1
                    };
                }

                var originalBusqueda = normalized;
                var numericFromTicket = normalized;
                if (normalized.Length > 1 && normalized[0] == 'M' && normalized[1..].All(char.IsDigit))
                {
                    numericFromTicket = normalized[1..];
                }
                var prefixedTicket = numericFromTicket.All(char.IsDigit)
                    ? "M" + long.Parse(numericFromTicket, CultureInfo.InvariantCulture).ToString("D8", CultureInfo.InvariantCulture)
                    : normalized;

                if (LooksLikeAppFolio(normalized))
                {
                    var appConnection = _dbContext.Database.GetDbConnection();
                    if (appConnection.State != ConnectionState.Open)
                        await appConnection.OpenAsync();
                    await EnsureRelacionesTicketTaxistaTableAsync(appConnection);
                    var appRelation = await ResolveVentaRelacionAsync(appConnection, null, normalized, prefixedTicket, normalized, normalized);
                    if (appRelation != null)
                    {
                        StoreVentaInfo? appStoreVenta = null;
                        var relationTicket = FirstText(appRelation.FolioPos, appRelation.FolioOperacion);
                        if (!string.IsNullOrWhiteSpace(relationTicket))
                        {
                            var relationNumeric = relationTicket;
                            if (relationNumeric.Length > 1 && relationNumeric[0] == 'M' && relationNumeric[1..].All(char.IsDigit))
                                relationNumeric = relationNumeric[1..];
                            var relationPrefixed = relationNumeric.All(char.IsDigit)
                                ? "M" + long.Parse(relationNumeric, CultureInfo.InvariantCulture).ToString("D8", CultureInfo.InvariantCulture)
                                : relationTicket;
                            appStoreVenta = await GetStoreVentaAsync(relationTicket, relationNumeric, relationPrefixed);
                        }
                        if (appStoreVenta != null)
                            return await BuildStoreVentaModelAsync(productoBusqueda ?? string.Empty, appStoreVenta, appRelation);

                        return BuildRelationVentaModel(productoBusqueda ?? string.Empty, appRelation);
                    }

                    var directAppStoreVenta = await GetStoreVentaForAppRelationAsync(normalized, DateTime.Today);
                    if (directAppStoreVenta != null)
                    {
                        var directAppRelation = await ResolveVentaRelacionForStoreVentaAsync(directAppStoreVenta);
                        return await BuildStoreVentaModelAsync(productoBusqueda ?? string.Empty, directAppStoreVenta, directAppRelation);
                    }
                }
                else
                {
                    var directStoreVenta = await GetStoreVentaAsync(originalBusqueda, numericFromTicket, prefixedTicket);
                    if (directStoreVenta != null)
                    {
                        var directRelation = await ResolveVentaRelacionForStoreVentaAsync(directStoreVenta);
                        return await BuildStoreVentaModelAsync(productoBusqueda ?? string.Empty, directStoreVenta, directRelation);
                    }
                }

                var resolvedConnection = _dbContext.Database.GetDbConnection();
                if (resolvedConnection.State != ConnectionState.Open)
                    await resolvedConnection.OpenAsync();
                await EnsureRelacionesTicketTaxistaTableAsync(resolvedConnection);
                normalized = await ResolveFolioOperacionFromRelacionAsync(resolvedConnection, normalized) ?? normalized;
                numericFromTicket = normalized;
                if (normalized.Length > 1 && normalized[0] == 'M' && normalized[1..].All(char.IsDigit))
                {
                    numericFromTicket = normalized[1..];
                }
                prefixedTicket = numericFromTicket.All(char.IsDigit)
                    ? "M" + long.Parse(numericFromTicket, CultureInfo.InvariantCulture).ToString("D8", CultureInfo.InvariantCulture)
                    : normalized;

                var venta = await (
                    from o in _posContext.Operaciones.AsNoTracking()
                    where o.Folio.HasValue
                       && (
                            o.Folio.Value.ToString() == normalized
                         || o.Folio.Value.ToString() == numericFromTicket
                         || o.FolioSoluone == normalized
                       )
                    join m in _posContext.MovOperaciones.AsNoTracking()
                        on o.Folio equals m.FolioOperacion into movs
                    from m in movs.DefaultIfEmpty()
                    where m == null
                       || m.FolioSoluone == normalized
                       || (o.Folio ?? 0).ToString() == normalized
                       || (o.Folio ?? 0).ToString() == numericFromTicket
                    orderby (m.Fecha ?? o.Fecha) descending
                    select new
                    {
                        o.Folio,
                        o.Fecha,
                        Cliente = o.Hotel,
                        Vendedor = o.StaffNombre,
                        o.IdStaff,
                        o.Pax,
                        Transporte = m.TransporteTipo ?? o.TransporteTipo ?? string.Empty,
                        TotalJoyeria = ToDecimal(m.TotalJoyeria),
                        TotalCompra = ToDecimal(m.TotalCompra),
                        TotalArtesania = ToDecimal(m.TotalArtesania),
                        TotalLicor = ToDecimal(m.TotalLicor),
                        TotalFarmacia = ToDecimal(m.TotalFarmacia),
                        Iva = ToDecimal(m.Impuestos),
                        Efectivo = ToDecimal(m.TotalEfectivo),
                        Tarjeta = ToDecimal(m.TotalTarjeta),
                        Referencia = m.FolioSoluone ?? o.FolioSoluone ?? string.Empty
                    })
                    .FirstOrDefaultAsync();

                if (venta != null)
                {
                    var detalle = new List<PosVentaDetalleViewModel>();
                    AddVentaDetalle(detalle, "JOYERIA", venta.TotalJoyeria);
                    AddVentaDetalle(detalle, "COMPRA", venta.TotalCompra);
                    AddVentaDetalle(detalle, "ARTESANIA", venta.TotalArtesania);
                    AddVentaDetalle(detalle, "LICOR", venta.TotalLicor);
                    AddVentaDetalle(detalle, "FARMACIA", venta.TotalFarmacia);

                    var subtotalVenta = detalle.Sum(x => x.Precio);
                    var ivaVenta = venta.Iva;
                    return new PosVentasViewModel
                    {
                        ProductoBusqueda = normalized,
                        Vendedor = FirstText(venta.Vendedor, venta.IdStaff?.ToString(CultureInfo.InvariantCulture)),
                        Cliente = venta.Cliente ?? string.Empty,
                        Usuario = "WEB",
                        TransporteTipo = FirstText(venta.Transporte, "WEB"),
                        Cantidad = detalle.Count == 0 ? 1 : detalle.Sum(x => x.Cantidad),
                        Pax = Math.Max(venta.Pax ?? 0, 1),
                        TipoCambio = "19.50",
                        Efectivo = venta.Efectivo,
                        Tarjeta = venta.Tarjeta,
                        Items = detalle.Count == 0
                            ? new List<PosVentaDetalleViewModel>
                            {
                                new()
                                {
                                    Cantidad = 1,
                                    Producto = $"FOLIO {normalized}",
                                    Departamento = "VENTA",
                                    Precio = 0,
                                    Importe = 0
                                }
                            }
                            : detalle,
                        Subtotal = subtotalVenta,
                        Iva = ivaVenta,
                        Total = subtotalVenta + ivaVenta
                    };
                }

                var setupConnection = _dbContext.Database.GetDbConnection();
                if (setupConnection.State != ConnectionState.Open)
                    await setupConnection.OpenAsync();
                await EnsureRelacionesTicketTaxistaTableAsync(setupConnection);
                var relacion = await ResolveVentaRelacionAsync(setupConnection, null, normalized, prefixedTicket, normalized, normalized);
                if (relacion != null)
                {
                    var storeVentaRelacion = await GetStoreVentaForAppRelationAsync(relacion.FolioApp, relacion.FechaRegistro);
                    if (storeVentaRelacion != null)
                        return await BuildStoreVentaModelAsync(productoBusqueda ?? string.Empty, storeVentaRelacion, relacion);

                    return BuildRelationVentaModel(productoBusqueda ?? string.Empty, relacion);
                }

                var storeVenta = await GetStoreVentaAsync(normalized, numericFromTicket, prefixedTicket);
                if (storeVenta != null)
                {
                    var storeRelation = await ResolveVentaRelacionForStoreVentaAsync(storeVenta);
                    return await BuildStoreVentaModelAsync(productoBusqueda ?? string.Empty, storeVenta, storeRelation);
                }

                var suspendedVenta = await TryGetSuspendedGafeteVentaModelAsync(productoBusqueda ?? string.Empty, normalized);
                if (suspendedVenta != null)
                    return suspendedVenta;

                var query = _dbContext.Products.AsNoTracking()
                    .Where(x => (x.Active ?? "S").ToUpper() != "N");

                query = query.Where(x =>
                    x.ProductId.ToString() == normalized ||
                    x.Barcode == normalized ||
                    EF.Functions.Like(x.Name ?? string.Empty, $"%{normalized}%"));

                var products = await query
                    .OrderBy(x => x.Name)
                    .Take(30)
                    .ToListAsync();

                var items = products.Select(x =>
                {
                    var price = ResolveProductPrice(x);
                    var iva = ResolveTax(price, ToDecimal(x.Iva));
                    return new PosVentaDetalleViewModel
                    {
                        ProductoId = x.ProductId,
                        Cantidad = 1,
                        Producto = $"{x.ProductId} - {x.Name}",
                        Departamento = x.Department ?? string.Empty,
                        Precio = price,
                        Iva = iva,
                        Importe = price + iva
                    };
                }).ToList();

                return new PosVentasViewModel
                {
                    ProductoBusqueda = productoBusqueda ?? string.Empty,
                    ProductoId = products.FirstOrDefault()?.ProductId ?? 0,
                    Vendedor = string.Empty,
                    Cliente = string.Empty,
                    Cantidad = 1,
                    Pax = 1,
                    TipoCambio = "19.50",
                    ResultadosBusqueda = items
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar productos para Ventas desde Productos.");
                return null;
            }
        }

        public async Task<PosVentaDetalleViewModel?> TryGetProductoVentaAsync(int productoId)
        {
            var product = await _dbContext.Products.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProductId == productoId && (x.Active ?? "S").ToUpper() != "N");
            if (product == null)
                return null;

            var price = ResolveProductPrice(product);
            var iva = ResolveTax(price, ToDecimal(product.Iva));
            return new PosVentaDetalleViewModel
            {
                ProductoId = product.ProductId,
                Cantidad = 1,
                Producto = $"{product.ProductId} - {product.Name}",
                Departamento = product.Department ?? string.Empty,
                Precio = price,
                Iva = iva,
                Importe = price + iva
            };
        }

        private async Task<StoreVentaInfo?> GetStoreVentaAsync(string normalized, string numericFromTicket, string prefixedTicket)
        {
            try
            {
                var folioNumber = long.TryParse(numericFromTicket, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFolio)
                    ? parsedFolio
                    : (long?)null;
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (20) *
FROM
(
    SELECT
        'COMP' AS Origen,
        CONVERT(nvarchar(30), folio_remision) AS Folio,
        CONVERT(nvarchar(30), folio_factura) AS Factura,
        fecha AS Fecha,
        COALESCE(cliente, '') AS Cliente,
        COALESCE(NULLIF(vendedor, ''), '') AS Vendedor,
        COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS TaxistaNombre,
        COALESCE(usuario, '') AS Usuario,
        COALESCE(stotal, 0) AS Subtotal,
        COALESCE(iva, 0) AS Iva,
        COALESCE(total, 0) AS Total,
        COALESCE(efectivo, 0) AS Efectivo,
        COALESCE(tarjeta, 0) AS Tarjeta,
        COALESCE(dolares, 0) AS Dolares,
        COALESCE(tipo_cambio, 0) AS TipoCambio,
        CONVERT(nvarchar(30), folioregistro) AS FolioRegistro
    FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE (@folioNumber IS NOT NULL AND (folio_operacion = @folioNumber OR folioregistro = @folioNumber))
       OR UPPER(folio_remision) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
       OR UPPER(folio_factura) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
    UNION ALL
    SELECT
        'JOY' AS Origen,
        CONVERT(nvarchar(30), folio_pedido) AS Folio,
        CONVERT(nvarchar(30), folio_factura) AS Factura,
        fecha AS Fecha,
        COALESCE(cliente, '') AS Cliente,
        CONVERT(nvarchar(30), COALESCE(vendedor, 0)) AS Vendedor,
        COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS TaxistaNombre,
        COALESCE(usuario, '') AS Usuario,
        COALESCE(stotal, 0) AS Subtotal,
        COALESCE(iva, 0) AS Iva,
        COALESCE(total, 0) AS Total,
        0 AS Efectivo,
        0 AS Tarjeta,
        0 AS Dolares,
        COALESCE(tipo_cambio, 0) AS TipoCambio,
        CONVERT(nvarchar(30), folio_registro) AS FolioRegistro
    FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE (@folioNumber IS NOT NULL AND (folio_operacion = @folioNumber OR folio_registro = @folioNumber))
       OR UPPER(folio_pedido) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
       OR UPPER(folio_factura) IN (UPPER(@folio), UPPER(@ticket), UPPER(@prefixedTicket))
) ventas
ORDER BY Fecha DESC",
                    ("@folio", numericFromTicket),
                    ("@ticket", normalized),
                    ("@prefixedTicket", prefixedTicket),
                    ("@folioNumber", folioNumber));

                var row = rows.FirstOrDefault();
                if (row == null)
                    return null;

                var lines = await LoadStoreVentaLinesAsync(PickText(row, "FolioRegistro"));
                if (lines.Count == 0)
                    lines = rows.Select(BuildStoreVentaLine).ToList();

                return BuildStoreVentaInfo(lines, row);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar venta desde remisioM.");
                return null;
            }
        }

        private async Task<StoreVentaInfo?> GetStoreVentaForAppRelationAsync(string folioApp, DateTime? fechaRegistro)
        {
            var normalized = Normalize(folioApp);
            if (normalized == null || !long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folioNumber))
                return null;
            if (!fechaRegistro.HasValue)
                return null;

            try
            {
                var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (20) *
FROM
(
    SELECT
        'COMP' AS Origen,
        CONVERT(nvarchar(30), folio_remision) AS Folio,
        CONVERT(nvarchar(30), folio_factura) AS Factura,
        fecha AS Fecha,
        COALESCE(cliente, '') AS Cliente,
        COALESCE(NULLIF(vendedor, ''), '') AS Vendedor,
        COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS TaxistaNombre,
        COALESCE(usuario, '') AS Usuario,
        COALESCE(stotal, 0) AS Subtotal,
        COALESCE(iva, 0) AS Iva,
        COALESCE(total, 0) AS Total,
        COALESCE(efectivo, 0) AS Efectivo,
        COALESCE(tarjeta, 0) AS Tarjeta,
        COALESCE(dolares, 0) AS Dolares,
        COALESCE(tipo_cambio, 0) AS TipoCambio,
        CONVERT(nvarchar(30), folioregistro) AS FolioRegistro
    FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE folioregistro = @folio
      AND (@fecha IS NULL OR CAST(fecha AS date) = @fecha)
    UNION ALL
    SELECT
        'JOY' AS Origen,
        CONVERT(nvarchar(30), folio_pedido) AS Folio,
        CONVERT(nvarchar(30), folio_factura) AS Factura,
        fecha AS Fecha,
        COALESCE(cliente, '') AS Cliente,
        CONVERT(nvarchar(30), COALESCE(vendedor, 0)) AS Vendedor,
        COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS TaxistaNombre,
        COALESCE(usuario, '') AS Usuario,
        COALESCE(stotal, 0) AS Subtotal,
        COALESCE(iva, 0) AS Iva,
        COALESCE(total, 0) AS Total,
        0 AS Efectivo,
        0 AS Tarjeta,
        0 AS Dolares,
        COALESCE(tipo_cambio, 0) AS TipoCambio,
        CONVERT(nvarchar(30), folio_registro) AS FolioRegistro
    FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE folio_registro = @folio
      AND (@fecha IS NULL OR CAST(fecha AS date) = @fecha)
) ventas
ORDER BY Fecha DESC",
                    ("@folio", folioNumber),
                    ("@fecha", fechaRegistro?.Date));

                var row = rows.FirstOrDefault();
                if (row == null)
                    return null;

                var lines = rows.Select(BuildStoreVentaLine).ToList();
                return BuildStoreVentaInfo(lines, row);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar venta relacionada desde remisioM.");
                return null;
            }
        }

        private sealed record StoreVentaLineInfo(
            string Origen,
            string Folio,
            string Factura,
            string Cliente,
            string Vendedor,
            string TaxistaNombre,
            string Usuario,
            DateTime? Fecha,
            decimal Subtotal,
            decimal Iva,
            decimal Total,
            decimal Efectivo,
            decimal Tarjeta,
            decimal Dolares,
            decimal TipoCambio,
            string FolioRegistro);

        private sealed record StoreVentaInfo(
            string Origen,
            string Folio,
            string Cliente,
            string Vendedor,
            string TaxistaNombre,
            string Usuario,
            decimal Subtotal,
            decimal Iva,
            decimal Total,
            decimal Efectivo,
            decimal Tarjeta,
            decimal Dolares,
            decimal TipoCambio,
            string FolioRegistro,
            IReadOnlyList<StoreVentaLineInfo> Lines);

        private async Task<List<StoreVentaLineInfo>> LoadStoreVentaLinesAsync(string folioRegistro)
        {
            if (!long.TryParse(Normalize(folioRegistro), NumberStyles.Integer, CultureInfo.InvariantCulture, out var folioNumber))
                return new List<StoreVentaLineInfo>();

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (30) *
FROM
(
    SELECT
        'COMP' AS Origen,
        CONVERT(nvarchar(30), folio_remision) AS Folio,
        CONVERT(nvarchar(30), folio_factura) AS Factura,
        fecha AS Fecha,
        COALESCE(cliente, '') AS Cliente,
        COALESCE(NULLIF(vendedor, ''), '') AS Vendedor,
        COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS TaxistaNombre,
        COALESCE(usuario, '') AS Usuario,
        COALESCE(stotal, 0) AS Subtotal,
        COALESCE(iva, 0) AS Iva,
        COALESCE(total, 0) AS Total,
        COALESCE(efectivo, 0) AS Efectivo,
        COALESCE(tarjeta, 0) AS Tarjeta,
        COALESCE(dolares, 0) AS Dolares,
        COALESCE(tipo_cambio, 0) AS TipoCambio,
        CONVERT(nvarchar(30), folioregistro) AS FolioRegistro
    FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE folioregistro = @folio
    UNION ALL
    SELECT
        'JOY' AS Origen,
        CONVERT(nvarchar(30), folio_pedido) AS Folio,
        CONVERT(nvarchar(30), folio_factura) AS Factura,
        fecha AS Fecha,
        COALESCE(cliente, '') AS Cliente,
        CONVERT(nvarchar(30), COALESCE(vendedor, 0)) AS Vendedor,
        COALESCE(NULLIF(CONVERT(nvarchar(max), observaciones), ''), '') AS TaxistaNombre,
        COALESCE(usuario, '') AS Usuario,
        COALESCE(stotal, 0) AS Subtotal,
        COALESCE(iva, 0) AS Iva,
        COALESCE(total, 0) AS Total,
        0 AS Efectivo,
        0 AS Tarjeta,
        0 AS Dolares,
        COALESCE(tipo_cambio, 0) AS TipoCambio,
        CONVERT(nvarchar(30), folio_registro) AS FolioRegistro
    FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioM]
    WHERE folio_registro = @folio
) ventas
ORDER BY Fecha DESC", ("@folio", folioNumber));

            return rows.Select(BuildStoreVentaLine).ToList();
        }

        private static StoreVentaLineInfo BuildStoreVentaLine(Dictionary<string, object?> row)
        {
            return new StoreVentaLineInfo(
                PickText(row, "Origen"),
                FirstText(PickText(row, "Folio"), PickText(row, "Factura")),
                PickText(row, "Factura"),
                PickText(row, "Cliente"),
                PickText(row, "Vendedor"),
                PickText(row, "TaxistaNombre"),
                FirstText(PickText(row, "Usuario"), "WEB"),
                ToDate(PickObject(row, "Fecha")),
                ToDecimal(PickObject(row, "Subtotal")),
                ToDecimal(PickObject(row, "Iva")),
                ToDecimal(PickObject(row, "Total")),
                ToDecimal(PickObject(row, "Efectivo")),
                ToDecimal(PickObject(row, "Tarjeta")),
                ToDecimal(PickObject(row, "Dolares")),
                ToDecimal(PickObject(row, "TipoCambio")),
                PickText(row, "FolioRegistro"));
        }

        private static StoreVentaInfo BuildStoreVentaInfo(IReadOnlyList<StoreVentaLineInfo> lines, Dictionary<string, object?> fallbackRow)
        {
            var first = lines.FirstOrDefault() ?? BuildStoreVentaLine(fallbackRow);
            return new StoreVentaInfo(
                first.Origen,
                first.Folio,
                FirstText(first.Cliente, lines.Select(x => x.Cliente).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty),
                FirstText(first.Vendedor, lines.Select(x => x.Vendedor).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty),
                FirstText(first.TaxistaNombre, lines.Select(x => x.TaxistaNombre).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty),
                FirstText(first.Usuario, "WEB"),
                lines.Sum(x => x.Subtotal),
                lines.Sum(x => x.Iva),
                lines.Sum(x => x.Total),
                lines.Sum(x => x.Efectivo),
                lines.Sum(x => x.Tarjeta),
                lines.Sum(x => x.Dolares),
                lines.Select(x => x.TipoCambio).FirstOrDefault(x => x > 0m),
                first.FolioRegistro,
                lines.Count == 0 ? new[] { first } : lines);
        }

        private async Task<VentaRelacionInfo?> ResolveVentaRelacionForStoreVentaAsync(StoreVentaInfo storeVenta)
        {
            var folioRegistro = Normalize(storeVenta.FolioRegistro);
            if (folioRegistro == null)
                return null;

            var paddedFolio = folioRegistro;
            if (long.TryParse(folioRegistro, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folioNumber))
                paddedFolio = folioNumber.ToString("D4", CultureInfo.InvariantCulture);

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();

            await EnsureRelacionesTicketTaxistaTableAsync(connection);

            return await ResolveVentaRelacionFromDejadasAsync(connection, folioRegistro, storeVenta.Folio)
                ?? await ResolveVentaRelacionAsync(connection, null, paddedFolio, paddedFolio, storeVenta.TaxistaNombre, string.Empty)
                ?? await ResolveVentaRelacionAsync(connection, null, folioRegistro, folioRegistro, storeVenta.Vendedor, string.Empty)
                ?? await ResolveVentaRelacionFromAppRegistroAsync(connection, paddedFolio, folioRegistro);
        }

        private static async Task<VentaRelacionInfo?> ResolveVentaRelacionFromDejadasAsync(
            DbConnection connection,
            string folioRegistro,
            string folioPos)
        {
            if (!long.TryParse(Normalize(folioRegistro), NumberStyles.Integer, CultureInfo.InvariantCulture, out var folioNumber))
                return null;

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT TOP (1)
    COALESCE(d.codigorecepcion, CONVERT(nvarchar(60), d.folioregistro), d.folioregistrostr, '') AS FolioControl,
    '' AS FolioApp,
    COALESCE(NULLIF(d.gafete, ''), CONVERT(nvarchar(30), COALESCE(d.idtaxi, 0)), '') AS Gafete,
    COALESCE(d.idtaxi, 0) AS TaxistaId,
    COALESCE(NULLIF(t.nombre, ''), NULLIF(d.nombrestaff, ''), NULLIF(d.nombrevendedor, ''), '') AS TaxistaNombre,
    COALESCE(d.tipotransporte, '') AS TransporteTipo,
    COALESCE(d.hotel, d.nombrealmacen, '') AS Hotel,
    COALESCE(d.pax, 1) AS Pax,
    COALESCE(d.total, 0) AS Dejada,
    d.fecha AS FechaRegistro,
    COALESCE(CONVERT(nvarchar(60), d.folioregistro), d.folioregistrostr, '') AS FolioOperacion,
    @folioPos AS FolioPos
FROM {PosTable("dejadas")} d
LEFT JOIN {PosTable("cataxi")} t
    ON t.idtaxi = d.idtaxi
WHERE d.folioregistro = @folio
   OR d.folioregistrostr = @folioText
   OR d.codigorecepcion = @folioText
ORDER BY d.fecha DESC, d.hora DESC";
            AddParameter(command, "@folio", folioNumber);
            AddParameter(command, "@folioText", folioRegistro);
            AddParameter(command, "@folioPos", folioPos);

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

        private static async Task<VentaRelacionInfo?> ResolveVentaRelacionFromAppRegistroAsync(
            DbConnection connection,
            string paddedFolio,
            string rawFolio)
        {
            var numericValue = long.TryParse(rawFolio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : -1;

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT TOP (1)
    COALESCE(c.FolioControl, a.folio_app, '') AS FolioControl,
    a.folio_app AS FolioApp,
    COALESCE(NULLIF(a.folio_gafete, ''), NULLIF(r.Gafete, ''), '') AS Gafete,
    CASE
        WHEN COALESCE(a.id_catalogo, 0) > 0
         AND (
                COALESCE(r.TaxistaId, 0) = 0
             OR CONVERT(nvarchar(30), COALESCE(r.TaxistaId, 0)) = NULLIF(LTRIM(RTRIM(a.folio_gafete)), '')
         )
            THEN a.id_catalogo
        ELSE COALESCE(r.TaxistaId, a.id_catalogo, 0)
    END AS TaxistaId,
    CASE
        WHEN COALESCE(a.id_catalogo, 0) > 0
         AND (
                COALESCE(r.TaxistaId, 0) = 0
             OR CONVERT(nvarchar(30), COALESCE(r.TaxistaId, 0)) = NULLIF(LTRIM(RTRIM(a.folio_gafete)), '')
             OR NULLIF(LTRIM(RTRIM(r.TaxistaNombre)), '') IS NULL
         )
            THEN COALESCE(a.vendedor_nombre, '')
        ELSE COALESCE(NULLIF(r.TaxistaNombre, ''), a.vendedor_nombre, '')
    END AS TaxistaNombre,
    COALESCE(NULLIF(r.TransporteTipo, ''), a.tipo_operacion, '') AS TransporteTipo,
    COALESCE(a.hotel, '') AS Hotel,
    COALESCE(a.pax, 1) AS Pax,
    a.fecha_operacion AS FechaRegistro,
    COALESCE(r.FolioOperacion, '') AS FolioOperacion,
    COALESCE(r.FolioPos, '') AS FolioPos,
    COALESCE(a.total, 0) AS Dejada
FROM {PosTable("AppMovilRegistro")} a
LEFT JOIN {PosTable("AppMovilFolioControl")} c
    ON c.FolioControl = a.folio_app OR c.FolioAppOriginal = a.folio_app_original OR c.FolioAppOriginal = a.folio_app
LEFT JOIN {PosTable("RelacionTicketTaxista")} r
    ON r.FolioApp = a.folio_app OR r.FolioApp = a.folio_app_original
WHERE UPPER(a.folio_app) IN (UPPER(@padded), UPPER(@raw))
   OR UPPER(a.folio_app_original) IN (UPPER(@padded), UPPER(@raw))
   OR (ISNUMERIC(a.folio_app) = 1 AND CAST(a.folio_app AS bigint) = @numeric)
   OR (ISNUMERIC(a.folio_app_original) = 1 AND CAST(a.folio_app_original AS bigint) = @numeric)
ORDER BY a.fecha_operacion DESC";

            AddParameter(command, "@padded", paddedFolio);
            AddParameter(command, "@raw", rawFolio);
            AddParameter(command, "@numeric", numericValue);

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

        private static bool LooksLikeAppFolio(string value)
        {
            return value.Length > 1 && value[0] == '0' && value.All(char.IsDigit);
        }

        private async Task<PosVentasViewModel?> TryGetSuspendedGafeteVentaModelAsync(string productoBusqueda, string normalized)
        {
            var folioText = Normalize(normalized);
            if (folioText == null)
                return null;

            var numericText = folioText;
            if (numericText.Length > 1 && numericText[0] == 'M' && numericText[1..].All(char.IsDigit))
                numericText = numericText[1..];

            if (!long.TryParse(numericText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var folioNumber))
                return null;

            var padded = folioNumber.ToString("D4", CultureInfo.InvariantCulture);
            var prefixed = "M" + folioNumber.ToString("D8", CultureInfo.InvariantCulture);

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT TOP (1)
    COALESCE(NULLIF(d.codigorecepcion, ''), d.folioregistrostr, CONVERT(nvarchar(60), d.folioregistro), @prefixed) AS Folio,
    COALESCE(NULLIF(d.gafete, ''), CONVERT(nvarchar(30), g.gafete), '') AS Gafete,
    COALESCE(NULLIF(t.nombre, ''), NULLIF(d.nombrestaff, ''), NULLIF(d.nombrevendedor, ''), '') AS TaxistaNombre,
    COALESCE(d.idtaxi, 0) AS TaxistaId,
    COALESCE(d.tipotransporte, '') AS TransporteTipo,
    COALESCE(d.hotel, d.nombrealmacen, '') AS Hotel,
    COALESCE(d.pax, 1) AS Pax,
    COALESCE(d.total, 0) AS Dejada,
    COALESCE(g.venta, '') AS VentaStatus,
    COALESCE(CONVERT(nvarchar(60), g.folioperacion), CONVERT(nvarchar(60), d.folioregistro), d.folioregistrostr, '') AS FolioOperacion
FROM {PosTable("dejadas")} d
OUTER APPLY (
    SELECT TOP (1) gf.*
    FROM {PosTable("gafete")} gf
    WHERE CONVERT(nvarchar(60), gf.folioperacion) IN (@folioText, @numericText, @padded, @prefixed)
       OR CONVERT(nvarchar(60), gf.gafete) = NULLIF(LTRIM(RTRIM(d.gafete)), '')
    ORDER BY
        CASE WHEN gf.venta = 'S' THEN 0 WHEN gf.venta = 'A' THEN 1 ELSE 2 END,
        gf.fecha DESC,
        gf.hora DESC
) g
LEFT JOIN {PosTable("cataxi")} t
    ON t.idtaxi = d.idtaxi
WHERE (
       d.folioregistro = @folioNumber
    OR d.folioregistrostr IN (@folioText, @numericText, @padded, @prefixed)
    OR d.codigorecepcion IN (@folioText, @numericText, @padded, @prefixed)
)
AND COALESCE(g.venta, '') = 'S'
ORDER BY d.fecha DESC, d.hora DESC",
                ("@folioText", folioText),
                ("@numericText", numericText),
                ("@padded", padded),
                ("@prefixed", prefixed),
                ("@folioNumber", folioNumber));

            var row = rows.FirstOrDefault();
            if (row == null)
                return null;

            var folio = FirstText(PickText(row, "Folio"), productoBusqueda);
            var dejada = ToDecimal(PickObject(row, "Dejada"));
            return new PosVentasViewModel
            {
                ProductoBusqueda = productoBusqueda,
                FolioControl = folio,
                Gafete = PickText(row, "Gafete"),
                Vendedor = PickText(row, "TaxistaNombre"),
                Cliente = PickText(row, "Hotel"),
                Usuario = "POS",
                TransporteTipo = FirstText(PickText(row, "TransporteTipo"), "WEB"),
                TaxistaId = Convert.ToInt64(ToDecimal(PickObject(row, "TaxistaId")), CultureInfo.InvariantCulture),
                TaxistaNombre = PickText(row, "TaxistaNombre"),
                Cantidad = 1,
                Pax = Math.Max(PickInt(row, "Pax"), 1),
                TipoCambio = "19.50",
                Items = new List<PosVentaDetalleViewModel>
                {
                    new()
                    {
                        Cantidad = 1,
                        Producto = $"{folio} - SUSPENDIDO",
                        Departamento = "GAFETE SUSPENDIDO",
                        Precio = 0,
                        Importe = 0
                    }
                },
                Subtotal = 0,
                Iva = 0,
                Total = 0
            };
        }

        private static PosVentasViewModel BuildRelationVentaModel(string productoBusqueda, VentaRelacionInfo relacion)
        {
            return new PosVentasViewModel
            {
                ProductoBusqueda = productoBusqueda,
                FolioControl = relacion.FolioControl,
                FolioApp = relacion.FolioApp,
                Gafete = relacion.Gafete,
                Vendedor = relacion.TaxistaNombre,
                Cliente = relacion.Hotel,
                Usuario = "WEB",
                TransporteTipo = FirstText(relacion.TransporteTipo, "WEB"),
                TaxistaId = relacion.TaxistaId,
                TaxistaNombre = relacion.TaxistaNombre,
                Cantidad = 1,
                Pax = Math.Max(relacion.Pax, 1),
                TipoCambio = "19.50"
            };
        }

        private async Task<PosVentasViewModel> BuildStoreVentaModelAsync(string productoBusqueda, StoreVentaInfo storeVenta, VentaRelacionInfo? relacion = null)
        {
            var model = new PosVentasViewModel
            {
                ProductoBusqueda = productoBusqueda,
                FolioControl = relacion?.FolioControl ?? string.Empty,
                FolioApp = relacion?.FolioApp ?? string.Empty,
                Gafete = relacion?.Gafete ?? string.Empty,
                Vendedor = FirstText(storeVenta.Vendedor, storeVenta.Usuario),
                Cliente = storeVenta.Cliente,
                Usuario = storeVenta.Usuario,
                TransporteTipo = FirstText(relacion?.TransporteTipo, "WEB"),
                TaxistaId = relacion?.TaxistaId ?? 0,
                TaxistaNombre = FirstText(relacion?.TaxistaNombre, storeVenta.TaxistaNombre),
                Cantidad = 1,
                Pax = Math.Max(relacion?.Pax ?? 1, 1),
                TipoCambio = storeVenta.TipoCambio > 0m ? storeVenta.TipoCambio.ToString("0.####", CultureInfo.InvariantCulture) : "19.50",
                Efectivo = storeVenta.Efectivo,
                Tarjeta = storeVenta.Tarjeta,
                Dolares = storeVenta.Dolares,
                FolioRegistro = storeVenta.FolioRegistro,
                FolioFactura = storeVenta.Lines.Select(x => x.Factura).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
                OrigenVenta = storeVenta.Origen,
                Items = storeVenta.Lines
                    .Select(line => new PosVentaDetalleViewModel
                    {
                        Cantidad = 1,
                        Producto = $"{line.Origen} {line.Folio}",
                        Referencia = line.Folio,
                        Factura = line.Factura,
                        OrigenVenta = line.Origen,
                        FolioRegistro = line.FolioRegistro,
                        FechaVenta = line.Fecha?.ToString("dd/MM/yyyy HH:mm") ?? string.Empty,
                        Departamento = "VENTA",
                        Precio = line.Subtotal,
                        Iva = line.Iva,
                        Importe = line.Total
                    })
                    .ToList(),
                Subtotal = storeVenta.Subtotal,
                Iva = storeVenta.Iva,
                Total = storeVenta.Total
            };

            await DecorateStoreVentaModelAsync(model, storeVenta, relacion);
            return model;
        }

        private async Task DecorateStoreVentaModelAsync(PosVentasViewModel model, StoreVentaInfo storeVenta, VentaRelacionInfo? relacion)
        {
            var activeGafetes = await GetActiveGafetesForOperacionAsync(storeVenta.FolioRegistro);
            var gafeteMap = await GetLineGafeteAssignmentsAsync(storeVenta, relacion, activeGafetes);

            for (var index = 0; index < model.Items.Count && index < storeVenta.Lines.Count; index++)
            {
                var item = model.Items[index];
                var line = storeVenta.Lines[index];
                var key = Normalize(line.Folio) ?? string.Empty;

                if (gafeteMap.TryGetValue(key, out var assigned) && assigned.Count > 0)
                {
                    item.GafetesAsignados = string.Join(", ", assigned);
                }
                else if (activeGafetes.Count == 1)
                {
                    item.GafetesAsignados = activeGafetes[0];
                }
            }

            model.GafetesActivos = activeGafetes;
            if (string.IsNullOrWhiteSpace(model.Gafete) && activeGafetes.Count > 0)
                model.Gafete = string.Join(", ", activeGafetes);

            var rows = new List<PosVentaGafeteRelacionViewModel>();
            foreach (var item in model.Items.Where(x => x.ProductoId <= 0))
            {
                var gafetes = SplitCatalogTokens(item.GafetesAsignados).ToList();
                if (gafetes.Count == 0 && activeGafetes.Count == 1)
                    gafetes.Add(activeGafetes[0]);

                if (gafetes.Count == 0)
                {
                    rows.Add(new PosVentaGafeteRelacionViewModel
                    {
                        Gafete = "SIN GAFETE ASIGNADO",
                        Venta = item.Producto,
                        Referencia = item.Referencia,
                        OrigenVenta = item.OrigenVenta,
                        Importe = item.Importe
                    });
                    continue;
                }

                foreach (var gafete in gafetes)
                {
                    rows.Add(new PosVentaGafeteRelacionViewModel
                    {
                        Gafete = gafete,
                        Venta = item.Producto,
                        Referencia = item.Referencia,
                        OrigenVenta = item.OrigenVenta,
                        Importe = item.Importe
                    });
                }
            }

            foreach (var active in activeGafetes.Where(active => rows.All(x => !string.Equals(x.Gafete, active, StringComparison.OrdinalIgnoreCase))))
            {
                rows.Add(new PosVentaGafeteRelacionViewModel
                {
                    Gafete = active,
                    Venta = "SIN VENTA ASIGNADA",
                    Referencia = string.Empty,
                    OrigenVenta = string.Empty,
                    Importe = 0m
                });
            }

            model.GafeteVentas = rows;
        }

        private async Task<List<string>> GetActiveGafetesForOperacionAsync(string folioRegistro)
        {
            var normalized = Normalize(folioRegistro);
            if (normalized == null)
                return new List<string>();

            var rows = await ReadRowsFromSqlAsyncWithParam($@"
SELECT DISTINCT CONVERT(nvarchar(50), gafete) AS Gafete
FROM {PosTable("gafete")}
WHERE CONVERT(nvarchar(60), folioperacion) = @folio
  AND UPPER(COALESCE(venta, '')) = 'A'
ORDER BY Gafete", ("@folio", normalized));

            return rows
                .Select(row => PickText(row, "Gafete"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<Dictionary<string, List<string>>> GetLineGafeteAssignmentsAsync(
            StoreVentaInfo storeVenta,
            VentaRelacionInfo? relacion,
            List<string> activeGafetes)
        {
            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void AddAssignment(string ticket, IEnumerable<string> gafetes)
            {
                var key = Normalize(ticket);
                if (string.IsNullOrWhiteSpace(key))
                    return;

                if (!map.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    map[key] = set;
                }

                foreach (var gafete in gafetes.Where(x => !string.IsNullOrWhiteSpace(x)))
                    set.Add(gafete);
            }

            var relaciones = await GetRelacionesReporteDejadasAsync(busqueda: storeVenta.FolioRegistro);
            foreach (var row in relaciones.Where(row =>
                string.Equals(Normalize(row.FolioOperacion), Normalize(storeVenta.FolioRegistro), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Normalize(row.FolioControl), Normalize(storeVenta.FolioRegistro), StringComparison.OrdinalIgnoreCase)
                || string.Equals(Normalize(row.FolioApp), Normalize(storeVenta.FolioRegistro), StringComparison.OrdinalIgnoreCase)
                || storeVenta.Lines.Any(line => string.Equals(Normalize(line.Folio), Normalize(row.FolioPos), StringComparison.OrdinalIgnoreCase))))
            {
                var tickets = SplitCatalogTokens(row.FolioPos).ToList();
                var gafetes = SplitCatalogTokens(row.Gafete).ToList();
                if (tickets.Count == 0)
                    continue;

                if (tickets.Count == gafetes.Count && tickets.Count > 1)
                {
                    for (var i = 0; i < tickets.Count; i++)
                        AddAssignment(tickets[i], new[] { gafetes[i] });
                }
                else if (gafetes.Count == 1)
                {
                    foreach (var ticket in tickets)
                        AddAssignment(ticket, gafetes);
                }
                else
                {
                    foreach (var ticket in tickets)
                        AddAssignment(ticket, gafetes);
                }
            }

            if (!string.IsNullOrWhiteSpace(relacion?.FolioPos))
            {
                var relationTickets = SplitCatalogTokens(relacion.FolioPos).ToList();
                var relationGafetes = SplitCatalogTokens(relacion.Gafete).ToList();
                if (relationTickets.Count == relationGafetes.Count && relationTickets.Count > 1)
                {
                    for (var i = 0; i < relationTickets.Count; i++)
                        AddAssignment(relationTickets[i], new[] { relationGafetes[i] });
                }
                else
                {
                    foreach (var ticket in relationTickets)
                        AddAssignment(ticket, relationGafetes);
                }
            }

            if (activeGafetes.Count == 1)
            {
                foreach (var line in storeVenta.Lines)
                    AddAssignment(line.Folio, activeGafetes);
            }

            return map.ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SplitCatalogTokens(string? value) =>
            (value ?? string.Empty)
                .Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);

        public async Task<List<PosVentaDetalleViewModel>> TryGetRemisionProductosAsync(string folioRegistro)
        {
            try
            {
                if (!long.TryParse(Normalize(folioRegistro), NumberStyles.Integer, CultureInfo.InvariantCulture, out var folioNumber))
                    return new List<PosVentaDetalleViewModel>();

                var query = $@"
SELECT
    COALESCE(d.producto, d.descripcion_larga, '') AS Producto,
    COALESCE(d.cantidads, 0) AS Cantidad,
    COALESCE(d.p_unitario, 0) AS Precio,
    COALESCE(d.stotal, 0) AS Importe,
    COALESCE(d.deportiva, d.categoria, '') AS Departamento
FROM {QuoteSqlIdentifier(_compuadmoStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioD] d
WHERE d.folio_registro = @folio
UNION ALL
SELECT
    COALESCE(d.producto, d.descripcion_larga, '') AS Producto,
    COALESCE(d.cantidads, 0) AS Cantidad,
    COALESCE(d.p_unitario, 0) AS Precio,
    COALESCE(d.stotal, 0) AS Importe,
    COALESCE(d.deportiva, d.categoria, '') AS Departamento
FROM {QuoteSqlIdentifier(_joyeriaStoreDatabaseName)}.{QuoteSqlIdentifier(_storeSchemaName)}.[remisioD] d
WHERE d.folio_registro = @folio
";

                var rows = await ReadRowsFromSqlAsyncWithParam(query, ("@folio", folioNumber));
                return rows.Select(row => new PosVentaDetalleViewModel
                {
                    Producto = FirstText(PickText(row, "Producto"), string.Empty),
                    Cantidad = (int)ToDecimal(PickObject(row, "Cantidad")),
                    Precio = ToDecimal(PickObject(row, "Precio")),
                    Importe = ToDecimal(PickObject(row, "Importe")),
                    Departamento = PickText(row, "Departamento")
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar lineas de remisión.");
                return new List<PosVentaDetalleViewModel>();
            }
        }

    }
}

