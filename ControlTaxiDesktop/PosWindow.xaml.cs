using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;

namespace ControlTaxiDesktop;

public partial class PosWindow : Window
{
    private readonly LocalDatabase _database;
    private readonly LocalPosRepository _pos;
    private readonly LocalOperationsRepository _operations;
    private readonly DesktopOutputService _output = new();
    private readonly LocalErrorLogger _errors;
    private readonly string _user;
    private readonly string _branchCode;
    private readonly string? _initialSaleLookup;
    private readonly string? _cascoOperationFolioForSaleLink;
    private readonly bool _startEmpty;
    private BranchConfiguration? _currentBranch;
    private IReadOnlyList<LocalRelation> _cachedReportRelations = Array.Empty<LocalRelation>();

    private bool IsCascoBranch => string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase);
    private IReadOnlyList<LocalOperationsPreviewRow> _cachedOperationPreviewRows = Array.Empty<LocalOperationsPreviewRow>();
    private IReadOnlyList<LocalCommissionPaymentPreviewRow> _cachedCommissionPayments = Array.Empty<LocalCommissionPaymentPreviewRow>();
    private IReadOnlyList<LocalCut> _cachedReportCuts = Array.Empty<LocalCut>();
    private IReadOnlyList<LocalSalesBrowserRow> _salesBrowserRows = Array.Empty<LocalSalesBrowserRow>();
    private IReadOnlyList<LocalSalesTicketRow> _salesTicketRows = Array.Empty<LocalSalesTicketRow>();
    private List<LocalCommissionBrowserRow> _commissionRows = [];
    private List<CommissionSelectionRow> _commissionFilteredRows = [];
    private int _commissionPage = 1;
    private int _reportLoadSequence;
    private const int CommissionPageSize = 20;
    private string? _lastCommissionAutoRefreshKey;
    private DateTime _lastCommissionAutoRefreshAtUtc;
    private bool _windowReady;
    private bool _autoRefreshRunning;
    private readonly DispatcherTimer _cascoAutoRefreshTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private string ReportLogPath => Path.Combine(AppContext.BaseDirectory, "Logs", "report-center-cv.log");
    private string PosLogPath => Path.Combine(AppContext.BaseDirectory, "Logs", "pos-window-cv.log");

    public PosWindow(LocalDatabase database, string user, string branchCode, string? selectedModule = null, string? initialSaleLookup = null, bool startEmpty = false, string? cascoOperationFolioForSaleLink = null)
    {
        _database = database;
        _pos = new LocalPosRepository(database);
        _operations = new LocalOperationsRepository(database);
        _errors = new LocalErrorLogger(database);
        _user = user;
        _branchCode = string.IsNullOrWhiteSpace(branchCode) ? "P28" : branchCode.Trim().ToUpperInvariant();
        _currentBranch = new BranchConfigurationService().GetBranch(_branchCode) ?? new BranchConfigurationService().GetBranch("P28");
        _initialSaleLookup = string.IsNullOrWhiteSpace(initialSaleLookup) ? null : initialSaleLookup.Trim();
        _cascoOperationFolioForSaleLink = string.IsNullOrWhiteSpace(cascoOperationFolioForSaleLink) ? null : cascoOperationFolioForSaleLink.Trim();
        _startEmpty = startEmpty;
        InitializeComponent();
        ApplyWindowBounds();
        ApplySelectedModule(selectedModule);
        PosTabs.SelectionChanged += PosTabs_SelectionChanged;
        _cascoAutoRefreshTimer.Tick += CascoAutoRefreshTimer_Tick;
        CutDate.SelectedDate = DateTime.Today;
        AuditStart.SelectedDate = DateTime.Today.AddDays(-7);
        AuditEnd.SelectedDate = DateTime.Today;
        ReportStart.SelectedDate = DateTime.Today;
        ReportEnd.SelectedDate = DateTime.Today;
        CommissionStartDate.SelectedDate = DateTime.Today;
        CommissionEndDate.SelectedDate = DateTime.Today;
        CommissionPrevPageButton.IsEnabled = false;
        CommissionNextPageButton.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            _windowReady = true;
            await RunAsync(RefreshAsync);
        };
        Closed += (_, _) =>
        {
            _cascoAutoRefreshTimer.Stop();
            CascoBackgroundSyncService.Instance.RecordsChanged -= HandleCascoRecordsChanged;
        };
        CascoBackgroundSyncService.Instance.RecordsChanged += HandleCascoRecordsChanged;
    }

    private void ApplyWindowBounds()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Manual;
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;

        MinWidth = Math.Max(MinWidth, 920);
        MinHeight = Math.Max(MinHeight, 620);

        var workArea = SystemParameters.WorkArea;
        ClearValue(MaxWidthProperty);
        ClearValue(MaxHeightProperty);
        Width = Math.Min(Math.Max(Width, 980), Math.Max(MinWidth, workArea.Width - 24));
        Height = Math.Min(Math.Max(Height, 660), Math.Max(MinHeight, workArea.Height - 24));
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(12, (workArea.Height - Height) / 2);
    }

    private DateTime ReportStartDate => ParsePickerDate(ReportStart, DateTime.Today);
    private DateTime ReportEndDate => ParsePickerDate(ReportEnd, ReportStartDate);

    private void ApplySelectedModule(string? selectedModule)
    {
        var normalized = selectedModule ?? "Ventas";
        PosTabs.SelectedIndex = normalized switch
        {
            "Pagos" => 1,
            "Comisiones" => 2,
            "Cortes" => 3,
            "Reportes" => 4,
            "Auditoria" => 5,
            _ => 0
        };
        TitleBarText.Text = normalized switch
        {
            "Comisiones" => "COMISIONES",
            "Cortes" => "CIERRE",
            "Reportes" => "CENTRO DE REPORTES",
            "Pagos" => "PAGOS",
            "Auditoria" => "AUDITORIA",
            _ => "VENTA"
        };
    }

    private async Task RefreshAsync()
    {
        switch (PosTabs.SelectedIndex)
        {
            case 0:
                if (!string.IsNullOrWhiteSpace(_initialSaleLookup) && string.IsNullOrWhiteSpace(SaleFolio.Text))
                    SaleFolio.Text = _initialSaleLookup;
                await RefreshSalesBrowserAsync();
                break;
            case 1:
                PaymentsGrid.ItemsSource = await _pos.GetPaymentsAsync();
                break;
            case 2:
                await RefreshCommissionBrowserAsync(resetPage: true);
                break;
            case 3:
                await RefreshCutsAsync();
                break;
            case 4:
                await LoadReportCenterAsync();
                break;
            case 5:
                AuditGrid.ItemsSource = await _pos.GetAuditAsync(AuditStart.SelectedDate, AuditEnd.SelectedDate);
                break;
            default:
                if (!string.IsNullOrWhiteSpace(_initialSaleLookup) && string.IsNullOrWhiteSpace(SaleFolio.Text))
                    SaleFolio.Text = _initialSaleLookup;
                await RefreshSalesBrowserAsync();
                break;
        }
    }

    private async void PosTabs_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_windowReady || !IsLoaded)
            return;

        // SelectionChanged burbujea, y los DataGrid de cada pestaña tambien son Selector: al
        // seleccionar una FILA el evento subia hasta aca y recargaba toda la pantalla, lo que
        // ademas limpiaba la seleccion y cerraba el panel de detalle. Solo interesa el cambio
        // de pestaña, es decir el que nace en el propio TabControl.
        if (!ReferenceEquals(e.OriginalSource, PosTabs))
            return;

        await RunAsync(RefreshAsync);
    }

    private async void CascoAutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsCascoBranch || !_windowReady || !IsLoaded || _autoRefreshRunning)
            return;

        try
        {
            _autoRefreshRunning = true;
            if (PosTabs.SelectedIndex == 4)
                await LoadReportCenterAsync();
        }
        catch (Exception ex)
        {
            WritePosLog("No se pudo refrescar automaticamente la vista de Casco.", ex);
        }
        finally
        {
            _autoRefreshRunning = false;
        }
    }

    private async void SaveProduct_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureNotCascoWriteOperation();
        await _pos.SaveProductAsync(new LocalProduct(0, ProductCode.Text, ProductName.Text, Number(ProductPrice.Text), Number(ProductTax.Text), Integer(ProductStock.Text), true), _user);
        await RefreshAsync();
    });

    private async void CreateSale_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (IsCascoBranch)
        {
            await LinkCascoSaleToOperationAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(SaleFolio.Text) && string.IsNullOrWhiteSpace(SaleCustomer.Text))
            throw new ArgumentException("Captura un folio, cliente o usa la busqueda de ventas.");
        await RefreshSalesBrowserAsync();
    });

    private async void RegisterPayment_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureNotCascoWriteOperation();
        var estado = await _pos.RegisterPaymentAsync(PaymentSaleFolio.Text, Number(PaymentAmount.Text), SelectedText(PaymentMethod), PaymentNotes.Text, _user);
        await RefreshAsync();
        var mensaje = string.Equals(estado, "Pagado", StringComparison.OrdinalIgnoreCase)
            ? "Pago registrado exitosamente. Estado: PAGADO."
            : "Pago parcial registrado exitosamente. Estado: " + estado.ToUpperInvariant() + ".";
        WebDialogWindow.Show(this, mensaje, "Control Taxi", "OK");
    });

    private async void RecalculateCommissions_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureNotCascoWriteOperation();
        var affected = await _pos.RecalculateCommissionsAsync(_user);
        _lastCommissionAutoRefreshKey = null;
        WebDialogWindow.Show(this, $"Comisiones recalculadas: {affected:N0}.", "Control Taxi", "OK");
        await RefreshAsync();
    });

    private async void PayCommission_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureNotCascoWriteOperation();
        var row = _commissionFilteredRows.FirstOrDefault(x => string.Equals(x.Source.Folio, CommissionFolio.Text, StringComparison.OrdinalIgnoreCase));
        if (row is null) throw new InvalidOperationException("Selecciona una comision valida.");
        await _pos.PayCommissionAsync(row.Source.Folio, row.Source.Saldo, _user);
        await RefreshCommissionBrowserAsync(resetPage: true);
        WebDialogWindow.Show(this, "Comision pagada exitosamente. Estado: PAGADA.", "Control Taxi", "OK");
    });

    private async void CalculateCut_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureNotCascoWriteOperation();
        await _pos.CalculateCutAsync(CutDate.SelectedDate ?? DateTime.Today, Number(CutCounted.Text), _user);
        await RefreshAsync();
    });

    private async void CloseCut_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureNotCascoWriteOperation();
        await _pos.CloseCutAsync(CutDate.SelectedDate ?? DateTime.Today, _user);
        await RefreshAsync();
    });

    private async void SearchCommissions_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await RefreshCommissionBrowserAsync(resetPage: true);
    });

    // Enter dentro del campo de folio dispara la busqueda, sin tener que ir al boton.
    private async void CommissionFilter_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        await RunAsync(async () => await RefreshCommissionBrowserAsync(resetPage: true));
    }

    private async void ClearCommissionFilters_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        CommissionFolio.Text = string.Empty;
        CommissionStartDate.SelectedDate = DateTime.Today;
        CommissionEndDate.SelectedDate = DateTime.Today;
        await RefreshCommissionBrowserAsync(resetPage: true);
    });

    // Atajos de rango de fechas. Ademas de ahorrar tecleo, empujan al usuario a rangos
    // cortos, que es donde la consulta de comisiones responde rapido.
    private async void CommissionQuickRange_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (sender is not Button button || button.Tag is not string tag) return;
        var today = DateTime.Today;
        var (start, end) = tag switch
        {
            "yesterday" => (today.AddDays(-1), today.AddDays(-1)),
            "week" => (today.AddDays(-6), today),
            "month" => (new DateTime(today.Year, today.Month, 1), today),
            _ => (today, today),
        };
        CommissionFolio.Text = string.Empty;
        CommissionStartDate.SelectedDate = start;
        CommissionEndDate.SelectedDate = end;
        await RefreshCommissionBrowserAsync(resetPage: true);
    });

    private void ToggleCommissionFilters_Click(object sender, RoutedEventArgs e)
    {
        var collapse = CommissionFiltersFieldsPanel.Visibility == Visibility.Visible;
        CommissionFiltersFieldsPanel.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
        CommissionFiltersToggleButton.Content = collapse ? "Mostrar ▼" : "Ocultar ▲";
    }

    // Boton PAGAR de la columna de acciones: paga solo esa fila. Marca la fila como la unica
    // seleccionada y reusa el mismo flujo de pago del boton PAGAR de arriba, para no tener dos
    // caminos distintos que puedan quedar desalineados.
    private async void PaySingleCommission_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (sender is not FrameworkElement { DataContext: CommissionSelectionRow row }) return;
        foreach (var candidate in _commissionFilteredRows) candidate.IsSelected = false;
        row.IsSelected = true;
        CommissionsGrid.SelectedItem = row;
        await PayCommissionsCoreAsync();
    });

    private async void PaySelectedCommissions_Click(object sender, RoutedEventArgs e) => await RunAsync(PayCommissionsCoreAsync);

    private async Task PayCommissionsCoreAsync()
    {
        if (IsCascoBranch)
        {
            await PaySelectedCascoCommissionsAsync();
            return;
        }

        var selected = _commissionFilteredRows
            .Where(x => x.IsSelected && x.Source.PuedePagar)
            .Select(x => x.Source)
            .ToList();

        if (selected.Count == 0 && CommissionsGrid.SelectedItem is CommissionSelectionRow selectedRow && selectedRow.Source.PuedePagar)
            selected.Add(selectedRow.Source);

        selected = selected
            .GroupBy(x => string.IsNullOrWhiteSpace(x.SaleFolio) ? x.Folio : x.SaleFolio, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        if (selected.Count == 0) throw new InvalidOperationException("Selecciona al menos una comision pendiente.");

        var total = selected.Sum(x => x.Saldo);
        var message = selected.Count == 1
            ? $"Deseas pagar la comision del folio {selected[0].Folio}?{Environment.NewLine}Importe: {total:C2}"
            : $"Deseas pagar {selected.Count:N0} comisiones seleccionadas?{Environment.NewLine}Importe total: {total:C2}";
        if (!WebDialogWindow.Confirm(this, message, "PAGAR COMISION", "?", "PAGAR", "CANCELAR"))
            return;

        foreach (var row in selected)
        {
            await _pos.EnsureCommissionSnapshotAsync(row, _user);
            await _pos.PayCommissionAsync(row.Folio, row.Saldo, _user);
        }

        var ticketRows = selected
            .Select(row => row with { Pagado = row.PagoComision, Saldo = 0m, Estatus = "PAGADA" })
            .ToArray();
        await RefreshCommissionBrowserAsync(resetPage: true);
        var preview = new TicketPreviewWindow(string.Join(
            Environment.NewLine + Environment.NewLine,
            ticketRows.Select(BuildCommissionPreviewTicket))) { Owner = this };
        preview.ShowDialog();
        WebDialogWindow.Show(this, $"Se pagaron {selected.Count:N0} comisiones exitosamente. Estado: PAGADA.", "Control Taxi", "OK");
    }

    private async Task PaySelectedCascoCommissionsAsync()
    {
        var selected = _commissionFilteredRows
            .Where(x => x.IsSelected && x.Source.PuedePagar)
            .Select(x => x.Source)
            .ToList();

        if (selected.Count == 0 && CommissionsGrid.SelectedItem is CommissionSelectionRow selectedRow && selectedRow.Source.PuedePagar)
            selected.Add(selectedRow.Source);

        selected = selected
            .GroupBy(x => x.Folio, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        if (selected.Count == 0)
            throw new InvalidOperationException("Selecciona una comision pendiente para pagar.");

        var total = selected.Sum(x => x.Saldo);
        var message = selected.Count == 1
            ? $"Deseas pagar la comision del folio {selected[0].Folio}?{Environment.NewLine}Importe: {total:C2}"
            : $"Deseas pagar {selected.Count:N0} comisiones seleccionadas?{Environment.NewLine}Importe total: {total:C2}";
        if (!WebDialogWindow.Confirm(this, message, "PAGAR COMISION | Casco Viejo", "?", "PAGAR", "CANCELAR"))
            return;

        var branch = new BranchConfigurationService().GetBranch("CV");
        var password = ResolveCascoSqlPassword();
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("No se encontro la credencial SQL de Casco para pagar la comision.");

        var service = new CascoCommissionPersistenceService();
        var results = new List<CascoCommissionPaymentResult>();
        foreach (var row in selected)
        {
            var commissionPreview = await service.PreviewCommissionAsync(branch, password, row.Folio, _user, CancellationToken.None);
            var save = await service.SaveCommissionAsync(branch, password, commissionPreview, CancellationToken.None);
            if (!save.Saved && commissionPreview.ComisionCalculada <= 0m)
                throw new InvalidOperationException("No hay comision pendiente con importe mayor a cero.");

            var result = await service.PayCommissionAsync(branch, password, row.Folio, _user, CancellationToken.None);
            results.Add(result);
        }

        var ticketText = BuildCascoCommissionPaymentTicket(results, selected);
        await RefreshCommissionBrowserAsync(resetPage: true);
        var preview = new TicketPreviewWindow(ticketText) { Owner = this };
        preview.ShowDialog();
        WebDialogWindow.Show(this, "Comision pagada exitosamente. Estado: PAGADA.", "Control Taxi", "OK");
    }

    private static string BuildCascoCommissionPaymentTicket(
        IReadOnlyList<CascoCommissionPaymentResult> payments,
        IReadOnlyList<LocalCommissionBrowserRow> sourceRows)
    {
        const int width = 42;
        static string Line(char value = '-') => new(value, width);
        static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        static string Center(string value)
        {
            value = Clean(value);
            if (value.Length >= width) return value[..width];
            var left = (width - value.Length) / 2;
            return new string(' ', left) + value;
        }
        static string Pair(string label, string value)
        {
            label = Clean(label).ToUpperInvariant();
            value = Clean(value);
            var prefix = $"{label}: ";
            var maxValue = Math.Max(1, width - prefix.Length);
            return prefix + (value.Length > maxValue ? value[..maxValue] : value);
        }

        var lines = new List<string>
        {
            Center("CONTROL TAXI"),
            Center("PAGO DE COMISION"),
            Line()
        };

        foreach (var payment in payments)
        {
            var row = sourceRows.FirstOrDefault(x => string.Equals(x.Folio, payment.FolioOriginal, StringComparison.OrdinalIgnoreCase));
            lines.Add(Pair("Ticket", payment.TicketPago));
            lines.Add(Pair("Folio", payment.FolioOriginal));
            lines.Add(Pair("Ticket POS", payment.TicketPos));
            lines.Add(Pair("Fecha pago", payment.FechaPago.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            lines.Add(Pair("Taxista", payment.Taxista));
            lines.Add(Pair("Vendedor", row?.Vendedor ?? string.Empty));
            lines.Add(Pair("Gafete", payment.Gafete));
            lines.Add(Pair("Transporte", payment.Transporte));
            lines.Add(Pair("Venta", (row?.VentaTotal ?? 0m).ToString("C2", CultureInfo.CurrentCulture)));
            lines.Add(Pair("Comision", payment.Comision.ToString("C2", CultureInfo.CurrentCulture)));
            lines.Add(Pair("Usuario", payment.Usuario));
            lines.Add(Pair("Estatus", payment.Estatus));
            lines.Add(Line());
        }

        lines.Add(Pair("Total pagado", payments.Sum(x => x.Comision).ToString("C2", CultureInfo.CurrentCulture)));
        lines.Add(Center("CONSERVE ESTE COMPROBANTE"));
        lines.Add("________________________");
        lines.Add(Center("FIRMA"));
        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private void PrintCommissions_Click(object sender, RoutedEventArgs e)
    {
        if (_commissionFilteredRows.Count == 0) { WebDialogWindow.Show(this, "No hay comisiones para imprimir.", "Control Taxi", "!"); return; }
        _output.PrintText("Comisiones", BuildCommissionsPrintablePreview(_commissionFilteredRows.Select(x => x.Source).ToArray()));
    }

    private void PrintSingleCommissionTicket_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CommissionSelectionRow row)
            return;

        if (!row.Source.PuedeImprimirTicket)
        {
            WebDialogWindow.Show(this, "El ticket de comision solo esta disponible cuando la comision ya esta pagada.", "Control Taxi", "!");
            return;
        }

        // REIMPRIMIR va directo al printer: solo abre el dialogo de impresion de Windows.
        // Para revisar el ticket antes esta VER TICKET, dentro del panel de detalle.
        TicketPrinting.Print(BuildCommissionPreviewTicket(row.Source), "Ticket comision");
    }

    // VER TICKET (panel de detalle): muestra el ticket sin mandarlo a imprimir.
    private void ShowCommissionTicket_Click(object sender, RoutedEventArgs e)
    {
        if (CommissionsGrid.SelectedItem is not CommissionSelectionRow row) return;
        if (!row.Source.PuedeImprimirTicket)
        {
            WebDialogWindow.Show(this, "El ticket de comision solo esta disponible cuando la comision ya esta pagada.", "Control Taxi", "!");
            return;
        }

        _commissionTicketContent = BuildCommissionPreviewTicket(row.Source);
        CommissionTicketText.Text = _commissionTicketContent;
        CommissionTicketPanel.Visibility = Visibility.Visible;
    }

    private string _commissionTicketContent = string.Empty;

    private void PrintCommissionTicketFromPanel_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_commissionTicketContent)) return;
        TicketPrinting.Print(_commissionTicketContent, "Ticket comision");
    }

    private async void SaveCommissionTicketFromPanel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_commissionTicketContent)) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Ticket texto (*.txt)|*.txt",
            FileName = $"ticket_comision_{DateTime.Now:yyyyMMddHHmmss}.txt"
        };
        if (dialog.ShowDialog() == true)
            await new DesktopOutputService().ExportTextAsync(_commissionTicketContent, dialog.FileName);
    });

    private void CloseCommissionTicketPanel_Click(object sender, RoutedEventArgs e) => ResetCommissionTicketPanel();

    private void ResetCommissionTicketPanel()
    {
        _commissionTicketContent = string.Empty;
        CommissionTicketText.Text = string.Empty;
        CommissionTicketPanel.Visibility = Visibility.Collapsed;
    }

    // Boton DETALLE de la columna de acciones: la unica via para abrir el cajon lateral.
    private void ShowCommissionDetail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CommissionSelectionRow row) return;
        OpenCommissionDetail(row);
    }

    private static string BuildCommissionPreviewTicket(LocalCommissionBrowserRow row)
    {
        const int width = 42;
        static string Line(char value = '-') => new(value, width);
        static string Clean(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        static string Center(string value)
        {
            value = Clean(value);
            if (value.Length >= width) return value[..width];
            var left = (width - value.Length) / 2;
            return new string(' ', left) + value;
        }
        static string Pair(string label, string value)
        {
            label = Clean(label).ToUpperInvariant();
            value = Clean(value);
            var prefix = $"{label}: ";
            var maxValue = Math.Max(1, width - prefix.Length);
            return prefix + (value.Length > maxValue ? value[..maxValue] : value);
        }

        var lines = new[]
        {
            Center("CONTROL TAXI"),
            Center("TICKET DE COMISION"),
            Line(),
            Pair("Folio", row.Folio),
            Pair("Ticket POS", row.Ticket),
            Pair("Fecha", row.Fecha.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            Pair("Taxista", row.Nombre),
            Pair("Vendedor", row.Vendedor),
            Pair("Gafete", row.Gafete),
            Pair("Transporte", row.Unidad),
            Pair("Unidad", row.NumeroUnidad),
            Pair("Pax", row.Pax.ToString(CultureInfo.InvariantCulture)),
            Pair("Hotel", row.Hotel),
            Line(),
            Pair("Venta farmacia", row.VentaFarmacia.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("Venta joyeria", row.VentaJoyeria.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("Venta total", row.VentaTotal.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("Dejada", row.Dejada.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("% comision", row.PorcentajeComision.ToString("P0", CultureInfo.CurrentCulture)),
            Pair("Comision", row.PagoComision.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("Pagado", row.Pagado.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("Saldo", row.Saldo.ToString("C2", CultureInfo.CurrentCulture)),
            Pair("Estatus", row.Estatus),
            Line(),
            Center("COMPROBANTE INFORMATIVO")
        };

        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private void CommissionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CommissionSelectionRow row)
        {
            // Solo se pueden marcar comisiones del MISMO taxista: el pago en bloque emite un
            // ticket por taxista, asi que mezclar dos descuadraria el comprobante.
            if (row.IsSelected)
            {
                var otherDriver = _commissionFilteredRows.FirstOrDefault(x =>
                    x.IsSelected
                    && !ReferenceEquals(x, row)
                    && !string.Equals(x.Source.Nombre?.Trim(), row.Source.Nombre?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (otherDriver is not null)
                {
                    row.IsSelected = false;
                    UpdateCommissionSelectionTotal();
                    WebDialogWindow.Show(
                        this,
                        $"Solo puedes seleccionar comisiones del mismo taxista.{Environment.NewLine}Ya tienes seleccionadas de {otherDriver.Source.Nombre}.",
                        "Control Taxi",
                        "!");
                    return;
                }
            }
            // Marcar la casilla ya NO selecciona la fila: hacerlo abria el panel de detalle
            // encima y tapaba la tabla justo cuando se estaban marcando varias comisiones.
            // El detalle se abre unicamente con el boton DETALLE.
        }

        UpdateCommissionSelectionTotal();
    }
    private async void CommissionPrevPage_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_commissionPage <= 1) return;
        _commissionPage--;
        ApplyCommissionPage();
        await Task.CompletedTask;
    });
    private async void CommissionNextPage_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var totalPages = Math.Max((int)Math.Ceiling(_commissionFilteredRows.Count / (double)CommissionPageSize), 1);
        if (_commissionPage >= totalPages) return;
        _commissionPage++;
        ApplyCommissionPage();
        await Task.CompletedTask;
    });
    // Doble clic en una fila: abre el cajon directamente en la pestaña de la venta completa.
    private void CommissionsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CommissionsGrid.SelectedItem is not CommissionSelectionRow row) return;
        OpenCommissionDetail(row);
        CommissionTabSaleButton.IsChecked = true;
    }

    private async void OpenCommissionSaleDetail_Click(object sender, RoutedEventArgs e) => await RunAsync(ShowCommissionSaleLinesAsync);

    // Cambio de pestaña del cajon de detalle. La venta completa se carga la primera vez que se
    // entra a su pestaña (no al abrir el detalle), para no pegarle a la base sin necesidad.
    private async void CommissionDetailTab_Changed(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (!IsLoaded || CommissionTabGeneralContent is null) return;

        var showSale = CommissionTabSaleButton.IsChecked == true;
        CommissionTabGeneralContent.Visibility = showSale ? Visibility.Collapsed : Visibility.Visible;
        CommissionSaleLinesPanel.Visibility = showSale ? Visibility.Visible : Visibility.Collapsed;

        if (showSale && !_commissionSaleLinesLoaded)
            await ShowCommissionSaleLinesAsync();
    });

    private bool _commissionSaleLinesLoaded;

    // La venta completa se muestra dentro del panel lateral de esta misma pantalla.
    // Antes se abria una segunda PosWindow como dialogo, que sacaba al usuario del contexto
    // de comisiones y lo obligaba a cerrarla para volver.
    private async Task ShowCommissionSaleLinesAsync()
    {
        if (CommissionsGrid.SelectedItem is not CommissionSelectionRow row) return;
        var lookup = string.IsNullOrWhiteSpace(row.Source.Ticket) ? row.Source.SaleFolio : row.Source.Ticket;
        if (string.IsNullOrWhiteSpace(lookup)) return;

        CommissionSaleLinesLoading.Visibility = Visibility.Visible;
        try
        {
            var sale = (await _pos.GetSalesBrowserRowsAsync(lookup)).FirstOrDefault();
            var lines = sale is null
                ? Array.Empty<LocalSalesTicketRow>()
                : (await GetSalesTicketRowsAsync(sale)).ToArray();
            CommissionSaleLines.ItemsSource = lines;
            CommissionSaleLinesEmpty.Visibility = lines.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _commissionSaleLinesLoaded = true;
        }
        finally
        {
            CommissionSaleLinesLoading.Visibility = Visibility.Collapsed;
        }
    }

    private void ResetCommissionSaleLines()
    {
        CommissionSaleLines.ItemsSource = null;
        CommissionSaleLinesEmpty.Visibility = Visibility.Collapsed;
        CommissionSaleLinesLoading.Visibility = Visibility.Collapsed;
        _commissionSaleLinesLoaded = false;

        // Al cambiar de comision se regresa a la pestaña GENERAL: la venta de la fila anterior
        // ya no aplica y dejar abierta esa pestaña mostraria datos que no corresponden.
        if (CommissionTabGeneralButton is not null) CommissionTabGeneralButton.IsChecked = true;
        if (CommissionTabGeneralContent is not null) CommissionTabGeneralContent.Visibility = Visibility.Visible;
        CommissionSaleLinesPanel.Visibility = Visibility.Collapsed;
    }

    private sealed record CommissionDetailField(string Label, string Value);

    // Cambiar de fila ya NO abre el panel: solo refresca su contenido si el panel ya estaba
    // abierto. Abrirlo con cada seleccion tapaba la tabla al marcar casillas o al arrastrar
    // para hacer scroll. El detalle se abre unicamente con el boton DETALLE de la fila.
    private void CommissionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommissionDetailPanel.Visibility != Visibility.Visible) return;

        if (CommissionsGrid.SelectedItem is not CommissionSelectionRow row)
        {
            HideCommissionDetail();
            return;
        }

        LoadCommissionDetail(row);
    }

    private void LoadCommissionDetail(CommissionSelectionRow row)
    {
        // Al cambiar de fila se cierra la venta completa y el ticket de la fila anterior.
        ResetCommissionSaleLines();
        ResetCommissionTicketPanel();

        var s = row.Source;
        CommissionDetailFolio.Text = s.Folio;
        CommissionDetailFields.ItemsSource = new[]
        {
            new CommissionDetailField("Número unidad", s.NumeroUnidad),
            new CommissionDetailField("Pax", s.Pax.ToString(CultureInfo.InvariantCulture)),
            new CommissionDetailField("Ticket", s.Ticket),
            new CommissionDetailField("Venta artesanía", s.VentaArtesania.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Venta farmacia", s.VentaFarmacia.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Venta tienda", s.VentaTienda.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Venta joyería", s.VentaJoyeria.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("% descuento", s.DescuentoPorcentaje.ToString("P0", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Bebidas y cajas regalo", s.BebidasCajasRegalo.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Rep", s.Reparacion.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Degustación", s.Degustacion.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Pagado", s.Pagado.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Saldo", s.Saldo.ToString("C2", CultureInfo.CurrentCulture)),
            // Los dos estatus juntos, para que se vea claro que son pagos distintos.
            new CommissionDetailField("Dejada", s.Dejada.ToString("C2", CultureInfo.CurrentCulture)),
            new CommissionDetailField("Estatus dejada", string.IsNullOrWhiteSpace(s.EstatusDejada) ? "—" : s.EstatusDejada),
            new CommissionDetailField("Estatus comisión", s.Estatus),
        };
        // Ver el ticket solo aplica a comisiones ya pagadas.
        CommissionShowTicketButton.Visibility = s.PuedeImprimirTicket ? Visibility.Visible : Visibility.Collapsed;

        // Badge de estatus: verde si esta pagada, ambar si sigue pendiente.
        var pagada = string.Equals(s.Estatus, "PAGADA", StringComparison.OrdinalIgnoreCase);
        CommissionDetailStatusText.Text = string.IsNullOrWhiteSpace(s.Estatus) ? "SIN ESTATUS" : s.Estatus.ToUpperInvariant();
        CommissionDetailStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            pagada
                ? System.Windows.Media.Color.FromRgb(0x0F, 0x7A, 0x44)
                : System.Windows.Media.Color.FromRgb(0x8A, 0x56, 0x00));
        CommissionDetailStatusBadge.Background = new System.Windows.Media.SolidColorBrush(
            pagada
                ? System.Windows.Media.Color.FromRgb(0xE4, 0xF6, 0xEC)
                : System.Windows.Media.Color.FromRgb(0xFD, 0xF2, 0xDA));
    }

    // Abre el cajon lateral con el detalle de una fila (boton DETALLE).
    private void OpenCommissionDetail(CommissionSelectionRow row)
    {
        CommissionsGrid.SelectedItem = row;
        LoadCommissionDetail(row);
        CommissionDetailScrim.Visibility = Visibility.Visible;
        CommissionDetailPanel.Visibility = Visibility.Visible;
    }

    private void HideCommissionDetail()
    {
        CommissionDetailScrim.Visibility = Visibility.Collapsed;
        CommissionDetailPanel.Visibility = Visibility.Collapsed;
    }

    private void CloseCommissionDetail_Click(object sender, RoutedEventArgs e) => CloseCommissionDetail();

    // Clic en el velo oscuro de atras: cierra el panel, como en el admin web.
    // Handler aparte porque MouseLeftButtonDown exige MouseButtonEventArgs.
    private void CommissionDetailScrim_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => CloseCommissionDetail();

    private void CloseCommissionDetail()
    {
        ResetCommissionSaleLines();
        ResetCommissionTicketPanel();
        CommissionsGrid.SelectedItem = null;
        HideCommissionDetail();
    }

    private async void RefreshAudit_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshAsync);
    private void OpenCuts_Click(object sender, RoutedEventArgs e) => ApplySelectedModule("Cortes");
    private void OpenExpenses_Click(object sender, RoutedEventArgs e) => new OperationsWindow(_database, _user, _branchCode, "Gastos") { Owner = this }.ShowDialog();
    private void OpenTickets_Click(object sender, RoutedEventArgs e) => new OperationsWindow(_database, _user, _branchCode, "Registro diario") { Owner = this }.ShowDialog();
    private async void OpenRemissionDetail_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => await RunAsync(async () =>
    {
        if (SalesGrid.SelectedItem is not LocalSalesBrowserRow saleRow) return;
        var sale = MapBrowserSale(saleRow);
        var lines = MapTicketRowsToSaleLines(await _pos.GetSalesTicketRowsAsync(saleRow), saleRow);
        new RemissionDetailWindow(_database, _user, _branchCode, sale, lines) { Owner = this }.ShowDialog();
        await Task.CompletedTask;
    });
    private async void DeleteLinePending_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureNotCascoWriteOperation();
        await _pos.DeleteLastSaleLineAsync(SaleFolio.Text, _user);
        await RefreshAsync();
    });
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    private async void RefreshSales_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshSalesBrowserAsync);

    private void NewSale_Click(object sender, RoutedEventArgs e)
    {
        SaleFolio.Clear();
        SaleCustomer.Clear();
        _salesBrowserRows = Array.Empty<LocalSalesBrowserRow>();
        _salesTicketRows = Array.Empty<LocalSalesTicketRow>();
        SalesGrid.ItemsSource = _salesBrowserRows;
        ProductsGrid.ItemsSource = _salesTicketRows;
        UpdateSaleSummary(null);
        ProductCode.Text = string.Empty;
    }

    private async Task LinkCascoSaleToOperationAsync()
    {
        var selectedSale = SalesGrid.SelectedItem as LocalSalesBrowserRow
            ?? throw new InvalidOperationException("Selecciona una remision real antes de enlazar la venta.");

        var operationFolio = FirstFilled(_cascoOperationFolioForSaleLink, SaleCustomer.Text, SaleFolio.Text);
        if (string.IsNullOrWhiteSpace(operationFolio))
            throw new InvalidOperationException("No hay folio original de operacion para enlazar esta venta.");

        if (!string.Equals(selectedSale.OrigenVenta, "COMP", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(selectedSale.OrigenVenta, "JOY", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Selecciona una remision real de compuadmoCasco o joyeriaCasco. No se enlazan filas de APP MOVIL.");
        }

        var branch = CascoCommissionRuleService.BuildLocalBranch();
        var password = ResolveCascoSqlPassword();
        var message =
            "Enlazar venta POS de Casco" + Environment.NewLine + Environment.NewLine
            + $"Folio original: {operationFolio}" + Environment.NewLine
            + $"Remision: {selectedSale.Folio}" + Environment.NewLine
            + $"Origen: {selectedSale.OrigenVenta}" + Environment.NewLine
            + $"Total: {selectedSale.Total.ToString("C2", CultureInfo.CurrentCulture)}" + Environment.NewLine + Environment.NewLine
            + "Se guardara el folio original como numero en RemisioM y mov_operacion.";

        if (!WebDialogWindow.Confirm(this, message, "Venta Casco", "?", "ENLAZAR", "CANCELAR"))
            return;

        var service = new CascoPosSaleLinkService();
        var result = await service.LinkSelectedRemissionAsync(
            branch,
            password,
            operationFolio,
            selectedSale,
            _user,
            CancellationToken.None);

        SaleFolio.Text = operationFolio;
        await RefreshSalesBrowserAsync();

        var commission = await CascoOperationsDataService.GetLocalCommissionPreviewAsync(
            password,
            operationFolio,
            transportOverride: selectedSale.Transporte,
            paymentMethodOverride: selectedSale.Efectivo > 0m ? "Efectivo" : selectedSale.Tarjeta > 0m ? "Tarjeta" : string.Empty,
            cancellationToken: CancellationToken.None);

        WebDialogWindow.Show(
            this,
            "Venta enlazada correctamente." + Environment.NewLine + Environment.NewLine
            + $"Folio original: {result.Preview.FolioOriginal}" + Environment.NewLine
            + $"Operacion numerica: {result.Preview.OperationNumber}" + Environment.NewLine
            + $"RemisioM afectadas: {result.RemisioRowsAffected}" + Environment.NewLine
            + $"mov_operacion afectadas: {result.MovOperacionRowsAffected}" + Environment.NewLine
            + $"Venta visible: {commission.VentaTotal.ToString("C2", CultureInfo.CurrentCulture)}" + Environment.NewLine
            + $"Comision preview: {commission.CommissionAmount.ToString("C2", CultureInfo.CurrentCulture)}",
            "Venta Casco",
            "OK");
    }

    private async void SearchSales_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshSalesBrowserAsync);

    private async void ClearSalesSearch_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        SaleFolio.Clear();
        SaleCustomer.Clear();
        await RefreshSalesBrowserAsync();
    });

    private async void SalesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => await RunAsync(async () =>
    {
        if (SalesGrid.SelectedItem is LocalSalesBrowserRow row)
            await PopulateSaleFieldsAsync(row);
        else
            UpdateSaleSummary(null);
    });

    private async Task RefreshSalesBrowserAsync()
    {
        var search = FirstFilled(SaleFolio.Text, SaleCustomer.Text, _initialSaleLookup);
        if (_startEmpty && string.IsNullOrWhiteSpace(search))
        {
            _salesBrowserRows = Array.Empty<LocalSalesBrowserRow>();
            _salesTicketRows = Array.Empty<LocalSalesTicketRow>();
            SalesGrid.ItemsSource = _salesBrowserRows;
            ProductsGrid.ItemsSource = _salesTicketRows;
            UpdateSaleSummary(null);
            ProductCode.Text = string.Empty;
            ProductName.Text = string.Empty;
            ProductPrice.Text = "0";
            ProductTax.Text = "0";
            ProductStock.Text = "0";
            return;
        }
        _salesBrowserRows = await GetSalesBrowserRowsAsync(search);
        SalesGrid.ItemsSource = _salesBrowserRows;
        var selected = _salesBrowserRows.FirstOrDefault();
        SalesGrid.SelectedItem = selected;
        await PopulateSaleFieldsAsync(selected);
    }

    private async Task RefreshCutsAsync()
    {
        if (IsCascoBranch)
        {
            var cascoCuts = await BuildCascoCutsAsync(CutDate.SelectedDate ?? DateTime.Today);
            CutsGrid.ItemsSource = cascoCuts;
            var selectedCut = cascoCuts.FirstOrDefault();
            CutsGrid.SelectedItem = selectedCut;
            UpdateCutSummary(selectedCut, cascoCuts);
            return;
        }

        var selectedDay = CutDate.SelectedDate ?? DateTime.Today;
        var cuts = await _pos.GetCutsAsync(selectedDay);
        CutsGrid.ItemsSource = cuts;

        var selected = cuts
            .FirstOrDefault(x => x.Date.Date == selectedDay.Date)
            ?? cuts.FirstOrDefault();

        CutsGrid.SelectedItem = selected;
        UpdateCutSummary(selected, cuts);
    }

    private async Task PopulateSaleFieldsAsync(LocalSalesBrowserRow? row)
    {
        if (row is null)
        {
            _salesTicketRows = Array.Empty<LocalSalesTicketRow>();
            ProductsGrid.ItemsSource = _salesTicketRows;
            ProductCode.Text = string.Empty;
            ProductName.Text = string.Empty;
            ProductPrice.Text = "0";
            ProductTax.Text = "0";
            ProductStock.Text = "0";
            UpdateSaleSummary(null);
            return;
        }

        ProductCode.Text = row.Folio;
        ProductName.Text = string.IsNullOrWhiteSpace(row.Cliente) ? row.Taxista : row.Cliente;
        ProductPrice.Text = row.Subtotal.ToString("N2", CultureInfo.CurrentCulture);
        ProductTax.Text = row.Iva.ToString("N2", CultureInfo.CurrentCulture);
        ProductStock.Text = row.Total.ToString("N2", CultureInfo.CurrentCulture);

        _salesTicketRows = await GetSalesTicketRowsAsync(row);
        ProductsGrid.ItemsSource = _salesTicketRows;
        UpdateSaleSummary(row);
        await UpdateCascoSaleCommissionSummaryAsync(row);
    }

    private void UpdateSaleSummary(LocalSalesBrowserRow? row)
    {
        SaleDateText.Text = row?.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "HOY";
        SaleSubtotalText.Text = (row?.Subtotal ?? 0m).ToString("N2", CultureInfo.CurrentCulture);
        SaleTaxText.Text = (row?.Iva ?? 0m).ToString("N2", CultureInfo.CurrentCulture);
        SaleTotalText.Text = (row?.Total ?? 0m).ToString("N2", CultureInfo.CurrentCulture);
        SalePayoutText.Text = "0.00";
        SaleCommissionText.Text = "0.00";
    }

    private async Task UpdateCascoSaleCommissionSummaryAsync(LocalSalesBrowserRow? row)
    {
        if (!IsCascoBranch || row is null)
            return;

        var branch = new BranchConfigurationService().GetBranch("CV");
        var password = ResolveCascoSqlPassword();
        var folioOriginal = FirstFilled(_cascoOperationFolioForSaleLink, row.FolioRegistro, row.FolioApp, row.FolioControl, row.Folio);
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(folioOriginal))
            return;

        try
        {
            var relations = await CascoOperationsDataService.LoadRelationsAsync(
                branch,
                "CV",
                password,
                search: folioOriginal,
                cancellationToken: CancellationToken.None);
            var normalized = folioOriginal.TrimStart('0');
            var relation = relations.FirstOrDefault(x =>
                string.Equals((x.OperationFolio ?? string.Empty).TrimStart('0'), normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals((x.AppFolio ?? string.Empty).TrimStart('0'), normalized, StringComparison.OrdinalIgnoreCase));
            if (relation is not null)
            {
                SalePayoutText.Text = (relation.Payout ?? 0m).ToString("N2", CultureInfo.CurrentCulture);
                SaleCommissionText.Text = relation.Commission.ToString("N2", CultureInfo.CurrentCulture);
                return;
            }

            var preview = await new CascoCommissionRuleService().PreviewAsync(
                branch,
                password,
                folioOriginal,
                saleOverride: row.Total,
                transportOverride: row.Transporte,
                paymentMethodOverride: row.Efectivo > 0m ? "Efectivo" : row.Tarjeta > 0m ? "Tarjeta" : string.Empty,
                cancellationToken: CancellationToken.None);
            SaleCommissionText.Text = preview.CommissionAmount.ToString("N2", CultureInfo.CurrentCulture);
        }
        catch (Exception ex)
        {
            // La ventana de venta debe seguir abierta aunque no exista tabla o regla de comisiones.
            WritePosLog("No se pudo calcular el resumen de comision de la venta.", ex);
            SaleCommissionText.Text = 0m.ToString("N2", CultureInfo.CurrentCulture);
        }
    }

    private void UpdateCutSummary(LocalCut? cut, IReadOnlyList<LocalCut>? allCuts = null)
    {
        CutSummaryDateText.Text = cut?.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? (CutDate.SelectedDate ?? DateTime.Today).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        CutSummaryMovementsText.Text = ((allCuts?.Count ?? 0) > 0 ? allCuts!.Count : cut is null ? 0 : 1).ToString("N0", CultureInfo.InvariantCulture);
        CutSummaryStatusText.Text = string.IsNullOrWhiteSpace(cut?.Status) ? "ABIERTO" : cut!.Status.ToUpperInvariant();
        CutSummaryCollectedText.Text = ((cut?.Cash ?? 0m) + (cut?.Card ?? 0m)).ToString("C2", CultureInfo.CurrentCulture);
        CutCashText.Text = (cut?.Cash ?? 0m).ToString("C2", CultureInfo.CurrentCulture);
        CutCardText.Text = (cut?.Card ?? 0m).ToString("C2", CultureInfo.CurrentCulture);
        CutPaymentsText.Text = (cut?.Payments ?? 0m).ToString("C2", CultureInfo.CurrentCulture);
        CutExpensesText.Text = (cut?.Expenses ?? 0m).ToString("C2", CultureInfo.CurrentCulture);
        CutExpectedText.Text = (cut?.Expected ?? 0m).ToString("C2", CultureInfo.CurrentCulture);
        CutCountedText.Text = (cut?.Counted ?? 0m).ToString("C2", CultureInfo.CurrentCulture);
        CutDifferenceText.Text = (cut?.Difference ?? 0m).ToString("C2", CultureInfo.CurrentCulture);
        CutCounted.Text = (cut?.Counted ?? 0m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static LocalSale MapBrowserSale(LocalSalesBrowserRow row)
    {
        var paid = row.Efectivo + row.Tarjeta + row.Dolares;
        var paymentStatus = paid >= row.Total && row.Total > 0m ? "Pagado" : paid > 0m ? "Parcial" : "Pendiente";
        return new LocalSale(
            0,
            row.Folio,
            row.Fecha,
            row.Cliente,
            "ACTIVA",
            row.Subtotal,
            row.Iva,
            row.Total,
            paid,
            paymentStatus,
            row.Usuario);
    }

    private static IReadOnlyList<LocalSaleLine> MapTicketRowsToSaleLines(IReadOnlyList<LocalSalesTicketRow> rows, LocalSalesBrowserRow row)
    {
        if (rows.Count == 0)
        {
            return
            [
                new LocalSaleLine(
                    0,
                    0,
                    0,
                    row.Folio,
                    string.IsNullOrWhiteSpace(row.Cliente) ? $"VENTA {row.Folio}" : row.Cliente,
                    1,
                    row.Subtotal,
                    row.Iva,
                    row.Subtotal,
                    row.Iva,
                    row.Total)
            ];
        }

        return rows.Select((item, index) => new LocalSaleLine(
            index + 1,
            0,
            0,
            row.Folio,
            item.Producto,
            item.Cantidad,
            item.Precio,
            0m,
            item.Importe,
            0m,
            item.Importe)).ToArray();
    }

    private async Task RefreshCommissionBrowserAsync(bool resetPage)
    {
        ShowCommissionsSkeleton();
        try
        {
            await RefreshCommissionBrowserCoreAsync(resetPage);
        }
        finally
        {
            HideCommissionsSkeleton();
        }
    }

    private void ShowCommissionsSkeleton()
    {
        if (CommissionsSkeletonRows.Items.Count == 0)
        {
            for (var i = 0; i < 8; i++)
                CommissionsSkeletonRows.Items.Add(new object());
        }
        CommissionsSkeleton.Visibility = Visibility.Visible;
        CommissionsGrid.Visibility = Visibility.Collapsed;
        CommissionsEmptyState.Visibility = Visibility.Collapsed;
    }

    private void HideCommissionsSkeleton()
    {
        // No toca CommissionsGrid.Visibility aqui: ApplyCommissionPage ya decidio si se
        // muestra la tabla o el estado vacio, y esto corre despues en el finally.
        CommissionsSkeleton.Visibility = Visibility.Collapsed;
    }

    private async Task RefreshCommissionBrowserCoreAsync(bool resetPage)
    {
        if (IsCascoBranch)
        {
            var cascoSelectedByFolio = _commissionFilteredRows
                .Where(x => x.IsSelected)
                .Select(x => x.Source.Folio)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _commissionRows = (await BuildCascoCommissionRowsAsync(
                CommissionFolio.Text,
                CommissionStartDate.SelectedDate,
                CommissionEndDate.SelectedDate)).ToList();

            _commissionFilteredRows = _commissionRows
                .Select(x => new CommissionSelectionRow(x) { IsSelected = cascoSelectedByFolio.Contains(x.Folio) && x.PuedePagar })
                .ToList();

            if (resetPage)
                _commissionPage = 1;

            CommissionTotalSaleText.Text = _commissionRows.Sum(x => x.VentaTotal).ToString("C2", CultureInfo.CurrentCulture);
            CommissionTotalBaseText.Text = _commissionRows.Sum(x => x.VentaTotal - x.Dejada - x.BebidasCajasRegalo - x.Reparacion - x.Degustacion).ToString("C2", CultureInfo.CurrentCulture);
            CommissionTotalAmountText.Text = _commissionRows.Sum(x => x.PagoComision).ToString("C2", CultureInfo.CurrentCulture);
            CommissionTotalPaidText.Text = _commissionRows.Sum(x => x.Pagado).ToString("C2", CultureInfo.CurrentCulture);
            CommissionPendingCountText.Text = _commissionRows.Count(x => string.Equals(x.Estatus, "PENDIENTE", StringComparison.OrdinalIgnoreCase)).ToString("N0", CultureInfo.InvariantCulture);
            CommissionPaidCountText.Text = _commissionRows.Count(x => string.Equals(x.Estatus, "PAGADA", StringComparison.OrdinalIgnoreCase)).ToString("N0", CultureInfo.InvariantCulture);
            ApplyCommissionPage();
            return;
        }

        var search = CommissionFolio.Text;
        CommissionStartDate.SelectedDate ??= DateTime.Today;
        CommissionEndDate.SelectedDate ??= CommissionStartDate.SelectedDate;
        var start = CommissionStartDate.SelectedDate;
        var end = CommissionEndDate.SelectedDate;
        _commissionRows = await Task.Run(async () => (await _pos.GetCommissionBrowserRowsAsync(
            search,
            start,
            end)).ToList());

        var selectedByFolio = _commissionFilteredRows
            .Where(x => x.IsSelected)
            .Select(x => x.Source.Folio)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _commissionFilteredRows = _commissionRows
            .Select(x => new CommissionSelectionRow(x) { IsSelected = selectedByFolio.Contains(x.Folio) })
            .ToList();

        if (resetPage)
            _commissionPage = 1;

        CommissionTotalSaleText.Text = _commissionRows.Sum(x => x.VentaTotal).ToString("C2", CultureInfo.CurrentCulture);
        CommissionTotalBaseText.Text = _commissionRows.Sum(x => x.VentaTotal - x.Dejada - x.BebidasCajasRegalo - x.Reparacion - x.Degustacion).ToString("C2", CultureInfo.CurrentCulture);
        CommissionTotalAmountText.Text = _commissionRows.Sum(x => x.PagoComision).ToString("C2", CultureInfo.CurrentCulture);
        CommissionTotalPaidText.Text = _commissionRows.Sum(x => x.Pagado).ToString("C2", CultureInfo.CurrentCulture);
        CommissionPendingCountText.Text = _commissionRows.Count(x => string.Equals(x.Estatus, "PENDIENTE", StringComparison.OrdinalIgnoreCase)).ToString("N0", CultureInfo.InvariantCulture);
        CommissionPaidCountText.Text = _commissionRows.Count(x => string.Equals(x.Estatus, "PAGADA", StringComparison.OrdinalIgnoreCase)).ToString("N0", CultureInfo.InvariantCulture);

        ApplyCommissionPage();
    }

    private async Task EnsureCommissionDataUpToDateAsync()
    {
        if (await _pos.HasImportedCommissionSourceAsync())
            return;

        var folio = (CommissionFolio.Text ?? string.Empty).Trim();
        var start = CommissionStartDate.SelectedDate?.Date;
        var end = CommissionEndDate.SelectedDate?.Date;

        if (string.IsNullOrWhiteSpace(folio) && !start.HasValue && !end.HasValue)
        {
            CommissionStartDate.SelectedDate = DateTime.Today;
            CommissionEndDate.SelectedDate = DateTime.Today;
            start = DateTime.Today;
            end = DateTime.Today;
        }

        var shouldRefresh = false;
        if (!string.IsNullOrWhiteSpace(folio))
        {
            shouldRefresh = true;
        }
        else if (start.HasValue || end.HasValue)
        {
            var effectiveStart = start ?? end ?? DateTime.Today;
            var effectiveEnd = end ?? start ?? effectiveStart;
            if (effectiveEnd < effectiveStart)
                (effectiveStart, effectiveEnd) = (effectiveEnd, effectiveStart);
            shouldRefresh = effectiveStart == effectiveEnd;
        }

        if (!shouldRefresh)
            return;

        var refreshKey = string.Join("|", new[]
        {
            _user,
            folio.ToUpperInvariant(),
            start?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty,
            end?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty
        });
        var now = DateTime.UtcNow;
        if (string.Equals(_lastCommissionAutoRefreshKey, refreshKey, StringComparison.OrdinalIgnoreCase)
            && now - _lastCommissionAutoRefreshAtUtc < TimeSpan.FromMinutes(5))
        {
            return;
        }

        await Task.Run(async () => await _pos.RecalculateCommissionsAsync(_user));
        _lastCommissionAutoRefreshKey = refreshKey;
        _lastCommissionAutoRefreshAtUtc = now;
    }

    private void ApplyCommissionPage()
    {
        var totalRows = _commissionFilteredRows.Count;
        var totalPages = Math.Max((int)Math.Ceiling(totalRows / (double)CommissionPageSize), 1);
        _commissionPage = Math.Clamp(_commissionPage, 1, totalPages);
        var paged = _commissionFilteredRows
            .Skip((_commissionPage - 1) * CommissionPageSize)
            .Take(CommissionPageSize)
            .ToArray();
        CommissionsGrid.ItemsSource = paged;
        var start = totalRows == 0 ? 0 : ((_commissionPage - 1) * CommissionPageSize) + 1;
        var end = totalRows == 0 ? 0 : Math.Min(_commissionPage * CommissionPageSize, totalRows);
        CommissionRowsText.Text = $"REGISTROS {start}-{end} DE {totalRows}";
        CommissionPageText.Text = $"PAGINA {_commissionPage} DE {totalPages}";
        CommissionPrevPageButton.IsEnabled = _commissionPage > 1;
        CommissionNextPageButton.IsEnabled = _commissionPage < totalPages;
        UpdateCommissionSelectionTotal();

        if (totalRows == 0)
        {
            var folio = CommissionFolio.Text?.Trim();
            var start2 = CommissionStartDate.SelectedDate;
            var end2 = CommissionEndDate.SelectedDate;
            CommissionsEmptyStateDetail.Text = !string.IsNullOrWhiteSpace(folio)
                ? $"No se encontró ninguna comisión con el folio \"{folio}\"."
                : start2.HasValue && end2.HasValue
                    ? $"No hay comisiones registradas entre el {start2:dd/MM/yyyy} y el {end2:dd/MM/yyyy}."
                    : "No hay comisiones para este filtro.";
            CommissionsEmptyState.Visibility = Visibility.Visible;
            CommissionsGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            CommissionsEmptyState.Visibility = Visibility.Collapsed;
            CommissionsGrid.Visibility = Visibility.Visible;
        }
    }

    private async Task<IReadOnlyList<LocalRelation>> LoadCascoRelationsAsync(DateTime? start, DateTime? end)
    {
        return await CascoOperationsDataService.LoadRelationsAsync(
            new BranchConfigurationService().GetBranch("CV"),
            "CV",
            ResolveCascoSqlPassword(),
            start: start?.Date,
            end: end?.Date,
            cancellationToken: CancellationToken.None);
    }

    private async Task<IReadOnlyList<LocalCommissionBrowserRow>> BuildCascoCommissionRowsAsync(string? search, DateTime? start, DateTime? end)
    {
        var rows = await LoadCascoRelationsAsync(start ?? DateTime.Today, end ?? start ?? DateTime.Today);
        IEnumerable<LocalRelation> filtered = rows;
        var query = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = filtered.Where(row =>
                ContainsIgnoreCase(row.OperationFolio, query)
                || ContainsIgnoreCase(row.DisplayLocalFolio, query)
                || ContainsIgnoreCase(row.PosFolio, query)
                || ContainsIgnoreCase(row.Driver, query)
                || ContainsIgnoreCase(row.Badge, query)
                || ContainsIgnoreCase(row.Hotel, query)
                || ContainsIgnoreCase(row.TransportType, query));
        }

        return filtered
            .Select(MapCascoCommissionBrowserRow)
            .OrderByDescending(x => x.Fecha)
            .ThenByDescending(x => x.Folio, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalCut>> BuildCascoCutsAsync(DateTime day)
    {
        var rows = await LoadCascoRelationsAsync(day, day);
        if (rows.Count == 0)
            return Array.Empty<LocalCut>();

        var cash = rows.Sum(x => x.CashAmount);
        var card = rows.Sum(x => x.CardAmount);
        var payments = rows.Sum(x => x.PayoutPaid);
        var expected = rows.Sum(x => x.Payout ?? 0m);
        var counted = Number(CutCounted.Text);
        var difference = counted - expected;

        return
        [
            new LocalCut(
                1,
                day.Date,
                cash,
                card,
                payments,
                0m,
                expected,
                counted,
                difference,
                rows.Any(x => string.Equals(x.PayoutStatus, "PAGADO", StringComparison.OrdinalIgnoreCase)) ? "Abierto" : "Abierto",
                _user,
                null)
        ];
    }

    private static LocalCommissionBrowserRow MapCascoCommissionBrowserRow(LocalRelation row)
    {
        var date = TryParsePreviewDate(row.DateText);
        var commission = row.Commission;
        var paid = row.CommissionPaid;
        var sale = row.Sale;
        var payout = row.Payout ?? 0m;
        var status = string.IsNullOrWhiteSpace(row.CommissionStatus)
            ? ResolveCascoCommissionStatus(commission, paid)
            : row.CommissionStatus;

        return new LocalCommissionBrowserRow(
            row.OperationFolio,
            row.DisplayLocalFolio,
            date,
            row.TransportType,
            row.Unit,
            string.IsNullOrWhiteSpace(row.Driver) ? row.Vendor : row.Driver,
            row.Passengers,
            row.Hotel,
            0m,
            0m,
            0m,
            sale,
            sale,
            row.PosFolio,
            row.PaymentMethod,
            0m,
            payout,
            0m,
            0m,
            0m,
            sale > 0m && commission > 0m ? commission / sale : 0m,
            commission,
            paid,
            Math.Max(commission - paid, 0m),
            status,
            row.Vendor,
            row.Badge);
    }

    private static string ResolveCascoCommissionStatus(decimal amount, decimal paid) =>
        amount <= 0m ? "SIN COMISION" : paid >= amount ? "PAGADA" : paid > 0m ? "PARCIAL" : "PENDIENTE";

    private void UpdateCommissionSelectionTotal()
    {
        var selected = _commissionFilteredRows
            .Where(x => x.IsSelected)
            .Sum(x => x.Source.Saldo);
        var formatted = selected.ToString("C2", CultureInfo.CurrentCulture);
        CommissionSelectedTotalText.Text = formatted;
        CommissionSelectionFooterText.Text = $"Seleccionado {formatted}";
    }

    private static string BuildCommissionsPrintablePreview(IReadOnlyList<LocalCommissionBrowserRow> rows)
    {
        var lines = new List<string>
        {
            "** REPORTE DE COMISIONES **",
            string.Empty,
            Pad("Folios", 18) + Pad("Unidad", 16) + Pad("Nombre", 22) + Pad("Hotel", 20) + Pad("Ticket", 12) + Pad("Comision", 12, true) + Pad("Pagado", 12, true) + Pad("Estatus", 12),
        };

        lines.AddRange(rows.Select(x =>
            Pad(x.Folio, 18) +
            Pad(x.Unidad, 16) +
            Pad(x.Nombre, 22) +
            Pad(x.Hotel, 20) +
            Pad(x.Ticket, 12) +
            Pad(x.PagoComision.ToString("0.00", CultureInfo.InvariantCulture), 12, true) +
            Pad(x.Pagado.ToString("0.00", CultureInfo.InvariantCulture), 12, true) +
            Pad(x.Estatus, 12)));

        lines.Add(string.Empty);
        lines.Add($"Venta: {rows.Sum(x => x.VentaTotal):C2}  Comision: {rows.Sum(x => x.PagoComision):C2}  Pagado: {rows.Sum(x => x.Pagado):C2}");
        return string.Join(Environment.NewLine, lines);
    }

    private async void ExportSalesExcel_Click(object sender, RoutedEventArgs e) => await ExportTableAsync("ControlTaxi-Ventas.xls", "Ventas", _salesBrowserRows, true);
    private async void ExportSalesPdf_Click(object sender, RoutedEventArgs e) => await ExportTableAsync("ControlTaxi-Ventas.pdf", "Ventas", _salesBrowserRows, false);
    private async void ExportPaymentsCsv_Click(object sender, RoutedEventArgs e) => await ExportCsvAsync("pagos.csv", await _pos.GetPaymentsAsync());
    private async void ExportPaymentsPdf_Click(object sender, RoutedEventArgs e) => await ExportTableAsync("ControlTaxi-Pagos.pdf", "Pagos", await _pos.GetPaymentsAsync(), false);
    private async void ExportCommissionsCsv_Click(object sender, RoutedEventArgs e) => await ExportCsvAsync("comisiones.csv", _commissionFilteredRows.Select(x => x.Source).ToArray());
    private async void ExportCommissionsPdf_Click(object sender, RoutedEventArgs e) => await ExportTableAsync("ControlTaxi-Comisiones.pdf", "Comisiones", _commissionFilteredRows.Select(x => x.Source).ToArray(), false);
    private async void ExportCutsCsv_Click(object sender, RoutedEventArgs e) => await ExportCsvAsync("corte.csv", await _pos.GetCutsAsync());
    private async void ExportCutsPdf_Click(object sender, RoutedEventArgs e) => await ExportTableAsync("ControlTaxi-Cortes.pdf", "Cortes", await _pos.GetCutsAsync(), false);
    private async void RefreshCuts_Click(object sender, RoutedEventArgs e) => await RunAsync(RefreshCutsAsync);
    private void CutsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CutsGrid.SelectedItem is LocalCut cut)
            UpdateCutSummary(cut);
    }
    private void PrintCuts_Click(object sender, RoutedEventArgs e)
    {
        var cut = CutsGrid.SelectedItem as LocalCut;
        if (cut is null)
        {
            WebDialogWindow.Show(this, "No hay corte para imprimir.", "Control Taxi", "!");
            return;
        }

        var lines = new List<string>
        {
            "** REPORTE DE CIERRE / CORTE **",
            string.Empty,
            $"Fecha del Reporte : {cut.Date:dd/MM/yyyy} al {cut.Date:dd/MM/yyyy}",
            $"{new string(' ', 96)}Page 1 of 1",
            string.Empty,
            Pad("concepto", 24) + Pad("importe", 14, true),
            Pad("EFECTIVO", 24) + Pad(cut.Cash.ToString("0.00", CultureInfo.InvariantCulture), 14, true),
            Pad("TARJETA", 24) + Pad(cut.Card.ToString("0.00", CultureInfo.InvariantCulture), 14, true),
            Pad("PAGOS", 24) + Pad(cut.Payments.ToString("0.00", CultureInfo.InvariantCulture), 14, true),
            Pad("GASTOS", 24) + Pad(cut.Expenses.ToString("0.00", CultureInfo.InvariantCulture), 14, true),
            Pad("TOTAL DEL DIA", 24) + Pad(cut.Expected.ToString("0.00", CultureInfo.InvariantCulture), 14, true),
            Pad("CONTADO", 24) + Pad(cut.Counted.ToString("0.00", CultureInfo.InvariantCulture), 14, true),
            Pad("DIFERENCIA", 24) + Pad(cut.Difference.ToString("0.00", CultureInfo.InvariantCulture), 14, true),
            Pad("ESTATUS", 24) + Pad(cut.Status, 14, true)
        };

        _output.PrintText("Corte", string.Join(Environment.NewLine, lines));
    }
    private async void CalculateReport_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadReportCenterAsync);
    private async void ClearReport_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        ReportStart.SelectedDate = DateTime.Today;
        ReportEnd.SelectedDate = DateTime.Today;
        ReportStart.Text = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        ReportEnd.Text = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        await LoadReportCenterAsync();
    });
    private async void PreviewReport_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadReportCenterAsync);
    private async void ExportReportPdf_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = ReportsTabControl.SelectedIndex == 1 ? "pagos_comisiones.pdf" : "reporte_operativo.pdf" };
        if (dialog.ShowDialog() != true) return;
        var printable = BuildReportPrintablePreview(
            ReportOperationsGrid.Items.OfType<LocalOperationsPreviewRow>().ToArray(),
            ReportCommissionPaymentsGrid.Items.OfType<LocalCommissionPaymentPreviewRow>().ToArray(),
            ReportsTabControl.SelectedIndex == 1,
            ReportStartDate,
            ReportEndDate);
        await _output.ExportTextPdfAsync(printable, dialog.FileName);
    });
    private async void ExportOperationalExcel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"control_dejadas_{ReportStartDate:yyyyMMdd}_{ReportEndDate:yyyyMMdd}.xlsx" };
        if (dialog.ShowDialog() != true) return;
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await _output.ExportCascoControlDejadasWorkbookAsync(
                ReportStartDate,
                ReportEndDate,
                await GetReportWorkbookRelationsAsync(),
                dialog.FileName);
            return;
        }

        await EnsureDetailedReportRelationsAsync();
        await _output.ExportControlDejadasWorkbookAsync(
            ReportStartDate,
            ReportEndDate,
            _cachedReportRelations,
            dialog.FileName);
    });
    private async void ExportConcentratedExcel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"concentrado_general_{ReportStartDate:yyyyMMdd}_{ReportEndDate:yyyyMMdd}.xlsx" };
        if (dialog.ShowDialog() != true) return;
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await _output.ExportCascoConcentradoWorkbookAsync(
                ReportStartDate,
                ReportEndDate,
                await GetReportWorkbookRelationsAsync(),
                dialog.FileName);
            return;
        }

        await EnsureDetailedReportRelationsAsync();
        await _output.ExportConcentradoWorkbookAsync(
            ReportStartDate,
            ReportEndDate,
            _cachedReportRelations,
            await _pos.GetCamionesResumenAsync(ReportStartDate, ReportEndDate),
            dialog.FileName);
    });
    private async void ExportCuadreExcel_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = $"cuadre_final_{ReportStartDate:yyyyMMdd}_{ReportEndDate:yyyyMMdd}.xlsx" };
        if (dialog.ShowDialog() != true) return;
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await _output.ExportCascoCuadreWorkbookAsync(
                ReportStartDate,
                ReportEndDate,
                await GetReportWorkbookRelationsAsync(),
                await GetReportWorkbookCommissionsAsync(),
                dialog.FileName);
            return;
        }

        await EnsureDetailedReportRelationsAsync();
        var commissions = await _pos.GetCommissionsAsync();
        var camiones = await _pos.GetCamionesResumenAsync(ReportStartDate, ReportEndDate);
        await _output.ExportCuadreWorkbookAsync(
            ReportStartDate,
            ReportEndDate,
            _cachedReportRelations,
            commissions,
            _cachedReportCuts,
            camiones,
            dialog.FileName);
    });
    private async void OpenReportSearch_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadReportCenterAsync);
    private async void OpenReportMovementsModule_Click(object sender, RoutedEventArgs e) => await RunAsync(LoadReportCenterAsync);
    private void OpenReportCommissionsModule_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            ReportsTabControl.SelectedIndex = 1;
            UpdateReportDashboard(
                ReportOperationsGrid.Items.OfType<LocalOperationsPreviewRow>().ToArray(),
                ReportCommissionPaymentsGrid.Items.OfType<LocalCommissionPaymentPreviewRow>().ToArray(),
                Array.Empty<LocalCut>());
            return;
        }

        new PosWindow(_database, _user, _branchCode, "Comisiones") { Owner = this }.ShowDialog();
    }
    private void OpenReportCutsModule_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Seguridad de sucursal: Centro de Reportes CV intento cargar Plaza 28.");

        new PosWindow(_database, _user, _branchCode, "Cortes") { Owner = this }.ShowDialog();
    }
    private void OpenReportPaymentsModule_Click(object sender, RoutedEventArgs e)
    {
        ReportsTabControl.SelectedIndex = 1;
        UpdateReportDashboard(
            ReportOperationsGrid.Items.OfType<LocalOperationsPreviewRow>().ToArray(),
            ReportCommissionPaymentsGrid.Items.OfType<LocalCommissionPaymentPreviewRow>().ToArray(),
            Array.Empty<LocalCut>());
    }
    private void PrintReport_Click(object sender, RoutedEventArgs e)
    {
        var active = ReportPrintPreviewText.Text;
        if (string.IsNullOrWhiteSpace(active)) { WebDialogWindow.Show(this, "Calcula primero el reporte.", "Control Taxi", "!"); return; }
        _output.PrintText("Centro de reportes", active);
    }

    private string? GetCurrentSiteName()
    {
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
            return "Casco Viejo";

        if (string.Equals(_branchCode, "ALL", StringComparison.OrdinalIgnoreCase))
            return null;

        return _currentBranch?.SiteName;
    }

    private static bool ContainsIgnoreCase(string? value, string? query) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.IsNullOrWhiteSpace(query)
        && value.Contains(query!, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> SplitSearchTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        return value
            .Split(',', ';', '|', '/', '\\', '\n', '\r', '\t', ' ')
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTime? ParseCascoDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
    }

    private async Task<IReadOnlyList<LocalSalesBrowserRow>> GetSalesBrowserRowsAsync(string? search = null)
    {
        if (!IsCascoBranch)
            return await _pos.GetSalesBrowserRowsAsync(search);

        var branch = new BranchConfigurationService().GetBranch("CV");
        var password = ResolveCascoSqlPassword();
        if (string.IsNullOrWhiteSpace(password))
        {
            WritePosLog("No se pudo cargar ventas Casco: falta la credencial SQL local.");
            return Array.Empty<LocalSalesBrowserRow>();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            try
            {
                var salesProvider = new CascoSalesDataProvider(branch);
                var realSales = await salesProvider.GetSalesBrowserRowsAsync(password, search);
                WritePosLog($"Busqueda VENTA Casco '{search}' devolvio {realSales.Count} fila(s) desde RemisioM.");
                if (realSales.Count > 0)
                    return await ApplyCascoRelationPaymentFallbackAsync(realSales, password, search);
            }
            catch (Exception ex)
            {
                WritePosLog($"No se pudo leer ventas POS Casco para busqueda '{search}'.", ex);
                // Conserva la lectura actual de APP MOVIL si no hay conexion local a POS.
            }
        }

        var provider = new CascoReadOnlyDataProvider(branch);
        var records = await provider.GetDetailedAppRecordsAsync(
            password,
            string.IsNullOrWhiteSpace(search) ? DateTime.Today.AddDays(-30) : null,
            string.IsNullOrWhiteSpace(search) ? DateTime.Today : null,
            CancellationToken.None);

        var tokens = SplitSearchTokens(search);
        var filtered = tokens.Count == 0
            ? records
            : records.Where(row => tokens.Any(token =>
                ContainsIgnoreCase(row.FolioControl, token)
                || ContainsIgnoreCase(row.OriginalFolio, token)
                || ContainsIgnoreCase(row.PosFolio, token)
                || ContainsIgnoreCase(row.Badge, token)
                || ContainsIgnoreCase(row.DriverName, token)
                || ContainsIgnoreCase(row.Origin, token)
                || ContainsIgnoreCase(row.Destination, token)
                || ContainsIgnoreCase(row.User, token)
                || ContainsIgnoreCase(row.TransportType, token)
                || ContainsIgnoreCase(row.Hotel, token)))
                .ToArray();

        return filtered
            .Select(row => new LocalSalesBrowserRow(
                string.IsNullOrWhiteSpace(row.OriginalFolio) ? row.FolioControl : row.OriginalFolio,
                row.PosFolio,
                ParseCascoDate(row.OperationDate) ?? DateTime.MinValue,
                string.IsNullOrWhiteSpace(row.DriverName) ? row.User : row.DriverName,
                row.User,
                row.DriverName,
                row.User,
                row.OriginalFolio,
                row.FolioControl,
                row.FolioControl,
                row.Badge,
                row.TransportType,
                row.Passengers,
                row.Total,
                0m,
                row.Total,
                row.Cash,
                row.Card,
                row.Dollars,
                row.ExchangeRate,
                string.IsNullOrWhiteSpace(row.Origin) ? row.Destination : row.Origin))
            .OrderByDescending(row => row.Fecha)
            .ThenByDescending(row => row.Folio, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<LocalSalesBrowserRow>> ApplyCascoRelationPaymentFallbackAsync(
        IReadOnlyList<LocalSalesBrowserRow> rows,
        string password,
        string? search)
    {
        if (rows.Count == 0 || string.IsNullOrWhiteSpace(password))
            return rows;

        var folio = FirstFilled(_cascoOperationFolioForSaleLink, search ?? string.Empty);
        if (string.IsNullOrWhiteSpace(folio))
            folio = FirstFilled(rows.Select(row => row.FolioRegistro).ToArray());
        if (string.IsNullOrWhiteSpace(folio))
            return rows;

        try
        {
            var branch = new BranchConfigurationService().GetBranch("CV");
            var relations = await CascoOperationsDataService.LoadRelationsAsync(
                branch,
                "CV",
                password,
                search: folio,
                cancellationToken: CancellationToken.None);
            var normalized = folio.TrimStart('0');
            var relation = relations.FirstOrDefault(row =>
                string.Equals((row.OperationFolio ?? string.Empty).TrimStart('0'), normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals((row.AppFolio ?? string.Empty).TrimStart('0'), normalized, StringComparison.OrdinalIgnoreCase));
            if (relation is null || string.IsNullOrWhiteSpace(relation.PaymentMethod))
                return rows;

            var fallback = relation.PaymentMethod.Trim() + " (respaldo normalizado)";
            return rows.Select(row =>
                row.TotalPagos > 0m && string.Equals(row.FormaPagoDetalle, "NO ESPECIFICADA", StringComparison.OrdinalIgnoreCase)
                    ? row with { FormaPagoDetalle = fallback, PagoDetalle = BuildSalePaymentDetail(row.TotalPagos, row.MonedaDetalle, fallback) }
                    : row)
                .ToArray();
        }
        catch (Exception ex)
        {
            WritePosLog("No se pudo aplicar el respaldo de forma de pago de la relacion.", ex);
            return rows;
        }
    }

    private async Task<IReadOnlyList<LocalSalesTicketRow>> GetSalesTicketRowsAsync(LocalSalesBrowserRow sale)
    {
        if (!IsCascoBranch)
            return await _pos.GetSalesTicketRowsAsync(sale);

        if (string.Equals(sale.OrigenVenta, "COMP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sale.OrigenVenta, "JOY", StringComparison.OrdinalIgnoreCase))
        {
            var password = ResolveCascoSqlPassword();
            if (!string.IsNullOrWhiteSpace(password))
            {
                try
                {
                    var salesProvider = new CascoSalesDataProvider(new BranchConfigurationService().GetBranch("CV"));
                    var rows = await salesProvider.GetSalesTicketRowsAsync(password, sale);
                    if (rows.Count > 0)
                        return rows;
                }
                catch (Exception ex)
                {
                    WritePosLog($"No se pudo leer el detalle RemisioD para ticket '{sale.Folio}'.", ex);
                    // Si RemisioD no tiene detalle, se muestra al menos el encabezado de la venta.
                }
            }
        }

        return new[] { BuildFallbackSalesTicketRow(sale) };
    }

    private static LocalSalesTicketRow BuildFallbackSalesTicketRow(LocalSalesBrowserRow sale) =>
        new(
            1,
            string.IsNullOrWhiteSpace(sale.Cliente) ? $"VENTA {sale.Folio}" : sale.Cliente,
            string.IsNullOrWhiteSpace(sale.OrigenVenta) ? "VENTA" : sale.OrigenVenta,
            sale.Subtotal,
            sale.Total,
            sale.Folio,
            sale.Factura,
            sale.Fecha.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            sale.Gafete,
            sale.OrigenVenta);

    private void EnsureNotCascoWriteOperation()
    {
        if (IsCascoBranch)
            throw new InvalidOperationException("Esta vista esta en modo solo lectura para Casco Viejo. No se permite escribir en Plaza 28.");
    }

    private async void HandleCascoRecordsChanged(object? sender, CascoRecordsChangedEventArgs args)
    {
        if (!string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase) || !_windowReady || !IsLoaded)
            return;

        await Dispatcher.InvokeAsync(async () =>
        {
            if (PosTabs.SelectedIndex == 4)
                await RunAsync(LoadReportCenterAsync);
        });
    }

    private async Task LoadReportCenterAsync()
    {
        var loadId = Interlocked.Increment(ref _reportLoadSequence);
        var start = ReportStartDate;
        var end = ReportEndDate;
        ReportStart.SelectedDate = start;
        ReportEnd.SelectedDate = end;
        _cachedReportRelations = Array.Empty<LocalRelation>();
        var reportBranch = string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase)
            ? new BranchConfigurationService().GetBranch("CV")
            : null;
        var report = await CascoOperationsDataService.LoadActiveReportAsync(
            reportBranch,
            _branchCode,
            ResolveCascoSqlPassword(),
            async (rangeStart, rangeEnd, siteName, cancellationToken) =>
                (await _operations.GetOperationsReportPreviewAsync(rangeStart, rangeEnd, siteName)).ToArray(),
            async (rangeStart, rangeEnd, siteName, cancellationToken) =>
                (await _operations.GetCommissionPaymentsReportAsync(rangeStart, rangeEnd, siteName)).ToArray(),
            start,
            end,
            GetCurrentSiteName(),
            CancellationToken.None);
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            _cachedOperationPreviewRows = report.OperationRows.ToArray();
            _cachedCommissionPayments = report.PaymentRows.ToArray();
            _cachedReportCuts = Array.Empty<LocalCut>();
            WriteReportLog(
                "CENTRO REPORTES:",
                $"Usuario: {_user}",
                $"BranchCode recibido: {_branchCode}",
                $"BranchCode normalizado: {_branchCode}",
                $"Sucursal activa: {reportBranch?.Name ?? _branchCode}",
                $"Rango inicio: {start:yyyy-MM-dd}",
                $"Rango fin: {end:yyyy-MM-dd}",
                $"Proveedor seleccionado: {report.Provider}",
                "Metodo llamado: CascoOperationsDataService.LoadActiveReportAsync -> LoadReportAsync",
                $"Cantidad devuelta: {report.OperationRows.Count}",
                $"Primer folio original: {report.OperationRows.FirstOrDefault()?.FolioOriginal ?? ""}",
                $"Primer taxista: {report.OperationRows.FirstOrDefault()?.Taxista ?? ""}",
                $"Sitios encontrados: {string.Join(", ", report.Sitios)}");
        }
        else
        {
            _cachedOperationPreviewRows = report.OperationRows.ToArray();
            _cachedCommissionPayments = report.PaymentRows.ToArray();
            _cachedReportCuts = (await _pos.GetCutsAsync())
                .Where(x => x.Date.Date >= start.Date && x.Date.Date <= end.Date)
                .ToArray();
        }

        if (loadId != Volatile.Read(ref _reportLoadSequence))
        {
            if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
            {
                WriteReportLog(
                    $"Carga descartada por ser obsoleta. loadId={loadId}",
                    $"Rango descartado: {start:yyyy-MM-dd} a {end:yyyy-MM-dd}");
            }
            return;
        }

        ReportOperationsGrid.ItemsSource = _cachedOperationPreviewRows;
        ReportCommissionPaymentsGrid.ItemsSource = _cachedCommissionPayments;
        ReportHeroPeriodText.Text = $"{start:dd/MM/yyyy} al {end:dd/MM/yyyy}";
        UpdateReportDashboard(_cachedOperationPreviewRows, _cachedCommissionPayments, _cachedReportCuts);
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            await Dispatcher.BeginInvoke(new Action(() =>
            {
                if (loadId != Volatile.Read(ref _reportLoadSequence))
                    return;
                WriteReportLog(
                    $"Cantidad asignada al grid: {ReportOperationsGrid.Items.Count}",
                    $"Cantidad final del grid: {ReportOperationsGrid.Items.Count}",
                    $"Cantidad final de tarjetas: {ReportMovementsText.Text} / {ReportPaxText.Text} / {ReportAmountText.Text}",
                    $"Movimientos: {ReportMovementsText.Text}",
                    $"PAX: {ReportPaxText.Text}",
                    $"Importe: {ReportAmountText.Text}");
            }));
        }
    }

    private async Task<IReadOnlyList<LocalOperationsPreviewRow>> GetOperationPreviewRowsAsync()
    {
        if (_cachedOperationPreviewRows.Count > 0) return _cachedOperationPreviewRows;
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var password = ResolveCascoSqlPassword();
            return (await CascoOperationsDataService.LoadReportAsync(
                new BranchConfigurationService().GetBranch("CV"),
                "CV",
                password,
                ReportStartDate,
                ReportEndDate,
                CancellationToken.None)).OperationRows;
        }

        return await _operations.GetOperationsReportPreviewAsync(ReportStartDate, ReportEndDate, GetCurrentSiteName());
    }

    private static IReadOnlyList<LocalOperationsPreviewRow> MapOperationPreviewRows(IEnumerable<LocalRelation> rows)
    {
        return rows.Select(x => new LocalOperationsPreviewRow(
            string.IsNullOrWhiteSpace(x.Driver) ? x.Vendor : x.Driver,
            x.DateText,
            x.Passengers,
            x.Hotel,
            x.Payout ?? 0m,
            x.Sale,
            x.Commission,
            x.CommissionPaid,
            x.OperationFolio,
            x.AppFolio,
            x.Badge,
            x.PayoutStatus,
            x.PayoutDate,
            x.PayoutUser,
            x.PayoutTicket,
            x.Site,
            x.Origin,
            x.Destination,
            x.Unit,
            x.Plates,
            x.TransportType,
            x.Notes,
            x.Vendor,
            x.AdultPassengers,
            x.YouthPassengers,
            x.ChildPassengers)).ToArray();
    }

    private async Task<IReadOnlyList<LocalCommissionPaymentPreviewRow>> GetCommissionPaymentPreviewRowsAsync()
    {
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var password = ResolveCascoSqlPassword();
            return (await CascoOperationsDataService.LoadReportAsync(
                new BranchConfigurationService().GetBranch("CV"),
                "CV",
                password,
                ReportStartDate,
                ReportEndDate,
                CancellationToken.None)).PaymentRows;
        }

        return (await _operations.GetCommissionPaymentsReportAsync(ReportStart.SelectedDate, ReportEnd.SelectedDate, GetCurrentSiteName())).ToArray();
    }

    private async Task ExportWorkbookSheetAsync<T>(string fileName, string sheetName, IEnumerable<T> rows)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = fileName };
        if (dialog.ShowDialog() == true) await _output.ExportOpenXmlWorkbookAsync(sheetName, rows, dialog.FileName);
    }

    private void ReportsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ReportOperationsGrid is null || ReportCommissionPaymentsGrid is null) return;
        UpdateReportDashboard(
            ReportOperationsGrid.Items.OfType<LocalOperationsPreviewRow>().ToArray(),
            ReportCommissionPaymentsGrid.Items.OfType<LocalCommissionPaymentPreviewRow>().ToArray(),
            Array.Empty<LocalCut>());
    }

    private void UpdateReportDashboard(IReadOnlyList<LocalOperationsPreviewRow> operationRows, IReadOnlyList<LocalCommissionPaymentPreviewRow> paymentRows, IReadOnlyList<LocalCut> cuts)
    {
        var isPayments = ReportsTabControl.SelectedIndex == 1;
        var distinctTaxistas = operationRows
            .Select(x => x.Taxista)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ReportOperationsGrid.Visibility = isPayments ? Visibility.Collapsed : Visibility.Visible;
        ReportCommissionPaymentsGrid.Visibility = isPayments ? Visibility.Visible : Visibility.Collapsed;
        ReportHeroTitleText.Text = isPayments ? "Pagos de comisiones" : "Operaciones del dia";
        ReportPreviewHeaderText.Text = isPayments ? "Reporte de pagos generados" : "Reporte de operaciones total";
        ReportPreviewMetaText.Text = isPayments
            ? paymentRows.Sum(x => x.Pago).ToString("C2", CultureInfo.CurrentCulture)
            : string.Join(", ", operationRows.Select(x => x.Sitio).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("Sin referencia"));

        ReportMovementsLabelText.Text = isPayments ? "PAGOS" : "MOVIMIENTOS";
        ReportMovementsText.Text = (isPayments ? paymentRows.Count : operationRows.Count).ToString("N0", CultureInfo.InvariantCulture);
        ReportPaxLabelText.Text = isPayments ? "PAGADO" : "PAX";
        ReportPaxText.Text = isPayments
            ? paymentRows.Sum(x => x.Pago).ToString("C2", CultureInfo.CurrentCulture)
            : operationRows.Sum(x => x.Pax).ToString("N0", CultureInfo.InvariantCulture);
        ReportAmountLabelText.Text = isPayments ? "COMISION" : "IMPORTE";
        ReportAmountText.Text = (isPayments
            ? paymentRows.Sum(x => x.Comision)
            : operationRows.Sum(x => x.Importe)).ToString("C2", CultureInfo.CurrentCulture);
        ReportDriverLabelText.Text = isPayments ? "SALDO" : "TAXISTAS UNICOS";
        ReportDriverText.Text = isPayments
            ? paymentRows.Sum(x => x.Comision - x.Pago).ToString("C2", CultureInfo.CurrentCulture)
            : distinctTaxistas.Length switch
            {
                0 => "Sin movimientos",
                1 => "1 taxista unico",
                _ => $"{distinctTaxistas.Length:N0} taxistas unicos"
            };

        SetReportPrintPreview(BuildReportPrintablePreview(
            operationRows,
            paymentRows,
            isPayments,
            ReportStartDate,
            ReportEndDate));
    }

    private static string BuildReportPrintablePreview(IReadOnlyList<LocalOperationsPreviewRow> operationRows, IReadOnlyList<LocalCommissionPaymentPreviewRow> paymentRows, bool isPayments, DateTime start, DateTime end)
    {
        var lines = new List<string>
        {
            isPayments ? "** REPORTE DE PAGOS DE COMISIONES **" : "** REPORTE DE OPERACIONES TOTAL **",
            string.Empty,
            $"Fecha del Reporte : {start:dd/MM/yyyy} al {end:dd/MM/yyyy}",
            $"{new string(' ', 96)}Page 1 of 1",
            string.Empty
        };

        if (isPayments)
        {
            lines.Add(
                Pad("Folio", 12) +
                Pad("Folio local", 12) +
                Pad("Fecha pago", 18) +
                Pad("Taxista", 28) +
                Pad("Gafete", 10) +
                Pad("Transporte", 18) +
                Pad("Dejada", 12, true) +
                Pad("Importe", 12, true) +
                Pad("Comision", 12, true) +
                Pad("Pago", 12, true) +
                Pad("Estatus", 12));
            lines.AddRange(paymentRows.Select(x =>
                Pad(x.Folio, 12) +
                Pad(x.FolioLocal, 12) +
                Pad(x.FechaPago, 18) +
                Pad(x.Taxista, 28) +
                Pad(x.Gafete, 10) +
                Pad(x.Transporte, 18) +
                Pad(x.Dejada.ToString("0.00", CultureInfo.InvariantCulture), 12, true) +
                Pad(x.Importe.ToString("0.00", CultureInfo.InvariantCulture), 12, true) +
                Pad(x.Comision.ToString("0.00", CultureInfo.InvariantCulture), 12, true) +
                Pad(x.Pago.ToString("0.00", CultureInfo.InvariantCulture), 12, true) +
                Pad(x.Estatus, 12)));
        }
        else
        {
            lines.Add(
                Pad("folio orig", 12) +
                Pad("folio local", 12) +
                Pad("fecha", 18) +
                Pad("taxista", 28) +
                Pad("gafete", 10) +
                Pad("PAX", 6) +
                Pad("HOTEL", 20) +
                Pad("ORIGEN", 18) +
                Pad("DESTINO", 18) +
                Pad("SUCURSAL", 14) +
                Pad("UNIDAD", 10) +
                Pad("PLACAS", 10) +
                Pad("TIPO", 18) +
                Pad("DEJADA", 10, true) +
                Pad("IMPORTE", 10, true) +
                Pad("COMISION", 10, true) +
                Pad("ESTADO", 12));
            lines.AddRange(operationRows.Select(x =>
                Pad(x.FolioOriginal, 12) +
                Pad(x.FolioLocal, 12) +
                Pad(x.Fecha, 18) +
                Pad(x.Taxista, 28) +
                Pad(x.Gafete, 10) +
                Pad(x.Pax.ToString(CultureInfo.InvariantCulture), 6) +
                Pad(x.Hotel, 20) +
                Pad(x.Origen, 18) +
                Pad(x.Destino, 18) +
                Pad(x.Sitio, 14) +
                Pad(x.Unidad, 10) +
                Pad(x.Placas, 10) +
                Pad(x.TipoServicio, 18) +
                Pad(x.Dejada.ToString("0.00", CultureInfo.InvariantCulture), 10, true) +
                Pad(x.Importe.ToString("0.00", CultureInfo.InvariantCulture), 10, true) +
                Pad(x.Comision.ToString("0.00", CultureInfo.InvariantCulture), 10, true) +
                Pad(x.Estatus, 12)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Pad(string? value, int width, bool alignRight = false)
    {
        var clean = (value ?? string.Empty).Replace(Environment.NewLine, " ").Trim();
        if (clean.Length > width) clean = clean[..width];
        return alignRight ? clean.PadLeft(width) : clean.PadRight(width);
    }

    private sealed class CommissionSelectionRow : INotifyPropertyChanged
    {
        public CommissionSelectionRow(LocalCommissionBrowserRow source) => Source = source;
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LocalCommissionBrowserRow Source { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    private void SetReportPrintPreview(string content)
    {
        ReportPrintPreviewText.Text = content;
        ReportPrintPreviewText.ScrollToHome();
    }

    private void WriteReportLog(params string[] lines)
    {
        var directory = Path.GetDirectoryName(ReportLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.AppendAllText(
            ReportLogPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {string.Join(Environment.NewLine, lines)}{Environment.NewLine}{Environment.NewLine}");
    }

    private string ResolveCascoSqlPassword()
    {
        var password = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(password))
            return password;

        if (!IsCascoBranch)
            return string.Empty;

        if (CascoCredentialStore.TryApplyToEnvironment(out var error))
            return Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? string.Empty;

        WritePosLog("No se pudo aplicar la credencial cifrada de Casco.", error);
        return string.Empty;
    }

    private void WritePosLog(string message, Exception? exception = null)
    {
        WritePosLog(message, exception?.GetType().Name ?? string.Empty, exception?.Message ?? string.Empty);
    }

    private void WritePosLog(string message, string detail)
    {
        WritePosLog(message, string.Empty, detail);
    }

    private void WritePosLog(string message, string exceptionType, string detail)
    {
        if (!IsCascoBranch)
            return;

        var directory = Path.GetDirectoryName(PosLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var sanitizedDetail = (detail ?? string.Empty)
            .Replace(Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD") ?? "\0", "***", StringComparison.Ordinal);
        File.AppendAllText(
            PosLogPath,
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}"
            + (string.IsNullOrWhiteSpace(exceptionType) ? string.Empty : $" {exceptionType}: {sanitizedDetail}")
            + Environment.NewLine);
    }

    private async Task EnsureDetailedReportRelationsAsync()
    {
        if (_cachedReportRelations.Count > 0) return;
        _cachedReportRelations = (await _operations.GetReportRelationsAsync(ReportStartDate, ReportEndDate, GetCurrentSiteName())).ToArray();
    }

    private async Task<IReadOnlyList<LocalRelation>> GetReportWorkbookRelationsAsync()
    {
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
        {
            var password = ResolveCascoSqlPassword();
            return await CascoOperationsDataService.LoadRelationsAsync(
                new BranchConfigurationService().GetBranch("CV"),
                "CV",
                password,
                start: ReportStartDate,
                end: ReportEndDate,
                cancellationToken: CancellationToken.None);
        }

        return MapReportWorkbookRelations(await GetOperationPreviewRowsAsync());
    }

    private async Task<IReadOnlyList<LocalCommission>> GetReportWorkbookCommissionsAsync()
    {
        if (string.Equals(_branchCode, "CV", StringComparison.OrdinalIgnoreCase))
            return MapReportWorkbookCommissions(await GetCommissionPaymentPreviewRowsAsync());

        return await _pos.GetCommissionsAsync();
    }

    private static IReadOnlyList<LocalRelation> MapReportWorkbookRelations(IEnumerable<LocalOperationsPreviewRow> rows)
    {
        return rows.Select((row, index) => new LocalRelation(
            index + 1,
            row.FolioLocal,
            row.FolioOriginal,
            string.Empty,
            row.Gafete,
            row.Taxista,
            row.Vendedor,
            row.Dejada,
            row.Notas,
            Source: "Casco Reporte",
            SourceUser: row.UsuarioPago,
            DateText: row.Fecha,
            Hotel: row.Hotel,
            Origin: row.Origen,
            Site: row.Sitio,
            Destination: row.Destino,
            Unit: row.Unidad,
            Plates: row.Placas,
            Phone: string.Empty,
            Nationality: string.Empty,
            TransportType: row.TipoServicio,
            Sale: row.Importe,
            Commission: row.Comision,
            CommissionPaid: row.Pago,
            PaymentMethod: string.Empty,
            PayoutStatus: row.Estatus,
            CommissionStatus: row.Estatus,
            PayoutTicket: row.TicketPago,
            TaxistaId: string.Empty,
            PayoutUser: row.UsuarioPago,
            PayoutDate: row.FechaPago,
            PayoutPaid: row.Pago,
            Passengers: row.Pax,
            SaleDetail: string.Empty,
            Currency: "MXN",
            RemotePaymentMethod: string.Empty,
            PaymentsJson: string.Empty,
            TotalAmount: row.Importe,
            CashAmount: 0m,
            CardAmount: 0m,
            DollarsAmount: 0m,
            ExchangeRate: 0m,
            AdultPassengers: row.Adulto,
            YouthPassengers: row.Joven,
            ChildPassengers: row.Nino)).ToArray();
    }

    private static IReadOnlyList<LocalCommission> MapReportWorkbookCommissions(IEnumerable<LocalCommissionPaymentPreviewRow> rows)
    {
        return rows.Select((row, index) => new LocalCommission(
            index + 1,
            row.Folio,
            row.FolioLocal,
            row.Gafete,
            row.Taxista,
            TryParsePreviewDate(row.FechaPago),
            row.Importe,
            row.Comision,
            row.Pago,
            Math.Max(row.Comision - row.Pago, 0m),
            string.IsNullOrWhiteSpace(row.Estatus) ? "PENDIENTE" : row.Estatus)).ToArray();
    }

    private static DateTime TryParsePreviewDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DateTime.Today;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariant))
            return invariant;
        if (DateTime.TryParse(value, new CultureInfo("es-MX"), DateTimeStyles.AllowWhiteSpaces, out var mexican))
            return mexican;
        return DateTime.TryParse(value, out var generic) ? generic : DateTime.Today;
    }

    private static DateTime ParsePickerDate(DatePicker picker, DateTime fallback)
    {
        var text = (picker.Text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(text)
            && DateTime.TryParseExact(text, ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
            return exact.Date;
        if (!string.IsNullOrWhiteSpace(text)
            && DateTime.TryParseExact(text, ["dd/MM/yyyy", "d/M/yyyy"], new CultureInfo("es-MX"), DateTimeStyles.None, out var mexican))
            return mexican.Date;
        if (!string.IsNullOrWhiteSpace(text)
            && DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var current))
            return current.Date;
        if (picker.SelectedDate.HasValue)
            return picker.SelectedDate.Value.Date;
        return fallback.Date;
    }

    private async Task ExportTableAsync<T>(string fileName, string title, IEnumerable<T> rows, bool excel)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = excel ? "Excel (*.xls)|*.xls" : "PDF (*.pdf)|*.pdf", FileName = fileName };
        if (dialog.ShowDialog() != true) return;
        if (excel) await _output.ExportSpreadsheetXmlAsync(title, rows, dialog.FileName);
        else await _output.ExportSimplePdfAsync(title, rows, dialog.FileName);
    }

    private async Task ExportOpenXmlAsync<T>(string fileName, string sheetName, IEnumerable<T> rows)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = fileName };
        if (dialog.ShowDialog() == true) await _output.ExportOpenXmlWorkbookAsync(sheetName, rows, dialog.FileName);
    }

    private async Task ExportCsvAsync<T>(string fileName, IEnumerable<T> rows)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = fileName };
        if (dialog.ShowDialog() == true) await _output.ExportCsvAsync(rows, dialog.FileName);
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            await _errors.LogAsync(_user, "POS", "Error de pantalla", ex);
            WebDialogWindow.Show(this, "No se pudo completar la operacion. " + ex.Message, "Control Taxi", "!");
        }
    }

    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    private static int Integer(string value) => int.Parse(value, CultureInfo.InvariantCulture);
    private static long? SelectedLong(object? value) => value is long id ? id : value is int number ? number : null;
    private static string SelectedText(ComboBox comboBox) => ((ComboBoxItem)comboBox.SelectedItem).Content.ToString() ?? string.Empty;
    private static string FirstFilled(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    private static string BuildSalePaymentDetail(decimal totalPagos, string moneda, string formaPago)
    {
        if (totalPagos <= 0m)
            return string.Empty;

        var amount = totalPagos.ToString("C2", CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(moneda)
            ? $"{amount} | {formaPago}"
            : $"{amount} | {moneda} | {formaPago}";
    }
}

