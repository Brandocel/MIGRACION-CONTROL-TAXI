using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        private async Task<bool> IsCorteClosedAsync(DateTime fecha)
        {
            try
            {
                return await _appContext.Cortes
                    .AsNoTracking()
                    .AnyAsync(x => x.Fecha == fecha.Date && x.Estatus == "Cerrado");
            }
            catch
            {
                return false;
            }
        }

        private static Task EnsureUsuariosTableAsync(DbConnection connection) =>
            Task.CompletedTask;

        private static async Task EnsurePosOperacionBeneficiariosTableAsync(DbConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF OBJECT_ID('{AppTable("PosOperacionBeneficiarios").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
    CREATE TABLE {AppTable("PosOperacionBeneficiarios")}
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FolioOperacion NVARCHAR(30) NOT NULL,
        StaffClave NVARCHAR(20) NOT NULL CONSTRAINT DF_posbenef_staffclave DEFAULT '',
        StaffNombre NVARCHAR(100) NOT NULL CONSTRAINT DF_posbenef_staff DEFAULT '',
        TransporteTipo NVARCHAR(10) NOT NULL CONSTRAINT DF_posbenef_trans DEFAULT '',
        GuiaMatricula INT NOT NULL CONSTRAINT DF_posbenef_guia DEFAULT 0,
        TaxistaId BIGINT NOT NULL CONSTRAINT DF_posbenef_taxista DEFAULT 0,
        TaxistaNombre NVARCHAR(80) NOT NULL CONSTRAINT DF_posbenef_taxistanombre DEFAULT '',
        Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_posbenef_usuario DEFAULT '',
        Fecha DATETIME2 NOT NULL CONSTRAINT DF_posbenef_fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_posbenef_folio ON {AppTable("PosOperacionBeneficiarios")}(FolioOperacion);
END;";
            await command.ExecuteNonQueryAsync();
        }

        private static async Task EnsureRelacionesTicketTaxistaTableAsync(DbConnection connection)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $@"
IF OBJECT_ID('{PosTable("RelacionTicketTaxista").Replace("[", string.Empty).Replace("]", string.Empty)}', 'U') IS NULL
BEGIN
        CREATE TABLE {PosTable("RelacionTicketTaxista")}
        (
            Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            FolioApp NVARCHAR(60) NOT NULL,
            FolioOperacion NVARCHAR(60) NOT NULL,
            FolioPos NVARCHAR(120) NOT NULL CONSTRAINT DF_reltaxi_foliopos DEFAULT '',
            Gafete NVARCHAR(300) NOT NULL CONSTRAINT DF_reltaxi_gafete DEFAULT '',
            TaxistaId BIGINT NOT NULL,
            TaxistaNombre NVARCHAR(150) NOT NULL CONSTRAINT DF_reltaxi_taxista DEFAULT '',
            Vendedor NVARCHAR(150) NOT NULL CONSTRAINT DF_reltaxi_vendedor DEFAULT '',
            TransporteTipo NVARCHAR(20) NOT NULL CONSTRAINT DF_reltaxi_transporte DEFAULT '',
            Dejada DECIMAL(18,2) NULL,
            Observaciones NVARCHAR(300) NOT NULL CONSTRAINT DF_reltaxi_obs DEFAULT '',
            Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_reltaxi_usuario DEFAULT '',
            FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_reltaxi_fecha DEFAULT SYSUTCDATETIME(),
            FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_reltaxi_actualiza DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_reltaxi_folioapp ON {PosTable("RelacionTicketTaxista")}(FolioApp);
    CREATE INDEX IX_reltaxi_foliooperacion ON {PosTable("RelacionTicketTaxista")}(FolioOperacion);
    CREATE INDEX IX_reltaxi_taxista ON {PosTable("RelacionTicketTaxista")}(TaxistaId);
END
ELSE
BEGIN
    IF COL_LENGTH('{PosTable("RelacionTicketTaxista").Replace("[", string.Empty).Replace("]", string.Empty)}', 'Gafete') IS NOT NULL
    BEGIN
        DECLARE @gafeteMaxLength INT;
        SELECT @gafeteMaxLength = c.max_length
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID('{PosTable("RelacionTicketTaxista").Replace("[", string.Empty).Replace("]", string.Empty)}')
          AND c.name = 'Gafete';

        IF COALESCE(@gafeteMaxLength, 0) > 0 AND @gafeteMaxLength < 600
            ALTER TABLE {PosTable("RelacionTicketTaxista")} ALTER COLUMN Gafete NVARCHAR(300) NOT NULL;
    END;

    IF COL_LENGTH('{PosTable("RelacionTicketTaxista").Replace("[", string.Empty).Replace("]", string.Empty)}', 'Vendedor') IS NULL
        ALTER TABLE {PosTable("RelacionTicketTaxista")} ADD Vendedor NVARCHAR(150) NOT NULL CONSTRAINT DF_reltaxi_vendedor_existing DEFAULT '';

    IF COL_LENGTH('{PosTable("RelacionTicketTaxista").Replace("[", string.Empty).Replace("]", string.Empty)}', 'Dejada') IS NULL
        ALTER TABLE {PosTable("RelacionTicketTaxista")} ADD Dejada DECIMAL(18,2) NULL;
  END;";
            await command.ExecuteNonQueryAsync();
        }
    }
}
