using System;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosGafeteRowViewModel
    {
        public string Numero { get; set; } = string.Empty;
        public string Staff { get; set; } = string.Empty;
        public string FolioOperacion { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Nacionalidad { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public DateTime? Regreso { get; set; }
    }
}
