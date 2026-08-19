using System;
using System.Collections.Generic;

namespace YourNamespace.Features.Gafetes.Contracts;

/// <summary>
/// Solicitud para sincronizar gafetes de Casco.
/// </summary>
public class SyncGafetesRequest
{
    /// <summary>
    /// Código de sucursal. Solo se acepta "CV".
    /// </summary>
    public required string BranchCode { get; set; }

    /// <summary>
    /// Lista de gafetes a sincronizar.
    /// Preferido sobre mkt2_gafetes.
    /// </summary>
    public List<GafeteDto>? Gafetes { get; set; }

    /// <summary>
    /// Lista de gafetes (formato alternativo para retrocompatibilidad).
    /// Se usa si Gafetes es nulo.
    /// </summary>
    public List<GafeteDto>? Mkt2Gafetes { get; set; }

    /// <summary>
    /// Obtiene la lista de gafetes, priorizando Gafetes sobre Mkt2Gafetes.
    /// </summary>
    public List<GafeteDto> GetGafetes() => Gafetes ?? Mkt2Gafetes ?? new List<GafeteDto>();
}

/// <summary>
/// Datos de un gafete individual.
/// </summary>
public class GafeteDto
{
    /// <summary>
    /// Identificador único del gafete (requerido).
    /// </summary>
    public required string BadgeId { get; set; }

    /// <summary>
    /// Código de barras del gafete (requerido).
    /// </summary>
    public required string Barcode { get; set; }

    /// <summary>
    /// Estado del gafete: A (Activo), R (Retirado), S (Suspendido).
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Ciclo del gafete (entero >= 1).
    /// </summary>
    public required int Cycle { get; set; }

    /// <summary>
    /// ID del taxista asociado (opcional).
    /// </summary>
    public int? TaxistaId { get; set; }

    /// <summary>
    /// Nombre del taxista (opcional).
    /// </summary>
    public string? TaxistaName { get; set; }

    /// <summary>
    /// Fecha y hora de creación en formato ISO 8601.
    /// </summary>
    public required string CreatedAt { get; set; }
}

/// <summary>
/// Respuesta de la operación de sincronización.
/// </summary>
public class SyncGafetesResponse
{
    /// <summary>
    /// Indica si el procesamiento fue exitoso.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Sucursal procesada.
    /// </summary>
    public string? BranchCode { get; set; }

    /// <summary>
    /// Cantidad total de gafetes recibidos.
    /// </summary>
    public int Received { get; set; }

    /// <summary>
    /// Cantidad de gafetes nuevos insertados.
    /// </summary>
    public int Inserted { get; set; }

    /// <summary>
    /// Cantidad de gafetes actualizados (status cambió).
    /// </summary>
    public int Updated { get; set; }

    /// <summary>
    /// Cantidad de gafetes sin cambios.
    /// </summary>
    public int Unchanged { get; set; }

    /// <summary>
    /// Lista de errores (si aplica).
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Mensaje de error general (si aplica).
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Índice del gafete que causó el error (si aplica).
    /// </summary>
    public int? ErrorIndex { get; set; }

    /// <summary>
    /// Timestamp de procesamiento.
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Error de validación detallado.
/// </summary>
public class ValidationError
{
    /// <summary>
    /// Campo que generó el error.
    /// </summary>
    public required string Field { get; set; }

    /// <summary>
    /// Mensaje de error.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Índice en la colección (si aplica).
    /// </summary>
    public int? Index { get; set; }

    /// <summary>
    /// Valor que causó el error.
    /// </summary>
    public string? Value { get; set; }
}
