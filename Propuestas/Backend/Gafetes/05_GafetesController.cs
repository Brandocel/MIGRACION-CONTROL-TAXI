using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using YourNamespace.Features.Gafetes.Contracts;
using YourNamespace.Features.Gafetes.Models;
using YourNamespace.Features.Gafetes.Services;

namespace YourNamespace.Features.Gafetes.Controllers;

/// <summary>
/// Controlador para sincronizar gafetes de Casco.
/// </summary>
[ApiController]
[Route("api/gafetes")]
[Produces("application/json")]
[Authorize(Policy = "CascoApiAccess")]
public class GafetesController : ControllerBase
{
    private readonly IGafetesValidator _validator;
    private readonly IGafetesSyncService _syncService;
    private readonly ILogger<GafetesController> _logger;

    public GafetesController(
        IGafetesValidator validator,
        IGafetesSyncService syncService,
        ILogger<GafetesController> logger)
    {
        _validator = validator;
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// Sincroniza gafetes de Casco.
    /// 
    /// Solo se aceptan gafetes para sucursal "CV".
    /// Plaza 28 está protegida.
    /// 
    /// UPSERT por (branch_code, badge_id, cycle):
    /// - Si no existe: INSERT
    /// - Si existe y cambió status: UPDATE
    /// - Si existe igual: OMITIR
    /// </summary>
    /// <param name="request">Solicitud de sincronización</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>Resultado de sincronización</returns>
    [HttpPost("sync")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(SyncGafetesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SyncGafetesResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(SyncGafetesResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(SyncGafetesResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(SyncGafetesResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncGafetes(
        [FromBody] SyncGafetesRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Solicitud de sincronización recibida: BranchCode={BranchCode}, Gafetes count={Count}",
            request?.BranchCode, request?.GetGafetes().Count ?? 0);

        try
        {
            // Validar solicitud
            var validationResult = _validator.Validate(request);
            if (!validationResult.IsValid)
            {
                return HandleValidationErrors(validationResult);
            }

            var gafetes = request!.GetGafetes();
            var branchCode = request.BranchCode;

            // Sincronizar en base de datos
            var syncResult = await _syncService.SyncGafetesAsync(
                branchCode,
                gafetes,
                cancellationToken);

            if (syncResult.HasErrors)
            {
                _logger.LogError(
                    "Errores durante sincronización: {Errors}",
                    string.Join("; ", syncResult.Errors));

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new SyncGafetesResponse
                    {
                        Success = false,
                        BranchCode = branchCode,
                        Received = gafetes.Count,
                        Inserted = syncResult.Inserted,
                        Updated = syncResult.Updated,
                        Unchanged = syncResult.Unchanged,
                        Errors = syncResult.Errors,
                        ProcessedAt = DateTime.UtcNow
                    });
            }

            // Respuesta exitosa
            _logger.LogInformation(
                "Sincronización completada exitosamente: Inserted={Inserted}, Updated={Updated}, Unchanged={Unchanged}",
                syncResult.Inserted, syncResult.Updated, syncResult.Unchanged);

            return Ok(new SyncGafetesResponse
            {
                Success = true,
                BranchCode = branchCode,
                Received = gafetes.Count,
                Inserted = syncResult.Inserted,
                Updated = syncResult.Updated,
                Unchanged = syncResult.Unchanged,
                Errors = new(),
                ProcessedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no manejado en SyncGafetes");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new SyncGafetesResponse
                {
                    Success = false,
                    Error = "Error interno del servidor",
                    ProcessedAt = DateTime.UtcNow
                });
        }
    }

    /// <summary>
    /// Maneja errores de validación y devuelve respuesta apropiada.
    /// </summary>
    private IActionResult HandleValidationErrors(ValidationResult validationResult)
    {
        var firstError = validationResult.GetFirstError();
        if (firstError == null)
            return BadRequest();

        // Determinar código HTTP basado en tipo de error
        var httpStatusCode = DetermineHttpStatusCode(firstError.Field);

        var response = new SyncGafetesResponse
        {
            Success = false,
            Error = firstError.Message,
            ErrorIndex = firstError.Index,
            ProcessedAt = DateTime.UtcNow
        };

        _logger.LogWarning(
            "Validación fallida: {Field} - {Message}",
            firstError.Field, firstError.Message);

        return StatusCode(httpStatusCode, response);
    }

    /// <summary>
    /// Determina el código HTTP basado en el tipo de error.
    /// </summary>
    private static int DetermineHttpStatusCode(string field)
    {
        return field switch
        {
            // Errores de negocio/validación (400)
            "branchCode" when field.Contains("Plaza 28") => StatusCodes.Status400BadRequest,
            "branchCode" => StatusCodes.Status400BadRequest,
            "gafetes" when field.Contains("vacío") || field.Contains("Máximo") => StatusCodes.Status400BadRequest,

            // Errores de formato/contrato (422)
            _ when field.Contains("gafetes[") => StatusCodes.Status422UnprocessableEntity,
            _ when field.Contains("badgeId") => StatusCodes.Status422UnprocessableEntity,
            _ when field.Contains("status") => StatusCodes.Status422UnprocessableEntity,
            _ when field.Contains("cycle") => StatusCodes.Status422UnprocessableEntity,
            _ when field.Contains("createdAt") => StatusCodes.Status422UnprocessableEntity,

            // Default
            _ => StatusCodes.Status400BadRequest
        };
    }
}
