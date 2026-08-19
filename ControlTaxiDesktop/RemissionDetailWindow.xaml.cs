using System.Windows;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop;

public partial class RemissionDetailWindow : Window
{
    private readonly LocalDatabase _database;
    private readonly string _user;
    private readonly string _branchCode;
    private readonly LocalSale _sale;
    private readonly IReadOnlyList<LocalSaleLine> _lines;

    public RemissionDetailWindow(LocalDatabase database, string user, string branchCode, LocalSale sale, IReadOnlyList<LocalSaleLine> lines)
    {
        _database = database;
        _user = user;
        _branchCode = string.IsNullOrWhiteSpace(branchCode) ? "P28" : branchCode.Trim().ToUpperInvariant();
        _sale = sale;
        _lines = lines;
        InitializeComponent();
        RemisionText.Text = sale.Folio;
        RegistroText.Text = sale.Folio;
        FacturaText.Text = sale.Id.ToString();
        ClienteText.Text = sale.Customer;
        UsuarioText.Text = sale.User;
        ControlText.Text = sale.Folio;
        AmountsGrid.ItemsSource = new[] { sale };
        LinesGrid.ItemsSource = lines;
        LoadImportedDetails();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Products_Click(object sender, RoutedEventArgs e) => new RemissionProductsWindow(_lines) { Owner = this }.ShowDialog();
    private void Relation_Click(object sender, RoutedEventArgs e) => new OperationsWindow(_database, _user, _branchCode, "Relaciones") { Owner = this }.ShowDialog();
    private void Commission_Click(object sender, RoutedEventArgs e) => new PosWindow(_database, _user, _branchCode, "Comisiones") { Owner = this }.ShowDialog();
    private void Register_Click(object sender, RoutedEventArgs e) => new OperationsWindow(_database, _user, _branchCode, "Registro diario") { Owner = this }.ShowDialog();

    private void LoadImportedDetails()
    {
        try
        {
            using var connection = _database.Open();
            var token = _sale.Folio.Replace("MKT-", "", StringComparison.OrdinalIgnoreCase);
            var info = TryReadImportedInfo(connection, _sale.Folio) ?? TryReadImportedInfo(connection, token);
            if (info is null)
            {
                SetUnavailable();
                return;
            }

            TaxistaText.Text = Empty(info.Taxista);
            TaxistaIdText.Text = Empty(info.TaxistaId);
            TransporteText.Text = Empty(info.Transporte);
            FolioAppText.Text = Empty(info.FolioApp);
            GafetesText.Text = string.IsNullOrWhiteSpace(info.Gafetes) ? "SIN GAFETES ACTIVOS" : info.Gafetes;
            HotelText.Text = Empty(info.Hotel);
            FormaPagoText.Text = Empty(info.FormaPago);
            ComisionText.Text = info.Comision.HasValue ? info.Comision.Value.ToString("C2") : "No disponible";
            VentaText.Text = info.Venta.HasValue ? info.Venta.Value.ToString("C2") : _sale.Total.ToString("C2");
            OrigenText.Text = Empty(info.Origen);
            if (!string.IsNullOrWhiteSpace(info.FolioApp)) ControlText.Text = info.FolioApp;
        }
        catch
        {
            SetUnavailable();
        }
    }

    private static ImportedRemissionInfo? TryReadImportedInfo(SqliteConnection connection, string folio)
    {
        if (!HasTable(connection, "mkt__dbo__AppMovilRegistro") || !HasTable(connection, "mkt__dbo__dejadas")) return null;
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              COALESCE(a.folio_app, d.codigorecepcion, '') AS FolioApp,
              COALESCE(a.folio_gafete, d.gafete, '') AS Gafetes,
              COALESCE(a.vendedor_nombre, d.nombrevendedor, '') AS Taxista,
              COALESCE(CAST(a.id_catalogo AS TEXT), '') AS TaxistaId,
              COALESCE(a.tipo_operacion, d.tipotransporte, '') AS Transporte,
              COALESCE(a.hotel, d.hotel, '') AS Hotel,
              COALESCE(a.origen, '') AS Origen,
              CASE WHEN COALESCE(a.tarjeta,0) > 0 THEN 'TARJETA' WHEN COALESCE(a.efectivo,0) > 0 THEN 'EFECTIVO' ELSE '' END AS FormaPago,
              COALESCE(a.total, d.total, 0) AS Venta,
              COALESCE(c.ImporteComision, 0) AS Comision
            FROM "mkt__dbo__AppMovilRegistro" a
            LEFT JOIN "mkt__dbo__dejadas" d
              ON CAST(d.codigorecepcion AS TEXT) = CAST(a.folio_app AS TEXT)
              OR CAST(d.folioregistrostr AS TEXT) = CAST(a.folio_app AS TEXT)
              OR CAST(d.folioregistro AS TEXT) = CAST(a.folio_app AS TEXT)
            LEFT JOIN LocalRelaciones lr
              ON lr.FolioApp = a.folio_app OR lr.FolioApp = a.folio_app_original
            LEFT JOIN LocalComisiones c
              ON c.Folio = COALESCE(lr.FolioOperacion, a.folio_app_original, a.folio_app)
              OR c.VentaFolio = COALESCE(lr.FolioPos, a.folio_pos)
            WHERE CAST(a.folio_app AS TEXT) = $folio
               OR CAST(a.folio_app_original AS TEXT) = $folio
               OR CAST(a.folio_pos AS TEXT) = $folio
               OR CAST(d.folioregistro AS TEXT) = $folio
               OR CAST(d.folioregistrostr AS TEXT) = $folio
               OR CAST(d.codigorecepcion AS TEXT) = $folio
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$folio", folio);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new ImportedRemissionInfo(
            Text(reader, 0),
            Text(reader, 1),
            Text(reader, 2),
            Text(reader, 3),
            Text(reader, 4),
            Text(reader, 5),
            Text(reader, 6),
            Text(reader, 7),
            reader.IsDBNull(8) ? null : Convert.ToDecimal(reader.GetValue(8)),
            reader.IsDBNull(9) ? null : Convert.ToDecimal(reader.GetValue(9)));
    }

    private void SetUnavailable()
    {
        TaxistaText.Text = "No disponible";
        TaxistaIdText.Text = "No disponible";
        TransporteText.Text = "No disponible";
        FolioAppText.Text = "No disponible";
        HotelText.Text = "No disponible";
        FormaPagoText.Text = "No disponible";
        ComisionText.Text = "No disponible";
        VentaText.Text = _sale.Total.ToString("C2");
    }

    private static bool HasTable(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static string Text(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? string.Empty : Convert.ToString(reader.GetValue(index)) ?? string.Empty;
    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "No disponible" : value;
    private sealed record ImportedRemissionInfo(string FolioApp, string Gafetes, string Taxista, string TaxistaId, string Transporte, string Hotel, string Origen, string FormaPago, decimal? Venta, decimal? Comision);
}
