using ControlTaxiWeb.Models.PosTriton;

namespace ControlTaxiWeb.Interfaces
{
    public interface IPosAuthService
    {
        Task<bool> ValidateUserAsync(string usuario, string password);
    }

    public interface IPosDashboardService
    {
        PosDashboardViewModel BuildDashboard();
    }

    public interface IPosRegistroService
    {
        Task<PosRegistroDiarioViewModel?> TryGetAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<bool> UpdateAsync(string folioOperacion, string staff, int pax, DateTime fechaTrabajo, string usuario);
        Task<string?> CreateAppAsync(PosRegistroAppViewModel model, string usuario);
        Task<List<PosRegistroAppTaxistaOption>> SearchTaxistasAsync(string? query, int limit = 20);
        Task<List<PosRegistroAppTarifaOption>> SearchTarifasAsync(string? query, int limit = 50);
        Task<List<string>> SearchHotelesAsync(string? query, int limit = 50);
    }

    public interface IPosVentasService
    {
        Task<PosVentasViewModel?> TryGetAsync(string? productoBusqueda = null);
        Task<PosVentaDetalleViewModel?> TryGetProductAsync(int productoId);
        Task<string?> CreateAsync(PosVentasViewModel model);
        Task<List<PosVentaDetalleViewModel>> TryGetRemisionProductosAsync(string folioRegistro);
    }

    public interface IPosPagosService
    {
        Task<PosPagosViewModel?> TryGetAsync(string? folioOperacion = null);
        Task<bool> RegisterAsync(string folioOperacion, decimal importe, string usuario);
    }

    public interface IPosGastosService
    {
        Task<PosGastosViewModel?> TryGetAsync(string? folioOperacion = null);
        Task<bool> UpdateAsync(string folioOperacion, decimal importe, string concepto, string observaciones, string usuario);
    }

    public interface IPosCortesService
    {
        Task<PosCorteViewModel?> TryGetAsync(DateTime? fecha = null);
        Task<bool> CloseAsync(string usuario, DateTime fecha);
    }

    public interface IPosComisionesService
    {
        Task<PosComisionesViewModel?> TryGetAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<List<PosComisionRowViewModel>> GetPagosReporteAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<int> RecalcularAsync(string? folioOperacion = null, string usuario = "WEB", DateTime? fecha = null);
        Task<bool> PayAsync(string folioOperacion, string usuario);
    }

    public interface IPosCatalogosService
    {
        Task<PosCatalogoViewModel?> TryGetTransportesAsync();
        Task<PosCatalogoViewModel?> TryGetGuiasAsync();
        Task<PosCatalogoViewModel?> TryGetTaxistasAsync(string? busqueda = null, int pagina = 1, int tamanoPagina = 50);
        Task<bool> SaveTransporteAsync(string clave, string nombre, decimal minimo, decimal maximo, decimal comision, decimal descEfectivo, decimal descTarjeta, decimal descAmex, string usuario);
        Task<bool> SaveGuiaAsync(string clave, string nombre, string telefono, decimal comision, string estatus, string usuario);
        Task<bool> SaveTaxistaAsync(string clave, string nombre, string telefono, string unidad, string placas, string estatus, string usuario);
    }

    public interface IPosGafetesService
    {
        Task<PosGafetesViewModel?> TryGetAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, bool soloSinFecha = false);
        Task<bool> InsertAsync(int matricula, string gafete, long? folioOperacion, string movimiento, string usuario);
        Task<bool> UpdateAsync(int? matriculaOriginal, string? gafeteOriginal, long? folioOperacionOriginal, int matricula, string gafete, long? folioOperacion, string movimiento, string usuario);
        Task<bool> MarcarRegresoAsync(string gafete, long? folioOperacion, string usuario);
        Task<(int Actualizados, int NoActualizados)> MarcarRegresoMasivoAsync(string gafetes, long? folioOperacion, string usuario);
        Task<PosGafeteRowViewModel?> TryFindAsync(string gafete);
    }

    public interface IPosUsuariosService
    {
        Task<PosUsuariosViewModel?> TryGetAsync(string? usuario = null);
        Task<IReadOnlyCollection<string>> GetPermissionsAsync(string usuario);
        Task<bool> SaveAsync(string usuario, string password, string rol, string estatus, IEnumerable<string> permisos, string usuarioActor);
    }

    public interface IPosRelacionesService
    {
        Task<PosRelacionesViewModel?> TryGetAsync(string? busqueda = null, DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<List<PosRelacionRowViewModel>> GetReporteDejadasAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, string? busqueda = null);
        Task<List<CuadreCamionesSummary>> GetCamionesResumenAsync(DateTime? fechaInicio, DateTime? fechaFin);
        Task<bool> SaveAsync(PosRelacionesViewModel model, string usuario);
        Task<PosDejadaTicketViewModel?> PayDejadaAsync(string? folioControl, string? folioApp, string? folioOperacion, string? folioPos, string usuario);
    }
}
