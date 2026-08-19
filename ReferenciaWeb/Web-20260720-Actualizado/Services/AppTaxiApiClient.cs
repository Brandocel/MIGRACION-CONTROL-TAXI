using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ControlTaxiWeb.Interfaces;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.Extensions.Caching.Memory;

namespace ControlTaxiWeb.Services
{
    public sealed class AppTaxiApiClient : IAppTaxiApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AppTaxiApiClient> _logger;
        private readonly string _syncToken;

        public AppTaxiApiClient(HttpClient httpClient, IMemoryCache cache, ILogger<AppTaxiApiClient> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
            _syncToken = configuration["TaxiApi:SyncToken"] ?? "HokaTaxisSync2050";
        }

        public async Task<IReadOnlyList<PosOperacionRowViewModel>> GetTripRecordsAsync(
            string? query,
            DateTime? fechaInicio,
            DateTime? fechaFin,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var cacheKey = $"app-taxi-registros:{query}:{fechaInicio:yyyyMMdd}:{fechaFin:yyyyMMdd}";
                if (_cache.TryGetValue(cacheKey, out IReadOnlyList<PosOperacionRowViewModel>? cached) && cached != null)
                    return cached;

                var parameters = new Dictionary<string, string>();
                if (fechaInicio.HasValue)
                    parameters["dateFrom"] = fechaInicio.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (fechaFin.HasValue)
                    parameters["dateTo"] = fechaFin.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!fechaInicio.HasValue && !fechaFin.HasValue)
                    parameters["date"] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(query))
                    parameters["query"] = query.Trim();

                var path = "api/taxis/registros";
                if (parameters.Count > 0)
                    path += "?" + string.Join("&", parameters.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

                var records = await _httpClient.GetFromJsonAsync<List<AppTripRecordDto>>(path, cancellationToken)
                    ?? new List<AppTripRecordDto>();

                var result = records.Select(x => new PosOperacionRowViewModel
                {
                    FolioOperacion = string.Empty,
                    FolioControl = x.RecordId ?? string.Empty,
                    Fuente = "APP MOVIL",
                    UsuarioOrigen = FirstText(x.UsuarioMovil, x.UserName, x.CreatedBy, x.User, x.CreatedByName, "hostinger"),
                    Ticket = FirstText(x.TicketSale, x.FolioPos),
                    CantidadTickets = 1,
                    CatalogId = x.CatalogId,
                    Hotel = FirstText(x.Hotel, x.Origin, x.Site, x.Destination),
                    LlegadaSucursal = FirstText(x.AssignedBranchName, x.AssignedBranch, x.Destination, x.Site, x.Origin, x.Hotel),
                    Gafete = x.BadgeId ?? string.Empty,
                    Origen = x.Origin ?? string.Empty,
                    Sitio = x.Site ?? string.Empty,
                    Destino = x.Destination ?? string.Empty,
                    Unidad = x.UnitNumber ?? string.Empty,
                    Placas = x.Plate ?? string.Empty,
                    ModeloVehiculo = x.VehicleModel ?? string.Empty,
                    Telefono = FirstText(x.DriverPhone, x.ContactPhone),
                    TelefonoContacto = x.ContactPhone ?? string.Empty,
                    Nacionalidad = FirstText(x.Nationality, x.Nacionalidad),
                    Notas = x.Notes ?? string.Empty,
                    Hora = x.RecordDate?.DateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) ?? string.Empty,
                    Pax = x.PassengerCount,
                    Vendedor = x.DriverName ?? string.Empty,
                    TipoOperacion = x.ServiceType ?? string.Empty,
                    Total = x.TripCost,
                    Efectivo = string.Equals(x.PaymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase) ? 0m : x.TripCost,
                    Tarjeta = string.Equals(x.PaymentMethod, "Tarjeta", StringComparison.OrdinalIgnoreCase) ? x.TripCost : 0m,
                    PayoutStatus = x.PayoutStatus ?? string.Empty,
                    PayoutDate = x.PayoutDate ?? string.Empty,
                    PayoutUser = x.PayoutUser ?? string.Empty,
                    PayoutTicket = x.PayoutTicket ?? string.Empty
                }).ToList();
                _cache.Set(cacheKey, result, TimeSpan.FromSeconds(10));
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible consultar registros desde la API APP_TAXI.");
                return Array.Empty<PosOperacionRowViewModel>();
            }
        }

        private static string FirstText(params string?[] values) =>
            values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

        public async Task<IReadOnlyList<PosRegistroAppTaxistaOption>> SearchCatalogTaxistasAsync(
            string? query,
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var value = (query ?? string.Empty).Trim();
                if (value.Length == 0)
                    return Array.Empty<PosRegistroAppTaxistaOption>();

                var uri = $"api/taxis/catalogo/buscar?query={Uri.EscapeDataString(value)}";
                var records = await _httpClient.GetFromJsonAsync<List<AppCatalogTaxiItemDto>>(uri, cancellationToken)
                    ?? new List<AppCatalogTaxiItemDto>();
                return records
                    .Take(Math.Clamp(limit, 5, 80))
                    .Select(x => new PosRegistroAppTaxistaOption
                    {
                        Clave = x.CatalogId.ToString(CultureInfo.InvariantCulture),
                        Nombre = x.DriverName ?? string.Empty,
                        Telefono = x.PhoneNumber ?? string.Empty,
                        Placas = x.Plate ?? string.Empty,
                        Modelo = x.VehicleModel ?? string.Empty,
                        Unidad = x.UnitNumber ?? string.Empty,
                        TransporteTipo = x.ServiceType ?? string.Empty,
                        Nacionalidad = FirstText(x.Nationality, x.Nacionalidad),
                        Hotel = x.Hotel ?? string.Empty,
                        Sitio = x.Site ?? string.Empty,
                        Destino = FirstText(x.Destination, x.Hotel, x.Site),
                        Gafete = x.BadgeId ?? string.Empty,
                        SuggestedAmount = x.SuggestedAmount
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Nombre) || !string.IsNullOrWhiteSpace(x.Clave))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible buscar taxistas en el catalogo Hostinger.");
                return Array.Empty<PosRegistroAppTaxistaOption>();
            }
        }

        public async Task<IReadOnlyList<string>> GetCatalogHotelsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var options = await _httpClient.GetFromJsonAsync<AppTaxiOptionsDto>("api/taxis/options", cancellationToken);
                return options?.Hotels?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar hoteles desde Hostinger.");
                return Array.Empty<string>();
            }
        }

        public async Task<IReadOnlyList<PosRegistroAppTarifaOption>> GetCatalogRatesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var rates = await _httpClient.GetFromJsonAsync<List<AppServiceRateDto>>("api/taxis/tarifas", cancellationToken)
                    ?? new List<AppServiceRateDto>();
                return rates
                    .Where(x => !string.IsNullOrWhiteSpace(x.ServiceType))
                    .Select(x => new PosRegistroAppTarifaOption
                    {
                        Tipo = x.ServiceType ?? string.Empty,
                        Nombre = x.Description ?? string.Empty,
                        Dejada = x.BaseAmount,
                        Minimo = x.BaseAmount,
                        Maximo = x.BaseAmount
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar tarifas desde Hostinger.");
                return Array.Empty<PosRegistroAppTarifaOption>();
            }
        }

        public async Task<bool> MarkTripPayoutPaidAsync(
            string recordId,
            string user,
            string ticket,
            DateTime paidAt,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(recordId))
                    return false;

                using var directResponse = await _httpClient.PostAsJsonAsync(
                    $"api/taxis/registros/{Uri.EscapeDataString(recordId.Trim())}/pagar",
                    new { user },
                    cancellationToken);
                if (directResponse.IsSuccessStatusCode || directResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
                    return true;

                var records = await _httpClient.GetFromJsonAsync<List<AppTripRecordDto>>(
                    $"api/taxis/registros?query={Uri.EscapeDataString(recordId.Trim())}",
                    cancellationToken) ?? new List<AppTripRecordDto>();
                var record = records.FirstOrDefault(x => string.Equals(x.RecordId, recordId.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? (records.Count == 1 ? records[0] : null);
                if (record == null)
                    return false;

                record.PayoutStatus = "pagado";
                record.PayoutDate = paidAt.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
                record.PayoutUser = user;
                record.PayoutTicket = ticket;

                using var request = new HttpRequestMessage(HttpMethod.Post, "sync/push-changes");
                request.Headers.TryAddWithoutValidation("X-Sync-Token", _syncToken);
                request.Content = JsonContent.Create(new PushChangesPayload
                {
                    TripRecords = new List<AppTripRecordDto> { record }
                });

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return false;

                var result = await response.Content.ReadFromJsonAsync<PushChangesResponse>(cancellationToken: cancellationToken);
                if ((result?.Upserted?.TripRecords ?? 0) <= 0)
                    return false;

                InvalidateTripCache(record);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible marcar la dejada como pagada en la API APP_TAXI.");
                return false;
            }
        }

        public async Task<bool> UpdateTripBadgesAsync(
            string recordId,
            IReadOnlyCollection<string> badges,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(recordId))
                    return false;

                var cleanBadges = badges
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var records = await _httpClient.GetFromJsonAsync<List<AppTripRecordDto>>(
                    $"api/taxis/registros?query={Uri.EscapeDataString(recordId.Trim())}",
                    cancellationToken) ?? new List<AppTripRecordDto>();
                var record = records.FirstOrDefault(x => string.Equals(x.RecordId, recordId.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? (records.Count == 1 ? records[0] : null);
                if (record == null)
                    return false;

                record.BadgeId = string.Join(", ", cleanBadges);

                using var request = new HttpRequestMessage(HttpMethod.Post, "sync/push-changes");
                request.Headers.TryAddWithoutValidation("X-Sync-Token", _syncToken);
                request.Content = JsonContent.Create(new PushChangesPayload
                {
                    TripRecords = new List<AppTripRecordDto> { record }
                });

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return false;

                var result = await response.Content.ReadFromJsonAsync<PushChangesResponse>(cancellationToken: cancellationToken);
                if ((result?.Upserted?.TripRecords ?? 0) <= 0)
                    return false;

                InvalidateTripCache(record);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible actualizar gafetes del viaje {RecordId} en la API APP_TAXI.", recordId);
                return false;
            }
        }

        public async Task<bool> UpdateTripAmountAsync(
            string recordId,
            decimal tripCost,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(recordId))
                    return false;

                var records = await _httpClient.GetFromJsonAsync<List<AppTripRecordDto>>(
                    $"api/taxis/registros?query={Uri.EscapeDataString(recordId.Trim())}",
                    cancellationToken) ?? new List<AppTripRecordDto>();
                var record = records.FirstOrDefault(x => string.Equals(x.RecordId, recordId.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? (records.Count == 1 ? records[0] : null);
                if (record == null)
                    return false;

                record.TripCost = tripCost;

                using var request = new HttpRequestMessage(HttpMethod.Post, "sync/push-changes");
                request.Headers.TryAddWithoutValidation("X-Sync-Token", _syncToken);
                request.Content = JsonContent.Create(new PushChangesPayload
                {
                    TripRecords = new List<AppTripRecordDto> { record }
                });

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return false;

                var result = await response.Content.ReadFromJsonAsync<PushChangesResponse>(cancellationToken: cancellationToken);
                if ((result?.Upserted?.TripRecords ?? 0) <= 0)
                    return false;

                InvalidateTripCache(record);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible actualizar el importe de dejada del viaje {RecordId} en la API APP_TAXI.", recordId);
                return false;
            }
        }

        public async Task<bool> PushBadgeStatesAsync(
            IReadOnlyCollection<AppTaxiBadgeState> badges,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var items = badges
                    .Where(x => !string.IsNullOrWhiteSpace(x.BadgeId))
                    .Select(x => new AppGafeteDto
                    {
                        BadgeId = x.BadgeId.Trim(),
                        Barcode = x.BadgeId.Trim(),
                        Status = string.IsNullOrWhiteSpace(x.Status) ? "Disponible" : x.Status.Trim(),
                        Cycle = x.Cycle <= 0 ? 1 : x.Cycle,
                        TaxistaId = x.TaxistaId,
                        TaxistaName = x.TaxistaName?.Trim() ?? string.Empty,
                        CreatedAt = DateTime.Now
                    })
                    .GroupBy(x => $"{x.BadgeId}|{x.Cycle}", StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList();

                if (items.Count == 0)
                    return true;

                using var request = new HttpRequestMessage(HttpMethod.Post, "sync/push-changes");
                request.Headers.TryAddWithoutValidation("X-Sync-Token", _syncToken);
                request.Content = JsonContent.Create(new PushChangesPayload
                {
                    Gafetes = items
                });

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return false;

                var result = await response.Content.ReadFromJsonAsync<PushChangesResponse>(cancellationToken: cancellationToken);
                return (result?.Upserted?.Gafetes ?? 0) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible sincronizar estados de gafetes hacia la API APP_TAXI.");
                return false;
            }
        }

        public async Task<bool> PushTripRecordAsync(
            string recordId,
            PosRegistroAppViewModel model,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(recordId))
                    return false;

                var record = new AppTripRecordDto
                {
                    RecordId = recordId.Trim(),
                    UsuarioMovil = "ControlTaxiWeb",
                    CreatedBy = "ControlTaxiWeb",
                    FolioPos = string.Empty,
                    TicketSale = string.Empty,
                    CatalogId = model.TaxistaId,
                    BadgeId = model.Gafete,
                    DriverName = model.TaxistaNombre,
                    DriverPhone = model.Telefono,
                    ContactPhone = FirstText(model.TelefonoContacto, model.Telefono),
                    Nationality = model.Nacionalidad,
                    Plate = model.Placas,
                    VehicleModel = model.Modelo,
                    UnitNumber = model.Unidad,
                    Hotel = model.Hotel,
                    Origin = model.Origen,
                    Site = model.Sitio,
                    Destination = model.Destino,
                    PassengerCount = Math.Max(model.Pax, 1),
                    ServiceType = model.TipoOperacion,
                    TripCost = model.Total,
                    PaymentMethod = model.MetodoPago,
                    Notes = model.Notas,
                    RecordDate = model.FechaOperacion == default ? DateTime.Now : model.FechaOperacion,
                    AssignedBranch = model.SucursalAsignada,
                    AssignedBranchCode = "100",
                    AssignedBranchName = model.SucursalAsignada,
                    PayoutStatus = "pendiente",
                    PayoutDate = string.Empty,
                    PayoutUser = string.Empty,
                    PayoutTicket = string.Empty
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, "sync/push-changes");
                request.Headers.TryAddWithoutValidation("X-Sync-Token", _syncToken);
                request.Content = JsonContent.Create(new PushChangesPayload
                {
                    TripRecords = new List<AppTripRecordDto> { record }
                });

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return false;

                var result = await response.Content.ReadFromJsonAsync<PushChangesResponse>(cancellationToken: cancellationToken);
                if ((result?.Upserted?.TripRecords ?? 0) <= 0)
                    return false;

                InvalidateTripCache(record);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible sincronizar el registro web {RecordId} hacia Hostinger.", recordId);
                return false;
            }
        }

        private void InvalidateTripCache(AppTripRecordDto record)
        {
            if (record?.RecordDate == null || string.IsNullOrWhiteSpace(record.RecordId))
                return;

            var day = record.RecordDate.Value.Date;
            var recordId = record.RecordId.Trim();

            _cache.Remove($"app-taxi-registros:{recordId}::");
            _cache.Remove($"app-taxi-registros:{recordId}:{day:yyyyMMdd}:{day:yyyyMMdd}");
            _cache.Remove($"app-taxi-registros::{day:yyyyMMdd}:{day:yyyyMMdd}");
            _cache.Remove($"app-taxi-registros::{day:yyyyMMdd}:");
            _cache.Remove($"app-taxi-registros:::{day:yyyyMMdd}");
        }

        private sealed class AppTripRecordDto
        {
            public string? RecordId { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("usuario_movil")]
            public string? UsuarioMovil { get; set; }
            public string? UserName { get; set; }
            public string? CreatedBy { get; set; }
            public string? CreatedByName { get; set; }
            public string? User { get; set; }
            public string? FolioPos { get; set; }
            public string? TicketSale { get; set; }
            public int? CatalogId { get; set; }
            public string? BadgeId { get; set; }
            public string? DriverName { get; set; }
            public string? DriverPhone { get; set; }
            public string? ContactPhone { get; set; }
            public string? Nationality { get; set; }
            [JsonPropertyName("nacionalidad")]
            public string? Nacionalidad { get; set; }
            public string? Plate { get; set; }
            public string? VehicleModel { get; set; }
            public string? UnitNumber { get; set; }
            public string? Hotel { get; set; }
            public string? Origin { get; set; }
            public string? Site { get; set; }
            public string? Destination { get; set; }
            public int PassengerCount { get; set; }
            public string? ServiceType { get; set; }
            public decimal TripCost { get; set; }
            public string? PaymentMethod { get; set; }
            public string? Notes { get; set; }
            // La API envia la fecha con offset (-06:00). Se usa DateTimeOffset para
            // conservar el reloj original tal cual viene, sin que .NET lo convierta a
            // la zona horaria de la maquina (eso provocaba +1h en Cancun).
            public DateTimeOffset? RecordDate { get; set; }
            public string? AssignedBranch { get; set; }
            public string? AssignedBranchCode { get; set; }
            public string? AssignedBranchName { get; set; }
            public string? PayoutStatus { get; set; }
            public string? PayoutDate { get; set; }
            public string? PayoutUser { get; set; }
            public string? PayoutTicket { get; set; }
        }

        private sealed class AppCatalogTaxiItemDto
        {
            public int CatalogId { get; set; }
            public string? BadgeId { get; set; }
            public string? DriverName { get; set; }
            public string? PhoneNumber { get; set; }
            public string? Plate { get; set; }
            public string? VehicleModel { get; set; }
            public string? UnitNumber { get; set; }
            public string? ServiceType { get; set; }
            public string? Nationality { get; set; }
            [JsonPropertyName("nacionalidad")]
            public string? Nacionalidad { get; set; }
            public string? Site { get; set; }
            public string? Hotel { get; set; }
            public string? Destination { get; set; }
            public string? Notes { get; set; }
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public decimal SuggestedAmount { get; set; }
        }

        private sealed class AppTaxiOptionsDto
        {
            public List<string> Hotels { get; set; } = new();
            public List<string> ServiceTypes { get; set; } = new();
            public List<string> Sites { get; set; } = new();
        }

        private sealed class AppServiceRateDto
        {
            public string? ServiceType { get; set; }
            public string? Description { get; set; }
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public decimal BaseAmount { get; set; }
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public decimal ExtraPersonAmount { get; set; }
        }

        private sealed class AppGafeteDto
        {
            [JsonPropertyName("badgeId")]
            public string BadgeId { get; set; } = string.Empty;
            [JsonPropertyName("barcode")]
            public string Barcode { get; set; } = string.Empty;
            [JsonPropertyName("status")]
            public string Status { get; set; } = "Disponible";
            [JsonPropertyName("cycle")]
            public int Cycle { get; set; } = 1;
            [JsonPropertyName("taxistaId")]
            public int? TaxistaId { get; set; }
            [JsonPropertyName("taxistaName")]
            public string TaxistaName { get; set; } = string.Empty;
            [JsonPropertyName("createdAt")]
            public DateTime CreatedAt { get; set; }
        }

        private sealed class PushChangesPayload
        {
            [System.Text.Json.Serialization.JsonPropertyName("mkt2_trip_records")]
            public List<AppTripRecordDto> TripRecords { get; set; } = new();

            [System.Text.Json.Serialization.JsonPropertyName("mkt2_gafetes")]
            public List<AppGafeteDto> Gafetes { get; set; } = new();
        }

        private sealed class PushChangesResponse
        {
            public bool Ok { get; set; }
            public PushChangesUpserted? Upserted { get; set; }
        }

        private sealed class PushChangesUpserted
        {
            [System.Text.Json.Serialization.JsonPropertyName("mkt2_trip_records")]
            public int TripRecords { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("mkt2_gafetes")]
            public int Gafetes { get; set; }
        }
    }
}
