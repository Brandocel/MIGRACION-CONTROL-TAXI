using System.Collections.ObjectModel;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.ViewModels;

public sealed class ServicesViewModel : NavierasViewModelBase
{
    public ObservableCollection<NavierasServiceSummary> Services { get; } = [];
}
