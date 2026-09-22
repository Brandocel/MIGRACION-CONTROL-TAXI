using System.Globalization;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Arma el cuadre como datos (no como XML de Excel) para poder mandarlo a Hostinger y que Hoka
/// Solutions lo muestre.
///
/// Vive en la misma clase parcial que <c>ExportCuadreWorkbookAsync</c> a proposito: asi puede
/// llamar a los mismos metodos privados que arman el Excel — <c>BuildCategorySummaries</c>,
/// <c>MergeCamionesIntoCategorySummaries</c>, <c>ApplyPlaza28HistoricalCuadreReconciliation</c>
/// y las correcciones de despliegue — en vez de reimplementar la aritmetica. Mientras las dos
/// salidas partan de la misma lista <c>ordered</c>, el Excel del escritorio y la pantalla de
/// Hoka no pueden discrepar.
/// </summary>
public sealed partial class DesktopOutputService
{
    /// <summary>
    /// Construye el cuadre completo del rango. Recibe exactamente los mismos argumentos que
    /// <see cref="ExportCuadreWorkbookAsync"/> para que quien exporte el Excel pueda empujar el
    /// mismo resultado sin volver a consultar la base.
    /// </summary>
    public CuadrePayload BuildCuadrePayload(
        DateTime start,
        DateTime end,
        IReadOnlyList<LocalRelation> rows,
        IReadOnlyList<LocalCommission> commissions,
        IReadOnlyList<LocalCut> cuts,
        IReadOnlyList<LocalCuadreResumenRow> camiones,
        string branchCode,
        IReadOnlyList<LocalCommissionBrowserRow>? authoritativeCommissions = null)
    {
        // Mismo encadenado que ExportCuadreWorkbookAsync, incluida la regla de esconder OTRO
        // cuando viene en ceros: si aqui se filtrara distinto, Hoka mostraria una fila que el
        // Excel no tiene.
        var summaries = MergeCamionesIntoCategorySummaries(BuildCategorySummaries(rows, authoritativeCommissions), camiones, start);
        var ordered = ConcentratedGroups
            .Select(group => summaries.TryGetValue(group.Code, out var value) ? value : CategorySummary.Empty(group.Code, group.Name))
            .Where(summary => !string.Equals(summary.Code, "OTRO", StringComparison.OrdinalIgnoreCase)
                || summary.Pax != 0
                || summary.Entraron != 0
                || summary.Salieron != 0
                || summary.Unidades != 0
                || summary.Dejada != 0m
                || summary.Comision != 0m
                || summary.Venta != 0m
                || summary.TotalGastos != 0m)
            .ToArray();

        // Las dos correcciones historicas se aplican dentro de BuildCuadreSheet, no antes, asi
        // que hay que repetirlas aqui o el payload saldria con los numeros sin reconciliar.
        var reconciliadas = ApplyPlaza28HistoricalCuadreReconciliation(start, ordered);
        var displayOverrides = GetPlaza28HistoricalCuadreDisplayOverrides(start);

        var resumen = new List<CuadreResumenPayloadRow>(reconciliadas.Count);
        foreach (var summary in reconciliadas)
        {
            displayOverrides.TryGetValue(summary.Code, out var display);
            var ticketPromedio = display.TicketPromedio ?? (summary.Pax > 0 ? summary.Venta / summary.Pax : 0m);
            var porcentajeGasto = display.PorcentajeGasto ?? (summary.Venta > 0 ? summary.TotalGastos / summary.Venta : 0m);

            resumen.Add(new CuadreResumenPayloadRow(
                summary.Name,
                summary.Pax,
                summary.Adultos,
                summary.Jovenes,
                summary.Menores,
                summary.Entraron,
                summary.Salieron,
                summary.Unidades,
                summary.Dejada,
                summary.Comision,
                summary.Venta,
                summary.TotalGastos,
                ticketPromedio,
                porcentajeGasto));
        }

        var camionesPayload = BuildCamionesPayload(start, reconciliadas, camiones);
        var dejadas = BuildDejadasPayload(rows, ResolveSiteLabel(branchCode));
        var hoteles = BuildHotelesPayload(rows);
        var comisionesPayload = BuildComisionesPayload(commissions);
        var cortes = BuildCortesPayload(cuts);

        return new CuadrePayload(
            NormalizeBranchCode(branchCode),
            start.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            reconciliadas.Sum(x => x.Dejada),
            reconciliadas.Sum(x => x.Venta),
            reconciliadas.Sum(x => x.Comision),
            resumen,
            camionesPayload,
            dejadas,
            hoteles,
            comisionesPayload,
            cortes);
    }

    /// <summary>
    /// El bloque "registros de camiones" que en el Excel va debajo de los totales.
    /// El 07/08/2026 el dato bueno esta en las filas ACAR/MC/TUR ya reconciliadas y no en la
    /// respuesta de la API; <c>BuildCuadreSheet</c> hace esa misma excepcion, y repetirla aqui
    /// es lo que mantiene iguales las dos salidas para ese dia.
    /// </summary>
    private static IReadOnlyList<CuadreCamionPayloadRow> BuildCamionesPayload(
        DateTime date,
        IReadOnlyList<CategorySummary> reconciliadas,
        IReadOnlyList<LocalCuadreResumenRow> camiones)
    {
        if (date.Date == new DateTime(2026, 8, 7))
        {
            var dejadaHistorica = reconciliadas
                .Where(x => x.Code is "ACAR" or "MC" or "TUR")
                .Sum(x => x.Dejada);

            return [new CuadreCamionPayloadRow("REGISTROS DE CAMIONES", 0, 0, 0, 0, dejadaHistorica)];
        }

        return camiones
            .Select(x => new CuadreCamionPayloadRow(x.Concepto, x.Pax, x.Entraron, x.Salieron, x.Unidades, x.Dejada))
            .ToList();
    }

    /// <summary>
    /// Las 25 columnas de la hoja "CUADRE dejadas", con el mismo orden y los mismos respaldos
    /// por campo vacio que usa <c>BuildControlDejadasSheet</c> (telefono "S/N", estatus
    /// "pendiente", comision "SIN CALCULAR", taxista "0").
    /// </summary>
    private static IReadOnlyList<CuadreDejadaPayloadRow> BuildDejadasPayload(
        IReadOnlyList<LocalRelation> rows,
        string defaultSiteLabel)
    {
        var lista = new List<CuadreDejadaPayloadRow>(rows.Count);

        foreach (var row in rows.OrderBy(x => ParseRelationDate(x)?.Date).ThenBy(x => ParseRelationDate(x)))
        {
            var date = ParseRelationDate(row);
            var payoutDate = ParseTextDate(row.PayoutDate);

            lista.Add(new CuadreDejadaPayloadRow(
                date is not null ? date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty,
                Clean(row.OperationFolio, row.AppFolio),
                date is not null ? date.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : string.Empty,
                Clean(row.Driver, row.Vendor),
                Clean(row.Vendor, row.Driver),
                Clean(row.TransportType),
                Clean(row.Unit, row.Plates),
                Clean(row.Hotel, row.Origin),
                Clean(row.Site, row.Destination, defaultSiteLabel),
                row.AdultPassengers,
                row.YouthPassengers,
                row.ChildPassengers,
                ResolveRelationPax(row),
                row.Payout ?? 0m,
                Clean(row.Phone, "S/N"),
                row.Sale,
                Clean(row.PayoutStatus, "pendiente"),
                payoutDate is not null
                    ? payoutDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : Clean(row.PayoutDate),
                row.Commission,
                row.CommissionPaid,
                Clean(row.CommissionStatus, "SIN CALCULAR"),
                CleanTicketDetail(row.SaleDetail, row.PosFolio, row.OperationFolio),
                Clean(row.TaxistaId, "0"),
                Clean(row.Badge),
                Clean(row.Nationality, "S/N")));
        }

        return lista;
    }

    /// <summary>Mismo agrupado y mismo orden (venta descendente) que <c>BuildHotelsSheet</c>.</summary>
    private static IReadOnlyList<CuadreHotelPayloadRow> BuildHotelesPayload(IReadOnlyList<LocalRelation> rows) =>
        rows
            .GroupBy(x => Clean(x.Hotel, "SIN HOTEL"))
            .Select(g => new CuadreHotelPayloadRow(
                g.Key,
                g.Sum(x => x.Passengers),
                g.Sum(x => x.Payout ?? 0m),
                g.Sum(x => x.Sale),
                g.Sum(x => x.Commission),
                g.Sum(x => x.CommissionPaid)))
            .OrderByDescending(x => x.Venta)
            .ToList();

    private static IReadOnlyList<CuadreComisionPayloadRow> BuildComisionesPayload(IReadOnlyList<LocalCommission> rows) =>
        rows
            .OrderByDescending(x => x.Date)
            .Select(x => new CuadreComisionPayloadRow(
                Clean(x.Folio),
                x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Clean(x.DriverName),
                x.SaleTotal,
                x.CommissionAmount,
                x.PaidAmount,
                x.Balance,
                Clean(x.Status)))
            .ToList();

    private static IReadOnlyList<CuadreCortePayloadRow> BuildCortesPayload(IReadOnlyList<LocalCut> rows) =>
        rows
            .OrderByDescending(x => x.Date)
            .Select(x => new CuadreCortePayloadRow(
                x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                x.Cash,
                x.Card,
                x.Payments,
                x.Expenses,
                x.Expected,
                x.Counted,
                x.Difference,
                Clean(x.Status)))
            .ToList();

    /// <summary>
    /// El rotulo que el Excel pone en la columna SITIO/HOTEL cuando el registro no trae sitio.
    /// </summary>
    private static string ResolveSiteLabel(string branchCode) =>
        string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase) ? "Casco Viejo" : "Tienda Plaza 28";

    /// <summary>
    /// Traduce el codigo interno de sucursal al que espera la API. El escritorio usa P28 y CV;
    /// la API guarda plaza28 y cascoviejo.
    /// </summary>
    private static string NormalizeBranchCode(string branchCode) =>
        string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase) ? "cascoviejo" : "plaza28";
}
