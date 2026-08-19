using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosGastosViewModel
    {
        public string FolioOperacion { get; set; } = string.Empty;
        public decimal ImporteCaptura { get; set; }
        public decimal TotalDia { get; set; }
        public List<List<string>> Filas { get; set; } = new();
    }
}
