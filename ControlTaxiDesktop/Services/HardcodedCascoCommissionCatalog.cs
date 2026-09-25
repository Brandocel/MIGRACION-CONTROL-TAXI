using System;
using System.Collections.Generic;
using System.Linq;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Catalogo de comisiones de Casco Viejo, fijo en el programa.
///
/// POR QUE ESTA AQUI Y NO EN LA BASE DE CADA MAQUINA
/// Igual que el de Plaza 28 (<see cref="HardcodedTransportCatalog"/>): guardado en la base local,
/// cada caja terminaba con su propia copia y calculaban distinto. Aqui viaja con el programa, asi
/// que el dia que se instala todas las maquinas cobran lo mismo. Tambien evita que Casco dependa
/// de una tabla en SQL Server que alguien tenga que sembrar a mano.
///
/// DE DONDE SALEN LOS NUMEROS
/// Del Excel del negocio "CALCULO DE COMISIONES, PTO MORELOS.xlsx", hojas CASCO (que se descuenta
/// y que porcentaje se paga), DEJADA (tabulador por adultos) y DEGUSTACION. Confirmado el
/// 25/09/2026.
///
/// COMO SE LEE CADA RENGLON DEL EXCEL
/// La hoja arma una resta: venta, menos el 19 % del banco (solo con tarjeta, salvo MAJESTIC que lo
/// lleva siempre), menos la dejada, el gasto y la degustacion cuando ese proveedor los quita. Al
/// resultado se le saca el porcentaje. Cuando el Excel paga dos comisiones sobre la misma base
/// (AVENTURAS MAYAS: 10 % guia + 2 % agencia), aqui van sumadas, porque el sistema paga un solo
/// importe.
///
/// LO QUE NO ESTA AQUI, A PROPOSITO
/// - FARMACIAS: el Excel dice "20 % y/o 10 %" segun si el medicamento es controlado. Falta que el
///   negocio entregue la lista de controlados, asi que no tiene regla y cae en el respaldo del
///   10 %, que es como se venia calculando.
/// - La comision del vendedor y la deportiva (40/45 de meta, 30 %, 35 %, 50 %): quedaron fuera del
///   alcance desde el 19/09/2026.
/// - La hoja MATILDE: es otra tienda y todavia no esta definido como distingue el sistema una
///   venta suya de una de Casco.
///
/// PARA CAMBIAR UNA TARIFA: mismas reglas que el catalogo de Plaza 28. Si la tarifa nueva aplica
/// tambien a lo viejo, se edita el renglon; si es un cambio a partir de una fecha, se le pone
/// EffectiveTo al renglon viejo y se agrega otro que empieza al dia siguiente.
/// </summary>
public static class HardcodedCascoCommissionCatalog
{
    /// <summary>
    /// El catalogo cubre toda la historia: un folio viejo se recalcula con esta tarifa y no con
    /// otra cosa. Misma decision que en Plaza 28.
    /// </summary>
    public static readonly DateTime Inicio = new(2000, 1, 1);

    /// <summary>Retencion del banco que el Excel descuenta antes de sacar la comision.</summary>
    public const decimal RetencionBanco = 19m;

    public sealed record Entry(
        string Code,
        string Name,
        decimal CommissionPercent,
        decimal CashRetentionPercent,
        decimal CardRetentionPercent,
        bool AppliesPayout,
        bool AppliesExpense,
        decimal? PayoutOneToFourAdults,
        decimal? PayoutFiveOrMoreAdults,
        string Notes,
        DateTime? EffectiveTo = null);

    /// <summary>
    /// Las reglas tal como estan en el Excel. El orden es el de la hoja CASCO.
    ///
    /// Los nombres son los que usa el negocio; el programa empata el transporte del viaje contra
    /// ellos con <see cref="CascoCommissionRuleService.NormalizeProvider"/>, que traduce lo que
    /// llega de la app ("VAN BLANCA 7914", "TAXI VERDE") al proveedor del Excel.
    /// </summary>
    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("BIKE CID", "BIKE CID", 10m, 0m, RetencionBanco, true, true, 50m, 100m,
            "10 % de agencia. Quita 19 % con tarjeta, dejada, gasto y degustacion. Dejada del tabulador: $50 de 1 a 4 adultos y $100 de 5 en adelante."),

        new("TAXIS/VANS", "TAXIS/VANS", 10m, 0m, RetencionBanco, true, true, 150m, 200m,
            "10 % del taxista. Quita 19 % con tarjeta, dejada, gasto y degustacion. Dejada del tabulador de TAXI: $150 de 1 a 4 adultos y $200 de 5 en adelante, igual desde Playa del Carmen, Cancun o Puerto Morelos."),

        new("UBER", "UBER", 10m, 0m, RetencionBanco, true, true, 100m, 100m,
            "Mismo 10 % de TAXIS/VANS, pero el tabulador de dejada de Uber es $100 con cualquier numero de adultos."),

        new("UBER +", "UBER +", 10m, 0m, RetencionBanco, true, true, 150m, 150m,
            "Mismo 10 % de TAXIS/VANS, con dejada de $150 con cualquier numero de adultos."),

        new("CALLE", "CALLE", 0m, 0m, RetencionBanco, false, true,  null, null,
            "En el Excel la venta de calle no paga comision de taxi ni de agencia: solo lleva comision de vendedor, que quedo fuera del alcance. Queda en 0 % a proposito para que no se invente un importe."),

        new("EXTREME", "EXTREME", 10m, 0m, RetencionBanco, false, true, null, null,
            "10 % de guia. Quita 19 % con tarjeta, gasto y degustacion. NO quita dejada."),

        new("AVENTURAS MAYAS", "AVENTURAS MAYAS", 12m, 0m, RetencionBanco, false, true, null, null,
            "10 % de guia mas 2 % de agencia. Quita 19 % con tarjeta, gasto y degustacion, y ademas $100 por cada $1,000 de venta a partir de $1,000."),

        new("MAJESTIC", "MAJESTIC", 12m, RetencionBanco, RetencionBanco, false, true, null, null,
            "8 % de guia mas 4 % de agencia. El 19 % se quita TAMBIEN sin tarjeta, es el unico proveedor asi (confirmado 19/09/2026). No quita dejada."),

        new("VENTAS ENTRE TIENDAS", "VENTAS ENTRE TIENDAS", 50m, 0m, RetencionBanco, false, true, null, null,
            "50 % de comision de Matilde. Quita 19 % con tarjeta, gasto y degustacion."),
    ];

    /// <summary>
    /// El Excel las tiene como reglas del negocio; el resto del programa las consume como
    /// cualquier otra regla de comision, para que la pantalla de configuracion, el simulador y el
    /// calculo salgan todos de la misma fuente.
    /// </summary>
    public static IReadOnlyList<CommissionSettingsRule> BuildRules()
    {
        var updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        return Entries.Select((entry, index) => new CommissionSettingsRule(
            Id: -(index + 1),
            Category: "TRANSPORTE",
            Code: entry.Code,
            Name: entry.Name,
            CommissionPercent: entry.CommissionPercent,
            CashRetentionPercent: entry.CashRetentionPercent,
            CardRetentionPercent: entry.CardRetentionPercent,
            AmexRetentionPercent: entry.CardRetentionPercent,
            PaymentKind: string.Empty,
            AppliesPayout: entry.AppliesPayout,
            AppliesExpense: entry.AppliesExpense,
            Active: true,
            EffectiveFrom: Inicio,
            EffectiveTo: entry.EffectiveTo,
            UpdatedAt: updated,
            UpdatedBy: "CATALOGO CASCO",
            Notes: "Catalogo unico del sistema. " + entry.Notes)
        {
            Branch = "CV",
            CvPayoutOneToFourAdults = entry.PayoutOneToFourAdults,
            CvPayoutFiveOrMoreAdults = entry.PayoutFiveOrMoreAdults
        }).ToArray();
    }

    /// <summary>Las reglas vigentes en una fecha, que es como las pide el calculo.</summary>
    public static IReadOnlyList<CommissionSettingsRule> BuildRules(DateTime? date, string? search = null, bool? active = null)
    {
        var rules = BuildRules().AsEnumerable();
        if (date is DateTime dia)
            rules = rules.Where(rule => rule.EffectiveFrom.Date <= dia.Date
                && (rule.EffectiveTo is null || rule.EffectiveTo.Value.Date >= dia.Date));
        if (active is bool quiereActivas)
            rules = rules.Where(rule => rule.Active == quiereActivas);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var texto = search.Trim();
            rules = rules.Where(rule => rule.Code.Contains(texto, StringComparison.OrdinalIgnoreCase)
                || rule.Name.Contains(texto, StringComparison.OrdinalIgnoreCase));
        }
        return rules.ToArray();
    }

    /// <summary>
    /// Descuento especial de AVENTURAS MAYAS: $100 por cada $1,000 de venta desde $1,000. Va
    /// pegado al proveedor y no a un porcentaje, por eso no cabe en los campos normales.
    /// </summary>
    public static (decimal Amount, string Detail) ResolveSpecialDiscount(string? providerOrTransport, decimal sale)
    {
        var provider = CascoCommissionRuleService.NormalizeProvider(providerOrTransport);
        if (!string.Equals(provider, "AVENTURAS MAYAS", StringComparison.OrdinalIgnoreCase) || sale < 1000m)
            return (0m, string.Empty);

        var amount = Math.Floor(sale / 1000m) * 100m;
        return (amount, $"Descuento especial AVENTURAS MAYAS: {amount:C2} ($100 por cada $1,000).");
    }

    /// <summary>
    /// Degustacion de joyeria, del tabulador del Excel: $100 hasta $20,000 de venta de joyeria y
    /// $200 de ahi en adelante. Solo joyeria se calcula solo, porque el Excel dice que ahi
    /// SIEMPRE se quita. La de licores depende de si la venta trae algun articulo de licor, dato
    /// que el sistema no ve, asi que esa se sigue capturando a mano.
    /// </summary>
    public static (decimal Amount, string Detail) ResolveJewelryTasting(decimal jewelrySale)
    {
        if (jewelrySale <= 0m)
            return (0m, string.Empty);

        var amount = jewelrySale <= 20000m ? 100m : 200m;
        return (amount, $"Degustacion de joyeria del tabulador: {amount:C2} sobre una venta de joyeria de {jewelrySale:C2}.");
    }
}
