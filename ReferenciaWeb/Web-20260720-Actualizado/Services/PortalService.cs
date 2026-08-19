using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services;

public sealed class PortalService(
    IDbContextFactory<CompuadmoPlazaContext> compuFactory,
    IDbContextFactory<JoyeriaPlazaContext> joyeriaFactory)
{
    public async Task<DashboardPageViewModel> GetDashboardAsync(
        PortalDatabase database,
        DateTime? date,
        CancellationToken cancellationToken)
    {
        var workDate = (date ?? DateTime.Today).Date;
        var metrics = database switch
        {
            PortalDatabase.JoyeriaPlaza => await GetJoyeriaMetricsAsync(workDate, cancellationToken),
            _ => await GetCompuadmoMetricsAsync(workDate, cancellationToken)
        };

        var recentOperations = await GetOperationsCoreAsync(database, workDate, null, 8, cancellationToken);

        return new DashboardPageViewModel
        {
            Database = database,
            Date = workDate,
            Metrics = metrics,
            RecentOperations = recentOperations
        };
    }

    public Task<OperationsPageViewModel> GetOperationsAsync(
        PortalDatabase database,
        DateTime? date,
        string? query,
        CancellationToken cancellationToken) =>
        BuildOperationsPageAsync(database, (date ?? DateTime.Today).Date, query, cancellationToken);

    public async Task<CommissionsPageViewModel> GetCommissionsAsync(
        PortalDatabase database,
        string? query,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(query);

        if (database == PortalDatabase.JoyeriaPlaza)
        {
            await using var context = await joyeriaFactory.CreateDbContextAsync(cancellationToken);
            var rows = await BuildCommissionQuery(context, normalized).ToListAsync(cancellationToken);
            return new CommissionsPageViewModel
            {
                Database = database,
                Query = normalized ?? string.Empty,
                Commissions = rows
            };
        }

        await using var compuContext = await compuFactory.CreateDbContextAsync(cancellationToken);
        var compuRows = await BuildCommissionQuery(compuContext, normalized).ToListAsync(cancellationToken);
        return new CommissionsPageViewModel
        {
            Database = database,
            Query = normalized ?? string.Empty,
            Commissions = compuRows
        };
    }

    public async Task<VendorsPageViewModel> GetVendorsAsync(
        PortalDatabase database,
        string? query,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(query);
        var vendors = database == PortalDatabase.JoyeriaPlaza
            ? await LoadVendorsAsync(await joyeriaFactory.CreateDbContextAsync(cancellationToken), normalized, cancellationToken)
            : await LoadVendorsAsync(await compuFactory.CreateDbContextAsync(cancellationToken), normalized, cancellationToken);

        return new VendorsPageViewModel
        {
            Database = database,
            Query = normalized ?? string.Empty,
            Vendors = vendors
        };
    }

    public async Task<ProductsPageViewModel> GetProductsAsync(
        PortalDatabase database,
        string? query,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(query);
        var products = database == PortalDatabase.JoyeriaPlaza
            ? await LoadProductsAsync(await joyeriaFactory.CreateDbContextAsync(cancellationToken), normalized, cancellationToken)
            : await LoadProductsAsync(await compuFactory.CreateDbContextAsync(cancellationToken), normalized, cancellationToken);

        return new ProductsPageViewModel
        {
            Database = database,
            Query = normalized ?? string.Empty,
            Products = products
        };
    }

    public async Task<CatalogsPageViewModel> GetCatalogsAsync(
        PortalDatabase database,
        CancellationToken cancellationToken)
    {
        if (database == PortalDatabase.JoyeriaPlaza)
        {
            await using var context = await joyeriaFactory.CreateDbContextAsync(cancellationToken);
            return new CatalogsPageViewModel
            {
                Database = database,
                Guides = await LoadGuidesAsync(context, cancellationToken),
                Transports = await LoadTransportsAsync(context, cancellationToken)
            };
        }

        await using var compuContext = await compuFactory.CreateDbContextAsync(cancellationToken);
        return new CatalogsPageViewModel
        {
            Database = database,
            Guides = await LoadGuidesAsync(compuContext, cancellationToken),
            Transports = await LoadTransportsAsync(compuContext, cancellationToken)
        };
    }

    public async Task<CreateOperationPageViewModel> GetCreateOperationAsync(
        PortalDatabase database,
        CancellationToken cancellationToken,
        string? successMessage = null,
        CreateOperationInputModel? input = null)
    {
        input ??= new CreateOperationInputModel { Database = database };
        input.Database = database;

        var vendors = await GetVendorsAsync(database, null, cancellationToken);
        var catalogs = await GetCatalogsAsync(database, cancellationToken);

        return new CreateOperationPageViewModel
        {
            Input = input,
            Vendors = vendors.Vendors,
            Guides = catalogs.Guides,
            Transports = catalogs.Transports,
            SuccessMessage = successMessage
        };
    }

    public async Task<string> CreateOperationAsync(
        CreateOperationInputModel input,
        CancellationToken cancellationToken)
    {
        var total = input.Subtotal + input.Tax;
        var saleDate = input.SaleDate == default ? DateTime.Now : input.SaleDate;
        var folio = $"WEB-{saleDate:yyyyMMddHHmmss}";
        var folioRegistro = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var folioOperacion = BuildFolioOperacion(saleDate);
        var notes = BuildNotes(input);

        if (input.Database == PortalDatabase.JoyeriaPlaza)
        {
            await using var context = await joyeriaFactory.CreateDbContextAsync(cancellationToken);
            var sellerNumber = int.TryParse(input.SellerKey, out var parsedSeller) ? parsedSeller : 0;

            context.Remisiones.Add(new JoyeriaRemisionHeader
            {
                FolioFactura = folio,
                FolioPedido = folio,
                Fecha = saleDate,
                Tipo = SafeText(input.OperationType, 10),
                Cliente = SafeText(input.Hotel, 6),
                Vendedor = sellerNumber,
                Estatus = "A",
                Subtotal = (double)input.Subtotal,
                Iva = (double)input.Tax,
                Total = (double)total,
                Saldo = 0,
                FechaCobro = saleDate,
                Observaciones = notes,
                Descuento = 0,
                Moneda = 1,
                TipoCambio = (float)input.ExchangeRate,
                Letras = string.Empty,
                Cajero = sellerNumber,
                Procesado = "N",
                NCorte = 0,
                Hora = saleDate,
                Comisionista = SafeText(input.SellerKey, 4),
                Peso = 0,
                CostoTienda = 0,
                CuantosVend = input.PassengerCount,
                UtilidadBruta = 0,
                Almacen = "WEB",
                Usuario = SafeText(input.UserName, 4),
                FolioOperacion = folioOperacion,
                FolioRegistro = folioRegistro
            });

            await context.SaveChangesAsync(cancellationToken);
            return folio;
        }

        await using var compuContext = await compuFactory.CreateDbContextAsync(cancellationToken);
        var guideNumber = int.TryParse(input.GuideCode, out var parsedGuide) ? parsedGuide : 0;

        compuContext.Remisiones.Add(new CompuadmoRemisionHeader
        {
            FolioRemision = folio,
            FolioFactura = folio,
            Fecha = saleDate,
            TipoF = SafeText(input.OperationType, 10),
            Cliente = SafeText(input.Hotel, 6),
            Vendedor = SafeText(input.SellerKey, 4),
            Estatus = "A",
            Subtotal = (double)input.Subtotal,
            Iva = (double)input.Tax,
            Total = (double)total,
            Saldo = 0,
            FechaCobro = saleDate,
            Observaciones = notes,
            Descuento = 0,
            Tipo = 1,
            TipoCambio = (float)input.ExchangeRate,
            Letras = string.Empty,
            Usuario = SafeText(input.UserName, 4),
            Hora = saleDate.ToString("HH:mm:ss"),
            Almacen = "WEB",
            Moneda = "P",
            OperacionServicio = SafeText(input.OperationType, 50),
            Efectivo = (float)input.Cash,
            Tarjeta = (float)input.Card,
            Dolares = (float)input.Dollars,
            CotizacionDolar = (float)input.ExchangeRate,
            FolioOperacion = folioOperacion,
            Guia = guideNumber,
            CodigoGuia = SafeText(input.GuideCode, 4),
            FolioRegistro = folioRegistro,
            Condiciones = "CONTADO"
        });

        await compuContext.SaveChangesAsync(cancellationToken);
        return folio;
    }

    private static IQueryable<CommissionRowViewModel> BuildCommissionQuery<TContext>(
        TContext context,
        string? normalized)
        where TContext : DbContext
    {
        return context.Set<SalesCommissionRow>()
            .AsNoTracking()
            .Where(x => normalized == null
                || EF.Functions.Like(x.Folio ?? string.Empty, $"%{normalized}%")
                || EF.Functions.Like(x.BeneficiaryName ?? string.Empty, $"%{normalized}%")
                || EF.Functions.Like(x.SellerName ?? string.Empty, $"%{normalized}%"))
            .OrderByDescending(x => x.SaleDate)
            .Take(200)
            .Select(x => new CommissionRowViewModel(
                x.Folio ?? string.Empty,
                x.SaleDate,
                x.BeneficiaryName ?? string.Empty,
                x.SellerName ?? string.Empty,
                Convert.ToDecimal((x.FixedAmount ?? 0f) + (x.SportAmount ?? 0f)),
                Convert.ToDecimal((x.FixedCommission ?? 0f) + (x.SportCommission ?? 0f))));
    }

    private async Task<DashboardMetricsViewModel> GetCompuadmoMetricsAsync(
        DateTime workDate,
        CancellationToken cancellationToken)
    {
        await using var context = await compuFactory.CreateDbContextAsync(cancellationToken);
        var remisiones = context.Remisiones.AsNoTracking().Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == workDate);

        var operationsCount = await remisiones.CountAsync(cancellationToken);
        var artesaniasTotal = await context.SalesCommissions.AsNoTracking()
            .Where(x => x.SaleDate.HasValue && x.SaleDate.Value.Date == workDate)
            .SumAsync(x => x.FixedAmount ?? 0f, cancellationToken);
        var joyeriaTotal = await context.SalesCommissions.AsNoTracking()
            .Where(x => x.SaleDate.HasValue && x.SaleDate.Value.Date == workDate)
            .SumAsync(x => x.SportAmount ?? 0f, cancellationToken);
        var paymentsTotal = await remisiones.SumAsync(
            x => (decimal)(x.Efectivo ?? 0) + (decimal)(x.Tarjeta ?? 0) + ((decimal)(x.Dolares ?? 0) * (decimal)(x.CotizacionDolar ?? 1)),
            cancellationToken);
        var expensesTotal = await context.CashExpenses.AsNoTracking()
            .Where(x => x.ExpenseDate.HasValue && x.ExpenseDate.Value.Date == workDate)
            .SumAsync(x => x.TotalExpense ?? 0f, cancellationToken);

        return new DashboardMetricsViewModel(
            operationsCount,
            operationsCount,
            Convert.ToDecimal(artesaniasTotal),
            Convert.ToDecimal(joyeriaTotal),
            paymentsTotal,
            Convert.ToDecimal(expensesTotal));
    }

    private async Task<DashboardMetricsViewModel> GetJoyeriaMetricsAsync(
        DateTime workDate,
        CancellationToken cancellationToken)
    {
        await using var context = await joyeriaFactory.CreateDbContextAsync(cancellationToken);
        var remisiones = context.Remisiones.AsNoTracking().Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == workDate);

        var operationsCount = await remisiones.CountAsync(cancellationToken);
        var artesaniasTotal = await context.SalesCommissions.AsNoTracking()
            .Where(x => x.SaleDate.HasValue && x.SaleDate.Value.Date == workDate)
            .SumAsync(x => x.FixedAmount ?? 0f, cancellationToken);
        var joyeriaTotal = await context.SalesCommissions.AsNoTracking()
            .Where(x => x.SaleDate.HasValue && x.SaleDate.Value.Date == workDate)
            .SumAsync(x => x.SportAmount ?? 0f, cancellationToken);
        var expensesTotal = await context.CashExpenses.AsNoTracking()
            .Where(x => x.ExpenseDate.HasValue && x.ExpenseDate.Value.Date == workDate)
            .SumAsync(x => x.TotalExpense ?? 0f, cancellationToken);

        return new DashboardMetricsViewModel(
            operationsCount,
            operationsCount,
            Convert.ToDecimal(artesaniasTotal),
            Convert.ToDecimal(joyeriaTotal),
            0m,
            Convert.ToDecimal(expensesTotal));
    }

    private async Task<OperationsPageViewModel> BuildOperationsPageAsync(
        PortalDatabase database,
        DateTime workDate,
        string? query,
        CancellationToken cancellationToken)
    {
        var items = await GetOperationsCoreAsync(database, workDate, query, 200, cancellationToken);
        return new OperationsPageViewModel
        {
            Database = database,
            Date = workDate,
            Query = Normalize(query) ?? string.Empty,
            Operations = items
        };
    }

    private async Task<IReadOnlyList<OperationRowViewModel>> GetOperationsCoreAsync(
        PortalDatabase database,
        DateTime workDate,
        string? query,
        int take,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(query);

        if (database == PortalDatabase.JoyeriaPlaza)
        {
            await using var context = await joyeriaFactory.CreateDbContextAsync(cancellationToken);
            var vendorMap = await context.Vendors.AsNoTracking()
                .Where(x => x.VendorKey != null && x.VendorKey.Trim() != string.Empty)
                .Select(x => new
                {
                    Key = x.VendorKey!.Trim(),
                    Name = x.Name ?? string.Empty
                })
                .ToListAsync(cancellationToken);

            var joyeriaVendorMap = vendorMap
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);

            var rows = await context.Remisiones.AsNoTracking()
                .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == workDate)
                .OrderByDescending(x => x.Fecha)
                .Take(take)
                .ToListAsync(cancellationToken);

            return rows
                .Select(x =>
                {
                    var sellerKey = x.Vendedor?.ToString() ?? string.Empty;
                    var sellerName = joyeriaVendorMap.TryGetValue(sellerKey, out var name) && !string.IsNullOrWhiteSpace(name)
                        ? name
                        : sellerKey;
                    return new OperationRowViewModel(
                        x.FolioFactura ?? string.Empty,
                        x.Fecha,
                        sellerName,
                        x.Cliente ?? string.Empty,
                        x.CuantosVend ?? 0,
                        (decimal)(x.Total ?? 0),
                        (decimal)(x.Saldo ?? 0),
                        $"Procesado {(x.Procesado ?? "N")}");
                })
                .Where(x => normalized == null
                    || x.Folio.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || x.SellerName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || x.Hotel.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        await using var compuContext = await compuFactory.CreateDbContextAsync(cancellationToken);
        var compuVendorRows = await compuContext.Vendors.AsNoTracking()
            .Where(x => x.VendorKey != null && x.VendorKey.Trim() != string.Empty)
            .Select(x => new
            {
                Key = x.VendorKey!.Trim(),
                Name = x.Name ?? string.Empty
            })
            .ToListAsync(cancellationToken);

        var compuVendorMap = compuVendorRows
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Name).FirstOrDefault() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        var compuRows = await compuContext.Remisiones.AsNoTracking()
            .Where(x => x.Fecha.HasValue && x.Fecha.Value.Date == workDate)
            .OrderByDescending(x => x.Fecha)
            .Take(take)
            .ToListAsync(cancellationToken);

        return compuRows
            .Select(x =>
            {
                var sellerKey = x.Vendedor?.Trim() ?? string.Empty;
                var sellerName = compuVendorMap.TryGetValue(sellerKey, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : sellerKey;
                return new OperationRowViewModel(
                    x.FolioRemision ?? string.Empty,
                    x.Fecha,
                    sellerName,
                    x.Cliente ?? string.Empty,
                    ExtractPassengerCount(x.Observaciones),
                    (decimal)(x.Total ?? 0),
                    (decimal)(x.Saldo ?? 0),
                    $"Efectivo {(x.Efectivo ?? 0):0.##} / Tarjeta {(x.Tarjeta ?? 0):0.##}");
            })
            .Where(x => normalized == null
                || x.Folio.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || x.SellerName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || x.Hotel.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static async Task<IReadOnlyList<VendorRowViewModel>> LoadVendorsAsync<TContext>(
        TContext context,
        string? query,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        return await context.Set<VendorRow>()
            .AsNoTracking()
            .Where(x => query == null
                || EF.Functions.Like(x.Name ?? string.Empty, $"%{query}%")
                || EF.Functions.Like(x.VendorKey ?? string.Empty, $"%{query}%"))
            .OrderBy(x => x.Name)
            .Take(200)
            .Select(x => new VendorRowViewModel(
                (x.VendorKey ?? string.Empty).Trim(),
                (x.Name ?? string.Empty).Trim(),
                (x.PhoneNumber ?? string.Empty).Trim(),
                Convert.ToDecimal(x.CommissionPercent ?? 0d)))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<ProductRowViewModel>> LoadProductsAsync<TContext>(
        TContext context,
        string? query,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        return await context.Set<ProductRow>()
            .AsNoTracking()
            .Where(x => (x.Active ?? "S").ToUpper() != "N"
                && (query == null
                    || EF.Functions.Like(x.Name ?? string.Empty, $"%{query}%")
                    || EF.Functions.Like(x.Barcode ?? string.Empty, $"%{query}%")
                    || EF.Functions.Like(x.ProductId.ToString(), $"%{query}%")))
            .OrderBy(x => x.Name)
            .Take(200)
            .Select(x => new ProductRowViewModel(
                x.ProductId,
                string.IsNullOrWhiteSpace(x.Barcode) ? x.ProductId.ToString() : x.Barcode!,
                x.Name ?? "Sin nombre",
                x.Department ?? string.Empty,
                Convert.ToDecimal((x.Price1 > 0 ? x.Price1 : x.PublicPrice) ?? 0f),
                Convert.ToDecimal(x.Iva ?? 0f),
                string.IsNullOrWhiteSpace(x.Currency) ? "MXN" : x.Currency!))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<GuideRowViewModel>> LoadGuidesAsync<TContext>(
        TContext context,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        return await context.Set<GuideRow>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new GuideRowViewModel(
                x.GuideKey ?? string.Empty,
                x.Name ?? string.Empty,
                x.Company ?? string.Empty,
                x.PhoneNumber ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TransportRowViewModel>> LoadTransportsAsync<TContext>(
        TContext context,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        return await context.Set<TransportRow>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new TransportRowViewModel(
                x.Code ?? string.Empty,
                x.Name ?? string.Empty))
            .ToListAsync(cancellationToken);
    }

    private static string BuildNotes(CreateOperationInputModel input)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.Notes))
        {
            parts.Add(input.Notes.Trim());
        }
        if (!string.IsNullOrWhiteSpace(input.GuideCode))
        {
            parts.Add($"GUIA:{input.GuideCode.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(input.TransportCode))
        {
            parts.Add($"TRANS:{input.TransportCode.Trim()}");
        }
        parts.Add($"PAX:{input.PassengerCount}");
        parts.Add($"TIPO:{input.OperationType.Trim()}");
        return string.Join(" | ", parts);
    }

    private static int BuildFolioOperacion(DateTime saleDate)
    {
        var raw = saleDate.ToString("ddHHmmss");
        return int.TryParse(raw, out var parsed) ? parsed : 0;
    }

    private static int ExtractPassengerCount(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return 0;
        }

        const string marker = "PAX:";
        var start = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return 0;
        }

        var value = new string(notes[(start + marker.Length)..].TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(value, out var parsed) ? parsed : 0;
    }

    private static string SafeText(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
