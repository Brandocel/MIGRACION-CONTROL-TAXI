namespace ControlTaxiDesktop.Models;

public enum LocalPortalDatabase
{
    CompuadmoPlaza,
    JoyeriaPlaza
}

public sealed record LocalPortalMetrics(int OperationsCount, int TicketsCount, decimal ArtesaniasTotal, decimal JoyeriaTotal, decimal PaymentsTotal, decimal ExpensesTotal);
public sealed record LocalPortalOperation(string Folio, DateTime? OperationDate, string SellerName, string Hotel, int PassengerCount, decimal Total, decimal Balance, string PaymentSummary);
public sealed record LocalPortalCommission(string Folio, DateTime? SaleDate, string BeneficiaryName, string SellerName, decimal BaseAmount, decimal CommissionAmount);
public sealed record LocalPortalVendor(string VendorKey, string Name, string PhoneNumber, decimal CommissionPercent);
public sealed record LocalPortalProduct(int ProductId, string Code, string Name, string Department, decimal Price, decimal Iva, string Currency);
public sealed record LocalPortalGuide(string GuideKey, string Name, string Company, string PhoneNumber);
public sealed record LocalPortalTransport(string Code, string Name);
public sealed record LocalPortalCreateOperationInput(LocalPortalDatabase Database, string SellerKey, string Hotel, string OperationType, DateTime SaleDate, string UserName, string GuideCode, string TransportCode, int PassengerCount, string Notes, decimal Subtotal, decimal Tax, decimal Cash, decimal Card, decimal Dollars, decimal ExchangeRate);
