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
    string Notes)
{
    public string StatusText => Active ? "Activo" : "Inactivo";
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

public sealed record CommissionPaymentConfiguration(string Code, string Name, string PaymentKind, decimal RetentionPercent, bool Active);

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
