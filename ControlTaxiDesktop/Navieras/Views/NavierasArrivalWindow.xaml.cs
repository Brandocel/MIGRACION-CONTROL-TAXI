using System.Globalization;
using System.Windows;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Views;

public partial class NavierasArrivalWindow : Window
{
    public NavierasArrivalWindow(
        IReadOnlyList<NavierasDock> docks,
        IReadOnlyList<NavierasPerson> guides,
        IReadOnlyList<NavierasPerson> captains,
        NavierasServiceSummary service)
    {
        InitializeComponent();
        DockComboBox.ItemsSource = docks;
        GuideComboBox.ItemsSource = guides;
        CaptainComboBox.ItemsSource = captains;
        ArrivalTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        PaxTextBox.Text = service.ScheduledPax.ToString(CultureInfo.InvariantCulture);
        DockComboBox.SelectedValue = service.RealDockId ?? service.ScheduledDockId;
        GuideComboBox.SelectedValue = service.RealGuideId ?? service.ScheduledGuideId;
        CaptainComboBox.SelectedValue = service.RealCaptainId ?? service.ScheduledCaptainId;
        Request = new NavierasArrivalRequest(service.ServiceId, DateTimeOffset.Now, service.ScheduledDockId, service.ScheduledGuideId, service.ScheduledCaptainId, service.ScheduledPax, string.Empty);
    }

    public NavierasArrivalRequest? Request { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DateTimeOffset.TryParse(ArrivalTextBox.Text, out var arrival))
        {
            MessageBox.Show(this, "La fecha/hora de llegada no es valida.", "Navieras");
            return;
        }
        if (!int.TryParse(PaxTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pax))
        {
            MessageBox.Show(this, "La cantidad de pasajeros no es valida.", "Navieras");
            return;
        }
        Request = (Request ?? throw new InvalidOperationException("No se pudo preparar la solicitud de llegada.")) with
        {
            RealArrival = arrival,
            RealDockId = DockComboBox.SelectedValue?.ToString() ?? string.Empty,
            RealGuideId = GuideComboBox.SelectedValue?.ToString() ?? string.Empty,
            RealCaptainId = CaptainComboBox.SelectedValue?.ToString() ?? string.Empty,
            RealPax = pax,
            Notes = NotesTextBox.Text.Trim()
        };
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
