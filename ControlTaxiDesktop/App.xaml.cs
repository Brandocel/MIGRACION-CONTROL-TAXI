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
            if (!CascoCredentialStore.TryApplyToEnvironment(out var credentialError))
            {
                var setupWindow = new CascoConnectionSetupWindow(cascoBranch);
                var configured = setupWindow.ShowDialog();
                if (configured != true || !CascoCredentialStore.TryApplyToEnvironment(out credentialError))
                {
                    MessageBox.Show(
                        "No se completo la configuracion de Casco Viejo." + Environment.NewLine + credentialError,
                        "Control Taxi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    Shutdown(0);
                    return;
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
