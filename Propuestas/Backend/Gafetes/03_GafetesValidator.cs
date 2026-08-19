using System;
using System.Collections.Generic;
using System.Linq;
using YourNamespace.Features.Gafetes.Contracts;
using YourNamespace.Features.Gafetes.Models;

namespace YourNamespace.Features.Gafetes.Services;

/// <summary>
/// Validador de solicitudes de sincronización de gafetes.
/// </summary>
public interface IGafetesValidator
{
    /// <summary>
    /// Valida una solicitud de sincronización.
    /// </summary>
    ValidationResult Validate(SyncGafetesRequest request);
}

public class GafetesValidator : IGafetesValidator
{
    private const int MaxGafetesPerBatch = 1000;
    private const string ValidBranchCode = "CV";
    private const string ProtectedBranchCode = "28";

    public ValidationResult Validate(SyncGafetesRequest request)
    {
        var errors = new List<ValidationError>();

        // Validar BranchCode
        if (string.IsNullOrWhiteSpace(request?.BranchCode))
        {
            errors.Add(new ValidationError
            {
                Field = "branchCode",
                Message = "branchCode es obligatorio"
            });
            return new ValidationResult { Errors = errors };
        }

        // Proteger Plaza 28
        if (request.BranchCode == ProtectedBranchCode)
        {
            errors.Add(new ValidationError
            {
                Field = "branchCode",
                Message = "No se pueden sincronizar gafetes de Plaza 28"
            });
            return new ValidationResult { Errors = errors };
        }

        // Validar que sea CV
        if (request.BranchCode != ValidBranchCode)
        {
            errors.Add(new ValidationError
            {
                Field = "branchCode",
                Message = $"branchCode debe ser '{ValidBranchCode}'"
            });
            return new ValidationResult { Errors = errors };
        }

        // Obtener lista de gafetes
        var gafetes = request.GetGafetes();

        // Validar que no esté vacío
        if (gafetes.Count == 0)
        {
            errors.Add(new ValidationError
            {
                Field = "gafetes",
                Message = "gafetes no puede estar vacío"
            });
            return new ValidationResult { Errors = errors };
        }

        // Validar tamaño del lote
        if (gafetes.Count > MaxGafetesPerBatch)
        {
            errors.Add(new ValidationError
            {
                Field = "gafetes",
                Message = $"Máximo {MaxGafetesPerBatch} gafetes por request"
            });
            return new ValidationResult { Errors = errors };
        }

        // Validar cada gafete
        for (int i = 0; i < gafetes.Count; i++)
        {
            var gafeteErrors = ValidateGafete(gafetes[i], i);
            errors.AddRange(gafeteErrors);
        }

        // Validar duplicados
        var duplicateErrors = ValidateDuplicates(gafetes, request.BranchCode);
        errors.AddRange(duplicateErrors);

        return new ValidationResult { Errors = errors };
    }

    private List<ValidationError> ValidateGafete(GafeteDto gafete, int index)
    {
        var errors = new List<ValidationError>();

        // Validar BadgeId
        if (string.IsNullOrWhiteSpace(gafete?.BadgeId))
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].badgeId",
                Message = "badgeId es obligatorio",
                Index = index
            });
        }
        else if (gafete.BadgeId.Length > 50)
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].badgeId",
                Message = "badgeId no puede exceder 50 caracteres",
                Index = index,
                Value = gafete.BadgeId
            });
        }

        // Validar Barcode
        if (string.IsNullOrWhiteSpace(gafete?.Barcode))
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].barcode",
                Message = "barcode es obligatorio",
                Index = index
            });
        }
        else if (gafete.Barcode.Length > 50)
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].barcode",
                Message = "barcode no puede exceder 50 caracteres",
                Index = index,
                Value = gafete.Barcode
            });
        }

        // Validar Status
        if (string.IsNullOrWhiteSpace(gafete?.Status))
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].status",
                Message = "status es obligatorio",
                Index = index
            });
        }
        else if (!GafeteStatus.IsValid(gafete.Status))
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].status",
                Message = $"'{gafete.Status}' no es válido. Permitidos: {string.Join(", ", GafeteStatus.ValidValues)}",
                Index = index,
                Value = gafete.Status
            });
        }

        // Validar Cycle
        if (gafete?.Cycle is null or < 1)
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].cycle",
                Message = "cycle debe ser un entero >= 1",
                Index = index,
                Value = gafete?.Cycle.ToString()
            });
        }

        // Validar CreatedAt
        if (string.IsNullOrWhiteSpace(gafete?.CreatedAt))
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].createdAt",
                Message = "createdAt es obligatorio",
                Index = index
            });
        }
        else if (!DateTime.TryParse(gafete.CreatedAt, out _))
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].createdAt",
                Message = "createdAt no es una fecha válida (formato ISO 8601 esperado)",
                Index = index,
                Value = gafete.CreatedAt
            });
        }

        // Validar TaxistaName (longitud)
        if (!string.IsNullOrEmpty(gafete?.TaxistaName) && gafete.TaxistaName.Length > 255)
        {
            errors.Add(new ValidationError
            {
                Field = $"gafetes[{index}].taxistaName",
                Message = "taxistaName no puede exceder 255 caracteres",
                Index = index,
                Value = gafete.TaxistaName
            });
        }

        return errors;
    }

    private List<ValidationError> ValidateDuplicates(List<GafeteDto> gafetes, string branchCode)
    {
        var errors = new List<ValidationError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var gafete in gafetes)
        {
            // Usar clave única: branchCode:badgeId:cycle
            var key = $"{branchCode}:{gafete.BadgeId}:{gafete.Cycle}".ToLowerInvariant();

            if (!seen.Add(key))
            {
                errors.Add(new ValidationError
                {
                    Field = "gafetes",
                    Message = $"Gafete duplicado en el payload: badgeId={gafete.BadgeId} (cycle={gafete.Cycle})"
                });
            }
        }

        return errors;
    }
}

/// <summary>
/// Resultado de validación.
/// </summary>
public class ValidationResult
{
    public List<ValidationError> Errors { get; set; } = new();

    public bool IsValid => !Errors.Any();

    /// <summary>
    /// Obtiene el primer error de validación (si aplica).
    /// </summary>
    public ValidationError? GetFirstError() => Errors.FirstOrDefault();

    /// <summary>
    /// Obtiene errores por índice de gafete (si aplica).
    /// </summary>
    public List<ValidationError> GetErrorsForIndex(int index) =>
        Errors.Where(e => e.Index == index).ToList();
}
