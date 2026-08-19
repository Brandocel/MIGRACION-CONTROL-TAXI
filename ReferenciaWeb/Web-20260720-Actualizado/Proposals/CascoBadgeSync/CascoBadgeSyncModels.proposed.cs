using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ControlTaxiWeb.Proposals.CascoBadgeSync;

public sealed class CascoBadgeSyncRequest
{
    [JsonPropertyName("branchCode")]
    public string? BranchCode { get; set; }

    [JsonPropertyName("gafetes")]
    public List<CascoBadgeSyncItem>? Gafetes { get; set; }

    // Compatibilidad con el contrato actual del cliente local.
    [JsonPropertyName("mkt2_gafetes")]
    public List<CascoBadgeSyncItem>? LegacyGafetes { get; set; }

    public IReadOnlyList<CascoBadgeSyncItem> ResolveItems() =>
        (Gafetes ?? LegacyGafetes ?? new List<CascoBadgeSyncItem>())
        .Where(item => item is not null)
        .ToList();
}

public sealed class CascoBadgeSyncItem
{
    [Required]
    [JsonPropertyName("badgeId")]
    public string BadgeId { get; set; } = string.Empty;

    [JsonPropertyName("barcode")]
    public string Barcode { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    [JsonPropertyName("cycle")]
    public int Cycle { get; set; } = 1;

    [JsonPropertyName("taxistaId")]
    public int? TaxistaId { get; set; }

    [JsonPropertyName("taxistaName")]
    public string TaxistaName { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAtRaw { get; set; } = string.Empty;

    public bool TryParseCreatedAt(out DateTimeOffset createdAt) =>
        DateTimeOffset.TryParse(CreatedAtRaw, out createdAt);
}

public sealed class CascoBadgeSyncRow
{
    public string BranchCode { get; set; } = "CV";
    public string BadgeId { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Cycle { get; set; }
    public int? TaxistaId { get; set; }
    public string TaxistaName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CascoBadgeSyncResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("branchCode")]
    public string BranchCode { get; set; } = "CV";

    [JsonPropertyName("received")]
    public int Received { get; set; }

    [JsonPropertyName("inserted")]
    public int Inserted { get; set; }

    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    [JsonPropertyName("unchanged")]
    public int Unchanged { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();
}

public sealed class CascoBadgeSyncResult
{
    public int Received { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public List<string> Errors { get; } = new();
}
