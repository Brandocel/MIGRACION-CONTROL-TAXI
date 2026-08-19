namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosPagoRowViewModel
    {
        public string Fecha { get; set; } = string.Empty;
        public string FormaPago { get; set; } = string.Empty;
        public decimal Importe { get; set; }
        public string Referencia { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
    }
}
