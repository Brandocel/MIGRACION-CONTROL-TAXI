using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosReportesViewModel
    {
        public List<PosModuleCardViewModel> Reportes { get; set; } = new();
        public DateTime FechaReporte { get; set; } = DateTime.Today;
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string TipoReporte { get; set; } = "operaciones";
        public List<PosOperacionRowViewModel> Operaciones { get; set; } = new();
        public List<PosComisionRowViewModel> Comisiones { get; set; } = new();
        public List<PosComisionRowViewModel> PagosComisiones { get; set; } = new();
        public decimal TotalDejadas { get; set; }
    }
}
