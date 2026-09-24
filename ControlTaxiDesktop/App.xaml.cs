using System.IO;
using System.Windows;
using ControlTaxiDesktop.Models;
using ControlTaxiDesktop.Services;

namespace ControlTaxiDesktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs eventArgs)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                var database = new LocalDatabase();
                new LocalErrorLogger(database).LogAsync("Sistema", "Aplicacion", "Excepcion no controlada", args.Exception).GetAwaiter().GetResult();
            }
            catch
            {
            }

            MessageBox.Show(
                "Ocurrio un error inesperado y la operacion no se pudo completar." + Environment.NewLine + args.Exception.Message,
                "Control Taxi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };

        if (eventArgs.Args.Contains("--diag-sql", StringComparer.OrdinalIgnoreCase))
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "diag-sql.txt");
            var lines = new List<string>();
            try
            {
                var source = LocalSqlServerSource.TryLoad();
                if (source is null)
                {
                    lines.Add("TryLoad() devolvio null: no encontro appsettings.json ni coincidio con Plaza28Production.");
                }
                else
                {
                    lines.Add($"PosDatabase={source.PosDatabase} AppDatabase={source.AppDatabase} CompuadmoDatabase={source.CompuadmoDatabase} JoyeriaDatabase={source.JoyeriaDatabase}");
                    try
                    {
                        await using var connection = await source.OpenPosAsync();
                        lines.Add($"OpenPosAsync OK. DataSource={connection.DataSource} Database={connection.Database} ServerVersion={connection.ServerVersion}");
                        await using var command = connection.CreateCommand();
                        command.CommandText = "SELECT COUNT(*) FROM dbo.RelacionTicketTaxista WHERE FolioOperacion = '4242' OR FolioApp = '4242';";
                        var count = await command.ExecuteScalarAsync();
                        lines.Add($"RelacionTicketTaxista folio 4242 count={count}");
                    }
                    catch (Exception ex)
                    {
                        lines.Add($"OpenPosAsync FALLO: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                lines.Add($"Excepcion general: {ex.GetType().Name}: {ex.Message}");
            }
            await File.WriteAllLinesAsync(logPath, lines);
            Shutdown(0);
            return;
        }

        // Diagnostic: print branch config and effective DB targets without opening connections.
        if (eventArgs.Args.Contains("--diag-branches", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var branches = new BranchConfigurationService();
                // Force loading of branch configs so LoadedConfigPath is populated
                branches.GetAllBranches();
                var loaded = branches.GetLoadedConfigPath();
                var isLocal = branches.IsLocalOverrideActive();
                var database = new LocalDatabase(true);
                // Do not initialize or open any DB files
                var repo = new LocalUserRepository(database);
                var cv = repo.GetBranchConnectionInfo("CV");
                var p28 = repo.GetBranchConnectionInfo("P28");

                // Gather where we will look for branches.local.json (same order as BranchConfigurationService)
                var appBase = AppContext.BaseDirectory;
                string? workspaceRootLocal = null;
                try
                {
                    var current = new DirectoryInfo(AppContext.BaseDirectory);
                    while (current is not null)
                    {
                        if (File.Exists(Path.Combine(current.FullName, "CONTROL TAXI.sln")))
                        {
                            workspaceRootLocal = current.FullName;
                            break;
                        }
                        current = current.Parent;
                    }
                }
                catch
                {
                    workspaceRootLocal = null;
                }

                var localCandidates = new List<string>
                {
                    Path.Combine(appBase, "branches.local.json"),
                    Path.Combine(Environment.CurrentDirectory, "branches.local.json")
                };
                if (!string.IsNullOrWhiteSpace(workspaceRootLocal))
                    localCandidates.Add(Path.Combine(workspaceRootLocal, "branches.local.json"));

                var firstLocal = localCandidates.FirstOrDefault(File.Exists);
                var localExists = firstLocal is null ? "false" : "true";
                string localReadError = string.Empty;
                if (firstLocal is not null)
                {
                    try
                    {
                        var text = File.ReadAllText(firstLocal);
                        using var doc = System.Text.Json.JsonDocument.Parse(text);
                        // no-op: parsing succeeded
                    }
                    catch (Exception ex)
                    {
                        localReadError = ex.Message;
                    }
                }

                var lines = new List<string>();
                // Prepend AppContext.BaseDirectory and local candidate info
                lines.Add("AppContext.BaseDirectory: " + appBase);
                lines.Add("branches.local.json candidate: " + (firstLocal ?? "(none)"));
                lines.Add("branches.local.json exists: " + localExists);
                lines.Add(string.IsNullOrWhiteSpace(localReadError) ? "branches.local.json read error: (none)" : "branches.local.json read error: " + localReadError);

                // Then keep the original seven lines (loaded, isLocal, cv server/db, p28 server/db)
                lines.Add(loaded ?? "(none)");
                lines.Add(isLocal ? "true" : "false");
                lines.Add(cv.DataSource ?? string.Empty);
                lines.Add(cv.InitialCatalog ?? string.Empty);
                lines.Add(p28.DataSource ?? string.Empty);
                lines.Add(p28.InitialCatalog ?? string.Empty);
                var message = string.Join('\n', lines);

                // Print to console first so automated runs capture the exact text,
                // then show a MessageBox with the same text for interactive inspection.
                Console.WriteLine(message);
                MessageBox.Show(message, "Diagnóstico sucursales", MessageBoxButton.OK, MessageBoxImage.Information);

                // Detect presence of CV credentials (without revealing them).
                // Use TryLoad to detect an existing casco.credentials.dat without creating folders.
                var cvHasCred = false;
                try
                {
                    if (CascoCredentialStore.TryLoad(out var cred, out var _ ) && cred is not null
                        && !string.IsNullOrWhiteSpace(cred.SqlUser)
                        && !string.IsNullOrWhiteSpace(cred.SqlPassword))
                    {
                        cvHasCred = true;
                    }
                    else
                    {
                        // Fallback: require both env vars present
                        var envUser = Environment.GetEnvironmentVariable("CASCO_SQL_USER");
                        var envPass = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD");
                        if (!string.IsNullOrWhiteSpace(envUser) && !string.IsNullOrWhiteSpace(envPass))
                            cvHasCred = true;
                    }
                }
                catch
                {
                    cvHasCred = false;
                }

                Console.WriteLine(cvHasCred ? "yes" : "no");
                MessageBox.Show(message + "\n" + (cvHasCred ? "yes" : "no"), "Diagnóstico sucursales", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Environment.Exit(2);
            }

            Shutdown(0);
            return;
        }

        // Limpia transportes duplicados del catalogo de comisiones y deja el resultado en
        // Logs\limpiar-catalogo.txt. Se agrego como flag y no como script de base de datos
        // porque las maquinas donde se instala no tienen sqlite3, y esto hay que correrlo una
        // vez por equipo.
        if (eventArgs.Args.Contains("--limpiar-catalogo", StringComparer.OrdinalIgnoreCase))
        {
            var reporte = new System.Text.StringBuilder();
            try
            {
                var database = new LocalDatabase();
                await database.InitializeAsync();
                var settings = new CommissionSettingsRepository(database);
                // Crea el esquema y siembra antes de medir: GetDuplicateTransportNamesAsync
                // solo lee, asi que sin esto falla en una base recien creada.
                await settings.InitializeAsync();

                var antes = (await settings.GetDuplicateTransportNamesAsync()).ToArray();
                var borradas = await settings.RemoveDuplicateTransportsAsync();
                // Se relee DESPUES de la limpieza y sin volver a sembrar, para que el reporte
                // muestre el estado real y no el de un nuevo sembrado.
                var despues = (await settings.GetDuplicateTransportNamesAsync()).ToArray();

                reporte.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Limpieza de catalogo");
                reporte.AppendLine($"Filas eliminadas: {borradas}");
                reporte.AppendLine();
                reporte.AppendLine($"Duplicados ANTES ({antes.Length}):");
                foreach (var d in antes) reporte.AppendLine("  - " + d);
                reporte.AppendLine();
                reporte.AppendLine($"Duplicados DESPUES ({despues.Length}):");
                if (despues.Length == 0) reporte.AppendLine("  (ninguno)");
                foreach (var d in despues) reporte.AppendLine("  - " + d);
            }
            catch (Exception ex)
            {
                reporte.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {ex.Message}");
            }

            var carpeta = System.IO.Path.Combine(AppContext.BaseDirectory, "Logs");
            System.IO.Directory.CreateDirectory(carpeta);
            var ruta = System.IO.Path.Combine(carpeta, "limpiar-catalogo.txt");
            await System.IO.File.WriteAllTextAsync(ruta, reporte.ToString());
            Shutdown(0);
            return;
        }

        // Publica el cuadre de Plaza 28 en la API de Hostinger y se cierra. Lo llama el
        // sincronizador cada ciclo, para que Hoka Solutions tenga el cuadre al dia sin depender
        // de que alguien exporte el Excel: si nadie lo exporta, Hoka mostraria el dato viejo y
        // nadie se daria cuenta.
        //
        // Va como bandera del ejecutable y no como programa aparte porque ControlTaxiDesktop.exe
        // ya esta instalado en todas las maquinas de la tienda; un binario nuevo obligaria a
        // rehacer el paquete y el instalador.
        if (eventArgs.Args.Contains("--push-cuadre", StringComparer.OrdinalIgnoreCase))
        {
            var bitacora = new System.Text.StringBuilder();
            var exito = false;

            try
            {
                // Sin la credencial cifrada no hay SQL Server, y sin SQL Server no hay cuadre.
                if (!Plaza28CredentialStore.TryApplyToEnvironment(out var errorCredencial))
                    throw new InvalidOperationException("No se pudo leer la credencial cifrada de Plaza 28. " + errorCredencial);

                var desde = ParseFechaArgumento(eventArgs.Args, "--desde=") ?? DateTime.Today;
                var hasta = ParseFechaArgumento(eventArgs.Args, "--hasta=") ?? desde;

                var database = new LocalDatabase();
                await database.InitializeAsync();

                var resultado = await new CuadreSnapshotService(database).BuildAndPushPlaza28Async(desde, hasta);
                exito = resultado.Ok;

                bitacora.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {desde:yyyy-MM-dd} a {hasta:yyyy-MM-dd} -> "
                    + (resultado.Ok ? "OK" : "FALLO") + ": " + resultado.Mensaje
                    + $" (resumen={resultado.FilasResumen}, dejadas={resultado.FilasDejadas})");
            }
            catch (Exception ex)
            {
                bitacora.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: {ex.Message}");
            }

            try
            {
                var carpetaBitacora = Path.Combine(AppContext.BaseDirectory, "Logs");
                Directory.CreateDirectory(carpetaBitacora);
                await File.AppendAllTextAsync(Path.Combine(carpetaBitacora, "push-cuadre.txt"), bitacora.ToString());
            }
            catch
            {
                // Si no se puede escribir la bitacora igual importa el codigo de salida.
            }

            Console.Write(bitacora.ToString());
            Environment.ExitCode = exito ? 0 : 1;
            Shutdown(Environment.ExitCode);
            return;
        }

        if (eventArgs.Args.Contains("--init-local-db", StringComparer.OrdinalIgnoreCase))
        {
            var database = new LocalDatabase();
            await database.InitializeAsync();
            Shutdown(0);
            return;
        }

        if (eventArgs.Args.Contains("--seed-plaza28-guadalupe", StringComparer.OrdinalIgnoreCase))
        {
            var database = new LocalDatabase();
            var users = new LocalUserRepository(database);
            await database.InitializeAsync();
            await users.SaveUserAsync(
                "Guadalupe",
                "280625",
                "Administrador",
                "Activo",
                "P28",
                LocalUserRepository.AllModules);
            Shutdown(0);
            return;
        }

        if (eventArgs.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            var database = new LocalDatabase(forceTestDatabase: true);
            var users = new LocalUserRepository(database);
            await database.InitializeAsync();
            await users.EnsureTestUserAsync();
            var authResult = await users.AuthenticateAsync("admin", "admin");
            if (!authResult.Success || authResult.Session is not { IsTestDatabase: true })
            {
                Environment.ExitCode = 1;
                Shutdown(Environment.ExitCode);
                return;
            }

            var operations = new LocalOperationsRepository(database);
            var suffix = DateTime.UtcNow.Ticks.ToString();
            var rateId = await operations.SaveRateAsync(new LocalRate(0, "PRUEBA", "Tarifa " + suffix, 125m, 100m, 150m, true));
            var hotelId = await operations.SaveHotelAsync("Hotel " + suffix);
            var driverId = await operations.SaveDriverAsync(new LocalDriver(0, "D" + suffix, "Taxista prueba", "", "", "", "", "PRUEBA", "Activo"));
            await operations.SaveBadgeAsync("G" + suffix);
            await operations.SaveRecordAsync(new LocalRecord(0, "T" + suffix, DateTime.Now, driverId, hotelId, rateId, "G" + suffix, 1, "Origen", "Destino", 125m, "Efectivo", "", "admin"));
            var report = await operations.GetReportAsync(DateTime.Today, DateTime.Today);
            var pos = new LocalPosRepository(database);
            var productId = await pos.SaveProductAsync(new LocalProduct(0, "P" + suffix, "Producto prueba", 100m, 0.16m, 10, true), "admin");
            await pos.CreateSaleAsync("V" + suffix, "Cliente prueba", [(productId, 2)], "admin");
            await pos.RegisterPaymentAsync("V" + suffix, 232m, "Efectivo", "Prueba local", "admin");
            var recalculated = await pos.RecalculateCommissionsAsync("admin");
            await pos.PayCommissionAsync("C-V" + suffix, 23.20m, "admin");
            await pos.CalculateCutAsync(DateTime.Today, 232m, "admin");
            var audit = await pos.GetAuditAsync(DateTime.Today, DateTime.Today);
            await users.SaveUserAsync("supervisor" + suffix, "admin", "Supervisor", "Activo", "P28", new[] { "RegistroDiario", "Comisiones", "Reportes" });
            var userRows = await users.GetUsersAsync();
            var specialRows = await pos.GetSpecialReportAsync(DateTime.Today, DateTime.Today);
            var taxiRows = await pos.GetTaxiReportAsync(DateTime.Today, DateTime.Today);
            var portal = new LocalPortalRepository(database);
            var portalMetrics = await portal.GetDashboardAsync(LocalPortalDatabase.CompuadmoPlaza, DateTime.Today);
            var portalFolio = await portal.CreateOperationAsync(new LocalPortalCreateOperationInput(LocalPortalDatabase.CompuadmoPlaza, "VEND", "Hotel prueba", "Venta", DateTime.Now, "admin", "", "", 1, "Prueba portal offline", 100m, 16m, 116m, 0m, 0m, 1m), "admin");
            Environment.ExitCode = report.Records > 0 && report.Total >= 125m && recalculated > 0 && audit.Count > 0 && userRows.Count > 0 && specialRows.Count >= 0 && taxiRows.Count >= 0 && portalMetrics.OperationsCount >= 0 && !string.IsNullOrWhiteSpace(portalFolio) ? 0 : 1;
            TryDeleteSelfTestDatabase(database.TestPath);
            Shutdown(Environment.ExitCode);
            return;
        }

        Plaza28CredentialStore.TryApplyToEnvironment(out _);

        var shouldStartCascoServices = ShouldStartCascoServices();
        if (shouldStartCascoServices)
        {
            var branchService = new BranchConfigurationService();
            var cascoBranch = branchService.GetBranch("CV");
            // Try to apply any saved credential. If missing or invalid, show the setup window so the
            // user can Test and Save a new credential. Do NOT abort startup immediately: allow the
            // user to provide credentials interactively (important when running under F5).
            if (!CascoCredentialStore.TryApplyToEnvironment(out var credentialError))
            {
                var setupWindow = new CascoConnectionSetupWindow(cascoBranch);
                var configured = setupWindow.ShowDialog();
                if (configured == true)
                {
                    // If user saved new credential, ensure it's applied. If Apply still fails, show a
                    // warning but continue startup so the app can show the login and allow retry.
                    CascoCredentialStore.TryApplyToEnvironment(out credentialError);
                }
                else
                {
                    // User cancelled setup: warn but continue startup so they can still use the app
                    // (some modules may not require Casco). Do not shutdown here to allow interactive
                    // development with F5.
                }
            }
        }

        SyncTaxiLauncher.TryStart();
        if (shouldStartCascoServices)
        {
            _ = CascoBackgroundSyncService.Instance.StartAsync();
        }

        base.OnStartup(eventArgs);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await CascoBackgroundSyncService.Instance.StopAsync();
        base.OnExit(e);
    }

    private static bool ShouldStartCascoServices()
    {
        var explicitValue = Environment.GetEnvironmentVariable("CONTROL_TAXI_START_CASCO_SERVICES");
        if (bool.TryParse(explicitValue, out var explicitFlag))
        {
            return explicitFlag;
        }

        foreach (var path in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalized = path.Replace('\\', '/').ToUpperInvariant();
            if (normalized.Contains("PLAZA28") ||
                normalized.Contains("PLAZA 28") ||
                normalized.Contains("DESKTOP TAXIS") ||
                normalized.Contains("RELEASE-CONTROLTAXI-PLAZA28-PRODUCCION") ||
                normalized.Contains("SYNCTAXI_PLAZA28"))
            {
                return false;
            }

            if (normalized.Contains("RELEASE-CONTROLTAXI-CASCO-PRODUCCION") ||
                normalized.Contains("CASCO NUEVO") ||
                normalized.Contains("CASCO VIEJO"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lee una fecha de los argumentos con el formato <c>--desde=2026-09-09</c>. Devuelve null si
    /// el argumento no viene o no se entiende, para que quien llama use su valor por omision.
    /// </summary>
    private static DateTime? ParseFechaArgumento(string[] args, string prefijo)
    {
        var argumento = args.FirstOrDefault(x => x.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase));
        if (argumento is null)
            return null;

        var valor = argumento[prefijo.Length..].Trim().Trim('"');
        return DateTime.TryParse(valor, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fecha)
            ? fecha.Date
            : null;
    }

    private static void TryDeleteSelfTestDatabase(string path)
    {
        try
        {
            foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
