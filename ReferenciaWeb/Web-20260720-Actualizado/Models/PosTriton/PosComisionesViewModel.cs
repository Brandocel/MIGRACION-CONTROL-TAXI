using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosComisionesViewModel
    {
        public string FolioOperacion { get; set; } = string.Empty;
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public decimal TotalVenta { get; set; }
        public decimal TotalBase { get; set; }
        public decimal TotalComision { get; set; }
        public decimal TotalPagado { get; set; }
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 100;
        public int TotalFilas { get; set; }
        public int TotalPaginas { get; set; } = 1;
        public List<PosComisionRowViewModel> Comisiones { get; set; } = new();
    }

    public class PosComisionRowViewModel
    {
        public string Folio { get; set; } = string.Empty;
        public string Ticket { get; set; } = string.Empty;
        public string Fecha { get; set; } = string.Empty;
        public string BeneficiarioTipo { get; set; } = string.Empty;
        public string Beneficiario { get; set; } = string.Empty;
        public string Staff { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string NumeroUnidad { get; set; } = string.Empty;
        public string Hotel { get; set; } = string.Empty;
        public string FormaPago { get; set; } = string.Empty;
        public string Vendedor { get; set; } = string.Empty;
        public int Pax { get; set; }
        public string Transporte { get; set; } = string.Empty;
        public decimal Venta { get; set; }
        public decimal VentaArtesania { get; set; }
        public decimal VentaFarmacia { get; set; }
        public decimal VentaTienda { get; set; }
        public decimal VentaJoyeria { get; set; }
        public decimal Compra { get; set; }
        public decimal Joyeria { get; set; }
        public decimal Base { get; set; }
        public decimal Porcentaje { get; set; }
        public decimal Importe { get; set; }
        public decimal Pago { get; set; }
        public string FechaPago { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public decimal DescuentoAplicado { get; set; }
        public decimal DeduccionDejada { get; set; }
        public decimal DeduccionGastos { get; set; }
        public decimal DeduccionDegustacion { get; set; }
        public decimal DeduccionReparacion { get; set; }
        public decimal DeduccionBebidas { get; set; }
        public decimal DeduccionCajasRegalo { get; set; }
    }
}
