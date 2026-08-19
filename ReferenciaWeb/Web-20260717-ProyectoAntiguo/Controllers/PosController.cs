using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ControlTaxiWeb.Interfaces;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ControlTaxiWeb.Controllers
{
    public class PosController : Controller
    {
        private const string AuthView = "~/Views/Pos/Auth/Login.cshtml";
        private const string DashboardView = "~/Views/Pos/Dashboard/Index.cshtml";
        private const string RegistroView = "~/Views/Pos/Registro/Index.cshtml";
        private const string RegistroAppView = "~/Views/Pos/RegistroApp/Index.cshtml";
        private const string VentasView = "~/Views/Pos/Ventas/Index.cshtml";
        private const string VentaRemisionDetalleView = "~/Views/Pos/Ventas/RemisionDetalle.cshtml";
        private const string CatalogosView = "~/Views/Pos/Catalogos/Index.cshtml";
        private const string GafetesView = "~/Views/Pos/Gafetes/Index.cshtml";
        private const string RelacionesView = "~/Views/Pos/Relaciones/Index.cshtml";
        private const string DejadaTicketView = "~/Views/Pos/Relaciones/DejadaTicket.cshtml";
        private const string GastosView = "~/Views/Pos/Gastos/Index.cshtml";
        private const string CortesView = "~/Views/Pos/Cortes/Index.cshtml";
        private const string ComisionesView = "~/Views/Pos/Comisiones/Index.cshtml";
        private const string UsuariosView = "~/Views/Pos/Usuarios/Index.cshtml";
        private const string ReportesView = "~/Views/Pos/Reportes/Index.cshtml";

        private readonly IPosAuthService _authService;
        private readonly IPosDashboardService _dashboardService;
        private readonly IPosRegistroService _registroService;
        private readonly IPosVentasService _ventasService;
        private readonly IPosPagosService _pagosService;
        private readonly IPosGastosService _gastosService;
        private readonly IPosCortesService _cortesService;
        private readonly IPosComisionesService _comisionesService;
        private readonly IPosCatalogosService _catalogosService;
        private readonly IPosGafetesService _gafetesService;
        private readonly IPosUsuariosService _usuariosService;
        private readonly IPosRelacionesService _relacionesService;
        private static readonly ConcurrentDictionary<string, DateTime> AutoComisionesRecalcMarks = new(StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyDictionary<string, string> ActionModules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(RegistroDiario)] = "RegistroDiario",
            [nameof(RegistroApp)] = "RegistroDiario",
            [nameof(BuscarRegistroAppTaxistas)] = "RegistroDiario",
            [nameof(BuscarRegistroAppTarifas)] = "RegistroDiario",
            [nameof(BuscarRegistroAppHoteles)] = "RegistroDiario",
            [nameof(GuardarRegistro)] = "RegistroDiario",
            [nameof(GuardarRegistroApp)] = "RegistroDiario",
            [nameof(ExportRegistroCsv)] = "RegistroDiario",
            [nameof(ExportRegistroExcel)] = "RegistroDiario",
            [nameof(ExportRegistroPdf)] = "RegistroDiario",
            [nameof(Ventas)] = "Ventas",
            [nameof(RemisionDetalle)] = "Ventas",
            [nameof(RemisionProductos)] = "Ventas",
            [nameof(GuardarVenta)] = "Ventas",
            [nameof(AgregarLineaVenta)] = "Ventas",
            [nameof(BorrarLineaVenta)] = "Ventas",
            [nameof(NuevaVenta)] = "Ventas",
            [nameof(Pagos)] = "Comisiones",
            [nameof(GuardarPago)] = "Comisiones",
            [nameof(ExportPagosCsv)] = "Comisiones",
            [nameof(ExportPagosPdf)] = "Comisiones",
            [nameof(Comisiones)] = "Comisiones",
            [nameof(CalcularComisiones)] = "Comisiones",
            [nameof(PagarComision)] = "Comisiones",
            [nameof(AbonarComision)] = "Comisiones",
            [nameof(PagarComisionesSeleccionadas)] = "Comisiones",
            [nameof(ExportComisionesCsv)] = "Comisiones",
            [nameof(ExportComisionesPdf)] = "Comisiones",
            [nameof(Transportes)] = "Transportes",
            [nameof(GuardarTransporte)] = "Transportes",
            [nameof(Guias)] = "Guias",
            [nameof(GuardarGuia)] = "Guias",
            [nameof(Taxistas)] = "Taxistas",
            [nameof(GuardarTaxista)] = "Taxistas",
            [nameof(Gafetes)] = "Gafetes",
            [nameof(GuardarGafete)] = "Gafetes",
            [nameof(ReiniciarGafetes)] = "Gafetes",
            [nameof(RegistrarRegresoGafete)] = "Gafetes",
            [nameof(ExportGafetesCsv)] = "Gafetes",
            [nameof(Relaciones)] = "Relaciones",
            [nameof(GuardarRelacion)] = "Relaciones",
            [nameof(PagarDejada)] = "Relaciones",
            [nameof(ReimprimirTicketDejada)] = "Relaciones",
            [nameof(DescargarTicketDejada)] = "Relaciones",
            [nameof(Gastos)] = "Gastos",
            [nameof(GuardarGasto)] = "Gastos",
            [nameof(ExportGastosCsv)] = "Gastos",
            [nameof(Cortes)] = "Cortes",
            [nameof(CalcularCorte)] = "Cortes",
            [nameof(CerrarCorte)] = "Cortes",
            [nameof(ExportCorteCsv)] = "Cortes",
            [nameof(ExportCortePdf)] = "Cortes",
            [nameof(Reportes)] = "Reportes",
            [nameof(ExportReporteDejadasExcel)] = "Reportes",
            [nameof(ExportReporteConcentradoExcel)] = "Reportes",
            [nameof(ExportReporteCuadreFinalExcel)] = "Reportes",
            [nameof(ExportReporteTaxiExcel)] = "Reportes",
            [nameof(ExportReportePagosComisionesExcel)] = "Reportes",
            [nameof(Usuarios)] = "Usuarios",
            [nameof(GuardarUsuario)] = "Usuarios"
        };

        public PosController(
            IPosAuthService authService,
            IPosDashboardService dashboardService,
            IPosRegistroService registroService,
            IPosVentasService ventasService,
            IPosPagosService pagosService,
            IPosGastosService gastosService,
            IPosCortesService cortesService,
            IPosComisionesService comisionesService,
            IPosCatalogosService catalogosService,
            IPosGafetesService gafetesService,
            IPosUsuariosService usuariosService,
            IPosRelacionesService relacionesService)
        {
            _authService = authService;
            _dashboardService = dashboardService;
            _registroService = registroService;
            _ventasService = ventasService;
            _pagosService = pagosService;
            _gastosService = gastosService;
            _cortesService = cortesService;
            _comisionesService = comisionesService;
            _catalogosService = catalogosService;
            _gafetesService = gafetesService;
            _usuariosService = usuariosService;
            _relacionesService = relacionesService;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var action = Convert.ToString(context.RouteData.Values["action"]) ?? string.Empty;
            if (!string.Equals(action, nameof(Login), StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(HttpContext.Session.GetString("PosUser")))
            {
                context.Result = RedirectToAction(nameof(Login));
                return;
            }

            if (ActionModules.TryGetValue(action, out var module) && !HasModulePermission(module))
            {
                TempData["PosNotice"] = $"No tienes permiso para entrar a {module}.";
                context.Result = RedirectToAction(nameof(Index));
                return;
            }

            try
            {
                await next();
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                TempData["PosError"] = BuildErrorDetail("Base de datos SQL", ex.Message, action, ex.Number.ToString());
                context.Result = RedirectToAction(nameof(PosError));
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                TempData["PosError"] = BuildErrorDetail("Conexion a internet / API", ex.Message, action, ex.StatusCode?.ToString() ?? "Sin codigo");
                context.Result = RedirectToAction(nameof(PosError));
            }
            catch (TaskCanceledException ex)
            {
                TempData["PosError"] = BuildErrorDetail("Tiempo de espera agotado", ex.Message, action, "TIMEOUT");
                context.Result = RedirectToAction(nameof(PosError));
            }
            catch (Exception ex)
            {
                TempData["PosError"] = BuildErrorDetail(ex.GetType().Name, ex.Message, action, ex.InnerException?.Message ?? "-");
                context.Result = RedirectToAction(nameof(PosError));
            }
        }

        public IActionResult Login()
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            return View(AuthView);
        }

        [HttpPost]
        public async Task<IActionResult> Login(string? usuario, string? password)
        {
            bool success;
            try
            {
                success = await _authService.ValidateUserAsync(usuario ?? string.Empty, password ?? string.Empty);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex)
            {
                TempData["PosError"] = BuildErrorDetail("Base de datos SQL al validar usuario", ex.Message, nameof(Login), ex.Number.ToString());
                return RedirectToAction(nameof(PosError));
            }
            catch (Exception ex)
            {
                TempData["PosError"] = BuildErrorDetail("Error al conectar con la base de datos", ex.Message, nameof(Login), ex.GetType().Name);
                return RedirectToAction(nameof(PosError));
            }

            if (!success)
            {
                TempData["PosNotice"] = "Usuario o contrasena incorrectos.";
                return RedirectToAction(nameof(Login));
            }

            HttpContext.Session.SetString("PosUser", usuario?.Trim() ?? "WEB");
            IReadOnlyCollection<string> permisos;
            try
            {
                permisos = await _usuariosService.GetPermissionsAsync(usuario?.Trim() ?? "WEB");
            }
            catch
            {
                permisos = PosModuloPermisoViewModel.Catalogo().Select(x => x.Clave).ToArray();
            }
            if (permisos.Count == 0)
                permisos = PosModuloPermisoViewModel.Catalogo().Select(x => x.Clave).ToArray();
            HttpContext.Session.SetString("PosPermissions", string.Join("|", ExpandPermissions(permisos)));
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }

        public IActionResult Error()
        {
            TempData["PosNotice"] = "Ocurrio un error inesperado. Revisa la conexion o intenta de nuevo.";
            return RedirectToAction(nameof(PosError));
        }

        public IActionResult PosError()
        {
            string? detail = TempData["PosError"] as string;

            if (string.IsNullOrEmpty(detail) && HttpContext.Request.Cookies.TryGetValue("PosLastError", out var cookie))
            {
                detail = Uri.UnescapeDataString(cookie);
                HttpContext.Response.Cookies.Delete("PosLastError");
            }

            detail ??= BuildErrorDetail("Error desconocido", "No se recibio detalle del error.", "-", "-");
            return View("~/Views/Pos/PosError.cshtml", detail);
        }

        private static string BuildErrorDetail(string tipo, string mensaje, string accion, string codigoExtra) =>
            $"{tipo}||{mensaje}||{accion}||{codigoExtra}||{DateTime.Now:dd/MM/yyyy HH:mm:ss}";

        public IActionResult Index()
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            ViewBag.Permisos = GetPermissions();
            return View(DashboardView, _dashboardService.BuildDashboard());
        }

        public IActionResult Menu() => RedirectToAction(nameof(Index));

        public async Task<IActionResult> RegistroDiario(string? folioOperacion, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (!fechaInicio.HasValue && !fechaFin.HasValue)
            {
                fechaInicio = DateTime.Today;
                fechaFin = DateTime.Today;
            }

            ViewBag.FolioOperacion = folioOperacion;
            ViewBag.FechaInicio = fechaInicio?.ToString("yyyy-MM-dd");
            ViewBag.FechaFin = fechaFin?.ToString("yyyy-MM-dd");
            ViewBag.PosNotice = TempData["PosNotice"];
            var sqlModel = await _registroService.TryGetAsync(folioOperacion, fechaInicio, fechaFin);
            if (sqlModel != null && sqlModel.Operaciones.Count > 0)
                return View(RegistroView, sqlModel);

            var model = new PosRegistroDiarioViewModel
            {
                FolioOperacion = folioOperacion ?? string.Empty,
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                FechaTrabajo = DateTime.Today
            };

            return View(RegistroView, model);
        }

        public async Task<IActionResult> RegistroApp()
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var taxistas = await _registroService.SearchTaxistasAsync(null, 80);
            var tarifas = await _registroService.SearchTarifasAsync(null, 120);
            var hoteles = await _registroService.SearchHotelesAsync(null, 120);
            return View(RegistroAppView, new PosRegistroAppViewModel
            {
                FechaOperacion = DateTime.Now,
                Sitio = "TIENDA PLAZA 28",
                Taxistas = taxistas,
                Tarifas = tarifas,
                Hoteles = hoteles,
                Recientes = new List<PosOperacionRowViewModel>()
            });
        }

        public async Task<IActionResult> BuscarRegistroAppTaxistas(string? q) =>
            Json(await _registroService.SearchTaxistasAsync(q, 30));

        public async Task<IActionResult> BuscarRegistroAppTarifas(string? q) =>
            Json(await _registroService.SearchTarifasAsync(q, 80));

        public async Task<IActionResult> BuscarRegistroAppHoteles(string? q) =>
            Json(await _registroService.SearchHotelesAsync(q, 80));

        public async Task<IActionResult> Ventas(string? productoBusqueda)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var model = await _ventasService.TryGetAsync(productoBusqueda)
                ?? new PosVentasViewModel { ProductoBusqueda = productoBusqueda ?? string.Empty };
            var cart = GetVentaCart();
            var cartOwner = HttpContext.Session.GetString("PosVentaCartOwner") ?? string.Empty;
            var currentOwner = NormalizeVentaCartOwner(productoBusqueda ?? model.ProductoBusqueda);
            if (cart.Count > 0 && string.Equals(cartOwner, currentOwner, StringComparison.OrdinalIgnoreCase))
            {
                model.Items = cart;
            }
            else if (cart.Count > 0)
            {
                ClearVentaCart();
            }
            RecalculateVenta(model);

            return View(VentasView, model);
        }

        public async Task<IActionResult> RemisionDetalle(string folio, string? productoBusqueda = null)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var model = await _ventasService.TryGetAsync(folio)
                ?? new PosVentasViewModel { ProductoBusqueda = folio };
            ViewBag.RegresoVenta = productoBusqueda ?? folio;
            ViewBag.RemisionFolio = folio;
            return View(VentaRemisionDetalleView, model);
        }

        public async Task<IActionResult> RemisionProductos(string folioRegistro)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var lines = await _ventasService.TryGetRemisionProductosAsync(folioRegistro);
            return View("~/Views/Pos/Ventas/RemisionProductos.cshtml", lines);
        }

        public IActionResult Pagos(string? folioOperacion) =>
            RedirectToAction(nameof(Comisiones), new { folioOperacion });

        public async Task<IActionResult> Transportes(string? busqueda = null)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var sqlModel = await _catalogosService.TryGetTransportesAsync();
            if (sqlModel != null)
            {
                sqlModel.Busqueda = busqueda ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(busqueda))
                {
                    var filtro = busqueda.Trim();
                    sqlModel.Filas = sqlModel.Filas
                        .Where(row => row.Any(col => !string.IsNullOrWhiteSpace(col)
                            && col.Contains(filtro, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                }
            }

            if (sqlModel != null && sqlModel.Filas.Count > 0)
                return View(CatalogosView, sqlModel);

            return View(CatalogosView, new PosCatalogoViewModel
            {
                Titulo = "TIPOS DE TRANSPORTE",
                Descripcion = "Catalogo de transportes del POS.",
                Columnas = new List<string> { "CODIGO", "TRANSPORTE" },
                Busqueda = busqueda ?? string.Empty
            });
        }

        public async Task<IActionResult> Guias()
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var sqlModel = await _catalogosService.TryGetGuiasAsync();
            if (sqlModel != null && sqlModel.Filas.Count > 0)
                return View(CatalogosView, sqlModel);

            return View(CatalogosView, new PosCatalogoViewModel
            {
                Titulo = "GUIAS",
                Descripcion = "Catalogo de guias del POS.",
                Columnas = new List<string> { "CLAVE", "NOMBRE", "EMPRESA", "TELEFONO" }
            });
        }

        public async Task<IActionResult> Taxistas(string? busqueda = null, int pagina = 1)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var sqlModel = await _catalogosService.TryGetTaxistasAsync(busqueda, pagina, 50);
            if (sqlModel != null && sqlModel.Filas.Count > 0)
                return View(CatalogosView, sqlModel);

            return View(CatalogosView, new PosCatalogoViewModel
            {
                Titulo = "TAXISTAS",
                Descripcion = "Catalogo de taxistas del POS.",
                Columnas = new List<string> { "CLAVE", "NOMBRE", "EMPRESA", "TARJETA", "BANCO", "TELEFONO", "ACTIVO" },
                Busqueda = busqueda ?? string.Empty,
                Pagina = Math.Max(pagina, 1)
            });
        }

        public async Task<IActionResult> Gafetes(DateTime? fechaInicio, DateTime? fechaFin, string? selectedGafete, long? selectedFolioOperacion, string? gafeteBusqueda, string? folioOperacionBusqueda, int? matriculaBusqueda, bool soloSinFecha = false, int pagina = 1)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var updatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var updatedRaw = TempData["PosUpdatedGafetes"]?.ToString();
            if (!string.IsNullOrWhiteSpace(updatedRaw))
            {
                foreach (var item in updatedRaw.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    updatedKeys.Add(item);
                }
            }
            ViewBag.UpdatedGafetes = updatedKeys;
            var sqlModel = await _gafetesService.TryGetAsync(fechaInicio, fechaFin, soloSinFecha);
            if (sqlModel != null && sqlModel.Filas.Count > 0)
            {
                static string NormalizeText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
                static string NormalizeFolioText(string? value)
                {
                    var text = NormalizeText(value);
                    if (string.IsNullOrWhiteSpace(text))
                        return string.Empty;
                    return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                        ? number.ToString(CultureInfo.InvariantCulture)
                        : text;
                }

                var filtroGafete = NormalizeText(gafeteBusqueda);
                var filtroFolio = NormalizeText(folioOperacionBusqueda);
                var filtroFolioNormalizado = NormalizeFolioText(folioOperacionBusqueda);
                var filtroMatricula = matriculaBusqueda;

                if (!string.IsNullOrWhiteSpace(filtroGafete) || !string.IsNullOrWhiteSpace(filtroFolio) || filtroMatricula.HasValue)
                {
                    sqlModel.Filas = sqlModel.Filas
                        .Where(row =>
                        {
                            var gafete = row.Count > 0 ? NormalizeText(row[0]) : string.Empty;
                            var staff = row.Count > 1 ? NormalizeText(row[1]) : string.Empty;
                            var folio = row.Count > 2 ? NormalizeText(row[2]) : string.Empty;
                            var folioNormalizado = NormalizeFolioText(folio);
                            var matchGafete = string.IsNullOrWhiteSpace(filtroGafete)
                                || string.Equals(gafete, filtroGafete, StringComparison.OrdinalIgnoreCase);
                            var matchFolio = string.IsNullOrWhiteSpace(filtroFolio)
                                || string.Equals(folioNormalizado, filtroFolioNormalizado, StringComparison.OrdinalIgnoreCase)
                                || string.Equals(folio, filtroFolio, StringComparison.OrdinalIgnoreCase);
                            var matchMatricula = !filtroMatricula.HasValue
                                || string.Equals(staff, filtroMatricula.Value.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
                            return matchGafete && matchFolio && matchMatricula;
                        })
                        .ToList();
                }

                sqlModel.SelectedGafete = selectedGafete ?? string.Empty;
                sqlModel.SelectedFolioOperacion = selectedFolioOperacion;
                sqlModel.Gafete = FirstText(gafeteBusqueda, selectedGafete);
                sqlModel.FolioOperacion = !string.IsNullOrWhiteSpace(folioOperacionBusqueda)
                    ? long.TryParse(folioOperacionBusqueda, out var folioBusquedaNumeroSql) ? folioBusquedaNumeroSql : selectedFolioOperacion
                    : selectedFolioOperacion;
                sqlModel.Matricula = matriculaBusqueda ?? sqlModel.Matricula;
                sqlModel.SoloSinFecha = soloSinFecha;
                ApplyGafetesPagination(sqlModel, pagina);
                return View(GafetesView, sqlModel);
            }

            var model = new PosGafetesViewModel
            {
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                SelectedGafete = selectedGafete ?? string.Empty,
                SelectedFolioOperacion = selectedFolioOperacion,
                Gafete = FirstText(gafeteBusqueda, selectedGafete),
                FolioOperacion = !string.IsNullOrWhiteSpace(folioOperacionBusqueda)
                    ? long.TryParse(folioOperacionBusqueda, out var folioBusquedaNumeroModel) ? folioBusquedaNumeroModel : selectedFolioOperacion
                    : selectedFolioOperacion,
                Matricula = matriculaBusqueda ?? 0,
                SoloSinFecha = soloSinFecha,
                Pagina = Math.Max(pagina, 1)
            };

            return View(GafetesView, model);
        }

        public async Task<IActionResult> FindGafete(string gafete)
        {
            if (string.IsNullOrWhiteSpace(gafete))
                return Json(null);

            try
            {
                var row = await _gafetesService.TryFindAsync(gafete);
                return Json(row);
            }
            catch
            {
                Response.StatusCode = 500;
                return Json(new { error = "No se pudo consultar el gafete en el servidor." });
            }
        }

        public async Task<IActionResult> Relaciones(string? busqueda, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var resetFilters = string.Equals(TempData["PosResetFilters"]?.ToString(), "1", StringComparison.OrdinalIgnoreCase);
            if (!resetFilters && !fechaInicio.HasValue && !fechaFin.HasValue)
            {
                fechaInicio = DateTime.Today;
                fechaFin = DateTime.Today;
            }

            ViewBag.FechaInicio = fechaInicio?.ToString("yyyy-MM-dd");
            ViewBag.FechaFin = fechaFin?.ToString("yyyy-MM-dd");
            ViewBag.PosNotice = TempData["PosNotice"];
            var model = await _relacionesService.TryGetAsync(resetFilters ? null : busqueda, resetFilters ? null : fechaInicio, resetFilters ? null : fechaFin)
                ?? new PosRelacionesViewModel { Busqueda = string.Empty, FechaInicio = resetFilters ? null : fechaInicio, FechaFin = resetFilters ? null : fechaFin };
            if (resetFilters)
            {
                model.Busqueda = string.Empty;
                model.FechaInicio = null;
                model.FechaFin = null;
            }
            return View(RelacionesView, model);
        }

        public async Task<IActionResult> Gastos(string? folioOperacion)
        {
            ViewBag.FolioOperacion = folioOperacion;
            ViewBag.PosNotice = TempData["PosNotice"];
            var sqlModel = await _gastosService.TryGetAsync(folioOperacion);
            if (sqlModel != null && sqlModel.Filas.Count > 0)
                return View(GastosView, sqlModel);

            var model = new PosGastosViewModel
            {
                FolioOperacion = folioOperacion ?? string.Empty
            };

            return View(GastosView, model);
        }

        public async Task<IActionResult> Cortes(DateTime? fecha)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var sqlModel = await _cortesService.TryGetAsync(fecha);
            if (sqlModel != null)
                return View(CortesView, sqlModel);

            var model = new PosCorteViewModel();

            return View(CortesView, model);
        }

        public async Task<IActionResult> Comisiones(string? folioOperacion, DateTime? fechaInicio, DateTime? fechaFin, int pagina = 1)
        {
            if (!string.IsNullOrWhiteSpace(folioOperacion))
            {
                fechaInicio = null;
                fechaFin = null;
            }
            else if (!fechaInicio.HasValue && !fechaFin.HasValue)
            {
                fechaInicio = DateTime.Today;
                fechaFin = DateTime.Today;
            }

            ViewBag.FolioOperacion = folioOperacion;
            ViewBag.FechaInicio = fechaInicio?.ToString("yyyy-MM-dd");
            ViewBag.FechaFin = fechaFin?.ToString("yyyy-MM-dd");
            ViewBag.PosNotice = TempData["PosNotice"];
            await AutoRecalcularComisionesAsync(folioOperacion, fechaInicio, fechaFin, pagina);
            var model = await _comisionesService.TryGetAsync(folioOperacion, fechaInicio, fechaFin)
                ?? new PosComisionesViewModel { FolioOperacion = folioOperacion ?? string.Empty, FechaInicio = fechaInicio, FechaFin = fechaFin };
            ApplyComisionesPagination(model, pagina);

            return View(ComisionesView, model);
        }

        private async Task AutoRecalcularComisionesAsync(string? folioOperacion, DateTime? fechaInicio, DateTime? fechaFin, int pagina)
        {
            if (pagina > 1)
                return;

            var usuario = CurrentUser();
            var key = string.Join("|", new[]
            {
                usuario,
                folioOperacion?.Trim().ToUpperInvariant() ?? string.Empty,
                fechaInicio?.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
                fechaFin?.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty
            });
            var now = DateTime.UtcNow;
            if (AutoComisionesRecalcMarks.TryGetValue(key, out var last)
                && now - last < TimeSpan.FromMinutes(5))
            {
                return;
            }

            AutoComisionesRecalcMarks[key] = now;

            if (string.IsNullOrWhiteSpace(folioOperacion) && (fechaInicio.HasValue || fechaFin.HasValue))
            {
                var inicio = (fechaInicio ?? fechaFin)!.Value.Date;
                var fin = (fechaFin ?? fechaInicio)!.Value.Date;
                if (fin < inicio)
                    (inicio, fin) = (fin, inicio);
                if (inicio != fin)
                    return;

                for (var fecha = inicio; fecha <= fin; fecha = fecha.AddDays(1))
                    await _comisionesService.RecalcularAsync(null, usuario, fecha);
                return;
            }

            await _comisionesService.RecalcularAsync(folioOperacion, usuario);
        }

        public async Task<IActionResult> Usuarios(string? usuario)
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var model = await _usuariosService.TryGetAsync(usuario) ?? new PosUsuariosViewModel();
            return View(UsuariosView, model);
        }

        public async Task<IActionResult> Reportes(DateTime? fechaInicio = null, DateTime? fechaFin = null, string tipoReporte = "operaciones")
        {
            ViewBag.PosNotice = TempData["PosNotice"];
            var esPagosComisiones = string.Equals(tipoReporte, "pagos-comisiones", StringComparison.OrdinalIgnoreCase);
            fechaInicio ??= DateTime.Today;
            fechaFin ??= fechaInicio;
            var reporteDejadas = !esPagosComisiones
                ? await _relacionesService.GetReporteDejadasAsync(fechaInicio, fechaFin)
                : new List<PosRelacionRowViewModel>();
            var comisiones = !esPagosComisiones
                ? MapReporteDejadasToComisiones(reporteDejadas)
                : new List<PosComisionRowViewModel>();
            var pagosComisiones = esPagosComisiones
                ? await _comisionesService.GetPagosReporteAsync(fechaInicio, fechaFin)
                : new List<PosComisionRowViewModel>();
            var permisos = GetPermissions();
            var operacionesDelDia = !esPagosComisiones
                ? MapReporteDejadasToOperaciones(reporteDejadas)
                : new List<PosOperacionRowViewModel>();
            var fechasComision = comisiones
                .Select(x => TryParseReportDate(x.Fecha));
            var fechaReporte = fechasComision
                .Where(x => x.HasValue)
                .Select(x => x!.Value.Date)
                .DefaultIfEmpty(DateTime.Today)
                .Max();
            var model = new PosReportesViewModel
            {
                FechaReporte = fechaReporte,
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                TipoReporte = esPagosComisiones ? "pagos-comisiones" : "operaciones",
                Operaciones = operacionesDelDia,
                Comisiones = comisiones,
                PagosComisiones = pagosComisiones,
                TotalDejadas = reporteDejadas.Sum(x => x.Dejada),
                Reportes = new List<PosModuleCardViewModel>
                {
                    new() { Title = "MOVIMIENTOS", Description = "Registro diario", Action = nameof(RegistroDiario), Icon = "bi-graph-up-arrow", AccentClass = "accent-blue" },
                    new() { Title = "COMISIONES", Description = "Control de comisiones y pagos", Action = nameof(Comisiones), Icon = "bi-percent", AccentClass = "accent-orange" },
                    new() { Title = "CIERRES", Description = "Corte del dia", Action = nameof(Cortes), Icon = "bi-safe2", AccentClass = "accent-red" }
                }
                .Where(x => ActionModules.TryGetValue(x.Action, out var module) && permisos.Contains(module))
                .ToList()
            };

            return View(ReportesView, model);
        }

        private static List<PosComisionRowViewModel> MapReporteDejadasToComisiones(IEnumerable<PosRelacionRowViewModel> rows)
        {
            return rows.Select(row => new PosComisionRowViewModel
            {
                Folio = FirstText(row.FolioControl, row.FolioApp, row.FolioOperacion),
                Ticket = FirstText(row.FolioPos, row.TicketPagoDejada),
                Fecha = row.Fecha,
                Beneficiario = FirstText(row.TaxistaNombre, row.Vendedor),
                Staff = FirstText(row.TaxistaNombre, row.Vendedor),
                Unidad = row.Unidad,
                NumeroUnidad = row.Unidad,
                Hotel = FirstText(row.Hotel, row.Sitio, row.Origen),
                Vendedor = row.Vendedor,
                Pax = row.Pax,
                Transporte = row.TransporteTipo,
                Base = row.Dejada,
                Venta = row.Venta,
                Importe = row.Comision,
                Pago = row.Pago,
                FechaPago = row.FechaPagoDejada,
                Estatus = row.Estatus,
                DeduccionDejada = row.Dejada
            }).ToList();
        }

        private static List<PosOperacionRowViewModel> MapReporteDejadasToOperaciones(IEnumerable<PosRelacionRowViewModel> rows)
        {
            return rows.Select(row => new PosOperacionRowViewModel
            {
                FolioOperacion = FirstText(row.FolioOperacion, row.FolioControl, row.FolioApp),
                FolioControl = row.FolioControl,
                Fuente = row.Fuente,
                UsuarioOrigen = row.UsuarioOrigen,
                Ticket = FirstText(row.FolioPos, row.TicketPagoDejada),
                CantidadTickets = 1,
                Hotel = FirstText(row.Hotel, row.Sitio, row.Origen),
                LlegadaSucursal = FirstText(row.Sitio, row.Origen, row.Hotel),
                Gafete = row.Gafete,
                Origen = row.Origen,
                Sitio = row.Sitio,
                Destino = row.Destino,
                Unidad = row.Unidad,
                Placas = row.Placas,
                Telefono = row.Telefono,
                Nacionalidad = row.Nacionalidad,
                Hora = row.Fecha,
                Pax = row.Pax,
                Vendedor = FirstText(row.TaxistaNombre, row.Vendedor),
                TipoOperacion = row.TransporteTipo,
                Total = row.Dejada,
                Efectivo = row.Dejada,
                Tarjeta = 0m
            }).ToList();
        }

        [HttpPost]
        public async Task<IActionResult> GuardarTransporte(string? clave, string? nombre, decimal minimo, decimal maximo, decimal comision, decimal descEfectivo, decimal descTarjeta, decimal descAmex)
        {
            var success = await _catalogosService.SaveTransporteAsync(clave ?? string.Empty, nombre ?? string.Empty, minimo, maximo, comision, descEfectivo, descTarjeta, descAmex, CurrentUser());
            TempData["PosNotice"] = success
                ? $"Transporte {clave} guardado."
                : "No se pudo guardar transporte. Revisa clave, nombre y minimo/maximo.";
            return RedirectToAction(nameof(Transportes));
        }

        [HttpPost]
        public async Task<IActionResult> GuardarGuia(string? clave, string? nombre, string? telefono, decimal comision, string? estatus)
        {
            var success = await _catalogosService.SaveGuiaAsync(clave ?? string.Empty, nombre ?? string.Empty, telefono ?? string.Empty, comision, estatus ?? "Activo", CurrentUser());
            TempData["PosNotice"] = success
                ? $"Guia {clave} guardada."
                : "No se pudo guardar guia. Revisa clave y nombre.";
            return RedirectToAction(nameof(Guias));
        }

        [HttpPost]
        public async Task<IActionResult> GuardarTaxista(string? clave, string? nombre, string? telefono, string? unidad, string? placas, string? estatus)
        {
            var success = await _catalogosService.SaveTaxistaAsync(clave ?? string.Empty, nombre ?? string.Empty, telefono ?? string.Empty, unidad ?? string.Empty, placas ?? string.Empty, estatus ?? "Activo", CurrentUser());
            TempData["PosNotice"] = success
                ? $"Taxista {clave} guardado."
                : "No se pudo guardar taxista. Revisa clave y nombre.";
            return RedirectToAction(nameof(Taxistas));
        }

        [HttpPost]
        public async Task<IActionResult> GuardarUsuario(string? usuario, string? password, string? rol, string? estatus, string[]? permisos)
        {
            var success = await _usuariosService.SaveAsync(usuario ?? string.Empty, password ?? string.Empty, rol ?? "Cajero", estatus ?? "Activo", permisos ?? Array.Empty<string>(), CurrentUser());
            var currentUser = HttpContext.Session.GetString("PosUser");
            if (success && string.Equals(currentUser, usuario?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var permisosActualizados = await _usuariosService.GetPermissionsAsync(currentUser ?? string.Empty);
                HttpContext.Session.SetString("PosPermissions", string.Join("|", ExpandPermissions(permisosActualizados)));
            }

            TempData["PosNotice"] = success
                ? $"Usuario {usuario} guardado."
                : "No se pudo guardar usuario. Revisa usuario y contrasena.";
            return RedirectToAction(nameof(Usuarios), new { usuario });
        }

        [HttpPost]
        public async Task<IActionResult> GuardarRelacion(PosRelacionesViewModel model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.FolioControl)
                    && string.IsNullOrWhiteSpace(model.FolioApp)
                    && string.IsNullOrWhiteSpace(model.FolioOperacion)
                    && string.IsNullOrWhiteSpace(model.FolioPos)
                    && model.TaxistaId <= 0)
                {
                    TempData["PosResetFilters"] = "1";
                    return RedirectToAction(nameof(Relaciones));
                }

                var success = await _relacionesService.SaveAsync(model, CurrentUser());
                TempData["PosNotice"] = success
                    ? $"Relacion guardada para folio app {model.FolioApp}."
                    : "No se pudo guardar la relacion. Revisa folio app, folio operacion y taxista.";
            }
            catch (Exception ex)
            {
                TempData["PosNotice"] = $"Error: {ex.Message} | " +
                    $"FolioApp={model.FolioApp}({model.FolioApp?.Length}c) " +
                    $"FolioControl={model.FolioControl}({model.FolioControl?.Length}c) " +
                    $"FolioPos={model.FolioPos?.Length}c " +
                    $"Gafete='{model.Gafete}'({model.Gafete?.Length}c) " +
                    $"TaxistaNombre={model.TaxistaNombre?.Length}c " +
                    $"Transporte='{model.TransporteTipo}'({model.TransporteTipo?.Length}c) " +
                    $"Nac={model.Nacionalidad?.Length}c";
            }

            var busquedaRedireccion = !string.IsNullOrWhiteSpace(model.Busqueda)
                ? model.Busqueda
                : FirstText(model.FolioApp, model.FolioControl, model.FolioOperacion, model.FolioPos);
            return RedirectToAction(nameof(Relaciones), new { busqueda = busquedaRedireccion, fechaInicio = model.FechaInicio, fechaFin = model.FechaFin });
        }

        [HttpPost]
        public async Task<IActionResult> PagarDejada(
            string? folioControl,
            string? folioApp,
            string? folioOperacion,
            string? folioPos,
            string? taxista,
            string? vendedor,
            string? gafete,
            string? nacionalidad,
            string? transporte,
            string? hotel,
            string? unidad,
            string? placas,
            string? destino,
            string? telefono,
            int? pax,
            decimal? importe,
            string? fechaViaje,
            string? busqueda,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            var folioSolicitado = !string.IsNullOrWhiteSpace(folioControl)
                ? folioControl
                : !string.IsNullOrWhiteSpace(folioApp)
                    ? folioApp
                    : !string.IsNullOrWhiteSpace(folioOperacion)
                        ? folioOperacion
                        : folioPos;

            if (string.IsNullOrWhiteSpace(folioSolicitado))
            {
                TempData["PosNotice"] = "Selecciona una dejada para pagar.";
                return RedirectToAction(nameof(Relaciones), new { busqueda, fechaInicio, fechaFin });
            }

            var ticket = await _relacionesService.PayDejadaAsync(folioControl, folioApp, folioOperacion, folioPos, CurrentUser());
            if (ticket == null)
            {
                TempData["PosNotice"] = $"No se pudo pagar la dejada {folioSolicitado}. Revisa importe o estatus.";
                return RedirectToAction(nameof(Relaciones), new { busqueda = folioSolicitado, fechaInicio, fechaFin });
            }

            var folioPagado = folioSolicitado;
            ticket.Taxista = FirstText(taxista, ticket.Taxista);
            ticket.Vendedor = FirstText(vendedor, ticket.Vendedor);
            ticket.Gafete = FirstText(gafete, ticket.Gafete);
            ticket.Nacionalidad = FirstText(nacionalidad, ticket.Nacionalidad);
            ticket.Transporte = FirstText(transporte, ticket.Transporte);
            ticket.Hotel = FirstText(hotel, ticket.Hotel);
            ticket.Unidad = FirstText(unidad, ticket.Unidad);
            ticket.Placas = FirstText(placas, ticket.Placas);
            ticket.Destino = FirstText(destino, ticket.Destino);
            ticket.Telefono = FirstText(telefono, ticket.Telefono);
            ticket.FechaViaje = FirstText(fechaViaje, ticket.FechaViaje);
            if (pax.HasValue && pax.Value > 0)
                ticket.Pax = pax.Value;
            if (importe.HasValue && importe.Value > 0)
                ticket.Importe = importe.Value;
            ticket.Busqueda = busqueda ?? folioPagado;
            ticket.FechaInicio = fechaInicio;
            ticket.FechaFin = fechaFin;
            ticket.TicketTexto = BuildDejadaReceiptContent(ticket);
            ViewBag.AutoPrint = true;
            ViewBag.RefreshParentUrl = Url.Action(nameof(Relaciones), new
            {
                busqueda = string.IsNullOrWhiteSpace(ticket.Busqueda) ? folioPagado : ticket.Busqueda,
                fechaInicio,
                fechaFin
            });
            return View(DejadaTicketView, ticket);
        }

        [HttpPost]
        public IActionResult ReimprimirTicketDejada(PosDejadaTicketViewModel model)
        {
            model.TicketTexto = BuildDejadaReceiptContent(model);
            ViewBag.AutoPrint = true;
            return View(DejadaTicketView, model);
        }

        [HttpPost]
        public IActionResult DescargarTicketDejada(PosDejadaTicketViewModel model) =>
            BuildDejadaReceiptTicket(model);

        private static FileContentResult BuildDejadaReceiptTicket(PosDejadaTicketViewModel ticket)
        {
            var bytes = Encoding.UTF8.GetBytes(BuildDejadaReceiptContent(ticket));
            var folio = string.Join("_", new[]
            {
                ticket.Ticket,
                ticket.FolioControl,
                ticket.FolioApp
            }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));

            return new FileContentResult(bytes, "text/plain; charset=utf-8")
            {
                FileDownloadName = $"ticket_dejada_{folio}_{DateTime.Now:yyyyMMddHHmmss}.txt"
            };
        }

        private static string BuildDejadaReceiptContent(PosDejadaTicketViewModel ticket)
        {
            const int width = 42;
            static string Line(char value = '-') => new(value, width);
            static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
            static string First(params string?[] values) =>
                values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "-";
            static string Center(string value)
            {
                value = Clean(value);
                if (value.Length >= width) return value[..width];
                var left = (width - value.Length) / 2;
                return new string(' ', left) + value;
            }

            static string Pair(string label, string value)
            {
                label = Clean(label).ToUpperInvariant();
                value = Clean(value);
                var prefix = $"{label}: ";
                var maxValue = Math.Max(1, width - prefix.Length);
                return prefix + (value.Length > maxValue ? value[..maxValue] : value);
            }

            var folio = Clean(First(ticket.FolioOperacion, ticket.FolioControl, ticket.FolioApp, ticket.FolioPos));
            var folioApp = Clean(ticket.FolioApp);
            var operacion = Clean(ticket.FolioOperacion);
            return string.Join(Environment.NewLine, new[]
            {
                Center("CONTROL TAXI"),
                Center("PAGO DE DEJADA"),
                Line(),
                Pair("Ticket", ticket.Ticket),
                Pair("Folio", folio),
                folioApp != folio ? Pair("Folio app", ticket.FolioApp) : string.Empty,
                operacion != folio && operacion != folioApp ? Pair("Operacion", ticket.FolioOperacion) : string.Empty,
                Pair("Fecha viaje", ticket.FechaViaje),
                Pair("Fecha pago", ticket.FechaPago),
                Line(),
                Pair("Taxista", ticket.Taxista),
                Pair("Vendedor", ticket.Vendedor),
                Pair("Gafete", ticket.Gafete),
                Pair("Unidad", ticket.Unidad),
                Pair("Placas", ticket.Placas),
                Pair("Telefono", ticket.Telefono),
                Pair("Nacionalidad", ticket.Nacionalidad),
                Pair("Transporte", ticket.Transporte),
                Pair("Hotel", ticket.Hotel),
                Pair("Destino", ticket.Destino),
                Pair("Pax", ticket.Pax.ToString()),
                Line(),
                Pair("Importe", ticket.Importe.ToString("C2")),
                Pair("Usuario", ticket.Usuario),
                Pair("Estatus", ticket.Estatus),
                Line(),
                Center("CONSERVE ESTE COMPROBANTE"),
                "________________________",
                Center("FIRMA DEL TAXISTA")
            }.Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        [HttpPost]
        public async Task<IActionResult> GuardarVenta(PosVentasViewModel model)
        {
            model.Usuario = HttpContext.Session.GetString("PosUser") ?? model.Usuario;
            model.Items = GetVentaCart();
            var folio = await _ventasService.CreateAsync(model);
            TempData["PosNotice"] = !string.IsNullOrWhiteSpace(folio)
                ? $"Venta guardada con folio {folio}."
                : "No se pudo guardar la venta: revisa producto, vendedor e importes.";
            if (!string.IsNullOrWhiteSpace(folio))
                ClearVentaCart();
            return RedirectToAction(nameof(Ventas), new { productoBusqueda = model.ProductoBusqueda });
        }

        [HttpPost]
        public async Task<IActionResult> AgregarLineaVenta(int productoId, int cantidad = 1, string? productoBusqueda = null)
        {
            var item = await _ventasService.TryGetProductAsync(productoId);
            if (item == null)
            {
                TempData["PosNotice"] = "No se encontro el producto para agregar.";
                return RedirectToAction(nameof(Ventas), new { productoBusqueda });
            }

            var cart = GetVentaCart();
            var existing = cart.FirstOrDefault(x => x.ProductoId == productoId);
            var units = Math.Max(cantidad, 1);
            if (existing == null)
            {
                item.Cantidad = units;
                item.Iva *= units;
                item.Precio *= units;
                item.Importe *= units;
                cart.Add(item);
            }
            else
            {
                var unitPrice = existing.Precio / Math.Max(existing.Cantidad, 1);
                var unitIva = existing.Iva / Math.Max(existing.Cantidad, 1);
                existing.Cantidad += units;
                existing.Precio = unitPrice * existing.Cantidad;
                existing.Iva = unitIva * existing.Cantidad;
                existing.Importe = existing.Precio + existing.Iva;
            }

            SaveVentaCart(cart, productoBusqueda);
            TempData["PosNotice"] = "Producto agregado al ticket.";
            return RedirectToAction(nameof(Ventas), new { productoBusqueda });
        }

        [HttpPost]
        public IActionResult BorrarLineaVenta(int productoId)
        {
            var cart = GetVentaCart();
            var removed = cart.RemoveAll(x => x.ProductoId == productoId) > 0;
            SaveVentaCart(cart, HttpContext.Session.GetString("PosVentaCartOwner"));
            TempData["PosNotice"] = removed ? "Linea eliminada del ticket." : "No habia linea para eliminar.";
            return RedirectToAction(nameof(Ventas));
        }

        [HttpPost]
        public IActionResult NuevaVenta()
        {
            ClearVentaCart();
            TempData["PosNotice"] = "Nuevo ticket listo.";
            return RedirectToAction(nameof(Ventas));
        }

        [HttpPost]
        public async Task<IActionResult> GuardarRegistro(string? folioOperacion, string? staff, int pax, DateTime? fechaTrabajo)
        {
            var success = !string.IsNullOrWhiteSpace(folioOperacion)
                && await _registroService.UpdateAsync(folioOperacion, staff ?? string.Empty, pax, fechaTrabajo ?? DateTime.Today, CurrentUser());
            TempData["PosNotice"] = success
                ? $"Registro actualizado para el folio {folioOperacion}."
                : $"No se pudo actualizar el registro del folio {folioOperacion ?? "sin filtro"}.";
            return RedirectToAction(nameof(RegistroDiario), new { folioOperacion });
        }

        [HttpPost]
        public async Task<IActionResult> GuardarRegistroApp(PosRegistroAppViewModel model)
        {
            if (model.FechaOperacion == default)
                model.FechaOperacion = DateTime.Now;

            if (model.Pax <= 0)
                model.Pax = 1;

            if (IsSuspiciousMajesticAmount(model.TipoOperacion, model.Total))
            {
                TempData["PosNotice"] = "No se guardo el registro: MAJESTIC no puede quedar con monto 0 o 1. Captura el importe real antes de guardar.";
                return RedirectToAction(nameof(RegistroApp));
            }

            var folio = await _registroService.CreateAppAsync(model, CurrentUser());
            TempData["PosNotice"] = !string.IsNullOrWhiteSpace(folio)
                ? $"Registro app guardado con folio {folio}."
                : "No se pudo guardar el registro app.";
            return !string.IsNullOrWhiteSpace(folio)
                ? RedirectToAction(nameof(RegistroDiario), new { folioOperacion = folio })
                : RedirectToAction(nameof(RegistroApp));
        }

        private static bool IsSuspiciousMajesticAmount(string? tipoOperacion, decimal total) =>
            !string.IsNullOrWhiteSpace(tipoOperacion)
            && (tipoOperacion.Contains("MAJESTIC", StringComparison.OrdinalIgnoreCase)
                || tipoOperacion.Contains("MAESTIC", StringComparison.OrdinalIgnoreCase))
            && total <= 1m;

        [HttpPost]
        public async Task<IActionResult> GuardarPago(string? folioOperacion, decimal importe)
        {
            var success = !string.IsNullOrWhiteSpace(folioOperacion)
                && await _pagosService.RegisterAsync(folioOperacion, importe, CurrentUser());
            TempData["PosNotice"] = success
                ? $"Pago actualizado para el folio {folioOperacion}."
                : $"No se pudo actualizar el pago del folio {folioOperacion ?? "sin filtro"}.";
            return RedirectToAction(nameof(Comisiones), new { folioOperacion });
        }

        [HttpPost]
        public async Task<IActionResult> GuardarGasto(string? folioOperacion, decimal importe, string? concepto, string? observaciones)
        {
            var success = await _gastosService.UpdateAsync(folioOperacion ?? string.Empty, importe, concepto ?? string.Empty, observaciones ?? string.Empty, CurrentUser());
            TempData["PosNotice"] = success
                ? $"Gasto actualizado para el folio {folioOperacion}."
                : $"No se pudo actualizar el gasto del folio {folioOperacion ?? "sin filtro"}.";
            return RedirectToAction(nameof(Gastos), new { folioOperacion });
        }

        [HttpPost]
        public async Task<IActionResult> GuardarGafete(
            int matricula,
            string? gafete,
            long? folioOperacion,
            string? movimiento,
            bool esEdicion = false,
            int? matriculaOriginal = null,
            string? gafeteOriginal = null,
            long? folioOperacionOriginal = null)
        {
            var success = esEdicion
                ? await _gafetesService.UpdateAsync(matriculaOriginal, gafeteOriginal, folioOperacionOriginal, matricula, gafete ?? string.Empty, folioOperacion, movimiento ?? "A", CurrentUser())
                : await _gafetesService.InsertAsync(matricula, gafete ?? string.Empty, folioOperacion, movimiento ?? "A", CurrentUser());
            TempData["PosNotice"] = success
                ? esEdicion ? "Gafete actualizado." : "Gafete guardado."
                : esEdicion ? "No se pudo actualizar el gafete." : "No se pudo guardar el movimiento de gafete.";
            return RedirectToAction(nameof(Gafetes));
        }

        [HttpPost]
        public IActionResult ReiniciarGafetes(DateTime? fechaInicio, DateTime? fechaFin, bool soloSinFecha = false, int pagina = 1)
        {
            TempData["PosNotice"] = "Se recargo la consulta de gafetes.";
            return RedirectToAction(nameof(Gafetes), new { fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd"), soloSinFecha, pagina });
        }

        [HttpPost]
        public async Task<IActionResult> RegistrarRegresoGafete(string? gafete, long? folioOperacion, DateTime? fechaInicio, DateTime? fechaFin, bool soloSinFecha = false, int pagina = 1)
        {
            if (string.IsNullOrWhiteSpace(gafete))
            {
                TempData["PosNotice"] = "Gafete inválido.";
                return RedirectToAction(nameof(Gafetes), new { fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd"), soloSinFecha, pagina });
            }

            var existing = await _gafetesService.TryFindAsync(gafete);
            if (existing == null)
            {
                TempData["PosNotice"] = "No se encontro movimiento activo para ese gafete.";
                return RedirectToAction(nameof(Gafetes), new { fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd"), soloSinFecha, pagina });
            }

            var estatus = (existing.Estatus ?? string.Empty).Trim();
            if (!string.Equals(estatus, "OCUPADO", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(estatus, "SUSPENDIDO", StringComparison.OrdinalIgnoreCase))
            {
                TempData["PosNotice"] = $"Gafete {gafete} no esta en uso; estatus actual: {estatus}.";
                return RedirectToAction(nameof(Gafetes), new { fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd"), soloSinFecha, pagina });
            }

            var success = await _gafetesService.MarcarRegresoAsync(gafete, folioOperacion, CurrentUser());
            TempData["PosNotice"] = success
                ? $"Gafete {gafete} estaba en uso y se marco como regreso/libre."
                : "No se pudo actualizar el regreso del gafete.";
            if (success)
                TempData["PosUpdatedGafetes"] = $"{gafete}|{(folioOperacion?.ToString() ?? string.Empty)}";
            return RedirectToAction(nameof(Gafetes), new { fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd"), selectedGafete = gafete, selectedFolioOperacion = folioOperacion, soloSinFecha, pagina });
        }

        [HttpPost]
        public async Task<IActionResult> RegistrarRegresoGafetesBloque(
            string[]? seleccion,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            var items = (seleccion ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count == 0)
            {
                TempData["PosNotice"] = "Escanea o selecciona al menos un gafete ocupado.";
                return RedirectToAction(nameof(Gafetes), new
                {
                    fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"),
                    fechaFin = fechaFin?.ToString("yyyy-MM-dd")
                });
            }

            var actualizados = 0;
            var noActualizados = 0;
            foreach (var item in items)
            {
                try
                {
                    var parts = item.Split('|', 2, StringSplitOptions.TrimEntries);
                    var gafete = parts[0];
                    long? folioOperacion = parts.Length > 1
                        && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var folio)
                            ? folio
                            : null;

                    if (await _gafetesService.MarcarRegresoAsync(gafete, folioOperacion, CurrentUser()))
                        actualizados++;
                    else
                        noActualizados++;
                }
                catch
                {
                    noActualizados++;
                }
            }

            TempData["PosNotice"] = actualizados > 0
                ? $"Regreso en bloque actualizado. Gafetes liberados: {actualizados}. Sin cambio: {noActualizados}."
                : "No se pudo liberar ningun gafete del bloque. Revisa que esten ocupados o que el folio corresponda.";

            return RedirectToAction(nameof(Gafetes), new
            {
                fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"),
                fechaFin = fechaFin?.ToString("yyyy-MM-dd")
            });
        }

        [HttpPost]
        public async Task<IActionResult> RegistrarRegresoGafetesMasivo(string[]? seleccion, DateTime? fechaInicio, DateTime? fechaFin, bool soloSinFecha = false, int pagina = 1)
        {
            var items = (seleccion ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (items.Count == 0)
            {
                TempData["PosNotice"] = "Selecciona al menos un gafete ocupado.";
                return RedirectToAction(nameof(Gafetes), new { fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd"), soloSinFecha, pagina });
            }

            var ok = 0;
            var actualizados = new List<string>();
            foreach (var item in items)
            {
                var parts = item.Split('|', 2, StringSplitOptions.TrimEntries);
                var gafete = parts.Length > 0 ? parts[0] : string.Empty;
                long? folioOperacion = null;
                if (parts.Length > 1 && long.TryParse(parts[1], out var parsed))
                    folioOperacion = parsed;

                if (string.IsNullOrWhiteSpace(gafete))
                    continue;

                if (await _gafetesService.MarcarRegresoAsync(gafete, folioOperacion, CurrentUser()))
                {
                    ok++;
                    actualizados.Add($"{gafete}|{(folioOperacion?.ToString() ?? string.Empty)}");
                }
            }

            TempData["PosNotice"] = ok > 0
                ? $"Regreso masivo aplicado. Gafetes actualizados: {ok}."
                : "No se pudo actualizar el regreso de los gafetes seleccionados.";
            if (ok > 0)
                TempData["PosUpdatedGafetes"] = string.Join("|", actualizados);
            return RedirectToAction(nameof(Gafetes), new { fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd"), soloSinFecha, pagina });
        }

        [HttpPost]
        public async Task<IActionResult> CalcularCorte(DateTime? fecha)
        {
            var actualizados = await _comisionesService.RecalcularAsync(usuario: CurrentUser(), fecha: fecha);
            TempData["PosNotice"] = $"Corte recalculado. Comisiones actualizadas: {actualizados}.";
            return RedirectToAction(nameof(Cortes), new { fecha = fecha?.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        public async Task<IActionResult> CalcularComisiones(string? folioOperacion, DateTime? fechaInicio, DateTime? fechaFin, string? regreso, string? busqueda)
        {
            var actualizados = 0;
            if (string.IsNullOrWhiteSpace(folioOperacion) && (fechaInicio.HasValue || fechaFin.HasValue))
            {
                var inicio = (fechaInicio ?? fechaFin)!.Value.Date;
                var fin = (fechaFin ?? fechaInicio)!.Value.Date;
                if (fin < inicio)
                    (inicio, fin) = (fin, inicio);

                for (var fecha = inicio; fecha <= fin; fecha = fecha.AddDays(1))
                    actualizados += await _comisionesService.RecalcularAsync(null, CurrentUser(), fecha);
            }
            else
            {
                actualizados = await _comisionesService.RecalcularAsync(folioOperacion, CurrentUser());
            }

            TempData["PosNotice"] = !string.IsNullOrWhiteSpace(folioOperacion)
                ? $"Comision recalculada para el folio {folioOperacion}. Filas actualizadas: {actualizados}."
                : fechaInicio.HasValue || fechaFin.HasValue
                    ? $"Comisiones recalculadas del {(fechaInicio ?? fechaFin)!.Value:dd/MM/yyyy} al {(fechaFin ?? fechaInicio)!.Value:dd/MM/yyyy}. Filas actualizadas: {actualizados}."
                    : $"Comisiones recalculadas. Filas actualizadas: {actualizados}.";
            if (string.Equals(regreso, nameof(Relaciones), StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Relaciones), new { busqueda, fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd") });

            return RedirectToAction(nameof(Comisiones), new { folioOperacion, fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        public async Task<IActionResult> PagarComision(string? folioOperacion, DateTime? fechaInicio, DateTime? fechaFin, string? regreso, string? busqueda)
        {
            var success = !string.IsNullOrWhiteSpace(folioOperacion)
                && await _comisionesService.PayAsync(folioOperacion, HttpContext.Session.GetString("PosUser") ?? "WEB");
            TempData["PosNotice"] = success
                ? $"Comision pagada para el folio {folioOperacion}."
                : $"No se pudo pagar la comision del folio {folioOperacion ?? "sin folio"}. Revisa si ya estaba pagada, sin calcular o en corte cerrado.";
            if (string.Equals(regreso, nameof(Relaciones), StringComparison.OrdinalIgnoreCase))
                return RedirectToAction(nameof(Relaciones), new { busqueda, fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd") });

            return RedirectToAction(nameof(Comisiones), new { folioOperacion, fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        public async Task<IActionResult> AbonarComision(string? folioOperacion, decimal importe, string? filtro, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var success = !string.IsNullOrWhiteSpace(folioOperacion)
                && await _pagosService.RegisterAsync(folioOperacion, importe, CurrentUser());
            TempData["PosNotice"] = success
                ? $"Pago aplicado a la comision del folio {folioOperacion}."
                : $"No se pudo aplicar el pago del folio {folioOperacion ?? "sin folio"}. Revisa saldo o importe.";
            return RedirectToAction(nameof(Comisiones), new { folioOperacion = filtro, fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        public async Task<IActionResult> PagarComisionesSeleccionadas(string? folioOperacion, DateTime? fechaInicio, DateTime? fechaFin, string[]? foliosSeleccionados)
        {
            var folios = (foliosSeleccionados ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var pagadas = 0;
            foreach (var folio in folios)
            {
                if (await _comisionesService.PayAsync(folio, CurrentUser()))
                    pagadas++;
            }

            TempData["PosNotice"] = folios.Count == 0
                ? "Selecciona al menos una comision pendiente."
                : $"Comisiones seleccionadas: {folios.Count}. Pagadas: {pagadas}.";
            return RedirectToAction(nameof(Comisiones), new { folioOperacion, fechaInicio = fechaInicio?.ToString("yyyy-MM-dd"), fechaFin = fechaFin?.ToString("yyyy-MM-dd") });
        }

        [HttpPost]
        public async Task<IActionResult> CerrarCorte(DateTime fecha)
        {
            var success = await _cortesService.CloseAsync(CurrentUser(), fecha);
            TempData["PosNotice"] = success
                ? "Corte cerrado. El dia queda bloqueado para cambios normales."
                : "No se pudo cerrar el corte.";
            return RedirectToAction(nameof(Cortes), new { fecha = fecha.ToString("yyyy-MM-dd") });
        }

        public async Task<FileContentResult> ExportRegistroCsv(string? folioOperacion, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var model = await _registroService.TryGetAsync(folioOperacion, fechaInicio, fechaFin) ?? new PosRegistroDiarioViewModel();
            var rows = model.Operaciones.Select(x => new[]
            {
                x.FolioOperacion,
                x.FolioControl,
                x.CantidadTickets.ToString(),
                x.Ticket,
                x.Hotel,
                x.Hora,
                x.Pax.ToString(),
                x.Vendedor,
                x.TipoOperacion,
                x.Total.ToString("0.00")
            });

            return BuildCsv("registro_diario.csv", new[] { "FolioOperacion", "FolioControl", "CantidadTickets", "Ticket", "LlegadaSucursal", "Hora", "Pax", "Taxista", "Tipo", "Total" }, rows);
        }

        public async Task<FileContentResult> ExportRegistroExcel(string? folioOperacion, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var model = await _registroService.TryGetAsync(folioOperacion, fechaInicio, fechaFin) ?? new PosRegistroDiarioViewModel();
            var bytes = BuildReporteTaxiExcel(model.Operaciones);
            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = $"reporte_taxi_{DateTime.Today:yyyyMMdd}.xlsx"
            };
        }

        public async Task<FileContentResult> ExportReporteTaxiExcel(DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var model = await _registroService.TryGetAsync() ?? new PosRegistroDiarioViewModel();
            var operaciones = fechaInicio.HasValue || fechaFin.HasValue
                ? FilterReportOperations(model.Operaciones, fechaInicio, fechaFin)
                : new List<PosOperacionRowViewModel>();
            var bytes = BuildReporteTaxiExcel(operaciones);
            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = $"reporte_taxi_{DateTime.Today:yyyyMMdd}.xlsx"
            };
        }

        public async Task<FileContentResult> ExportReporteDejadasExcel(DateTime? fechaInicio = null, DateTime? fechaFin = null, string? busqueda = null)
        {
            if (!fechaInicio.HasValue && !fechaFin.HasValue)
            {
                fechaInicio = DateTime.Today;
                fechaFin = DateTime.Today;
            }

            var rows = await _relacionesService.GetReporteDejadasAsync(fechaInicio, fechaFin, busqueda);
            var bytes = BuildReporteDejadasExcel(rows, fechaInicio, fechaFin);
            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = $"control_dejadas_{(fechaInicio ?? DateTime.Today):yyyyMMdd}_{(fechaFin ?? fechaInicio ?? DateTime.Today):yyyyMMdd}.xlsx"
            };
        }

        public async Task<FileContentResult> ExportReporteConcentradoExcel(DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            if (!fechaInicio.HasValue && !fechaFin.HasValue)
            {
                fechaInicio = DateTime.Today;
                fechaFin = DateTime.Today;
            }

            var rows = await _relacionesService.GetReporteDejadasAsync(fechaInicio, fechaFin);
            var bytes = BuildReporteConcentradoExcel(rows, fechaInicio, fechaFin);
            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = $"concentrado_general_{(fechaInicio ?? DateTime.Today):yyyyMMdd}_{(fechaFin ?? fechaInicio ?? DateTime.Today):yyyyMMdd}.xlsx"
            };
        }

        public async Task<FileContentResult> ExportReporteCuadreFinalExcel(DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            if (!fechaInicio.HasValue && !fechaFin.HasValue)
            {
                fechaInicio = DateTime.Today;
                fechaFin = DateTime.Today;
            }

            var rows = await _relacionesService.GetReporteDejadasAsync(fechaInicio, fechaFin);
            var camiones = await _relacionesService.GetCamionesResumenAsync(fechaInicio, fechaFin);
            var bytes = BuildReporteCuadreFinalExcel(rows, camiones, fechaInicio, fechaFin);
            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = $"cuadre_final_{(fechaInicio ?? DateTime.Today):yyyyMMdd}_{(fechaFin ?? fechaInicio ?? DateTime.Today):yyyyMMdd}.xlsx"
            };
        }

        public async Task<FileContentResult> ExportReportePagosComisionesExcel(DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var pagos = await _comisionesService.GetPagosReporteAsync(fechaInicio, fechaFin);
            var bytes = BuildReportePagosComisionesExcel(pagos, fechaInicio, fechaFin);
            return new FileContentResult(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
            {
                FileDownloadName = $"reporte_pagos_comisiones_{DateTime.Today:yyyyMMdd}.xlsx"
            };
        }


        public async Task<FileContentResult> ExportRegistroPdf(string? folioOperacion, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var model = await _registroService.TryGetAsync(folioOperacion, fechaInicio, fechaFin) ?? new PosRegistroDiarioViewModel();
            var lines = new List<string>
            {
                "** REPORTE DE OPERACIONES TOTAL **",
                $"Fecha del Reporte : {DateTime.Today:dd/MM/yyyy} al {DateTime.Today:dd/MM/yyyy}".PadRight(88) + "Page 1 of 1",
                string.Empty,
                "taxista".PadRight(20) +
                "fecha".PadRight(20) +
                "PAX".PadLeft(5) + "  " +
                "LLEGADA/SUC".PadRight(22) +
                "DEJADA".PadLeft(10) +
                "IMPORTE".PadLeft(12) +
                "COMISION".PadLeft(12) +
                "PAGO".PadLeft(10),
                new string('-', 115)
            };

            lines.AddRange(model.Operaciones.Take(42).Select(x =>
                SafePdfColumn(x.Vendedor, 20) +
                SafePdfColumn(x.Hora, 20) +
                x.Pax.ToString().PadLeft(5) + "  " +
                SafePdfColumn(x.Hotel, 22) +
                "0.00".PadLeft(10) +
                x.Total.ToString("0.00").PadLeft(12) +
                "0.00".PadLeft(12) +
                "0.00".PadLeft(10)));

            return new FileContentResult(BuildSimplePdf(lines), "application/pdf") { FileDownloadName = "movimientos.pdf" };
        }

        public async Task<FileContentResult> ExportPagosCsv(string? folioOperacion)
        {
            var model = await _pagosService.TryGetAsync(folioOperacion) ?? new PosPagosViewModel();
            var rows = model.Pagos.Select(x => new[]
            {
                model.FolioOperacion,
                model.Ticket,
                x.Fecha,
                x.FormaPago,
                x.Importe.ToString("0.00"),
                x.Referencia,
                x.Estatus
            });

            return BuildCsv("pagos.csv", new[] { "FolioOperacion", "Ticket", "Fecha", "FormaPago", "Importe", "Referencia", "Estatus" }, rows);
        }

        public async Task<FileContentResult> ExportPagosPdf(string? folioOperacion)
        {
            var model = await _pagosService.TryGetAsync(folioOperacion) ?? new PosPagosViewModel();
            var lines = new List<string>
            {
                "HOKA - PAGOS",
                $"Folio: {model.FolioOperacion}",
                $"Total: {model.TotalPagado:0.00}"
            };
            lines.AddRange(model.Pagos.Take(35).Select(x => $"{x.Fecha} | {x.FormaPago} | {x.Importe:0.00} | {x.Estatus}"));
            return new FileContentResult(BuildSimplePdf(lines), "application/pdf") { FileDownloadName = "pagos.pdf" };
        }

        public async Task<FileContentResult> ExportGastosCsv(string? folioOperacion)
        {
            var model = await _gastosService.TryGetAsync(folioOperacion) ?? new PosGastosViewModel();
            return BuildCsv("gastos.csv", new[] { "Fecha", "Concepto", "Importe", "Observacion", "Estatus" }, model.Filas.Select(x => x.ToArray()));
        }

        public async Task<FileContentResult> ExportComisionesCsv(string? folioOperacion, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var model = await _comisionesService.TryGetAsync(folioOperacion, fechaInicio, fechaFin) ?? new PosComisionesViewModel();
            var rows = model.Comisiones.Select(x => new[]
            {
                x.Folio,
                x.Transporte,
                x.NumeroUnidad,
                string.IsNullOrWhiteSpace(x.Staff) ? x.Beneficiario : x.Staff,
                x.Pax.ToString(CultureInfo.InvariantCulture),
                x.Hotel,
                x.VentaArtesania.ToString("0.00"),
                x.VentaFarmacia.ToString("0.00"),
                x.VentaTienda.ToString("0.00"),
                x.VentaJoyeria.ToString("0.00"),
                x.Base.ToString("0.00"),
                x.Ticket,
                x.FormaPago,
                FormatPercent(x.DescuentoAplicado),
                x.DeduccionDejada.ToString("0.00"),
                (x.DeduccionBebidas + x.DeduccionCajasRegalo).ToString("0.00"),
                x.DeduccionReparacion.ToString("0.00"),
                x.DeduccionDegustacion.ToString("0.00"),
                FormatPercent(x.Porcentaje),
                x.Importe.ToString("0.00"),
                x.Vendedor,
                x.Estatus
            });

            return BuildCsv("comisiones.csv", new[] { "Folios", "Unidad", "NumUnidad", "Nombre", "Pax", "Hotel", "VA", "VF", "VT", "VJ", "VntTotal", "Ticket", "FP", "Descuento", "Dejada", "BebidasCajasRegalo", "Rep", "Degustacion", "ComisionPorcentaje", "PagoComision", "Vendedor", "Estatus" }, rows);
        }

        public async Task<FileContentResult> ExportComisionesPdf(string? folioOperacion, DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var model = await _comisionesService.TryGetAsync(folioOperacion, fechaInicio, fechaFin) ?? new PosComisionesViewModel();
            var lines = new List<string>
            {
                "HOKA - COMISIONES",
                $"Folio filtro: {model.FolioOperacion}",
                $"Base: {model.TotalBase:0.00}",
                $"Comision: {model.TotalComision:0.00}",
                $"Pagado: {model.TotalPagado:0.00}"
            };
            lines.AddRange(model.Comisiones.Take(30).Select(x => $"{x.Folio} | {x.BeneficiarioTipo} | {(string.IsNullOrWhiteSpace(x.Staff) ? x.Beneficiario : x.Staff)} | {x.Importe:0.00} | {x.Estatus}"));
            return new FileContentResult(BuildSimplePdf(lines), "application/pdf") { FileDownloadName = "comisiones.pdf" };
        }

        public async Task<FileContentResult> ExportCorteCsv(DateTime? fecha)
        {
            var model = await _cortesService.TryGetAsync(fecha) ?? new PosCorteViewModel();
            var rows = new List<string[]>
            {
                new[] { "Efectivo", model.Efectivo.ToString("0.00") },
                new[] { "Tarjeta", model.Tarjeta.ToString("0.00") },
                new[] { "Amex", model.Amex.ToString("0.00") },
                new[] { "Gastos", model.Gastos.ToString("0.00") },
                new[] { "Comisiones", model.Comisiones.ToString("0.00") },
                new[] { "TotalDia", model.TotalDia.ToString("0.00") },
                new[] { "Diferencia", model.Diferencia.ToString("0.00") }
            };

            return BuildCsv("corte.csv", new[] { "Concepto", "Importe" }, rows);
        }

        public async Task<FileContentResult> ExportCortePdf(DateTime? fecha)
        {
            var model = await _cortesService.TryGetAsync(fecha) ?? new PosCorteViewModel();
            var lines = new[]
            {
                "HOKA - CORTE",
                $"Fecha: {model.Fecha:dd/MM/yyyy}",
                $"Efectivo: {model.Efectivo:0.00}",
                $"Tarjeta: {model.Tarjeta:0.00}",
                $"AMEX: {model.Amex:0.00}",
                $"Gastos: {model.Gastos:0.00}",
                $"Comisiones: {model.Comisiones:0.00}",
                $"Total del dia: {model.TotalDia:0.00}",
                $"Diferencia: {model.Diferencia:0.00}",
                $"Estatus: {(model.Cerrado ? "Cerrado" : "Abierto")}"
            };

            return new FileContentResult(BuildSimplePdf(lines), "application/pdf")
            {
                FileDownloadName = $"corte_{model.Fecha:yyyyMMdd}.pdf"
            };
        }

        public async Task<FileContentResult> ExportGafetesCsv(DateTime? fechaInicio = null, DateTime? fechaFin = null)
        {
            var model = await _gafetesService.TryGetAsync(fechaInicio, fechaFin) ?? new PosGafetesViewModel();
            return BuildCsv("gafetes.csv", new[] { "Gafete", "Staff", "Folio Operacion", "Unidad", "Telefono", "Nacionalidad", "Entrega", "Estatus", "Regreso" }, model.Filas.Select(x => x.ToArray()));
        }

        private static FileContentResult BuildCsv(string fileName, IEnumerable<string> headers, IEnumerable<string[]> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
            foreach (var row in rows)
            {
                builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            }

            return new FileContentResult(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv")
            {
                FileDownloadName = fileName
            };
        }

        private static string FormatPercent(decimal value)
        {
            var absolute = Math.Abs(value);
            var ratio = absolute > 0m && absolute <= 1m ? value : value / 100m;
            return ratio.ToString("0.##%", CultureInfo.InvariantCulture);
        }

        private static string EscapeCsv(string? value)
        {
            var text = value ?? string.Empty;
            if (text.Contains(',') || text.Contains('"') || text.Contains('\n'))
                return $"\"{text.Replace("\"", "\"\"")}\"";

            return text;
        }

        private static byte[] BuildSimplePdf(IEnumerable<string> lines)
        {
            var content = new StringBuilder();
            content.AppendLine("BT");
            content.AppendLine("/F1 14 Tf");
            content.AppendLine("50 780 Td");
            foreach (var line in lines)
            {
                content.Append('(').Append(EscapePdf(line)).AppendLine(") Tj");
                content.AppendLine("0 -22 Td");
            }
            content.AppendLine("ET");

            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>",
                $"<< /Length {Encoding.ASCII.GetByteCount(content.ToString())} >>\nstream\n{content}endstream"
            };

            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int> { 0 };
            foreach (var obj in objects.Select((value, index) => new { value, index }))
            {
                offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
                pdf.Append(obj.index + 1).AppendLine(" 0 obj");
                pdf.AppendLine(obj.value);
                pdf.AppendLine("endobj");
            }

            var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.AppendLine("xref");
            pdf.AppendLine($"0 {objects.Count + 1}");
            pdf.AppendLine("0000000000 65535 f ");
            foreach (var offset in offsets.Skip(1))
                pdf.AppendLine($"{offset:0000000000} 00000 n ");
            pdf.AppendLine("trailer");
            pdf.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
            pdf.AppendLine("startxref");
            pdf.AppendLine(xref.ToString());
            pdf.AppendLine("%%EOF");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }

        private static string EscapePdf(string? value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

        private static string SafePdfColumn(string? value, int width)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length > width)
                text = text[..width];

            return text.PadRight(width);
        }

        private static byte[] BuildReporteTaxiExcel(IReadOnlyCollection<PosOperacionRowViewModel> operaciones)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddZipEntry(archive, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""");
                AddZipEntry(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
                AddZipEntry(archive, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""");
                AddZipEntry(archive, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets><sheet name="Reporte taxi" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""");
                AddZipEntry(archive, "xl/styles.xml", BuildReporteTaxiStyles());
                AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildReporteTaxiWorksheet(operaciones));
            }

            return stream.ToArray();
        }

        private static byte[] BuildReportePagosComisionesExcel(IReadOnlyCollection<PosComisionRowViewModel> pagos, DateTime? fechaInicio, DateTime? fechaFin)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddZipEntry(archive, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""");
                AddZipEntry(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
                AddZipEntry(archive, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""");
                AddZipEntry(archive, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets><sheet name="Pagos comisiones" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""");
                AddZipEntry(archive, "xl/styles.xml", BuildReporteTaxiStyles());
                AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildReportePagosComisionesWorksheet(pagos, fechaInicio, fechaFin));
            }

            return stream.ToArray();
        }

        private static byte[] BuildReporteDejadasExcel(IReadOnlyCollection<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                AddZipEntry(archive, "[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
</Types>
""");
                AddZipEntry(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
                AddZipEntry(archive, "xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
</Relationships>
""");
                AddZipEntry(archive, "xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets><sheet name="Control Dejadas" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""");
                AddZipEntry(archive, "xl/styles.xml", BuildReporteTaxiStyles());
                AddZipEntry(archive, "xl/worksheets/sheet1.xml", BuildReporteDejadasWorksheet(rows, fechaInicio, fechaFin));
            }

            return stream.ToArray();
        }

        private static byte[] BuildReporteConcentradoExcel(IReadOnlyCollection<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var sheetSpecs = BuildConcentradoSheetSpecs(rows, fechaInicio, fechaFin);
            return BuildWorkbookWithSheets(sheetSpecs);
        }

        private static byte[] BuildReporteCuadreFinalExcel(IReadOnlyCollection<PosRelacionRowViewModel> rows, IReadOnlyCollection<CuadreCamionesSummary> camiones, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var sheetSpecs = new List<ExcelSheetSpec>
            {
                new("CUADRE", BuildCuadreResumenWorksheet(rows, camiones, fechaInicio, fechaFin)),
                new("CUADRE dejadas", BuildReporteDejadasWorksheet(rows, fechaInicio, fechaFin)),
                new("REPORTE HOTELES", BuildReporteHotelesWorksheet(rows, fechaInicio, fechaFin)),
                new("comisiones", BuildReporteComisionesResumenWorksheet(rows, fechaInicio, fechaFin)),
                new("CORTE FINAL", BuildReporteCorteFinalWorksheet(rows, fechaInicio, fechaFin))
            };
            return BuildWorkbookWithSheets(sheetSpecs);
        }

        private static string BuildReporteTaxiStyles() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <numFmts count="5">
    <numFmt numFmtId="164" formatCode="dd/mm/yyyy"/>
    <numFmt numFmtId="165" formatCode="hh:mm"/>
    <numFmt numFmtId="166" formatCode='"$"#,##0.00'/>
    <numFmt numFmtId="167" formatCode="0%"/>
    <numFmt numFmtId="168" formatCode="#,##0"/>
  </numFmts>
  <fonts count="4">
    <font><sz val="11"/><name val="Calibri"/></font>
    <font><b/><sz val="11"/><name val="Calibri"/></font>
    <font><b/><sz val="11"/><name val="Calibri"/><color rgb="FFFFFFFF"/></font>
    <font><sz val="10"/><name val="Calibri"/></font>
  </fonts>
  <fills count="8">
    <fill><patternFill patternType="none"/></fill>
    <fill><patternFill patternType="gray125"/></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFFFEB00"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF92D050"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFD9EAD3"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FF1F3864"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/><bgColor indexed="64"/></patternFill></fill>
    <fill><patternFill patternType="solid"><fgColor rgb="FFD6DCE4"/><bgColor indexed="64"/></patternFill></fill>
  </fills>
  <borders count="2">
    <border><left/><right/><top/><bottom/><diagonal/></border>
    <border><left style="thin"/><right style="thin"/><top style="thin"/><bottom style="thin"/><diagonal/></border>
  </borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="18">
    <xf numFmtId="0"   fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0"   fontId="1" fillId="2" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="0"   fontId="1" fillId="3" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="0"   fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/>
    <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="165" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
    <xf numFmtId="0"   fontId="0" fillId="4" borderId="1" xfId="0" applyFill="1" applyBorder="1"/>
    <xf numFmtId="0"   fontId="2" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyAlignment="1"><alignment horizontal="center"/></xf>
    <xf numFmtId="166" fontId="2" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center"/></xf>
    <xf numFmtId="168" fontId="2" fillId="5" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1" applyAlignment="1"><alignment horizontal="center"/></xf>
    <xf numFmtId="0"   fontId="1" fillId="6" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="166" fontId="1" fillId="6" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1"/>
    <xf numFmtId="167" fontId="1" fillId="6" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1"/>
    <xf numFmtId="166" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/>
    <xf numFmtId="167" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/>
    <xf numFmtId="0"   fontId="1" fillId="7" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1"/>
    <xf numFmtId="166" fontId="1" fillId="7" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyFont="1" applyNumberFormat="1"/>
    <xf numFmtId="168" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyNumberFormat="1"/>
  </cellXfs>
  <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
</styleSheet>
""";

        private static string BuildReporteTaxiWorksheet(IReadOnlyCollection<PosOperacionRowViewModel> operaciones)
        {
            var rows = new StringBuilder();
            rows.AppendLine("<row r=\"2\">" + TextCell("D2", "A", 3) + TextCell("E2", "ADULTO", 3) + "</row>");
            rows.AppendLine("<row r=\"3\">" + TextCell("D3", "N", 3) + TextCell("E3", "NINO", 3) + "</row>");
            rows.AppendLine("<row r=\"4\">" + TextCell("D4", "IN", 3) + TextCell("E4", "INFANTE", 3) + "</row>");
            rows.AppendLine("<row r=\"5\">" + NumberCell("O5", 0.16m, 6) + NumberCell("P5", 0.19m, 6) + "</row>");

            var headers = new (string Cell, string Text)[]
            {
                ("B6", "Nombre taxista"),
                ("C6", "Codigo"),
                ("D6", "Tipo"),
                ("E6", "Fecha"),
                ("F6", "Hora"),
                ("G6", "numero Pasajero A"),
                ("H6", "numero Pasajero N"),
                ("I6", "numero Pasajero IN"),
                ("J6", "Hotel"),
                ("K6", "extranjero"),
                ("L6", "nacional"),
                ("M6", "venta bruta"),
                ("N6", "efectivo"),
                ("O6", "tarjeta varias"),
                ("P6", "amexco"),
                ("Q6", "dejada"),
                ("R6", "gastos"),
                ("S6", "venta neta"),
                ("T6", "comision"),
                ("U6", "Fecha de pago"),
                ("V6", "Estatus")
            };
            rows.Append("<row r=\"6\">");
            foreach (var header in headers)
                rows.Append(TextCell(header.Cell, header.Text, 1));
            rows.AppendLine("</row>");

            var rowNumber = 7;
            foreach (var op in operaciones)
            {
                var fecha = TryParseReportDate(op.Hora);
                var efectivo = op.Efectivo > 0 || op.Tarjeta > 0 ? op.Efectivo : op.Total;
                var tarjeta = op.Tarjeta;
                rows.Append($"<row r=\"{rowNumber}\">");
                rows.Append(TextCell($"B{rowNumber}", op.Vendedor, 3));
                rows.Append(TextCell($"C{rowNumber}", op.FolioOperacion, 3));
                rows.Append(TextCell($"D{rowNumber}", op.TipoOperacion, 3));
                if (fecha.HasValue)
                {
                    rows.Append(NumberCell($"E{rowNumber}", (decimal)fecha.Value.Date.ToOADate(), 4));
                    rows.Append(NumberCell($"F{rowNumber}", (decimal)fecha.Value.TimeOfDay.TotalDays, 5));
                }
                else
                {
                    rows.Append(TextCell($"E{rowNumber}", op.Hora, 3));
                    rows.Append(TextCell($"F{rowNumber}", string.Empty, 3));
                }
                rows.Append(NumberCell($"G{rowNumber}", op.Pax, 3));
                rows.Append(NumberCell($"H{rowNumber}", 0, 3));
                rows.Append(NumberCell($"I{rowNumber}", 0, 3));
                rows.Append(TextCell($"J{rowNumber}", op.Hotel, 3));
                rows.Append(NumberCell($"K{rowNumber}", 0, 3));
                rows.Append(NumberCell($"L{rowNumber}", 0, 3));
                rows.Append(NumberCell($"M{rowNumber}", op.Total, 3));
                rows.Append(NumberCell($"N{rowNumber}", efectivo, 3));
                rows.Append(NumberCell($"O{rowNumber}", tarjeta, 3));
                rows.Append(NumberCell($"P{rowNumber}", 0, 3));
                rows.Append(NumberCell($"Q{rowNumber}", 0, 3));
                rows.Append(NumberCell($"R{rowNumber}", 0, 3));
                rows.Append(NumberCell($"S{rowNumber}", op.Total, 6));
                rows.Append(NumberCell($"T{rowNumber}", 0, 3));
                rows.Append(TextCell($"U{rowNumber}", string.Empty, 3));
                rows.Append(TextCell($"V{rowNumber}", "pendiente", 3));
                rows.AppendLine("</row>");
                rowNumber++;
            }

            if (operaciones.Count == 0)
            {
                rows.Append($"<row r=\"{rowNumber}\">");
                rows.Append(TextCell($"B{rowNumber}", "Sin operaciones para mostrar", 3));
                rows.AppendLine("</row>");
            }

            var lastRow = Math.Max(rowNumber - 1, 7);
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B2:V{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="2" width="22" customWidth="1"/>
    <col min="3" max="3" width="12" customWidth="1"/>
    <col min="4" max="4" width="16" customWidth="1"/>
    <col min="5" max="6" width="12" customWidth="1"/>
    <col min="7" max="9" width="18" customWidth="1"/>
    <col min="10" max="10" width="24" customWidth="1"/>
    <col min="11" max="22" width="14" customWidth="1"/>
  </cols>
  <sheetData>
{{rows}}
  </sheetData>
  <autoFilter ref="B6:V{{lastRow}}"/>
  <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        private static string BuildReportePagosComisionesWorksheet(IReadOnlyCollection<PosComisionRowViewModel> pagos, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var rows = new StringBuilder();
            var rango = fechaInicio.HasValue || fechaFin.HasValue
                ? $"{fechaInicio?.ToString("dd/MM/yyyy") ?? "Inicio"} al {fechaFin?.ToString("dd/MM/yyyy") ?? "Fin"}"
                : DateTime.Today.ToString("dd/MM/yyyy");
            rows.AppendLine("<row r=\"2\">" + TextCell("B2", "REPORTE DE PAGOS DE COMISIONES", 2) + TextCell("C2", rango, 2) + "</row>");
            rows.AppendLine("<row r=\"3\">" + TextCell("B3", "Total pagado", 3) + NumberCell("C3", pagos.Sum(x => x.Pago), 3) + "</row>");

            var headers = new (string Cell, string Text)[]
            {
                ("B5", "Folio operacion"),
                ("C5", "Ticket app"),
                ("D5", "Fecha operacion"),
                ("E5", "Fecha pago"),
                ("F5", "Taxista"),
                ("G5", "Gafete/Beneficiario"),
                ("H5", "Transporte"),
                ("I5", "Base"),
                ("J5", "%"),
                ("K5", "Comision"),
                ("L5", "Pago"),
                ("M5", "Saldo"),
                ("N5", "Estatus")
            };
            rows.Append("<row r=\"5\">");
            foreach (var header in headers)
                rows.Append(TextCell(header.Cell, header.Text, 1));
            rows.AppendLine("</row>");

            var rowNumber = 6;
            foreach (var pago in pagos)
            {
                rows.Append($"<row r=\"{rowNumber}\">");
                rows.Append(TextCell($"B{rowNumber}", pago.Folio, 3));
                rows.Append(TextCell($"C{rowNumber}", pago.Ticket, 3));
                rows.Append(TextCell($"D{rowNumber}", pago.Fecha, 3));
                rows.Append(TextCell($"E{rowNumber}", pago.FechaPago, 3));
                rows.Append(TextCell($"F{rowNumber}", pago.Staff, 3));
                rows.Append(TextCell($"G{rowNumber}", pago.Beneficiario, 3));
                rows.Append(TextCell($"H{rowNumber}", pago.Transporte, 3));
                rows.Append(NumberCell($"I{rowNumber}", pago.Base, 3));
                rows.Append(NumberCell($"J{rowNumber}", pago.Porcentaje, 3));
                rows.Append(NumberCell($"K{rowNumber}", pago.Importe, 3));
                rows.Append(NumberCell($"L{rowNumber}", pago.Pago, 3));
                rows.Append(NumberCell($"M{rowNumber}", pago.Importe - pago.Pago, 3));
                rows.Append(TextCell($"N{rowNumber}", pago.Estatus, 3));
                rows.AppendLine("</row>");
                rowNumber++;
            }

            if (pagos.Count == 0)
            {
                rows.Append($"<row r=\"{rowNumber}\">");
                rows.Append(TextCell($"B{rowNumber}", "Sin pagos de comisiones para mostrar", 3));
                rows.AppendLine("</row>");
            }

            var lastRow = Math.Max(rowNumber - 1, 6);
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B2:N{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="3" width="18" customWidth="1"/>
    <col min="4" max="5" width="16" customWidth="1"/>
    <col min="6" max="8" width="22" customWidth="1"/>
    <col min="9" max="13" width="14" customWidth="1"/>
    <col min="14" max="14" width="16" customWidth="1"/>
  </cols>
  <sheetData>
{{rows}}
  </sheetData>
  <autoFilter ref="B5:N{{lastRow}}"/>
  <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        private static string BuildReporteDejadasWorksheet(IReadOnlyCollection<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var sheetRows = new StringBuilder();
            var rango = fechaInicio.HasValue || fechaFin.HasValue
                ? $"{fechaInicio?.ToString("dd/MM/yyyy") ?? "Inicio"} al {fechaFin?.ToString("dd/MM/yyyy") ?? "Fin"}"
                : DateTime.Today.ToString("dd/MM/yyyy");
            sheetRows.AppendLine("<row r=\"1\">" + TextCell("B1", "CONTROL DEJADAS", 2) + TextCell("C1", rango, 2) + "</row>");
            sheetRows.AppendLine("<row r=\"2\">" + TextCell("B2", "Registros", 3) + NumberCell("C2", rows.Count, 3) + TextCell("D2", "Dejada total", 3) + NumberCell("E2", rows.Sum(x => x.Dejada), 3) + TextCell("F2", "Venta total", 3) + NumberCell("G2", rows.Sum(x => x.Venta), 3) + "</row>");

            var headers = new (string Cell, string Text)[]
            {
                ("B4", "FECHA"),
                ("C4", "FOLIO"),
                ("D4", "HORA"),
                ("E4", "NOMBRE"),
                ("F4", "VENDEDOR"),
                ("G4", "UNIDAD"),
                ("H4", "NUMERO"),
                ("I4", "ORIGEN"),
                ("J4", "SITIO/HOTEL"),
                ("K4", "ADULTO"),
                ("L4", "JOVEN"),
                ("M4", "NINO"),
                ("N4", "IMPORTE DEJADA"),
                ("O4", "TELEFONO"),
                ("P4", "VENTA"),
                ("Q4", "ESTATUS DEJADA"),
                ("R4", "FECHA PAGO DEJADA"),
                ("S4", "COMISION"),
                ("T4", "PAGO COMISION"),
                ("U4", "ESTATUS COMISION"),
                ("V4", "TICKET"),
                ("W4", "TAXISTA ID"),
                ("X4", "GAFETE"),
                ("Y4", "NACIONALIDAD"),
                ("Z4", "SE FUERON INFO")
            };
            sheetRows.Append("<row r=\"4\">");
            foreach (var header in headers)
                sheetRows.Append(TextCell(header.Cell, header.Text, 1));
            sheetRows.AppendLine("</row>");

            var rowNumber = 5;
            foreach (var item in rows)
            {
                var fecha = TryParseReportDate(item.Fecha);
                var comisionReporte = item.Comision;
                var estatusComision = ResolveReportCommissionStatus(item.Comision, item.Pago);
                var folio = FirstText(item.FolioControl, item.FolioApp, item.FolioOperacion);
                var sitioHotel = FirstText(item.Sitio, item.Hotel, item.Destino);
                var numero = FirstText(item.Unidad, item.Placas);
                var ticket = FirstText(item.FolioPos, item.TicketPagoDejada);

                sheetRows.Append($"<row r=\"{rowNumber}\">");
                if (fecha.HasValue)
                {
                    sheetRows.Append(NumberCell($"B{rowNumber}", (decimal)fecha.Value.Date.ToOADate(), 4));
                    sheetRows.Append(NumberCell($"D{rowNumber}", (decimal)fecha.Value.TimeOfDay.TotalDays, 5));
                }
                else
                {
                    sheetRows.Append(TextCell($"B{rowNumber}", item.Fecha, 3));
                    sheetRows.Append(TextCell($"D{rowNumber}", string.Empty, 3));
                }

                sheetRows.Append(TextCell($"C{rowNumber}", folio, 3));
                sheetRows.Append(TextCell($"E{rowNumber}", FirstText(item.TaxistaNombre, item.Vendedor), 3));
                sheetRows.Append(TextCell($"F{rowNumber}", FirstText(item.Vendedor, item.UsuarioOrigen), 3));
                sheetRows.Append(TextCell($"G{rowNumber}", item.TransporteTipo, 3));
                sheetRows.Append(TextCell($"H{rowNumber}", numero, 3));
                sheetRows.Append(TextCell($"I{rowNumber}", FirstText(item.Origen, item.Hotel), 3));
                sheetRows.Append(TextCell($"J{rowNumber}", sitioHotel, 3));
                sheetRows.Append(NumberCell($"K{rowNumber}", item.Pax, 3));
                sheetRows.Append(NumberCell($"L{rowNumber}", 0, 3));
                sheetRows.Append(NumberCell($"M{rowNumber}", 0, 3));
                sheetRows.Append(NumberCell($"N{rowNumber}", item.Dejada, 3));
                sheetRows.Append(TextCell($"O{rowNumber}", item.Telefono, 3));
                sheetRows.Append(NumberCell($"P{rowNumber}", item.Venta, 3));
                sheetRows.Append(TextCell($"Q{rowNumber}", item.EstatusDejada, 3));
                sheetRows.Append(TextCell($"R{rowNumber}", item.FechaPagoDejada, 3));
                sheetRows.Append(NumberCell($"S{rowNumber}", comisionReporte, 3));
                sheetRows.Append(NumberCell($"T{rowNumber}", item.Pago, 3));
                sheetRows.Append(TextCell($"U{rowNumber}", estatusComision, 3));
                sheetRows.Append(TextCell($"V{rowNumber}", ticket, 3));
                sheetRows.Append(NumberCell($"W{rowNumber}", item.TaxistaId, 3));
                sheetRows.Append(TextCell($"X{rowNumber}", item.Gafete, 3));
                sheetRows.Append(TextCell($"Y{rowNumber}", item.Nacionalidad, 3));
                sheetRows.Append(item.SeFueron > 0 ? NumberCell($"Z{rowNumber}", item.SeFueron, 3) : TextCell($"Z{rowNumber}", string.Empty, 3));
                sheetRows.AppendLine("</row>");
                rowNumber++;
            }

            if (rows.Count == 0)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(TextCell($"B{rowNumber}", "Sin registros para mostrar", 3));
                sheetRows.AppendLine("</row>");
            }

            var lastRow = Math.Max(rowNumber - 1, 5);
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B1:Z{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="4" width="14" customWidth="1"/>
    <col min="5" max="5" width="28" customWidth="1"/>
    <col min="6" max="9" width="18" customWidth="1"/>
    <col min="10" max="12" width="10" customWidth="1"/>
    <col min="13" max="19" width="16" customWidth="1"/>
    <col min="20" max="26" width="18" customWidth="1"/>
  </cols>
  <sheetData>
{{sheetRows}}
  </sheetData>
  <autoFilter ref="B4:Z{{lastRow}}"/>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        private static List<ExcelSheetSpec> BuildConcentradoSheetSpecs(IReadOnlyCollection<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            return TransportSheetDefinitions()
                .Select(def => new ExcelSheetSpec(def.SheetName, BuildConcentradoCategoriaWorksheet(rows.Where(x => def.Match(x)).ToList(), def.DisplayName, fechaInicio, fechaFin)))
                .ToList();
        }

        private static string BuildConcentradoCategoriaWorksheet(IReadOnlyCollection<PosRelacionRowViewModel> rows, string categoria, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var dias = BuildDailyCategoryRows(rows, categoria)
                .OrderBy(x => x.Fecha)
                .ToList();
            var sheetRows = new StringBuilder();
            var totalPax = dias.Sum(x => x.Pax);
            var totalEntraron = dias.Sum(x => x.Entraron);
            var totalSalieron = dias.Sum(x => x.Salieron);
            var totalUnidades = dias.Sum(x => x.Unidades);
            var totalDejada = dias.Sum(x => x.Dejada);
            var totalComision = dias.Sum(x => x.Comision);
            var totalVenta = dias.Sum(x => x.Venta);
            var totalGastos = dias.Sum(x => x.TotalGastos);
            var ticketPromedio = totalPax > 0 ? totalVenta / totalPax : 0m;
            var porcentajeGasto = totalVenta > 0 ? totalGastos / totalVenta : 0m;

            sheetRows.AppendLine("<row r=\"2\">" +
                                 TextCell("B2", "PROMEDIO", 2) +
                                 NumberCell("C2", dias.Count > 0 ? Math.Round(totalPax / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("D2", dias.Count > 0 ? Math.Round(totalEntraron / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("E2", dias.Count > 0 ? Math.Round(totalSalieron / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("F2", dias.Count > 0 ? Math.Round(totalUnidades / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("G2", dias.Count > 0 ? Math.Round(totalDejada / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("H2", dias.Count > 0 ? Math.Round(totalComision / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("I2", dias.Count > 0 ? Math.Round(totalVenta / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("J2", dias.Count > 0 ? Math.Round(totalGastos / Math.Max(1m, dias.Count), 2) : 0m, 3) +
                                 NumberCell("K2", ticketPromedio, 3) +
                                 NumberCell("L2", porcentajeGasto, 6) +
                                 "</row>");
            sheetRows.AppendLine("<row r=\"3\">" +
                                 NumberCell("B3", dias.Count, 3) +
                                 TextCell("C3", "TOTALES", 2) +
                                 NumberCell("D3", totalPax, 3) +
                                 NumberCell("E3", totalEntraron, 3) +
                                 NumberCell("F3", totalSalieron, 3) +
                                 NumberCell("G3", totalUnidades, 3) +
                                 NumberCell("H3", totalDejada, 3) +
                                 NumberCell("I3", totalComision, 3) +
                                 NumberCell("J3", totalVenta, 3) +
                                 NumberCell("K3", totalGastos, 3) +
                                 NumberCell("L3", ticketPromedio, 3) +
                                 NumberCell("M3", porcentajeGasto, 6) +
                                 "</row>");
            sheetRows.AppendLine("<row r=\"4\">" +
                                 TextCell("B4", "FECHA", 1) +
                                 TextCell("C4", "TIPO", 1) +
                                 TextCell("D4", "PAX", 1) +
                                 TextCell("E4", "ENTRARON", 1) +
                                 TextCell("F4", "SE FUERON", 1) +
                                 TextCell("G4", "UNIDADES", 1) +
                                 TextCell("H4", "DEJADA", 1) +
                                 TextCell("I4", "COMISION", 1) +
                                 TextCell("J4", "VENTA", 1) +
                                 TextCell("K4", "TOTAL GASTOS", 1) +
                                 TextCell("L4", "TIKET PROMEDIO", 1) +
                                 TextCell("M4", "PORCENTAJE GASTO", 1) +
                                 "</row>");

            var rowNumber = 5;
            foreach (var dia in dias)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(NumberCell($"B{rowNumber}", (decimal)dia.Fecha.ToOADate(), 4));
                sheetRows.Append(TextCell($"C{rowNumber}", categoria, 3));
                sheetRows.Append(NumberCell($"D{rowNumber}", dia.Pax, 3));
                sheetRows.Append(NumberCell($"E{rowNumber}", dia.Entraron, 3));
                sheetRows.Append(NumberCell($"F{rowNumber}", dia.Salieron, 3));
                sheetRows.Append(NumberCell($"G{rowNumber}", dia.Unidades, 3));
                sheetRows.Append(NumberCell($"H{rowNumber}", dia.Dejada, 3));
                sheetRows.Append(NumberCell($"I{rowNumber}", dia.Comision, 3));
                sheetRows.Append(NumberCell($"J{rowNumber}", dia.Venta, 3));
                sheetRows.Append(NumberCell($"K{rowNumber}", dia.TotalGastos, 3));
                sheetRows.Append(NumberCell($"L{rowNumber}", dia.TicketPromedio, 3));
                sheetRows.Append(NumberCell($"M{rowNumber}", dia.PorcentajeGasto, 6));
                sheetRows.AppendLine("</row>");
                rowNumber++;
            }

            if (dias.Count == 0)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(TextCell($"B{rowNumber}", "Sin datos para este rango", 3));
                sheetRows.AppendLine("</row>");
            }

            var lastRow = Math.Max(rowNumber - 1, 5);
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B2:M{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="3" width="18" customWidth="1"/>
    <col min="4" max="7" width="12" customWidth="1"/>
    <col min="8" max="13" width="16" customWidth="1"/>
  </cols>
  <sheetData>
{{sheetRows}}
  </sheetData>
  <autoFilter ref="B4:M{{lastRow}}"/>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        private static string BuildReporteHotelesWorksheet(IReadOnlyCollection<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var hoteles = rows
                .GroupBy(x => FirstText(x.Hotel, x.Sitio, x.Destino, "SIN HOTEL"), StringComparer.OrdinalIgnoreCase)
                .Select(x => new
                {
                    Hotel = x.Key,
                    Viajes = x.Count(),
                    Venta = x.Sum(item => item.Venta),
                    Dejada = x.Sum(item => item.Dejada),
                    Comision = x.Sum(item => item.Comision),
                    Pax = x.Sum(item => item.Pax)
                })
                .OrderByDescending(x => x.Venta)
                .ThenBy(x => x.Hotel)
                .ToList();

            var sheetRows = new StringBuilder();
            sheetRows.AppendLine("<row r=\"2\">" + TextCell("B2", "REPORTE POR HOTELES", 2) + "</row>");
            sheetRows.AppendLine("<row r=\"4\">" +
                                 TextCell("B4", "HOTEL", 1) +
                                 TextCell("C4", "VIAJES", 1) +
                                 TextCell("D4", "PAX", 1) +
                                 TextCell("E4", "VENTA", 1) +
                                 TextCell("F4", "DEJADA", 1) +
                                 TextCell("G4", "COMISION", 1) +
                                 "</row>");
            var rowNumber = 5;
            foreach (var hotel in hoteles)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(TextCell($"B{rowNumber}", hotel.Hotel, 3));
                sheetRows.Append(NumberCell($"C{rowNumber}", hotel.Viajes, 3));
                sheetRows.Append(NumberCell($"D{rowNumber}", hotel.Pax, 3));
                sheetRows.Append(NumberCell($"E{rowNumber}", hotel.Venta, 3));
                sheetRows.Append(NumberCell($"F{rowNumber}", hotel.Dejada, 3));
                sheetRows.Append(NumberCell($"G{rowNumber}", hotel.Comision, 3));
                sheetRows.AppendLine("</row>");
                rowNumber++;
            }

            if (hoteles.Count == 0)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(TextCell($"B{rowNumber}", "Sin datos para este rango", 3));
                sheetRows.AppendLine("</row>");
            }

            var lastRow = Math.Max(rowNumber - 1, 5);
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B2:G{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="2" width="28" customWidth="1"/>
    <col min="3" max="7" width="14" customWidth="1"/>
  </cols>
  <sheetData>
{{sheetRows}}
  </sheetData>
  <autoFilter ref="B4:G{{lastRow}}"/>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        private static string BuildReporteComisionesResumenWorksheet(IReadOnlyCollection<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var detalles = rows
                .Where(x => x.Comision != 0m || x.Pago > 0m || x.Venta > 0m)
                .OrderBy(x => TryParseReportDate(x.Fecha) ?? DateTime.MaxValue)
                .ThenBy(x => FirstText(x.TaxistaNombre, x.Vendedor))
                .ToList();

            var sheetRows = new StringBuilder();
            sheetRows.AppendLine("<row r=\"2\">" + TextCell("B2", "COMISIONES", 2) + "</row>");
            sheetRows.AppendLine("<row r=\"4\">" +
                                 TextCell("B4", "FECHA", 1) +
                                 TextCell("C4", "FOLIO", 1) +
                                 TextCell("D4", "UNIDAD", 1) +
                                 TextCell("E4", "NOMBRE", 1) +
                                 TextCell("F4", "PAX", 1) +
                                 TextCell("G4", "HOTEL", 1) +
                                 TextCell("H4", "VENTA", 1) +
                                 TextCell("I4", "DEJADA", 1) +
                                 TextCell("J4", "COMISION", 1) +
                                 TextCell("K4", "PAGO", 1) +
                                 TextCell("L4", "ESTATUS", 1) +
                                 "</row>");
            var rowNumber = 5;
            foreach (var item in detalles)
            {
                var fecha = TryParseReportDate(item.Fecha);
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                if (fecha.HasValue)
                    sheetRows.Append(NumberCell($"B{rowNumber}", (decimal)fecha.Value.Date.ToOADate(), 4));
                else
                    sheetRows.Append(TextCell($"B{rowNumber}", item.Fecha, 3));
                sheetRows.Append(TextCell($"C{rowNumber}", FirstText(item.FolioOperacion, item.FolioControl, item.FolioApp), 3));
                sheetRows.Append(TextCell($"D{rowNumber}", FirstText(item.TransporteTipo, item.Unidad), 3));
                sheetRows.Append(TextCell($"E{rowNumber}", FirstText(item.TaxistaNombre, item.Vendedor), 3));
                sheetRows.Append(NumberCell($"F{rowNumber}", item.Pax, 3));
                sheetRows.Append(TextCell($"G{rowNumber}", FirstText(item.Hotel, item.Sitio), 3));
                sheetRows.Append(NumberCell($"H{rowNumber}", item.Venta, 3));
                sheetRows.Append(NumberCell($"I{rowNumber}", item.Dejada, 3));
                sheetRows.Append(NumberCell($"J{rowNumber}", item.Comision, 3));
                sheetRows.Append(NumberCell($"K{rowNumber}", item.Pago, 3));
                sheetRows.Append(TextCell($"L{rowNumber}", ResolveReportCommissionStatus(item.Comision, item.Pago), 3));
                sheetRows.AppendLine("</row>");
                rowNumber++;
            }

            if (detalles.Count == 0)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(TextCell($"B{rowNumber}", "Sin datos para este rango", 3));
                sheetRows.AppendLine("</row>");
            }

            var lastRow = Math.Max(rowNumber - 1, 5);
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B2:L{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="3" width="14" customWidth="1"/>
    <col min="4" max="7" width="18" customWidth="1"/>
    <col min="8" max="12" width="14" customWidth="1"/>
  </cols>
  <sheetData>
{{sheetRows}}
  </sheetData>
  <autoFilter ref="B4:L{{lastRow}}"/>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        // Estilos cuadre: 7=header oscuro texto, 8=header oscuro $, 9=header oscuro #,
        //   10=total amarillo texto, 11=total amarillo $, 12=total amarillo %,
        //   13=dato $, 14=dato %, 15=subtotal gris texto, 16=subtotal gris $, 17=dato #
        // Cuadre styles reference:
        //  3 = white+border (texto dato)        10 = amarillo+bold (header texto)
        // 11 = amarillo+bold+$                  12 = amarillo+bold+%
        // 13 = white+border+$                   14 = white+border+%
        // 17 = white+border+#,##0
        private static string BuildCuadreResumenWorksheet(
            IReadOnlyCollection<PosRelacionRowViewModel> rows,
            IReadOnlyCollection<CuadreCamionesSummary> camiones,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            // --- Calcular totales sección taxistas ---
            var allRows = rows.ToList();
            var totalPax       = allRows.Sum(x => x.Pax);
            var totalSalieron  = allRows.Count(x => x.Dejada > 0m && x.DejadaPagada <= 0m);
            var totalEntraron  = totalPax - totalSalieron;
            var totalUnidades  = allRows
                .Select(x => FirstText(x.Unidad, x.Placas, x.Gafete, x.TaxistaId.ToString()))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var totalDejada   = allRows.Sum(x => x.Dejada);
            var totalComision = allRows.Sum(x => x.Comision);
            var totalVenta    = allRows.GroupBy(ReportSaleKey, StringComparer.OrdinalIgnoreCase)
                                      .Sum(g => g.Max(x => x.Venta));
            var totalGastos   = totalDejada + totalComision;

            var transportDefs = TransportSheetDefinitions()
                .Where(d => d.SheetName is not ("ACAR" or "MC" or "TUR"))
                .ToList();

            var sb = new StringBuilder();
            var r = 1;

            // ── Fila 1: fecha | totales numéricos | etiquetas de columnas monetarias ──
            // Fondo amarillo en toda la fila  (estilo 10 = amarillo+bold)
            var fi = fechaInicio ?? DateTime.Today;
            var ff = fechaFin ?? fi;
            var fecha = fi.Date == ff.Date
                ? fi.ToString("dd/MM/yyyy")
                : $"{fi:dd/MM/yyyy} al {ff:dd/MM/yyyy}";
            sb.Append($"<row r=\"{r}\" ht=\"20\" customHeight=\"1\">");
            sb.Append(TextCell($"B{r}", fecha, 10));
            sb.Append(NumberCell($"C{r}", totalPax, 10));
            sb.Append(NumberCell($"D{r}", totalEntraron, 10));
            sb.Append(NumberCell($"E{r}", totalSalieron, 10));
            sb.Append(NumberCell($"F{r}", totalUnidades, 10));
            sb.Append(TextCell($"G{r}", "DEJADA", 10));
            sb.Append(TextCell($"H{r}", "COMISION", 10));
            sb.Append(TextCell($"I{r}", "VENTA", 10));
            sb.Append(TextCell($"J{r}", "TOTAL DE GASTOS", 10));
            sb.Append(TextCell($"K{r}", "TIKET PROMEDIO", 10));
            sb.Append(TextCell($"L{r}", "PORCENTAJE DE GASTO", 10));
            sb.AppendLine("</row>");
            r++;

            // ── Fila 2: sub-etiquetas PAX / ENTRARON / SE FUERON / UNIDADES ──
            sb.Append($"<row r=\"{r}\" ht=\"14\" customHeight=\"1\">");
            sb.Append(TextCell($"B{r}", "", 10));
            sb.Append(TextCell($"C{r}", "PAX", 10));
            sb.Append(TextCell($"D{r}", "ENTRARON", 10));
            sb.Append(TextCell($"E{r}", "SE FUERON", 10));
            sb.Append(TextCell($"F{r}", "UNIDADES", 10));
            sb.Append(TextCell($"G{r}", "", 10));
            sb.Append(TextCell($"H{r}", "", 10));
            sb.Append(TextCell($"I{r}", "", 10));
            sb.Append(TextCell($"J{r}", "", 10));
            sb.Append(TextCell($"K{r}", "", 10));
            sb.Append(TextCell($"L{r}", "", 10));
            sb.AppendLine("</row>");
            r++;

            // ── Filas por tipo de transporte (taxistas) ──
            // Mostramos todos aunque tengan cero, igual que la imagen
            foreach (var def in transportDefs)
            {
                var tr = allRows.Where(def.Match).ToList();
                var pax      = tr.Sum(x => x.Pax);
                var seFueron = tr.Count(x => x.Dejada > 0m && x.DejadaPagada <= 0m);
                var entraron = pax - seFueron;
                var unidades = tr.Select(x => FirstText(x.Unidad, x.Placas, x.Gafete, x.TaxistaId.ToString()))
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                var dejada   = tr.Sum(x => x.Dejada);
                var comision = tr.Sum(x => x.Comision);
                var venta    = tr.GroupBy(ReportSaleKey, StringComparer.OrdinalIgnoreCase)
                                 .Sum(g => g.Max(x => x.Venta));
                var gastos   = dejada + comision;

                sb.Append($"<row r=\"{r}\">");
                sb.Append(TextCell($"B{r}", def.DisplayName, 3));
                sb.Append(NumberCell($"C{r}", pax, 17));
                sb.Append(NumberCell($"D{r}", entraron, 17));
                sb.Append(NumberCell($"E{r}", seFueron, 17));
                sb.Append(NumberCell($"F{r}", unidades, 17));
                sb.Append(CuadreCurrencyCell($"G{r}", dejada));
                sb.Append(CuadreCurrencyCell($"H{r}", comision));
                sb.Append(CuadreCurrencyCell($"I{r}", venta));
                sb.Append(CuadreCurrencyCell($"J{r}", gastos));
                // Ticket promedio: solo si hay pax y venta
                sb.Append(pax > 0 && venta > 0
                    ? NumberCell($"K{r}", venta / pax, 13)
                    : TextCell($"K{r}", "$ -", 3));
                // % gasto: solo si hay venta
                sb.Append(venta > 0m
                    ? NumberCell($"L{r}", gastos / venta, 14)
                    : TextCell($"L{r}", "-", 3));
                sb.AppendLine("</row>");
                r++;
            }

            // ── Fila subtotal taxistas (amarillo) ──
            sb.Append($"<row r=\"{r}\" ht=\"16\" customHeight=\"1\">");
            sb.Append(TextCell($"B{r}", "", 10));
            sb.Append(NumberCell($"C{r}", totalPax, 10));
            sb.Append(NumberCell($"D{r}", totalEntraron, 10));
            sb.Append(NumberCell($"E{r}", totalSalieron, 10));
            sb.Append(NumberCell($"F{r}", totalUnidades, 10));
            sb.Append(NumberCell($"G{r}", totalDejada, 11));
            sb.Append(NumberCell($"H{r}", totalComision, 11));
            sb.Append(NumberCell($"I{r}", totalVenta, 11));
            sb.Append(NumberCell($"J{r}", totalGastos, 11));
            sb.Append(totalPax > 0 && totalVenta > 0
                ? NumberCell($"K{r}", totalVenta / totalPax, 11)
                : TextCell($"K{r}", "$ -", 10));
            sb.Append(totalVenta > 0m
                ? NumberCell($"L{r}", totalGastos / totalVenta, 12)
                : TextCell($"L{r}", "-", 10));
            sb.AppendLine("</row>");
            r++;

            // ── Fila vacía separadora ──
            sb.AppendLine($"<row r=\"{r}\"></row>");
            r++;

            // ── Filas de camiones (datos de tabla dejadas MKT) ──
            var camTotal = new CuadreCamionesSummary();
            foreach (var cam in camiones)
            {
                sb.Append($"<row r=\"{r}\">");
                sb.Append(TextCell($"B{r}", cam.Nombre, 3));
                sb.Append(NumberCell($"C{r}", cam.Pax, 17));
                sb.Append(NumberCell($"D{r}", cam.Pax - cam.Salieron, 17));
                sb.Append(NumberCell($"E{r}", cam.Salieron, 17));
                sb.Append(NumberCell($"F{r}", cam.Unidades, 17));
                sb.Append(CuadreCurrencyCell($"G{r}", cam.Dejada));
                sb.Append(TextCell($"H{r}", "$ -", 3));
                sb.Append(TextCell($"I{r}", "$ -", 3));
                sb.Append(TextCell($"J{r}", "", 3));
                sb.Append(TextCell($"K{r}", "$ -", 3));
                sb.Append(TextCell($"L{r}", "-", 3));
                sb.AppendLine("</row>");
                r++;
                camTotal.Pax      += cam.Pax;
                camTotal.Entraron += cam.Entraron;
                camTotal.Salieron += cam.Salieron;
                camTotal.Unidades += cam.Unidades;
                camTotal.Dejada   += cam.Dejada;
            }

            if (camiones.Count == 0)
            {
                sb.Append($"<row r=\"{r}\">");
                sb.Append(TextCell($"B{r}", "Sin registros de camiones", 3));
                sb.AppendLine("</row>");
                r++;
            }

            // ── Fila subtotal camiones (amarillo) ──
            sb.Append($"<row r=\"{r}\" ht=\"16\" customHeight=\"1\">");
            sb.Append(TextCell($"B{r}", "", 10));
            sb.Append(NumberCell($"C{r}", camTotal.Pax, 10));
            sb.Append(NumberCell($"D{r}", camTotal.Pax - camTotal.Salieron, 10));
            sb.Append(NumberCell($"E{r}", camTotal.Salieron, 10));
            sb.Append(NumberCell($"F{r}", camTotal.Unidades, 10));
            sb.Append(NumberCell($"G{r}", camTotal.Dejada, 11));
            sb.Append(TextCell($"H{r}", "$ -", 10));
            sb.Append(TextCell($"I{r}", "$ -", 10));
            sb.Append(TextCell($"J{r}", "$ -", 10));
            sb.Append(TextCell($"K{r}", "-", 10));
            sb.Append(TextCell($"L{r}", "-", 10));
            sb.AppendLine("</row>");
            r++;

            var lastRow = r - 1;
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B1:L{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"><selection activeCell="B1" sqref="B1"/></sheetView></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="2" width="24" customWidth="1"/>
    <col min="3" max="6" width="12" customWidth="1"/>
    <col min="7" max="11" width="16" customWidth="1"/>
    <col min="12" max="12" width="20" customWidth="1"/>
  </cols>
  <sheetData>
{{sb}}
  </sheetData>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        // Celda de moneda: muestra "$ -" si valor es cero, formato $ si tiene valor
        private static string CuadreCurrencyCell(string cellRef, decimal value) =>
            value == 0m
                ? TextCell(cellRef, "$ -", 3)
                : NumberCell(cellRef, value, 13);

        private static string BuildReporteCorteFinalWorksheet(IReadOnlyCollection<PosRelacionRowViewModel> rows, DateTime? fechaInicio, DateTime? fechaFin)
        {
            var dias = BuildDailyCategoryRows(rows, "GENERAL")
                .OrderBy(x => x.Fecha)
                .ToList();
            var sheetRows = new StringBuilder();
            sheetRows.AppendLine("<row r=\"1\">" +
                                 TextCell("B1", "FECHA", 1) +
                                 TextCell("C1", "PAX", 1) +
                                 TextCell("D1", "ENTRARON", 1) +
                                 TextCell("E1", "SE FUERON", 1) +
                                 TextCell("F1", "UNIDADES", 1) +
                                 TextCell("G1", "DEJADA", 1) +
                                 TextCell("H1", "COMISION", 1) +
                                 TextCell("I1", "VENTA", 1) +
                                 TextCell("J1", "TOTAL GASTOS", 1) +
                                 TextCell("K1", "TIKET PROMEDIO", 1) +
                                 TextCell("L1", "PORCENTAJE GASTO", 1) +
                                 "</row>");
            var rowNumber = 2;
            foreach (var dia in dias)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(NumberCell($"B{rowNumber}", (decimal)dia.Fecha.ToOADate(), 4));
                sheetRows.Append(NumberCell($"C{rowNumber}", dia.Pax, 3));
                sheetRows.Append(NumberCell($"D{rowNumber}", dia.Entraron, 3));
                sheetRows.Append(NumberCell($"E{rowNumber}", dia.Salieron, 3));
                sheetRows.Append(NumberCell($"F{rowNumber}", dia.Unidades, 3));
                sheetRows.Append(NumberCell($"G{rowNumber}", dia.Dejada, 3));
                sheetRows.Append(NumberCell($"H{rowNumber}", dia.Comision, 3));
                sheetRows.Append(NumberCell($"I{rowNumber}", dia.Venta, 3));
                sheetRows.Append(NumberCell($"J{rowNumber}", dia.TotalGastos, 3));
                sheetRows.Append(NumberCell($"K{rowNumber}", dia.TicketPromedio, 3));
                sheetRows.Append(NumberCell($"L{rowNumber}", dia.PorcentajeGasto, 6));
                sheetRows.AppendLine("</row>");
                rowNumber++;
            }

            if (dias.Count == 0)
            {
                sheetRows.Append($"<row r=\"{rowNumber}\">");
                sheetRows.Append(TextCell($"B{rowNumber}", "Sin datos para este rango", 3));
                sheetRows.AppendLine("</row>");
            }

            var lastRow = Math.Max(rowNumber - 1, 2);
            return $$"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <dimension ref="B1:L{{lastRow}}"/>
  <sheetViews><sheetView workbookViewId="0"/></sheetViews>
  <sheetFormatPr defaultRowHeight="15"/>
  <cols>
    <col min="2" max="2" width="14" customWidth="1"/>
    <col min="3" max="12" width="14" customWidth="1"/>
  </cols>
  <sheetData>
{{sheetRows}}
  </sheetData>
  <autoFilter ref="B1:L{{lastRow}}"/>
  <pageMargins left="0.5" right="0.5" top="0.75" bottom="0.75" header="0.3" footer="0.3"/>
</worksheet>
""";
        }

        private static byte[] BuildWorkbookWithSheets(IReadOnlyList<ExcelSheetSpec> sheets)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var contentTypes = new StringBuilder("""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
""");
                for (var i = 0; i < sheets.Count; i++)
                    contentTypes.AppendLine($"  <Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                contentTypes.Append("</Types>");

                var workbookRels = new StringBuilder("""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
""");
                for (var i = 0; i < sheets.Count; i++)
                    workbookRels.AppendLine($"  <Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                workbookRels.AppendLine($"  <Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
                workbookRels.Append("</Relationships>");

                var workbook = new StringBuilder("""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
""");
                for (var i = 0; i < sheets.Count; i++)
                    workbook.AppendLine($"    <sheet name=\"{EscapeXml(sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                workbook.Append("""
  </sheets>
</workbook>
""");

                AddZipEntry(archive, "[Content_Types].xml", contentTypes.ToString());
                AddZipEntry(archive, "_rels/.rels", """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
                AddZipEntry(archive, "xl/_rels/workbook.xml.rels", workbookRels.ToString());
                AddZipEntry(archive, "xl/workbook.xml", workbook.ToString());
                AddZipEntry(archive, "xl/styles.xml", BuildReporteTaxiStyles());
                for (var i = 0; i < sheets.Count; i++)
                    AddZipEntry(archive, $"xl/worksheets/sheet{i + 1}.xml", sheets[i].WorksheetXml);
            }

            return stream.ToArray();
        }

        private static List<DailyCategorySummary> BuildDailyCategoryRows(IEnumerable<PosRelacionRowViewModel> sourceRows, string categoria)
        {
            return sourceRows
                .Where(x => TryParseReportDate(x.Fecha).HasValue)
                .GroupBy(x => TryParseReportDate(x.Fecha)!.Value.Date)
                .Select(x =>
                {
                    var pax = x.Sum(item => item.Pax);
                    var entraron = x.Count();
                    var seFueron = x.Count(item => item.Dejada > 0m && item.DejadaPagada <= 0m);
                    var unidades = x.Select(item => FirstText(item.Unidad, item.Placas, item.Gafete, item.TaxistaId.ToString())).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    var dejada = x.Sum(item => item.Dejada);
                    var comision = x.Sum(item => item.Comision);
                    var venta = x
                        .GroupBy(ReportSaleKey, StringComparer.OrdinalIgnoreCase)
                        .Sum(group => group.Max(item => item.Venta));
                    var totalGastos = dejada + comision;
                    var ticketPromedio = pax > 0 ? venta / pax : 0m;
                    var porcentajeGasto = venta > 0m ? totalGastos / venta : 0m;
                    return new DailyCategorySummary(x.Key, categoria, pax, entraron, seFueron, unidades, dejada, comision, venta, totalGastos, ticketPromedio, porcentajeGasto);
                })
                .ToList();
        }

        private static string ReportSaleKey(PosRelacionRowViewModel row)
        {
            var fecha = TryParseReportDate(row.Fecha)?.Date;
            var fechaKey = fecha.HasValue
                ? fecha.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                : (row.Fecha ?? string.Empty).Trim();
            var id = FirstText(row.FolioOperacion, row.FolioControl, row.FolioApp, row.FolioPos, row.TicketPagoDejada);
            if (!string.IsNullOrWhiteSpace(id))
                return $"{fechaKey}|{NormalizeReportToken(id)}";

            return $"{fechaKey}|{NormalizeReportToken(row.Hotel)}|{NormalizeReportToken(row.TaxistaNombre)}|{NormalizeReportToken(row.Gafete)}|{row.Venta:0.00}";
        }

        private static string NormalizeReportToken(string? value)
        {
            var text = (value ?? string.Empty).Trim();
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : text.ToUpperInvariant();
        }

        private static IReadOnlyList<TransportSheetDefinition> TransportSheetDefinitions() =>
            new List<TransportSheetDefinition>
            {
                // TAXI, TAXIZH, TAXIVG, TAXIV, TAXI VERDE, TAXI  VERD → TAXIS VERDES
                // Se excluyen ROJO, ALIAN para no solapar con otras categorías
                new("VER", "TAXIS VERDES", x =>
                    (ContainsAny(x.TransporteTipo, "TAXI") || ContainsAny(x.TransporteTipo, "TAXIZH"))
                    && !ContainsAny(x.TransporteTipo, "ROJO", "ALIAN", "ALIANZA")),
                new("ROJ", "TAXIS ROJOS",   x => ContainsAny(x.TransporteTipo, "ROJO")),
                new("AZU", "TAXIS AZUL",    x => ContainsAny(x.TransporteTipo, "AZUL")),
                new("CAFE", "TAXIS CAFÉ",   x => ContainsAny(x.TransporteTipo, "CAFE")),
                new("UBER", "UBER",         x => ContainsAny(x.TransporteTipo, "UBER") && !ContainsAny(x.TransporteTipo, "ALIANZA", "ALIAN")),
                new("ALI", "UBER ALIANZA",  x => ContainsAny(x.TransporteTipo, "ALIANZA", "ALIAN")),
                new("MAJ", "MAJESTIC",      x => ContainsAny(x.TransporteTipo, "MAJESTIC", "MAESTIC")),
                new("SALAN", "SALMORAN",    x => ContainsAny(x.TransporteTipo, "SALMORAN")),
                // TURIBUS, TURIBUS AD, TURIBUS SA → TURIBUS ADO
                new("TADO", "TURIBUS ADO",  x => ContainsAny(x.TransporteTipo, "TURIBUS", "ADO")),
                new("TEXP", "TRAVEL EXPERIENCE", x => ContainsAny(x.TransporteTipo, "TRAVEL", "EXPERIENCE")),
                // CALLE, GUIA → CALLE
                new("CALLE", "CALLE",       x => ContainsAny(x.TransporteTipo, "CALLE", "GUIA")),
                // VAN, VAN VERDE, VAN4, VANE, VANN, VANTR, VANZH, VAN ISLA, VAN TRANSP, VAN BLANCA, TRANSPORTA → TRANSPORTADORAS
                new("VANS", "TRANSPORTADORAS", x => ContainsAny(x.TransporteTipo, "VAN", "TRANSPORT", "VANE", "VANS", "TULAKA")),
                new("ACAR", "AUTOCAR",      x => ContainsAny(x.TransporteTipo, "AUTOCAR")),
                new("MC",   "MAYA CARIBE",  x => ContainsAny(x.TransporteTipo, "MAYA CARIBE")),
                new("TUR",  "TURICUN",      x => ContainsAny(x.TransporteTipo, "TURICUN"))
            };

        private static bool ContainsAny(string? source, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(source))
                return false;
            return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveReportCommissionStatus(decimal comision, decimal pago)
        {
            if (comision == 0m)
                return "SIN CALCULAR";

            if (pago > 0m && pago >= comision)
                return "PAGADA";

            return "PENDIENTE";
        }

        private sealed record ExcelSheetSpec(string Name, string WorksheetXml);
        private sealed record DailyCategorySummary(DateTime Fecha, string Categoria, int Pax, int Entraron, int Salieron, int Unidades, decimal Dejada, decimal Comision, decimal Venta, decimal TotalGastos, decimal TicketPromedio, decimal PorcentajeGasto);
        private sealed record TransportSheetDefinition(string SheetName, string DisplayName, Func<PosRelacionRowViewModel, bool> Match);

        private static string FirstText(params string?[] values) =>
            values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

        private static readonly CultureInfo ReportDateCulture =
            CultureInfo.GetCultureInfo("es-MX");

        private static readonly string[] ReportDateFormats =
        {
            "dd/MM/yyyy HH:mm",
            "dd/MM/yyyy H:mm",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy hh:mm tt",
            "dd/MM/yyyy"
        };

        private static DateTime? TryParseReportDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var text = value.Trim();
            if (DateTime.TryParseExact(text, ReportDateFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var exact))
                return exact;
            if (DateTime.TryParse(text, ReportDateCulture,
                    DateTimeStyles.None, out var esmx))
                return esmx;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var inv)
                ? inv
                : null;
        }

        private static List<PosOperacionRowViewModel> FilterReportOperations(
            IEnumerable<PosOperacionRowViewModel> operaciones,
            DateTime? fechaInicio,
            DateTime? fechaFin)
        {
            var inicio = fechaInicio?.Date;
            var fin = fechaFin?.Date;

            return operaciones
                .Where(op =>
                {
                    var fecha = TryParseReportDate(op.Hora)?.Date;
                    if (!fecha.HasValue)
                        return !inicio.HasValue && !fin.HasValue;
                    if (inicio.HasValue && fecha.Value < inicio.Value)
                        return false;
                    if (fin.HasValue && fecha.Value > fin.Value)
                        return false;
                    return true;
                })
                .OrderByDescending(op => TryParseReportDate(op.Hora) ?? DateTime.MinValue)
                .ToList();
        }

        private static string TextCell(string cell, string? value, int style) =>
            $"<c r=\"{cell}\" s=\"{style}\" t=\"inlineStr\"><is><t>{EscapeXml(value)}</t></is></c>";

        private static string NumberCell(string cell, decimal value, int style) =>
            $"<c r=\"{cell}\" s=\"{style}\"><v>{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";

        private static string EscapeXml(string? value) =>
            (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");

        private static void AddZipEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        private HashSet<string> GetPermissions()
        {
            var permissions = (HttpContext.Session.GetString("PosPermissions") ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (permissions.Contains("Reportes"))
            {
                permissions.Add("ReporteTaxis");
                permissions.Add("ControlDejadas");
                permissions.Add("DejadasComisiones");
                permissions.Add("ConcentradoGeneral");
            }

            return permissions;
        }

        private static HashSet<string> ExpandPermissions(IEnumerable<string> permisos)
        {
            var expanded = permisos
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (expanded.Contains("Reportes"))
            {
                expanded.Add("ReporteTaxis");
                expanded.Add("ControlDejadas");
                expanded.Add("DejadasComisiones");
                expanded.Add("ConcentradoGeneral");
            }

            return expanded;
        }

        private bool HasModulePermission(string module) => GetPermissions().Contains(module);

        private string CurrentUser() =>
            HttpContext.Session.GetString("PosUser") ?? "WEB";

        private List<PosVentaDetalleViewModel> GetVentaCart()
        {
            var raw = HttpContext.Session.GetString("PosVentaCart");
            return string.IsNullOrWhiteSpace(raw)
                ? new List<PosVentaDetalleViewModel>()
                : JsonSerializer.Deserialize<List<PosVentaDetalleViewModel>>(raw) ?? new List<PosVentaDetalleViewModel>();
        }

        private void SaveVentaCart(List<PosVentaDetalleViewModel> cart, string? productoBusqueda)
        {
            HttpContext.Session.SetString("PosVentaCart", JsonSerializer.Serialize(cart));
            HttpContext.Session.SetString("PosVentaCartOwner", NormalizeVentaCartOwner(productoBusqueda));
        }

        private void ClearVentaCart()
        {
            HttpContext.Session.Remove("PosVentaCart");
            HttpContext.Session.Remove("PosVentaCartOwner");
        }

        private static string NormalizeVentaCartOwner(string? value) =>
            (value ?? string.Empty).Trim().ToUpperInvariant();

        private static void RecalculateVenta(PosVentasViewModel model)
        {
            model.Subtotal = model.Items.Sum(x => x.Precio);
            model.Iva = model.Items.Sum(x => x.Iva);
            model.Total = model.Items.Sum(x => x.Importe);
        }

        private static void ApplyComisionesPagination(PosComisionesViewModel model, int pagina)
        {
            const int pageSize = 100;
            var totalRows = model.Comisiones.Count;
            var totalPages = Math.Max((int)Math.Ceiling(totalRows / (double)pageSize), 1);
            var page = Math.Clamp(pagina, 1, totalPages);
            model.TotalFilas = totalRows;
            model.TotalPaginas = totalPages;
            model.Pagina = page;
            model.TamanoPagina = pageSize;
            model.Comisiones = model.Comisiones
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }

        private static void ApplyGafetesPagination(PosGafetesViewModel model, int pagina)
        {
            const int pageSize = 50;
            var totalRows = model.Filas.Count;
            var totalPages = Math.Max((int)Math.Ceiling(totalRows / (double)pageSize), 1);
            var page = Math.Clamp(pagina, 1, totalPages);
            model.TotalFilas = totalRows;
            model.TotalPaginas = totalPages;
            model.Pagina = page;
            model.TamanoPagina = pageSize;
            model.Filas = model.Filas
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
        }
    }
}
