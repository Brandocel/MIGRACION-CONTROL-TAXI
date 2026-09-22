using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Arma el cuadre de Plaza 28 sin abrir ninguna ventana, leyendo las mismas fuentes que usa el
/// Centro de Reportes cuando alguien exporta el Excel.
///
/// Existe para que el cuadre pueda subirse solo desde el sincronizador. Antes la publicacion
/// dependia de que alguien apretara "EXCEL CUADRE": si nadie exportaba, Hoka se quedaba con el
/// dato del dia anterior y nadie se enteraba. El sincronizador ya corre cada 5 minutos como
/// tarea programada en las maquinas de la tienda, asi que publicar desde ahi es lo que hace que
/// el reporte de Hoka este siempre al dia sin que nadie tenga que acordarse.
///
/// Las consultas son las mismas cuatro que hace <c>ExportCuadreExcel_Click</c>. No se recalcula
/// nada por otro lado: la aritmetica del cuadre sigue viviendo en <see cref="DesktopOutputService"/>.
/// </summary>
public sealed class CuadreSnapshotService
{
    private readonly LocalDatabase _database;

    public CuadreSnapshotService(LocalDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Consulta el rango y devuelve el cuadre listo para mandar a Hostinger.
    /// </summary>
    public async Task<CuadrePayload> BuildPlaza28Async(DateTime start, DateTime end)
    {
        var operations = new LocalOperationsRepository(_database);
        var pos = new LocalPosRepository(_database);
        var siteName = new BranchConfigurationService().GetBranch("P28").SiteName;

        var relations = await operations.GetReportRelationsAsync(start, end, siteName);
        var authoritativeCommissions = await pos.GetCommissionBrowserRowsAsync(null, start, end);
        var commissions = MapCommissions(authoritativeCommissions);
        var cuts = (await pos.GetCutsAsync())
            .Where(x => x.Date.Date >= start.Date && x.Date.Date <= end.Date)
            .ToArray();
        var camiones = await pos.GetCamionesResumenAsync(start, end);

        return new DesktopOutputService().BuildCuadrePayload(
            start,
            end,
            relations,
            commissions,
            cuts,
            camiones,
            "P28",
            authoritativeCommissions);
    }

    /// <summary>
    /// Arma el cuadre y lo publica. Devuelve el resultado en lugar de lanzar, porque quien llama
    /// es el sincronizador y un error aqui no debe tumbar el resto de su ciclo.
    /// </summary>
    public async Task<CuadrePushResult> BuildAndPushPlaza28Async(DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await BuildPlaza28Async(start, end);
            return await CuadrePushService.ForBranch("P28").PushAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new CuadrePushResult(false, "No se pudo armar el cuadre: " + ex.Message, 0, 0);
        }
    }

    /// <summary>
    /// Copia del mapeo que hace el Centro de Reportes al pasar de la vista de comisiones a las
    /// filas que consume el libro. Se repite aqui porque el original es privado de PosWindow y
    /// no vale la pena abrir la ventana entera para reusarlo.
    /// </summary>
    private static IReadOnlyList<LocalCommission> MapCommissions(IEnumerable<LocalCommissionBrowserRow> rows) =>
        rows.Select((row, index) => new LocalCommission(
            index + 1,
            row.Folio,
            row.SaleFolio,
            row.Gafete,
            row.Nombre,
            row.Fecha,
            row.VentaTotal,
            row.PagoComision,
            row.Pagado,
            row.Saldo,
            string.IsNullOrWhiteSpace(row.Estatus) ? "PENDIENTE" : row.Estatus)).ToArray();
}
