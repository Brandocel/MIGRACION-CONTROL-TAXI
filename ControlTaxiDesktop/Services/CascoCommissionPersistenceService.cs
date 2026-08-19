using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

public sealed record CascoGeneratedCommissionPreview(
    string BranchCode,
    string SqlServer,
    string Database,
    string FolioOriginal,
    int FolioOperacion,
    string TicketPos,
    string Taxista,
    string Gafete,
    string Transporte,
    string FormaPago,
    decimal VentaCompuadmo,
    decimal VentaJoyeria,
    decimal VentaTotal,
    int? ReglaId,
    string NombreRegla,
    decimal? PorcentajeAplicado,
    decimal ComisionDeportiva,
    decimal ComisionCalculada,
    string Estatus,
    DateTime FechaCalculo,
    DateTime? FechaPago,
    string Usuario,
    string Observaciones,
    bool RuleFound,
    string Detail);

public sealed record CascoGeneratedCommissionSaveResult(
    bool Saved,
    int RowsAffected,
    CascoGeneratedCommissionPreview Preview);

public sealed record CascoCommissionPaymentResult(
    string FolioOriginal,
    string TicketPago,
    decimal Comision,
    DateTime FechaPago,
    string Usuario,
    string Taxista,
    string Gafete,
    string Transporte,
    string TicketPos,
    string Estatus);

public sealed class CascoCommissionPersistenceService
{
    private readonly CascoCommissionRuleService _ruleService = new();

    public async Task<CascoGeneratedCommissionPreview> PreviewCommissionAsync(
        BranchConfiguration branch,
        string sqlPassword,
        string folioOriginal,
        string? user = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCasco(branch);

        var cleanFolio = (folioOriginal ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanFolio))
            throw new InvalidOperationException("Se requiere folio original para preparar la comision de Casco.");

        var operationNumber = ParseOperationNumber(cleanFolio);
        if (operationNumber <= 0)
            throw new InvalidOperationException("El folio original no es valido para preparar la comision de Casco.");

        var commission = await _ruleService.PreviewAsync(
            branch,
            sqlPassword,
            cleanFolio,
            cancellationToken: cancellationToken);

        var relation = (await CascoOperationsDataService.LoadRelationsAsync(
                branch,
                branch.Code,
                sqlPassword,
                cleanFolio,
                null,
                null,
                cancellationToken))
            .FirstOrDefault(item =>
                string.Equals(item.OperationFolio, cleanFolio, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.AppFolio, cleanFolio, StringComparison.OrdinalIgnoreCase));

        var status = commission.RuleFound && commission.CommissionAmount > 0m
            ? "PENDIENTE"
            : commission.Detail.Contains("ambigua", StringComparison.OrdinalIgnoreCase)
            ? "REGLA AMBIGUA"
            : "SIN COMISION";

        return new CascoGeneratedCommissionPreview(
            branch.Code,
            branch.SqlServer,
            branch.Database,
            cleanFolio,
            operationNumber,
            relation?.PosFolio?.Trim() ?? string.Empty,
            relation?.Driver?.Trim() ?? string.Empty,
            relation?.Badge?.Trim() ?? string.Empty,
            relation?.TransportType?.Trim() ?? string.Empty,
            relation?.PaymentMethod?.Trim() ?? string.Empty,
            commission.VentaCompuadmo,
            commission.VentaJoyeria,
            commission.VentaTotal,
            commission.Rule?.Id,
            commission.Rule?.ReglaNombre ?? string.Empty,
            ResolveAppliedPercentage(commission.Rule),
            commission.SportAmount,
            commission.CommissionAmount,
            status,
            DateTime.Now,
            ParseOptionalDate(relation?.PayoutDate),
            (user ?? string.Empty).Trim(),
            relation?.Notes?.Trim() ?? string.Empty,
            commission.RuleFound,
            commission.Detail);
    }

    public async Task<CascoGeneratedCommissionSaveResult> SaveCommissionAsync(
        BranchConfiguration branch,
        string sqlPassword,
        CascoGeneratedCommissionPreview preview,
        CancellationToken cancellationToken = default)
    {
        EnsureCasco(branch);
        if (!string.Equals(preview.BranchCode, "CV", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La persistencia de comisiones solo acepta BranchCode CV.");
        if (preview.FolioOperacion <= 0)
            throw new InvalidOperationException("La persistencia de comisiones requiere FolioOperacion mayor a cero.");
        if (preview.VentaTotal <= 0m
            || preview.ComisionCalculada <= 0m
            || preview.ReglaId is null or <= 0
            || !preview.RuleFound
            || !string.Equals(preview.Estatus, "PENDIENTE", StringComparison.OrdinalIgnoreCase))
        {
            return new CascoGeneratedCommissionSaveResult(false, 0, preview);
        }

        await using var connection = await OpenAsync(branch, sqlPassword, cancellationToken);
        await EnsureStorageAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var generatedPreview = preview with
        {
            Estatus = "PENDIENTE",
            FechaCalculo = DateTime.Now
        };

        const string sql = """
            DECLARE @RowsAffected int = 0;

            IF EXISTS (
                SELECT 1
                FROM dbo.ControlTaxiComisionesGeneradas
                    WITH (UPDLOCK, HOLDLOCK, INDEX(UX_ControlTaxiComisionesGeneradas_Operacion_Regla))
                WHERE BranchCode = @BranchCode
                  AND FolioOriginal = @FolioOriginal
                  AND FolioOperacion = @FolioOperacion
                  AND ReglaId = @ReglaId
                  AND Estatus IN (N'AUTORIZADA', N'PAGADA', N'CANCELADA')
            )
            BEGIN
                SELECT @RowsAffected;
            END
            ELSE IF EXISTS (
                SELECT 1
                FROM dbo.ControlTaxiComisionesGeneradas
                    WITH (UPDLOCK, HOLDLOCK, INDEX(UX_ControlTaxiComisionesGeneradas_Operacion_Regla))
                WHERE BranchCode = @BranchCode
                  AND FolioOriginal = @FolioOriginal
                  AND FolioOperacion = @FolioOperacion
                  AND ReglaId = @ReglaId
            )
            BEGIN
                UPDATE dbo.ControlTaxiComisionesGeneradas
                SET TicketPos = @TicketPos,
                    Taxista = @Taxista,
                    Gafete = @Gafete,
                    Transporte = @Transporte,
                    FormaPago = @FormaPago,
                    VentaCompuadmo = @VentaCompuadmo,
                    VentaJoyeria = @VentaJoyeria,
                    VentaTotal = @VentaTotal,
                    NombreRegla = @NombreRegla,
                    PorcentajeAplicado = @PorcentajeAplicado,
                    ComisionDeportiva = @ComisionDeportiva,
                    ComisionCalculada = @ComisionCalculada,
                    Estatus = @Estatus,
                    FechaCalculo = @FechaCalculo,
                    FechaPago = @FechaPago,
                    Usuario = @Usuario,
                    Observaciones = @Observaciones,
                    FechaModificacion = SYSDATETIME(),
                    UsuarioModificacion = @Usuario,
                    NumeroRecalculos = NumeroRecalculos + 1
                WHERE BranchCode = @BranchCode
                  AND FolioOriginal = @FolioOriginal
                  AND FolioOperacion = @FolioOperacion
                  AND ReglaId = @ReglaId;

                SET @RowsAffected = @@ROWCOUNT;
                SELECT @RowsAffected;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ControlTaxiComisionesGeneradas
                (
                    BranchCode,
                    FolioOriginal,
                    FolioOperacion,
                    TicketPos,
                    Taxista,
                    Gafete,
                    Transporte,
                    FormaPago,
                    VentaCompuadmo,
                    VentaJoyeria,
                    VentaTotal,
                    ReglaId,
                    NombreRegla,
                    PorcentajeAplicado,
                    ComisionDeportiva,
                    ComisionCalculada,
                    Estatus,
                    FechaCalculo,
                    FechaPago,
                    Usuario,
                    Observaciones,
                    FechaCreacion,
                    UsuarioCreacion,
                    FechaModificacion,
                    UsuarioModificacion,
                    NumeroRecalculos
                )
                VALUES
                (
                    @BranchCode,
                    @FolioOriginal,
                    @FolioOperacion,
                    @TicketPos,
                    @Taxista,
                    @Gafete,
                    @Transporte,
                    @FormaPago,
                    @VentaCompuadmo,
                    @VentaJoyeria,
                    @VentaTotal,
                    @ReglaId,
                    @NombreRegla,
                    @PorcentajeAplicado,
                    @ComisionDeportiva,
                    @ComisionCalculada,
                    @Estatus,
                    @FechaCalculo,
                    @FechaPago,
                    @Usuario,
                    @Observaciones,
                    SYSDATETIME(),
                    @Usuario,
                    NULL,
                    N'',
                    0
                );

                SET @RowsAffected = @@ROWCOUNT;
                SELECT @RowsAffected;
            END;
            """;

        try
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            AddParameters(command, generatedPreview);
            var rows = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            await transaction.CommitAsync(cancellationToken);
            return new CascoGeneratedCommissionSaveResult(rows > 0, rows, generatedPreview);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            var rows = await UpdateExistingAfterDuplicateAsync(connection, generatedPreview, cancellationToken);
            return new CascoGeneratedCommissionSaveResult(rows > 0, rows, generatedPreview);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CascoCommissionPaymentResult> PayCommissionAsync(
        BranchConfiguration branch,
        string sqlPassword,
        string folioOriginal,
        string user,
        CancellationToken cancellationToken = default)
    {
        EnsureCasco(branch);

        var cleanFolio = (folioOriginal ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(cleanFolio))
            throw new InvalidOperationException("Selecciona una comision valida para pagar.");

        if (ParseOperationNumber(cleanFolio) <= 0)
            throw new InvalidOperationException("El folio de operacion no es valido para pagar comision.");
        var operationNumber = ParseOperationNumber(cleanFolio);

        await using var connection = await OpenAsync(branch, sqlPassword, cancellationToken);
        await EnsureStorageAsync(connection, cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            const string selectSql = """
                SELECT
                    COALESCE(SUM(ComisionCalculada), 0) AS Comision,
                    MAX(COALESCE(Taxista, N'')) AS Taxista,
                    MAX(COALESCE(Gafete, N'')) AS Gafete,
                    MAX(COALESCE(Transporte, N'')) AS Transporte,
                    MAX(COALESCE(TicketPos, N'')) AS TicketPos
                FROM dbo.ControlTaxiComisionesGeneradas WITH (UPDLOCK, HOLDLOCK)
                WHERE BranchCode = N'CV'
                  AND (
                      FolioOriginal = @FolioOriginal
                      OR FolioOperacion = @FolioOperacion
                      OR TRY_CONVERT(INT, FolioOriginal) = @FolioOperacion
                  )
                  AND Estatus = N'PENDIENTE'
                  AND ComisionCalculada > 0;
                """;

            await using var select = new SqlCommand(selectSql, connection, transaction);
            select.Parameters.Add("@FolioOriginal", SqlDbType.NVarChar, 120).Value = cleanFolio;
            select.Parameters.Add("@FolioOperacion", SqlDbType.Int).Value = operationNumber;
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("No se encontro una comision pendiente para pagar.");

            var commission = reader.IsDBNull(0) ? 0m : Convert.ToDecimal(reader.GetValue(0), CultureInfo.InvariantCulture);
            var taxista = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var gafete = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var transporte = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
            var ticketPos = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
            await reader.DisposeAsync();

            if (commission <= 0m)
            {
                var alreadyPaid = await HasPaidCommissionAsync(connection, transaction, cleanFolio, cancellationToken);
                throw new InvalidOperationException(alreadyPaid
                    ? "La comision ya esta pagada."
                    : "No hay comision pendiente con importe mayor a cero.");
            }

            var paidAt = DateTime.Now;
            var ticket = BuildCommissionPaymentTicket(cleanFolio);
            var cleanUser = string.IsNullOrWhiteSpace(user) ? "desktop" : user.Trim();

            const string updateSql = """
                UPDATE dbo.ControlTaxiComisionesGeneradas
                SET PagoComision = ComisionCalculada,
                    Estatus = N'PAGADA',
                    FechaPagoComision = @FechaPago,
                    UsuarioPagoComision = @Usuario,
                    TicketPagoComision = @TicketPago,
                    FechaModificacion = SYSDATETIME(),
                    UsuarioModificacion = @Usuario
                WHERE BranchCode = N'CV'
                  AND (
                      FolioOriginal = @FolioOriginal
                      OR FolioOperacion = @FolioOperacion
                      OR TRY_CONVERT(INT, FolioOriginal) = @FolioOperacion
                  )
                  AND Estatus = N'PENDIENTE'
                  AND ComisionCalculada > 0;
                """;

            await using var update = new SqlCommand(updateSql, connection, transaction);
            update.Parameters.Add("@FechaPago", SqlDbType.DateTime2).Value = paidAt;
            update.Parameters.Add("@Usuario", SqlDbType.NVarChar, 160).Value = cleanUser;
            update.Parameters.Add("@TicketPago", SqlDbType.NVarChar, 160).Value = ticket;
            update.Parameters.Add("@FolioOriginal", SqlDbType.NVarChar, 120).Value = cleanFolio;
            update.Parameters.Add("@FolioOperacion", SqlDbType.Int).Value = operationNumber;
            var affected = await update.ExecuteNonQueryAsync(cancellationToken);
            if (affected <= 0)
                throw new InvalidOperationException("No se pudo marcar la comision como pagada.");

            await transaction.CommitAsync(cancellationToken);
            return new CascoCommissionPaymentResult(cleanFolio, ticket, commission, paidAt, cleanUser, taxista, gafete, transporte, ticketPos, "PAGADA");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<SqlConnection> OpenAsync(BranchConfiguration branch, string sqlPassword, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sqlPassword))
            throw new InvalidOperationException("Se requiere contrasena SQL para persistir comisiones de Casco.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = branch.SqlServer,
            InitialCatalog = branch.Database,
            UserID = string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser,
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 30
        };

        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static void AddParameters(SqlCommand command, CascoGeneratedCommissionPreview preview)
    {
        command.Parameters.Add("@BranchCode", SqlDbType.NVarChar, 10).Value = preview.BranchCode;
        command.Parameters.Add("@FolioOriginal", SqlDbType.NVarChar, 120).Value = preview.FolioOriginal;
        command.Parameters.Add("@FolioOperacion", SqlDbType.Int).Value = preview.FolioOperacion;
        command.Parameters.Add("@TicketPos", SqlDbType.NVarChar, 160).Value = preview.TicketPos;
        command.Parameters.Add("@Taxista", SqlDbType.NVarChar, 200).Value = preview.Taxista;
        command.Parameters.Add("@Gafete", SqlDbType.NVarChar, 50).Value = preview.Gafete;
        command.Parameters.Add("@Transporte", SqlDbType.NVarChar, 120).Value = preview.Transporte;
        command.Parameters.Add("@FormaPago", SqlDbType.NVarChar, 120).Value = preview.FormaPago;
        command.Parameters.Add("@VentaCompuadmo", SqlDbType.Decimal).Value = preview.VentaCompuadmo;
        command.Parameters.Add("@VentaJoyeria", SqlDbType.Decimal).Value = preview.VentaJoyeria;
        command.Parameters.Add("@VentaTotal", SqlDbType.Decimal).Value = preview.VentaTotal;
        command.Parameters.Add("@ReglaId", SqlDbType.Int).Value = preview.ReglaId is null ? DBNull.Value : preview.ReglaId.Value;
        command.Parameters.Add("@NombreRegla", SqlDbType.NVarChar, 200).Value = preview.NombreRegla;
        command.Parameters.Add("@PorcentajeAplicado", SqlDbType.Decimal).Value = preview.PorcentajeAplicado is null ? DBNull.Value : preview.PorcentajeAplicado.Value;
        command.Parameters.Add("@ComisionDeportiva", SqlDbType.Decimal).Value = preview.ComisionDeportiva;
        command.Parameters.Add("@ComisionCalculada", SqlDbType.Decimal).Value = preview.ComisionCalculada;
        command.Parameters.Add("@Estatus", SqlDbType.NVarChar, 40).Value = preview.Estatus;
        command.Parameters.Add("@FechaCalculo", SqlDbType.DateTime2).Value = preview.FechaCalculo;
        command.Parameters.Add("@FechaPago", SqlDbType.DateTime2).Value = preview.FechaPago is null ? DBNull.Value : preview.FechaPago.Value;
        command.Parameters.Add("@Usuario", SqlDbType.NVarChar, 160).Value = preview.Usuario;
        command.Parameters.Add("@Observaciones", SqlDbType.NVarChar, 1000).Value = preview.Observaciones;

        foreach (SqlParameter parameter in command.Parameters)
        {
            if (parameter.SqlDbType is SqlDbType.Decimal)
            {
                parameter.Precision = 18;
                parameter.Scale = 2;
            }
        }

        command.Parameters["@PorcentajeAplicado"].Precision = 9;
        command.Parameters["@PorcentajeAplicado"].Scale = 6;
    }

    private static async Task<int> UpdateExistingAfterDuplicateAsync(
        SqlConnection connection,
        CascoGeneratedCommissionPreview preview,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE dbo.ControlTaxiComisionesGeneradas
            SET TicketPos = @TicketPos,
                Taxista = @Taxista,
                Gafete = @Gafete,
                Transporte = @Transporte,
                FormaPago = @FormaPago,
                VentaCompuadmo = @VentaCompuadmo,
                VentaJoyeria = @VentaJoyeria,
                VentaTotal = @VentaTotal,
                NombreRegla = @NombreRegla,
                PorcentajeAplicado = @PorcentajeAplicado,
                ComisionDeportiva = @ComisionDeportiva,
                ComisionCalculada = @ComisionCalculada,
                FechaCalculo = @FechaCalculo,
                FechaPago = COALESCE(FechaPago, @FechaPago),
                Usuario = @Usuario,
                Observaciones = @Observaciones,
                FechaModificacion = SYSDATETIME(),
                UsuarioModificacion = @Usuario,
                NumeroRecalculos = NumeroRecalculos + 1
            WHERE BranchCode = @BranchCode
              AND FolioOriginal = @FolioOriginal
              AND FolioOperacion = @FolioOperacion
              AND ReglaId = @ReglaId
              AND Estatus = N'PENDIENTE';
            """;

        await using var command = new SqlCommand(sql, connection);
        AddParameters(command, preview);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureCasco(BranchConfiguration branch)
    {
        if (!CascoCommissionRuleService.IsCascoCommissionEnvironment(branch, branch.Code))
            throw new InvalidOperationException("La persistencia de comisiones de Casco solo esta habilitada para configuraciones CV validas.");
    }

    private static async Task EnsureStorageAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.ControlTaxiComisionesGeneradas
                (
                    Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ControlTaxiComisionesGeneradas PRIMARY KEY CLUSTERED,
                    BranchCode NVARCHAR(10) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_BranchCode DEFAULT (N'CV'),
                    FolioOriginal NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FolioOriginal DEFAULT (N''),
                    FolioOperacion INT NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FolioOperacion DEFAULT ((0)),
                    TicketPos NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_TicketPos DEFAULT (N''),
                    Taxista NVARCHAR(200) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Taxista DEFAULT (N''),
                    Gafete NVARCHAR(50) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Gafete DEFAULT (N''),
                    Transporte NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Transporte DEFAULT (N''),
                    FormaPago NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FormaPago DEFAULT (N''),
                    VentaCompuadmo DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaCompuadmo DEFAULT ((0)),
                    VentaJoyeria DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaJoyeria DEFAULT ((0)),
                    VentaTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaTotal DEFAULT ((0)),
                    ReglaId INT NOT NULL,
                    NombreRegla NVARCHAR(200) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_NombreRegla DEFAULT (N''),
                    PorcentajeAplicado DECIMAL(9,6) NULL,
                    ComisionDeportiva DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_ComisionDeportiva DEFAULT ((0)),
                    ComisionCalculada DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_ComisionCalculada DEFAULT ((0)),
                    Estatus NVARCHAR(40) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Estatus DEFAULT (N'PENDIENTE'),
                    FechaCalculo DATETIME2(0) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FechaCalculo DEFAULT (SYSDATETIME()),
                    FechaPago DATETIME2(0) NULL,
                    Usuario NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Usuario DEFAULT (N''),
                    Observaciones NVARCHAR(1000) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Observaciones DEFAULT (N'')
                );
            END;

            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'TicketPos') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD TicketPos NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_TicketPos_Add DEFAULT (N'');
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'Transporte') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD Transporte NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Transporte_Add DEFAULT (N'');
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FormaPago') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FormaPago NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FormaPago_Add DEFAULT (N'');
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'ComisionDeportiva') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD ComisionDeportiva DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_ComisionDeportiva_Add DEFAULT ((0));
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FechaPago') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FechaPago DATETIME2(0) NULL;
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FechaCreacion') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FechaCreacion DATETIME2(0) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FechaCreacion_Add DEFAULT (SYSDATETIME());
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FechaModificacion') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FechaModificacion DATETIME2(0) NULL;
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'UsuarioCreacion') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD UsuarioCreacion NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_UsuarioCreacion_Add DEFAULT (N'');
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'UsuarioModificacion') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD UsuarioModificacion NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_UsuarioModificacion_Add DEFAULT (N'');
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'NumeroRecalculos') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD NumeroRecalculos INT NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_NumeroRecalculos_Add DEFAULT ((0));
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'PagoComision') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD PagoComision DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_PagoComision_Add DEFAULT ((0));
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FechaPagoComision') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FechaPagoComision DATETIME2(0) NULL;
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'UsuarioPagoComision') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD UsuarioPagoComision NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_UsuarioPagoComision_Add DEFAULT (N'');
            IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'TicketPagoComision') IS NULL
                ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD TicketPagoComision NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_TicketPagoComision_Add DEFAULT (N'');

            IF EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_ControlTaxiComisionesGeneradas_CV_Folio'
                  AND object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
            )
            BEGIN
                DROP INDEX UX_ControlTaxiComisionesGeneradas_CV_Folio ON dbo.ControlTaxiComisionesGeneradas;
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE name = N'UX_ControlTaxiComisionesGeneradas_Operacion_Regla'
                  AND object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
            )
            BEGIN
                CREATE UNIQUE INDEX UX_ControlTaxiComisionesGeneradas_Operacion_Regla
                ON dbo.ControlTaxiComisionesGeneradas (BranchCode, FolioOriginal, FolioOperacion, ReglaId);
            END;
            """;

        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasPaidCommissionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string folioOriginal,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM dbo.ControlTaxiComisionesGeneradas
            WHERE BranchCode = N'CV'
              AND (
                  FolioOriginal = @FolioOriginal
                  OR FolioOperacion = @FolioOperacion
                  OR TRY_CONVERT(INT, FolioOriginal) = @FolioOperacion
              )
              AND Estatus = N'PAGADA';
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@FolioOriginal", SqlDbType.NVarChar, 120).Value = folioOriginal;
        command.Parameters.Add("@FolioOperacion", SqlDbType.Int).Value = ParseOperationNumber(folioOriginal);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    private static int ParseOperationNumber(string folioOriginal)
    {
        var normalized = folioOriginal.Trim().TrimStart('0');
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        return int.TryParse(normalized, out var value) ? value : 0;
    }

    private static string BuildCommissionPaymentTicket(string folioOriginal)
    {
        var normalized = string.IsNullOrWhiteSpace(folioOriginal) ? "0000" : folioOriginal.Trim();
        return ("TC" + normalized + DateTime.Now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture)).Trim();
    }

    private static decimal? ResolveAppliedPercentage(CascoCommissionRule? rule)
    {
        if (rule is null)
            return null;

        return rule.ComisionTaxista
            ?? rule.ComisionVendedor
            ?? rule.ComisionAgencia;
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        return DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed)
            ? parsed
            : null;
    }
}
