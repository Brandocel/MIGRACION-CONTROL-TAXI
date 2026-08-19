using System.Threading.Tasks;
using ControlTaxiWeb.Models.PosTriton;

namespace ControlTaxiWeb.Interfaces
{
    public interface IPosSqlMirrorService
    {
        Task<PosRegistroDiarioViewModel?> TryGetRegistroDiarioAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<PosPagosViewModel?> TryGetPagosAsync(string? folioOperacion = null);
        Task<PosGastosViewModel?> TryGetGastosAsync(string? folioOperacion = null);
        Task<PosCorteViewModel?> TryGetCorteAsync(DateTime? fecha = null);
        Task<PosComisionesViewModel?> TryGetComisionesAsync(string? folioOperacion = null, DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<List<PosComisionRowViewModel>> GetPagosComisionesReporteAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<PosVentasViewModel?> TryGetVentasAsync(string? productoBusqueda = null);
        Task<List<ControlTaxiWeb.Models.PosTriton.PosVentaDetalleViewModel>> TryGetRemisionProductosAsync(string folioRegistro);
        Task<PosVentaDetalleViewModel?> TryGetProductoVentaAsync(int productoId);
        Task<string?> CreateVentaAsync(PosVentasViewModel model);
        Task<bool> ValidateUserAsync(string usuario, string password);
        Task<bool> UpdateRegistroAsync(string folioOperacion, string staff, int pax, DateTime fechaTrabajo, string usuario);
        Task<string?> CreateRegistroAppAsync(PosRegistroAppViewModel model, string usuario);
        Task<List<PosRegistroAppTaxistaOption>> SearchRegistroAppTaxistasAsync(string? query, int limit = 20);
        Task<List<PosRegistroAppTarifaOption>> SearchRegistroAppTarifasAsync(string? query, int limit = 50);
        Task<List<string>> SearchRegistroAppHotelesAsync(string? query, int limit = 50);
        Task<bool> RegisterPagoAsync(string folioOperacion, decimal importe, string usuario);
        Task<bool> UpdateGastoAsync(string folioOperacion, decimal importe, string concepto, string observaciones, string usuario);
        Task<int> RecalcularComisionesAsync(string? folioOperacion = null, string usuario = "WEB", DateTime? fecha = null);
        Task<bool> PayComisionAsync(string folioOperacion, string usuario);
        Task<bool> CloseCorteAsync(string usuario, DateTime fecha);
        Task<bool> InsertGafeteAsync(int matricula, string gafete, long? folioOperacion, string movimiento, string usuario);
        Task<bool> UpdateGafeteAsync(int? matriculaOriginal, string? gafeteOriginal, long? folioOperacionOriginal, int matricula, string gafete, long? folioOperacion, string movimiento, string usuario);
        Task<bool> MarcarRegresoGafeteAsync(string gafete, long? folioOperacion, string usuario);
        Task<(int Actualizados, int NoActualizados)> MarcarRegresoGafetesAsync(string gafetes, long? folioOperacion, string usuario);
        Task<bool> SaveTransporteAsync(string clave, string nombre, decimal minimo, decimal maximo, decimal comision, decimal descEfectivo, decimal descTarjeta, decimal descAmex, string usuario);
        Task<bool> SaveGuiaAsync(string clave, string nombre, string telefono, decimal comision, string estatus, string usuario);
        Task<bool> SaveTaxistaAsync(string clave, string nombre, string telefono, string unidad, string placas, string estatus, string usuario);
        Task<PosCatalogoViewModel?> TryGetTransportesAsync();
        Task<PosCatalogoViewModel?> TryGetGuiasAsync();
        Task<PosCatalogoViewModel?> TryGetTaxistasAsync(string? busqueda = null, int pagina = 1, int tamanoPagina = 50);
        Task<PosGafetesViewModel?> TryGetGafetesAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, bool soloSinFecha = false);
        Task<ControlTaxiWeb.Models.PosTriton.PosGafeteRowViewModel?> TryFindGafeteAsync(string gafete);
        Task<PosUsuariosViewModel?> TryGetUsuariosAsync(string? usuario = null);
        Task<IReadOnlyCollection<string>> GetUserPermissionsAsync(string usuario);
        Task<bool> SaveUsuarioAsync(string usuario, string password, string rol, string estatus, IEnumerable<string> permisos, string usuarioActor);
        Task<PosRelacionesViewModel?> TryGetRelacionesAsync(string? busqueda = null, DateTime? fechaInicio = null, DateTime? fechaFin = null);
        Task<List<PosRelacionRowViewModel>> GetRelacionesReporteDejadasAsync(DateTime? fechaInicio = null, DateTime? fechaFin = null, string? busqueda = null);
        Task<List<CuadreCamionesSummary>> GetCamionesResumenAsync(DateTime? fechaInicio, DateTime? fechaFin);
        Task<bool> SaveRelacionAsync(PosRelacionesViewModel model, string usuario);
        Task<PosDejadaTicketViewModel?> PayDejadaAsync(string? folioControl, string? folioApp, string? folioOperacion, string? folioPos, string usuario);
    }
}
