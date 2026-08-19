using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface ITimelineService
{
    Task<IReadOnlyList<NavierasTimelineEvent>> GetTimelineAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default);
}
