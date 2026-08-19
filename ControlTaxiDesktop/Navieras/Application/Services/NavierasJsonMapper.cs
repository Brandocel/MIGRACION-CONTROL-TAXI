using System.Globalization;
using System.Text.Json;
using ControlTaxiDesktop.Navieras.Domain;

namespace ControlTaxiDesktop.Navieras.Application.Services;

public static class NavierasJsonMapper
{
    public static NavierasUser MapUser(JsonElement root)
    {
        var userElement = root.TryGetProperty("user", out var nestedUser) ? nestedUser : root;
        var roles = root.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array
            ? rolesElement.EnumerateArray().Select(static x => x.GetString() ?? string.Empty).Where(static x => x.Length > 0).ToArray()
            : [];
        var permissions = root.TryGetProperty("permissions", out var permissionsElement) && permissionsElement.ValueKind == JsonValueKind.Array
            ? permissionsElement.EnumerateArray().Select(static x => x.GetString() ?? string.Empty).Where(static x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new NavierasUser(
            GetString(userElement, "id"),
            GetString(userElement, "username"),
            GetString(userElement, "displayName", "nombre"),
            roles.FirstOrDefault() ?? "Sin rol",
            permissions,
            true,
            false);
    }

    public static NavierasDashboardSnapshot MapDashboard(JsonElement data)
        => new(
            GetInt(data, "boatsToday"),
            GetInt(data, "servicesToday"),
            GetInt(data, "passengersToday", "scheduledPassengers"),
            GetInt(data, "inOperation"),
            GetInt(data, "finished"),
            GetInt(data, "delayed"));

    public static IReadOnlyList<NavierasCompany> MapCompanies(JsonElement data)
        => data.EnumerateArray().Select(static item => new NavierasCompany(
            GetString(item, "id"),
            GetString(item, "clave", "code"),
            GetString(item, "nombre", "name"),
            GetBool(item, "activo", defaultValue: true))).ToArray();

    public static IReadOnlyList<NavierasBoat> MapBoats(JsonElement data)
        => data.EnumerateArray().Select(static item => new NavierasBoat(
            GetString(item, "id"),
            GetString(item, "compania_id", "companyId"),
            GetString(item, "nombre", "name"),
            GetString(item, "tipo_embarcacion", "imageUrl"),
            GetBool(item, "activo", defaultValue: true))).ToArray();

    public static IReadOnlyList<NavierasDock> MapDocks(JsonElement data)
        => data.EnumerateArray().Select(static item => new NavierasDock(
            GetString(item, "id"),
            GetString(item, "clave", "code"),
            GetString(item, "nombre", "name"),
            GetString(item, "zona", "colorHex"),
            GetBool(item, "activo", defaultValue: true))).ToArray();

    public static IReadOnlyList<NavierasPerson> MapPeople(JsonElement data, string roleLabel)
        => data.EnumerateArray().Select(item => new NavierasPerson(
            GetString(item, "id"),
            GetString(item, "clave"),
            GetString(item, "nombre", "name"),
            roleLabel,
            GetBool(item, "activo", defaultValue: true),
            GetNullableString(item, "numero_guia"))).ToArray();

    public static IReadOnlyList<NavierasServiceSummary> MapServices(JsonElement data)
        => data.EnumerateArray().Select(MapService).ToArray();

    public static NavierasServiceSummary MapService(JsonElement item)
        => new(
            GetString(item, "servicio_id", "id"),
            GetString(item, "folio_servicio"),
            GetString(item, "compania_id"),
            GetString(item, "compania_nombre"),
            GetString(item, "barco_id"),
            GetString(item, "barco_nombre"),
            ParseDateOnly(GetString(item, "fecha_operacion")),
            ParseDateTime(GetString(item, "hora_llegada_programada")),
            ParseDateTime(GetString(item, "hora_salida_programada")),
            ParseNullableDateTime(item, "hora_llegada_real"),
            ParseNullableDateTime(item, "hora_salida_real"),
            GetString(item, "muelle_programado_id"),
            GetString(item, "muelle_programado_nombre"),
            GetNullableString(item, "muelle_real_id"),
            GetNullableString(item, "muelle_real_nombre"),
            GetString(item, "guia_programado_id"),
            BuildGuideLabel(item, "guia_programado_numero", "guia_programado_nombre", "guia_programado_nombre") ?? string.Empty,
            GetNullableString(item, "guia_real_id"),
            BuildGuideLabel(item, "guia_real_numero", "guia_real_nombre", "guia_real_nombre"),
            GetString(item, "capitan_programado_id"),
            GetString(item, "capitan_programado_nombre"),
            GetNullableString(item, "capitan_real_id"),
            GetNullableString(item, "capitan_real_nombre"),
            GetInt(item, "pax_programados", "scheduledPax"),
            GetNullableInt(item, "pax_reales"),
            GetNullableInt(item, "pax_salida"),
            GetString(item, "estado", "status"),
            GetString(item, "observaciones", "notes"),
            GetNullableString(item, "folio_brazalete_real", "folio_brazalete_programado"),
            GetBool(item, "brazalete_validado"),
            GetNullableString(item, "brazalete_validado_por_nombre", "braceletValidatedBy"),
            ParseNullableDateTime(item, "brazalete_validado_en"),
            GetNullableString(item, "motivo_cancelacion", "cancelReason"));

    public static IReadOnlyList<NavierasTimelineEvent> MapTimeline(JsonElement data)
        => data.EnumerateArray().Select(static item => new NavierasTimelineEvent(
            GetString(item, "id"),
            ParseDateTime(GetString(item, "fecha_evento", "timestamp")),
            GetString(item, "servicio_id"),
            GetString(item, "titulo", "title"),
            GetString(item, "descripcion", "description"),
            GetString(item, "barco_nombre", "shipName"),
            GetString(item, "compania_nombre", "shippingCompanyName"),
            GetNullableString(item, "usuario_nombre", "userName"),
            GetString(item, "tipo", "type"))).ToArray();

    public static IReadOnlyList<NavierasOperationFolio> MapOperationFolios(JsonElement data)
        => data.EnumerateArray().Select(static item => new NavierasOperationFolio(
            GetString(item, "id"),
            GetString(item, "servicio_id"),
            GetString(item, "folio"),
            GetString(item, "compania_nombre"),
            GetString(item, "barco_nombre"),
            ParseDateOnly(GetString(item, "fecha_operacion")),
            GetString(item, "capturado_por_nombre", "capturedBy"),
            ParseNullableDateTime(item, "created_at", "capturedAt"))).ToArray();

    public static IReadOnlyList<NavierasCatalogItem> MapCatalogItems(JsonElement data, string type)
        => data.EnumerateArray().Select(item => new NavierasCatalogItem(
            GetString(item, "id"),
            type,
            GetString(item, "clave", "code"),
            GetString(item, "nombre", "name"),
            GetString(item, "subtitle", "descripcion", "zona"),
            GetBool(item, "activo", defaultValue: true),
            GetInt(item, "usageCount"),
            GetNullableString(item, "compania_id", "parentId"))).ToArray();

    public static IReadOnlyList<NavierasEditableUser> MapUsers(JsonElement data)
        => data.EnumerateArray().Select(MapEditableUser).ToArray();

    public static NavierasEditableUser MapEditableUser(JsonElement item)
    {
        var roles = item.TryGetProperty("roles", out var rolesElement) && rolesElement.ValueKind == JsonValueKind.Array
            ? rolesElement.EnumerateArray().Select(static x => x.GetString() ?? string.Empty).Where(static x => x.Length > 0).ToArray()
            : [];
        return new NavierasEditableUser(
            new NavierasUser(
                GetString(item, "id"),
                GetString(item, "usuario", "username"),
                GetString(item, "nombre", "displayName"),
                roles.FirstOrDefault() ?? GetString(item, "rol", "role"),
                Array.Empty<string>().ToHashSet(StringComparer.OrdinalIgnoreCase),
                GetBool(item, "activo", defaultValue: true),
                GetBool(item, "bloqueado")),
            GetBool(item, "activo", defaultValue: true),
            GetBool(item, "bloqueado"),
            GetInt(item, "intentos_fallidos"),
            ParseNullableDateTime(item, "bloqueado_hasta"),
            ParseNullableDateTime(item, "ultimo_login_at"));
    }

    public static IReadOnlyList<NavierasRole> MapRoles(JsonElement data)
        => data.EnumerateArray().Select(static item => new NavierasRole(
            GetString(item, "id"),
            GetString(item, "clave", "key"),
            GetString(item, "nombre", "name"),
            GetBool(item, "activo", defaultValue: true),
            GetInt(item, "userCount"),
            item.TryGetProperty("permissions", out var permissionsElement) && permissionsElement.ValueKind == JsonValueKind.Array
                ? permissionsElement.EnumerateArray().Select(static x => x.GetString() ?? string.Empty).Where(static x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase))).ToArray();

    public static IReadOnlyList<NavierasPermissionItem> MapPermissions(JsonElement data)
        => data.EnumerateArray().Select(static item => new NavierasPermissionItem(
            GetString(item, "id"),
            GetString(item, "clave", "code"),
            GetString(item, "nombre", "name"),
            GetString(item, "descripcion", "description"),
            GetBool(item, "activo", defaultValue: true))).ToArray();

    public static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString() ?? string.Empty,
                    JsonValueKind.Number => property.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => string.Empty
                };
            }
        }
        return string.Empty;
    }

    public static string? GetNullableString(JsonElement element, params string[] names)
    {
        var value = GetString(element, names);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static int GetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
                    return value;
                if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return value;
            }
        }
        return 0;
    }

    public static int? GetNullableInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                if (property.ValueKind == JsonValueKind.Null)
                    return null;
                if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
                    return value;
                if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                    return value;
            }
        }
        return null;
    }

    public static bool GetBool(JsonElement element, string name, bool defaultValue = false)
    {
        if (!element.TryGetProperty(name, out var property))
            return defaultValue;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => property.TryGetInt32(out var value) && value != 0,
            JsonValueKind.String => bool.TryParse(property.GetString(), out var value) ? value : property.GetString() == "1",
            _ => defaultValue
        };
    }

    public static DateOnly ParseDateOnly(string value)
    {
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
            return dateOnly;
        var dateTime = ParseDateTime(value);
        return DateOnly.FromDateTime(dateTime.LocalDateTime);
    }

    public static DateTimeOffset ParseDateTime(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return parsed;
        return DateTimeOffset.MinValue;
    }

    public static DateTimeOffset? ParseNullableDateTime(JsonElement element, params string[] names)
    {
        var value = GetNullableString(element, names);
        return string.IsNullOrWhiteSpace(value) ? null : ParseDateTime(value);
    }

    private static string? BuildGuideLabel(JsonElement item, string numberField, string nameField, string fallbackField)
    {
        var number = GetNullableString(item, numberField);
        var name = GetNullableString(item, nameField) ?? GetNullableString(item, fallbackField);
        if (!string.IsNullOrWhiteSpace(number) && !string.IsNullOrWhiteSpace(name))
            return $"{number} - {name}";
        return name;
    }
}
