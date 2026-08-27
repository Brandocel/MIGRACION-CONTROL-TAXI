namespace ControlTaxiDesktop.Services;

internal static class CommissionPaymentRules
{
    public const decimal CashRetentionPercent = 0m;
    public const decimal CardRetentionPercent = 19m;
    public const decimal AmexRetentionPercent = 24m;
    private static readonly object Sync = new();
    private static IReadOnlyList<PaymentRule> _configuredPayments = Array.Empty<PaymentRule>();

    public static void ConfigurePayments(IEnumerable<Models.CommissionPaymentConfiguration> rules)
    {
        lock (Sync)
        {
            _configuredPayments = rules
                .Where(x => x.Active && (!string.IsNullOrWhiteSpace(x.Code) || !string.IsNullOrWhiteSpace(x.Name)))
                .Select(x => new PaymentRule(NormalizeKey(x.Code), NormalizeKey(x.Name), x.PaymentKind.Trim().ToUpperInvariant(), NormalizePercent(x.RetentionPercent), x.MonedaId))
                .ToArray();
        }
    }

    public static decimal NetAfterRetention(decimal amount, decimal retentionPercent)
    {
        if (amount <= 0m) return 0m;
        var normalized = NormalizePercent(retentionPercent);
        return amount - amount * (normalized / 100m);
    }

    public static decimal NormalizePercent(decimal value)
    {
        if (value < 0m) return 0m;
        return value <= 1m ? value * 100m : value;
    }

    /// <summary>
    /// Forma de pago resuelta por el NUMERO de moneda del punto de venta (dbo.Monedas).
    ///
    /// Es la via confiable: el numero no cambia aunque le editen el nombre a la moneda. La
    /// deteccion por texto se queda como respaldo para tickets viejos que no traigan numero.
    /// Devuelve null si esa moneda todavia no esta configurada en el catalogo.
    /// </summary>
    public static (string Kind, decimal RetentionPercent)? FindMoneda(int monedaId)
    {
        if (monedaId < 0) return null;
        var rules = _configuredPayments;
        var match = rules.FirstOrDefault(x => x.MonedaId == monedaId);
        return match is null ? null : (match.Kind, match.RetentionPercent);
    }

    public static bool IsAmexMoneda(int monedaId) =>
        FindMoneda(monedaId) is { Kind: var kind } && kind.Equals("AMEX", StringComparison.OrdinalIgnoreCase);

    public static bool IsCardMoneda(int monedaId) =>
        FindMoneda(monedaId) is { Kind: var kind }
        && (kind.Contains("TARJETA", StringComparison.OrdinalIgnoreCase) || kind.Equals("AMEX", StringComparison.OrdinalIgnoreCase));

    public static bool IsAmexPayment(string? paymentName)
    {
        var text = paymentName ?? string.Empty;
        var configured = FindConfiguredPayment(text);
        if (configured is not null)
            return configured.Kind.Equals("AMEX", StringComparison.OrdinalIgnoreCase);

        return text.Contains("AMEX", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AMERICAN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Deja el texto comparable: mayusculas, sin espacios ni puntos y sin acentos.
    /// </summary>
    private static string Compact(string? value)
    {
        var descompuesto = (value ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD);
        var limpio = descompuesto
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant);
        return new string(limpio.ToArray());
    }

    public static bool IsCardPayment(string? paymentName)
    {
        var text = paymentName ?? string.Empty;
        var configured = FindConfiguredPayment(text);
        if (configured is not null)
            return configured.Kind.Contains("TARJETA", StringComparison.OrdinalIgnoreCase)
                || configured.Kind.Equals("AMEX", StringComparison.OrdinalIgnoreCase);

        // Se compara sin espacios, sin puntos y sin acentos: en el punto de venta la misma forma
        // de pago aparece como "T.CREDITO DLS", "T CREDITO", "TARJETA DE CREDITO" o "T.CREDITO",
        // y con la comparacion literal "T.CREDITO DLS" caia como EFECTIVO y se le perdonaba la
        // retencion del 19%. Detectado el 2026-08-21 con un ticket real de $8,880.
        var compact = Compact(text);
        return IsAmexPayment(text)
            || compact.Contains("TCREDITO", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("TDEBITO", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("TARJETACREDITO", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("TARJETADEBITO", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("TDC", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("VISA", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("MASTERCARD", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("MAESTRO", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("BANORTE", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("BANAMEX", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("HSBC", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("SCOTIABANK", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TARJ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("CITI", StringComparison.OrdinalIgnoreCase)
            || text.Contains("BBVA", StringComparison.OrdinalIgnoreCase)
            || text.Contains("BANCOMER", StringComparison.OrdinalIgnoreCase)
            || text.Contains("SANTANDER", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MIFEL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AFIRME", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MERCADO", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("MERCADOPAGO", StringComparison.OrdinalIgnoreCase);
    }

    public static decimal ResolveCardRetention(decimal catalogValue)
    {
        var catalog = NormalizePercent(catalogValue);
        return catalog > 0m ? catalog : ResolveConfiguredRetention("TARJETA_NORMAL", CardRetentionPercent);
    }

    public static decimal ResolveAmexRetention(decimal catalogValue)
    {
        var catalog = NormalizePercent(catalogValue);
        return catalog > 0m ? catalog : ResolveConfiguredRetention("AMEX", AmexRetentionPercent);
    }

    public static decimal ResolvePaymentRetention(string? paymentName, decimal fallbackPercent)
    {
        var configured = FindConfiguredPayment(paymentName);
        return configured is null ? NormalizePercent(fallbackPercent) : configured.RetentionPercent;
    }

    private static decimal ResolveConfiguredRetention(string kind, decimal fallback)
    {
        lock (Sync)
        {
            var match = _configuredPayments.FirstOrDefault(x => x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase));
            return match is null ? fallback : match.RetentionPercent;
        }
    }

    private static PaymentRule? FindConfiguredPayment(string? paymentName)
    {
        var key = NormalizeKey(paymentName ?? string.Empty);
        if (key.Length == 0) return null;
        lock (Sync)
        {
            return _configuredPayments.FirstOrDefault(rule =>
                (!string.IsNullOrWhiteSpace(rule.Code) && key.Contains(rule.Code, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(rule.Name) && key.Contains(rule.Name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    private static string NormalizeKey(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private sealed record PaymentRule(string Code, string Name, string Kind, decimal RetentionPercent, int MonedaId = -1);
}
