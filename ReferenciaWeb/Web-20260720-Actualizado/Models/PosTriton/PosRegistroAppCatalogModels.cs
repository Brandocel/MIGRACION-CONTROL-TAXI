namespace ControlTaxiWeb.Models.PosTriton
{
    public sealed class PosRegistroAppTaxistaOption
    {
        public string Clave { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string TransporteTipo { get; set; } = string.Empty;
        public string Unidad { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string Placas { get; set; } = string.Empty;
        public string Modelo { get; set; } = string.Empty;
        public string Nacionalidad { get; set; } = string.Empty;
        public string Hotel { get; set; } = string.Empty;
        public string Sitio { get; set; } = string.Empty;
        public string Destino { get; set; } = string.Empty;
        public string Gafete { get; set; } = string.Empty;
        public decimal SuggestedAmount { get; set; }
        public string Display => string.Join(" | ", new[] { Clave, Nombre, TransporteTipo }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    public sealed class PosRegistroAppTarifaOption
    {
        public string Tipo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public decimal Dejada { get; set; }
        public decimal Minimo { get; set; }
        public decimal Maximo { get; set; }
        public string Display => $"{Tipo} | {Nombre} | {Dejada:C2}";
    }
}
