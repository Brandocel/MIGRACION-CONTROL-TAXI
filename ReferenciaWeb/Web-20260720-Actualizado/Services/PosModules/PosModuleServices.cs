using ControlTaxiWeb.Interfaces;
using ControlTaxiWeb.Models.PosTriton;

namespace ControlTaxiWeb.Services.PosModules
{
    public sealed class PosAuthService(IPosSqlMirrorService pos) : IPosAuthService
    {
        public Task<bool> ValidateUserAsync(string usuario, string password) =>
            pos.ValidateUserAsync(usuario, password);
    }

    public sealed class PosDashboardService : IPosDashboardService
    {
        public PosDashboardViewModel BuildDashboard() => new();
    }

    public sealed class PosRegistroService(IPosSqlMirrorService pos) : IPosRegistroService
    {
        public Task<PosRegistroDiarioViewModel?> TryGetAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null) =>
            pos.TryGetRegistroDiarioAsync(folioOperacion, fechaInicio, fechaFin);

        public Task<bool> UpdateAsync(string folioOperacion, string staff, int pax, DateTime fechaTrabajo, string usuario) =>
            pos.UpdateRegistroAsync(folioOperacion, staff, pax, fechaTrabajo, usuario);

        public Task<string?> CreateAppAsync(PosRegistroAppViewModel model, string usuario) =>
            pos.CreateRegistroAppAsync(model, usuario);

        public Task<List<PosRegistroAppTaxistaOption>> SearchTaxistasAsync(string? query, int limit = 20) =>
            pos.SearchRegistroAppTaxistasAsync(query, limit);

        public Task<List<PosRegistroAppTarifaOption>> SearchTarifasAsync(string? query, int limit = 50) =>
            pos.SearchRegistroAppTarifasAsync(query, limit);

        public Task<List<string>> SearchHotelesAsync(string? query, int limit = 50) =>
            pos.SearchRegistroAppHotelesAsync(query, limit);
    }

    public sealed class PosVentasService(IPosSqlMirrorService pos) : IPosVentasService
    {
        public Task<PosVentasViewModel?> TryGetAsync(string? productoBusqueda = null) =>
            pos.TryGetVentasAsync(productoBusqueda);

        public Task<PosVentaDetalleViewModel?> TryGetProductAsync(int productoId) =>
            pos.TryGetProductoVentaAsync(productoId);

        public Task<string?> CreateAsync(PosVentasViewModel model) =>
            pos.CreateVentaAsync(model);

        public Task<List<PosVentaDetalleViewModel>> TryGetRemisionProductosAsync(string folioRegistro) =>
            pos.TryGetRemisionProductosAsync(folioRegistro);
    }

    public sealed class PosPagosService(IPosSqlMirrorService pos) : IPosPagosService
    {
        public Task<PosPagosViewModel?> TryGetAsync(string? folioOperacion = null) =>
            pos.TryGetPagosAsync(folioOperacion);

        public Task<bool> RegisterAsync(string folioOperacion, decimal importe, string usuario) =>
            pos.RegisterPagoAsync(folioOperacion, importe, usuario);
    }

    public sealed class PosGastosService(IPosSqlMirrorService pos) : IPosGastosService
    {
        public Task<PosGastosViewModel?> TryGetAsync(string? folioOperacion = null) =>
            pos.TryGetGastosAsync(folioOperacion);

        public Task<bool> UpdateAsync(string folioOperacion, decimal importe, string concepto, string observaciones, string usuario) =>
            pos.UpdateGastoAsync(folioOperacion, importe, concepto, observaciones, usuario);
    }

    public sealed class PosCortesService(IPosSqlMirrorService pos) : IPosCortesService
    {
        public Task<PosCorteViewModel?> TryGetAsync(DateTime? fecha = null) =>
            pos.TryGetCorteAsync(fecha);

        public Task<bool> CloseAsync(string usuario, DateTime fecha) =>
            pos.CloseCorteAsync(usuario, fecha);
    }

    public sealed class PosComisionesService(IPosSqlMirrorService pos) : IPosComisionesService
    {
        public Task<PosComisionesViewModel?> TryGetAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null) =>
            pos.TryGetComisionesAsync(folioOperacion, fechaInicio, fechaFin);

        public Task<List<PosComisionRowViewModel>> GetPagosReporteAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null) =>
            pos.GetPagosComisionesReporteAsync(fechaInicio, fechaFin);

        public Task<int> RecalcularAsync(string? folioOperacion = null, string usuario = "WEB", DateTime? fecha = null) =>
            pos.RecalcularComisionesAsync(folioOperacion, usuario, fecha);

        public Task<bool> PayAsync(string folioOperacion, string usuario) =>
            pos.PayComisionAsync(folioOperacion, usuario);
    }

    public sealed class PosCatalogosService(IPosSqlMirrorService pos) : IPosCatalogosService
    {
        public Task<PosCatalogoViewModel?> TryGetTransportesAsync() =>
            pos.TryGetTransportesAsync();

        public Task<PosCatalogoViewModel?> TryGetGuiasAsync() =>
            pos.TryGetGuiasAsync();

        public Task<PosCatalogoViewModel?> TryGetTaxistasAsync(string? busqueda = null, int pagina = 1, int tamanoPagina = 50) =>
            pos.TryGetTaxistasAsync(busqueda, pagina, tamanoPagina);

        public Task<bool> SaveTransporteAsync(string clave, string nombre, decimal minimo, decimal maximo, decimal comision, decimal descEfectivo, decimal descTarjeta, decimal descAmex, string usuario) =>
            pos.SaveTransporteAsync(clave, nombre, minimo, maximo, comision, descEfectivo, descTarjeta, descAmex, usuario);

        public Task<bool> SaveGuiaAsync(string clave, string nombre, string telefono, decimal comision, string estatus, string usuario) =>
            pos.SaveGuiaAsync(clave, nombre, telefono, comision, estatus, usuario);

        public Task<bool> SaveTaxistaAsync(string clave, string nombre, string telefono, string unidad, string placas, string estatus, string usuario) =>
            pos.SaveTaxistaAsync(clave, nombre, telefono, unidad, placas, estatus, usuario);
    }

    public sealed class PosGafetesService(IPosSqlMirrorService pos) : IPosGafetesService
    {
        public Task<PosGafetesViewModel?> TryGetAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, bool soloSinFecha = false) =>
            pos.TryGetGafetesAsync(fechaInicio, fechaFin, soloSinFecha);

        public Task<bool> InsertAsync(int matricula, string gafete, long? folioOperacion, string movimiento, string usuario) =>
            pos.InsertGafeteAsync(matricula, gafete, folioOperacion, movimiento, usuario);

        public Task<bool> UpdateAsync(int? matriculaOriginal, string? gafeteOriginal, long? folioOperacionOriginal, int matricula, string gafete, long? folioOperacion, string movimiento, string usuario) =>
            pos.UpdateGafeteAsync(matriculaOriginal, gafeteOriginal, folioOperacionOriginal, matricula, gafete, folioOperacion, movimiento, usuario);

        public Task<bool> MarcarRegresoAsync(string gafete, long? folioOperacion, string usuario) =>
            pos.MarcarRegresoGafeteAsync(gafete, folioOperacion, usuario);

        public Task<(int Actualizados, int NoActualizados)> MarcarRegresoMasivoAsync(string gafetes, long? folioOperacion, string usuario) =>
            pos.MarcarRegresoGafetesAsync(gafetes, folioOperacion, usuario);

        public Task<PosGafeteRowViewModel?> TryFindAsync(string gafete) =>
            pos.TryFindGafeteAsync(gafete);
    }

    public sealed class PosUsuariosService(IPosSqlMirrorService pos) : IPosUsuariosService
    {
        public Task<PosUsuariosViewModel?> TryGetAsync(string? usuario = null) =>
            pos.TryGetUsuariosAsync(usuario);

        public Task<IReadOnlyCollection<string>> GetPermissionsAsync(string usuario) =>
            pos.GetUserPermissionsAsync(usuario);

        public Task<bool> SaveAsync(string usuario, string password, string rol, string estatus, IEnumerable<string> permisos, string usuarioActor) =>
            pos.SaveUsuarioAsync(usuario, password, rol, estatus, permisos, usuarioActor);
    }

    public sealed class PosRelacionesService(IPosSqlMirrorService pos) : IPosRelacionesService
    {
        public Task<PosRelacionesViewModel?> TryGetAsync(string? busqueda = null, DateTime? fechaInicio = null, DateTime? fechaFin = null) =>
            pos.TryGetRelacionesAsync(busqueda, fechaInicio, fechaFin);

        public Task<List<PosRelacionRowViewModel>> GetReporteDejadasAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, string? busqueda = null) =>
            pos.GetRelacionesReporteDejadasAsync(fechaInicio, fechaFin, busqueda);

        public Task<List<CuadreCamionesSummary>> GetCamionesResumenAsync(DateTime? fechaInicio, DateTime? fechaFin) =>
            pos.GetCamionesResumenAsync(fechaInicio, fechaFin);

        public Task<bool> SaveAsync(PosRelacionesViewModel model, string usuario) =>
            pos.SaveRelacionAsync(model, usuario);

        public Task<PosDejadaTicketViewModel?> PayDejadaAsync(string? folioControl, string? folioApp, string? folioOperacion, string? folioPos, string usuario) =>
            pos.PayDejadaAsync(folioControl, folioApp, folioOperacion, folioPos, usuario);
    }
}
