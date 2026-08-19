using System.Text.Json.Serialization;

namespace ControlTaxiDesktop.Tools.CascoSync.Models;

public sealed class CascoTripRecord
{
    [JsonPropertyName("recordId")]
    public string? RecordId { get; set; }

    [JsonPropertyName("catalogId")]
    public string? CatalogId { get; set; }

    [JsonPropertyName("badgeId")]
    public string? BadgeId { get; set; }

    [JsonPropertyName("driverName")]
    public string? DriverName { get; set; }

    [JsonPropertyName("driverPhone")]
    public string? DriverPhone { get; set; }

    [JsonPropertyName("contactPhone")]
    public string? ContactPhone { get; set; }

    [JsonPropertyName("nationality")]
    public string? Nationality { get; set; }

    [JsonPropertyName("plate")]
    public string? Plate { get; set; }

    [JsonPropertyName("vehicleModel")]
    public string? VehicleModel { get; set; }

    [JsonPropertyName("unitNumber")]
    public string? UnitNumber { get; set; }

    [JsonPropertyName("hotel")]
    public string? Hotel { get; set; }

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("site")]
    public string? Site { get; set; }

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("passengerCount")]
    [JsonConverter(typeof(FlexibleNullableIntConverter))]
    public int? PassengerCount { get; set; }

    [JsonPropertyName("serviceType")]
    public string? ServiceType { get; set; }

    [JsonPropertyName("tripCost")]
    [JsonConverter(typeof(FlexibleNullableDecimalConverter))]
    public decimal? TripCost { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("recordDate")]
    public DateTimeOffset? RecordDate { get; set; }

    [JsonPropertyName("paymentMethod")]
    public string? PaymentMethod { get; set; }

    [JsonPropertyName("assignedBranchCode")]
    public string? AssignedBranchCode { get; set; }

    [JsonPropertyName("payoutStatus")]
    public string? PayoutStatus { get; set; }

    [JsonPropertyName("payoutDate")]
    public string? PayoutDate { get; set; }

    [JsonPropertyName("payoutUser")]
    public string? PayoutUser { get; set; }

    [JsonPropertyName("payoutTicket")]
    public string? PayoutTicket { get; set; }
}
