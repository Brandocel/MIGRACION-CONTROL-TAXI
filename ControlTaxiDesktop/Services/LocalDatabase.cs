using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;

namespace ControlTaxiDesktop.Services;

public sealed class LocalDatabase
{
    private readonly bool _forceTestDatabase;
    private readonly string _dataDirectory;
    private string? _resolvedProductionPath;

    public string ProductionPath => Path.Combine(_dataDirectory, "ControlTaxi.db");
    public string TestPath => Path.Combine(_dataDirectory, "ControlTaxi.prueba.db");
    public bool IsTestDatabase { get; private set; }
    public string ActivePath => IsTestDatabase ? TestPath : _resolvedProductionPath ?? ProductionPath;

    public LocalDatabase(bool forceTestDatabase = false)
    {
        _forceTestDatabase = forceTestDatabase;
        _dataDirectory = forceTestDatabase
            ? Path.Combine(Path.GetTempPath(), "ControlTaxiDesktopSelfTest", Guid.NewGuid().ToString("N"))
            : Path.Combine(AppContext.BaseDirectory, "DatosLocal");
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dataDirectory);
        _resolvedProductionPath = _forceTestDatabase ? null : ResolveProductionPath() ?? ProductionPath;
        IsTestDatabase = _forceTestDatabase;
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS DesktopUsers (
              Usuario TEXT NOT NULL PRIMARY KEY,
              PasswordHash TEXT NOT NULL,
              Rol TEXT NOT NULL,
              Estatus TEXT NOT NULL,
              FechaAlta TEXT NOT NULL,
              BranchCode TEXT NOT NULL DEFAULT 'P28');
            CREATE TABLE IF NOT EXISTS DesktopPermissions (
              Usuario TEXT NOT NULL,
              Modulo TEXT NOT NULL,
              PuedeVer INTEGER NOT NULL,
              PRIMARY KEY (Usuario, Modulo));
            CREATE TABLE IF NOT EXISTS LocalTarifas (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Tipo TEXT NOT NULL, Nombre TEXT NOT NULL,
              Dejada REAL NOT NULL DEFAULT 0, Minimo REAL NOT NULL DEFAULT 0, Maximo REAL NOT NULL DEFAULT 0,
              Activa INTEGER NOT NULL DEFAULT 1, UNIQUE(Tipo, Nombre));
            CREATE TABLE IF NOT EXISTS LocalHoteles (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Nombre TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Activo INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE IF NOT EXISTS LocalTaxistas (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Clave TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Nombre TEXT NOT NULL, Telefono TEXT NOT NULL DEFAULT '', Placas TEXT NOT NULL DEFAULT '',
              Modelo TEXT NOT NULL DEFAULT '', Unidad TEXT NOT NULL DEFAULT '', TipoServicio TEXT NOT NULL DEFAULT '',
              Estatus TEXT NOT NULL DEFAULT 'Activo');
            CREATE TABLE IF NOT EXISTS LocalGafetes (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Numero TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Estatus TEXT NOT NULL DEFAULT 'Disponible', TaxistaId INTEGER NULL, FechaAsignacion TEXT NULL,
              FechaRegreso TEXT NULL, FOREIGN KEY(TaxistaId) REFERENCES LocalTaxistas(Id));
            CREATE TABLE IF NOT EXISTS LocalRegistros (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Folio TEXT NOT NULL COLLATE NOCASE UNIQUE,
              Fecha TEXT NOT NULL, TaxistaId INTEGER NULL, HotelId INTEGER NULL, TarifaId INTEGER NULL,
              Gafete TEXT NOT NULL DEFAULT '', Pax INTEGER NOT NULL DEFAULT 1, Origen TEXT NOT NULL DEFAULT '',
              Destino TEXT NOT NULL DEFAULT '', Importe REAL NOT NULL DEFAULT 0, MetodoPago TEXT NOT NULL DEFAULT 'Efectivo',
              Notas TEXT NOT NULL DEFAULT '', Usuario TEXT NOT NULL, FOREIGN KEY(TaxistaId) REFERENCES LocalTaxistas(Id),
              FOREIGN KEY(HotelId) REFERENCES LocalHoteles(Id), FOREIGN KEY(TarifaId) REFERENCES LocalTarifas(Id));
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
              Total REAL NOT NULL DEFAULT 0, FOREIGN KEY(VentaId) REFERENCES LocalVentas(Id) ON DELETE CASCADE,
              FOREIGN KEY(ProductoId) REFERENCES LocalProductos(Id));
            CREATE TABLE IF NOT EXISTS LocalPagos (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Folio TEXT NOT NULL COLLATE NOCASE UNIQUE,
              VentaFolio TEXT NOT NULL COLLATE NOCASE, Fecha TEXT NOT NULL, Importe REAL NOT NULL,
              Metodo TEXT NOT NULL DEFAULT 'Efectivo', Notas TEXT NOT NULL DEFAULT '', Usuario TEXT NOT NULL);
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
            CREATE TABLE IF NOT EXISTS LocalAuditoria (
              Id INTEGER PRIMARY KEY AUTOINCREMENT, Fecha TEXT NOT NULL, Usuario TEXT NOT NULL,
              Modulo TEXT NOT NULL, Accion TEXT NOT NULL, Referencia TEXT NOT NULL DEFAULT '',
              Importe REAL NULL, Detalles TEXT NOT NULL DEFAULT '');
            CREATE INDEX IF NOT EXISTS IX_LocalVentas_Fecha ON LocalVentas(Fecha);
            CREATE INDEX IF NOT EXISTS IX_LocalPagos_VentaFolio ON LocalPagos(VentaFolio);
            CREATE INDEX IF NOT EXISTS IX_LocalComisiones_VentaFolio ON LocalComisiones(VentaFolio);
            CREATE INDEX IF NOT EXISTS IX_LocalAuditoria_Fecha ON LocalAuditoria(Fecha);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureUserBranchCodeColumnAsync(connection);
        await EnsureAuditColumnsAsync(connection);
        await EnsureImportedFolioIndexesAsync(connection);
    }

    private static async Task EnsureUserBranchCodeColumnAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('DesktopUsers');";
        await using var reader = await command.ExecuteReaderAsync();
        var hasBranchCode = false;
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), "BranchCode", StringComparison.OrdinalIgnoreCase))
            {
                hasBranchCode = true;
                break;
            }
        }

        if (hasBranchCode)
            return;

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE DesktopUsers ADD COLUMN BranchCode TEXT NOT NULL DEFAULT 'P28';";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task EnsureImportedFolioIndexesAsync(SqliteConnection connection)
    {
        // Las tablas importadas (mkt__dbo__gafete llega a tener cientos de miles de filas) se cruzan
        // por folio usando CAST(...AS TEXT); sin estos índices por expresión, SQLite no puede evitar
        // el escaneo cruzado completo y la pantalla de Operaciones se congela al abrir cualquier módulo.
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__gafete", "idx_gafete_folioperacion_cast", "CAST(folioperacion AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__AppMovilRegistro", "idx_appmovil_folio_app_cast", "CAST(folio_app AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__AppMovilRegistro", "idx_appmovil_folio_app_original_cast", "CAST(folio_app_original AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__dejadas", "idx_dejadas_folioregistro_cast", "CAST(folioregistro AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__dejadas", "idx_dejadas_folioregistrostr_cast", "CAST(folioregistrostr AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__dejadas", "idx_dejadas_codigorecepcion_cast", "CAST(codigorecepcion AS TEXT)");

        // Las consultas de comisiones cruzan por ticket contra las tablas de tienda
        // (remisioM, pagosM, egresos/gastos) usando CAST(... AS TEXT). Sin estos índices
        // por expresión, cada búsqueda escanea cientos de miles de filas (remisioM ~204k,
        // pagosM ~219k) por cada registro, y la ventana POS se congela. Con ellos, cada
        // búsqueda es una lectura de índice instantánea.
        await CreateIndexIfTableExistsAsync(connection, "compuadmo__dbo__remisioM", "idx_compu_remisioM_folioreg_cast", "CAST(folioregistro AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "compuadmo__dbo__remisioM", "idx_compu_remisioM_folioremi_cast", "CAST(folio_remision AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "joyeria__dbo__remisioM", "idx_joy_remisioM_folioreg_cast", "CAST(folio_registro AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "joyeria__dbo__remisioM", "idx_joy_remisioM_foliofact_cast", "CAST(folio_factura AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "compuadmo__dbo__pagosM", "idx_compu_pagosM_foliofact_cast", "CAST(folio_factura AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "joyeria__dbo__pagosM", "idx_joy_pagosM_foliofact_cast", "CAST(folio_factura AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "compuadmo__dbo__egresos", "idx_compu_egresos_folioremi_cast", "CAST(folio_remision AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "joyeria__dbo__gastos", "idx_joy_gastos_foliofact_cast", "CAST(folio_factura AS TEXT)");
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__dejadas", "idx_dejadas_codigorecepcion_plain", "codigorecepcion");
        await CreateIndexIfTableExistsAsync(connection, "mkt__dbo__dejadas", "idx_dejadas_folioregistrostr_plain", "folioregistrostr");
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task CreateIndexIfTableExistsAsync(SqliteConnection connection, string table, string indexName, string expression)
    {
        if (!await HasTableAsync(connection, table)) return;
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE INDEX IF NOT EXISTS \"{indexName}\" ON \"{table}\" ({expression});";
        await command.ExecuteNonQueryAsync();
    }

    public SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ActivePath) ?? _dataDirectory);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = ActivePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 15
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=15000;
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    private string? ResolveProductionPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        string? firstBinCandidate = null;

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "DatosLocal", "ControlTaxi.db");
            if (File.Exists(candidate))
            {
                if (current.FullName.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0
                    || current.FullName.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    firstBinCandidate ??= candidate;
                }
                else
                {
                    return candidate;
                }
            }
            current = current.Parent;
        }

        if (firstBinCandidate is not null)
            return firstBinCandidate;

        var workingCandidate = Path.Combine(Environment.CurrentDirectory, "DatosLocal", "ControlTaxi.db");
        return File.Exists(workingCandidate) ? workingCandidate : null;
    }

    private static async Task EnsureAuditColumnsAsync(SqliteConnection connection)
    {
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "IdRegistro", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "Descripcion", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "BaseDatos", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "Tabla", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "FolioApp", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "FolioOperacion", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "FolioPos", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "Taxista", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "Gafete", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "Exito", "INTEGER NOT NULL DEFAULT 1");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "Equipo", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(connection, "LocalAuditoria", "Aplicacion", "TEXT NOT NULL DEFAULT 'ControlTaxiDesktop'");
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string definition)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = $"PRAGMA table_info(\"{table}\");";
        await using var reader = await inspect.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;
        }
        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};";
        await alter.ExecuteNonQueryAsync();
    }
}
