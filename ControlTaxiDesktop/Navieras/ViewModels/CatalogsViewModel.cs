using System.Collections.ObjectModel;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.ViewModels;

public sealed class CatalogsViewModel : NavierasViewModelBase
{
    public ObservableCollection<NavierasCatalogItem> CatalogItems { get; } = [];
    public IReadOnlyList<NavierasCompany> Companies { get; set; } = [];
    public IReadOnlyList<NavierasBoat> Boats { get; set; } = [];
    public IReadOnlyList<NavierasDock> Docks { get; set; } = [];
    public IReadOnlyList<NavierasPerson> Guides { get; set; } = [];
    public IReadOnlyList<NavierasPerson> Captains { get; set; } = [];
}
