using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;
using Microsoft.Win32;

namespace ControlTaxiDesktop;

public partial class CommissionSettingsWindow : Window
{
    private readonly CommissionSettingsRepository _settings;
    private readonly string _user;
    private readonly bool _canEdit;
    private long _editingId;
    private bool _isSaving;

    private readonly string _branchCode;

    public CommissionSettingsWindow(LocalDatabase database, string user, bool canEdit, string branchCode)
    {
        _settings = new CommissionSettingsRepository(database);
        _user = string.IsNullOrWhiteSpace(user) ? Environment.UserName : user;
        _canEdit = canEdit;
        _branchCode = string.IsNullOrWhiteSpace(branchCode) ? string.Empty : branchCode.Trim().ToUpperInvariant();
        InitializeComponent();
        var isCv = _branchCode == "CV";
        CvSmallPayoutColumn.Visibility = CvLargePayoutColumn.Visibility = CvTastingColumn.Visibility = isCv ? Visibility.Visible : Visibility.Collapsed;
        LegacyPayoutColumn.Visibility = isCv ? Visibility.Collapsed : Visibility.Visible;
        SimAdultsPanel.Visibility = isCv ? Visibility.Visible : Visibility.Collapsed;
        SimPayout.IsReadOnly = isCv;
        if (isCv) SimPayoutLabel.Text = "DEJADA SEGÚN ADULTOS (CALCULADA)";
        UpdateCvPayoutEditor();
        SimDate.SelectedDate = DateTime.Today;
        VigencyFilter.SelectedDate = DateTime.Today;
        EditFrom.SelectedDate = DateTime.Today;
        NewRuleButton.IsEnabled = _canEdit;
        SaveRuleButton.IsEnabled = _canEdit;
        EditRuleButton.IsEnabled = _canEdit;
        QuickCommissionButton.IsEnabled = _canEdit;
        RetireTestRuleButton.IsEnabled = _canEdit;
        Loaded += async (_, _) =>
        {
            await RefreshAllAsync();
            SearchBox.Focus();
        };
        // NewRule enablement for TRANSPORTE will be checked at NewRule click time using _branchCode.
    }

    /// <summary>
    /// Un solo lugar para los avisos: el mensaje del editor vive dentro del panel emergente,
    /// asi que cuando el panel esta cerrado el mismo texto se muestra junto a los filtros.
    /// Sin esto, avisos como "Selecciona una regla" quedaban invisibles.
    /// </summary>
    private string EditorMessageText
    {
        set
        {
            EditorMessage.Text = value;
            CatalogStatusText.Text = value;
            CatalogStatusText.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OpenEditor(string title)
    {
        EditorTitle.Text = title;
        EditorOverlay.Visibility = Visibility.Visible;
        EditName.Focus();
    }

    private void CloseEditor_Click(object sender, RoutedEventArgs e) => EditorOverlay.Visibility = Visibility.Collapsed;

    private async Task RefreshAllAsync()
    {
        await _settings.InitializeAsync();
        await RefreshSummaryAsync();
        await RefreshRulesAsync();
        await RefreshAuditAsync();
        await RefreshDiagnosticsAsync();
        await RefreshSimulatorCatalogsAsync();
        await RefreshGlobalRulesAsync();
    }

    // ===== Reglas globales =====

    private async Task RefreshGlobalRulesAsync()
    {
        var umbral = await _settings.GetGlobalSettingAsync(
            CommissionSettingsRepository.PayoutDeductionMinSaleKey,
            CommissionGlobalRules.DefaultPayoutDeductionMinSale);

        PayoutThresholdBox.Text = umbral.ToString("0.##", CultureInfo.InvariantCulture);
        PayoutThresholdBox.IsEnabled = _canEdit;
        UpdatePayoutThresholdExample(umbral);
    }

    private void UpdatePayoutThresholdExample(decimal umbral)
    {
        var arriba = umbral + 1m;
        PayoutThresholdExample.Text =
            $"Con una dejada de $200.00 y 10% de comisión:{Environment.NewLine}"
            + $"  • Venta de {arriba:C2}  →  sí se descuenta la dejada  →  comisión sobre {(arriba - 200m):C2}{Environment.NewLine}"
            + $"  • Venta de {umbral:C2}  →  NO se descuenta la dejada  →  comisión sobre {umbral:C2}";
    }

    private async void SavePayoutThreshold_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit)
        {
            PayoutThresholdStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B));
            PayoutThresholdStatus.Text = "Tu usuario no tiene permiso para cambiar esta regla.";
            return;
        }

        var texto = (PayoutThresholdBox.Text ?? string.Empty).Trim().Replace("$", string.Empty).Replace(",", string.Empty);
        if (!decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var umbral) || umbral < 0m)
        {
            PayoutThresholdStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x2B));
            PayoutThresholdStatus.Text = "Escribe un monto válido (por ejemplo 400). No se guardó nada.";
            return;
        }

        await _settings.SetGlobalSettingAsync(
            CommissionSettingsRepository.PayoutDeductionMinSaleKey,
            umbral,
            _user,
            "Venta maxima en la que NO se descuenta la dejada de la base de comision.");

        // Se aplica de inmediato, sin reiniciar: el calculo lee de aqui.
        CommissionGlobalRules.ConfigurePayoutDeductionMinSale(umbral);
        UpdatePayoutThresholdExample(umbral);

        PayoutThresholdStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0F, 0x98, 0x8F));
        PayoutThresholdStatus.Text =
            $"Guardado. En ventas de hasta {umbral:C2} ya no se descuenta la dejada. "
            + "Vuelve a buscar en Comisiones para ver el cálculo actualizado.";
    }

    private async Task RefreshSimulatorCatalogsAsync()
    {
        var transportItem = SimTransport.SelectedItem;
        var paymentItem = SimPayment.SelectedItem;
        // Filter transport rules by session branch: if branch is CV show only CV rules.
        var allTransports = await _settings.GetRulesAsync("TRANSPORTE", active: true, date: null, sessionBranch: _branchCode);
        var transports = allTransports;
        SimTransport.ItemsSource = transports
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SimPayment.ItemsSource = (await _settings.GetRulesAsync("FORMA_PAGO", active: true, date: null, sessionBranch: _branchCode))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        SimTransport.SelectedItem = transportItem;
        SimPayment.SelectedItem = paymentItem;
    }

    private async Task RefreshSummaryAsync()
    {
        var summary = await _settings.GetSummaryAsync(_branchCode);
        var diagnostics = await _settings.GetDiagnosticsAsync();
        ActiveRulesCard.Text = summary.ActiveRules.ToString("N0", CultureInfo.CurrentCulture);
        TransportRulesCard.Text = summary.TransportRules.ToString("N0", CultureInfo.CurrentCulture);
        FallbackRulesCard.Text = diagnostics.Count(x => x.Source.Contains("FALLBACK", StringComparison.OrdinalIgnoreCase)).ToString("N0", CultureInfo.CurrentCulture);
        ExpiringRulesCard.Text = summary.ExpiringSoon.ToString("N0", CultureInfo.CurrentCulture);
        RecentChangesCard.Text = summary.RecentChanges.ToString("N0", CultureInfo.CurrentCulture);
    }

    private async Task RefreshRulesAsync()
    {
        var status = SelectedComboText(StatusFilter);
        var rules = await _settings.GetRulesAsync(
            CategoryCode(SelectedComboText(CategoryFilter)),
            SearchBox.Text,
            ActiveFilter(status),
            DateFilter(status), _branchCode);

        if (IsExpiringFilter(status))
        {
            var today = DateTime.Today;
            var limit = today.AddDays(30);
            rules = rules
                .Where(x => x.Active && x.EffectiveTo is not null && x.EffectiveTo.Value.Date >= today && x.EffectiveTo.Value.Date <= limit)
                .ToArray();
        }

        RulesGrid.ItemsSource = rules;
        // Conteo visible junto a los filtros, igual que el "REGISTROS x DE y" de Comisiones:
        // sin el no se sabe si un filtro dejo fuera media tabla.
        RulesCountText.Text = rules.Count == 1 ? "1 REGLA" : $"{rules.Count:N0} REGLAS";
    }

    private async void ClearRuleFilters_Click(object sender, RoutedEventArgs e)
    {
        VigencyFilter.SelectedDate = DateTime.Today;
        await ApplyRuleFilterAsync("Todos", "Activos");
    }

    private async Task ApplyRuleFilterAsync(string category, string status, string search = "")
    {
        SetComboText(CategoryFilter, category);
        SetComboText(StatusFilter, status);
        SearchBox.Text = search;
        await SafeRefreshRulesAsync();
    }

    private static string CategoryCode(string text) => text.ToUpperInvariant() switch
    {
        "TRANSPORTES" => "TRANSPORTE",
        "FORMAS DE PAGO" => "FORMA_PAGO",
        "JOYERÍA" or "JOYERIA" => "JOYERIA",
        "TEQUILA" => "TEQUILA",
        "GUÍAS" or "GUIAS" => "GUIA",
        "DEJADAS" => "DEJADA",
        "GASTOS" => "GASTO",
        "GENERAL" => "GENERAL",
        _ => string.Empty,
    };

    private static bool? ActiveFilter(string status) => status.ToUpperInvariant() switch
    {
        "ACTIVOS" or "VIGENTES" or "POR VENCER" => true,
        "INACTIVOS" => false,
        _ => null,
    };

    private DateTime? DateFilter(string status) =>
        string.Equals(status, "Vigentes", StringComparison.OrdinalIgnoreCase)
            ? VigencyFilter.SelectedDate
            : null;

    private static bool IsExpiringFilter(string status) =>
        string.Equals(status, "Por vencer", StringComparison.OrdinalIgnoreCase);

    private async Task RefreshAuditAsync() => AuditGrid.ItemsSource = await _settings.GetAuditAsync();
    private async Task RefreshDiagnosticsAsync() => DiagnosticsGrid.ItemsSource = await _settings.GetDiagnosticsAsync();

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => await SafeRefreshRulesAsync();
    private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => await SafeRefreshRulesAsync();
    private async void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => await SafeRefreshRulesAsync();
    private async void Filter_Changed(object sender, RoutedEventArgs e) => await SafeRefreshRulesAsync();
    private async void VigencyFilter_SelectedDateChanged(object? sender, SelectionChangedEventArgs e) => await SafeRefreshRulesAsync();

    private async Task SafeRefreshRulesAsync()
    {
        if (!IsLoaded || RulesGrid is null) return;
        try { await RefreshRulesAsync(); }
        catch (Exception ex) { EditorMessageText = ex.Message; }
    }

    private void RulesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not CommissionSettingsRule rule)
            return;
        LoadEditor(rule);
        FallbackAlert.Visibility = UsesFallback(rule) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadEditor(CommissionSettingsRule rule)
    {
        _editingId = rule.Id;
        SetComboText(EditCategory, rule.Category);
        EditCode.Text = rule.Code;
        EditName.Text = rule.Name;
        EditCommission.Text = rule.CommissionPercent.ToString("0.####", CultureInfo.InvariantCulture);
        EditCash.Text = rule.CashRetentionPercent.ToString("0.####", CultureInfo.InvariantCulture);
        EditCard.Text = rule.CardRetentionPercent.ToString("0.####", CultureInfo.InvariantCulture);
        EditAmex.Text = rule.AmexRetentionPercent.ToString("0.####", CultureInfo.InvariantCulture);
        SetComboText(EditPaymentKind, rule.PaymentKind);
        EditPayout.IsChecked = rule.AppliesPayout;
        EditPayoutAmount.Text = rule.PayoutAmount.ToString("0.##", CultureInfo.InvariantCulture);
        EditCvPayoutSmall.Text = rule.CvPayoutOneToFourAdults?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        EditCvPayoutLarge.Text = rule.CvPayoutFiveOrMoreAdults?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
        EditCvTasting.Text = rule.CvTastingPercent?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty;
        SetComboText(EditPaxKind, rule.PaxKind);
        EditMonedaId.Text = rule.MonedaId.ToString(CultureInfo.InvariantCulture);
        EditExpense.IsChecked = rule.AppliesExpense;
        EditActive.IsChecked = rule.Active;
        EditFrom.SelectedDate = rule.EffectiveFrom;
        EditTo.SelectedDate = rule.EffectiveTo;
        EditNotes.Text = rule.Notes;
        EditReason.Clear();
        EditorHint.Text = _canEdit
            ? "Puedes editar la regla seleccionada. Antes de guardar se mostrará un resumen y se pedirá motivo."
            : "Tu usuario puede consultar y probar comisiones, pero no guardar cambios.";
        EditorMessageText = _canEdit ? "Regla cargada. Revisa porcentajes, vigencia y motivo antes de guardar." : "Tu usuario puede consultar, pero no guardar cambios.";
    }

    private void NewRule_Click(object sender, RoutedEventArgs e)
    {
        var category = MessageBox.Show(
            this,
            "¿Qué deseas configurar?\n\nSí = Transporte\nNo = Forma de pago\nCancelar = elegir manualmente en el formulario",
            "Nueva regla",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        _editingId = 0;
        if (category == MessageBoxResult.Yes)
            SetComboText(EditCategory, "TRANSPORTE");
        else if (category == MessageBoxResult.No)
            SetComboText(EditCategory, "FORMA_PAGO");
        else
            SetComboText(EditCategory, "TRANSPORTE");
        EditCode.Clear();
        EditName.Clear();
        EditCommission.Text = "0";
        EditCash.Text = "0";
        EditCard.Text = "19";
        EditAmex.Text = "24";
        SetComboText(EditPaymentKind, string.Empty);
        EditPayout.IsChecked = true;
        EditPayoutAmount.Text = "0";
        EditCvPayoutSmall.Clear();
        EditCvPayoutLarge.Clear();
        EditCvTasting.Clear();
        EditPaxKind.SelectedIndex = 0;
        EditMonedaId.Text = "-1";
        EditExpense.IsChecked = true;
        EditActive.IsChecked = true;
        EditFrom.SelectedDate = DateTime.Today;
        EditTo.SelectedDate = null;
        EditNotes.Clear();
        EditReason.Clear();
        EditorHint.Text = "Nueva regla: captura nombre, porcentajes, vigencia y motivo. Los porcentajes se escriben como 10 para 10%.";
        EditorMessageText = "Captura la nueva regla. El motivo es obligatorio.";
        OpenEditor("NUEVA REGLA");
    }

    private async void SaveRule_Click(object sender, RoutedEventArgs e)
    {
        if (_isSaving)
            return;

        if (!_canEdit)
        {
            EditorMessageText = "Tu usuario puede consultar esta pantalla, pero no tiene permiso para guardar cambios.";
            return;
        }

        try
        {
            _isSaving = true;
            SaveRuleButton.IsEnabled = false;
            NewRuleButton.IsEnabled = false;
            EditorMessageText = "Guardando configuración...";
            var previous = _editingId > 0 ? RulesGrid.SelectedItem as CommissionSettingsRule : null;
            var rule = new CommissionSettingsRule(
                _editingId,
                SelectedComboText(EditCategory),
                EditCode.Text,
                EditName.Text,
                Percent(EditCommission.Text, "Comisión"),
                Percent(EditCash.Text, "Efectivo"),
                Percent(EditCard.Text, "Tarjeta"),
                Percent(EditAmex.Text, "AMEX"),
                SelectedComboText(EditPaymentKind),
                EditPayout.IsChecked == true,
                EditExpense.IsChecked == true,
                EditActive.IsChecked == true,
                EditFrom.SelectedDate ?? DateTime.Today,
                EditTo.SelectedDate,
                string.Empty,
                _user,
                EditNotes.Text,
                Money(EditPayoutAmount.Text, "Dejada"),
                SelectedComboText(EditPaxKind),
                Entero(EditMonedaId.Text))
            {
                Branch = SelectedComboText(EditCategory) == "TRANSPORTE" ? _branchCode : string.Empty,
                CvPayoutOneToFourAdults = IsCvTransportEditor ? OptionalMoney(EditCvPayoutSmall.Text, "Dejada 1–4 adultos") : null,
                CvPayoutFiveOrMoreAdults = IsCvTransportEditor ? OptionalMoney(EditCvPayoutLarge.Text, "Dejada 5 o más") : null,
                CvTastingPercent = IsCvTransportEditor ? OptionalPercent(EditCvTasting.Text, "Degustación") : null
            };

            if (previous is not null && previous.Active && !rule.Active)
            {
                var deactivate = "Esta regla dejará de aplicarse a nuevas operaciones según su vigencia. ¿Deseas continuar?";
                if (MessageBox.Show(this, deactivate, "Confirmar desactivación", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    return;
            }

            var previousText = previous is null
                ? "Nueva regla"
                : $"{previous.Name}\nComisión: {previous.CommissionPercent:0.##}% -> {rule.CommissionPercent:0.##}%\nEfectivo: {previous.CashRetentionPercent:0.##}% -> {rule.CashRetentionPercent:0.##}%\nTarjeta: {previous.CardRetentionPercent:0.##}% -> {rule.CardRetentionPercent:0.##}%\nAMEX: {previous.AmexRetentionPercent:0.##}% -> {rule.AmexRetentionPercent:0.##}%";
            // EDITAR corrige la regla que ya existe (mismo Id): el operador quiere arreglar ESA
            // comision, no dejar dos reglas con vigencias distintas. Para abrir una vigencia
            // nueva esta CAMBIAR COMISIÓN con una fecha posterior.
            var isEdit = _editingId > 0;
            var message = isEdit
                ? $"Vas a corregir esta regla (no se crea otra):\n\n{rule.Name}\n\n{previousText}\nVigencia: {rule.EffectiveRange}\nMotivo: {EditReason.Text.Trim()}\n\nSelecciona Sí para confirmar el cambio o No para cancelar."
                : $"Vas a dar de alta:\n\n{rule.Name}\n\nVigencia: {rule.EffectiveRange}\nMotivo: {EditReason.Text.Trim()}\n\nSelecciona Sí para confirmar el alta o No para cancelar.";
            if (IsCvTransportEditor)
                message += $"\nDegustación: {previous?.CvTastingDisplay ?? "Pendiente"} -> {rule.CvTastingDisplay}\nDejada 1–4 adultos: {previous?.CvPayoutOneToFourDisplay ?? "Pendiente"} -> {rule.CvPayoutOneToFourDisplay}\nDejada 5 o más: {previous?.CvPayoutFiveOrMoreDisplay ?? "Pendiente"} -> {rule.CvPayoutFiveOrMoreDisplay}";
            if (MessageBox.Show(this, message, isEdit ? "Confirmar corrección de comisión" : "Confirmar alta de comisión", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            // Attach current session branch to the rule using branch passed to the window (_branchCode).
            rule = rule with { Branch = rule.Category == "TRANSPORTE" ? _branchCode : string.Empty };
            if (isEdit)
                await _settings.UpdateRuleAsync(rule, _user, EditReason.Text, _canEdit);
            else
                await _settings.SaveRuleAsync(rule, _user, EditReason.Text, _canEdit);

            EditorMessageText = isEdit
                ? "La comisión se corrigió sobre la misma regla. El cambio quedó guardado en auditoría."
                : "La regla se dio de alta correctamente. El alta quedó guardada en auditoría.";
            EditorOverlay.Visibility = Visibility.Collapsed;
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            EditorMessageText = FriendlyError(ex);
        }
        finally
        {
            _isSaving = false;
            SaveRuleButton.IsEnabled = _canEdit;
            NewRuleButton.IsEnabled = _canEdit;
        }
    }

    private bool IsCvTransportEditor => _branchCode == "CV" && SelectedComboText(EditCategory) == "TRANSPORTE";

    private void EditCategory_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCvPayoutEditor();

    private void UpdateCvPayoutEditor()
    {
        if (CvPayoutEditor is null || EditCategory is null) return;
        CvPayoutEditor.Visibility = IsCvTransportEditor ? Visibility.Visible : Visibility.Collapsed;
        CvTastingEditor.Visibility = IsCvTransportEditor ? Visibility.Visible : Visibility.Collapsed;
        EditPayoutAmount.Visibility = LegacyPayoutLabel.Visibility = IsCvTransportEditor ? Visibility.Collapsed : Visibility.Visible;
        EditCvPayoutSmall.IsEnabled = EditCvPayoutLarge.IsEnabled = _canEdit;
        EditCvTasting.IsEnabled = _canEdit;
    }

    private static decimal? OptionalMoney(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0m)
            throw new InvalidOperationException($"{label}: escribe un importe válido no negativo, o déjalo vacío si está pendiente.");
        return amount;
    }

    private static decimal? OptionalPercent(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (!decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            && !decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out value))
            throw new InvalidOperationException($"{label}: escribe un porcentaje válido entre 0 y 100, o déjalo vacío si está pendiente.");
        if (value < 0m || value > 100m)
            throw new InvalidOperationException($"{label}: el porcentaje debe estar entre 0 y 100, o quedar vacío si está pendiente.");
        return value;
    }

    private int? ReadSimulatorAdults()
    {
        if (string.IsNullOrWhiteSpace(SimAdults.Text)) return null;
        if (!int.TryParse(SimAdults.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var adults))
            throw new InvalidOperationException("Adultos debe ser un número entero no negativo; no se calcula con PAX total.");
        return adults;
    }

    private async void Simulate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _settings.SimulateAsync(new CommissionSimulationInput(
                SimDate.SelectedDate ?? DateTime.Today,
                SimTransport.Text,
                Number(SimSale.Text),
                Number(SimSubtotal.Text),
                SimPayment.Text,
                0m,
                0m,
                0m,
                _branchCode == "CV" ? 0m : Number(SimPayout.Text),
                Number(SimExpense.Text),
                string.Empty, _branchCode == "CV" ? ReadSimulatorAdults() : null), _branchCode);
            if (_branchCode == "CV") SimPayout.Text = result.Configured ? result.Payout.ToString("0.##", CultureInfo.InvariantCulture) : "Pendiente";
            SimulationResult.Text =
                $"Venta: {Number(SimSale.Text):C2}\n"
                + $"Forma de pago: {SimPayment.Text}\n"
                + $"Retención: {result.RetentionPercent:0.##}%\n"
                + $"Base: {result.BaseAmount:C2}\n"
                + $"Porcentaje comisión: {result.CommissionPercent:0.##}%\n"
                + $"Comisión final: {result.FinalCommission:C2}\n\n"
                + "¿Cómo se calculó?\n"
                + result.Explanation
                + Environment.NewLine
                + (result.Configured ? "Estado: regla configurada." : "Estado: falta configuración válida; revisa el detalle antes de liquidar.");
        }
        catch (Exception ex)
        {
            SimulationResult.Text = FriendlyError(ex);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel CSV (*.csv)|*.csv",
                FileName = "catalogo_comisiones.csv"
            };
            if (dialog.ShowDialog(this) != true) return;
            await _settings.ExportCatalogCsvAsync(dialog.FileName, _branchCode);
            MessageBox.Show(this, "El catálogo CSV se exportó correctamente.", "Control Taxi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            EditorMessageText = FriendlyError(ex);
        }
    }

    private async void ExportExcel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel (*.xls)|*.xls",
                FileName = "catalogo_comisiones.xls"
            };
            if (dialog.ShowDialog(this) != true) return;
            await _settings.ExportCatalogExcelAsync(dialog.FileName, _branchCode);
            MessageBox.Show(this, "El catálogo se exportó a Excel correctamente.", "Control Taxi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            EditorMessageText = FriendlyError(ex);
        }
    }

    private void EditSelectedRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is CommissionSettingsRule rule)
        {
            LoadEditor(rule);
            OpenEditor($"EDITAR: {rule.Name}");
            EditCommission.Focus();
            return;
        }
        EditorMessageText = "Selecciona una regla para editar.";
    }

    private void ViewHistory_Click(object sender, RoutedEventArgs e) => SettingsTabs.SelectedIndex = 2;

    private void RulesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RulesGrid.SelectedItem is not CommissionSettingsRule rule)
            return;
        if (_canEdit)
        {
            OpenQuickCommissionDialog(rule);
        }
        else
        {
            LoadEditor(rule);
            OpenEditor($"CONSULTA: {rule.Name}");
            EditorMessageText = "Detalle cargado en modo consulta. Tu usuario no puede guardar cambios.";
        }
    }

    private async void SummaryCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag)
            return;

        switch (tag)
        {
            case "ACTIVOS":
                await ApplyRuleFilterAsync("Todos", "Activos");
                SettingsTabs.SelectedIndex = 0;
                break;
            case "TRANSPORTE":
                await ApplyRuleFilterAsync("Transportes", "Activos");
                SettingsTabs.SelectedIndex = 0;
                break;
            // Los diagnosticos (de donde sale el conteo de fallback) viven en "Opciones
            // avanzadas", que es la pestaña 4. Con el 3 la tarjeta abria "Reglas globales".
            case "FALLBACK":
                SettingsTabs.SelectedIndex = 4;
                break;
            case "POR_VENCER":
                await ApplyRuleFilterAsync("Todos", "Por vencer");
                SettingsTabs.SelectedIndex = 0;
                break;
            case "CAMBIOS":
                SettingsTabs.SelectedIndex = 2;
                break;
        }
    }

    private void ClearSimulator_Click(object sender, RoutedEventArgs e)
    {
        SimDate.SelectedDate = DateTime.Today;
        SimTransport.SelectedIndex = -1;
        SimTransport.Text = string.Empty;
        SimSale.Text = "0";
        SimSubtotal.Text = "0";
        SimPayment.SelectedIndex = -1;
        SimPayment.Text = "MERCADO PAGO";
        SimPayout.Text = "0";
        SimAdults.Clear();
        SimExpense.Text = "0";
        SimulationResult.Text = "Listo para una nueva prueba.";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SettingsTabs.SelectedIndex = 0;
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N && _canEdit)
        {
            NewRule_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is DataGrid)
        {
            EditSelectedRule_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (EditorOverlay.Visibility == Visibility.Visible)
                EditorOverlay.Visibility = Visibility.Collapsed;
            else if (SettingsTabs.SelectedIndex != 0)
                SettingsTabs.SelectedIndex = 0;
            else
                EditReason.Clear();
            e.Handled = true;
        }
    }

    private void SimulateSelectedRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not CommissionSettingsRule rule)
        {
            EditorMessageText = "Selecciona una regla para simular.";
            return;
        }
        SettingsTabs.SelectedIndex = 1;
        SimDate.SelectedDate = rule.EffectiveFrom.Date <= DateTime.Today && (rule.EffectiveTo is null || rule.EffectiveTo.Value.Date >= DateTime.Today)
            ? DateTime.Today
            : rule.EffectiveFrom;
        SimTransport.Text = rule.Category == "TRANSPORTE" ? rule.Code : "TAXI TEST";
        SimPayment.Text = rule.Category == "FORMA_PAGO" ? rule.Code : "MERCADO PAGO";
        SimSale.Text = "1335";
        SimSubtotal.Text = "0";
        SimPayout.Text = "0";
        SimAdults.Clear();
        SimExpense.Text = "0";
    }

    private void RowSimulate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CommissionSettingsRule rule })
            RulesGrid.SelectedItem = rule;
        SimulateSelectedRule_Click(sender, e);
    }

    private void QuickCommission_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CommissionSettingsRule ruleFromRow })
            RulesGrid.SelectedItem = ruleFromRow;

        if (RulesGrid.SelectedItem is not CommissionSettingsRule rule)
        {
            EditorMessageText = "Selecciona una regla para cambiar su comisión.";
            return;
        }

        if (!_canEdit)
        {
            EditorMessageText = "Tu usuario puede consultar, pero no guardar cambios.";
            return;
        }

        OpenQuickCommissionDialog(rule);
    }

    private async void RetireTestRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not CommissionSettingsRule rule)
        {
            EditorMessageText = "Selecciona PRUEBA PREPUBLICACION para retirarla.";
            return;
        }

        if (!_canEdit)
        {
            EditorMessageText = "Tu usuario puede consultar, pero no retirar reglas de prueba.";
            return;
        }

        if (!IsPrepublicationTestRule(rule))
        {
            EditorMessageText = "Por seguridad, esta accion solo retira PRUEBA PREPUBLICACION. No modifica reglas reales.";
            return;
        }

        const string reason = "FIN PRUEBA INTEGRAL PREPUBLICACION";
        var confirm = $"Se desactivara la regla aislada:\n\n{rule.Name}\n\nMotivo: {reason}\n\nNo se borrara el historial. ¿Confirmar?";
        if (MessageBox.Show(this, confirm, "Retirar prueba", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            await _settings.DeactivatePrepublicationTestRuleAsync(rule.Id, _user, reason, _canEdit);
            await RefreshAllAsync();
            await RefreshAuditAsync();
            EditorMessageText = "PRUEBA PREPUBLICACION quedo inactiva y el retiro quedo auditado.";
        }
        catch (Exception ex)
        {
            EditorMessageText = FriendlyError(ex);
        }
    }

    private void OpenQuickCommissionDialog(CommissionSettingsRule current)
    {
        var title = $"{current.Name} / {CategoryDisplay(current.Category)}";
        var window = new Window
        {
            Title = "Cambio rápido de comisión",
            Owner = this,
            Width = 460,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };

        var commissionBox = new TextBox { Text = current.CommissionPercent.ToString("0.##", CultureInfo.InvariantCulture), Margin = new Thickness(0, 4, 0, 12), ToolTip = "Escribe 10 para 10%." };
        // Por omision la fecha es la vigencia que ya tiene la regla: asi el cambio CORRIGE esa
        // comision en lugar de abrir otra regla. Si el operador pone una fecha posterior,
        // entonces si se versiona (la vieja se cierra y nace una nueva vigencia).
        var fromPicker = new DatePicker { SelectedDate = current.EffectiveFrom.Date, Margin = new Thickness(0, 4, 0, 12), ToolTip = "Déjala igual para corregir esta comisión. Ponle una fecha posterior solo si quieres abrir una vigencia nueva y conservar la anterior." };
        var reasonBox = new TextBox { Height = 76, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Margin = new Thickness(0, 4, 0, 12), ToolTip = "Motivo obligatorio para auditoría." };
        var message = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        var save = new Button { Content = "GUARDAR CAMBIO", Width = 150, Padding = new Thickness(10, 7, 10, 7), FontWeight = FontWeights.Bold, IsDefault = true };
        var cancel = new Button { Content = "CANCELAR", Width = 110, Padding = new Thickness(10, 7, 10, 7), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Black, Foreground = System.Windows.Media.Brushes.DarkBlue, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Comisión actual: {current.CommissionPercent:0.##}%", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Nueva comisión" });
        panel.Children.Add(commissionBox);
        panel.Children.Add(new TextBlock { Text = "Vigente desde" });
        panel.Children.Add(fromPicker);
        panel.Children.Add(new TextBlock { Text = "Motivo" });
        panel.Children.Add(reasonBox);
        panel.Children.Add(message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);
        window.Content = panel;

        save.Click += async (_, _) =>
        {
            try
            {
                save.IsEnabled = false;
                var newPercent = Percent(commissionBox.Text, "Nueva comisión");
                var from = fromPicker.SelectedDate ?? DateTime.Today;
                var reason = reasonBox.Text.Trim();
                if (reason.Length == 0)
                {
                    message.Text = "Escribe el motivo del cambio.";
                    save.IsEnabled = true;
                    return;
                }

                var creaVigencia = from.Date > current.EffectiveFrom.Date;
                var confirm = $"Confirmar cambio\n\n{title}\n\nAntes: {current.CommissionPercent:0.##}%\nAhora: {newPercent:0.##}%\nVigente desde: {from:dd/MM/yyyy}\n"
                    + (creaVigencia
                        ? "Se cierra la vigencia actual y nace una regla nueva.\n"
                        : "Se corrige esta misma regla (no se crea otra).\n")
                    + $"Motivo: {reason}\n\n¿Confirmar?";
                if (MessageBox.Show(window, confirm, "Confirmar cambio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    save.IsEnabled = true;
                    return;
                }

                var next = current with
                {
                    CommissionPercent = newPercent,
                    EffectiveFrom = from.Date,
                    EffectiveTo = creaVigencia ? null : current.EffectiveTo,
                    UpdatedBy = _user
                };
                if (creaVigencia)
                    await _settings.SaveRuleAsync(next, _user, reason, _canEdit);
                else
                    await _settings.UpdateRuleAsync(next, _user, reason, _canEdit);
                await RefreshAllAsync();
                EditorMessageText = $"✓ Comisión actualizada correctamente: {current.Name} ahora muestra {newPercent:0.##}% desde {from:dd/MM/yyyy}.";
                window.DialogResult = true;
                window.Close();

                if (MessageBox.Show(this, "¿Quieres probar esta comisión ahora?", "Probar esta comisión", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    RulesGrid.SelectedItem = (RulesGrid.ItemsSource as IEnumerable<CommissionSettingsRule>)?.FirstOrDefault(x => x.Code == current.Code && x.Category == current.Category) ?? current;
                    SimulateSelectedRule_Click(this, new RoutedEventArgs());
                }
            }
            catch (Exception ex)
            {
                message.Text = FriendlyError(ex);
                save.IsEnabled = true;
            }
        };

        commissionBox.Focus();
        window.ShowDialog();
    }

    private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (SettingsTabs.SelectedIndex == 2)
            _ = RefreshAuditAsync();
        if (SettingsTabs.SelectedIndex == 4)
            _ = RefreshDiagnosticsAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string CategoryDisplay(string category) => category switch
    {
        "GUIA" => "GUÍA",
        "FORMA_PAGO" => "FORMA DE PAGO",
        "JOYERIA" => "JOYERÍA",
        _ => category
    };

    private static bool IsPrepublicationTestRule(CommissionSettingsRule rule) =>
        string.Equals(rule.Code.Trim(), "PRUEBA PREPUBLICACION", StringComparison.OrdinalIgnoreCase)
        || string.Equals(rule.Name.Trim(), "PRUEBA PREPUBLICACION", StringComparison.OrdinalIgnoreCase);

    private static bool UsesFallback(CommissionSettingsRule rule) =>
        rule.Code.Contains("DEFAULT", StringComparison.OrdinalIgnoreCase)
        || rule.Notes.Contains("fallback", StringComparison.OrdinalIgnoreCase)
        || rule.UpdatedBy.Contains("MIGRACION", StringComparison.OrdinalIgnoreCase);

    private static int Entero(string? value)
    {
        var texto = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(texto)) return -1;
        return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) ? numero : -1;
    }

    private static decimal Money(string? value, string field)
    {
        var number = Number(value);
        if (number < 0)
            throw new InvalidOperationException($"{field} no puede ser negativa.");
        return number;
    }

    private static decimal Percent(string? value, string field)
    {
        var number = Number(value);
        if (number < 0 || number > 100)
            throw new InvalidOperationException($"{field} debe estar entre 0 y 100. Escribe 10 para 10%.");
        return number;
    }

    private static string FriendlyError(Exception ex)
    {
        var text = ex.Message;
        if (text.Contains("overlap", StringComparison.OrdinalIgnoreCase) || text.Contains("cruza", StringComparison.OrdinalIgnoreCase))
            return "No se pudo guardar la regla porque ya existe otra vigencia que se cruza con estas fechas.";
        if (text.Contains("motivo", StringComparison.OrdinalIgnoreCase) || text.Contains("reason", StringComparison.OrdinalIgnoreCase))
            return "Escribe el motivo del cambio para poder guardar la auditoría.";
        if (text.Contains("permiso", StringComparison.OrdinalIgnoreCase) || text.Contains("permission", StringComparison.OrdinalIgnoreCase))
            return "Tu usuario no tiene permiso para guardar cambios.";
        return string.IsNullOrWhiteSpace(text) ? "No se pudo completar la acción. Revisa los datos e intenta de nuevo." : text;
    }

    private static decimal Number(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant)
            ? invariant
            : decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var current) ? current : 0m;

    private static string SelectedComboText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return Convert.ToString(item.Content, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        return comboBox.Text.Trim();
    }

    private static void SetComboText(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(Convert.ToString(item.Content, CultureInfo.InvariantCulture), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
        comboBox.Text = value;
    }
}
