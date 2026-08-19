using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;

namespace ControlTaxiDesktop;

public partial class ChecklistValidacionFinal : Window
{
    private readonly ObservableCollection<ChecklistItem> _items;

    public ChecklistValidacionFinal()
    {
        InitializeComponent();
        _items = new ObservableCollection<ChecklistItem>(CreateItems());
        ChecklistGrid.ItemsSource = _items;
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
        {
            item.Status = "Pendiente";
            item.ScreenOpens = false;
            item.ButtonsWork = false;
            item.FiltersWork = false;
            item.SearchWorks = false;
            item.TotalsMatchWeb = false;
            item.ExportWorks = false;
            item.PrintWorks = false;
            item.PermissionsWork = false;
            item.Offline = false;
            item.Observations = string.Empty;
            item.Evidence = string.Empty;
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var defaultDirectory = Path.Combine(AppContext.BaseDirectory, "Documentacion");
        if (!Directory.Exists(defaultDirectory))
            defaultDirectory = Path.Combine(Environment.CurrentDirectory, "Documentacion");
        Directory.CreateDirectory(defaultDirectory);

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Markdown (*.md)|*.md",
            FileName = "CHECKLIST_RETIRO_WEB_RESULTADO.md",
            InitialDirectory = defaultDirectory
        };
        if (dialog.ShowDialog() != true) return;

        await File.WriteAllTextAsync(dialog.FileName, BuildMarkdown(), new UTF8Encoding(true));
        WebDialogWindow.Show(this, "Checklist exportado correctamente.", "Control Taxi", "✓");
    }

    private string BuildMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Resultado checklist retiro Web");
        builder.AppendLine();
        builder.AppendLine("Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        builder.AppendLine();
        builder.AppendLine("| Módulo | Validado | Pendiente | No aplica | Pantalla | Botones | Filtros | Búsquedas | Totales Web | Exportación | Impresión | Permisos | Offline | Observaciones | Evidencia |");
        builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var item in _items)
        {
            builder.AppendLine(string.Join(" | ", new[]
            {
                "| " + Escape(item.Module),
                Mark(item.Status, "Validado"),
                Mark(item.Status, "Pendiente"),
                Mark(item.Status, "No aplica"),
                Check(item.ScreenOpens),
                Check(item.ButtonsWork),
                Check(item.FiltersWork),
                Check(item.SearchWorks),
                Check(item.TotalsMatchWeb),
                Check(item.ExportWorks),
                Check(item.PrintWorks),
                Check(item.PermissionsWork),
                Check(item.Offline),
                Escape(item.Observations),
                Escape(item.Evidence) + " |"
            }));
        }
        return builder.ToString();
    }

    private static IEnumerable<ChecklistItem> CreateItems()
    {
        string[] modules =
        [
            "Login",
            "Usuarios",
            "Permisos",
            "Ventas",
            "Pagos",
            "Comisiones",
            "Cortes",
            "Gafetes",
            "Portal Dashboard",
            "Portal Operations",
            "Portal Commissions",
            "Portal Vendors",
            "Portal Products",
            "Portal Catalogs",
            "Portal CreateOperation",
            "Transportes",
            "Guías",
            "Gastos",
            "Relaciones",
            "Dejadas",
            "Ticket",
            "Reportes",
            "Excel",
            "PDF",
            "CSV",
            "Impresión",
            "App móvil offline/local",
            "Auditoría",
            "Catálogos",
            "Backup y recuperación"
        ];

        return modules.Select(module => new ChecklistItem { Module = module });
    }

    private static string Check(bool value) => value ? "✅" : "";
    private static string Mark(string status, string expected) => string.Equals(status?.Trim(), expected, StringComparison.OrdinalIgnoreCase) ? "✅" : "";
    private static string Escape(string? value) => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, " ", StringComparison.Ordinal);

    public sealed class ChecklistItem : INotifyPropertyChanged
    {
        private string _status = "Pendiente";
        private bool _screenOpens;
        private bool _buttonsWork;
        private bool _filtersWork;
        private bool _searchWorks;
        private bool _totalsMatchWeb;
        private bool _exportWorks;
        private bool _printWorks;
        private bool _permissionsWork;
        private bool _offline;
        private string _observations = string.Empty;
        private string _evidence = string.Empty;

        public required string Module { get; init; }
        public string Status { get => _status; set => SetField(ref _status, value); }
        public bool ScreenOpens { get => _screenOpens; set => SetField(ref _screenOpens, value); }
        public bool ButtonsWork { get => _buttonsWork; set => SetField(ref _buttonsWork, value); }
        public bool FiltersWork { get => _filtersWork; set => SetField(ref _filtersWork, value); }
        public bool SearchWorks { get => _searchWorks; set => SetField(ref _searchWorks, value); }
        public bool TotalsMatchWeb { get => _totalsMatchWeb; set => SetField(ref _totalsMatchWeb, value); }
        public bool ExportWorks { get => _exportWorks; set => SetField(ref _exportWorks, value); }
        public bool PrintWorks { get => _printWorks; set => SetField(ref _printWorks, value); }
        public bool PermissionsWork { get => _permissionsWork; set => SetField(ref _permissionsWork, value); }
        public bool Offline { get => _offline; set => SetField(ref _offline, value); }
        public string Observations { get => _observations; set => SetField(ref _observations, value); }
        public string Evidence { get => _evidence; set => SetField(ref _evidence, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
