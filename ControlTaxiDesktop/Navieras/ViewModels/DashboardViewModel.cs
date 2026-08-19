using System.Collections.ObjectModel;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.ViewModels;

public sealed class DashboardViewModel : NavierasViewModelBase
{
    private int _boats;
    private int _services;
    private int _passengers;
    private int _inOperation;
    private int _finished;
    private int _delayed;

    public int Boats { get => _boats; set => SetField(ref _boats, value); }
    public int Services { get => _services; set => SetField(ref _services, value); }
    public int Passengers { get => _passengers; set => SetField(ref _passengers, value); }
    public int InOperation { get => _inOperation; set => SetField(ref _inOperation, value); }
    public int Finished { get => _finished; set => SetField(ref _finished, value); }
    public int Delayed { get => _delayed; set => SetField(ref _delayed, value); }

    public ObservableCollection<NavierasIndicatorItem> Indicators { get; } = [];
    public ObservableCollection<NavierasQuickActionItem> QuickActions { get; } = [];
    public ObservableCollection<NavierasServiceSummary> UpcomingArrivals { get; } = [];
    public ObservableCollection<NavierasShipDayItem> ShipsOfDay { get; } = [];
    public ObservableCollection<NavierasDockMapRow> DockMap { get; } = [];
}
