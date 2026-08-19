using System.Data.Common;
using System.Globalization;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        public async Task<List<PosRegistroAppTaxistaOption>> SearchRegistroAppTaxistasAsync(string? query, int limit = 20)
        {
            try
            {
                var pageSize = Math.Clamp(limit, 5, 80);
                var normalized = Normalize(query) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    var hostingerRows = (await _appTaxiApi.SearchCatalogTaxistasAsync(normalized, pageSize)).ToList();
                    if (hostingerRows.Count > 0)
                        return hostingerRows;
                }

                var connection = _dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync();

                var sources = new List<string>();
                var appMovilHasPlacas = await PosColumnExistsAsync(connection, "AppMovilRegistro", "placas");
                var appMovilHasModelo = await PosColumnExistsAsync(connection, "AppMovilRegistro", "modelo_vehiculo");
                var appMovilHasTelefonoContacto = await PosColumnExistsAsync(connection, "AppMovilRegistro", "telefono_contacto");
                if (await PosTableExistsAsync(connection, "cataxi"))
                {
                    sources.Add($@"
SELECT
    CONVERT(nvarchar(30), idtaxi) AS Clave,
    COALESCE(nombre, '') AS Nombre,
    COALESCE(tipo, '') AS TransporteTipo,
    COALESCE(tipo, '') AS Unidad,
    COALESCE(telefono, '') AS Telefono,
    '' AS Placas,
    '' AS Modelo,
    '' AS Nacionalidad,
    1 AS Prioridad
FROM {PosTable("cataxi")}
WHERE NULLIF(LTRIM(RTRIM(COALESCE(nombre, ''))), '') IS NOT NULL
  AND UPPER(COALESCE(activo, 'S')) <> 'N'");
                }

                if (await PosTableExistsAsync(connection, "AppMovilRegistro"))
                {
                    var placasSql = appMovilHasPlacas ? "COALESCE(placas, '')" : "''";
                    var modeloSql = appMovilHasModelo ? "COALESCE(modelo_vehiculo, '')" : "''";
                    var appMovilHasNacionalidad = await PosColumnExistsAsync(connection, "AppMovilRegistro", "nacionalidad");
                    var telefonoSql = appMovilHasTelefonoContacto
                        ? "COALESCE(NULLIF(telefono_taxista, ''), NULLIF(telefono_contacto, ''), '')"
                        : "COALESCE(telefono_taxista, '')";
                    var nacionalidadSql = appMovilHasNacionalidad ? "COALESCE(nacionalidad, '')" : "''";
                    sources.Add($@"
SELECT
    CONVERT(nvarchar(30), COALESCE(id_catalogo, 0)) AS Clave,
    COALESCE(vendedor_nombre, '') AS Nombre,
    COALESCE(tipo_operacion, unidad, '') AS TransporteTipo,
    COALESCE(NULLIF(unidad, ''), NULLIF({placasSql}, ''), '') AS Unidad,
    {telefonoSql} AS Telefono,
    {placasSql} AS Placas,
    {modeloSql} AS Modelo,
    {nacionalidadSql} AS Nacionalidad,
    2 AS Prioridad
FROM {PosTable("AppMovilRegistro")}
WHERE COALESCE(id_catalogo, 0) > 0
  AND NULLIF(LTRIM(RTRIM(COALESCE(vendedor_nombre, ''))), '') IS NOT NULL");
                }

                if (await AppTableExistsAsync(connection, "RegistrosViajes") && await AppColumnExistsAsync(connection, "RegistrosViajes", "id_catalogo"))
                {
                    var appNombreSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "nombre_taxista") ? "COALESCE(nombre_taxista, '')" : "''";
                    var appTipoSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "tipo_servicio") ? "COALESCE(tipo_servicio, '')" : "''";
                    var appUnidadSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "unidad") ? "COALESCE(unidad, '')" : "''";
                    var appTelefonoTaxistaSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "telefono_taxista") ? "NULLIF(telefono_taxista, '')" : "NULL";
                    var appTelefonoContactoSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "telefono_contacto") ? "NULLIF(telefono_contacto, '')" : "NULL";
                    var appPlacasSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "placas") ? "COALESCE(placas, '')" : "''";
                    var appModeloSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "modelo_vehiculo") ? "COALESCE(modelo_vehiculo, '')" : "''";
                    var appNacionalidadSql = await AppColumnExistsAsync(connection, "RegistrosViajes", "nacionalidad") ? "COALESCE(nacionalidad, '')" : "''";
                    sources.Add($@"
SELECT
    CONVERT(nvarchar(30), COALESCE(id_catalogo, 0)) AS Clave,
    {appNombreSql} AS Nombre,
    {appTipoSql} AS TransporteTipo,
    COALESCE(NULLIF({appUnidadSql}, ''), NULLIF({appPlacasSql}, ''), '') AS Unidad,
    COALESCE({appTelefonoTaxistaSql}, {appTelefonoContactoSql}, '') AS Telefono,
    {appPlacasSql} AS Placas,
    {appModeloSql} AS Modelo,
    {appNacionalidadSql} AS Nacionalidad,
    3 AS Prioridad
FROM {AppTable("RegistrosViajes")}
WHERE COALESCE(id_catalogo, 0) > 0
  AND NULLIF(LTRIM(RTRIM({appNombreSql})), '') IS NOT NULL");
                }

                if (await PosTableExistsAsync(connection, "dejadas"))
                {
                    sources.Add($@"
SELECT
    CONVERT(nvarchar(30), COALESCE(idtaxi, 0)) AS Clave,
    COALESCE(nombrevendedor, '') AS Nombre,
    COALESCE(tipotransporte, '') AS TransporteTipo,
    COALESCE(unidad, '') AS Unidad,
    COALESCE(telefono, '') AS Telefono,
    '' AS Placas,
    '' AS Modelo,
    '' AS Nacionalidad,
    4 AS Prioridad
FROM {PosTable("dejadas")}
WHERE COALESCE(idtaxi, 0) > 0
  AND NULLIF(LTRIM(RTRIM(COALESCE(nombrevendedor, ''))), '') IS NOT NULL");
                }

                if (sources.Count == 0)
                    return new List<PosRegistroAppTaxistaOption>();

                await using var command = connection.CreateCommand();
                command.CommandText = $@"
DECLARE @q NVARCHAR(200) = @query;

SELECT TOP (@limit)
    Clave,
    MAX(Nombre) AS Nombre,
    MAX(TransporteTipo) AS TransporteTipo,
    MAX(Unidad) AS Unidad,
    MAX(Telefono) AS Telefono,
    MAX(Placas) AS Placas,
    MAX(Modelo) AS Modelo,
    MAX(Nacionalidad) AS Nacionalidad,
    MIN(Prioridad) AS Prioridad
FROM (
    {string.Join("\nUNION ALL\n", sources)}
) t
WHERE (@q = ''
    OR Clave LIKE '%' + @q + '%'
    OR Nombre LIKE '%' + @q + '%'
    OR TransporteTipo LIKE '%' + @q + '%'
    OR Unidad LIKE '%' + @q + '%'
    OR Telefono LIKE '%' + @q + '%')
GROUP BY Clave
ORDER BY MIN(Prioridad), MAX(Nombre), Clave;";
                AddParameter(command, "@query", normalized);
                AddParameter(command, "@limit", pageSize);
                var rows = await ReadRowsAsync(command);
                return rows.Select(row => new PosRegistroAppTaxistaOption
                    {
                        Clave = PickText(row, "Clave"),
                        Nombre = CleanTaxiName(PickText(row, "Nombre")),
                        TransporteTipo = PickText(row, "TransporteTipo"),
                        Unidad = PickText(row, "Unidad"),
                        Telefono = PickText(row, "Telefono"),
                        Placas = PickText(row, "Placas"),
                        Modelo = PickText(row, "Modelo"),
                        Nacionalidad = PickText(row, "Nacionalidad")
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Clave) || !string.IsNullOrWhiteSpace(x.Nombre))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar taxistas para Registro App.");
                return new List<PosRegistroAppTaxistaOption>();
            }
        }

        public async Task<List<PosRegistroAppTarifaOption>> SearchRegistroAppTarifasAsync(string? query, int limit = 50)
        {
            var normalized = Normalize(query);
            var pageSize = Math.Clamp(limit, 5, 120);
            try
            {
                var hostingerRates = (await _appTaxiApi.GetCatalogRatesAsync()).ToList();
                if (hostingerRates.Count > 0)
                {
                    return hostingerRates
                        .Where(x => normalized == null
                            || TextMatches(x.Tipo, normalized)
                            || TextMatches(x.Nombre, normalized))
                        .Take(pageSize)
                        .ToList();
                }

                var rows = await _posContext.Transportes
                    .AsNoTracking()
                    .Where(x => x.Tipo != null && x.Tipo.Trim() != string.Empty)
                    .ToListAsync();

                return rows
                    .Where(x => normalized == null
                        || TextMatches(x.Tipo, normalized)
                        || TextMatches(x.Nombre, normalized))
                    .OrderBy(x => x.Nombre)
                    .ThenBy(x => x.Tipo)
                    .Take(pageSize)
                    .Select(x => new PosRegistroAppTarifaOption
                    {
                        Tipo = x.Tipo,
                        Nombre = x.Nombre ?? string.Empty,
                        Dejada = ResolveRegistroAppDejada(x),
                        Minimo = ToDecimal(x.Minimo),
                        Maximo = ToDecimal(x.Maximo)
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar tarifas para Registro App.");
                return new List<PosRegistroAppTarifaOption>();
            }
        }

        public async Task<List<string>> SearchRegistroAppHotelesAsync(string? query, int limit = 50)
        {
            var normalized = Normalize(query);
            var pageSize = Math.Clamp(limit, 5, 120);
            var hostingerHotels = (await _appTaxiApi.GetCatalogHotelsAsync()).ToList();
            if (hostingerHotels.Count > 0)
            {
                return hostingerHotels
                    .Where(x => normalized == null || TextMatches(x, normalized))
                    .Take(pageSize)
                    .ToList();
            }

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            try
            {
                var sources = new List<string>();
                if (await PosTableExistsAsync(connection, "AppMovilHoteles"))
                {
                    var hasHotelNombre = await PosColumnExistsAsync(connection, "AppMovilHoteles", "HotelNombre");
                    var hasHotelNormalizado = await PosColumnExistsAsync(connection, "AppMovilHoteles", "HotelNormalizado");
                    var hasNombre = await PosColumnExistsAsync(connection, "AppMovilHoteles", "Nombre");
                    var hasActivo = await PosColumnExistsAsync(connection, "AppMovilHoteles", "active");
                    var hotelExpr = hasHotelNombre && hasHotelNormalizado
                        ? "COALESCE(NULLIF(HotelNombre, ''), NULLIF(HotelNormalizado, ''))"
                        : hasHotelNombre
                            ? "HotelNombre"
                            : hasNombre
                                ? "Nombre"
                                : hasHotelNormalizado ? "HotelNormalizado" : null;
                    if (hotelExpr != null)
                    {
                    sources.Add($@"
SELECT {hotelExpr} AS Hotel
FROM {PosTable("AppMovilHoteles")}
WHERE {(hasActivo ? "COALESCE(active, 1) = 1" : "1 = 1")}");
                    }
                }

                if (await PosTableExistsAsync(connection, "AppMovilRegistro"))
                {
                    sources.Add($@"
SELECT hotel AS Hotel
FROM {PosTable("AppMovilRegistro")}
WHERE NULLIF(LTRIM(RTRIM(hotel)), '') IS NOT NULL");
                }

                if (await AppTableExistsAsync(connection, "RegistrosViajes") && await AppColumnExistsAsync(connection, "RegistrosViajes", "hotel"))
                {
                    sources.Add($@"
SELECT hotel AS Hotel
FROM {AppTable("RegistrosViajes")}
WHERE NULLIF(LTRIM(RTRIM(hotel)), '') IS NOT NULL");
                }

                if (await AppTableExistsAsync(connection, "AppMovilRegistro") && await AppColumnExistsAsync(connection, "AppMovilRegistro", "hotel"))
                {
                    sources.Add($@"
SELECT hotel AS Hotel
FROM {AppTable("AppMovilRegistro")}
WHERE NULLIF(LTRIM(RTRIM(hotel)), '') IS NOT NULL");
                }

                if (await PosTableExistsAsync(connection, "dejadas"))
                {
                    sources.Add($@"
SELECT hotel AS Hotel
FROM {PosTable("dejadas")}
WHERE NULLIF(LTRIM(RTRIM(hotel)), '') IS NOT NULL");
                }

                if (sources.Count == 0)
                    return new List<string>();

                await using var command = connection.CreateCommand();
                command.CommandText = $@"
DECLARE @q NVARCHAR(200) = @query;

SELECT TOP (@limit) Hotel
FROM (
    {string.Join("\nUNION\n", sources)}
) h
WHERE NULLIF(LTRIM(RTRIM(Hotel)), '') IS NOT NULL
  AND (@q = '' OR Hotel LIKE '%' + @q + '%')
ORDER BY Hotel;";
                AddParameter(command, "@query", normalized ?? string.Empty);
                AddParameter(command, "@limit", pageSize);
                var rows = await ReadRowsAsync(command);
                return rows
                    .Select(x => PickText(x, "Hotel"))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible cargar hoteles para Registro App.");
                return new List<string>();
            }
        }

        public async Task<string?> CreateRegistroAppAsync(PosRegistroAppViewModel model, string usuario)
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();

            await EnsureAppMovilMirrorSchemaAsync(connection);
            await SyncAppMovilRegistrosFromApiAsync(null, DateTime.Today.AddDays(-45), DateTime.Today);

            var folioOriginal = SafeText(FirstText(model.FolioOriginal, $"WEB{DateTime.Now:yyyyMMddHHmmssfff}"), 60);
            if (model.Total <= 0m)
            {
                var tarifa = (await SearchRegistroAppTarifasAsync(model.TipoOperacion, 1)).FirstOrDefault();
                if (tarifa != null)
                    model.Total = tarifa.Dejada;
            }

            var efectivo = string.Equals(model.MetodoPago, "Tarjeta", StringComparison.OrdinalIgnoreCase) ? 0m : model.Total;
            var tarjeta = string.Equals(model.MetodoPago, "Tarjeta", StringComparison.OrdinalIgnoreCase) ? model.Total : 0m;
            var row = new PosOperacionRowViewModel
            {
                FolioControl = folioOriginal,
                CatalogId = model.TaxistaId,
                Gafete = SafeText(model.Gafete, 300),
                Hora = model.FechaOperacion.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Vendedor = model.TaxistaNombre,
                Telefono = model.Telefono,
                TelefonoContacto = FirstText(model.TelefonoContacto, model.Telefono),
                Nacionalidad = model.Nacionalidad,
                Placas = model.Placas,
                ModeloVehiculo = model.Modelo,
                Unidad = model.Unidad,
                Hotel = model.Hotel,
                Origen = model.Origen,
                Sitio = model.Sitio,
                Destino = model.Destino,
                Pax = Math.Max(model.Pax, 1),
                TipoOperacion = model.TipoOperacion,
                Total = model.Total,
                Efectivo = efectivo,
                Tarjeta = tarjeta,
                UsuarioOrigen = FirstText(usuario, "WEB"),
                Notas = model.Notas
            };

            await UpsertAppMovilRegistroAsync(connection, row);

            var folioControl = await EnsureAppMovilFolioControlAsync(connection, folioOriginal);
            var gafetes = SplitGafeteNumbers(model.Gafete)
                .Select(NormalizeBadgeToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            model.Gafete = string.Join(", ", gafetes);
            await ReplaceAppMovilRegistroGafetesAsync(connection, folioControl, model.TaxistaId, gafetes);

            var hostingerOk = await _appTaxiApi.PushTripRecordAsync(folioOriginal, model);

            await InsertPosAuditoriaAsync(
                null,
                null,
                usuario,
                "RegistroDiario",
                "CREAR_REGISTRO_APP_WEB",
                folioControl,
                $"Registro tipo app creado desde Control Taxi con folio {folioControl}.",
                BuildAuditJson(
                    ("FolioOriginal", folioOriginal),
                    ("FolioOperacion", folioControl),
                    ("FolioApp", folioControl),
                    ("Gafete", model.Gafete),
                    ("TaxistaNombre", model.TaxistaNombre),
                    ("Hotel", model.Hotel),
                    ("Total", model.Total),
                    ("SyncHostinger", hostingerOk),
                    ("Tabla", "AppMovilRegistro")),
                true);

            return folioControl;
        }

        private async Task SyncAppMovilRegistrosFromApiAsync(string? query, DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (!_autoSyncAppMovilFromApi)
                return;

            try
            {
                var inicio = fechaInicio?.Date;
                var fin = fechaFin?.Date;
                if (!inicio.HasValue && !fin.HasValue && string.IsNullOrWhiteSpace(query))
                {
                    inicio = DateTime.Today.AddDays(-7);
                    fin = DateTime.Today;
                }

                var normalizedQuery = Normalize(query);
                var syncKey = $"{normalizedQuery ?? string.Empty}|{inicio:yyyyMMdd}|{fin:yyyyMMdd}";
                var minimumSyncAge = string.IsNullOrWhiteSpace(normalizedQuery)
                    ? TimeSpan.FromSeconds(45)
                    : TimeSpan.FromSeconds(30);
                var now = DateTime.UtcNow;
                if (AppMovilSyncMarks.TryGetValue(syncKey, out var lastSync)
                    && now - lastSync < minimumSyncAge)
                {
                    return;
                }

                var apiRows = await _appTaxiApi.GetTripRecordsAsync(query, inicio, fin);
                if (apiRows.Count == 0)
                {
                    AppMovilSyncMarks[syncKey] = now;
                    return;
                }

                var connection = _dbContext.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync();

                await EnsureAppMovilMirrorSchemaAsync(connection);
                foreach (var row in apiRows)
                {
                    if (await IsAppMovilFolioBlockedAsync(connection, row.FolioControl))
                        continue;

                    var folioControl = await UpsertAppMovilRegistroAsync(connection, row);
                    if (!string.IsNullOrWhiteSpace(folioControl))
                    {
                        var currentBadgeText = await FindSingleValueAsync(connection, $@"
SELECT TOP (1) COALESCE(folio_gafete, '')
FROM {PosTable("AppMovilRegistro")}
WHERE folio_app = @folio
   OR folio_app_original = @folio", ("@folio", folioControl));
                        var gafetes = SplitGafeteNumbers(FirstText(currentBadgeText, row.Gafete))
                            .Select(NormalizeBadgeToken)
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        await ReplaceAppMovilRegistroGafetesAsync(connection, folioControl, row.CatalogId, gafetes);
                    }
                }

                AppMovilSyncMarks[syncKey] = now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No fue posible sincronizar registros de la app movil hacia SQL Server.");
            }
        }

        private static async Task EnsureAppMovilMirrorSchemaAsync(DbConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF OBJECT_ID('{PosTable("AppMovilFolioControl").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
    CREATE TABLE {PosTable("AppMovilFolioControl")} (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilFolioControl PRIMARY KEY,
        FolioAppOriginal NVARCHAR(60) NOT NULL,
        FolioControl NVARCHAR(60) NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilFolioControl_Fecha DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
    CREATE TABLE {PosTable("AppMovilRegistro")} (
        id_app_movil_registro INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistro PRIMARY KEY,
        folio_app NVARCHAR(60) NOT NULL,
        folio_app_original NVARCHAR(60) NOT NULL DEFAULT '',
        folio_gafete NVARCHAR(300) NOT NULL DEFAULT '',
        folio_pos NVARCHAR(100) NOT NULL DEFAULT '',
        fecha_operacion DATETIME2 NOT NULL,
        id_catalogo INT NULL,
        vendedor_clave NVARCHAR(50) NOT NULL DEFAULT '',
        vendedor_nombre NVARCHAR(150) NOT NULL DEFAULT '',
        telefono_taxista NVARCHAR(30) NOT NULL DEFAULT '',
        telefono_contacto NVARCHAR(30) NOT NULL DEFAULT '',
        nacionalidad NVARCHAR(120) NOT NULL DEFAULT '',
        placas NVARCHAR(50) NOT NULL DEFAULT '',
        modelo_vehiculo NVARCHAR(150) NOT NULL DEFAULT '',
        unidad NVARCHAR(50) NOT NULL DEFAULT '',
        hotel NVARCHAR(200) NOT NULL DEFAULT '',
        origen NVARCHAR(150) NOT NULL DEFAULT '',
        sitio NVARCHAR(150) NOT NULL DEFAULT '',
        destino NVARCHAR(150) NOT NULL DEFAULT '',
        pax INT NOT NULL DEFAULT 0,
        tipo_operacion NVARCHAR(80) NOT NULL DEFAULT '',
        total DECIMAL(18,2) NOT NULL DEFAULT 0,
        efectivo DECIMAL(18,2) NOT NULL DEFAULT 0,
        tarjeta DECIMAL(18,2) NOT NULL DEFAULT 0,
        estado_pago_dejada NVARCHAR(20) NOT NULL DEFAULT 'pendiente',
        fecha_pago_dejada DATETIME2 NULL,
        usuario_pago_dejada NVARCHAR(180) NOT NULL DEFAULT '',
        ticket_pago_dejada NVARCHAR(80) NOT NULL DEFAULT '',
        usuario_movil NVARCHAR(80) NOT NULL DEFAULT 'hostinger',
        notas NVARCHAR(MAX) NOT NULL DEFAULT '',
        detalle_json NVARCHAR(MAX) NOT NULL DEFAULT '',
        estado_sync NVARCHAR(30) NOT NULL DEFAULT 'SINCRONIZADO',
        fecha_creacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioApp ON {PosTable("AppMovilRegistro")}(folio_app);
    CREATE INDEX IX_AppMovilRegistro_Fecha ON {PosTable("AppMovilRegistro")}(fecha_operacion DESC);
END;

IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'folio_app_original') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD folio_app_original NVARCHAR(60) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'id_catalogo') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD id_catalogo INT NULL;
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'folio_gafete') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD folio_gafete NVARCHAR(300) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'telefono_contacto') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD telefono_contacto NVARCHAR(30) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'nacionalidad') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD nacionalidad NVARCHAR(120) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'placas') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD placas NVARCHAR(50) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'modelo_vehiculo') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD modelo_vehiculo NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'unidad') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD unidad NVARCHAR(50) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'sitio') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD sitio NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'destino') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD destino NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_status') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_status NVARCHAR(30) NOT NULL DEFAULT 'pendiente';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_date') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_date DATETIME2 NULL;
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_user') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_user NVARCHAR(80) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'payout_ticket') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD payout_ticket NVARCHAR(80) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'estado_pago_dejada') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD estado_pago_dejada NVARCHAR(20) NOT NULL DEFAULT 'pendiente';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'fecha_pago_dejada') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD fecha_pago_dejada DATETIME2 NULL;
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'usuario_pago_dejada') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD usuario_pago_dejada NVARCHAR(180) NOT NULL DEFAULT '';
IF COL_LENGTH('{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', 'ticket_pago_dejada') IS NULL ALTER TABLE {PosTable("AppMovilRegistro")} ADD ticket_pago_dejada NVARCHAR(80) NOT NULL DEFAULT '';

DECLARE @objFolioControl INT = OBJECT_ID(N'{PosTable("AppMovilFolioControl").Replace("[", string.Empty).Replace("]", string.Empty)}', N'U');
DECLARE @objRegistro INT = OBJECT_ID(N'{PosTable("AppMovilRegistro").Replace("[", string.Empty).Replace("]", string.Empty)}', N'U');

IF @objFolioControl IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = @objFolioControl
          AND name = N'UX_AppMovilFolioControl_Original'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM {PosTable("AppMovilFolioControl")}
        GROUP BY FolioAppOriginal
        HAVING COUNT(*) > 1
    )
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM sys.stats
            WHERE object_id = @objFolioControl
              AND name = N'UX_AppMovilFolioControl_Original'
        )
        BEGIN
            DROP STATISTICS {QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("AppMovilFolioControl")}.{QuoteSqlIdentifier("UX_AppMovilFolioControl_Original")};
        END;

        CREATE UNIQUE INDEX UX_AppMovilFolioControl_Original
        ON {PosTable("AppMovilFolioControl")}(FolioAppOriginal);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = @objFolioControl
          AND name = N'UX_AppMovilFolioControl_Control'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM {PosTable("AppMovilFolioControl")}
        GROUP BY FolioControl
        HAVING COUNT(*) > 1
    )
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM sys.stats
            WHERE object_id = @objFolioControl
              AND name = N'UX_AppMovilFolioControl_Control'
        )
        BEGIN
            DROP STATISTICS {QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("AppMovilFolioControl")}.{QuoteSqlIdentifier("UX_AppMovilFolioControl_Control")};
        END;

        CREATE UNIQUE INDEX UX_AppMovilFolioControl_Control
        ON {PosTable("AppMovilFolioControl")}(FolioControl);
    END;
END;

IF @objRegistro IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = @objRegistro
          AND name = N'UX_AppMovilRegistro_FolioApp'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM {PosTable("AppMovilRegistro")}
        GROUP BY folio_app
        HAVING COUNT(*) > 1
    )
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM sys.stats
            WHERE object_id = @objRegistro
              AND name = N'UX_AppMovilRegistro_FolioApp'
        )
        BEGIN
            DROP STATISTICS {QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("AppMovilRegistro")}.{QuoteSqlIdentifier("UX_AppMovilRegistro_FolioApp")};
        END;

        CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioApp
        ON {PosTable("AppMovilRegistro")}(folio_app);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = @objRegistro
          AND name = N'UX_AppMovilRegistro_FolioOriginal'
    )
    AND NOT EXISTS (
        SELECT 1
        FROM {PosTable("AppMovilRegistro")}
        WHERE folio_app_original <> ''
        GROUP BY folio_app_original
        HAVING COUNT(*) > 1
    )
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM sys.stats
            WHERE object_id = @objRegistro
              AND name = N'UX_AppMovilRegistro_FolioOriginal'
        )
        BEGIN
            DROP STATISTICS {QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("AppMovilRegistro")}.{QuoteSqlIdentifier("UX_AppMovilRegistro_FolioOriginal")};
        END;

        CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioOriginal
        ON {PosTable("AppMovilRegistro")}(folio_app_original)
        WHERE folio_app_original <> '';
    END;
END;

IF OBJECT_ID('{PosTable("AppMovilRegistroGafetes").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
    CREATE TABLE {PosTable("AppMovilRegistroGafetes")} (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistroGafetes PRIMARY KEY,
        FolioApp NVARCHAR(60) NOT NULL,
        IdCatalogo INT NULL,
        FolioGafete NVARCHAR(20) NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilRegistroGafetes_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppMovilRegistroGafetes_FolioApp_Gafete ON {PosTable("AppMovilRegistroGafetes")}(FolioApp, FolioGafete);
    CREATE INDEX IX_AppMovilRegistroGafetes_Taxista ON {PosTable("AppMovilRegistroGafetes")}(IdCatalogo, FolioGafete);
END;

IF OBJECT_ID('{PosTable("AppMovilFoliosBloqueados").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
    CREATE TABLE {PosTable("AppMovilFoliosBloqueados")} (
        Folio NVARCHAR(60) NOT NULL CONSTRAINT PK_AppMovilFoliosBloqueados PRIMARY KEY,
        Motivo NVARCHAR(200) NOT NULL DEFAULT '',
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilFoliosBloqueados_Fecha DEFAULT SYSUTCDATETIME()
    );
END;";
            try
            {
                await ExecuteNonQueryAsync(command);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1913 || ex.Number == 2714 || ex.Number == 2705 || ex.Number == 3729)
            {
                // Index/table/constraint already exists — concurrent call already created it, safe to ignore.
            }

            var originalDatabase = connection.Database;
            connection.ChangeDatabase(_externalDatabaseName);
            try
            {
                await using (var dropView = connection.CreateCommand())
                {
                    dropView.CommandText = $@"
IF OBJECT_ID(N'{QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("vw_AppMovilRegistrosViajes")}', N'V') IS NOT NULL
    DROP VIEW {QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("vw_AppMovilRegistrosViajes")};";
                    await ExecuteNonQueryAsync(dropView);
                }

                await using var createView = connection.CreateCommand();
                createView.CommandText = $@"
CREATE VIEW {QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("vw_AppMovilRegistrosViajes")} AS
    SELECT
        folio_app AS id_registro,
        folio_app_original AS id_registro_original,
        id_catalogo,
        folio_gafete,
        vendedor_nombre AS nombre_taxista,
        telefono_taxista,
        telefono_contacto,
        nacionalidad,
        placas,
        modelo_vehiculo,
        unidad,
        hotel,
        origen,
        sitio,
        destino,
        pax AS numero_personas,
        tipo_operacion AS tipo_servicio,
        total AS costo_viaje,
        CASE WHEN tarjeta > 0 THEN 'Tarjeta' ELSE 'Efectivo' END AS metodo_pago,
        notas,
        detalle_json AS datos_escaneo,
        usuario_movil,
        fecha_operacion AS fecha_registro
    FROM {QuoteSqlIdentifier(_externalSchemaName)}.{QuoteSqlIdentifier("AppMovilRegistro")};";
                await ExecuteNonQueryAsync(createView);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(originalDatabase))
                    connection.ChangeDatabase(originalDatabase);
            }
        }

        private static async Task<bool> IsAppMovilFolioBlockedAsync(DbConnection connection, string? folio)
        {
            var normalized = Normalize(folio);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF OBJECT_ID('{PosTable("AppMovilFoliosBloqueados").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
    SELECT 0;
END
ELSE
BEGIN
    SELECT CASE WHEN EXISTS (
        SELECT 1
        FROM {PosTable("AppMovilFoliosBloqueados")}
        WHERE Folio = @folio
           OR (ISNUMERIC(Folio) = 1 AND ISNUMERIC(@folio) = 1 AND CONVERT(bigint, Folio) = CONVERT(bigint, @folio))
    ) THEN 1 ELSE 0 END;
END";
            AddParameter(command, "@folio", normalized);
            return ToDecimal(await ExecuteScalarAsync(command)) > 0m;
        }

        private static async Task<string?> UpsertAppMovilRegistroAsync(DbConnection connection, PosOperacionRowViewModel row)
        {
            if (string.IsNullOrWhiteSpace(row.FolioControl))
                return null;

            await EnsureRelacionesTicketTaxistaTableAsync(connection);
            var folioOriginal = SafeText(row.FolioControl, 60);
            var folioControl = await EnsureAppMovilFolioControlAsync(connection, folioOriginal);
            var fecha = ParseAppDate(row.Hora) ?? DateTime.Now;
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF EXISTS (
    SELECT 1
    FROM {PosTable("AppMovilRegistro")} WITH (UPDLOCK, HOLDLOCK)
    WHERE folio_app = @folioControl
       OR folio_app_original = @folioOriginal
)
BEGIN
    UPDATE {PosTable("AppMovilRegistro")}
    SET folio_app_original = @folioOriginal,
        id_catalogo = @catalogId,
        folio_gafete = COALESCE(
            NULLIF((
                SELECT TOP (1) r.Gafete
                FROM {PosTable("RelacionTicketTaxista")} r
                WHERE r.FolioApp = @folioControl
                   OR r.FolioApp = @folioOriginal
            ), ''),
            @gafete
        ),
        folio_pos = @ticket,
        fecha_operacion = @fecha,
        vendedor_nombre = @taxista,
        telefono_taxista = @telefono,
        telefono_contacto = @telefonoContacto,
        nacionalidad = @nacionalidad,
        placas = @placas,
        unidad = @unidad,
        modelo_vehiculo = @modelo,
        hotel = @hotel,
        origen = @origen,
        sitio = @sitio,
        destino = @destino,
        pax = @pax,
        tipo_operacion = @tipo,
        total = COALESCE((
            SELECT TOP (1) NULLIF(r.Dejada, 0)
            FROM {PosTable("RelacionTicketTaxista")} r
            WHERE r.FolioApp = @folioControl
               OR r.FolioApp = @folioOriginal
        ), @total),
        efectivo = @efectivo,
        tarjeta = @tarjeta,
        usuario_movil = @usuarioMovil,
        notas = @notas,
        estado_sync = 'SINCRONIZADO',
        payout_status = @payoutStatus,
        payout_date = @payoutDate,
        payout_user = @payoutUser,
        payout_ticket = @payoutTicket,
        estado_pago_dejada = @payoutStatus,
        fecha_pago_dejada = @payoutDate,
        usuario_pago_dejada = @payoutUser,
        ticket_pago_dejada = @payoutTicket
    WHERE folio_app = @folioControl
       OR folio_app_original = @folioOriginal;
END
ELSE
BEGIN
    INSERT INTO {PosTable("AppMovilRegistro")}
    (folio_app, folio_app_original, id_catalogo, folio_gafete, folio_pos, fecha_operacion, vendedor_nombre, telefono_taxista, telefono_contacto, nacionalidad, placas, modelo_vehiculo, unidad, hotel, origen, sitio, destino, pax, tipo_operacion, total, efectivo, tarjeta, usuario_movil, notas, detalle_json, estado_sync, payout_status, payout_date, payout_user, payout_ticket, estado_pago_dejada, fecha_pago_dejada, usuario_pago_dejada, ticket_pago_dejada)
    VALUES
    (@folioControl, @folioOriginal, @catalogId, @gafete, @ticket, @fecha, @taxista, @telefono, @telefonoContacto, @nacionalidad, @placas, @modelo, @unidad, @hotel, @origen, @sitio, @destino, @pax, @tipo, @total, @efectivo, @tarjeta, @usuarioMovil, @notas, '', 'SINCRONIZADO', @payoutStatus, @payoutDate, @payoutUser, @payoutTicket, @payoutStatus, @payoutDate, @payoutUser, @payoutTicket);
END;";
            AddParameter(command, "@folioControl", folioControl);
            AddParameter(command, "@folioOriginal", folioOriginal);
            AddParameter(command, "@catalogId", row.CatalogId);
            AddParameter(command, "@gafete", SafeText(row.Gafete, 300));
            AddParameter(command, "@ticket", SafeText(row.Ticket, 100));
            AddParameter(command, "@fecha", fecha);
            AddParameter(command, "@taxista", SafeText(row.Vendedor, 150));
            AddParameter(command, "@telefono", SafeText(row.Telefono, 30));
            AddParameter(command, "@telefonoContacto", SafeText(row.TelefonoContacto, 30));
            AddParameter(command, "@nacionalidad", SafeText(row.Nacionalidad, 120));
            AddParameter(command, "@placas", SafeText(row.Placas, 50));
            AddParameter(command, "@modelo", SafeText(row.ModeloVehiculo, 150));
            AddParameter(command, "@unidad", SafeText(row.Unidad, 50));
            AddParameter(command, "@hotel", SafeText(row.Hotel, 200));
            AddParameter(command, "@origen", SafeText(row.Origen, 150));
            AddParameter(command, "@sitio", SafeText(row.Sitio, 150));
            AddParameter(command, "@destino", SafeText(row.Destino, 150));
            AddParameter(command, "@pax", row.Pax);
            AddParameter(command, "@tipo", SafeText(row.TipoOperacion, 80));
            AddParameter(command, "@total", row.Total);
            AddParameter(command, "@efectivo", row.Efectivo);
            AddParameter(command, "@tarjeta", row.Tarjeta);
            AddParameter(command, "@usuarioMovil", SafeText(FirstText(row.UsuarioOrigen, "hostinger"), 80));
            AddParameter(command, "@notas", row.Notas ?? string.Empty);
            AddParameter(command, "@payoutStatus", SafeText(FirstText(row.PayoutStatus, "pendiente"), 30));
            AddParameter(command, "@payoutDate", ParseAppDate(row.PayoutDate));
            AddParameter(command, "@payoutUser", SafeText(row.PayoutUser, 80));
            AddParameter(command, "@payoutTicket", SafeText(row.PayoutTicket, 80));
            await ExecuteNonQueryAsync(command);
            return folioControl;
        }

        private static async Task<string> EnsureAppMovilFolioControlAsync(DbConnection connection, string folioOriginal)
        {
            await using (var existing = connection.CreateCommand())
            {
                existing.CommandText = $@"
SELECT TOP (1) FolioControl
FROM {PosTable("AppMovilFolioControl")}
WHERE FolioAppOriginal = @folio";
                AddParameter(existing, "@folio", folioOriginal);
                var result = await ExecuteScalarAsync(existing);
                var text = Convert.ToString(result, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            await using var next = connection.CreateCommand();
            next.CommandText = $@"
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRANSACTION;

DECLARE @existente NVARCHAR(60);
SELECT TOP (1) @existente = FolioControl
FROM {PosTable("AppMovilFolioControl")} WITH (UPDLOCK, HOLDLOCK)
WHERE FolioAppOriginal = @folioOriginal;

IF NULLIF(LTRIM(RTRIM(@existente)), '') IS NOT NULL
BEGIN
    COMMIT TRANSACTION;
    SELECT @existente;
    RETURN;
END;

DECLARE @isWeb BIT = CASE WHEN UPPER(@folioOriginal) LIKE 'WEB%' THEN 1 ELSE 0 END;

IF @isWeb = 0
   AND NOT EXISTS (SELECT 1 FROM {PosTable("AppMovilFolioControl")} WITH (UPDLOCK, HOLDLOCK) WHERE FolioControl = @folioOriginal OR FolioAppOriginal = @folioOriginal)
   AND NOT EXISTS (SELECT 1 FROM {PosTable("AppMovilRegistro")} WITH (UPDLOCK, HOLDLOCK) WHERE folio_app = @folioOriginal OR folio_app_original = @folioOriginal)
   AND NOT EXISTS (SELECT 1 FROM {PosTable("AppMovilFoliosBloqueados")} WITH (UPDLOCK, HOLDLOCK) WHERE Folio = @folioOriginal)
BEGIN
    INSERT INTO {PosTable("AppMovilFolioControl")} (FolioAppOriginal, FolioControl)
    VALUES (@folioOriginal, @folioOriginal);

    COMMIT TRANSACTION;
    SELECT @folioOriginal;
    RETURN;
END;

DECLARE @ultimo INT;
SELECT @ultimo = ISNULL(MAX(
    CASE
        WHEN ISNUMERIC(
        CASE
            WHEN FolioTexto LIKE 'WEB%' THEN SUBSTRING(FolioTexto, 4, 20)
            WHEN FolioTexto LIKE 'AP%' THEN SUBSTRING(FolioTexto, 3, 20)
            ELSE FolioTexto
        END) = 1
        THEN CAST(
        CASE
            WHEN FolioTexto LIKE 'WEB%' THEN SUBSTRING(FolioTexto, 4, 20)
            WHEN FolioTexto LIKE 'AP%' THEN SUBSTRING(FolioTexto, 3, 20)
            ELSE FolioTexto
        END AS INT)
        ELSE 0
    END), 0)
FROM
(
    SELECT FolioControl AS FolioTexto
    FROM {PosTable("AppMovilFolioControl")} WITH (UPDLOCK, HOLDLOCK)
    UNION ALL
    SELECT FolioAppOriginal
    FROM {PosTable("AppMovilFolioControl")} WITH (UPDLOCK, HOLDLOCK)
    UNION ALL
    SELECT folio_app
    FROM {PosTable("AppMovilRegistro")} WITH (UPDLOCK, HOLDLOCK)
    UNION ALL
    SELECT folio_app_original
    FROM {PosTable("AppMovilRegistro")} WITH (UPDLOCK, HOLDLOCK)
) Folios
WHERE NULLIF(LTRIM(RTRIM(FolioTexto)), '') IS NOT NULL;

IF @isWeb = 1 AND @ultimo < 100
    SET @ultimo = 100;

DECLARE @folioControl NVARCHAR(60) = RIGHT('0000' + CONVERT(NVARCHAR(20), @ultimo + 1), 4);

WHILE EXISTS (SELECT 1 FROM {PosTable("AppMovilFolioControl")} WHERE FolioControl = @folioControl OR FolioAppOriginal = @folioControl)
   OR EXISTS (SELECT 1 FROM {PosTable("AppMovilRegistro")} WHERE folio_app = @folioControl OR folio_app_original = @folioControl)
   OR EXISTS (SELECT 1 FROM {PosTable("AppMovilFoliosBloqueados")} WHERE Folio = @folioControl)
BEGIN
    SET @ultimo = @ultimo + 1;
    SET @folioControl = RIGHT('0000' + CONVERT(NVARCHAR(20), @ultimo + 1), 4);
END

INSERT INTO {PosTable("AppMovilFolioControl")} (FolioAppOriginal, FolioControl)
VALUES (@folioOriginal, @folioControl);

COMMIT TRANSACTION;
SELECT @folioControl;";
            AddParameter(next, "@folioOriginal", folioOriginal);
            var generated = await ExecuteScalarAsync(next);
            return Convert.ToString(generated, CultureInfo.InvariantCulture)?.Trim() ?? folioOriginal;
        }

        private static async Task ReplaceAppMovilRegistroGafetesAsync(
            DbConnection connection,
            string folioControl,
            int? taxistaId,
            IReadOnlyCollection<string> gafetes)
        {
            await using (var delete = connection.CreateCommand())
            {
                delete.CommandText = $@"DELETE FROM {PosTable("AppMovilRegistroGafetes")} WHERE FolioApp = @folioApp;";
                AddParameter(delete, "@folioApp", folioControl);
                await ExecuteNonQueryAsync(delete);
            }

            if (gafetes.Count == 0)
                return;

            await using var insert = connection.CreateCommand();
            insert.CommandText = $@"
INSERT INTO {PosTable("AppMovilRegistroGafetes")} (FolioApp, IdCatalogo, FolioGafete)
SELECT @folioApp, @catalogId, @gafete
WHERE NOT EXISTS (
    SELECT 1
    FROM {PosTable("AppMovilRegistroGafetes")}
    WHERE FolioApp = @folioApp
      AND UPPER(LTRIM(RTRIM(FolioGafete))) = UPPER(LTRIM(RTRIM(@gafete)))
);";
            AddParameter(insert, "@folioApp", folioControl);
            AddParameter(insert, "@catalogId", taxistaId.HasValue && taxistaId.Value > 0 ? taxistaId.Value : null);
            var gafeteParam = insert.CreateParameter();
            gafeteParam.ParameterName = "@gafete";
            insert.Parameters.Add(gafeteParam);

            foreach (var gafete in gafetes)
            {
                gafeteParam.Value = SafeText(gafete, 20);
                await ExecuteNonQueryAsync(insert);
            }
        }

        private static decimal ResolveRegistroAppDejada(ControlTaxiWeb.Data.PosTransporte transporte)
        {
            var dejada = ToDecimal(transporte.Dejada);
            if (dejada > 0m)
                return dejada;

            var minimo = ToDecimal(transporte.Minimo);
            if (minimo > 0m)
                return minimo;

            return ToDecimal(transporte.Maximo);
        }

        private static async Task<bool> PosTableExistsAsync(DbConnection connection, string tableName)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END;";
            AddParameter(command, "@tableName", $"{_externalDatabaseName}.{_externalSchemaName}.{tableName}");
            var result = await ExecuteScalarAsync(command);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }

        private static async Task<bool> AppTableExistsAsync(DbConnection connection, string tableName)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END;";
            AddParameter(command, "@tableName", $"{_appDatabaseName}.{_appSchemaName}.{tableName}");
            var result = await ExecuteScalarAsync(command);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }

        private static async Task<bool> PosColumnExistsAsync(DbConnection connection, string tableName, string columnName)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END;";
            AddParameter(command, "@tableName", $"{_externalDatabaseName}.{_externalSchemaName}.{tableName}");
            AddParameter(command, "@columnName", columnName);
            var result = await ExecuteScalarAsync(command);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }

        private static async Task<bool> AppColumnExistsAsync(DbConnection connection, string tableName, string columnName)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END;";
            AddParameter(command, "@tableName", $"{_appDatabaseName}.{_appSchemaName}.{tableName}");
            AddParameter(command, "@columnName", columnName);
            var result = await ExecuteScalarAsync(command);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }

        private static DateTime? ParseAppDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var clean = value.Trim();
            var formats = new[]
            {
                "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy HH:mm",
                "dd/MM/yyyy",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm",
                "yyyy-MM-dd'T'HH:mm:ss",
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
                "yyyy-MM-dd'T'HH:mm:ssK",
                "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
                "yyyy-MM-dd"
            };

            if (DateTimeOffset.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactOffset)
                || DateTimeOffset.TryParseExact(clean, formats, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out exactOffset)
                || DateTimeOffset.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out exactOffset)
                || DateTimeOffset.TryParse(clean, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out exactOffset))
            {
                return exactOffset.DateTime;
            }

            return DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)
                || DateTime.TryParseExact(clean, formats, CultureInfo.GetCultureInfo("es-MX"), DateTimeStyles.None, out exact)
                || DateTime.TryParse(clean, CultureInfo.CurrentCulture, DateTimeStyles.None, out exact)
                || DateTime.TryParse(clean, CultureInfo.InvariantCulture, DateTimeStyles.None, out exact)
                ? exact
                : null;
        }
    }
}
