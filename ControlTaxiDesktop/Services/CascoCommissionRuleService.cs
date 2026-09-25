using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ControlTaxiDesktop.Models;
using SqliteDatabase = ControlTaxiDesktop.Services.LocalDatabase;

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
    bool RequiereValidacion,
    // Que se le descuenta a la venta antes de sacar el porcentaje, regla por regla. Salen del
    // Excel de comisiones de Casco (hoja CASCO) confirmado con el negocio el 19/09/2026:
    //  - la retencion del banco (19 %) solo va con tarjeta, salvo MAJESTIC que la lleva siempre;
    //  - la dejada solo se descuenta en BIKE CID, TAXIS/VANS y FARMACIAS.
    // Antes el calculo aplicaba las cuatro cosas a todas las reglas por igual.
    bool AplicaRetencion = true,
    bool AplicaDejada = true,
    bool AplicaGasto = true,
    bool AplicaDegustacion = true);

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
        var sourceRows = await provider.GetDetailedRecordsByOriginalFolioAsync(sqlPassword, cleanFolio, cancellationToken);
        var sourceRow = sourceRows.FirstOrDefault();

        var ventaCompuadmo = summary.Compuadmo;
        var ventaJoyeria = summary.Joyeria;
        var ventaTotal = saleOverride ?? (ventaCompuadmo + ventaJoyeria);
        var transporte = FirstFilled(transportOverride, sourceRow?.TransportType, string.Empty);
        var paymentMethod = FirstFilled(paymentMethodOverride, summary.PaymentDescription, sourceRow is null ? string.Empty : InferPaymentMethod(sourceRow));
        var conTarjeta = IsCardLikePayment(paymentMethod, sourceRow?.Card ?? 0m);
        var proveedor = NormalizeProvider(transporte);
        var database = new SqliteDatabase();
        await database.InitializeAsync();
        var settings = new CommissionSettingsRepository(database);
        // Primera vez en esta maquina: se traen las reglas de Casco desde SQL Server.
        await CascoCommissionRuleImporter.EnsureImportedAsync(settings, branch, sqlPassword, "SISTEMA", cancellationToken);

        var dateValid = DateTime.TryParse(sourceRow?.OperationDate, out var operationDate);
        var input = CascoPayoutRules.FromRecords(sourceRows, operationDate, transporte, ventaTotal,
            paymentMethod, payoutOverride ?? 0m, (gastoOverride ?? 0m) + (degustacionOverride ?? 0m));
        var simulation = dateValid
            ? await settings.SimulateAsync(input, "CV")
            : new CommissionSimulationResult(transporte, "SIN_CONFIGURACION", "Sin fecha", 0m, 0m, 0m, 0m, 0m, 0m,
                "No se calculó comisión: falta una fecha de operación válida.", false);

        // SingleOrDefault sobre el mismo empatado que usa el calculo: con Single, dos reglas
        // vigentes o ninguna tiraban la pantalla con una excepcion en lugar de avisar.
        var storedRule = simulation.Configured
            ? CommissionConfigurationResolver.MatchTransportRules(
                    await settings.GetRulesAsync("TRANSPORTE", string.Empty, true, operationDate, "CV"), transporte, true)
                .SingleOrDefault()
            : null;
        var rule = storedRule is null
            ? null
            : new CascoCommissionRule(checked((int)storedRule.Id),
                "CV", proveedor, transporte, conTarjeta, 0m, null, null, simulation.CommissionPercent / 100m, null, null, true, simulation.RuleName, false);
        var agencyAmount = 0m;
        var taxistaAmount = simulation.FinalCommission;
        var vendorAmount = 0m;
        var sportAmount = 0m;
        var commissionAmount = simulation.FinalCommission;
        var detail = (rule is null
            ? "Sin regla configurada para este proveedor. "
            : "Adultos tomados del registro guardado (detalle_json.adultCount). ") + simulation.Explanation;
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
            rule is not null,
            rule,
            agencyAmount,
            taxistaAmount,
            vendorAmount,
            sportAmount,
            commissionAmount,
            detail);
    }

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
