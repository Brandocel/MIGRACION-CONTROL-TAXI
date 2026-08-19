using System.Globalization;
using System.Windows;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Views;

public partial class NavierasServiceEditorWindow : Window
{
    private readonly IReadOnlyList<NavierasBoat> _boats;

    public NavierasServiceEditorWindow(
        IReadOnlyList<NavierasCompany> companies,
        IReadOnlyList<NavierasBoat> boats,
        IReadOnlyList<NavierasDock> docks,
        IReadOnlyList<NavierasPerson> guides,
        IReadOnlyList<NavierasPerson> captains,
        NavierasServiceSummary? current = null)
    {
        _boats = boats;
        InitializeComponent();
        HeaderTextBlock.Text = current is null ? "Nuevo servicio" : "Editar servicio";
        CompanyComboBox.ItemsSource = companies;
        DockComboBox.ItemsSource = docks;
        GuideComboBox.ItemsSource = guides;
        CaptainComboBox.ItemsSource = captains;
        CompanyComboBox.SelectionChanged += (_, _) => ReloadBoatItems();

        OperationDatePicker.SelectedDate = current?.OperationDate.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        FolioTextBox.Text = current?.ServiceFolio ?? string.Empty;
        BraceletTextBox.Text = current?.BraceletFolio ?? string.Empty;
        ArrivalTextBox.Text = (current?.ScheduledArrival.LocalDateTime ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        DepartureTextBox.Text = (current?.ScheduledDeparture.LocalDateTime ?? DateTime.Now.AddHours(2)).ToString("yyyy-MM-dd HH:mm");
        PaxTextBox.Text = (current?.ScheduledPax ?? 0).ToString(CultureInfo.InvariantCulture);
        NotesTextBox.Text = current?.Notes ?? string.Empty;
        CompanyComboBox.SelectedValue = current?.CompanyId;
        ReloadBoatItems();
        BoatComboBox.SelectedValue = current?.BoatId;
        DockComboBox.SelectedValue = current?.ScheduledDockId;
        GuideComboBox.SelectedValue = current?.ScheduledGuideId;
        CaptainComboBox.SelectedValue = current?.ScheduledCaptainId;
        Request = current is null ? null : new NavierasServiceUpsertRequest(
            current.ServiceId,
            current.ServiceFolio,
            current.CompanyId,
            current.CompanyName,
            current.BoatId,
            current.BoatName,
            current.OperationDate,
            current.ScheduledGuideId,
            current.ScheduledCaptainId,
            current.ScheduledCaptainName,
            current.ScheduledDockId,
            current.ScheduledArrival,
            current.ScheduledDeparture,
            current.ScheduledPax,
            current.BraceletFolio ?? string.Empty,
            current.Notes,
            string.Empty);
    }

    public NavierasServiceUpsertRequest? Request { get; private set; }

    private void ReloadBoatItems()
    {
        var companyId = CompanyComboBox.SelectedValue?.ToString();
        BoatComboBox.ItemsSource = string.IsNullOrWhiteSpace(companyId)
            ? _boats
            : _boats.Where(x => string.Equals(x.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ErrorTextBlock.Text = string.Empty;
            var selectedCompany = CompanyComboBox.SelectedItem as NavierasCompany ?? throw new InvalidOperationException("Selecciona la compania.");
            var selectedBoat = BoatComboBox.SelectedItem as NavierasBoat ?? throw new InvalidOperationException("Selecciona el barco.");
            var selectedDock = DockComboBox.SelectedItem as NavierasDock ?? throw new InvalidOperationException("Selecciona el muelle.");
            var selectedGuide = GuideComboBox.SelectedItem as NavierasPerson ?? throw new InvalidOperationException("Selecciona el guia.");
            var selectedCaptain = CaptainComboBox.SelectedItem as NavierasPerson ?? throw new InvalidOperationException("Selecciona el capitan.");
            if (!DateTimeOffset.TryParse(ArrivalTextBox.Text, out var arrival))
                throw new InvalidOperationException("La llegada programada no es valida.");
            if (!DateTimeOffset.TryParse(DepartureTextBox.Text, out var departure))
                throw new InvalidOperationException("La salida programada no es valida.");
            if (!int.TryParse(PaxTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pax))
                throw new InvalidOperationException("La cantidad de pasajeros no es valida.");

            Request = new NavierasServiceUpsertRequest(
                Request?.ServiceId,
                FolioTextBox.Text.Trim().ToUpperInvariant(),
                selectedCompany.CompanyId,
                selectedCompany.Name,
                selectedBoat.BoatId,
                selectedBoat.Name,
                DateOnly.FromDateTime(OperationDatePicker.SelectedDate ?? DateTime.Today),
                selectedGuide.PersonId,
                selectedCaptain.PersonId,
                selectedCaptain.Name,
                selectedDock.DockId,
                arrival,
                departure,
                pax,
                BraceletTextBox.Text.Trim().ToUpperInvariant(),
                NotesTextBox.Text.Trim(),
                selectedBoat.VesselType);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = ex.Message;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
