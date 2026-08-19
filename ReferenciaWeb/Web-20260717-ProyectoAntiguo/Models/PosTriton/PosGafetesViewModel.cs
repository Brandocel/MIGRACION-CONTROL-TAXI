using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosGafetesViewModel
    {
        public int Matricula { get; set; }
        public string Gafete { get; set; } = string.Empty;
        public long? FolioOperacion { get; set; }
        public bool EsEdicion { get; set; }
        public int? MatriculaOriginal { get; set; }
        public string GafeteOriginal { get; set; } = string.Empty;
        public long? FolioOperacionOriginal { get; set; }
        public DateTime? FechaInicio { get; set; }
        public DateTime? FechaFin { get; set; }
        public string SelectedGafete { get; set; } = string.Empty;
        public long? SelectedFolioOperacion { get; set; }
        public string Movimiento { get; set; } = "A";
        public bool SoloSinFecha { get; set; }
        public int Pagina { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int TamanoPagina { get; set; } = 50;
        public int TotalFilas { get; set; }
        public List<List<string>> Filas { get; set; } = new();
    }
}
