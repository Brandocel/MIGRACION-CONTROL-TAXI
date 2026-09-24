using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

public sealed class CommissionConfigurationResolver(CommissionSettingsRepository settings, string sessionBranch)
{
    public async Task InitializeAsync()
    {
        await settings.InitializeAsync();
        CommissionPaymentRules.ConfigurePayments(await settings.GetActivePaymentConfigurationsAsync(DateTime.Today));

        // Reglas globales (aplican a todos los transportes): venta minima para descontar la dejada.
        CommissionGlobalRules.ConfigurePayoutDeductionMinSale(
            await settings.GetGlobalSettingAsync(
                CommissionSettingsRepository.PayoutDeductionMinSaleKey,
                CommissionGlobalRules.DefaultPayoutDeductionMinSale));
    }

    public async Task<CommissionResolvedRule> ResolveTransportAsync(string? transport, DateTime operationDate, decimal catalogCommission = 0m, decimal catalogCash = 0m, decimal catalogCard = 0m, decimal catalogAmex = 0m)
    {
        var text = Clean(transport);
        // Obtain branch from settings caller: the repository will filter by session branch when provided.
        var rules = await settings.GetRulesAsync("TRANSPORTE", text, true, operationDate, sessionBranch);
        var isCv = string.Equals(sessionBranch.Trim(), "CV", StringComparison.OrdinalIgnoreCase);
        var matches = rules.Where(r => string.Equals(r.Code, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.Name, text, StringComparison.OrdinalIgnoreCase)).ToArray();
        var rule = isCv ? (matches.Length == 1 ? matches[0] : null) : rules.FirstOrDefault();
        if (rule is not null)
        {
            return new CommissionResolvedRule("TRANSPORTE", rule.Code, rule.Name, rule.CommissionPercent, rule.CashRetentionPercent, rule.CardRetentionPercent, rule.AmexRetentionPercent, "CONFIGURACION_LOCAL", rule.EffectiveFrom, rule.EffectiveTo, true, string.Empty);
        }
        // If session branch is CV, do NOT fallback to fixed catalog or percent: signal missing configuration
        if (isCv)
        {
            await settings.LogDiagnosticAsync("FALTA", "TRANSPORTE", text, "Falta regla configurada para este transporte en Casco Viejo.", "Configurar regla en Configuracion de Comisiones (sucursal CV).", "MISSING_RULE_CV");
            return new CommissionResolvedRule("TRANSPORTE", text, text, 0m, 0m, 0m, 0m, "SIN_CONFIGURACION", DateTime.MinValue, null, false, "Falta regla para sucursal CV");
        }

        var normalizedCatalog = CommissionPaymentRules.NormalizePercent(catalogCommission);
        if (normalizedCatalog > 0m)
        {
            return new CommissionResolvedRule("TRANSPORTE", text, text, normalizedCatalog, CommissionPaymentRules.NormalizePercent(catalogCash), CommissionPaymentRules.ResolveCardRetention(catalogCard), CommissionPaymentRules.ResolveAmexRetention(catalogAmex), "CATALOGO_BASE_TRANSPORTE", DateTime.MinValue, null, true, string.Empty);
        }

        var fallbackPercent = IsMajestic(text) ? 8m : IsSalmoran(text) ? 20m : 10m;
        await settings.LogDiagnosticAsync("MEDIA", "TRANSPORTE", text, $"Se uso fallback tecnico {fallbackPercent:0.##}% porque no hay regla vigente ni porcentaje de catalogo.", "Configurar regla vigente en Configuracion de Comisiones.", "FALLBACK_TECNICO");
        return new CommissionResolvedRule("TRANSPORTE", text, text, fallbackPercent, 0m, CommissionPaymentRules.CardRetentionPercent, CommissionPaymentRules.AmexRetentionPercent, "FALLBACK_TECNICO", DateTime.MinValue, null, false, "Regla no configurada; fallback registrado.");
    }

    public async Task<CommissionResolvedRule> ResolvePaymentAsync(string? payment, DateTime operationDate)
    {
        var text = Clean(payment);
        var rule = (await settings.GetRulesAsync("FORMA_PAGO", text, true, operationDate)).FirstOrDefault();
        if (rule is not null)
        {
            return new CommissionResolvedRule("FORMA_PAGO", rule.Code, rule.Name, 0m, rule.CashRetentionPercent, rule.CardRetentionPercent, rule.AmexRetentionPercent, "CONFIGURACION_LOCAL", rule.EffectiveFrom, rule.EffectiveTo, true, string.Empty);
        }

        var kind = CommissionPaymentRules.IsAmexPayment(text)
            ? "AMEX"
            : CommissionPaymentRules.IsCardPayment(text) ? "TARJETA_NORMAL" : "EFECTIVO";
        var retention = kind == "AMEX"
            ? CommissionPaymentRules.AmexRetentionPercent
            : kind == "TARJETA_NORMAL" ? CommissionPaymentRules.CardRetentionPercent : CommissionPaymentRules.CashRetentionPercent;
        await settings.LogDiagnosticAsync("MEDIA", "FORMA_PAGO", text, $"Se uso fallback tecnico {kind}/{retention:0.##}% porque la forma de pago no tiene regla vigente.", "Clasificar forma de pago en Configuracion de Comisiones.", "FALLBACK_TECNICO");
        return new CommissionResolvedRule("FORMA_PAGO", text, text, 0m, kind == "EFECTIVO" ? retention : 0m, kind == "TARJETA_NORMAL" ? retention : 0m, kind == "AMEX" ? retention : 0m, "FALLBACK_TECNICO", DateTime.MinValue, null, false, "Forma de pago no configurada; fallback registrado.");
    }

    public async Task<CommissionSimulationResult> SimulateAsync(CommissionSimulationInput input)
    {
        var date = input.Date.Date;
        var transport = await ResolveTransportAsync(input.TransportCodeOrName, date, 0m, 0m, 0m, 0m);
        var isCv = string.Equals(sessionBranch.Trim(), "CV", StringComparison.OrdinalIgnoreCase);
        if (isCv && !transport.Configured)
            return new CommissionSimulationResult(transport.Name, transport.Source, "Sin vigencia", 0m, 0m, 0m, input.Payout, input.Expense, 0m,
                "Falta una regla CV vigente y única para este transporte en SQLite. No se calculó comisión ni se aplicaron porcentajes de respaldo.", false);
        var payment = isCv ? transport : await ResolvePaymentAsync(input.PaymentMethod, date);
        var sale = input.Subtotal > 0m ? input.Subtotal : input.Sale;
        var cash = input.Cash;
        var card = input.Card;
        var amex = input.Amex;
        var kind = isCv ? (CommissionPaymentRules.IsAmexPayment(input.PaymentMethod) ? "AMEX" : CommissionPaymentRules.IsCardPayment(input.PaymentMethod) ? "TARJETA_NORMAL" : "EFECTIVO")
            : payment.AmexRetentionPercent > 0m ? "AMEX" : payment.CardRetentionPercent > 0m ? "TARJETA_NORMAL" : "EFECTIVO";
        if (cash + card + amex <= 0m)
        {
            if (kind == "AMEX") amex = sale;
            else if (kind == "TARJETA_NORMAL") card = sale;
            else cash = sale;
        }

        var retentionPercent = kind == "AMEX" ? payment.AmexRetentionPercent : kind == "TARJETA_NORMAL" ? payment.CardRetentionPercent : payment.CashRetentionPercent;
        var retained = cash * (payment.CashRetentionPercent / 100m)
            + card * (payment.CardRetentionPercent / 100m)
            + amex * (payment.AmexRetentionPercent / 100m);
        var payout = input.Payout;
        var expense = input.Expense;
        if (isCv)
        {
            var rule = (await settings.GetRulesAsync("TRANSPORTE", input.TransportCodeOrName, true, date, "CV"))
                .Single(r => string.Equals(r.Code, input.TransportCodeOrName.Trim(), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Name, input.TransportCodeOrName.Trim(), StringComparison.OrdinalIgnoreCase));
            payout = rule.AppliesPayout ? payout : 0m;
            expense = rule.AppliesExpense ? expense : 0m;
        }
        var baseAmount = Math.Max(0m, sale - retained - payout - expense);
        var final = Math.Max(0m, decimal.Truncate(baseAmount * (transport.CommissionPercent / 100m)));
        var explanation = $"""
            Regla encontrada: {transport.Name}
            Fuente: {transport.Source}
            Vigencia: {FormatVigency(transport)}
            Venta = {input.Sale:C2}
            Subtotal/Base inicial = {sale:C2}
            Pago = {input.PaymentMethod} ({kind})
            Retencion = {retentionPercent:0.##}% ({retained:C2})
            Base = venta - retencion - dejada - gasto = {baseAmount:C2}
            Dejada aplicada = {payout:C2}
            Gasto aplicado = {expense:C2}
            Porcentaje comision = {transport.CommissionPercent:0.##}%
            Regla redondeo = truncar decimales
            Resultado = {final:C2}
            """;
        return new CommissionSimulationResult(transport.Name, transport.Source, FormatVigency(transport), transport.CommissionPercent, retentionPercent, baseAmount, input.Payout, input.Expense, final, explanation, transport.Configured && payment.Configured);
    }

    public static string FormatVigency(CommissionResolvedRule rule) =>
        rule.EffectiveFrom == DateTime.MinValue
            ? "Fallback sin vigencia"
            : rule.EffectiveTo is null ? $"{rule.EffectiveFrom:dd/MM/yyyy} - ..." : $"{rule.EffectiveFrom:dd/MM/yyyy} - {rule.EffectiveTo:dd/MM/yyyy}";

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
    private static bool IsMajestic(string value) => value.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase) || value.Contains("TRAVEL EXPERIENCE", StringComparison.OrdinalIgnoreCase) || value.Contains("MAESTIC", StringComparison.OrdinalIgnoreCase);
    private static bool IsSalmoran(string value) => value.Contains("SALMORAN", StringComparison.OrdinalIgnoreCase);
}
