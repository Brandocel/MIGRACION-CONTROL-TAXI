namespace ControlTaxiDesktop.Services;

/// <summary>
/// Reglas globales de comision: aplican a TODOS los transportes por igual, a diferencia de los
/// porcentajes que se configuran transporte por transporte.
///
/// Se cargan una vez al abrir la pantalla de Comisiones (ver
/// CommissionConfigurationResolver.InitializeAsync) y viven aqui porque el calculo de comision
/// es estatico.
/// </summary>
internal static class CommissionGlobalRules
{
    /// <summary>
    /// Valor con el que arranca el sistema si nadie ha configurado la regla.
    /// Definido con el usuario el 2026-08-20.
    /// </summary>
    public const decimal DefaultPayoutDeductionMinSale = 400m;

    private static readonly object Sync = new();
    private static decimal _payoutDeductionMinSale = DefaultPayoutDeductionMinSale;

    /// <summary>
    /// Venta minima para que la DEJADA se descuente de la base de comision.
    ///
    /// Regla: cuando la venta de tienda es MENOR O IGUAL a este monto, la venta es tan chica
    /// que descontarle la dejada dejaria al taxista sin comision, asi que NO se descuenta.
    /// Arriba de este monto se descuenta como siempre.
    ///
    /// Ejemplo con el valor por omision (400) y una dejada de $200:
    ///   Venta $1,000 -> se descuenta -> base $800
    ///   Venta   $401 -> se descuenta -> base $201
    ///   Venta   $400 -> NO se descuenta -> base $400
    ///   Venta   $300 -> NO se descuenta -> base $300
    /// </summary>
    public static decimal PayoutDeductionMinSale
    {
        get { lock (Sync) { return _payoutDeductionMinSale; } }
    }

    public static void ConfigurePayoutDeductionMinSale(decimal value)
    {
        lock (Sync)
        {
            // Un umbral negativo no tiene sentido; 0 es valido y significa "descontar siempre".
            _payoutDeductionMinSale = value < 0m ? 0m : value;
        }
    }

    /// <summary>
    /// Indica si a una venta le corresponde que se le descuente la dejada.
    /// </summary>
    public static bool ShouldDeductPayout(decimal saleTotal) => saleTotal > PayoutDeductionMinSale;
}
