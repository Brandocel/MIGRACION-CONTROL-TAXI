using System.Globalization;

namespace ControlTaxiDesktop.Models;

public sealed record LocalRate(long Id, string Type, string Name, decimal Payout, decimal Minimum, decimal Maximum, bool Active);
public sealed record LocalHotel(long Id, string Name, bool Active);
public sealed record LocalDriver(long Id, string Code, string Name, string Phone, string Plates, string Model, string Unit, string ServiceType, string Status);
public sealed record LocalBadge(long Id, string Number, string Status, long? DriverId, string? AssignedAt, string? ReturnedAt, string Staff = "", string OperationFolio = "", string Unit = "", string Phone = "", string Nationality = "", string LocalFolio = "", string Vendor = "")
{
    public bool BulkSelected { get; set; }
    /// <summary>
    /// Otros vendedores que atendieron la misma llegada (mismo folio de operacion), con su
    /// gafete: "Con LUIS CRUZ (205)". Vacio cuando la llegada la atendio uno solo. Lo llena la
    /// pantalla de Gafetes al cargar la lista, porque el dato sale de agrupar las filas.
    /// </summary>
    public string VendorTeam { get; set; } = string.Empty;
    public string VendorDisplay => string.IsNullOrWhiteSpace(Vendor) ? "-" : Vendor.Trim();
    public string VendorTooltip => string.IsNullOrWhiteSpace(VendorTeam)
        ? VendorDisplay
        : VendorDisplay + Environment.NewLine + VendorTeam;
    public bool CanBulkReturn
    {
        get
        {
            var normalized = (Status ?? string.Empty).Trim().ToUpperInvariant();
            return normalized is "A" or "ASIGNADO" or "OCUPADO";
        }
    }
}
public sealed record LocalRecord(long Id, string Folio, DateTime Date, long? DriverId, long? HotelId, long? RateId, string Badge, int Passengers, string Origin, string Destination, decimal Amount, string PaymentMethod, string Notes, string User);
public sealed record LocalReport(DateTime Start, DateTime End, int Records, decimal Total, decimal Cash, decimal Card);
public sealed record LocalTransport(long Id, string Code, string Name, decimal Minimum, decimal Maximum, decimal Commission, decimal CashDiscount, decimal CardDiscount, decimal AmexDiscount, bool Active);
public sealed record LocalGuide(long Id, string Code, string Name, string Phone, decimal Commission, string Status);
public sealed record LocalExpense(long Id, DateTime Date, string Folio, string Concept, decimal Amount, string Notes, string Status, string User);
public sealed record LocalRelation(long Id, string AppFolio, string OperationFolio, string PosFolio, string Badge, string Driver, string Vendor, decimal? Payout, string Notes, string Source = "", string SourceUser = "", string DateText = "", string Hotel = "", string Origin = "", string Site = "", string Destination = "", string Unit = "", string Plates = "", string Phone = "", string Nationality = "", string TransportType = "", decimal Sale = 0m, decimal Commission = 0m, decimal CommissionPaid = 0m, string PaymentMethod = "", string PayoutStatus = "", string CommissionStatus = "", string PayoutTicket = "", string TaxistaId = "", string PayoutUser = "", string PayoutDate = "", decimal PayoutPaid = 0m, int Passengers = 0, string SaleDetail = "", string Currency = "", string RemotePaymentMethod = "", string PaymentsJson = "", decimal TotalAmount = 0m, decimal CashAmount = 0m, decimal CardAmount = 0m, decimal DollarsAmount = 0m, decimal ExchangeRate = 0m, string OrigenComision = "", int AdultPassengers = 0, int YouthPassengers = 0, int ChildPassengers = 0, int NoShowCount = 0, string SellerBadges = "")
{
    public int? CommissionAdultCount { get; init; }

    /// <summary>
    /// Cuanto de la venta fue de joyeria. Se guarda aparte del total porque la degustacion del
    /// Excel depende solo de esa parte: en joyeria siempre se quita, en la otra tienda no.
    /// </summary>
    public decimal JewelrySale { get; init; }
    public string CommissionCalculationDetail { get; init; } = string.Empty;

    public string DisplayLocalFolio => string.IsNullOrWhiteSpace(OperationFolio) ? AppFolio : OperationFolio;
    public string SaleDisplay => Sale.ToString("C2", CultureInfo.CurrentCulture);
    public string CommissionDisplay => Commission.ToString("C2", CultureInfo.CurrentCulture);
    public string CommissionPaidDisplay => CommissionPaid > 0m ? CommissionPaid.ToString("C2", CultureInfo.CurrentCulture) : string.Empty;
}
public sealed record LocalAppRecordInput(string? OriginalFolio, long? DriverId, string DriverName, string Phone, string ContactPhone, string Nationality, string Plates, string Model, string Unit, string Hotel, string Origin, string Site, string Destination, int Passengers, string TransportType, decimal Amount, long? RateId, string Badge, string Notes, string User);
public sealed record LocalAppRecordRow(string FolioControl, string OriginalFolio, string DriverName, string Hotel, string Badge, string OperationDate, string TransportType, decimal Total, string PaymentStatus, string User, string Notes)
{
    public string FolioOriginal => OriginalFolio;
    public string FolioLocal => FolioControl;
    public string Taxista => DriverName;
    public string Fecha => OperationDate;
    public string Origen => string.Empty;
    public string Destino => string.Empty;
    public string Nacionalidad => string.Empty;
    public string Unidad => string.Empty;
    public string Sitio => string.Empty;
    public decimal Efectivo => 0m;
    public decimal Tarjeta => 0m;
    public string PayoutStatus => PaymentStatus;
}
public sealed class AppRecordGridRow
{
    public string FolioOriginal { get; init; } = string.Empty;
    public string FolioLocal { get; init; } = string.Empty;
    public string Taxista { get; init; } = string.Empty;
    public string Fecha { get; init; } = string.Empty;
    public string Hotel { get; init; } = string.Empty;
    public string Origen { get; init; } = string.Empty;
    public string Destino { get; init; } = string.Empty;
    public string Nacionalidad { get; init; } = string.Empty;
    public string Unidad { get; init; } = string.Empty;
    public string Sitio { get; init; } = string.Empty;
    public decimal Total { get; init; }
    public decimal Efectivo { get; init; }
    public decimal Tarjeta { get; init; }
    public string PayoutStatus { get; init; } = string.Empty;
}
public sealed record LocalBadgeSelection(string Number, string? OperationFolio);
public sealed record LocalScannedBadgeItem(string Number, string Staff, string? OperationFolio, string Status, string Unit = "", string Phone = "", string LocalFolio = "", string Vendor = "");
