using System.Globalization;
using System.Windows;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Views;

public partial class NavierasDepartureWindow : Window
{
    public NavierasDepartureWindow(NavierasServiceSummary service)
    {
        InitializeComponent();
        DepartureTextBox.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        PaxTextBox.Text = (service.RealPax ?? service.ScheduledPax).ToString(CultureInfo.InvariantCulture);
        Request = new NavierasDepartureRequest(service.ServiceId, DateTimeOffset.Now, service.RealPax ?? service.ScheduledPax, string.Empty);
    }

    public NavierasDepartureRequest? Request { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!DateTimeOffset.TryParse(DepartureTextBox.Text, out var departure))
        {
            MessageBox.Show(this, "La fecha/hora de salida no es valida.", "Navieras");
            return;
        }
        if (!int.TryParse(PaxTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pax))
        {
            MessageBox.Show(this, "La cantidad de pasajeros no es valida.", "Navieras");
            return;
        }
        Request = (Request ?? throw new InvalidOperationException("No se pudo preparar la solicitud de salida.")) with
        {
            RealDeparture = departure,
            DeparturePax = pax,
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
