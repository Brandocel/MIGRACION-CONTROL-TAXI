namespace ControlTaxiDesktop.Tools.Help;

internal static class UsageText
{
    public const string Text = """
ControlTaxiDesktop.Tools - importador local, sin red

Uso:
  dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver \
    --connection "<cadena a una base SQL Server restaurada localmente>" \
    --source-name mkt --output .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver \
    --server REYNA --user sa --password ***** \
    --databases compuamdoPlaza,joyeriaPlaza,mkt2,ControlTaxis \
    --db .\\DatosLocal\\ControlTaxi.db --report .\\REPORTE_IMPORTACION_REAL.md

  dotnet run --project ControlTaxiDesktop.Tools -- import-sqlserver

  dotnet run --project ControlTaxiDesktop.Tools -- import-bak \
    --server "(localdb)\\MSSQLLocalDB" --bak .\\Respaldo_Origen\\mkt.bak \
    --database mkt_restore --source-name mkt --output .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- import-csv \
    --input .\\Respaldo_Origen\\CSV --source-name csv --output .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- import-xlsx \
    --input .\\Respaldo_Origen\\Excel --source-name excel --output .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- import-sqlite \
    --input .\\Respaldo_Origen\\ControlTaxi.sqlite --source-name sqlite --output .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- import-json \
    --input .\\Respaldo_Origen\\Hostinger --output .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- verify \
    --output .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- validate-parity \
    --db .\\DatosLocal\\ControlTaxi.db --report .\\VALIDACION_PARIDAD_DATOS_REALES.md

  dotnet run --project ControlTaxiDesktop.Tools -- normalize-pos \
    --db .\\DatosLocal\\ControlTaxi.db

  dotnet run --project ControlTaxiDesktop.Tools -- run-pipeline \
    --db .\\DatosLocal\\ControlTaxi.db --report .\\REPORTE_IMPORTACION_REAL.md

  dotnet run --project ControlTaxiDesktop.Tools -- casco-sync --dry-run

  dotnet run --project ControlTaxiDesktop.Tools -- casco-sync --apply-one

  dotnet run --project ControlTaxiDesktop.Tools -- casco-sync --apply-new

  dotnet run --project ControlTaxiDesktop.Tools -- casco-sync --apply-update-one

  dotnet run --project ControlTaxiDesktop.Tools -- casco-sync --apply-update-one --simulate-update

  dotnet run --project ControlTaxiDesktop.Tools -- casco-ui-diagnostic --user ReynaV

  dotnet run --project ControlTaxiDesktop.Tools -- casco-grid-diagnostic --user ReynaV

  dotnet run --project ControlTaxiDesktop.Tools -- casco-registro-diagnostic --user ReynaV --date 2026-07-10

  dotnet run --project ControlTaxiDesktop.Tools -- casco-relations-diagnostic --user ReynaV --date-from 2026-07-08 --date-to 2026-07-10

  dotnet run --project ControlTaxiDesktop.Tools -- casco-payment-diagnostic --user ReynaV --folio-original 0002

  dotnet run --project ControlTaxiDesktop.Tools -- casco-payment-diagnostic --user ReynaV --folio-original 0000 --simulate-zero

  dotnet run --project ControlTaxiDesktop.Tools -- casco-payment-diagnostic --user ReynaV --folio-original 0002 --apply-payment-one

  dotnet run --project ControlTaxiDesktop.Tools -- casco-payout-consistency-diagnostic --user ReynaV --folio-original 0003

  dotnet run --project ControlTaxiDesktop.Tools -- casco-report-diagnostic --user ReynaV --date-from 2026-07-08 --date-to 2026-07-10

  dotnet run --project ControlTaxiDesktop.Tools -- casco-report-center-diagnostic --user ReynaV --date-from 2026-07-08 --date-to 2026-07-10

  dotnet run --project ControlTaxiDesktop.Tools -- casco-badges-diagnostic --user ReynaV --date-from 2026-07-01 --date-to 2026-07-31

  dotnet run --project ControlTaxiDesktop.Tools -- casco-badge-enter-smoke-test --user ReynaV --date-from 2026-07-01 --date-to 2026-07-18

  dotnet run --project ControlTaxiDesktop.Tools -- casco-badge-hostinger-diagnostic --badge 2121

  dotnet run --project ControlTaxiDesktop.Tools -- casco-badge-hostinger-push-one --badge 2121

  dotnet run --project ControlTaxiDesktop.Tools -- casco-badge-sync-diagnostic --badge 2121

  dotnet run --project ControlTaxiDesktop.Tools -- casco-badge-sync-one --badge 2121

  dotnet run --project ControlTaxiDesktop.Tools -- casco-badge-sync-watch

  dotnet run --project ControlTaxiDesktop.Tools -- casco-relation-calculation-diagnostic --user ReynaV --folio-original 0001

  dotnet run --project ControlTaxiDesktop.Tools -- casco-financial-source-diagnostic --user ReynaV --folio-original 0001

  dotnet run --project ControlTaxiDesktop.Tools -- branch-modules-diagnostic --user ReynaV

  dotnet run --project ControlTaxiDesktop.Tools -- operations-window-smoke-test --user ReynaV

  dotnet run --project ControlTaxiDesktop.Tools -- pos-report-center-smoke-test --user ReynaV --date-from 2026-07-03 --date-to 2026-07-17

  dotnet run --project ControlTaxiDesktop.Tools -- casco-auto-sync-status

  dotnet run --project ControlTaxiDesktop.Tools -- casco-auto-sync-once

  dotnet run --project ControlTaxiDesktop.Tools -- casco-auto-sync-duplicate-test

  dotnet run --project ControlTaxiDesktop.Tools -- casco-auto-sync-resilience-test

Los comandos de importación leen el origen y generan una base SQLite local.
El comando casco-sync consulta la API configurada.
Las escrituras requieren un modo explícito y confirmación manual.
""";
}
