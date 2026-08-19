using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ControlTaxiDesktop.Models;

namespace ControlTaxiDesktop.Services;

public enum BadgeScanNotice
{
    Selected,
    Duplicate,
    Free
}

public sealed record BadgeScanResolution(
    string Token,
    LocalBadge Badge,
    bool FoundInLoadedRows,
    bool AlreadySelected,
    bool IsOccupied,
    BadgeScanNotice Notice);

public static class BadgeSelectionWorkflow
{
    private static readonly char[] ScanSeparators = [',', ';', '/', '|', '\r', '\n', '\t', ' '];

    public static IReadOnlyList<string> SplitScanValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var values = raw
            .Split(ScanSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeScanToken)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return values;
    }

    public static string NormalizeScanToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // Eliminar espacios, \r, \n, \t y cualquier carácter de control
        // que algunos escáneres inyectan como prefijo/sufijo del código de barras
        var cleaned = new string(value
            .Where(c => !char.IsControl(c) && !char.IsWhiteSpace(c))
            .ToArray());

        return cleaned;
    }

    public static bool IsOccupiedStatus(string? status)
    {
        var normalized = NormalizeScanToken(status).ToUpperInvariant();
        return normalized is "A" or "ASIGNADO" or "OCUPADO";
    }

    public static string BuildSelectionKey(string? number, string? operationFolio) =>
        $"{NormalizeScanToken(number).ToUpperInvariant()}|{NormalizeScanToken(operationFolio).ToUpperInvariant()}";

    public static LocalBadge? FindLoadedBadge(IEnumerable<LocalBadge> rows, string token)
    {
        var normalized = NormalizeScanToken(token);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        // Intentar también la comparación numérica para manejar ceros iniciales:
        // si el escáner envía "0278" pero el grid tiene "278" (columna int), debe encontrarlo.
        var numericEquivalent = long.TryParse(normalized,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? n.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

        return rows.FirstOrDefault(row =>
            FieldContainsToken(row.Number, normalized)
            || FieldContainsToken(row.LocalFolio, normalized)
            || FieldContainsToken(row.OperationFolio, normalized)
            || (numericEquivalent is not null && FieldContainsToken(row.Number, numericEquivalent)));
    }

    public static bool IsAlreadyScanned(IEnumerable<LocalScannedBadgeItem> scannedBadges, LocalBadge badge)
    {
        var key = BuildSelectionKey(badge.Number, badge.OperationFolio);
        return scannedBadges.Any(item => string.Equals(
            BuildSelectionKey(item.Number, item.OperationFolio),
            key,
            StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<BadgeScanResolution> ResolveScanAsync(
        string rawValue,
        IEnumerable<LocalBadge> loadedRows,
        IEnumerable<LocalScannedBadgeItem> scannedBadges,
        Func<string, Task<LocalBadge?>> resolveMissingAsync,
        bool isCascoBranch)
    {
        var token = NormalizeScanToken(rawValue);
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("El gafete escaneado esta vacio.");

        var badge = FindLoadedBadge(loadedRows, token);
        var foundInLoadedRows = badge is not null;

        if (badge is null)
        {
            badge = await resolveMissingAsync(token);
            if (badge is null)
            {
                throw new ArgumentException(isCascoBranch
                    ? $"No se encontro el gafete {token} en Casco Viejo."
                    : $"No se encontro el gafete {token}.");
            }
        }

        var alreadySelected = IsAlreadyScanned(scannedBadges, badge);
        var isOccupied = IsOccupiedStatus(badge.Status);
        var notice = alreadySelected
            ? BadgeScanNotice.Duplicate
            : isOccupied
                ? BadgeScanNotice.Selected
                : BadgeScanNotice.Free;

        return new BadgeScanResolution(
            token,
            badge,
            foundInLoadedRows,
            alreadySelected,
            isOccupied,
            notice);
    }

    private static bool FieldContainsToken(string? rawField, string token)
    {
        var normalizedField = NormalizeScanToken(rawField);
        var normalizedToken = NormalizeScanToken(token);
        if (string.IsNullOrWhiteSpace(normalizedField) || string.IsNullOrWhiteSpace(normalizedToken))
            return false;

        if (string.Equals(normalizedField, normalizedToken, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedField
            .Split(ScanSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => string.Equals(part, normalizedToken, StringComparison.OrdinalIgnoreCase));
    }
}
