using System;
using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosRegistroDiarioViewModel
    {
        public string FolioOperacion { get; set; } = string.Empty;
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public DateTime FechaTrabajo { get; set; }
        public string Staff { get; set; } = string.Empty;
        public string Hotel { get; set; } = string.Empty;
        public int Pax { get; set; }
        public decimal TotalEfectivo { get; set; }
        public decimal TotalTarjeta { get; set; }
        public decimal TotalGeneral { get; set; }
        public List<PosOperacionRowViewModel> Operaciones { get; set; } = new();
    }
}
