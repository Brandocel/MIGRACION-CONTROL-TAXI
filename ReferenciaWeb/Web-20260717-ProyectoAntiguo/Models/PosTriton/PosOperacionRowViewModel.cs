namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosOperacionRowViewModel
    {
        public string FolioOperacion { get; set; } = string.Empty;
        public string FolioControl { get; set; } = string.Empty;
        public string Fuente { get; set; } = string.Empty;
        public string UsuarioOrigen { get; set; } = string.Empty;
        public string Hora { get; set; } = string.Empty;
        public string Ticket { get; set; } = string.Empty;
        public int CantidadTickets { get; set; }
        public int? CatalogId { get; set; }
        public string Hotel { get; set; } = string.Empty;
        public string LlegadaSucursal { get; set; } = string.Empty;
        public string Gafete { get; set; } = string.Empty;
        public string Origen { get; set; } = string.Empty;
        public string Sitio { get; set; } = string.Empty;
        public string Destino { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string Placas { get; set; } = string.Empty;
        public string ModeloVehiculo { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string TelefonoContacto { get; set; } = string.Empty;
        public string Nacionalidad { get; set; } = string.Empty;
        public string Notas { get; set; } = string.Empty;
        public int Pax { get; set; }
        public string Vendedor { get; set; } = string.Empty;
        public string TipoOperacion { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal Efectivo { get; set; }
        public decimal Tarjeta { get; set; }
        public string PayoutStatus { get; set; } = string.Empty;
        public string PayoutDate { get; set; } = string.Empty;
        public string PayoutUser { get; set; } = string.Empty;
        public string PayoutTicket { get; set; } = string.Empty;
    }
}
