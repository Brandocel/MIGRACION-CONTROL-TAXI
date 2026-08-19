using System.Data;
using System.Data.Common;
using System.Globalization;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
private static string PickText(IReadOnlyDictionary<string, object?> row, params string[] names)
        {
            foreach (var name in names)
            {
                if (row.TryGetValue(name, out var value) && value != null)
                {
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return string.Empty;
        }





private static int PickInt(IReadOnlyDictionary<string, object?> row, params string[] names)
        {
            foreach (var name in names)
            {
                if (row.TryGetValue(name, out var value) && value != null && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed))
                    return parsed;
            }

            return 0;
        }





private static string PickDate(IReadOnlyDictionary<string, object?> row, params string[] names)
        {
            foreach (var name in names)
            {
                if (row.TryGetValue(name, out var value) && value != null)
                {
                    if (value is DateTime dt)
                        return dt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }

            return string.Empty;
        }





private static object? PickObject(IReadOnlyDictionary<string, object?> row, string name) =>
            row.TryGetValue(name, out var value) ? value : null;





private static DateTime? ToDate(object? value)
        {
            if (value == null || value == DBNull.Value)
                return null;
            if (value is DateTime dt)
                return dt;
            return DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }





private static decimal ToDecimal(object? value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;
            if (value is decimal decimalValue) return decimalValue;
            if (value is double doubleValue) return Convert.ToDecimal(doubleValue);
            if (value is float floatValue) return Convert.ToDecimal(floatValue);
            if (value is int intValue) return intValue;
            if (value is long longValue) return longValue;
            return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;
        }





private static decimal ToDecimal(double? value) => Convert.ToDecimal(value ?? 0d);




private static decimal ToDecimal(float? value) => Convert.ToDecimal(value ?? 0f);





private static string FirstText(params string?[] values) =>
            values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;





private static string SafeText(string? value, int maxLength)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
        }





private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    }
}
