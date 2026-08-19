using System.Collections.Generic;

namespace ControlTaxiWeb.Models.PosTriton
{
    public class PosDashboardViewModel
    {
        public List<PosModuleCardViewModel> Modules { get; set; } = new();
    }
}
