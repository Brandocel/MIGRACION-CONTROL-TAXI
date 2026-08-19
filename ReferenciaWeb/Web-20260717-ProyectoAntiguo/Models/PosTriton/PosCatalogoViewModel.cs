using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosCatalogoViewModel
    {
        public string Titulo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public List<string> Columnas { get; set; } = new();
        public List<List<string>> Filas { get; set; } = new();
        public string Busqueda { get; set; } = string.Empty;
        public int Pagina { get; set; } = 1;
        public int TotalPaginas { get; set; } = 1;
        public int TotalFilas { get; set; }
    }
}
