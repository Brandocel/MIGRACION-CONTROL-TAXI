using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ControlTaxiWeb.Data;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        private async Task InsertPosAuditoriaAsync(
            DbConnection? connection,
            DbTransaction? transaction,
            string usuario,
            string modulo,
            string accion,
            string idRegistro,
            string descripcion,
            string? detalleJson = null,
            bool exito = true)
        {
            try
            {
                var usuarioFinal = string.IsNullOrWhiteSpace(usuario) ? "WEB" : usuario.Trim();
                var detalleFinal = string.IsNullOrWhiteSpace(detalleJson) ? "{}" : detalleJson;
                var audit = new PosAuditoriaMovimiento
                {
                    FechaUtc = DateTime.UtcNow,
                    Usuario = SafeText(usuarioFinal, 80),
                    Modulo = SafeText(modulo, 80),
                    Accion = SafeText(accion, 80),
                    IdRegistro = SafeText(idRegistro, 120),
                    Descripcion = SafeText(descripcion, 700),
                    BaseDatos = SafeText(GetAuditText(detalleFinal, "BaseDatos", _externalDatabaseName), 80),
                    Tabla = SafeText(GetAuditText(detalleFinal, "Tabla"), 120),
                    FolioApp = SafeText(GetAuditText(detalleFinal, "FolioApp"), 60),
                    FolioOperacion = SafeText(FirstText(GetAuditText(detalleFinal, "FolioOperacion"), idRegistro), 60),
                    FolioPos = SafeText(GetAuditText(detalleFinal, "FolioPos"), 120),
                    Taxista = SafeText(FirstText(GetAuditText(detalleFinal, "TaxistaNombre"), GetAuditText(detalleFinal, "Taxista")), 150),
                    Gafete = SafeText(GetAuditText(detalleFinal, "Gafete"), 30),
                    Importe = GetAuditDecimal(detalleFinal, "Importe", "Total", "TotalVenta"),
                    Exito = exito,
                    Equipo = SafeText(Environment.MachineName, 128),
                    Aplicacion = "ControlTaxiWeb",
                    DetalleJson = detalleFinal
                };

                var auditConnection = connection ?? _posContext.Database.GetDbConnection();
                if (auditConnection.State != System.Data.ConnectionState.Open)
                    await auditConnection.OpenAsync();

                await EnsureAuditoriaMovimientoTableAsync(auditConnection, transaction);
                await using var command = auditConnection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $@"
INSERT INTO {PosTable("AuditoriaMovimiento")}
    (FechaUtc, Usuario, Modulo, Accion, IdRegistro, Descripcion, BaseDatos, Tabla, FolioApp, FolioOperacion, FolioPos, Taxista, Gafete, Importe, Exito, Equipo, Aplicacion, DetalleJson)
VALUES
    (@fechaUtc, @usuario, @modulo, @accion, @idRegistro, @descripcion, @baseDatos, @tabla, @folioApp, @folioOperacion, @folioPos, @taxista, @gafete, @importe, @exito, @equipo, @aplicacion, @detalleJson);";
                AddParameter(command, "@fechaUtc", audit.FechaUtc);
                AddParameter(command, "@usuario", audit.Usuario);
                AddParameter(command, "@modulo", audit.Modulo);
                AddParameter(command, "@accion", audit.Accion);
                AddParameter(command, "@idRegistro", audit.IdRegistro);
                AddParameter(command, "@descripcion", audit.Descripcion);
                AddParameter(command, "@baseDatos", audit.BaseDatos);
                AddParameter(command, "@tabla", audit.Tabla);
                AddParameter(command, "@folioApp", audit.FolioApp);
                AddParameter(command, "@folioOperacion", audit.FolioOperacion);
                AddParameter(command, "@folioPos", audit.FolioPos);
                AddParameter(command, "@taxista", audit.Taxista);
                AddParameter(command, "@gafete", audit.Gafete);
                AddParameter(command, "@importe", audit.Importe.HasValue ? audit.Importe.Value : DBNull.Value);
                AddParameter(command, "@exito", audit.Exito);
                AddParameter(command, "@equipo", audit.Equipo);
                AddParameter(command, "@aplicacion", audit.Aplicacion);
                AddParameter(command, "@detalleJson", audit.DetalleJson);
                await ExecuteNonQueryAsync(command);
            }
            catch
            {
                // La auditoria no debe impedir guardar la operacion principal.
            }
        }

        private static async Task EnsureAuditoriaMovimientoTableAsync(DbConnection connection, DbTransaction? transaction)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
IF OBJECT_ID('{PosTable("AuditoriaMovimiento")}', 'U') IS NULL
BEGIN
    CREATE TABLE {PosTable("AuditoriaMovimiento")}
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditoriaMovimiento PRIMARY KEY,
        FechaUtc DATETIME2 NOT NULL CONSTRAINT DF_AuditoriaMovimiento_FechaUtc DEFAULT SYSUTCDATETIME(),
        FechaLocal AS CONVERT(DATETIME2, DATEADD(HOUR, -6, FechaUtc)) PERSISTED,
        Usuario NVARCHAR(80) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Usuario DEFAULT '',
        Modulo NVARCHAR(80) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Modulo DEFAULT '',
        Accion NVARCHAR(80) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Accion DEFAULT '',
        IdRegistro NVARCHAR(120) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_IdRegistro DEFAULT '',
        Descripcion NVARCHAR(700) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Descripcion DEFAULT '',
        BaseDatos NVARCHAR(80) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Base DEFAULT '',
        Tabla NVARCHAR(120) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Tabla DEFAULT '',
        FolioApp NVARCHAR(60) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_FolioApp DEFAULT '',
        FolioOperacion NVARCHAR(60) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_FolioOperacion DEFAULT '',
        FolioPos NVARCHAR(120) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_FolioPos DEFAULT '',
        Taxista NVARCHAR(150) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Taxista DEFAULT '',
        Gafete NVARCHAR(30) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Gafete DEFAULT '',
        Importe DECIMAL(18,2) NULL,
        Exito BIT NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Exito DEFAULT 1,
        Equipo NVARCHAR(128) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Equipo DEFAULT HOST_NAME(),
        Aplicacion NVARCHAR(128) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_App DEFAULT APP_NAME(),
        DetalleJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AuditoriaMovimiento_Detalle DEFAULT '{{}}'
    );

    CREATE INDEX IX_AuditoriaMovimiento_FechaUtc ON {PosTable("AuditoriaMovimiento")}(FechaUtc DESC);
    CREATE INDEX IX_AuditoriaMovimiento_Modulo ON {PosTable("AuditoriaMovimiento")}(Modulo, Accion, FechaUtc DESC);
    CREATE INDEX IX_AuditoriaMovimiento_Folios ON {PosTable("AuditoriaMovimiento")}(FolioApp, FolioOperacion, FolioPos);
END;";
            await ExecuteNonQueryAsync(command);
        }

        private Task InsertPosAuditoriaAsync(
            string usuario,
            string modulo,
            string accion,
            string idRegistro,
            string descripcion,
            string? detalleJson = null,
            bool exito = true) =>
            InsertPosAuditoriaAsync(null, null, usuario, modulo, accion, idRegistro, descripcion, detalleJson, exito);

        private static string BuildAuditJson(params (string Key, object? Value)[] values) =>
            JsonSerializer.Serialize(values
                .Select(x => (x.Key, x.Value))
                .Concat(new (string Key, object? Value)[]
                {
                    ("FechaLocal", (object?)DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                    ("Origen", "WEB POS HOKA"),
                    ("Servidor", Environment.MachineName)
                })
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.OrdinalIgnoreCase));

        private static string GetAuditText(string json, string key, string fallback = "")
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return document.RootElement.TryGetProperty(key, out var property)
                    ? Convert.ToString(property.ToString(), CultureInfo.InvariantCulture)?.Trim() ?? fallback
                    : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static decimal? GetAuditDecimal(string json, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = GetAuditText(json, key);
                if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }

            return null;
        }
    }
}
