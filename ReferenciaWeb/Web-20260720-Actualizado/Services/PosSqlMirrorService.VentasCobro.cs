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
public async Task<string?> CreateVentaAsync(PosVentasViewModel model)
        {
            var saleLines = new List<(ProductRow Product, PosVentaDetalleViewModel Line)>();
            foreach (var line in model.Items.Where(x => x.ProductoId > 0 && x.Cantidad > 0))
            {
                var productLine = await _dbContext.Products.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.ProductId == line.ProductoId);
                if (productLine != null)
                    saleLines.Add((productLine, line));
            }

            if (saleLines.Count == 0)
            {
                var product = await ResolveProductForSaleAsync(model);
                if (product != null)
                {
                    var quantity = Math.Max(model.Cantidad, 1);
                    var price = ResolveProductPrice(product) * quantity;
                    var lineIva = ResolveTax(price, ToDecimal(product.Iva));
                    saleLines.Add((product, new PosVentaDetalleViewModel
                    {
                        ProductoId = product.ProductId,
                        Cantidad = quantity,
                        Producto = product.Name ?? string.Empty,
                        Departamento = product.Department ?? string.Empty,
                        Precio = price,
                        Iva = lineIva,
                        Importe = price + lineIva
                    }));
                }
            }

            if (saleLines.Count == 0)
                return null;

            var subtotal = saleLines.Sum(x => x.Line.Precio);
            var iva = saleLines.Sum(x => x.Line.Iva);
            var total = subtotal + iva;
            var efectivo = model.Efectivo > 0 ? model.Efectivo : Math.Max(total - model.Tarjeta - model.Dolares, 0m);
            var exchangeRate = decimal.TryParse(model.TipoCambio, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedRate) && parsedRate > 0
                ? parsedRate
                : 1m;
            var now = DateTime.Now;
            if (await IsCorteClosedAsync(now.Date))
                return null;

            var folio = string.Empty;
            var sellerKey = await ResolveVendorKeyAsync(model.Vendedor);
            if (string.IsNullOrWhiteSpace(sellerKey))
                sellerKey = SafeText(model.Vendedor, 20);

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                await connection.OpenAsync();
            await EnsurePosOperacionBeneficiariosTableAsync(connection);
            await EnsureRelacionesTicketTaxistaTableAsync(connection);

            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                var relacion = await ResolveVentaRelacionAsync(connection, transaction, model.FolioControl, model.FolioApp, model.TaxistaNombre, model.Gafete);
                if (relacion != null)
                {
                    model.FolioControl = relacion.FolioControl;
                    model.FolioApp = relacion.FolioApp;
                    model.Gafete = FirstText(model.Gafete, relacion.Gafete);
                    model.TaxistaId = model.TaxistaId > 0 ? model.TaxistaId : relacion.TaxistaId;
                    model.TaxistaNombre = FirstText(model.TaxistaNombre, relacion.TaxistaNombre);
                    model.TransporteTipo = FirstText(model.TransporteTipo, relacion.TransporteTipo, "WEB");
                    model.Cliente = FirstText(model.Cliente, relacion.Hotel);
                    model.Pax = Math.Max(model.Pax, relacion.Pax);
                }

                var mktFolio = await InsertMkt2VentaAsync(
                    connection,
                    transaction,
                    now,
                    saleLines,
                    model,
                    subtotal,
                    iva,
                    total,
                    efectivo,
                    sellerKey,
                    relacion?.Dejada ?? 0m);
                folio = mktFolio.ToString(CultureInfo.InvariantCulture);
                var foliosPos = await InsertStoreTicketsAsync(
                    connection,
                    transaction,
                    folio,
                    now,
                    saleLines,
                    model,
                    efectivo,
                    exchangeRate);
                var folioPos = foliosPos.Count > 0 ? string.Join(", ", foliosPos) : $"WEB{folio}";
                await UpdateMkt2TicketReferenceAsync(connection, transaction, folio, folioPos);
                await SaveOperacionBeneficiarioAsync(connection, transaction, folio, model, sellerKey);
                if (relacion != null)
                    await LinkRelacionTicketTaxistaAsync(connection, transaction, relacion, folio, folioPos, model, model.Usuario, total);
                var staffId = await EnsurePosStaffAsync(connection, transaction, sellerKey, model.Vendedor, model.Usuario);
                int ventaId;
                await using (var header = connection.CreateCommand())
                {
                    header.Transaction = transaction;
                    header.CommandText = $@"
INSERT INTO {AppTable("Ventas")}
    (Folio, Fecha, StaffId, Pax, Hotel, TipoOperacion, Subtotal, Iva, Total, Saldo, Usuario, Estatus)
OUTPUT INSERTED.Id
VALUES
    (@folio, @fecha, @staffId, @pax, @hotel, @tipoOperacion, @subtotal, @iva, @total, @saldo, @usuario, @estatus)";
                    AddParameter(header, "@folio", folio);
                    AddParameter(header, "@fecha", now);
                    AddParameter(header, "@staffId", staffId);
                    AddParameter(header, "@pax", Math.Max(model.Pax, 1));
                    AddParameter(header, "@hotel", SafeText(model.Cliente, 150));
                    AddParameter(header, "@tipoOperacion", "VENTA");
                    AddParameter(header, "@subtotal", subtotal);
                    AddParameter(header, "@iva", iva);
                    AddParameter(header, "@total", total);
                    AddParameter(header, "@saldo", 0);
                    AddParameter(header, "@usuario", SafeText(model.Usuario, 50));
                    AddParameter(header, "@estatus", "Cobrada");
                    ventaId = Convert.ToInt32(await ExecuteScalarAsync(header), CultureInfo.InvariantCulture);
                }

                foreach (var (product, line) in saleLines)
                {
                    await using var detail = connection.CreateCommand();
                    detail.Transaction = transaction;
                    detail.CommandText = $@"
INSERT INTO {AppTable("VentaDetalle")}
    (VentaId, ProductoId, Cantidad, Descripcion, Precio, Importe, Descuento, Departamento)
VALUES
    (@ventaId, @producto, @cantidad, @descripcion, @precio, @importe, @descuento, @departamento)";
                    AddParameter(detail, "@ventaId", ventaId);
                    AddParameter(detail, "@producto", product.ProductId);
                    AddParameter(detail, "@cantidad", line.Cantidad);
                    AddParameter(detail, "@descripcion", product.Name ?? string.Empty);
                    AddParameter(detail, "@precio", ResolveProductPrice(product));
                    AddParameter(detail, "@importe", line.Precio);
                    AddParameter(detail, "@descuento", 0);
                    AddParameter(detail, "@departamento", product.Department ?? string.Empty);
                    await ExecuteNonQueryAsync(detail);
                }

                if (efectivo > 0)
                    await InsertPosPagoAsync(connection, transaction, ventaId, "EFECTIVO", "MXN", 1m, efectivo, string.Empty);
                if (model.Tarjeta > 0)
                    await InsertPosPagoAsync(connection, transaction, ventaId, "TARJETA", "MXN", 1m, model.Tarjeta, string.Empty);
                if (model.Dolares > 0)
                    await InsertPosPagoAsync(connection, transaction, ventaId, "EFECTIVO", "USD", exchangeRate, model.Dolares, string.Empty);

                await using (var commission = connection.CreateCommand())
                {
                    commission.Transaction = transaction;
                    commission.CommandText = $@"
INSERT INTO {AppTable("Comisiones")}
    (VentaId, BeneficiarioTipo, BeneficiarioId, BaseCalculo, Porcentaje, Importe, Pagado, Estatus)
VALUES
    (@ventaId, @tipo, @beneficiarioId, @base, @porcentaje, @importe, @pagado, @estatus)";
                    AddParameter(commission, "@ventaId", ventaId);
                    AddParameter(commission, "@tipo", "STAFF");
                    AddParameter(commission, "@beneficiarioId", staffId);
                    AddParameter(commission, "@base", total);
                    AddParameter(commission, "@porcentaje", 0);
                    AddParameter(commission, "@importe", 0);
                    AddParameter(commission, "@pagado", false);
                    AddParameter(commission, "@estatus", "Pendiente");
                    await ExecuteNonQueryAsync(commission);
                }

                await InsertPosAuditoriaAsync(connection, transaction, model.Usuario, "Ventas", "Cobrar", folio, $"Venta guardada por {total:N2} con ticket {folioPos}",
                    BuildAuditJson(
                        ("FolioOperacion", folio),
                        ("FolioPos", folioPos),
                        ("FolioControl", model.FolioControl),
                        ("FolioApp", model.FolioApp),
                        ("Gafete", model.Gafete),
                        ("Taxista", model.TaxistaNombre),
                        ("Lineas", saleLines.Select(x => new { x.Product.ProductId, x.Line.Cantidad, x.Line.Precio, x.Line.Iva })),
                        ("Subtotal", subtotal),
                        ("Iva", iva),
                        ("Total", total),
                        ("Efectivo", efectivo),
                        ("Tarjeta", model.Tarjeta),
                        ("Dolares", model.Dolares),
                        ("Vendedor", model.Vendedor)));

                await transaction.CommitAsync();
                return folio;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }





private async Task<List<string>> InsertStoreTicketsAsync(
            DbConnection connection,
            DbTransaction transaction,
            string operationFolio,
            DateTime saleDate,
            IReadOnlyCollection<(ProductRow Product, PosVentaDetalleViewModel Line)> saleLines,
            PosVentasViewModel model,
            decimal efectivo,
            decimal exchangeRate)
        {
            var folios = new List<string>();
            var compraLines = saleLines.Where(x => !IsJoyeriaLine(x.Product, x.Line)).ToList();
            var joyeriaLines = saleLines.Where(x => IsJoyeriaLine(x.Product, x.Line)).ToList();
            var total = saleLines.Sum(x => x.Line.Precio + x.Line.Iva);

            if (compraLines.Count > 0)
            {
                var folio = $"MC{operationFolio}";
                await InsertStoreTicketAsync(connection, transaction, _compuadmoStoreDatabaseName, false, folio, operationFolio, saleDate, model, compraLines, efectivo, exchangeRate, total);
                folios.Add(folio);
            }

            if (joyeriaLines.Count > 0)
            {
                var folio = $"MJ{operationFolio}";
                await InsertStoreTicketAsync(connection, transaction, _joyeriaStoreDatabaseName, true, folio, operationFolio, saleDate, model, joyeriaLines, efectivo, exchangeRate, total);
                folios.Add(folio);
            }

            return folios;
        }



private static async Task InsertStoreTicketAsync(
            DbConnection connection,
            DbTransaction transaction,
            string databaseName,
            bool isJoyeria,
            string folio,
            string operationFolio,
            DateTime saleDate,
            PosVentasViewModel model,
            IReadOnlyCollection<(ProductRow Product, PosVentaDetalleViewModel Line)> lines,
            decimal efectivoTotal,
            decimal exchangeRate,
            decimal ventaTotal)
        {
            var subtotal = lines.Sum(x => x.Line.Precio);
            var tax = lines.Sum(x => x.Line.Iva);
            var total = subtotal + tax;
            var ratio = ventaTotal > 0 ? total / ventaTotal : 1m;
            var efectivo = Math.Round(efectivoTotal * ratio, 2);
            var tarjeta = Math.Round(model.Tarjeta * ratio, 2);
            var dolares = Math.Round(model.Dolares * ratio, 2);
            var vendorNumeric = int.TryParse(Normalize(model.Vendedor), NumberStyles.Integer, CultureInfo.InvariantCulture, out var vendorId) ? vendorId : 0;
            var operationNumber = ParseLongOrZero(operationFolio);

            var headerColumns = await GetRemoteTableColumnsAsync(connection, transaction, databaseName, "remisioM");
            var headerValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["folio_remision"] = folio,
                ["folio_factura"] = folio,
                ["folio_pedido"] = folio,
                ["fecha"] = saleDate.Date,
                ["tipof"] = "R",
                ["tipo"] = isJoyeria ? "N" : 1,
                ["cliente"] = SanitizeForInsert(model.Cliente, 6),
                ["vendedor"] = isJoyeria ? vendorNumeric : SafeText(model.Vendedor, 4),
                ["estatus"] = "A",
                ["stotal"] = subtotal,
                ["iva"] = tax,
                ["total"] = total,
                ["saldo"] = total,
                ["fecha_cobro"] = saleDate.Date,
                ["observaciones"] = SanitizeForInsert(BuildTicketNotes(model, operationFolio), 500),
                ["descuento"] = 0m,
                ["moneda"] = isJoyeria ? 1 : "P",
                ["tipo_cambio"] = exchangeRate,
                ["letras"] = SanitizeForInsert(MoneyText(total), 100),
                ["usuario"] = SanitizeForInsert(model.Usuario, 4),
                ["hora"] = isJoyeria ? saleDate : saleDate.ToString("hh:mm:ss tt", CultureInfo.InvariantCulture),
                ["almacen"] = "100",
                ["efectivo"] = efectivo,
                ["tarjeta"] = tarjeta,
                ["dolares"] = dolares,
                ["cotizadolar"] = exchangeRate,
                ["folio_operacion"] = operationNumber,
                ["guia"] = model.GuiaMatricula,
                ["codigoguia"] = SafeText(model.GuiaMatricula > 0 ? model.GuiaMatricula.ToString(CultureInfo.InvariantCulture) : string.Empty, 4),
                ["folioregistro"] = operationNumber,
                ["folio_registro"] = operationNumber,
                ["comisionista"] = SanitizeForInsert(model.Vendedor, 4),
                ["cuantosvend"] = Math.Max(model.Pax, 1),
                ["cajero"] = vendorNumeric,
                ["procesado"] = "N",
                ["ncorte"] = 0
            };
            await ExecuteRemoteDynamicInsertAsync(connection, transaction, databaseName, "remisioM", headerColumns, headerValues);

            var detailColumns = await GetRemoteTableColumnsAsync(connection, transaction, databaseName, "remisioD");
            foreach (var (product, line) in lines)
            {
                var quantity = Math.Max(line.Cantidad, 1);
                var unitPrice = quantity > 0 ? line.Precio / quantity : line.Precio;
                var department = CleanDepartment(FirstText(line.Departamento, product.Department));
                var detailValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["folio_remision"] = folio,
                    ["folio_factura"] = folio,
                    ["producto"] = isJoyeria ? SafeText(product.ProductId.ToString(CultureInfo.InvariantCulture), 20) : product.ProductId,
                    ["cantidads"] = quantity,
                    ["p_unitario"] = unitPrice,
                    ["stotal"] = line.Precio,
                    ["descuento"] = 0m,
                    ["capacidad"] = "1",
                    ["unidad"] = "0",
                    ["descripcion_larga"] = SanitizeForInsert(FirstText(line.Producto, product.Name), 250),
                    ["deportiva"] = SanitizeForInsert(department, 20),
                    ["promo"] = DBNull.Value,
                    ["peso"] = 0,
                    ["almacen"] = "100",
                    ["fecha"] = saleDate.Date,
                    ["categoria"] = SafeText(department, 50),
                    ["codigo_barras"] = SanitizeForInsert(FirstText(product.Barcode, product.ProductId.ToString(CultureInfo.InvariantCulture)), 30)
                };
                await ExecuteRemoteDynamicInsertAsync(connection, transaction, databaseName, "remisioD", detailColumns, detailValues);
            }
        }



private static async Task UpdateMkt2TicketReferenceAsync(
            DbConnection connection,
            DbTransaction transaction,
            string operationFolio,
            string folioPos)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
UPDATE {PosTable("operacion")}
SET foliosoluone = @folioPos
WHERE CONVERT(nvarchar(30), folio) = @folio;

UPDATE {PosTable("mov_operacion")}
SET foliosoluone = @folioPos
WHERE CONVERT(nvarchar(30), folioperacion) = @folio;";
            AddParameter(command, "@folioPos", SafeText(folioPos, 100));
            AddParameter(command, "@folio", operationFolio);
            await ExecuteNonQueryAsync(command);
        }



private static async Task<HashSet<string>> GetRemoteTableColumnsAsync(
            DbConnection connection,
            DbTransaction transaction,
            string databaseName,
            string tableName)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
SELECT COLUMN_NAME
FROM [{databaseName}].INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @tableName;";
            AddParameter(command, "@tableName", tableName);

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await ExecuteReaderAsync(command);
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }

            return columns;
        }



private static async Task ExecuteRemoteDynamicInsertAsync(
            DbConnection connection,
            DbTransaction transaction,
            string databaseName,
            string tableName,
            HashSet<string> existingColumns,
            Dictionary<string, object?> desiredValues)
        {
            var columns = desiredValues.Keys.Where(existingColumns.Contains).ToList();
            if (columns.Count == 0)
                return;

            var parameterNames = columns.Select((_, index) => $"@p{index}").ToList();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
INSERT INTO [{databaseName}].{QuoteSqlIdentifier(_storeSchemaName)}.[{tableName}]
({string.Join(", ", columns.Select(x => $"[{x}]"))})
VALUES
({string.Join(", ", parameterNames)});";

            for (var index = 0; index < columns.Count; index++)
            {
                AddParameter(command, parameterNames[index], desiredValues[columns[index]]);
            }

            await ExecuteNonQueryAsync(command);
        }



private static bool IsJoyeriaLine(ProductRow product, PosVentaDetalleViewModel line)
        {
            var department = FirstText(line.Departamento, product.Department);
            return department.Contains("JOY", StringComparison.OrdinalIgnoreCase);
        }



private static string BuildTicketNotes(PosVentasViewModel model, string operationFolio)
        {
            var parts = new[]
            {
                $"FOLIO OPERACION: {operationFolio}",
                string.IsNullOrWhiteSpace(model.FolioControl) ? string.Empty : $"FOLIO APP: {model.FolioControl}",
                string.IsNullOrWhiteSpace(model.FolioApp) ? string.Empty : $"REGISTRO APP: {model.FolioApp}",
                string.IsNullOrWhiteSpace(model.Gafete) ? string.Empty : $"GAFETE: {model.Gafete}",
                string.IsNullOrWhiteSpace(model.TaxistaNombre) ? string.Empty : $"TAXISTA: {model.TaxistaNombre}",
                string.IsNullOrWhiteSpace(model.TransporteTipo) ? string.Empty : $"TRANSPORTE: {model.TransporteTipo}"
            };

            return SafeText(string.Join(" | ", parts.Where(x => !string.IsNullOrWhiteSpace(x))), 500);
        }



private static string CleanDepartment(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            var separator = normalized.IndexOf(':');
            return separator >= 0 && separator + 1 < normalized.Length ? normalized[(separator + 1)..].Trim() : normalized;
        }

private static string SanitizeForInsert(string? value, int maxLength)
{
    var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
    normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "\\s+", " ");
    return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
}



private static long ParseLongOrZero(string value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;



private static string MoneyText(decimal amount) =>
            $"{amount:0.##} PESOS 00/100 M.N.";



private async Task<int> InsertMkt2VentaAsync(
            DbConnection connection,
            DbTransaction transaction,
            DateTime now,
            IReadOnlyCollection<(ProductRow Product, PosVentaDetalleViewModel Line)> saleLines,
            PosVentasViewModel model,
            decimal subtotal,
            decimal iva,
            decimal total,
            decimal efectivo,
            string sellerKey,
            decimal dejada)
        {
            int folio;
            await using (var next = connection.CreateCommand())
            {
                next.Transaction = transaction;
                next.CommandText = $@"
SELECT ISNULL(MAX(folio), 0) + 1
FROM {PosTable("operacion")} WITH (UPDLOCK, HOLDLOCK);";
                folio = Convert.ToInt32(await ExecuteScalarAsync(next), CultureInfo.InvariantCulture);
            }

            var joyeria = saleLines.Where(x => (x.Product.Department ?? string.Empty).Contains("JOY", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Line.Precio);
            var artesania = saleLines.Where(x => (x.Product.Department ?? string.Empty).Contains("ART", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Line.Precio);
            var licor = saleLines.Where(x => (x.Product.Department ?? string.Empty).Contains("LIC", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Line.Precio);
            var farmacia = saleLines.Where(x => (x.Product.Department ?? string.Empty).Contains("FAR", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Line.Precio);
            var compra = subtotal - joyeria;
            var firstProduct = saleLines.First().Product;
            var referencia = $"WEB{folio}";

            await using (var op = connection.CreateCommand())
            {
                op.Transaction = transaction;
                op.CommandText = $@"
INSERT INTO {PosTable("operacion")}
    (fecha, tipo, hotel, nombre, pax, adl, men, folio, idstaff, staffnombre, foliosoluone, horaentrada, totalcompra, totaljoyeria, fechaventa, transportetipo, inf)
VALUES
    (@fecha, @tipo, @hotel, @nombre, @pax, @adl, 0, @folio, @idstaff, @staff, @referencia, @hora, @totalcompra, @totaljoyeria, @fechaventa, @transporte, 1);";
                AddParameter(op, "@fecha", now);
                AddParameter(op, "@tipo", "VENTA");
                AddParameter(op, "@hotel", SafeText(model.Cliente, 100));
                AddParameter(op, "@nombre", SafeText(firstProduct.Name ?? string.Empty, 50));
                AddParameter(op, "@pax", Math.Max(model.Pax, 1));
                AddParameter(op, "@adl", Math.Max(model.Pax, 1));
                AddParameter(op, "@folio", folio);
                AddParameter(op, "@idstaff", int.TryParse(sellerKey, out var staffId) ? staffId : 0);
                AddParameter(op, "@staff", SafeText(model.Vendedor, 100));
                AddParameter(op, "@referencia", referencia);
                AddParameter(op, "@hora", now.ToString("HH:mm", CultureInfo.InvariantCulture));
                AddParameter(op, "@totalcompra", compra);
                AddParameter(op, "@totaljoyeria", joyeria);
                AddParameter(op, "@fechaventa", now);
                AddParameter(op, "@transporte", SafeText(string.IsNullOrWhiteSpace(model.TransporteTipo) ? "WEB" : model.TransporteTipo, 10));
                await ExecuteNonQueryAsync(op);
            }

            await using (var mov = connection.CreateCommand())
            {
                mov.Transaction = transaction;
                mov.CommandText = $@"
INSERT INTO {PosTable("mov_operacion")}
    (foliosoluone, fecha, folioperacion, totaljoyeria, totalcompra, totalefectivo, totaltarjeta, tipo, impuestos, dejada, comision, transportetipo, porimpuestot, porimpuestoe, descuentotarjeta, descuentoefectivo, pago, fechapago, totalgastos, totalartesania, totallicor, totalfarmacia)
VALUES
    (@referencia, @fecha, @folio, @totaljoyeria, @totalcompra, @efectivo, @tarjeta, 'V', @iva, @dejada, 0, @transporte, 0, 0, 0, 0, 0, NULL, 0, @artesania, @licor, @farmacia);";
                AddParameter(mov, "@referencia", referencia);
                AddParameter(mov, "@fecha", now);
                AddParameter(mov, "@folio", folio);
                AddParameter(mov, "@totaljoyeria", joyeria);
                AddParameter(mov, "@totalcompra", compra);
                AddParameter(mov, "@efectivo", efectivo);
                AddParameter(mov, "@tarjeta", model.Tarjeta);
                AddParameter(mov, "@transporte", SafeText(string.IsNullOrWhiteSpace(model.TransporteTipo) ? "WEB" : model.TransporteTipo, 10));
                AddParameter(mov, "@iva", iva);
                AddParameter(mov, "@dejada", dejada);
                AddParameter(mov, "@artesania", artesania);
                AddParameter(mov, "@licor", licor);
                AddParameter(mov, "@farmacia", farmacia);
                await ExecuteNonQueryAsync(mov);
            }

            return folio;
        }



    }
}

