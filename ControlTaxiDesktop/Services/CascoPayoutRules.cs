using System.Globalization;
using System.Text.Json;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

public sealed record CvPayoutSelection(decimal? Amount, string Detail);

public static class CascoPayoutRules
{
    public static CvPayoutSelection Select(CommissionSettingsRule rule, int? adults)
    {
        if (adults is null)
            return new(null, "Falta el número de adultos confirmado. No se usa PAX, jóvenes ni menores para elegir la dejada.");
        if (adults <= 0)
            return new(null, "Adultos es 0 o inválido: falta confirmar el dato o decidir cómo tratar una operación sin adultos. No se eligió dejada.");
        var band = adults <= 4 ? "1–4 adultos" : "5 o más adultos";
        var amount = adults <= 4 ? rule.CvPayoutOneToFourAdults : rule.CvPayoutFiveOrMoreAdults;
        return amount is null
            ? new(null, $"Falta configurar la dejada de {band} para {rule.Name}.")
            : new(amount, $"Adultos: {adults}. Dejada de {band}: {amount.Value:C2}. No incluye jóvenes ni menores.");
    }

    // AppMovilRegistro.detalle_json.adultCount is the only source for this decision.
    public static int? ReadAdultCount(string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson)) return null;
        try
        {
            using var document = JsonDocument.Parse(detailJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("adultCount", out var value)) return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
                return numeric >= 0 ? numeric : null;
            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var text))
                return text;
            return null;
        }
        catch (JsonException) { return null; }
    }

    public static int? ConsistentAdults(IEnumerable<int?> values)
    {
        var distinct = values.Distinct().ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }

    public static CommissionSimulationInput FromRelation(LocalRelation row, DateTime date)
    {
        // La degustacion de joyeria se descuenta sola, igual que al generar la comision, para que
        // la tabla y la pantalla de comisiones no muestren dos importes distintos del mismo viaje.
        var (degustacion, _) = HardcodedCascoCommissionCatalog.ResolveJewelryTasting(row.JewelrySale);
        return new(date, row.TransportType, row.Sale, row.Sale, row.PaymentMethod,
            0m, 0m, 0m, row.Payout ?? 0m, degustacion, string.Empty, row.CommissionAdultCount);
    }

    public static CommissionSimulationInput FromRecords(IReadOnlyList<CascoAppRecordDetail> rows, DateTime date,
        string transport, decimal sale, string payment, decimal payout, decimal expense) =>
        FromRecord(rows.FirstOrDefault(), date, transport, sale, payment, payout, expense) with
        { AdultCount = ConsistentAdults(rows.Select(row => ReadAdultCount(row.DetailJson))) };

    public static CommissionSimulationInput FromRecord(CascoAppRecordDetail? row, DateTime date,
        string transport, decimal sale, string payment, decimal payout, decimal expense) =>
        new(date, transport, sale, sale, payment, 0m, 0m, 0m, payout, expense, string.Empty,
            ReadAdultCount(row?.DetailJson));
}
