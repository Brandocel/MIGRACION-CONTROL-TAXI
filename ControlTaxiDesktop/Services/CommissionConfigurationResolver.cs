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
        var isCv = string.Equals(sessionBranch.Trim(), "CV", StringComparison.OrdinalIgnoreCase);
        // En Casco la busqueda va por el proveedor normalizado ("VAN BLANCA 7914" -> "TAXIS/VANS"),
        // que es como se guardan las reglas. Buscando el texto tal cual casi nunca empatan y todo
        // se iria al respaldo del 10 %.
        var rules = await settings.GetRulesAsync("TRANSPORTE", isCv ? string.Empty : text, true, operationDate, sessionBranch);
        var matches = MatchTransportRules(rules, text, isCv);
        var rule = isCv ? (matches.Length == 1 ? matches[0] : null) : rules.FirstOrDefault();
        if (rule is not null)
        {
            return new CommissionResolvedRule("TRANSPORTE", rule.Code, rule.Name, rule.CommissionPercent, rule.CashRetentionPercent, rule.CardRetentionPercent, rule.AmexRetentionPercent, "CONFIGURACION_LOCAL", rule.EffectiveFrom, rule.EffectiveTo, true, string.Empty);
        }
        if (isCv)
        {
            // Dos reglas que empatan es un error de captura, no un caso a calcular: se avisa y no
            // se inventa un importe, igual que hacia el motor anterior ("REGLA AMBIGUA").
            if (matches.Length > 1)
            {
                await settings.LogDiagnosticAsync("ALTA", "TRANSPORTE", text, $"Hay {matches.Length} reglas CV vigentes para este transporte.", "Dejar una sola regla vigente en Configuracion de Comisiones (sucursal CV).", "REGLA_AMBIGUA_CV");
                return new CommissionResolvedRule("TRANSPORTE", text, text, 0m, 0m, 0m, 0m, CascoAmbiguousSource, DateTime.MinValue, null, false, "Mas de una regla CV vigente");
            }

            // Sin regla se conserva el 10 % de respaldo de Casco: un transporte nuevo o mal escrito
            // no puede dejar al taxista sin comision. Queda anotado en el diagnostico para que se
            // configure la regla de verdad.
            await settings.LogDiagnosticAsync("FALTA", "TRANSPORTE", text, "Falta regla configurada para este transporte en Casco Viejo. Se aplica el 10 % de respaldo.", "Configurar regla en Configuracion de Comisiones (sucursal CV).", "MISSING_RULE_CV");
            return new CommissionResolvedRule("TRANSPORTE", text, text, CascoFallbackPercent, 0m, 0m, 0m, CascoFallbackSource, DateTime.MinValue, null, false, "Falta regla CV; se aplica el 10 % de respaldo");
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
        if (isCv && string.Equals(transport.Source, CascoAmbiguousSource, StringComparison.Ordinal))
            return new CommissionSimulationResult(transport.Name, transport.Source, "Sin vigencia", 0m, 0m, 0m, input.Payout, input.Expense, 0m,
                "Hay mas de una regla CV vigente para este transporte. No se calcula hasta dejar una sola.", false);

        if (isCv && string.Equals(transport.Source, CascoFallbackSource, StringComparison.Ordinal))
        {
            var ventaBruta = input.Subtotal > 0m ? input.Subtotal : input.Sale;
            // Mismo respaldo que el motor anterior: 10 % de la venta, sin descontar nada.
            var respaldo = ventaBruta <= 0m ? 0m : decimal.Round(ventaBruta * (CascoFallbackPercent / 100m), 2, MidpointRounding.AwayFromZero);
            return new CommissionSimulationResult(transport.Name, transport.Source, "Sin vigencia", CascoFallbackPercent, 0m, ventaBruta, 0m, 0m, respaldo,
                $"Sin regla CV para {transport.Name}. Se aplica el 10 % de respaldo sobre la venta: {respaldo:C2}. Configura la regla para que deje de usarse el respaldo.", false);
        }
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
        var selectedPayout = input.Payout;
        var payoutDetail = string.Empty;
        var expense = input.Expense;
        var specialDiscount = 0m;
        var specialDetail = string.Empty;
        if (isCv)
        {
            var text = Clean(input.TransportCodeOrName);
            // SingleOrDefault y no Single: si la regla desaparece entre una consulta y otra se
            // sigue calculando con el respaldo, no truena la pantalla con una excepcion.
            var rule = MatchTransportRules(await settings.GetRulesAsync("TRANSPORTE", string.Empty, true, date, "CV"), text, true)
                .SingleOrDefault();
            if (rule is null)
            {
                var ventaBruta = sale;
                var respaldo = ventaBruta <= 0m ? 0m : decimal.Round(ventaBruta * (CascoFallbackPercent / 100m), 2, MidpointRounding.AwayFromZero);
                return new CommissionSimulationResult(transport.Name, CascoFallbackSource, "Sin vigencia", CascoFallbackPercent, 0m, ventaBruta, 0m, 0m, respaldo,
                    $"La regla CV de {transport.Name} ya no esta vigente. Se aplica el 10 % de respaldo: {respaldo:C2}.", false);
            }

            if (!rule.AppliesPayout)
            {
                payout = 0m;
                selectedPayout = 0m;
                payoutDetail = "Esta regla no descuenta dejada.";
            }
            else
            {
                var selection = CascoPayoutRules.Select(rule, input.AdultCount);
                if (selection.Amount is decimal banda)
                {
                    selectedPayout = banda;
                    payout = banda;
                    payoutDetail = selection.Detail;
                }
                else
                {
                    // Sin adultos confirmados o sin bandas capturadas se usa la dejada que trae el
                    // viaje, que es como se venia calculando. Se avisa en el detalle, pero nunca se
                    // deja la comision sin calcular.
                    selectedPayout = input.Payout;
                    payout = input.Payout;
                    payoutDetail = selection.Detail + $" Se usa la dejada del viaje: {input.Payout:C2}.";
                }
            }

            expense = rule.AppliesExpense ? expense : 0m;
            (specialDiscount, specialDetail) = HardcodedCascoCommissionCatalog.ResolveSpecialDiscount(text, sale);
        }
        var baseAmount = Math.Max(0m, sale - retained - payout - expense - specialDiscount);
        // Plaza 28 trunca los centavos; Casco los redondea a dos decimales, que es como venia
        // calculando su motor anterior. Truncar en Casco movia cada comision unos centavos hacia
        // abajo contra lo que el negocio ya tenia pagado.
        var bruto = baseAmount * (transport.CommissionPercent / 100m);
        var final = Math.Max(0m, isCv ? decimal.Round(bruto, 2, MidpointRounding.AwayFromZero) : decimal.Truncate(bruto));
        var explanation = $"""
            Regla encontrada: {transport.Name}
            Fuente: {transport.Source}
            Vigencia: {FormatVigency(transport)}
            Venta = {input.Sale:C2}
            Subtotal/Base inicial = {sale:C2}
            Pago = {input.PaymentMethod} ({kind})
            Retencion = {retentionPercent:0.##}% ({retained:C2})
            Base = venta - retencion - dejada - gasto = {baseAmount:C2}
            {payoutDetail}
            {specialDetail}
            Dejada aplicada = {payout:C2}
            Gasto aplicado = {expense:C2}
            Porcentaje comision = {transport.CommissionPercent:0.##}%
            Regla redondeo = truncar decimales
            Resultado = {final:C2}
            """;
        return new CommissionSimulationResult(transport.Name, transport.Source, FormatVigency(transport), transport.CommissionPercent, retentionPercent, baseAmount, selectedPayout, input.Expense, final, explanation, transport.Configured && payment.Configured);
    }

    /// <summary>
    /// Empata el transporte del viaje con las reglas. En Casco se compara tambien contra el
    /// proveedor normalizado, que es el nombre con el que el negocio tiene sus reglas.
    /// </summary>
    public static CommissionSettingsRule[] MatchTransportRules(IReadOnlyList<CommissionSettingsRule> rules, string transport, bool isCasco)
    {
        var text = Clean(transport);

        // Primero el nombre tal cual. Manda sobre el normalizado a proposito: UBER tiene su propia
        // regla (dejada de $100) y a la vez normaliza a TAXIS/VANS; sin esta preferencia empataban
        // las dos y el viaje salia como "regla ambigua".
        var exact = rules.Where(rule =>
                string.Equals(rule.Code, text, StringComparison.OrdinalIgnoreCase)
                || string.Equals(rule.Name, text, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exact.Length > 0 || !isCasco) return exact;

        var normalized = CascoCommissionRuleService.NormalizeProvider(text);
        return rules.Where(rule =>
                string.Equals(rule.Code, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(rule.Name, normalized, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>Respaldo de Casco cuando un transporte no tiene regla capturada.</summary>
    public const decimal CascoFallbackPercent = 10m;
    public const string CascoFallbackSource = "RESPALDO_CV";
    public const string CascoAmbiguousSource = "REGLA_AMBIGUA_CV";

    public static string FormatVigency(CommissionResolvedRule rule) =>
        rule.EffectiveFrom == DateTime.MinValue
            ? "Fallback sin vigencia"
            : rule.EffectiveTo is null ? $"{rule.EffectiveFrom:dd/MM/yyyy} - ..." : $"{rule.EffectiveFrom:dd/MM/yyyy} - {rule.EffectiveTo:dd/MM/yyyy}";

    private static string Clean(string? value) => (value ?? string.Empty).Trim();
    private static bool IsMajestic(string value) => value.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase) || value.Contains("TRAVEL EXPERIENCE", StringComparison.OrdinalIgnoreCase) || value.Contains("MAESTIC", StringComparison.OrdinalIgnoreCase);
    private static bool IsSalmoran(string value) => value.Contains("SALMORAN", StringComparison.OrdinalIgnoreCase);
}
