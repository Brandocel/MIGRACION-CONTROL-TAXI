namespace ControlTaxiDesktop.Navieras.Application.Interfaces;

public interface INavierasAuditRepository
{
    Task LogAsync(string userName, string action, string entityType, string entityId, string details, CancellationToken cancellationToken = default);
}
