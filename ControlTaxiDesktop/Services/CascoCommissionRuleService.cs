using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed record CascoCommissionRule(
    int Id,
    string BranchCode,
    string Proveedor,
    string TipoServicio,
    bool ConTarjeta,
    decimal VentaMinima,
    decimal? VentaMaxima,
    decimal? ComisionAgencia,
    decimal? ComisionTaxista,
    decimal? ComisionVendedor,
    decimal? ComisionDeportiva,
    bool Activo,
    string ReglaNombre,
    bool RequiereValidacion);

public sealed record CascoCommissionPreview(
    string BranchCode,
    string SqlServer,
    string Database,
    string FolioOriginal,
    decimal VentaCompuadmo,
    decimal VentaJoyeria,
    decimal VentaTotal,
    string TransporteOriginal,
    string ProveedorNormalizado,
    string PaymentMethod,
    bool ConTarjeta,
    bool RuleFound,
    CascoCommissionRule? Rule,
    decimal AgencyAmount,
    decimal TaxistaAmount,
    decimal VendorAmount,
    decimal SportAmount,
    decimal CommissionAmount,
    string Detail);

public sealed record CascoCommissionRuleMatch(
    CascoCommissionRule? Rule,
    bool IsAmbiguous,
    string Detail);

public sealed class CascoCommissionRuleService
{
    public const string LocalSqlServer = "REYNA";
    public const string LocalDatabase = "mktCasco";
    public const string LocalCompuadmoDatabase = "compuadmoCasco";
    public const string LocalJoyeriaDatabase = "joyeriaCasco";
    public const decimal DefaultCommissionRate = 0.10m;
    private static readonly CascoCommissionCalculator Calculator = new();

    public static BranchConfiguration BuildLocalBranch() =>
        new(
            Code: "CV",
            Name: "Casco Viejo",
            SqlServer: LocalSqlServer,
            Database: LocalDatabase,
            SiteName: "Casco Viejo",
            ApiBaseUrl: string.Empty,
            IsReadOnly: true)
        {
            CompuadmoDatabase = LocalCompuadmoDatabase,
            JoyeriaDatabase = LocalJoyeriaDatabase,
            SqlUser = "sa"
        };

    public static bool IsLocalCommissionEnvironment(BranchConfiguration? branch, string branchCode)
    {
        if (!string.Equals(branchCode?.Trim(), "CV", StringComparison.OrdinalIgnoreCase))
            return false;

        if (branch is null)
            return false;

        return string.Equals(branch.SqlServer?.Trim(), LocalSqlServer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(branch.Database?.Trim(), LocalDatabase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(branch.CompuadmoDatabase?.Trim(), LocalCompuadmoDatabase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(branch.JoyeriaDatabase?.Trim(), LocalJoyeriaDatabase, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCascoCommissionEnvironment(BranchConfiguration? branch, string branchCode)
    {
        if (!string.Equals(branchCode?.Trim(), "CV", StringComparison.OrdinalIgnoreCase))
            return false;

        if (branch is null || string.IsNullOrWhiteSpace(branch.SqlServer))
            return false;

        var database = branch.Database?.Trim() ?? string.Empty;
        var compuadmo = branch.CompuadmoDatabase?.Trim() ?? string.Empty;
        var joyeria = branch.JoyeriaDatabase?.Trim() ?? string.Empty;

        return (string.Equals(database, LocalDatabase, StringComparison.OrdinalIgnoreCase)
                && string.Equals(compuadmo, LocalCompuadmoDatabase, StringComparison.OrdinalIgnoreCase)
                && string.Equals(joyeria, LocalJoyeriaDatabase, StringComparison.OrdinalIgnoreCase))
            || (string.Equals(database, "mkt", StringComparison.OrdinalIgnoreCase)
                && string.Equals(compuadmo, "compuadmo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(joyeria, "joyeria", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CascoCommissionPreview> PreviewAsync(
        BranchConfiguration branch,
        string sqlPassword,
        string folioOriginal,
        decimal? saleOverride = null,
        string? transportOverride = null,
        string? paymentMethodOverride = null,
        decimal? payoutOverride = null,
        decimal? gastoOverride = null,
        decimal? degustacionOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsCascoCommissionEnvironment(branch, branch.Code))
            throw new InvalidOperationException("El preview de comisiones de Casco solo esta habilitado para configuraciones CV validas.");

        if (string.IsNullOrWhiteSpace(sqlPassword))
            throw new InvalidOperationException("Se requiere la contrasena SQL protegida de Casco para el preview de comisiones.");

        var cleanFolio = (folioOriginal ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanFolio))
            throw new InvalidOperationException("Se requiere un folio original para calcular el preview de comision.");

        var salesProvider = new CascoSalesDataProvider(branch);
        var salesSummary = await salesProvider.GetOperationSaleSummariesAsync(sqlPassword, new[] { cleanFolio });
        salesSummary.TryGetValue(cleanFolio.TrimStart('0'), out var summary);
        summary ??= new CascoSalesDataProvider.CascoOperationSaleSummary(0m, 0m, Array.Empty<string>(), string.Empty, string.Empty);

        var provider = new CascoReadOnlyDataProvider(branch);
        var sourceRow = (await provider.GetDetailedRecordsByOriginalFolioAsync(sqlPassword, cleanFolio, cancellationToken))
            .FirstOrDefault();

        var ventaCompuadmo = summary.Compuadmo;
        var ventaJoyeria = summary.Joyeria;
        var ventaTotal = saleOverride ?? (ventaCompuadmo + ventaJoyeria);
        var transporte = FirstFilled(transportOverride, sourceRow?.TransportType, string.Empty);
        var paymentMethod = FirstFilled(paymentMethodOverride, summary.PaymentDescription, sourceRow is null ? string.Empty : InferPaymentMethod(sourceRow));
        var conTarjeta = IsCardLikePayment(paymentMethod, sourceRow?.Card ?? 0m);
        var proveedor = NormalizeProvider(transporte);
        var rules = await LoadActiveRulesAsync(branch, sqlPassword, cancellationToken);
        var match = ResolveRule(rules, proveedor, transporte, conTarjeta, ventaTotal);
        var rule = match.Rule;

        var payout = payoutOverride ?? 0m;
        var gasto = gastoOverride ?? 0m;
        var degustacion = degustacionOverride ?? 0m;
        var (agencyAmount, taxistaAmount, vendorAmount, sportAmount, commissionAmount) = CalculateAmounts(rule, ventaTotal, payout, gasto, degustacion);

        var detail = match.IsAmbiguous
            ? match.Detail
            : rule is null
            ? "Sin regla activa. Comision calculada en 0.00."
            : string.Join(" | ", new[]
            {
                $"Regla: {rule.ReglaNombre}",
                $"Proveedor: {rule.Proveedor}",
                $"Tipo: {rule.TipoServicio}",
                $"Tarjeta: {(rule.ConTarjeta ? "SI" : "NO")}",
                $"Venta: {ventaTotal.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Dejada: {payout.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Gasto: {gasto.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Degustacion: {degustacion.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Agencia: {agencyAmount.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Taxista: {taxistaAmount.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Vendedor: {vendorAmount.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Deportiva: {sportAmount.ToString("0.00", CultureInfo.InvariantCulture)}",
                $"Comision final: {commissionAmount.ToString("0.00", CultureInfo.InvariantCulture)}"
            });

        return new CascoCommissionPreview(
            branch.Code,
            branch.SqlServer,
            branch.Database,
            cleanFolio,
            ventaCompuadmo,
            ventaJoyeria,
            ventaTotal,
            transporte,
            proveedor,
            paymentMethod,
            conTarjeta,
            rule is not null && !match.IsAmbiguous,
            rule,
            agencyAmount,
            taxistaAmount,
            vendorAmount,
            sportAmount,
            commissionAmount,
            detail);
    }

    public async Task<IReadOnlyList<CascoCommissionRule>> LoadActiveRulesAsync(
        BranchConfiguration branch,
        string sqlPassword,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenMainConnectionAsync(branch, sqlPassword, cancellationToken);
        await using (var existsCommand = new SqlCommand("SELECT OBJECT_ID(N'dbo.ControlTaxiComisiones', N'U');", connection))
        {
            var objectId = await existsCommand.ExecuteScalarAsync(cancellationToken);
            if (objectId is null || objectId == DBNull.Value)
                return Array.Empty<CascoCommissionRule>();
        }

        const string sql = """
            SELECT
                Id,
                BranchCode,
                Proveedor,
                TipoServicio,
                ConTarjeta,
                VentaMinima,
                VentaMaxima,
                ComisionAgencia,
                ComisionTaxista,
                ComisionVendedor,
                ComisionDeportiva,
                Activo,
                ReglaNombre,
                RequiereValidacion
            FROM dbo.ControlTaxiComisiones
            WHERE BranchCode = N'CV'
              AND Activo = 1
            ORDER BY Id;
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<CascoCommissionRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CascoCommissionRule(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                reader.IsDBNull(10) ? null : reader.GetDecimal(10),
                reader.GetBoolean(11),
                reader.GetString(12),
                reader.GetBoolean(13)));
        }

        return rows;
    }

    public CascoCommissionRuleMatch ResolveRule(
        IReadOnlyList<CascoCommissionRule> rules,
        string proveedorNormalizado,
        string tipoServicioOriginal,
        bool conTarjeta,
        decimal ventaTotal)
    {
        var candidates = rules
            .Where(rule => string.Equals(rule.Proveedor, proveedorNormalizado, StringComparison.OrdinalIgnoreCase))
            .Where(rule => rule.ConTarjeta == conTarjeta)
            .Where(rule => ventaTotal >= rule.VentaMinima)
            .Where(rule => !rule.VentaMaxima.HasValue || ventaTotal <= rule.VentaMaxima.Value)
            .ToArray();

        if (candidates.Length == 0)
            return new CascoCommissionRuleMatch(null, false, "Sin regla activa para el proveedor/tipo de pago/rango de venta.");

        var exactType = candidates
            .Where(rule => string.Equals(rule.TipoServicio, tipoServicioOriginal, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var selectedPriority = exactType.Length > 0
            ? exactType
            : candidates
                .Where(rule => string.Equals(rule.TipoServicio, "GENERAL", StringComparison.OrdinalIgnoreCase))
                .ToArray();

        if (selectedPriority.Length == 0)
            return new CascoCommissionRuleMatch(null, false, "Sin regla exacta ni regla GENERAL activa para el transporte.");

        if (selectedPriority.Length > 1)
        {
            var ids = string.Join(", ", selectedPriority.Select(rule => rule.Id.ToString(CultureInfo.InvariantCulture)));
            return new CascoCommissionRuleMatch(null, true, $"REGLA AMBIGUA: coinciden las reglas {ids}.");
        }

        return new CascoCommissionRuleMatch(selectedPriority[0], false, string.Empty);
    }

    private static async Task<SqlConnection> OpenMainConnectionAsync(BranchConfiguration branch, string sqlPassword, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = branch.SqlServer,
            InitialCatalog = branch.Database,
            UserID = string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser,
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 30
        };

        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public static (decimal AgencyAmount, decimal TaxistaAmount, decimal VendorAmount, decimal SportAmount, decimal CommissionAmount) CalculateAmounts(
        CascoCommissionRule? rule,
        decimal ventaTotal,
        decimal dejada = 0m,
        decimal gasto = 0m,
        decimal degustacion = 0m)
    {
        if (rule is null)
            return (0m, 0m, 0m, 0m, 0m);

        var sportAmount = rule.ComisionDeportiva ?? 0m;
        var agencyAmount = rule.ComisionAgencia is decimal agencia
            ? CalculateRuleAmount(ventaTotal, dejada, gasto, degustacion, agencia, "Agencia", rule).ImporteComision
            : 0m;
        var taxistaAmount = rule.ComisionTaxista is decimal taxista
            ? CalculateRuleAmount(ventaTotal, dejada, gasto, degustacion, taxista, "Taxi/Guia", rule).ImporteComision
            : 0m;
        var vendorAmount = rule.ComisionVendedor is decimal vendedor
            ? CalculateRuleAmount(ventaTotal, dejada, gasto, degustacion, vendedor, "Vendedor", rule).ImporteComision
            : 0m;
        var commissionAmount = agencyAmount + taxistaAmount + vendorAmount + sportAmount;

        return (agencyAmount, taxistaAmount, vendorAmount, sportAmount, commissionAmount);
    }

    public static CascoCommissionCalculationResult CalculateRuleAmount(
        decimal ventaTotal,
        decimal dejada,
        decimal gasto,
        decimal degustacion,
        decimal porcentajeComision,
        string tipoComision,
        CascoCommissionRule? rule = null)
    {
        var (specialDiscount, specialDescription) = CalculateSpecialDiscount(rule, ventaTotal);
        return Calculator.Calculate(new CascoCommissionCalculationInput(
            ventaTotal,
            Math.Max(dejada, 0m),
            Math.Max(gasto, 0m),
            Math.Max(degustacion, 0m),
            CascoCommissionCalculator.DefaultDiscountRate,
            porcentajeComision,
            tipoComision,
            specialDiscount,
            specialDescription));
    }

    private static (decimal Amount, string Description) CalculateSpecialDiscount(CascoCommissionRule? rule, decimal ventaTotal)
    {
        if (rule is null)
            return (0m, string.Empty);

        if (string.Equals(rule.Proveedor, "AVENTURAS MAYAS", StringComparison.OrdinalIgnoreCase) && ventaTotal >= 1000m)
        {
            var blocks = Math.Floor(ventaTotal / 1000m);
            return (blocks * 100m, "Bloques $100 por cada $1,000");
        }

        return (0m, string.Empty);
    }

    public static decimal CalculateDefaultCommission(decimal ventaTotal) =>
        ventaTotal <= 0m ? 0m : Decimal.Round(ventaTotal * DefaultCommissionRate, 2, MidpointRounding.AwayFromZero);

    public static bool IsCardLikePayment(string? paymentMethod, decimal cardAmount)
    {
        if (cardAmount > 0m)
            return true;

        var value = (paymentMethod ?? string.Empty).Trim().ToUpperInvariant();
        return value.Contains("TARJETA", StringComparison.Ordinal)
            || value.Contains("AMEX", StringComparison.Ordinal)
            || value.Contains("CARD", StringComparison.Ordinal);
    }

    public static string NormalizeProvider(string? transportType)
    {
        var value = (transportType ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (value.Contains("AVENTURAS", StringComparison.Ordinal))
            return "AVENTURAS MAYAS";
        if (value.Contains("MAJESTIC", StringComparison.Ordinal))
            return "MAJESTIC";
        if (value.Contains("BIKE", StringComparison.Ordinal))
            return "BIKE CID";
        if (value.Contains("CALLE", StringComparison.Ordinal))
            return "CALLE";
        if (value.Contains("EXTREME", StringComparison.Ordinal))
            return "EXTREME";
        if (value.Contains("FARMAC", StringComparison.Ordinal))
            return "TAXIS Y GUIAS IND (FARMACIAS)";
        if (value.Contains("TIENDA", StringComparison.Ordinal))
            return "VENTAS ENTRE TIENDAS";
        if (value.Contains("TAXI", StringComparison.Ordinal) || value.Contains("VAN", StringComparison.Ordinal) || value.Contains("UBER", StringComparison.Ordinal))
            return "TAXIS/VANS";
        return value;
    }

    private static string InferPaymentMethod(CascoAppRecordDetail row)
    {
        var parts = new List<string>();
        if (row.Cash > 0m)
            parts.Add("Efectivo");
        if (row.Card > 0m)
            parts.Add("Tarjeta");
        if (row.Dollars > 0m)
            parts.Add("Dolares");
        return parts.Count == 0 ? "SIN PAGO" : string.Join(" / ", parts);
    }

    private static string FirstFilled(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
