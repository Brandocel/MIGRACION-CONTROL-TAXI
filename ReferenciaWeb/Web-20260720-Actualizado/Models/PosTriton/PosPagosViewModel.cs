using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosPagosViewModel
    {
        public string FolioOperacion { get; set; } = string.Empty;
        public string Ticket { get; set; } = string.Empty;
        public string Fecha { get; set; } = string.Empty;
        public decimal TotalVenta { get; set; }
        public decimal TotalComision { get; set; }
        public decimal TotalPagado { get; set; }
        public decimal Saldo => TotalComision - TotalPagado;
        public decimal ImporteCaptura => Saldo > 0 ? Saldo : 0m;
        public List<PosPagoRowViewModel> Pagos { get; set; } = new();
    }
}
