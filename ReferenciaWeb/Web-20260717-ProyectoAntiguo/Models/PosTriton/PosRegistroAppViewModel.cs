using System;
using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosRegistroAppViewModel
    {
        public string FolioOriginal { get; set; } = string.Empty;
        public string FolioGenerado { get; set; } = string.Empty;
        public string SucursalAsignada { get; set; } = "100 - TIENDA PLAZA 28";
        public DateTime FechaOperacion { get; set; } = DateTime.Now;
        public string TaxistaNombre { get; set; } = string.Empty;
        public int? TaxistaId { get; set; }
        public string Gafete { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string TelefonoContacto { get; set; } = string.Empty;
        public string Nacionalidad { get; set; } = string.Empty;
        public string Placas { get; set; } = string.Empty;
        public string Modelo { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string Hotel { get; set; } = string.Empty;
        public string Origen { get; set; } = string.Empty;
        public string Sitio { get; set; } = string.Empty;
        public string Destino { get; set; } = string.Empty;
        public int Pax { get; set; } = 1;
        public string TipoOperacion { get; set; } = "DEJADA";
        public decimal Total { get; set; }
        public string MetodoPago { get; set; } = "Efectivo";
        public string Notas { get; set; } = string.Empty;
        public List<PosRegistroAppTaxistaOption> Taxistas { get; set; } = new();
        public List<PosRegistroAppTarifaOption> Tarifas { get; set; } = new();
        public List<string> Hoteles { get; set; } = new();
        public List<PosOperacionRowViewModel> Recientes { get; set; } = new();
    }
}
