using System.Collections.ObjectModel;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.ViewModels;

public sealed class OperationsViewModel : NavierasViewModelBase
{
    public ObservableCollection<NavierasOperationFolio> Folios { get; } = [];
    public ObservableCollection<NavierasTimelineEvent> Timeline { get; } = [];
}
