namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosUsuariosViewModel
    {
        public string UsuarioEdicion { get; set; } = string.Empty;
        public string RolEdicion { get; set; } = "Cajero";
        public string EstatusEdicion { get; set; } = "Activo";
        public List<string> PermisosEdicion { get; set; } = new();
        public List<PosUsuarioRowViewModel> Usuarios { get; set; } = new();
        public List<PosModuloPermisoViewModel> Modulos { get; set; } = PosModuloPermisoViewModel.Catalogo();
    }

    public class PosUsuarioRowViewModel
    {
        public string Usuario { get; set; } = string.Empty;
        public string Rol { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public string FechaAlta { get; set; } = string.Empty;
        public List<string> Permisos { get; set; } = new();
    }

    public class PosModuloPermisoViewModel
    {
        public string Clave { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;

        public static List<PosModuloPermisoViewModel> Catalogo() =>
        [
            new() { Clave = "RegistroDiario", Nombre = "Registro diario" },
            new() { Clave = "Comisiones", Nombre = "Comisiones" },
            new() { Clave = "Transportes", Nombre = "Transportes" },
            new() { Clave = "Guias", Nombre = "Guias" },
            new() { Clave = "Taxistas", Nombre = "Taxistas" },
            new() { Clave = "Gafetes", Nombre = "Gafetes" },
            new() { Clave = "Relaciones", Nombre = "Relacion ticket-taxista" },
            new() { Clave = "Gastos", Nombre = "Gastos" },
            new() { Clave = "Cortes", Nombre = "Cortes" },
            new() { Clave = "Reportes", Nombre = "Reportes" },
            new() { Clave = "ReporteTaxis", Nombre = "Reporte taxis" },
            new() { Clave = "ControlDejadas", Nombre = "Control dejadas" },
            new() { Clave = "DejadasComisiones", Nombre = "Dejadas y comisiones" },
            new() { Clave = "ConcentradoGeneral", Nombre = "Concentrado general" },
            new() { Clave = "Usuarios", Nombre = "Usuarios" }
        ];
    }
}
