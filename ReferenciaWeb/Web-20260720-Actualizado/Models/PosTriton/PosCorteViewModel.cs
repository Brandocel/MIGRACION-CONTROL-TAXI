namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosCorteViewModel
    {
        public DateTime Fecha { get; set; } = DateTime.Today;
        public decimal Efectivo { get; set; }
        public decimal Tarjeta { get; set; }
        public decimal Amex { get; set; }
        public decimal Gastos { get; set; }
        public decimal Comisiones { get; set; }
        public decimal TotalDia { get; set; }
        public decimal Diferencia { get; set; }
        public int Movimientos { get; set; }
        public bool Cerrado { get; set; }
    }
}
