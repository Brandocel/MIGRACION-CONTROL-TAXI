using Microsoft.AspNetCore.Mvc;

namespace ControlTaxiWeb.Proposals.CascoBadgeSync;

[ApiController]
[Route("api/gafetes")]
public sealed class CascoBadgeSyncController : ControllerBase
{
    private readonly ICascoBadgeSyncService _service;
    private readonly IConfiguration _configuration;

    public CascoBadgeSyncController(ICascoBadgeSyncService service, IConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
    }

    [HttpPost("sync")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<IActionResult> SyncAsync(
        [FromBody] CascoBadgeSyncRequest request,
        CancellationToken cancellationToken)
    {
        var expectedToken = _configuration["TaxiApi:SyncToken"] ?? "HokaTaxisSync2050";
        var providedToken = Request.Headers["X-Sync-Token"].ToString();
        if (!string.Equals(providedToken, expectedToken, StringComparison.Ordinal))
        {
            return Unauthorized(new
            {
                success = false,
                branchCode = request?.BranchCode ?? string.Empty,
                received = 0,
                inserted = 0,
                updated = 0,
                unchanged = 0,
                errors = new[] { "Token invalido." }
            });
        }

        if (!Request.HasJsonContentType())
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType, new
            {
                success = false,
                branchCode = request?.BranchCode ?? string.Empty,
                received = 0,
                inserted = 0,
                updated = 0,
                unchanged = 0,
                errors = new[] { "Content-Type debe ser application/json." }
            });
        }

        try
        {
            var items = request?.ResolveItems() ?? Array.Empty<CascoBadgeSyncItem>();
            var result = await _service.UpsertAsync(request?.BranchCode?.Trim() ?? string.Empty, items, cancellationToken);

            return Ok(new CascoBadgeSyncResponse
            {
                Success = true,
                BranchCode = request?.BranchCode?.Trim() ?? string.Empty,
                Received = result.Received,
                Inserted = result.Inserted,
                Updated = result.Updated,
                Unchanged = result.Unchanged,
                Errors = result.Errors
            });
        }
        catch (CascoBadgeSyncHttpException ex) when (ex.StatusCode == 400)
        {
            return BadRequest(new CascoBadgeSyncResponse
            {
                Success = false,
                BranchCode = request?.BranchCode?.Trim() ?? string.Empty,
                Errors = new List<string> { ex.Message }
            });
        }
        catch (CascoBadgeSyncHttpException ex) when (ex.StatusCode == 422)
        {
            return UnprocessableEntity(new CascoBadgeSyncResponse
            {
                Success = false,
                BranchCode = request?.BranchCode?.Trim() ?? string.Empty,
                Errors = new List<string> { ex.Message }
            });
        }
    }
}
