using ControlTaxiWeb.Data;
using System.ComponentModel.DataAnnotations;

namespace ControlTaxiWeb.Models;

public sealed class DashboardPageViewModel
{
    public required PortalDatabase Database { get; init; }
    public required DateTime Date { get; init; }
    public required DashboardMetricsViewModel Metrics { get; init; }
    public required IReadOnlyList<OperationRowViewModel> RecentOperations { get; init; }
}

public sealed record DashboardMetricsViewModel(
    int OperationsCount,
    int TicketsCount,
    decimal ArtesaniasTotal,
    decimal JoyeriaTotal,
    decimal PaymentsTotal,
    decimal ExpensesTotal);

public sealed record OperationRowViewModel(
    string Folio,
    DateTime? OperationDate,
    string SellerName,
    string Hotel,
    int PassengerCount,
    decimal Total,
    decimal Balance,
    string PaymentSummary);

public sealed class OperationsPageViewModel
{
    public required PortalDatabase Database { get; init; }
    public required DateTime Date { get; init; }
    public string Query { get; init; } = string.Empty;
    public required IReadOnlyList<OperationRowViewModel> Operations { get; init; }
}

public sealed record CommissionRowViewModel(
    string Folio,
    DateTime? SaleDate,
    string BeneficiaryName,
    string SellerName,
    decimal BaseAmount,
    decimal CommissionAmount);

public sealed class CommissionsPageViewModel
{
    public required PortalDatabase Database { get; init; }
    public string Query { get; init; } = string.Empty;
    public required IReadOnlyList<CommissionRowViewModel> Commissions { get; init; }
}

public sealed record VendorRowViewModel(
    string VendorKey,
    string Name,
    string PhoneNumber,
    decimal CommissionPercent);

public sealed class VendorsPageViewModel
{
    public required PortalDatabase Database { get; init; }
    public string Query { get; init; } = string.Empty;
    public required IReadOnlyList<VendorRowViewModel> Vendors { get; init; }
}

public sealed record ProductRowViewModel(
    int ProductId,
    string Code,
    string Name,
    string Department,
    decimal Price,
    decimal Iva,
    string Currency);

public sealed class ProductsPageViewModel
{
    public required PortalDatabase Database { get; init; }
    public string Query { get; init; } = string.Empty;
    public required IReadOnlyList<ProductRowViewModel> Products { get; init; }
}

public sealed record GuideRowViewModel(
    string GuideKey,
    string Name,
    string Company,
    string PhoneNumber);

public sealed record TransportRowViewModel(
    string Code,
    string Name);

public sealed class CatalogsPageViewModel
{
    public required PortalDatabase Database { get; init; }
    public required IReadOnlyList<GuideRowViewModel> Guides { get; init; }
    public required IReadOnlyList<TransportRowViewModel> Transports { get; init; }
}

public sealed class CreateOperationInputModel
{
    public PortalDatabase Database { get; set; } = PortalDatabase.CompuadmoPlaza;

    [Required]
    [Display(Name = "Vendedor")]
    public string SellerKey { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Cliente / hotel")]
    public string Hotel { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Tipo de operacion")]
    public string OperationType { get; set; } = "VENTA";

    [Display(Name = "Fecha")]
    public DateTime SaleDate { get; set; } = DateTime.Now;

    [Required]
    [Display(Name = "Usuario")]
    [StringLength(4)]
    public string UserName { get; set; } = "APP";

    [Display(Name = "Guia")]
    public string GuideCode { get; set; } = string.Empty;

    [Display(Name = "Transportista")]
    public string TransportCode { get; set; } = string.Empty;

    [Range(1, 100)]
    [Display(Name = "Pasajeros")]
    public int PassengerCount { get; set; } = 1;

    [Display(Name = "Notas")]
    public string Notes { get; set; } = string.Empty;

    [Range(0, 999999)]
    [Display(Name = "Subtotal")]
    public decimal Subtotal { get; set; }

    [Range(0, 999999)]
    [Display(Name = "IVA")]
    public decimal Tax { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Efectivo")]
    public decimal Cash { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Tarjeta")]
    public decimal Card { get; set; }

    [Range(0, 999999)]
    [Display(Name = "Dolares")]
    public decimal Dollars { get; set; }

    [Range(0.01, 999999)]
    [Display(Name = "Tipo cambio USD")]
    public decimal ExchangeRate { get; set; } = 1;
}

public sealed class CreateOperationPageViewModel
{
    public required CreateOperationInputModel Input { get; init; }
    public required IReadOnlyList<VendorRowViewModel> Vendors { get; init; }
    public required IReadOnlyList<GuideRowViewModel> Guides { get; init; }
    public required IReadOnlyList<TransportRowViewModel> Transports { get; init; }
    public string? SuccessMessage { get; init; }
}
