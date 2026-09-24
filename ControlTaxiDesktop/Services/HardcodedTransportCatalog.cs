using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Catalogo unico de comisiones por transporte, fijo en el programa.
///
/// POR QUE ESTA AQUI Y NO EN LA BASE LOCAL
/// Cada maquina guardaba su propia copia en su SQLite y las copias divergieron: el 2026-09-02 una
/// caja tenia 7 unidades configuradas y la de desarrollo 16. Las que faltaban eran las de mas
/// movimiento (taxis verdes, vans verdes, Uber, transportadoras), asi que en esa caja esas
/// unidades se cobraban con el catalogo viejo del punto de venta -- VAN TRANSPORTADORAS al 20 %
/// cuando la regla dice 10 %. De ahi venia el "no se respeta la configuracion". Al viajar con el
/// programa, todas las maquinas calculan igual desde el dia que se instala.
///
/// COMO FUNCIONAN LAS VIGENCIAS
/// Esto NO es una lista de tarifas: es una lista de PERIODOS. Una unidad puede aparecer tantas
/// veces como tarifas haya tenido, cada una con su rango de fechas. El sistema no guarda el
/// porcentaje calculado, lo recalcula en cada consulta contra la fecha del folio.
///
/// HOY NINGUNA UNIDAD TIENE PERIODOS PARTIDOS: por decision del negocio (2026-09-02) la tarifa
/// vigente aplica a TODO el historico, para que los folios viejos tambien cuadren con la tarifa
/// correcta. Por eso todos los renglones arrancan en <see cref="Inicio"/> y ninguno lleva fecha
/// de fin. El mecanismo de periodos sigue disponible para cuando haga falta.
///
/// PARA CAMBIAR UNA TARIFA
/// Hay dos formas, y la diferencia importa:
/// - Si la tarifa nueva debe aplicar tambien a lo viejo (corregir un error), se edita el renglon.
/// - Si es un cambio de tarifa a partir de una fecha y lo anterior se cobro distinto, NO se edita:
///   se le pone <see cref="Entry.EffectiveTo"/> con el ultimo dia que estuvo vigente y se agrega
///   otro renglon que empieza al dia siguiente. Editar el renglon viejo recalcularia todos los
///   folios pasados de esa unidad con la tarifa nueva.
///
/// REGLAS AL EDITAR ESTA LISTA
/// - Los periodos de una misma unidad no se traslapan y no dejan huecos.
/// - El periodo abierto (el vigente) lleva EffectiveTo = null.
/// - El nombre se escribe TAL CUAL esta en el catalogo del punto de venta, con todo y espacios
///   repetidos: "UBER  ALIANZA" lleva DOS espacios porque asi esta en dbo.transporte y asi llega
///   en cada captura.
/// - El catalogo arranca en <see cref="Inicio"/> y cubre TODA la historia, no solo el año en
///   curso: asi cualquier folio, por viejo que sea, se recalcula con este catalogo y no con el
///   del punto de venta. Pedido del negocio el 2026-09-02 para que los folios viejos tambien
///   cuadren.
/// </summary>
public static class HardcodedTransportCatalog
{
    /// <summary>Un periodo de vigencia de una unidad.</summary>
    public sealed record Entry(
        string Code,
        string Name,
        decimal CommissionPercent,
        decimal CashRetentionPercent,
        decimal PayoutAmount,
        DateTime EffectiveFrom,
        DateTime? EffectiveTo = null,
        Extras? Extra = null);

    /// <summary>
    /// Campos extra por unidad para reglas especiales que pida el negocio, sin tener que tocar el
    /// calculo. Todos vienen apagados: una unidad sin <c>Extra</c> calcula exactamente igual que
    /// siempre. Para una regla nueva se llena aqui y se agrega como periodo nuevo con su fecha.
    ///
    /// Formula completa por folio:
    ///   base = venta - retencion (efectivo/tarjeta/AMEX) - DescuentoExtraPorcentaje de la venta
    ///          - dejada (o DescuentoFijoPorLlegada) - gastos
    ///   comision = base x porcentaje + BonoFijoPorLlegada
    /// </summary>
    /// <param name="DescuentoFijoPorLlegada">
    /// Pesos fijos que se quitan de la base por cada llegada, en lugar de la dejada capturada o
    /// tabulada. Ejemplo: MAJESTIC $500 desde el 19/09/2026. 0 = se usa la dejada normal.
    /// </param>
    /// <param name="UmbralVentaChica">
    /// Venta del folio a partir de la cual se descuenta la dejada. null = la regla general
    /// (<see cref="CommissionGlobalRules"/>, $400). 0 = se descuenta siempre, aunque la base quede
    /// en negativo.
    /// </param>
    /// <param name="DescuentoExtraPorcentaje">
    /// Porcentaje de la venta que se quita de la base ademas de la retencion del banco.
    /// Ejemplo: 5 = se quita otro 5 % de la venta. 0 = nada.
    /// </param>
    /// <param name="BonoFijoPorLlegada">
    /// Pesos que se SUMAN a la comision por cada llegada, despues del porcentaje. 0 = nada.
    /// </param>
    public sealed record Extras(
        decimal DescuentoFijoPorLlegada = 0m,
        decimal? UmbralVentaChica = null,
        decimal DescuentoExtraPorcentaje = 0m,
        decimal BonoFijoPorLlegada = 0m)
    {
        public static Extras Ninguno { get; } = new();

        /// <summary>Texto para la pantalla de Configuracion de comisiones.</summary>
        public string Describir()
        {
            var partes = new List<string>();
            if (DescuentoFijoPorLlegada > 0m) partes.Add($"descuento fijo de ${DescuentoFijoPorLlegada:N0} por llegada");
            if (UmbralVentaChica is { } u) partes.Add(u <= 0m ? "descuenta aunque la venta sea chica" : $"descuenta desde ventas de ${u:N0}");
            if (DescuentoExtraPorcentaje > 0m) partes.Add($"quita {DescuentoExtraPorcentaje:0.##} % extra de la venta");
            if (BonoFijoPorLlegada > 0m) partes.Add($"bono de ${BonoFijoPorLlegada:N0} por llegada");
            return partes.Count == 0 ? string.Empty : "Regla especial: " + string.Join("; ", partes) + ".";
        }
    }

    /// <summary>Retenciones iguales para todas las unidades (confirmado 2026-09-02).</summary>
    public const decimal CardRetentionPercent = 19m;
    public const decimal AmexRetentionPercent = 24m;

    /// <summary>
    /// Arranque del catalogo. Va deliberadamente antes que cualquier dato del sistema (lo mas
    /// viejo que existe es de noviembre de 2022) para que NINGUN folio, por antiguo que sea, se
    /// quede sin regla y termine cobrandose con el catalogo del punto de venta.
    ///
    /// Decision del negocio el 2026-09-02: la tarifa vigente aplica a TODO el historico, sin
    /// conservar ninguna tarifa vieja. Los folios anteriores se recalculan con este catalogo, asi
    /// que donde antes habia diferencias ahora aparecen saldos:
    ///   GUIAS                8 % -> 10 %   (se pago de menos: sale PARCIAL con saldo)
    ///   VAN TRANSPORTADORAS 20 % -> 10 %   (se pago de mas)
    ///   TRAVEL EXPERIENCE   20 % -> 10 %   (se pago de mas)
    ///   MAJESTIC            20 % ->  8 %   (se pago de mas; es el de mayor volumen)
    /// </summary>
    private static readonly DateTime Inicio = new(2020, 1, 1);

    /// <summary>
    /// Catalogo revisado y autorizado por el negocio el 2026-09-02. Al cambiarlo hay que
    /// actualizar tambien el documento "Catalogo-de-comisiones.docx" de la carpeta Documentacion.
    /// </summary>
    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("GUIA", "GUIAS", 10m, 0m, 50m, Inicio),

        // Majestic SI retiene 10 % en efectivo (confirmado por operacion 2026-09-03: el folio
        // C-5094-BX14665458012 de ULISES QUENO salio sin quitarle nada). Es retencion, no
        // comision: la comision sigue siendo 8 %.
        //
        // Regla del negocio del 19/09/2026 (sólo Plaza 28): a cada llegada de Majestic se le
        // descuentan $500 fijos de la base, sin importar lo capturado ni el tamaño de la venta.
        // Si la venta no alcanza, la base queda en negativo. Por decision del usuario aplica a
        // TODO el historico, igual que el resto del catalogo; si se quiere que arranque en una
        // fecha, se parte en dos periodos (hasta el dia anterior sin Extra, desde la fecha con el).
        new("MAJESTIC", "MAJESTIC EXPEDITIONS", 8m, 10m, 0m, Inicio,
            Extra: new(DescuentoFijoPorLlegada: 500m, UmbralVentaChica: 0m)),

        // TRAVEL EXPERIENCE va aparte de MAJESTIC aunque en el punto de venta compartan renglon
        // (dbo.transporte tiene tipo=MAJESTIC, nombre=TRAVEL EXPERIENCE, 20 %). El negocio las
        // cobra distinto: MAJESTIC al 8 % y TRAVEL al 10 % (confirmado 2026-09-02), asi que cada
        // una lleva su propia clave para que no se roben el emparejamiento.
        new("TRAVEL", "TRAVEL EXPERIENCE", 10m, 0m, 0m, Inicio),

        new("TAXIAZUL", "TAXI AZUL", 10m, 0m, 250m, Inicio),
        new("TAXICAFE", "TAXI CAFE", 10m, 0m, 250m, Inicio),
        new("TAXIROJO", "TAXI ROJO", 10m, 0m, 300m, Inicio),
        new("TAXIVERDE", "TAXI VERDE", 10m, 0m, 0m, Inicio),
        new("TULAKA", "TULAKA", 20m, 16m, 200m, Inicio),
        new("TURIBUS ADO", "TURIBUS ADO", 10m, 0m, 100m, Inicio),
        new("TURIBUS SALMORAN", "TURIBUS SALMORAN", 20m, 16m, 200m, Inicio),
        new("UBER", "UBER", 10m, 0m, 200m, Inicio),

        // Dos espacios entre UBER y ALIANZA, igual que en dbo.transporte. Con un solo espacio
        // deja de emparejar con lo que llega capturado.
        new("TAXIAL", "UBER  ALIANZA", 10m, 0m, 250m, Inicio),

        new("VANAZUL", "VAN AZUL", 10m, 0m, 350m, Inicio),
        new("VANCAFE", "VAN CAFE", 10m, 0m, 350m, Inicio),
        new("VANR", "VAN ROJA", 10m, 0m, 0m, Inicio),

        new("VANTR", "VAN TRANSPORTADORAS", 10m, 0m, 0m, Inicio),

        new("VANVERDE", "VAN VERDE", 10m, 0m, 0m, Inicio),
    ];

    /// <summary>
    /// El catalogo en el formato que usa la pantalla de Configuracion de comisiones, para poder
    /// mostrarlo (solo lectura) sin depender de la base local.
    /// </summary>
    public static IReadOnlyList<CommissionSettingsRule> AsRules()
    {
        var result = new List<CommissionSettingsRule>(Entries.Count);
        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            result.Add(new CommissionSettingsRule(
                -(i + 1), // Id negativo: no existe en la base local, no se puede editar ni versionar.
                "TRANSPORTE",
                entry.Code,
                entry.Name,
                entry.CommissionPercent,
                entry.CashRetentionPercent,
                CardRetentionPercent,
                AmexRetentionPercent,
                string.Empty,
                true,
                true,
                true,
                entry.EffectiveFrom,
                entry.EffectiveTo,
                string.Empty,
                "CATALOGO FIJO",
                string.IsNullOrEmpty(entry.Extra?.Describir())
                    ? "Catalogo unico del sistema. Igual en todas las maquinas."
                    : "Catalogo unico del sistema. " + entry.Extra!.Describir(),
                entry.PayoutAmount,
                string.Empty,
                -1));
        }
        return result;
    }
}
