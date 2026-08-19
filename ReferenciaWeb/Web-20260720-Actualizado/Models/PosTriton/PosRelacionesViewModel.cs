namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosRelacionesViewModel
    {
        public string Busqueda { get; set; } = string.Empty;
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string FolioControl { get; set; } = string.Empty;
        public string FolioApp { get; set; } = string.Empty;
        public string Fuente { get; set; } = string.Empty;
        public string UsuarioOrigen { get; set; } = string.Empty;
        public string FolioOperacion { get; set; } = string.Empty;
        public string FolioPos { get; set; } = string.Empty;
        public string FolioOperacionSugerido { get; set; } = string.Empty;
        public string FolioPosSugerido { get; set; } = string.Empty;
        public string Fecha { get; set; } = string.Empty;
        public string Gafete { get; set; } = string.Empty;
        public string Nacionalidad { get; set; } = string.Empty;
        public long TaxistaId { get; set; }
        public string TaxistaNombre { get; set; } = string.Empty;
        public string Vendedor { get; set; } = string.Empty;
        public string TransporteTipo { get; set; } = string.Empty;
        public decimal? Dejada { get; set; }
        public decimal Comision { get; set; }
        public string Observaciones { get; set; } = string.Empty;
        public List<string> Vendedores { get; set; } = new();
        public List<PosRelacionRowViewModel> Relaciones { get; set; } = new();
    }

    public class PosRelacionRowViewModel
    {
        public string FolioControl { get; set; } = string.Empty;
        public string FolioApp { get; set; } = string.Empty;
        public string Fuente { get; set; } = string.Empty;
        public string UsuarioOrigen { get; set; } = string.Empty;
        public string FolioOperacion { get; set; } = string.Empty;
        public string IdStaff { get; set; } = string.Empty;
        public string FolioPos { get; set; } = string.Empty;
        public string FolioOperacionSugerido { get; set; } = string.Empty;
        public string FolioPosSugerido { get; set; } = string.Empty;
        public string Fecha { get; set; } = string.Empty;
        public string Vendedor { get; set; } = string.Empty;
        public string VendedorAsignado { get; set; } = string.Empty;
        public string Hotel { get; set; } = string.Empty;
        public string Origen { get; set; } = string.Empty;
        public string Sitio { get; set; } = string.Empty;
        public string Destino { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string Placas { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public int Pax { get; set; }
        public string Gafete { get; set; } = string.Empty;
        public string Nacionalidad { get; set; } = string.Empty;
        public long TaxistaId { get; set; }
        public string TaxistaNombre { get; set; } = string.Empty;
        public string TransporteTipo { get; set; } = string.Empty;
        public decimal Venta { get; set; }
        public decimal Dejada { get; set; }
        public decimal DejadaPagada { get; set; }
        public decimal Comision { get; set; }
        public decimal Pago { get; set; }
        public string FormaPago { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public string EstatusDejada { get; set; } = string.Empty;
        public string FechaPagoDejada { get; set; } = string.Empty;
        public string UsuarioPagoDejada { get; set; } = string.Empty;
        public string TicketPagoDejada { get; set; } = string.Empty;
        public bool PuedePagarDejada { get; set; }
        public string Observaciones { get; set; } = string.Empty;
    }

    public class PosDejadaTicketViewModel
    {
        public string Ticket { get; set; } = string.Empty;
        public string FolioApp { get; set; } = string.Empty;
        public string FolioControl { get; set; } = string.Empty;
        public string FolioOperacion { get; set; } = string.Empty;
        public string FolioPos { get; set; } = string.Empty;
        public string FechaViaje { get; set; } = string.Empty;
        public string FechaPago { get; set; } = string.Empty;
        public string Taxista { get; set; } = string.Empty;
        public string Gafete { get; set; } = string.Empty;
        public string Nacionalidad { get; set; } = string.Empty;
        public string Transporte { get; set; } = string.Empty;
        public string Hotel { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string Placas { get; set; } = string.Empty;
        public string Destino { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public int Pax { get; set; }
        public decimal Importe { get; set; }
        public string Usuario { get; set; } = string.Empty;
        public string Estatus { get; set; } = string.Empty;
        public string Busqueda { get; set; } = string.Empty;
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string Vendedor { get; set; } = string.Empty;
        public string TicketTexto { get; set; } = string.Empty;
    }

    public class CuadreCamionesSummary
    {
        public string Nombre { get; set; } = string.Empty;
        public int Pax { get; set; }
        public int Entraron { get; set; }
        public int Salieron { get; set; }
        public int Unidades { get; set; }
        public decimal Dejada { get; set; }
    }
}
