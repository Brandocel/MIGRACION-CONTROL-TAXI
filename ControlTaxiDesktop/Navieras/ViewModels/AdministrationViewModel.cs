using System.Collections.ObjectModel;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.ViewModels;

public sealed class AdministrationViewModel : NavierasViewModelBase
{
    public ObservableCollection<NavierasEditableUser> Users { get; } = [];
    public ObservableCollection<NavierasRole> Roles { get; } = [];
}
