namespace ControlTaxiDesktop.Models;

public sealed record LocalProduct(long Id, string Code, string Name, decimal Price, decimal TaxRate, int Stock, bool Active);
public sealed record LocalSale(long Id, string Folio, DateTime Date, string Customer, string Status, decimal Subtotal, decimal Tax, decimal Total, decimal Paid, string PaymentStatus, string User);
public sealed record LocalSaleLine(long Id, long SaleId, long ProductId, string ProductCode, string ProductName, int Quantity, decimal UnitPrice, decimal TaxRate, decimal Subtotal, decimal Tax, decimal Total);
public sealed record LocalPayment(long Id, string Folio, string SaleFolio, DateTime Date, decimal Amount, string Method, string Notes, string User);
public sealed record LocalCommission(long Id, string Folio, string SaleFolio, string DriverCode, string DriverName, DateTime Date, decimal SaleTotal, decimal CommissionAmount, decimal PaidAmount, decimal Balance, string Status);
public sealed record LocalCut(long Id, DateTime Date, decimal Cash, decimal Card, decimal Payments, decimal Expenses, decimal Expected, decimal Counted, decimal Difference, string Status, string User, string? ClosedAt);
public sealed record LocalAuditEntry(long Id, DateTime Date, string User, string Module, string Action, string RecordId, string Description, string DatabaseName, string TableName, string AppFolio, string OperationFolio, string PosFolio, string Driver, string Badge, decimal? Amount, bool Success, string Machine, string Application, string Details);
