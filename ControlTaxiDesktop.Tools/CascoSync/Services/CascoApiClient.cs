using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using ControlTaxiDesktop.Tools.CascoSync.Configuration;
using ControlTaxiDesktop.Tools.CascoSync.Models;

namespace ControlTaxiDesktop.Tools.CascoSync.Services;

public sealed class CascoApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly CascoSyncOptions _options;

    public CascoApiClient(HttpClient httpClient, CascoSyncOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<CascoApiCallResult> FetchRecordsAsync(CancellationToken cancellationToken)
    {
        var url = _options.BuildApiUrl();
        var stopwatch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        stopwatch.Stop();
        var contentType = response.Content.Headers.ContentType?.ToString();
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken);
        IReadOnlyList<CascoTripRecord> payload = Array.Empty<CascoTripRecord>();
        if (response.IsSuccessStatusCode && contentType is not null && contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                payload = JsonSerializer.Deserialize<List<CascoTripRecord>>(rawContent, SerializerOptions) ?? new List<CascoTripRecord>();
            }
            catch (JsonException)
            {
                payload = TryDeserializeWithFlexibleValues(rawContent);
            }
        }

        return new CascoApiCallResult
        {
            Url = url,
            HttpStatusCode = (int)response.StatusCode,
            ContentType = contentType,
            ResponseTimeMs = stopwatch.ElapsedMilliseconds,
            Records = payload
        };
    }

    private static IReadOnlyList<CascoTripRecord> TryDeserializeWithFlexibleValues(string rawContent)
    {
        try
        {
            using var document = JsonDocument.Parse(rawContent);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return Array.Empty<CascoTripRecord>();

            var records = new List<CascoTripRecord>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                var record = new CascoTripRecord();
                if (element.TryGetProperty("recordId", out var recordIdElement))
                    record.RecordId = recordIdElement.ValueKind == JsonValueKind.String ? recordIdElement.GetString() : recordIdElement.ToString();
                if (element.TryGetProperty("catalogId", out var catalogIdElement))
                    record.CatalogId = catalogIdElement.ValueKind == JsonValueKind.String ? catalogIdElement.GetString() : catalogIdElement.ToString();
                if (element.TryGetProperty("badgeId", out var badgeIdElement))
                    record.BadgeId = badgeIdElement.ValueKind == JsonValueKind.String ? badgeIdElement.GetString() : badgeIdElement.ToString();
                if (element.TryGetProperty("driverName", out var driverNameElement))
                    record.DriverName = driverNameElement.ValueKind == JsonValueKind.String ? driverNameElement.GetString() : driverNameElement.ToString();
                if (element.TryGetProperty("driverPhone", out var driverPhoneElement))
                    record.DriverPhone = driverPhoneElement.ValueKind == JsonValueKind.String ? driverPhoneElement.GetString() : driverPhoneElement.ToString();
                if (element.TryGetProperty("contactPhone", out var contactPhoneElement))
                    record.ContactPhone = contactPhoneElement.ValueKind == JsonValueKind.String ? contactPhoneElement.GetString() : contactPhoneElement.ToString();
                if (element.TryGetProperty("nationality", out var nationalityElement))
                    record.Nationality = nationalityElement.ValueKind == JsonValueKind.String ? nationalityElement.GetString() : nationalityElement.ToString();
                if (element.TryGetProperty("plate", out var plateElement))
                    record.Plate = plateElement.ValueKind == JsonValueKind.String ? plateElement.GetString() : plateElement.ToString();
                if (element.TryGetProperty("vehicleModel", out var vehicleModelElement))
                    record.VehicleModel = vehicleModelElement.ValueKind == JsonValueKind.String ? vehicleModelElement.GetString() : vehicleModelElement.ToString();
                if (element.TryGetProperty("unitNumber", out var unitNumberElement))
                    record.UnitNumber = unitNumberElement.ValueKind == JsonValueKind.String ? unitNumberElement.GetString() : unitNumberElement.ToString();
                if (element.TryGetProperty("hotel", out var hotelElement))
                    record.Hotel = hotelElement.ValueKind == JsonValueKind.String ? hotelElement.GetString() : hotelElement.ToString();
                if (element.TryGetProperty("origin", out var originElement))
                    record.Origin = originElement.ValueKind == JsonValueKind.String ? originElement.GetString() : originElement.ToString();
                if (element.TryGetProperty("site", out var siteElement))
                    record.Site = siteElement.ValueKind == JsonValueKind.String ? siteElement.GetString() : siteElement.ToString();
                if (element.TryGetProperty("destination", out var destinationElement))
                    record.Destination = destinationElement.ValueKind == JsonValueKind.String ? destinationElement.GetString() : destinationElement.ToString();
                if (element.TryGetProperty("passengerCount", out var passengerCountElement))
                    record.PassengerCount = passengerCountElement.ValueKind == JsonValueKind.Number ? passengerCountElement.GetInt32() : int.TryParse(passengerCountElement.ToString(), out var parsedPass) ? parsedPass : null;
                if (element.TryGetProperty("serviceType", out var serviceTypeElement))
                    record.ServiceType = serviceTypeElement.ValueKind == JsonValueKind.String ? serviceTypeElement.GetString() : serviceTypeElement.ToString();
                if (element.TryGetProperty("tripCost", out var tripCostElement))
                    record.TripCost = tripCostElement.ValueKind == JsonValueKind.Number ? tripCostElement.GetDecimal() : decimal.TryParse(tripCostElement.ToString(), out var parsedCost) ? parsedCost : null;
                if (element.TryGetProperty("notes", out var notesElement))
                    record.Notes = notesElement.ValueKind == JsonValueKind.String ? notesElement.GetString() : notesElement.ToString();
                if (element.TryGetProperty("recordDate", out var recordDateElement))
                    record.RecordDate = DateTimeOffset.TryParse(recordDateElement.ToString(), out var parsedDate) ? parsedDate : null;
                if (element.TryGetProperty("paymentMethod", out var paymentMethodElement))
                    record.PaymentMethod = paymentMethodElement.ValueKind == JsonValueKind.String ? paymentMethodElement.GetString() : paymentMethodElement.ToString();
                if (element.TryGetProperty("assignedBranchCode", out var branchCodeElement))
                    record.AssignedBranchCode = branchCodeElement.ValueKind == JsonValueKind.String ? branchCodeElement.GetString() : branchCodeElement.ToString();
                if (element.TryGetProperty("payoutStatus", out var payoutStatusElement))
                    record.PayoutStatus = payoutStatusElement.ValueKind == JsonValueKind.String ? payoutStatusElement.GetString() : payoutStatusElement.ToString();
                if (element.TryGetProperty("payoutDate", out var payoutDateElement))
                    record.PayoutDate = payoutDateElement.ValueKind == JsonValueKind.String ? payoutDateElement.GetString() : payoutDateElement.ToString();
                if (element.TryGetProperty("payoutUser", out var payoutUserElement))
                    record.PayoutUser = payoutUserElement.ValueKind == JsonValueKind.String ? payoutUserElement.GetString() : payoutUserElement.ToString();
                if (element.TryGetProperty("payoutTicket", out var payoutTicketElement))
                    record.PayoutTicket = payoutTicketElement.ValueKind == JsonValueKind.String ? payoutTicketElement.GetString() : payoutTicketElement.ToString();
                records.Add(record);
            }

            return records;
        }
        catch (JsonException)
        {
            return Array.Empty<CascoTripRecord>();
        }
    }
}
