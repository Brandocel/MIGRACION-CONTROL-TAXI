using System.Globalization;

namespace ControlTaxiDesktop.Models;

public sealed record CommissionSettingsRule(
    long Id,
    string Category,
    string Code,
    string Name,
    decimal CommissionPercent,
    decimal CashRetentionPercent,
    decimal CardRetentionPercent,
    decimal AmexRetentionPercent,
    string PaymentKind,
    bool AppliesPayout,
    bool AppliesExpense,
    bool Active,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string UpdatedAt,
    string UpdatedBy,
    string Notes,
    // Importe de dejada del tabulador (la "TARIFA PLAZA 28"). Va al final y con valor por
    // omision para no romper las construcciones posicionales que ya existian.
    decimal PayoutAmount = 0m,
    // Tipo de pax al que aplica la tarifa de dejada: EXTRANJEROS, NACIONALES o vacio (todos).
    // El tabulador cobra distinto por lo mismo segun quien llegue: TAXI VERDE son $350 con
    // extranjeros y $250 con nacionales, y sin esta llave no caben las dos tarifas.
    string PaxKind = "",
    // Numero de moneda del punto de venta (dbo.Monedas). Es la llave estable para saber la
    // forma de pago real: el nombre lo pueden cambiar, el numero no. -1 = la regla no esta
    // amarrada a ninguna moneda.
    int MonedaId = -1)
{
    public string StatusText => Active ? "Activo" : "Inactivo";
    public string PayoutDisplay => PayoutAmount <= 0m
        ? "—"
        : PayoutAmount.ToString("C0", CultureInfo.CurrentCulture);
    public string PaxKindDisplay => string.IsNullOrWhiteSpace(PaxKind) ? "Todos" : PaxKind;
    public string MonedaDisplay => MonedaId < 0 ? "—" : MonedaId.ToString(CultureInfo.InvariantCulture);
    public string EffectiveRange => EffectiveTo is null
        ? $"{EffectiveFrom:dd/MM/yyyy} - ..."
        : $"{EffectiveFrom:dd/MM/yyyy} - {EffectiveTo:dd/MM/yyyy}";
    public string CommissionDisplay => CommissionPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%";
}

public sealed record CommissionSettingsAuditRow(
    long Id,
    DateTime Date,
    string User,
    string Category,
    string Code,
    string FieldName,
    string PreviousValue,
    string NewValue,
    string Reason);

public sealed record CommissionSettingsSummary(
    int ActiveRules,
    int TransportRules,
    int PaymentRules,
    int ExpiringSoon,
    int RecentChanges);

public sealed record CommissionSimulationInput(
    DateTime Date,
    string TransportCodeOrName,
    decimal Sale,
    decimal Subtotal,
    string PaymentMethod,
    decimal Cash,
    decimal Card,
    decimal Amex,
    decimal Payout,
    decimal Expense,
    string OperationType);

public sealed record CommissionSimulationResult(
    string RuleName,
    string Source,
    string Vigency,
    decimal CommissionPercent,
    decimal RetentionPercent,
    decimal BaseAmount,
    decimal Payout,
    decimal Expense,
    decimal FinalCommission,
    string Explanation,
    bool Configured);

public sealed record CommissionPaymentConfiguration(string Code, string Name, string PaymentKind, decimal RetentionPercent, bool Active, int MonedaId = -1);

public sealed record CommissionResolvedRule(
    string Category,
    string Code,
    string Name,
    decimal CommissionPercent,
    decimal CashRetentionPercent,
    decimal CardRetentionPercent,
    decimal AmexRetentionPercent,
    string Source,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool Configured,
    string Diagnostic);

public sealed record CommissionDiagnosticRow(
    long Id,
    DateTime Date,
    string Severity,
    string Category,
    string Concept,
    string Detail,
    string Action,
    string Source);
