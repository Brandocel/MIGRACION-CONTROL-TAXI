using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ControlTaxiDesktop.Services;
using ControlTaxiDesktop.Tools.Commands;
using ControlTaxiDesktop.Tools.Help;
using ControlTaxiDesktop.Tools.Infrastructure;

return await DesktopImportProgram.RunAsync(args);

internal static class DesktopImportProgram
{
    private const string Usage = UsageText.Text;

    public static async Task<int> RunAsync(string[] args)
    {
        CascoCredentialStore.TryApplyToEnvironment(out _);

        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            var command = args[0].Trim().ToLowerInvariant();
            IReadOnlyDictionary<string, string> options = command.Equals("casco-sync", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-ui-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-grid-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-registro-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-relations-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-payment-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-report-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-report-center-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badges-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-enter-smoke-test", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-return-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-return-one", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-hostinger-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-hostinger-push-one", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-sync-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-sync-one", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-badge-sync-watch", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-relation-calculation-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-commission-preview", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-relations-commission-preview", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-pos-sale-link-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-financial-source-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-relation-save-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-relation-save-one", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-sales-source-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-ticket-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-financial-correction-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-financial-correction-one", StringComparison.OrdinalIgnoreCase)
                || command.Equals("branch-modules-diagnostic", StringComparison.OrdinalIgnoreCase)
                || command.Equals("operations-window-smoke-test", StringComparison.OrdinalIgnoreCase)
                || command.Equals("pos-report-center-smoke-test", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-auto-sync-status", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-auto-sync-once", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-auto-sync-watch", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-auto-sync-duplicate-test", StringComparison.OrdinalIgnoreCase)
                || command.Equals("casco-auto-sync-resilience-test", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : ProgramHelpers.ApplyConfigDefaults(command, ProgramHelpers.ReadOptions(args[1..]));

            return command switch
            {
                "import-sqlserver" => await ImportCommands.ImportSqlServerAsync(options),
                "import-bak" => await ImportCommands.ImportBakAsync(options),
                "import-csv" => await ImportCommands.ImportCsvAsync(options),
                "import-xlsx" => await ImportCommands.ImportXlsxAsync(options),
                "import-sqlite" => await ImportCommands.ImportSqliteAsync(options),
                "import-json" => await ImportCommands.ImportJsonAsync(options),
                "verify" => await ImportCommands.VerifyAsync(options),
                "validate-parity" => await PipelineCommands.ValidateParityAsync(options),
                "normalize-pos" => await PipelineCommands.NormalizePosAsync(options),
                "run-pipeline" => await PipelineCommands.RunPipelineAsync(options),
                "casco-sync" => await ImportCommands.RunCascoSyncAsync(args[1..]),
                "casco-ui-diagnostic" => await ImportCommands.RunCascoUiDiagnosticAsync(args[1..]),
                "casco-grid-diagnostic" => await ImportCommands.RunCascoGridDiagnosticAsync(args[1..]),
                "casco-registro-diagnostic" => await ImportCommands.RunCascoRegistroDiagnosticAsync(args[1..]),
                "casco-relations-diagnostic" => await ImportCommands.RunCascoRelationsDiagnosticAsync(args[1..]),
                "casco-payment-diagnostic" => await ImportCommands.RunCascoPaymentDiagnosticAsync(args[1..]),
                "casco-report-diagnostic" => await ImportCommands.RunCascoReportDiagnosticAsync(args[1..]),
                "casco-report-center-diagnostic" => await ImportCommands.RunCascoReportCenterDiagnosticAsync(args[1..]),
                "casco-payout-diagnostic" => await ImportCommands.RunCascoPayoutDiagnosticAsync(args[1..]),
                "casco-payout-consistency-diagnostic" => await ImportCommands.RunCascoPayoutConsistencyDiagnosticAsync(args[1..]),
                "casco-payout-one" => await ImportCommands.RunCascoPayoutOneAsync(args[1..]),
                "casco-payout-validation" => await ImportCommands.RunCascoPayoutValidationAsync(args[1..]),
                "casco-sales-source-diagnostic" => await ImportCommands.RunCascoSaleSourceDiagnosticAsync(args[1..]),
                "casco-sale-window-diagnostic" => await ImportCommands.RunCascoSaleWindowDiagnosticAsync(args[1..]),
                "casco-ticket-diagnostic" => await ImportCommands.RunCascoTicketDiagnosticAsync(args[1..]),
                "casco-financial-correction-diagnostic" => await ImportCommands.RunCascoFinancialCorrectionDiagnosticAsync(args[1..]),
                "casco-financial-correction-one" => await ImportCommands.RunCascoFinancialCorrectionOneAsync(args[1..]),
                "casco-badges-diagnostic" => await ImportCommands.RunCascoBadgesDiagnosticAsync(args[1..]),
                "casco-badge-enter-smoke-test" => await ImportCommands.RunCascoBadgeEnterSmokeTestAsync(args[1..]),
                "casco-badge-return-diagnostic" => await ImportCommands.RunCascoBadgeReturnDiagnosticAsync(args[1..]),
                "casco-badge-return-one" => await ImportCommands.RunCascoBadgeReturnOneAsync(args[1..]),
                "casco-badge-hostinger-diagnostic" => await ImportCommands.RunCascoBadgeHostingerDiagnosticAsync(args[1..]),
                "casco-badge-hostinger-push-one" => await ImportCommands.RunCascoBadgeHostingerPushOneAsync(args[1..]),
                "casco-badge-sync-diagnostic" => await ImportCommands.RunCascoBadgeSyncDiagnosticAsync(args[1..]),
                "casco-badge-sync-one" => await ImportCommands.RunCascoBadgeSyncOneAsync(args[1..]),
                "casco-badge-sync-watch" => await ImportCommands.RunCascoBadgeSyncWatchAsync(args[1..]),
                "casco-relation-calculation-diagnostic" => await ImportCommands.RunCascoRelationCalculationDiagnosticAsync(args[1..]),
                "casco-commission-preview" => await ImportCommands.RunCascoCommissionPreviewAsync(args[1..]),
                "casco-commission-save-test" => await ImportCommands.RunCascoCommissionSaveTestAsync(args[1..]),
                "casco-relations-commission-preview" => await ImportCommands.RunCascoRelationsCommissionPreviewAsync(args[1..]),
                "casco-pos-sale-link-diagnostic" => await ImportCommands.RunCascoPosSaleLinkDiagnosticAsync(args[1..]),
                "casco-financial-source-diagnostic" => await ImportCommands.RunCascoFinancialSourceDiagnosticAsync(args[1..]),
                "casco-relation-save-diagnostic" => await ImportCommands.RunCascoRelationSaveDiagnosticAsync(args[1..]),
                "casco-save-log-diagnostic" => await ImportCommands.RunCascoSaveLogDiagnosticAsync(args[1..]),
                "casco-relation-save-one" => await ImportCommands.RunCascoRelationSaveOneAsync(args[1..]),
                "branch-modules-diagnostic" => await ImportCommands.RunBranchModulesDiagnosticAsync(args[1..]),
                "operations-window-smoke-test" => await ImportCommands.RunOperationsWindowCloseSmokeTestAsync(args[1..]),
                "pos-report-center-smoke-test" => await ImportCommands.RunPosReportCenterSmokeTestAsync(args[1..]),
                "casco-auto-sync-status" => await ImportCommands.RunCascoAutoSyncStatusAsync(args[1..]),
                "casco-auto-sync-once" => await ImportCommands.RunCascoAutoSyncOnceAsync(args[1..]),
                "casco-auto-sync-watch" => await ImportCommands.RunCascoAutoSyncWatchAsync(args[1..]),
                "casco-auto-sync-duplicate-test" => await ImportCommands.RunCascoAutoSyncDuplicateTestAsync(args[1..]),
                "casco-auto-sync-resilience-test" => await ImportCommands.RunCascoAutoSyncResilienceTestAsync(args[1..]),
                "plaza28-cuadre-excel" => await ImportCommands.RunPlaza28CuadreExcelAsync(args[1..]),
                "plaza28-sync-camiones" => await ImportCommands.RunPlaza28SyncCamionesAsync(args[1..]),
                _ => ProgramHelpers.Fail($"Comando no reconocido: {command}")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }
    }
}
