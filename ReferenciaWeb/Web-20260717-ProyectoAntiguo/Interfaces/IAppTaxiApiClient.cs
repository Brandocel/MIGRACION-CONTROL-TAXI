using ControlTaxiWeb.Models.PosTriton;

namespace ControlTaxiWeb.Interfaces
{
    public interface IAppTaxiApiClient
    {
        Task<IReadOnlyList<PosOperacionRowViewModel>> GetTripRecordsAsync(
            string? query,
            DateTime? fechaInicio,
            DateTime? fechaFin,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PosRegistroAppTaxistaOption>> SearchCatalogTaxistasAsync(
            string? query,
            int limit = 20,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<string>> GetCatalogHotelsAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PosRegistroAppTarifaOption>> GetCatalogRatesAsync(CancellationToken cancellationToken = default);

        Task<bool> MarkTripPayoutPaidAsync(
            string recordId,
            string user,
            string ticket,
            DateTime paidAt,
            CancellationToken cancellationToken = default);

        Task<bool> UpdateTripBadgesAsync(
            string recordId,
            IReadOnlyCollection<string> badges,
            CancellationToken cancellationToken = default);

        Task<bool> UpdateTripAmountAsync(
            string recordId,
            decimal tripCost,
            CancellationToken cancellationToken = default);

        Task<bool> PushBadgeStatesAsync(
            IReadOnlyCollection<AppTaxiBadgeState> badges,
            CancellationToken cancellationToken = default);

        Task<bool> PushTripRecordAsync(
            string recordId,
            PosRegistroAppViewModel model,
            CancellationToken cancellationToken = default);
    }

    public sealed record AppTaxiBadgeState(
        string BadgeId,
        string Status,
        int Cycle = 1,
        int? TaxistaId = null,
        string TaxistaName = "");
}
