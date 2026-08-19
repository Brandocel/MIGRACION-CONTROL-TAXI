using System;
using System.Globalization;

namespace ControlTaxiDesktop.Services;

public sealed record CascoCommissionCalculationInput(
    decimal VentaBruta,
    decimal Dejada,
    decimal Gasto,
    decimal Degustacion,
    decimal PorcentajeDescuento,
    decimal PorcentajeComision,
    string TipoComision,
    decimal DescuentoEspecial = 0m,
    string DescuentoEspecialDescripcion = "");

public sealed record CascoCommissionCalculationResult(
    decimal VentaBruta,
    decimal PorcentajeDescuento,
    decimal ImporteDescuento,
    decimal DescuentoEspecial,
    string DescuentoEspecialDescripcion,
    decimal Dejada,
    decimal Gasto,
    decimal Degustacion,
    decimal BaseComisionable,
    decimal PorcentajeComision,
    decimal ImporteComision,
    string TipoComision,
    string Detail);

public sealed class CascoCommissionCalculator
{
    public const decimal DefaultDiscountRate = 0.19m;

    public CascoCommissionCalculationResult Calculate(CascoCommissionCalculationInput input)
    {
        if (input.VentaBruta < 0m)
            throw new InvalidOperationException("La venta bruta no puede ser negativa.");
        if (input.Dejada < 0m)
            throw new InvalidOperationException("La dejada no puede ser negativa.");
        if (input.Gasto < 0m)
            throw new InvalidOperationException("El gasto no puede ser negativo.");
        if (input.Degustacion < 0m)
            throw new InvalidOperationException("La degustacion no puede ser negativa.");
        if (input.PorcentajeDescuento < 0m)
            throw new InvalidOperationException("El porcentaje de descuento no puede ser negativo.");
        if (input.PorcentajeComision < 0m)
            throw new InvalidOperationException("El porcentaje de comision no puede ser negativo.");
        if (input.DescuentoEspecial < 0m)
            throw new InvalidOperationException("El descuento especial no puede ser negativo.");

        var discountAmount = Money(input.VentaBruta * input.PorcentajeDescuento);
        var baseAmount = input.VentaBruta
            - input.DescuentoEspecial
            - discountAmount
            - input.Dejada
            - input.Gasto
            - input.Degustacion;
        baseAmount = Money(Math.Max(baseAmount, 0m));
        var commission = Money(baseAmount * input.PorcentajeComision);
        var type = string.IsNullOrWhiteSpace(input.TipoComision)
            ? "SIN CLASIFICAR"
            : input.TipoComision.Trim();

        var detail = string.Join(" | ", new[]
        {
            $"Venta bruta: {Money(input.VentaBruta).ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Descuento especial {SpecialDiscountLabel(input)}: {Money(input.DescuentoEspecial).ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Descuento {input.PorcentajeDescuento:P0}: {discountAmount.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Dejada: {Money(input.Dejada).ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Gasto: {Money(input.Gasto).ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Degustacion: {Money(input.Degustacion).ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Base comisionable: {baseAmount.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Comision {input.PorcentajeComision:P0}: {commission.ToString("0.00", CultureInfo.InvariantCulture)}",
            $"Tipo comision: {type}"
        });

        return new CascoCommissionCalculationResult(
            Money(input.VentaBruta),
            input.PorcentajeDescuento,
            discountAmount,
            Money(input.DescuentoEspecial),
            input.DescuentoEspecialDescripcion,
            Money(input.Dejada),
            Money(input.Gasto),
            Money(input.Degustacion),
            baseAmount,
            input.PorcentajeComision,
            commission,
            type,
            detail);
    }

    private static decimal Money(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string SpecialDiscountLabel(CascoCommissionCalculationInput input) =>
        string.IsNullOrWhiteSpace(input.DescuentoEspecialDescripcion)
            ? "N/A"
            : input.DescuentoEspecialDescripcion.Trim();
}
