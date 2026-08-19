using ControlTaxiWeb.Data;
using ControlTaxiWeb.Interfaces;
using ControlTaxiWeb.Services;
using ControlTaxiWeb.Services.PosModules;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, ".keys")))
    .SetApplicationName("ControlTaxiWeb");
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddDbContextFactory<CompuadmoPlazaContext>(options =>
    options.UseSqlServer(BuildDatabaseConnectionString(builder.Configuration, "DatabaseNames:Compuadmo")));

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IDbContextFactory<CompuadmoPlazaContext>>().CreateDbContext());

builder.Services.AddDbContextFactory<JoyeriaPlazaContext>(options =>
    options.UseSqlServer(BuildDatabaseConnectionString(builder.Configuration, "DatabaseNames:Joyeria")));

builder.Services.AddDbContext<PosDbContext>(options =>
    options.UseSqlServer(BuildDatabaseConnectionString(builder.Configuration, "DatabaseNames:Pos")));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(BuildDatabaseConnectionString(builder.Configuration, "DatabaseNames:App")));

builder.Services.AddScoped<PortalService>();
builder.Services.AddHttpClient<IAppTaxiApiClient, AppTaxiApiClient>((sp, client) =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["TaxiApi:BaseUrl"] ?? "https://lightyellow-porpoise-679527.hostingersite.com";
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(4);
});
builder.Services.AddScoped<IPosSqlMirrorService, PosSqlMirrorService>();
builder.Services.AddScoped<IPosAuthService, PosAuthService>();
builder.Services.AddScoped<IPosDashboardService, PosDashboardService>();
builder.Services.AddScoped<IPosRegistroService, PosRegistroService>();
builder.Services.AddScoped<IPosVentasService, PosVentasService>();
builder.Services.AddScoped<IPosPagosService, PosPagosService>();
builder.Services.AddScoped<IPosGastosService, PosGastosService>();
builder.Services.AddScoped<IPosCortesService, PosCortesService>();
builder.Services.AddScoped<IPosComisionesService, PosComisionesService>();
builder.Services.AddScoped<IPosCatalogosService, PosCatalogosService>();
builder.Services.AddScoped<IPosGafetesService, PosGafetesService>();
builder.Services.AddScoped<IPosUsuariosService, PosUsuariosService>();
builder.Services.AddScoped<IPosRelacionesService, PosRelacionesService>();

var app = builder.Build();

try
{
    await EnsurePosTritonSchemaAsync(app);
}
catch (SqlException ex)
{
    app.Logger.LogWarning(ex, "No se pudo preparar el esquema inicial porque SQL Server no esta disponible. La aplicacion seguira arrancando.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errApp =>
    {
        errApp.Run(async context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            var isPosRequest = path.StartsWith("/Pos", StringComparison.OrdinalIgnoreCase)
                || path == "/" || path == string.Empty;

            if (isPosRequest)
            {
                var exFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
                var ex = exFeature?.Error;
                var tipo = ex?.GetType().Name ?? "Error desconocido";
                var msg = ex?.Message ?? "Sin detalle";
                var inner = ex?.InnerException?.Message ?? "-";
                var accion = exFeature?.Path ?? path;
                var timestamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                var detail = $"{tipo}||{msg}||{accion}||{inner}||{timestamp}";
                var encoded = Uri.EscapeDataString(detail);
                context.Response.Cookies.Append("PosLastError", encoded, new Microsoft.AspNetCore.Http.CookieOptions
                {
                    HttpOnly = true,
                    SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromMinutes(2)
                });
                context.Response.Redirect("/Pos/PosError");
            }
            else
            {
                context.Response.Redirect("/Portal/Error");
            }
            await Task.CompletedTask;
        });
    });
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "pos",
    pattern: "Pos/{action=Index}/{id?}",
    defaults: new { controller = "Pos" })
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Pos}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

static string BuildDatabaseConnectionString(IConfiguration configuration, string databaseKey)
{
    var defaultConnection = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(defaultConnection))
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection no esta configurado.");

    var databaseName = configuration[databaseKey];
    if (string.IsNullOrWhiteSpace(databaseName))
        throw new InvalidOperationException($"{databaseKey} no esta configurado.");

    var builder = new SqlConnectionStringBuilder(defaultConnection)
    {
        InitialCatalog = databaseName
    };
    return builder.ConnectionString;
}

static async Task EnsurePosTritonSchemaAsync(WebApplication app)
{
    var scriptPath = Path.Combine(app.Environment.ContentRootPath, "Data", "PosTritonSchema.sql");
    if (!File.Exists(scriptPath))
        return;

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var connection = dbContext.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
        await connection.OpenAsync();

    var script = await File.ReadAllTextAsync(scriptPath);
    var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
        .Select(x => x.Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x));

    foreach (var batch in batches)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = batch;
        await command.ExecuteNonQueryAsync();
    }
}
