using System;

namespace YourNamespace.Features.Gafetes.Models;

/// <summary>
/// Modelo de dominio para un gafete de Casco.
/// </summary>
public class Gafete
{
    public int Id { get; set; }
    public required string BranchCode { get; set; }
    public required string BadgeId { get; set; }
    public required string Barcode { get; set; }
    public required string Status { get; set; } // A, R, S
    public required int Cycle { get; set; }
    public int? TaxistaId { get; set; }
    public string? TaxistaName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Calcula un fingerprint basado en datos clave.
    /// Útil para detectar cambios.
    /// </summary>
    public string GetFingerprint() =>
        $"{BranchCode}:{BadgeId}:{Cycle}:{Status}:{TaxistaId}:{TaxistaName}".ToLowerInvariant();
}

/// <summary>
/// Resultado del procesamiento de sincronización.
/// </summary>
public class GafeteSyncResult
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Total => Inserted + Updated + Unchanged;
    public List<string> Errors { get; set; } = new();
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Estados válidos para un gafete.
/// </summary>
public static class GafeteStatus
{
    public const string Activo = "A";
    public const string Retirado = "R";
    public const string Suspendido = "S";

    public static IEnumerable<string> ValidValues => new[] { Activo, Retirado, Suspendido };

    public static bool IsValid(string status) => ValidValues.Contains(status);
}
