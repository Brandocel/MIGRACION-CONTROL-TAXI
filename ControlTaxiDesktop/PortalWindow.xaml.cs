using System.Globalization;
using System.Windows;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;

namespace ControlTaxiDesktop;

public partial class PortalWindow : Window
{
    private readonly LocalPortalRepository _portal;
    private readonly LocalErrorLogger _errors;
    private readonly string _userName;

    public PortalWindow(LocalDatabase database, string userName)
    {
        _portal = new LocalPortalRepository(database);
        _errors = new LocalErrorLogger(database);
        _userName = userName;
        InitializeComponent();
        WorkDatePicker.SelectedDate = DateTime.Today;
        SaleDatePicker.SelectedDate = DateTime.Today;
        UserBox.Text = userName;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var source = SelectedDatabase();
            var workDate = WorkDatePicker.SelectedDate ?? DateTime.Today;
            var query = SearchBox.Text;
            var metrics = await _portal.GetDashboardAsync(source, workDate);
            OperationsCountText.Text = metrics.OperationsCount.ToString("N0", CultureInfo.CurrentCulture);
            TicketsCountText.Text = metrics.TicketsCount.ToString("N0", CultureInfo.CurrentCulture);
            ArtesaniasText.Text = Money(metrics.ArtesaniasTotal);
            JoyeriaText.Text = Money(metrics.JoyeriaTotal);
            PaymentsText.Text = Money(metrics.PaymentsTotal);
            ExpensesText.Text = Money(metrics.ExpensesTotal);

            OperationsGrid.ItemsSource = await _portal.GetOperationsAsync(source, workDate, query);
            CommissionsGrid.ItemsSource = await _portal.GetCommissionsAsync(source, query);
            VendorsGrid.ItemsSource = await _portal.GetVendorsAsync(source, query);
            ProductsGrid.ItemsSource = await _portal.GetProductsAsync(source, query);
            GuidesGrid.ItemsSource = await _portal.GetGuidesAsync(source);
            TransportsGrid.ItemsSource = await _portal.GetTransportsAsync(source);
        }
        catch (Exception ex)
        {
            await _errors.LogAsync(_userName, "Portal", "Actualizar", ex);
            WebDialogWindow.Show(this, "No se pudo cargar el Portal local. " + ex.Message, "Control Taxi", "!");
        }
    }

    private async void CreateOperation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CreateMessageText.Text = string.Empty;
            var input = new LocalPortalCreateOperationInput(
                SelectedDatabase(),
                SellerBox.Text,
                HotelBox.Text,
                OperationTypeBox.Text,
                SaleDatePicker.SelectedDate ?? DateTime.Today,
                UserBox.Text,
                GuideBox.Text,
                TransportBox.Text,
                ParseInt(PassengerBox.Text, "Pasajeros"),
                NotesBox.Text,
                ParseDecimal(SubtotalBox.Text, "Subtotal"),
                ParseDecimal(TaxBox.Text, "IVA"),
                ParseDecimal(CashBox.Text, "Efectivo"),
                ParseDecimal(CardBox.Text, "Tarjeta"),
                ParseDecimal(DollarsBox.Text, "Dólares"),
                ParseDecimal(ExchangeRateBox.Text, "Tipo de cambio"));
            var folio = await _portal.CreateOperationAsync(input, _userName);
            CreateMessageText.Text = "Operación local guardada: " + folio;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await _errors.LogAsync(_userName, "Portal", "Crear operación", ex);
            WebDialogWindow.Show(this, ex.Message, "Control Taxi", "!");
        }
    }

    private LocalPortalDatabase SelectedDatabase() =>
        DatabaseBox.SelectedIndex == 1 ? LocalPortalDatabase.JoyeriaPlaza : LocalPortalDatabase.CompuadmoPlaza;

    private static int ParseInt(string value, string field)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var result)) return result;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)) return result;
        throw new ArgumentException($"{field} debe ser un número entero válido.");
    }

    private static decimal ParseDecimal(string value, string field)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var result)) return result;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)) return result;
        throw new ArgumentException($"{field} debe ser un importe válido.");
    }

    private static string Money(decimal value) => value.ToString("C2", CultureInfo.CurrentCulture);
}
