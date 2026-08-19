using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosVentasViewModel
    {
        public string Vendedor { get; set; } = string.Empty;
        public string Cliente { get; set; } = string.Empty;
        public string Usuario { get; set; } = "WEB";
        public string FolioControl { get; set; } = string.Empty;
        public string FolioApp { get; set; } = string.Empty;
        public string Gafete { get; set; } = string.Empty;
        public string TransporteTipo { get; set; } = "WEB";
        public int GuiaMatricula { get; set; }
        public long TaxistaId { get; set; }
        public string TaxistaNombre { get; set; } = string.Empty;
        public string ProductoBusqueda { get; set; } = string.Empty;
        public int ProductoId { get; set; }
        public int Cantidad { get; set; } = 1;
        public int Pax { get; set; } = 1;
        public decimal Efectivo { get; set; }
        public decimal Tarjeta { get; set; }
        public decimal Dolares { get; set; }
        public string TipoCambio { get; set; } = "19.50";
        public decimal Subtotal { get; set; }
        public decimal Iva { get; set; }
        public decimal Total { get; set; }
        public List<PosVentaDetalleViewModel> Items { get; set; } = new();
        public List<PosVentaDetalleViewModel> ResultadosBusqueda { get; set; } = new();
        public List<string> GafetesActivos { get; set; } = new();
        public List<PosVentaGafeteRelacionViewModel> GafeteVentas { get; set; } = new();
        public string FolioRegistro { get; set; } = string.Empty;
        public string FolioFactura { get; set; } = string.Empty;
        public string OrigenVenta { get; set; } = string.Empty;
    }

    public class PosVentaGafeteRelacionViewModel
    {
        public string Gafete { get; set; } = string.Empty;
        public string Venta { get; set; } = string.Empty;
        public string Referencia { get; set; } = string.Empty;
        public string OrigenVenta { get; set; } = string.Empty;
        public decimal Importe { get; set; }
    }
}
