using Microsoft.Extensions.DependencyInjection;
using YourNamespace.Features.Gafetes.Services;

namespace YourNamespace.Features.Gafetes.Infrastructure;

/// <summary>
/// Extensiones para registrar servicios de gafetes en el contenedor DI.
/// </summary>
public static class GafetesServiceCollectionExtensions
{
    /// <summary>
    /// Registra los servicios de sincronización de gafetes.
    /// </summary>
    public static IServiceCollection AddGafetesServices(
        this IServiceCollection services,
        string connectionString)
    {
        // Registrar validador
        services.AddScoped<IGafetesValidator, GafetesValidator>();

        // Registrar factory de conexiones
        services.AddScoped<IDbConnectionFactory>(_ => new DbConnectionFactory(connectionString));

        // Registrar servicio de sincronización
        services.AddScoped<IGafetesSyncService, GafetesSyncService>();

        // Registrar controlador (ASP.NET Core lo hace automáticamente, pero lo dejamos por claridad)
        services.AddControllers()
            .AddApplicationPart(typeof(GafetesServiceCollectionExtensions).Assembly);

        return services;
    }

    /// <summary>
    /// Configura las políticas de autorización para Casco.
    /// </summary>
    public static IServiceCollection AddCascoApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy("CascoApiAccess", policy =>
            {
                // Adaptar según tu implementación de autenticación
                // Ejemplos:
                policy.RequireAuthenticatedUser();
                // policy.RequireClaim("scope", "casco:sync");
                // policy.RequireRole("CascoAdmin");
            });

        return services;
    }
}

/// <summary>
/// Ejemplo de configuración en Program.cs
/// </summary>
/*
// En Program.cs:

var builder = WebApplicationBuilder.CreateBuilder(args);

// Agregar servicios de gafetes
builder.Services.AddGafetesServices(builder.Configuration.GetConnectionString("DefaultConnection")!);
builder.Services.AddCascoApiAuthorization();

// Agregar autenticación (adaptar según tu implementación)
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<BearerAuthenticationSchemeOptions, BearerAuthenticationHandler>("Bearer", o => { });

var app = builder.Build();

// Middleware
app.UseAuthentication();
app.UseAuthorization();

// Mapear controladores
app.MapControllers();

app.Run();
*/
