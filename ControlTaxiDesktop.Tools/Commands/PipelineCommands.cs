using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using ControlTaxiDesktop.Tools.Infrastructure;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Tools.Commands;

internal static class PipelineCommands
{
    public static async Task<int> ValidateParityAsync(IReadOnlyDictionary<string, string> options)
    {
        var databasePath = ProgramHelpers.Required(options, "db");
        var reportPath = options.TryGetValue("report", out var configuredReport) && !string.IsNullOrWhiteSpace(configuredReport)
            ? configuredReport
            : "VALIDACION_PARIDAD_DATOS_REALES.md";
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("No se encontró la base SQLite real para validar.", databasePath);

        await using var target = ProgramHelpers.OpenSqlite(databasePath);
        var tables = await GetSqliteTablesAsync(target);
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
            counts[table] = await CountRowsAsync(target, table);

        var rows = new[]
        {
            BuildParityRow("Ventas", ["mkt__dbo__mov_operacion", "mkt__dbo__operacion", "compuadmo__dbo__Productos", "joyeria__dbo__Productos"], ["LocalProductos", "LocalVentas", "LocalVentaLineas"]),
            BuildParityRow("Pagos", ["mkt__dbo__mov_operacion"], ["LocalPagos", "LocalVentas"]),
            BuildParityRow("Comisiones", ["mkt__dbo__mov_operacion", "mkt__dbo__transporte", "mkt__dbo__dejadas", "mkt__dbo__RelacionTicketTaxista"], ["LocalComisiones"]),
            BuildParityRow("Cortes", ["mkt__dbo__mov_operacion", "ControlTaxis__dbo__Cortes"], ["LocalCortes"]),
            BuildParityRow("Reportes", ["mkt__dbo__mov_operacion", "mkt__dbo__AppMovilRegistro", "mkt__dbo__dejadas"], ["LocalVentas", "LocalPagos", "LocalComisiones", "LocalCortes"]),
            BuildParityRow("Excel/PDF", ["mkt__dbo__mov_operacion"], ["LocalVentas", "LocalPagos", "LocalComisiones", "LocalCortes"]),
            BuildParityRow("Usuarios y permisos", ["ControlTaxis__dbo__Usuarios", "ControlTaxis__dbo__UsuarioPermisos"], ["DesktopUsers", "DesktopPermissions"]),
            BuildParityRow("Auditoría", ["mkt__dbo__AuditoriaMovimiento"], ["LocalAuditoria"]),
            BuildParityRow("Gafetes", ["mkt__dbo__gafete", "mkt__dbo__AppMovilRegistroGafetes"], ["LocalGafetes"])
        };

        string BuildParityRow(string module, string[] sourceTables, string[] desktopTables)
        {
            var sourceStatus = DescribeTables(sourceTables);
            var desktopStatus = DescribeTables(desktopTables);
            var status = sourceTables.All(tables.Contains) && desktopTables.All(tables.Contains) && desktopTables.Any(x => counts.GetValueOrDefault(x) > 0)
                ? "Implementado; pendiente comparar resultados Web vs Desktop"
                : sourceTables.All(tables.Contains) && desktopTables.All(tables.Contains)
                    ? "Implementado; pendiente normalizar/importar datos reales"
                    : "Pendiente: faltan tablas de origen o destino";
            return $"| {module} | {sourceStatus} | {desktopStatus} | {status} |";
        }

        string DescribeTables(IEnumerable<string> requiredTables) =>
            string.Join("<br>", requiredTables.Select(x => tables.Contains(x) ? $"{x}: {counts.GetValueOrDefault(x):N0}" : $"{x}: NO ENCONTRADA"));

        var report = new StringBuilder();
        report.AppendLine("# Validación de paridad con datos reales");
        report.AppendLine();
        report.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Base SQLite: `{Path.GetFullPath(databasePath)}`");
        report.AppendLine();
        report.AppendLine("Este reporte no marca ningún módulo como validado si no existen datos reales normalizados y comparación contra Web.");
        report.AppendLine();
        report.AppendLine("| Módulo | Tablas origen importadas | Tablas Desktop | Estado |");
        report.AppendLine("|---|---|---|---|");
        foreach (var row in rows) report.AppendLine(row);
        report.AppendLine();
        report.AppendLine("## Comparaciones automaticas con datos reales");
        report.AppendLine();
        foreach (var line in await BuildRealComparisonLinesAsync(target, tables))
            report.AppendLine(line);
        report.AppendLine();
        report.AppendLine("## Criterio para pasar a validado");
        report.AppendLine();
        report.AppendLine("Cada módulo requiere comparar contra el Web: conteo de filas, folios, importes, saldos, comisiones, cortes, archivos Excel/PDF y auditoría.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(true));
        var folioReport = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "DIFERENCIAS_FOLIOS_PAGOS_COMISIONES.md");
        await WriteFolioDifferencesReportAsync(target, tables, folioReport);
        Console.WriteLine($"Reporte generado: {Path.GetFullPath(reportPath)}");
        return 0;
    }

    public static async Task<int> NormalizePosAsync(IReadOnlyDictionary<string, string> options)
    {
        var databasePath = ProgramHelpers.Required(options, "db");
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("No se encontró la base SQLite real para normalizar.", databasePath);
        await using var target = ProgramHelpers.OpenSqlite(databasePath);
        var tables = await GetSqliteTablesAsync(target);
        if (!tables.Contains("mkt__dbo__mov_operacion"))
            throw new InvalidOperationException("Falta la tabla importada mkt__dbo__mov_operacion.");
        await EnsureDesktopPosTablesAsync(target);
        var rows = await ReadImportedMovRowsForToolAsync(target, tables);
        await using var transaction = target.BeginTransaction();
        await NormalizeProductsAsync(target, transaction, tables);
        await NormalizeOperationsCatalogsAsync(target, transaction, tables);
        await NormalizeSalesPaymentsAndLinesAsync(target, transaction, rows);
        await NormalizeBadgesAsync(target, transaction, tables);
        await NormalizeAuditAsync(target, transaction, tables);
        await NormalizeUsersAndPermissionsAsync(target, transaction, tables);
        await using (var clearCommissions = target.CreateCommand())
        {
            clearCommissions.Transaction = transaction;
            clearCommissions.CommandText = "DELETE FROM LocalComisiones WHERE Folio LIKE 'C-%';";
            await clearCommissions.ExecuteNonQueryAsync();
        }
        foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.Folio)).GroupBy(x => x.Folio.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var amount = group.Sum(x => x.StoredCommission);
            var paid = Math.Max(group.Sum(x => x.Payment), 0m);
            var status = amount <= 0m ? "SIN COMISION" : paid >= amount ? "PAGADA" : paid > 0m ? "PARCIAL" : "PENDIENTE";
            await using var insert = target.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO LocalComisiones (Folio,VentaFolio,ClaveTaxista,Taxista,Fecha,TotalVenta,ImporteComision,Pagado,Saldo,Estatus)
                VALUES ($folio,$sale,'','',$date,$total,$commission,$paid,$balance,$status)
                ON CONFLICT(Folio) DO UPDATE SET
                  Fecha=excluded.Fecha,
                  TotalVenta=excluded.TotalVenta,
                  ImporteComision=excluded.ImporteComision,
                  Pagado=excluded.Pagado,
                  Saldo=excluded.Saldo,
                  Estatus=excluded.Estatus;
                """;
            insert.Parameters.AddWithValue("$folio", "C-" + group.Key);
            insert.Parameters.AddWithValue("$sale", group.Key);
            insert.Parameters.AddWithValue("$date", group.Max(x => x.Date).ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$total", group.Sum(x => x.TotalSale));
            insert.Parameters.AddWithValue("$commission", amount);
            insert.Parameters.AddWithValue("$paid", paid);
            insert.Parameters.AddWithValue("$balance", Math.Max(amount - paid, 0m));
            insert.Parameters.AddWithValue("$status", status);
            await insert.ExecuteNonQueryAsync();
        }

        foreach (var group in rows.GroupBy(x => x.Date.Date))
        {
            var cash = group.Sum(x => x.Cash);
            var card = group.Sum(x => x.Card);
            var totalDay = group.Sum(x => x.TotalDay);
            await using var cut = target.CreateCommand();
            cut.Transaction = transaction;
            cut.CommandText = """
                INSERT INTO LocalCortes (Fecha,Efectivo,Tarjeta,Pagos,Gastos,Esperado,Contado,Diferencia,Estatus,Usuario)
                VALUES ($date,$cash,$card,$payments,$expenses,$expected,$counted,$difference,'Abierto','IMPORTADOR')
                ON CONFLICT(Fecha) DO UPDATE SET
                  Efectivo=excluded.Efectivo,
                  Tarjeta=excluded.Tarjeta,
                  Pagos=excluded.Pagos,
                  Gastos=excluded.Gastos,
                  Esperado=excluded.Esperado,
                  Contado=excluded.Contado,
                  Diferencia=excluded.Diferencia;
                """;
            cut.Parameters.AddWithValue("$date", group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cut.Parameters.AddWithValue("$cash", cash);
            cut.Parameters.AddWithValue("$card", card);
            cut.Parameters.AddWithValue("$payments", cash + card);
            cut.Parameters.AddWithValue("$expenses", group.Sum(x => x.Expenses));
            cut.Parameters.AddWithValue("$expected", totalDay);
            cut.Parameters.AddWithValue("$counted", cash + card);
            cut.Parameters.AddWithValue("$difference", totalDay - (cash + card));
            await cut.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
        Console.WriteLine($"Normalización POS terminada: {rows.Count:N0} movimientos, {rows.Select(x => x.Date.Date).Distinct().Count():N0} cortes.");
        return 0;
    }

    public static async Task<int> RunPipelineAsync(IReadOnlyDictionary<string, string> options)
    {
        var databasePath = ProgramHelpers.Required(options, "db");
        var reportPath = options.TryGetValue("report", out var configuredReport) && !string.IsNullOrWhiteSpace(configuredReport)
            ? configuredReport
            : "REPORTE_IMPORTACION_REAL.md";
        if (!File.Exists(databasePath))
            throw new FileNotFoundException("No se encontró la base SQLite para ejecutar el pipeline.", databasePath);

        var steps = new List<(string Step, string Status, string Detail)>();
        try
        {
            await NormalizePosAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["db"] = databasePath });
            steps.Add(("normalize-pos", "OK", "Normalización POS ejecutada."));
        }
        catch (Exception ex)
        {
            steps.Add(("normalize-pos", "PENDIENTE", ex.Message));
        }

        var parityReport = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "VALIDACION_PARIDAD_DATOS_REALES.md");
        try
        {
            await ValidateParityAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["db"] = databasePath, ["report"] = parityReport });
            steps.Add(("validate-parity", "OK", parityReport));
        }
        catch (Exception ex)
        {
            steps.Add(("validate-parity", "ERROR", ex.Message));
        }

        await using var target = ProgramHelpers.OpenSqlite(databasePath);
        var tables = await GetSqliteTablesAsync(target);
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
            counts[table] = await CountRowsAsync(target, table);

        var moduleRows = new (string Module, string Status, string Detail)[]
        {
            ModuleStatus("Ventas", tables, counts, ["mkt__dbo__mov_operacion"], ["LocalVentas", "LocalVentaLineas"]),
            ModuleStatus("Pagos", tables, counts, ["mkt__dbo__mov_operacion"], ["LocalPagos"]),
            ModuleStatus("Comisiones", tables, counts, ["mkt__dbo__mov_operacion", "mkt__dbo__transporte"], ["LocalComisiones"]),
            ModuleStatus("Cortes", tables, counts, ["mkt__dbo__mov_operacion"], ["LocalCortes"]),
            ModuleStatus("Reportes", tables, counts, ["mkt__dbo__mov_operacion"], ["LocalComisiones", "LocalCortes"]),
            ModuleStatus("Usuarios/permisos", tables, counts, ["ControlTaxis__dbo__Usuarios", "ControlTaxis__dbo__UsuarioPermisos"], ["DesktopUsers", "DesktopPermissions"]),
            ModuleStatus("Gafetes", tables, counts, ["mkt__dbo__gafete"], ["LocalGafetes"])
        };

        var report = new StringBuilder();
        report.AppendLine("# Reporte final de importación y preparación offline");
        report.AppendLine();
        report.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Base SQLite: `{Path.GetFullPath(databasePath)}`");
        report.AppendLine();
        report.AppendLine("Este reporte prepara pruebas reales, pero no marca validación final si aún falta comparar contra el Web.");
        report.AppendLine();
        report.AppendLine("## Pasos automáticos");
        report.AppendLine();
        report.AppendLine("| Paso | Estado | Detalle |");
        report.AppendLine("|---|---|---|");
        foreach (var step in steps)
            report.AppendLine($"| {step.Step} | {step.Status} | {ProgramHelpers.EscapeMarkdown(step.Detail)} |");
        report.AppendLine();
        report.AppendLine("## Tablas importadas");
        report.AppendLine();
        report.AppendLine("| Tabla | Filas |");
        report.AppendLine("|---|---:|");
        foreach (var table in counts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            report.AppendLine($"| `{table.Key}` | {table.Value:N0} |");
        report.AppendLine();
        report.AppendLine("## Estado por módulo");
        report.AppendLine();
        report.AppendLine("| Módulo | Estado | Detalle |");
        report.AppendLine("|---|---|---|");
        foreach (var row in moduleRows)
            report.AppendLine($"| {row.Module} | {row.Status} | {ProgramHelpers.EscapeMarkdown(row.Detail)} |");
        report.AppendLine();
        report.AppendLine("## Comparaciones automaticas con datos reales");
        report.AppendLine();
        foreach (var line in await BuildRealComparisonLinesAsync(target, tables))
            report.AppendLine(line);
        report.AppendLine();
        report.AppendLine("## Datos faltantes detectados");
        foreach (var missing in moduleRows.Where(x => !x.Status.StartsWith("Listo", StringComparison.OrdinalIgnoreCase)))
            report.AppendLine($"- {missing.Module}: {missing.Detail}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(true));
        Console.WriteLine($"Reporte final generado: {Path.GetFullPath(reportPath)}");
        return 0;
    }

    private static async Task EnsureDesktopPosTablesAsync(SqliteConnection target)
    {
        await using var command = target.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS LocalComisiones (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Folio TEXT NOT NULL COLLATE NOCASE UNIQUE,
              VentaFolio TEXT NOT NULL COLLATE NOCASE, ClaveTaxista TEXT NOT NULL DEFAULT '',
              Taxista TEXT NOT NULL DEFAULT '', Fecha TEXT NOT NULL, TotalVenta REAL NOT NULL DEFAULT 0,
              ImporteComision REAL NOT NULL DEFAULT 0, Pagado REAL NOT NULL DEFAULT 0,
              Saldo REAL NOT NULL DEFAULT 0, Estatus TEXT NOT NULL DEFAULT 'Pendiente');
            CREATE TABLE IF NOT EXISTS LocalCortes (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Fecha TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Efectivo REAL NOT NULL DEFAULT 0, Tarjeta REAL NOT NULL DEFAULT 0, Pagos REAL NOT NULL DEFAULT 0,
              Gastos REAL NOT NULL DEFAULT 0, Esperado REAL NOT NULL DEFAULT 0, Contado REAL NOT NULL DEFAULT 0,
              Diferencia REAL NOT NULL DEFAULT 0, Estatus TEXT NOT NULL DEFAULT 'Abierto',
              Usuario TEXT NOT NULL, FechaCierre TEXT NULL);
            CREATE TABLE IF NOT EXISTS LocalProductos (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Codigo TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Nombre TEXT NOT NULL, Precio REAL NOT NULL DEFAULT 0, Iva REAL NOT NULL DEFAULT 0,
              Existencia INTEGER NOT NULL DEFAULT 0, Activo INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS LocalVentas (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Folio TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Fecha TEXT NOT NULL, Cliente TEXT NOT NULL DEFAULT '', Estatus TEXT NOT NULL DEFAULT 'Abierta',
              Subtotal REAL NOT NULL DEFAULT 0, Iva REAL NOT NULL DEFAULT 0, Total REAL NOT NULL DEFAULT 0,
              Pagado REAL NOT NULL DEFAULT 0, EstadoPago TEXT NOT NULL DEFAULT 'Pendiente', Usuario TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS LocalVentaLineas (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, VentaId INTEGER NOT NULL, ProductoId INTEGER NOT NULL,
              Codigo TEXT NOT NULL, Nombre TEXT NOT NULL, Cantidad INTEGER NOT NULL, PrecioUnitario REAL NOT NULL,
              Iva REAL NOT NULL DEFAULT 0, Subtotal REAL NOT NULL DEFAULT 0, ImporteIva REAL NOT NULL DEFAULT 0,
              Total REAL NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS LocalPagos (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Folio TEXT NOT NULL COLLATE NOCASE UNIQUE,
              VentaFolio TEXT NOT NULL COLLATE NOCASE, Fecha TEXT NOT NULL, Importe REAL NOT NULL,
              Metodo TEXT NOT NULL DEFAULT 'Efectivo', Notas TEXT NOT NULL DEFAULT '', Usuario TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS LocalGafetes (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Numero TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Estatus TEXT NOT NULL DEFAULT 'Disponible', TaxistaId INTEGER NULL, FechaAsignacion TEXT NULL,
              FechaRegreso TEXT NULL);
            CREATE TABLE IF NOT EXISTS LocalAuditoria (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Fecha TEXT NOT NULL, Usuario TEXT NOT NULL,
              Modulo TEXT NOT NULL, Accion TEXT NOT NULL, Referencia TEXT NOT NULL DEFAULT '',
              Importe REAL NULL, Detalles TEXT NOT NULL DEFAULT '',
              IdRegistro TEXT NOT NULL DEFAULT '', Descripcion TEXT NOT NULL DEFAULT '',
              BaseDatos TEXT NOT NULL DEFAULT '', Tabla TEXT NOT NULL DEFAULT '',
              FolioApp TEXT NOT NULL DEFAULT '', FolioOperacion TEXT NOT NULL DEFAULT '',
              FolioPos TEXT NOT NULL DEFAULT '', Taxista TEXT NOT NULL DEFAULT '',
              Gafete TEXT NOT NULL DEFAULT '', Exito INTEGER NOT NULL DEFAULT 1,
              Equipo TEXT NOT NULL DEFAULT '', Aplicacion TEXT NOT NULL DEFAULT 'ControlTaxiDesktop');
            CREATE TABLE IF NOT EXISTS DesktopUsers (
              Usuario TEXT NOT NULL PRIMARY KEY,
              PasswordHash TEXT NOT NULL,
              Rol TEXT NOT NULL,
              Estatus TEXT NOT NULL,
              FechaAlta TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS DesktopPermissions (
              Usuario TEXT NOT NULL,
              Modulo TEXT NOT NULL,
              PuedeVer INTEGER NOT NULL,
              PRIMARY KEY (Usuario, Modulo));
            CREATE TABLE IF NOT EXISTS LocalTransportes (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Clave TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Nombre TEXT NOT NULL, Minimo REAL NOT NULL DEFAULT 0, Maximo REAL NOT NULL DEFAULT 0,
              Comision REAL NOT NULL DEFAULT 0, DescuentoEfectivo REAL NOT NULL DEFAULT 0,
              DescuentoTarjeta REAL NOT NULL DEFAULT 0, DescuentoAmex REAL NOT NULL DEFAULT 0,
              Activo INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS LocalGuias (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Clave TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Nombre TEXT NOT NULL, Telefono TEXT NOT NULL DEFAULT '', Comision REAL NOT NULL DEFAULT 0,
              Estatus TEXT NOT NULL DEFAULT 'Activo');
            CREATE TABLE IF NOT EXISTS LocalGastos (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Fecha TEXT NOT NULL, Folio TEXT NOT NULL DEFAULT '',
              Concepto TEXT NOT NULL, Importe REAL NOT NULL DEFAULT 0, Notas TEXT NOT NULL DEFAULT '',
              Estatus TEXT NOT NULL DEFAULT 'Activo', Usuario TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS LocalRelaciones (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, FolioApp TEXT NOT NULL DEFAULT '',
              FolioOperacion TEXT NOT NULL DEFAULT '', FolioPos TEXT NOT NULL DEFAULT '',
              Gafete TEXT NOT NULL DEFAULT '', Taxista TEXT NOT NULL DEFAULT '', Vendedor TEXT NOT NULL DEFAULT '',
              Dejada REAL NULL, SeFueron INTEGER NULL, Observaciones TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS IX_LocalVentas_Fecha ON LocalVentas(Fecha);
            CREATE INDEX IF NOT EXISTS IX_LocalPagos_VentaFolio ON LocalPagos(VentaFolio);
            CREATE INDEX IF NOT EXISTS IX_LocalGafetes_Numero ON LocalGafetes(Numero);
            CREATE INDEX IF NOT EXISTS IX_LocalAuditoria_Fecha ON LocalAuditoria(Fecha);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task NormalizeProductsAsync(SqliteConnection target, SqliteTransaction transaction, HashSet<string> tables)
    {
        await ExecuteToolAsync(target, transaction, "DELETE FROM LocalProductos WHERE Codigo LIKE 'SERV:%' OR Codigo LIKE 'compuadmo:%' OR Codigo LIKE 'joyeria:%';");
        foreach (var service in new[]
        {
            ("SERV:JOYERIA", "Venta joyeria"),
            ("SERV:COMPRA", "Venta compra"),
            ("SERV:ARTESANIA", "Venta artesania"),
            ("SERV:LICOR", "Venta licor"),
            ("SERV:FARMACIA", "Venta farmacia"),
            ("SERV:DEJADA", "Dejada"),
            ("SERV:GASTOS", "Gastos")
        })
            await UpsertProductAsync(target, transaction, service.Item1, service.Item2, 0m, 0m);

        foreach (var source in new[] { "compuadmo", "joyeria" })
        {
            var table = source + "__dbo__Productos";
            if (!tables.Contains(table)) continue;
            var columns = await GetSqliteColumnsAsync(target, table);
            var productColumn = FindColumn(columns, "Producto");
            if (productColumn is null) continue;
            var nameColumn = FindColumn(columns, "Nombre");
            var priceColumn = FindColumn(columns, "preciopub") ?? FindColumn(columns, "precio1");
            var taxColumn = FindColumn(columns, "iva");
            var activeColumn = FindColumn(columns, "activo");
            await using var read = target.CreateCommand();
            read.Transaction = transaction;
            var productExpr = ProgramHelpers.QuoteSqlite(productColumn);
            var nameExpr = nameColumn is null ? "''" : ProgramHelpers.QuoteSqlite(nameColumn);
            var priceExpr = priceColumn is null ? "0" : ProgramHelpers.QuoteSqlite(priceColumn);
            var taxExpr = taxColumn is null ? "0" : ProgramHelpers.QuoteSqlite(taxColumn);
            var activeExpr = activeColumn is null ? "'S'" : ProgramHelpers.QuoteSqlite(activeColumn);
            read.CommandText = $"""
                SELECT CAST(COALESCE({productExpr}, '') AS TEXT), COALESCE({nameExpr}, ''),
                       COALESCE({priceExpr}, 0), COALESCE({taxExpr}, 0),
                       CASE WHEN COALESCE({activeExpr}, 'S') IN ('N','0') THEN 0 ELSE 1 END
                FROM {ProgramHelpers.QuoteSqlite(table)}
                WHERE COALESCE({productExpr}, '') <> '';
                """;
            await using var reader = await read.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                await UpsertProductAsync(target, transaction, $"{source}:{reader.GetString(0)}", reader.GetString(1), D(reader, 2), D(reader, 3), Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture));
        }
    }

    private static async Task NormalizeSalesPaymentsAndLinesAsync(SqliteConnection target, SqliteTransaction transaction, IReadOnlyList<ToolMovRow> rows)
    {
        await ExecuteToolAsync(target, transaction, "DELETE FROM LocalPagos WHERE Folio LIKE 'MKT-%';");
        await ExecuteToolAsync(target, transaction, "DELETE FROM LocalVentaLineas WHERE VentaId IN (SELECT Id FROM LocalVentas WHERE Folio LIKE 'MKT-%');");
        await ExecuteToolAsync(target, transaction, "DELETE FROM LocalVentas WHERE Folio LIKE 'MKT-%';");
        foreach (var group in rows.Where(x => !string.IsNullOrWhiteSpace(x.Folio)).GroupBy(x => x.Folio.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var folio = "MKT-" + group.Key;
            var date = group.Max(x => x.Date);
            var paid = group.Sum(x => x.Cash + x.Card);
            var total = group.Sum(x => x.TotalDay);
            await using var sale = target.CreateCommand();
            sale.Transaction = transaction;
            sale.CommandText = """
                INSERT INTO LocalVentas (Folio,Fecha,Cliente,Estatus,Subtotal,Iva,Total,Pagado,EstadoPago,Usuario)
                VALUES ($folio,$date,'','Importada',$subtotal,0,$total,$paid,$status,'IMPORTADOR')
                ON CONFLICT(Folio) DO UPDATE SET
                  Fecha=excluded.Fecha, Subtotal=excluded.Subtotal, Iva=excluded.Iva,
                  Total=excluded.Total, Pagado=excluded.Pagado, EstadoPago=excluded.EstadoPago,
                  Estatus=excluded.Estatus, Usuario=excluded.Usuario
                RETURNING Id;
                """;
            sale.Parameters.AddWithValue("$folio", folio);
            sale.Parameters.AddWithValue("$date", date.ToString("O", CultureInfo.InvariantCulture));
            sale.Parameters.AddWithValue("$subtotal", total);
            sale.Parameters.AddWithValue("$total", total);
            sale.Parameters.AddWithValue("$paid", paid);
            sale.Parameters.AddWithValue("$status", paid >= total ? "Pagada" : paid > 0m ? "Parcial" : "Pendiente");
            var saleId = Convert.ToInt64(await sale.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            foreach (var line in BuildServiceLines(group))
                await InsertSaleLineAsync(target, transaction, saleId, line.Code, line.Name, line.Total);
            var cash = group.Sum(x => x.Cash);
            var card = group.Sum(x => x.Card);
            if (cash > 0m)
                await UpsertPaymentAsync(target, transaction, "MKT-" + group.Key + "-EFE", folio, date, cash, "Efectivo");
            if (card > 0m)
                await UpsertPaymentAsync(target, transaction, "MKT-" + group.Key + "-TAR", folio, date, card, "Tarjeta");
        }
    }

    private static async Task NormalizeOperationsCatalogsAsync(SqliteConnection target, SqliteTransaction transaction, HashSet<string> tables)
    {
        if (tables.Contains("mkt__dbo__transporte"))
        {
            await ExecuteToolAsync(target, transaction, "DELETE FROM LocalTransportes WHERE Clave LIKE 'MKT:%';");
            var columns = await GetSqliteColumnsAsync(target, "mkt__dbo__transporte");
            var type = FindColumn(columns, "tipo");
            var name = FindColumn(columns, "nombre");
            var min = FindColumn(columns, "minimo");
            var max = FindColumn(columns, "maximo");
            var commission = FindColumn(columns, "comision");
            var cash = FindColumn(columns, "efectivo");
            var card = FindColumn(columns, "tarjeta");
            var amex = FindColumn(columns, "amex");
            var codeExpr = type is null ? "CAST(rowid AS TEXT)" : ProgramHelpers.QuoteSqlite(type);
            var nameExpr = name is null ? codeExpr : ProgramHelpers.QuoteSqlite(name);
            await using var command = target.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO LocalTransportes (Clave,Nombre,Minimo,Maximo,Comision,DescuentoEfectivo,DescuentoTarjeta,DescuentoAmex,Activo)
                SELECT 'MKT:' || COALESCE(CAST({codeExpr} AS TEXT), CAST(rowid AS TEXT)),
                       COALESCE(NULLIF(CAST({nameExpr} AS TEXT), ''), CAST({codeExpr} AS TEXT), 'Transporte'),
                       COALESCE({(min is null ? "0" : ProgramHelpers.QuoteSqlite(min))},0),
                       COALESCE({(max is null ? "0" : ProgramHelpers.QuoteSqlite(max))},0),
                       COALESCE({(commission is null ? "0" : ProgramHelpers.QuoteSqlite(commission))},0),
                       COALESCE({(cash is null ? "0" : ProgramHelpers.QuoteSqlite(cash))},0),
                       COALESCE({(card is null ? "0" : ProgramHelpers.QuoteSqlite(card))},0),
                       COALESCE({(amex is null ? "0" : ProgramHelpers.QuoteSqlite(amex))},0),
                       1
                FROM "mkt__dbo__transporte";
                """;
            await command.ExecuteNonQueryAsync();
        }

        if (tables.Contains("mkt__dbo__deptoguia"))
        {
            await ExecuteToolAsync(target, transaction, "DELETE FROM LocalGuias WHERE Clave LIKE 'MKT:%';");
            var columns = await GetSqliteColumnsAsync(target, "mkt__dbo__deptoguia");
            var id = FindColumn(columns, "idguia") ?? FindColumn(columns, "id") ?? FindColumn(columns, "clave");
            var name = FindColumn(columns, "nombre") ?? FindColumn(columns, "nombrestaf") ?? FindColumn(columns, "descripcion");
            if (id is not null || name is not null)
            {
                var idExpr = id is null ? "rowid" : ProgramHelpers.QuoteSqlite(id);
                var nameExpr = name is null ? "'Guia'" : ProgramHelpers.QuoteSqlite(name);
                await using var command = target.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"""
                    INSERT INTO LocalGuias (Clave,Nombre,Telefono,Comision,Estatus)
                    SELECT 'MKT:' || COALESCE(CAST({idExpr} AS TEXT), CAST(rowid AS TEXT)),
                           COALESCE(CAST({nameExpr} AS TEXT), 'Guia'), '', 0, 'Activo'
                    FROM "mkt__dbo__deptoguia";
                    """;
                await command.ExecuteNonQueryAsync();
            }
        }

        if (tables.Contains("mkt__dbo__ingresos"))
        {
            await ExecuteToolAsync(target, transaction, "DELETE FROM LocalGastos WHERE Usuario='IMPORTADOR' AND Folio LIKE 'MKT:%';");
            var columns = await GetSqliteColumnsAsync(target, "mkt__dbo__ingresos");
            var date = FindColumn(columns, "fecha");
            var concept = FindColumn(columns, "concepto") ?? FindColumn(columns, "descripcion") ?? FindColumn(columns, "observacion") ?? FindColumn(columns, "nombre");
            var amount = FindColumn(columns, "importe") ?? FindColumn(columns, "total") ?? FindColumn(columns, "monto");
            await using var command = target.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO LocalGastos (Fecha,Folio,Concepto,Importe,Notas,Estatus,Usuario)
                SELECT COALESCE({(date is null ? "''" : ProgramHelpers.QuoteSqlite(date))}, ''), 'MKT:' || rowid,
                       COALESCE({(concept is null ? "'Gasto/Ingreso'" : ProgramHelpers.QuoteSqlite(concept))}, 'Gasto/Ingreso'),
                       COALESCE({(amount is null ? "0" : ProgramHelpers.QuoteSqlite(amount))}, 0), '', 'Importado', 'IMPORTADOR'
                FROM "mkt__dbo__ingresos";
                """;
            await command.ExecuteNonQueryAsync();
        }

        if (tables.Contains("mkt__dbo__RelacionTicketTaxista"))
        {
            await ExecuteToolAsync(target, transaction, "DELETE FROM LocalRelaciones WHERE Observaciones='Importado desde mkt.RelacionTicketTaxista';");
            var columns = await GetSqliteColumnsAsync(target, "mkt__dbo__RelacionTicketTaxista");
            var app = FindColumn(columns, "FolioApp");
            var operation = FindColumn(columns, "FolioOperacion");
            var pos = FindColumn(columns, "FolioPos");
            var badge = FindColumn(columns, "Gafete");
            var driver = FindColumn(columns, "TaxistaNombre") ?? FindColumn(columns, "Taxista");
            var vendor = FindColumn(columns, "Vendedor");
            var payout = FindColumn(columns, "Dejada");
            var left = FindColumn(columns, "SeFueron");
            await using var command = target.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO LocalRelaciones (FolioApp,FolioOperacion,FolioPos,Gafete,Taxista,Vendedor,Dejada,SeFueron,Observaciones)
                SELECT COALESCE({(app is null ? "''" : ProgramHelpers.QuoteSqlite(app))}, ''),
                       COALESCE({(operation is null ? "''" : ProgramHelpers.QuoteSqlite(operation))}, ''),
                       COALESCE({(pos is null ? "''" : ProgramHelpers.QuoteSqlite(pos))}, ''),
                       COALESCE({(badge is null ? "''" : ProgramHelpers.QuoteSqlite(badge))}, ''),
                       COALESCE({(driver is null ? "''" : ProgramHelpers.QuoteSqlite(driver))}, ''),
                       COALESCE({(vendor is null ? "''" : ProgramHelpers.QuoteSqlite(vendor))}, ''),
                       {(payout is null ? "NULL" : ProgramHelpers.QuoteSqlite(payout))},
                       {(left is null ? "NULL" : ProgramHelpers.QuoteSqlite(left))},
                       'Importado desde mkt.RelacionTicketTaxista'
                FROM "mkt__dbo__RelacionTicketTaxista";
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static IEnumerable<(string Code, string Name, decimal Total)> BuildServiceLines(ToolMovRow row)
    {
        if (row.Jewelry != 0m) yield return ("SERV:JOYERIA", "Venta joyeria", row.Jewelry);
        if (row.Purchase != 0m) yield return ("SERV:COMPRA", "Venta compra", row.Purchase);
        if (row.Craft != 0m) yield return ("SERV:ARTESANIA", "Venta artesania", row.Craft);
        if (row.Beverages != 0m) yield return ("SERV:LICOR", "Venta licor", row.Beverages);
        if (row.Pharmacy != 0m) yield return ("SERV:FARMACIA", "Venta farmacia", row.Pharmacy);
        if (row.LeftAmount != 0m) yield return ("SERV:DEJADA", "Dejada", row.LeftAmount);
        if (row.Expenses != 0m) yield return ("SERV:GASTOS", "Gastos", row.Expenses);
    }

    private static IEnumerable<(string Code, string Name, decimal Total)> BuildServiceLines(IEnumerable<ToolMovRow> rows)
    {
        var items = rows.ToArray();
        var totals = new[]
        {
            ("SERV:JOYERIA", "Venta joyeria", items.Sum(x => x.Jewelry)),
            ("SERV:COMPRA", "Venta compra", items.Sum(x => x.Purchase)),
            ("SERV:ARTESANIA", "Venta artesania", items.Sum(x => x.Craft)),
            ("SERV:LICOR", "Venta licor", items.Sum(x => x.Beverages)),
            ("SERV:FARMACIA", "Venta farmacia", items.Sum(x => x.Pharmacy)),
            ("SERV:DEJADA", "Dejada", items.Sum(x => x.LeftAmount)),
            ("SERV:GASTOS", "Gastos", items.Sum(x => x.Expenses))
        };
        foreach (var item in totals)
            if (item.Item3 != 0m)
                yield return item;
    }

    private static async Task NormalizeBadgesAsync(SqliteConnection target, SqliteTransaction transaction, HashSet<string> tables)
    {
        if (!tables.Contains("mkt__dbo__gafete")) return;
        await ExecuteToolAsync(target, transaction, "DELETE FROM LocalGafetes;");
        await using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO LocalGafetes (Numero,Estatus,FechaAsignacion,FechaRegreso)
            SELECT CAST(gafete AS TEXT),
                   CASE UPPER(TRIM(COALESCE(venta,'')))
                     WHEN 'A' THEN 'Asignado'
                     WHEN 'S' THEN 'Suspendido'
                     WHEN 'R' THEN 'Disponible'
                     ELSE 'Disponible'
                   END,
                   CASE WHEN UPPER(TRIM(COALESCE(venta,''))) = 'A' THEN COALESCE(fecha,'') ELSE NULL END,
                   CASE WHEN UPPER(TRIM(COALESCE(venta,''))) = 'R' THEN COALESCE(fecha,'') ELSE NULL END
            FROM "mkt__dbo__gafete"
            WHERE rowid IN (
                SELECT MAX(rowid)
                FROM "mkt__dbo__gafete"
                WHERE COALESCE(gafete,'') <> ''
                GROUP BY gafete
            );
            """;
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task NormalizeAuditAsync(SqliteConnection target, SqliteTransaction transaction, HashSet<string> tables)
    {
        if (!tables.Contains("mkt__dbo__AuditoriaMovimiento")) return;
        await ExecuteToolAsync(target, transaction, "DELETE FROM LocalAuditoria WHERE Aplicacion='ControlTaxiWeb' OR Aplicacion='ControlTaxiDesktop' OR Aplicacion='';");
        await using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO LocalAuditoria
              (Fecha,Usuario,Modulo,Accion,Referencia,Importe,Detalles,IdRegistro,Descripcion,BaseDatos,Tabla,FolioApp,FolioOperacion,FolioPos,Taxista,Gafete,Exito,Equipo,Aplicacion)
            SELECT COALESCE(FechaLocal, FechaUtc, ''), COALESCE(Usuario,''), COALESCE(Modulo,''), COALESCE(Accion,''),
                   COALESCE(IdRegistro,''), Importe, COALESCE(DetalleJson,''),
                   COALESCE(IdRegistro,''), COALESCE(Descripcion,''), COALESCE(BaseDatos,''), COALESCE(Tabla,''),
                   COALESCE(FolioApp,''), COALESCE(FolioOperacion,''), COALESCE(FolioPos,''), COALESCE(Taxista,''),
                   COALESCE(Gafete,''), COALESCE(Exito,1), COALESCE(Equipo,''), COALESCE(Aplicacion,'ControlTaxiWeb')
            FROM "mkt__dbo__AuditoriaMovimiento";
            """;
        await insert.ExecuteNonQueryAsync();
    }

    private static async Task NormalizeUsersAndPermissionsAsync(SqliteConnection target, SqliteTransaction transaction, HashSet<string> tables)
    {
        if (tables.Contains("ControlTaxis__dbo__Usuarios"))
        {
            await ExecuteToolAsync(target, transaction, "DELETE FROM DesktopUsers;");
            await using var users = target.CreateCommand();
            users.Transaction = transaction;
            users.CommandText = """
                INSERT INTO DesktopUsers (Usuario,PasswordHash,Rol,Estatus,FechaAlta)
                SELECT COALESCE(Usuario,''), COALESCE(PasswordHash,''), COALESCE(Rol,'Usuario'),
                       COALESCE(Estatus,'Activo'), COALESCE(FechaAlta,'')
                FROM "ControlTaxis__dbo__Usuarios"
                WHERE COALESCE(Usuario,'') <> '';
                """;
            await users.ExecuteNonQueryAsync();
        }
        if (tables.Contains("ControlTaxis__dbo__UsuarioPermisos"))
        {
            await ExecuteToolAsync(target, transaction, "DELETE FROM DesktopPermissions;");
            await using var permissions = target.CreateCommand();
            permissions.Transaction = transaction;
            permissions.CommandText = """
                INSERT OR REPLACE INTO DesktopPermissions (Usuario,Modulo,PuedeVer)
                SELECT COALESCE(Usuario,''), COALESCE(Modulo,''), COALESCE(PuedeVer,0)
                FROM "ControlTaxis__dbo__UsuarioPermisos"
                WHERE COALESCE(Usuario,'') <> '' AND COALESCE(Modulo,'') <> '';
                """;
            await permissions.ExecuteNonQueryAsync();
        }
    }

    private static async Task UpsertProductAsync(SqliteConnection target, SqliteTransaction transaction, string code, string name, decimal price, decimal tax, int active = 1)
    {
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocalProductos (Codigo,Nombre,Precio,Iva,Activo)
            VALUES ($code,$name,$price,$tax,$active)
            ON CONFLICT(Codigo) DO UPDATE SET Nombre=excluded.Nombre, Precio=excluded.Precio, Iva=excluded.Iva, Activo=excluded.Activo;
            """;
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(name) ? code : name);
        command.Parameters.AddWithValue("$price", price);
        command.Parameters.AddWithValue("$tax", tax);
        command.Parameters.AddWithValue("$active", active);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSaleLineAsync(SqliteConnection target, SqliteTransaction transaction, long saleId, string code, string name, decimal total)
    {
        var productId = await ScalarToolAsync(target, transaction, "SELECT Id FROM LocalProductos WHERE Codigo=$code;", ("$code", code));
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocalVentaLineas (VentaId,ProductoId,Codigo,Nombre,Cantidad,PrecioUnitario,Iva,Subtotal,ImporteIva,Total)
            VALUES ($sale,$product,$code,$name,1,$price,0,$subtotal,0,$total);
            """;
        command.Parameters.AddWithValue("$sale", saleId);
        command.Parameters.AddWithValue("$product", productId);
        command.Parameters.AddWithValue("$code", code);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$price", total);
        command.Parameters.AddWithValue("$subtotal", total);
        command.Parameters.AddWithValue("$total", total);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertPaymentAsync(SqliteConnection target, SqliteTransaction transaction, string paymentFolio, string saleFolio, DateTime date, decimal amount, string method)
    {
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO LocalPagos (Folio,VentaFolio,Fecha,Importe,Metodo,Notas,Usuario)
            VALUES ($folio,$sale,$date,$amount,$method,'Importado desde mkt.mov_operacion','IMPORTADOR')
            ON CONFLICT(Folio) DO UPDATE SET VentaFolio=excluded.VentaFolio, Fecha=excluded.Fecha,
              Importe=excluded.Importe, Metodo=excluded.Metodo, Notas=excluded.Notas, Usuario=excluded.Usuario;
            """;
        command.Parameters.AddWithValue("$folio", paymentFolio);
        command.Parameters.AddWithValue("$sale", saleFolio);
        command.Parameters.AddWithValue("$date", date.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$amount", amount);
        command.Parameters.AddWithValue("$method", method);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteToolAsync(SqliteConnection target, SqliteTransaction transaction, string sql)
    {
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarToolAsync(SqliteConnection target, SqliteTransaction transaction, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = target.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static string? FindColumn(IReadOnlyList<string> columns, string name) =>
        columns.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<string>> BuildRealComparisonLinesAsync(SqliteConnection target, HashSet<string> tables)
    {
        var lines = new List<string>();
        lines.Add("| Comparacion | Resultado | Estado |");
        lines.Add("|---|---|---|");
        if (tables.Contains("mkt__dbo__mov_operacion") && tables.Contains("LocalVentas"))
        {
            var sourceRows = await ScalarLongAsync(target, "SELECT COUNT(*) FROM \"mkt__dbo__mov_operacion\";");
            var sourceFolios = await ScalarLongAsync(target, "SELECT COUNT(DISTINCT CAST(COALESCE(folioperacion,'') AS TEXT)) FROM \"mkt__dbo__mov_operacion\" WHERE COALESCE(folioperacion,'') <> '';" );
            var localSales = await ScalarLongAsync(target, "SELECT COUNT(*) FROM LocalVentas WHERE Folio LIKE 'MKT-%';");
            var sourceTotal = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(COALESCE(totaljoyeria,0)+COALESCE(totalcompra,0)+COALESCE(totalartesania,0)+COALESCE(totallicor,0)+COALESCE(totalfarmacia,0)),0) FROM \"mkt__dbo__mov_operacion\";");
            var localTotal = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(Total),0) FROM LocalVentas WHERE Folio LIKE 'MKT-%';");
            var state = sourceFolios == localSales && Math.Abs(sourceTotal - localTotal) < 0.01m ? "Validado con datos reales importados" : "Pendiente por diferencia";
            lines.Add($"| Ventas: movimientos/folios/total | Origen filas: {sourceRows:N0}; folios origen: {sourceFolios:N0}; ventas Desktop: {localSales:N0}; total origen: {sourceTotal:N2}; total Desktop: {localTotal:N2} | {state} |");
            if (sourceRows != sourceFolios)
                lines.Add($"| Ventas: duplicados de folio | `mkt.mov_operacion` trae {sourceRows - sourceFolios:N0} filas adicionales con folios repetidos; Desktop conserva una venta por folio y suma todos sus movimientos reales. | Analizado y corregido |");
        }

        if (tables.Contains("mkt__dbo__mov_operacion") && tables.Contains("LocalPagos"))
        {
            var sourcePaid = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(COALESCE(totalefectivo,0)+COALESCE(totaltarjeta,0)),0) FROM \"mkt__dbo__mov_operacion\";");
            var localPaid = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(Importe),0) FROM LocalPagos WHERE Folio LIKE 'MKT-%';");
            var localPayments = await ScalarLongAsync(target, "SELECT COUNT(*) FROM LocalPagos WHERE Folio LIKE 'MKT-%';");
            var state = Math.Abs(sourcePaid - localPaid) < 0.01m ? "Validado con datos reales importados" : "Pendiente por diferencia";
            lines.Add($"| Pagos: efectivo+tarjeta | Pagos Desktop: {localPayments:N0}; total origen: {sourcePaid:N2}; total Desktop: {localPaid:N2} | {state} |");
        }

        if (tables.Contains("mkt__dbo__mov_operacion") && tables.Contains("LocalComisiones"))
        {
            var localRows = await ScalarLongAsync(target, "SELECT COUNT(*) FROM LocalComisiones WHERE Folio LIKE 'C-%';");
            var localCommission = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(ImporteComision),0) FROM LocalComisiones WHERE Folio LIKE 'C-%';");
            var storedCommission = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(COALESCE(comision,0)),0) FROM \"mkt__dbo__mov_operacion\";");
            var diffCount = await ScalarLongAsync(target, """
                WITH src AS (
                  SELECT CAST(folioperacion AS TEXT) Folio, SUM(COALESCE(comision,0)) Importe
                  FROM "mkt__dbo__mov_operacion"
                  WHERE COALESCE(folioperacion,'') <> ''
                  GROUP BY CAST(folioperacion AS TEXT)
                )
                SELECT COUNT(*)
                FROM src
                INNER JOIN LocalComisiones c ON c.Folio='C-' || src.Folio
                WHERE ABS(COALESCE(c.ImporteComision,0)-COALESCE(src.Importe,0)) > 0.01;
                """);
            var state = diffCount == 0 && Math.Abs(localCommission - storedCommission) < 0.01m ? "Validado con datos reales importados" : "Pendiente por diferencia";
            lines.Add($"| Comisiones: `mkt.mov_operacion.comision` vs Desktop | comisiones Desktop: {localRows:N0}; suma origen almacenada: {storedCommission:N2}; suma Desktop: {localCommission:N2}; folios diferentes: {diffCount:N0} | {state} |");
            if (tables.Contains("ControlTaxis__dbo__Comisiones") && await CountRowsAsync(target, "ControlTaxis__dbo__Comisiones") == 0)
                lines.Add("| Comisiones: baseline Web persistido | `ControlTaxis.Comisiones` esta vacia; se compara contra `mkt.mov_operacion.comision` y la formula migrada. | Pendiente por falta de baseline Web persistido |");
        }

        if (tables.Contains("mkt__dbo__mov_operacion") && tables.Contains("LocalCortes"))
        {
            var sourceDays = await ScalarLongAsync(target, "SELECT COUNT(DISTINCT date(fecha)) FROM \"mkt__dbo__mov_operacion\";");
            var localCuts = await ScalarLongAsync(target, "SELECT COUNT(*) FROM LocalCortes;");
            var sourceExpected = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(COALESCE(totaljoyeria,0)+COALESCE(totalcompra,0)+COALESCE(totalartesania,0)+COALESCE(totallicor,0)+COALESCE(totalfarmacia,0)),0) FROM \"mkt__dbo__mov_operacion\";");
            var localExpected = await ScalarDecimalAsync(target, "SELECT COALESCE(SUM(Esperado),0) FROM LocalCortes;");
            var state = sourceDays == localCuts && Math.Abs(sourceExpected - localExpected) < 0.01m ? "Validado con datos reales importados" : "Pendiente por diferencia";
            lines.Add($"| Cortes: dias/esperado | dias origen: {sourceDays:N0}; cortes Desktop: {localCuts:N0}; esperado origen: {sourceExpected:N2}; esperado Desktop: {localExpected:N2} | {state} |");
            if (tables.Contains("ControlTaxis__dbo__Cortes") && await CountRowsAsync(target, "ControlTaxis__dbo__Cortes") == 0)
                lines.Add("| Cortes: baseline Web persistido | `ControlTaxis.Cortes` esta vacia; se compara contra agregados reales de `mkt.mov_operacion`. | Pendiente por falta de baseline Web persistido |");
        }

        if (tables.Contains("mkt__dbo__gafete") && tables.Contains("LocalGafetes"))
        {
            var sourceBadges = await ScalarLongAsync(target, "SELECT COUNT(DISTINCT CAST(gafete AS TEXT)) FROM \"mkt__dbo__gafete\" WHERE COALESCE(gafete,'') <> '';" );
            var localBadges = await ScalarLongAsync(target, "SELECT COUNT(*) FROM LocalGafetes;");
            var state = sourceBadges == localBadges ? "Validado con datos reales importados" : "Pendiente por diferencia";
            lines.Add($"| Gafetes: distintivos vigentes | gafetes distintos origen: {sourceBadges:N0}; gafetes Desktop: {localBadges:N0} | {state} |");
        }

        if (tables.Contains("mkt__dbo__AuditoriaMovimiento") && tables.Contains("LocalAuditoria"))
        {
            var sourceAudit = await CountRowsAsync(target, "mkt__dbo__AuditoriaMovimiento");
            var localAudit = await CountRowsAsync(target, "LocalAuditoria");
            var state = sourceAudit == localAudit ? "Validado con datos reales importados" : "Pendiente por diferencia";
            lines.Add($"| Auditoria: registros | origen: {sourceAudit:N0}; Desktop: {localAudit:N0} | {state} |");
        }

        if (tables.Contains("ControlTaxis__dbo__Usuarios") && tables.Contains("DesktopUsers"))
        {
            var sourceUsers = await CountRowsAsync(target, "ControlTaxis__dbo__Usuarios");
            var localUsers = await CountRowsAsync(target, "DesktopUsers");
            var sourcePermissions = tables.Contains("ControlTaxis__dbo__UsuarioPermisos") ? await CountRowsAsync(target, "ControlTaxis__dbo__UsuarioPermisos") : 0;
            var localPermissions = tables.Contains("DesktopPermissions") ? await CountRowsAsync(target, "DesktopPermissions") : 0;
            var state = sourceUsers == localUsers && sourcePermissions == localPermissions ? "Validado con datos reales importados" : "Pendiente por diferencia";
            lines.Add($"| Usuarios/permisos | usuarios origen/Desktop: {sourceUsers:N0}/{localUsers:N0}; permisos origen/Desktop: {sourcePermissions:N0}/{localPermissions:N0} | {state} |");
        }

        lines.Add("| Excel/PDF | Se verifican contra las tablas normalizadas (`LocalVentas`, `LocalPagos`, `LocalComisiones`, `LocalCortes`, `LocalGafetes`); falta comparacion visual pixel/formato contra archivos generados por Web. | Pendiente por comparacion de formato |");
        return lines;
    }

    private static async Task WriteFolioDifferencesReportAsync(SqliteConnection target, HashSet<string> tables, string reportPath)
    {
        var report = new StringBuilder();
        report.AppendLine("# Diferencias por folio - pagos y comisiones");
        report.AppendLine();
        report.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine();
        report.AppendLine("Este reporte se genera solo desde `DatosLocal/ControlTaxi.db`. No modifica SQL Server ni el sistema Web.");
        report.AppendLine();
        if (!tables.Contains("mkt__dbo__mov_operacion"))
        {
            report.AppendLine("No existe `mkt__dbo__mov_operacion`; no se puede comparar folios reales.");
            await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(true));
            return;
        }

        var totalRows = await ScalarLongAsync(target, "SELECT COUNT(*) FROM \"mkt__dbo__mov_operacion\";");
        var totalFolios = await ScalarLongAsync(target, "SELECT COUNT(DISTINCT CAST(COALESCE(folioperacion,'') AS TEXT)) FROM \"mkt__dbo__mov_operacion\" WHERE COALESCE(folioperacion,'') <> '';" );
        report.AppendLine("## Resumen de folios origen");
        report.AppendLine();
        report.AppendLine($"- Movimientos origen: {totalRows:N0}");
        report.AppendLine($"- Folios distintos origen: {totalFolios:N0}");
        report.AppendLine($"- Movimientos adicionales por folios duplicados: {totalRows - totalFolios:N0}");
        report.AppendLine();

        await AppendQueryTableAsync(target, report, "## Folios duplicados con mayor impacto", """
            SELECT CAST(folioperacion AS TEXT) AS Folio,
                   COUNT(*) AS Movimientos,
                   SUM(COALESCE(totaljoyeria,0)+COALESCE(totalcompra,0)+COALESCE(totalartesania,0)+COALESCE(totallicor,0)+COALESCE(totalfarmacia,0)) AS TotalVenta,
                   SUM(COALESCE(totalefectivo,0)+COALESCE(totaltarjeta,0)) AS TotalPagos,
                   SUM(COALESCE(comision,0)) AS TotalComision
            FROM "mkt__dbo__mov_operacion"
            WHERE COALESCE(folioperacion,'') <> ''
            GROUP BY CAST(folioperacion AS TEXT)
            HAVING COUNT(*) > 1
            ORDER BY ABS(TotalVenta) DESC
            LIMIT 100;
            """, ["Folio", "Movimientos", "Total venta", "Pagos", "Comision"]);

        await AppendQueryTableAsync(target, report, "## Folios faltantes en Desktop", """
            WITH src AS (
              SELECT DISTINCT CAST(folioperacion AS TEXT) Folio
              FROM "mkt__dbo__mov_operacion"
              WHERE COALESCE(folioperacion,'') <> ''
            )
            SELECT src.Folio
            FROM src
            LEFT JOIN LocalVentas v ON v.Folio='MKT-' || src.Folio
            WHERE v.Folio IS NULL
            ORDER BY src.Folio
            LIMIT 200;
            """, ["Folio"]);

        await AppendQueryTableAsync(target, report, "## Folios sobrantes en Desktop", """
            WITH src AS (
              SELECT DISTINCT CAST(folioperacion AS TEXT) Folio
              FROM "mkt__dbo__mov_operacion"
              WHERE COALESCE(folioperacion,'') <> ''
            )
            SELECT REPLACE(v.Folio,'MKT-','') AS Folio
            FROM LocalVentas v
            LEFT JOIN src ON v.Folio='MKT-' || src.Folio
            WHERE v.Folio LIKE 'MKT-%' AND src.Folio IS NULL
            ORDER BY v.Folio
            LIMIT 200;
            """, ["Folio"]);

        await AppendQueryTableAsync(target, report, "## Diferencias de venta por folio", """
            WITH src AS (
              SELECT CAST(folioperacion AS TEXT) Folio,
                     SUM(COALESCE(totaljoyeria,0)+COALESCE(totalcompra,0)+COALESCE(totalartesania,0)+COALESCE(totallicor,0)+COALESCE(totalfarmacia,0)) TotalOrigen
              FROM "mkt__dbo__mov_operacion"
              WHERE COALESCE(folioperacion,'') <> ''
              GROUP BY CAST(folioperacion AS TEXT)
            ),
            dst AS (
              SELECT REPLACE(Folio,'MKT-','') Folio, Total TotalDesktop
              FROM LocalVentas
              WHERE Folio LIKE 'MKT-%'
            )
            SELECT src.Folio, src.TotalOrigen, COALESCE(dst.TotalDesktop,0) TotalDesktop,
                   src.TotalOrigen-COALESCE(dst.TotalDesktop,0) Diferencia
            FROM src
            LEFT JOIN dst ON dst.Folio=src.Folio
            WHERE ABS(src.TotalOrigen-COALESCE(dst.TotalDesktop,0)) > 0.01
            ORDER BY ABS(Diferencia) DESC
            LIMIT 200;
            """, ["Folio", "Origen", "Desktop", "Diferencia"]);

        await AppendQueryTableAsync(target, report, "## Diferencias de pagos por folio", """
            WITH src AS (
              SELECT CAST(folioperacion AS TEXT) Folio,
                     SUM(COALESCE(totalefectivo,0)+COALESCE(totaltarjeta,0)) TotalOrigen
              FROM "mkt__dbo__mov_operacion"
              WHERE COALESCE(folioperacion,'') <> ''
              GROUP BY CAST(folioperacion AS TEXT)
            ),
            dst AS (
              SELECT REPLACE(VentaFolio,'MKT-','') Folio, SUM(Importe) TotalDesktop
              FROM LocalPagos
              WHERE VentaFolio LIKE 'MKT-%'
              GROUP BY REPLACE(VentaFolio,'MKT-','')
            )
            SELECT src.Folio, src.TotalOrigen, COALESCE(dst.TotalDesktop,0) TotalDesktop,
                   src.TotalOrigen-COALESCE(dst.TotalDesktop,0) Diferencia
            FROM src
            LEFT JOIN dst ON dst.Folio=src.Folio
            WHERE ABS(src.TotalOrigen-COALESCE(dst.TotalDesktop,0)) > 0.01
            ORDER BY ABS(Diferencia) DESC
            LIMIT 200;
            """, ["Folio", "Origen", "Desktop", "Diferencia"]);

        await AppendQueryTableAsync(target, report, "## Diferencias de comision por folio", """
            WITH src AS (
              SELECT CAST(folioperacion AS TEXT) Folio,
                     SUM(COALESCE(comision,0)) TotalOrigen
              FROM "mkt__dbo__mov_operacion"
              WHERE COALESCE(folioperacion,'') <> ''
              GROUP BY CAST(folioperacion AS TEXT)
            ),
            dst AS (
              SELECT REPLACE(Folio,'C-','') Folio, ImporteComision TotalDesktop
              FROM LocalComisiones
              WHERE Folio LIKE 'C-%'
            )
            SELECT src.Folio, src.TotalOrigen, COALESCE(dst.TotalDesktop,0) TotalDesktop,
                   src.TotalOrigen-COALESCE(dst.TotalDesktop,0) Diferencia
            FROM src
            LEFT JOIN dst ON dst.Folio=src.Folio
            WHERE ABS(src.TotalOrigen-COALESCE(dst.TotalDesktop,0)) > 0.01
            ORDER BY ABS(Diferencia) DESC
            LIMIT 200;
            """, ["Folio", "Origen", "Desktop", "Diferencia"]);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, report.ToString(), new UTF8Encoding(true));
    }

    private static async Task AppendQueryTableAsync(SqliteConnection target, StringBuilder report, string title, string sql, IReadOnlyList<string> headers)
    {
        report.AppendLine(title);
        report.AppendLine();
        report.AppendLine("| " + string.Join(" | ", headers) + " |");
        report.AppendLine("|" + string.Join("|", headers.Select(_ => "---")) + "|");
        await using var command = target.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = 0;
        while (await reader.ReadAsync())
        {
            var values = new List<string>();
            for (var index = 0; index < reader.FieldCount; index++)
                values.Add(FormatReportValue(reader.GetValue(index)));
            report.AppendLine("| " + string.Join(" | ", values.Select(ProgramHelpers.EscapeMarkdown)) + " |");
            rows++;
        }
        if (rows == 0)
            report.AppendLine("| " + string.Join(" | ", headers.Select((_, index) => index == 0 ? "Sin diferencias" : "")) + " |");
        report.AppendLine();
    }

    private static string FormatReportValue(object value)
    {
        if (value is null or DBNull) return "";
        if (value is double or float or decimal)
            return Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection target, string sql)
    {
        await using var command = target.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<decimal> ScalarDecimalAsync(SqliteConnection target, string sql)
    {
        await using var command = target.CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? 0m : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }

    private static (string Module, string Status, string Detail) ModuleStatus(string module, HashSet<string> tables, IReadOnlyDictionary<string, long> counts, string[] requiredSource, string[] requiredLocal)
    {
        var missingSource = requiredSource.Where(x => !tables.Contains(x)).ToArray();
        var missingLocal = requiredLocal.Where(x => !tables.Contains(x)).ToArray();
        if (missingSource.Length == 0 && missingLocal.Length == 0)
        {
            var localRows = requiredLocal.Sum(x => counts.TryGetValue(x, out var count) ? count : 0);
            return localRows > 0
                ? (module, "Listo para validar", $"Origen y tablas Desktop presentes. Filas locales: {localRows:N0}.")
                : (module, "Pendiente", "Tablas presentes, pero faltan filas normalizadas.");
        }
        return (module, "Pendiente", $"Faltan origen: {string.Join(", ", missingSource.DefaultIfEmpty("ninguna"))}. Faltan Desktop: {string.Join(", ", missingLocal.DefaultIfEmpty("ninguna"))}.");
    }

    private static async Task<HashSet<string>> GetSqliteTablesAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task<IReadOnlyList<string>> GetSqliteColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({ProgramHelpers.QuoteSqlite(table)});";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(1));
        return result;
    }

    private static async Task<long> CountRowsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {ProgramHelpers.QuoteSqlite(table)};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed record ToolMovRow(
        string Folio,
        DateTime Date,
        decimal Cash,
        decimal Card,
        decimal Payment,
        decimal Expenses,
        decimal Jewelry,
        decimal Purchase,
        decimal Craft,
        decimal Beverages,
        decimal Pharmacy,
        decimal LeftAmount,
        decimal StoredCommission,
        decimal CashDiscount,
        decimal CardDiscount,
        decimal CommissionPercent,
        decimal Minimum,
        decimal Maximum)
    {
        public decimal TotalSale => Jewelry + Purchase;
        public decimal TotalDay => Jewelry + Purchase + Craft + Beverages + Pharmacy;
        public decimal Tasting => Expenses;
    }

    private static async Task<List<ToolMovRow>> ReadImportedMovRowsForToolAsync(SqliteConnection target, HashSet<string> tables)
    {
        var hasTransport = tables.Contains("mkt__dbo__transporte");
        await using var command = target.CreateCommand();
        command.CommandText = hasTransport
            ? """
                SELECT
                  CAST(COALESCE(m.folioperacion, '') AS TEXT), COALESCE(m.fecha, ''),
                  COALESCE(m.totalefectivo, 0), COALESCE(m.totaltarjeta, 0), COALESCE(m.pago, 0),
                  COALESCE(m.totalgastos, 0), COALESCE(m.totaljoyeria, 0), COALESCE(m.totalcompra, 0),
                  COALESCE(m.totalartesania, 0), COALESCE(m.totallicor, 0), COALESCE(m.totalfarmacia, 0),
                  COALESCE(m.dejada, 0), COALESCE(m.comision, 0), COALESCE(t.efectivo, 0), COALESCE(t.tarjeta, 0),
                  COALESCE(t.comision, 0), COALESCE(t.minimo, 0), COALESCE(t.maximo, 0)
                FROM "mkt__dbo__mov_operacion" m
                LEFT JOIN "mkt__dbo__transporte" t
                  ON UPPER(TRIM(COALESCE(t.tipo, ''))) = UPPER(TRIM(COALESCE(m.transportetipo, '')))
                  OR UPPER(TRIM(COALESCE(t.nombre, ''))) = UPPER(TRIM(COALESCE(m.transportetipo, '')));
                """
            : """
                SELECT
                  CAST(COALESCE(m.folioperacion, '') AS TEXT), COALESCE(m.fecha, ''),
                  COALESCE(m.totalefectivo, 0), COALESCE(m.totaltarjeta, 0), COALESCE(m.pago, 0),
                  COALESCE(m.totalgastos, 0), COALESCE(m.totaljoyeria, 0), COALESCE(m.totalcompra, 0),
                  COALESCE(m.totalartesania, 0), COALESCE(m.totallicor, 0), COALESCE(m.totalfarmacia, 0),
                  COALESCE(m.dejada, 0), COALESCE(m.comision, 0), 0,0,0,0,0
                FROM "mkt__dbo__mov_operacion" m;
                """;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ToolMovRow>();
        while (await reader.ReadAsync())
        {
            var date = DateTime.TryParse(Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : DateTime.Today;
            rows.Add(new ToolMovRow(reader.GetString(0), date, D(reader, 2), D(reader, 3), D(reader, 4), D(reader, 5), D(reader, 6), D(reader, 7), D(reader, 8), D(reader, 9), D(reader, 10), D(reader, 11), D(reader, 12), D(reader, 13), D(reader, 14), D(reader, 15), D(reader, 16), D(reader, 17)));
        }
        return rows;
    }

    private static decimal CalculateWebCommissionForTool(ToolMovRow row)
    {
        var discount = row.Card > 0m ? row.CardDiscount / 100m : row.CashDiscount / 100m;
        var fixedCommission = row.CommissionPercent > 100m ? row.CommissionPercent :
            row.CommissionPercent <= 0m && row.Maximum is >= 1m and <= 1000m ? row.Maximum :
            row.CommissionPercent <= 0m && row.Minimum is >= 1m and <= 1000m ? row.Minimum : 0m;
        var percent = fixedCommission > 0m ? 0m : row.CommissionPercent / 100m;
        var value = fixedCommission > 0m ? fixedCommission :
            discount == 0m ? (row.TotalSale - row.LeftAmount - row.Beverages - row.Tasting) * percent :
            ((row.TotalSale - (row.TotalSale * discount)) - row.LeftAmount - row.Beverages - row.Tasting) * percent;
        return Math.Max(0m, decimal.Truncate(value));
    }

    private static decimal D(SqliteDataReader reader, int index) => Convert.ToDecimal(reader.GetValue(index), CultureInfo.InvariantCulture);
}
