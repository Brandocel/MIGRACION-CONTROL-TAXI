using ControlTaxiDesktop.Navieras.Application.Interfaces;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public sealed class TimelineService(INavierasModuleService moduleService) : ITimelineService
{
    public Task<IReadOnlyList<NavierasTimelineEvent>> GetTimelineAsync(DateOnly? operationDate = null, CancellationToken cancellationToken = default)
        => moduleService.GetTimelineAsync(operationDate, cancellationToken);
}
