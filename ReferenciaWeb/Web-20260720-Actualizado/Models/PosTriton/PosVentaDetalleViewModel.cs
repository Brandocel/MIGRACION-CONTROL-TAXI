namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosVentaDetalleViewModel
    {
        public int ProductoId { get; set; }
        public int Cantidad { get; set; }
        public string Producto { get; set; } = string.Empty;
        public string Referencia { get; set; } = string.Empty;
        public string Factura { get; set; } = string.Empty;
        public string OrigenVenta { get; set; } = string.Empty;
        public string FolioRegistro { get; set; } = string.Empty;
        public string FechaVenta { get; set; } = string.Empty;
        public string GafetesAsignados { get; set; } = string.Empty;
        public decimal Precio { get; set; }
        public decimal Iva { get; set; }
        public decimal Importe { get; set; }
        public string Departamento { get; set; } = string.Empty;
    }
}
