using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using YourNamespace.Features.Gafetes.Contracts;
using YourNamespace.Features.Gafetes.Models;

namespace YourNamespace.Features.Gafetes.Services;

/// <summary>
/// Servicio para sincronizar gafetes de Casco con la base de datos.
/// </summary>
public interface IGafetesSyncService
{
    /// <summary>
    /// Sincroniza gafetes en la base de datos con lógica de UPSERT.
    /// </summary>
    Task<GafeteSyncResult> SyncGafetesAsync(
        string branchCode,
        List<GafeteDto> gafetes,
        CancellationToken cancellationToken = default);
}

public class GafetesSyncService : IGafetesSyncService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<GafetesSyncService> _logger;

    public GafetesSyncService(IDbConnectionFactory connectionFactory, ILogger<GafetesSyncService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<GafeteSyncResult> SyncGafetesAsync(
        string branchCode,
        List<GafeteDto> gafetes,
        CancellationToken cancellationToken = default)
    {
        var result = new GafeteSyncResult();

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var gafete in gafetes)
                {
                    var syncResult = await UpsertGafeteAsync(
                        connection,
                        transaction,
                        branchCode,
                        gafete,
                        cancellationToken);

                    switch (syncResult)
                    {
                        case UpsertAction.Inserted:
                            result.Inserted++;
                            break;
                        case UpsertAction.Updated:
                            result.Updated++;
                            break;
                        case UpsertAction.Unchanged:
                            result.Unchanged++;
                            break;
                    }
                }

                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation(
                    "Sincronización de gafetes completada: {Inserted} insertados, {Updated} actualizados, {Unchanged} sin cambios",
                    result.Inserted, result.Updated, result.Unchanged);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Error durante la sincronización de gafetes, transacción revertida");
                result.Errors.Add($"Error de transacción: {ex.Message}");
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en SyncGafetesAsync");
            result.Errors.Add($"Error en sincronización: {ex.Message}");
        }

        return result;
    }

    private async Task<UpsertAction> UpsertGafeteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string branchCode,
        GafeteDto gafeteDto,
        CancellationToken cancellationToken)
    {
        var createdAt = DateTime.Parse(gafeteDto.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var now = DateTime.UtcNow;

        // Primero, intentar obtener el gafete existente
        var existingGafete = await GetExistingGafeteAsync(
            connection,
            transaction,
            branchCode,
            gafeteDto.BadgeId,
            gafeteDto.Cycle,
            cancellationToken);

        if (existingGafete == null)
        {
            // INSERT
            return await InsertGafeteAsync(
                connection,
                transaction,
                branchCode,
                gafeteDto,
                createdAt,
                now,
                cancellationToken);
        }

        // Verificar si cambió algo
        if (existingGafete.Status == gafeteDto.Status &&
            existingGafete.Barcode == gafeteDto.Barcode &&
            existingGafete.TaxistaId == gafeteDto.TaxistaId &&
            existingGafete.TaxistaName == gafeteDto.TaxistaName)
        {
            // Nada cambió
            return UpsertAction.Unchanged;
        }

        // UPDATE (solo si cambió el status u otros datos)
        return await UpdateGafeteAsync(
            connection,
            transaction,
            existingGafete.Id,
            gafeteDto,
            now,
            cancellationToken);
    }

    private async Task<Gafete?> GetExistingGafeteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string branchCode,
        string badgeId,
        int cycle,
        CancellationToken cancellationToken)
    {
        const string query = @"
            SELECT TOP 1
                Id, BranchCode, BadgeId, Barcode, Status, Cycle, TaxistaId, TaxistaName, CreatedAt, UpdatedAt
            FROM casco_gafetes
            WHERE BranchCode = @BranchCode
              AND BadgeId = @BadgeId
              AND Cycle = @Cycle
              AND IsActive = 1
        ";

        using var command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@BranchCode", branchCode);
        command.Parameters.AddWithValue("@BadgeId", badgeId);
        command.Parameters.AddWithValue("@Cycle", cycle);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new Gafete
            {
                Id = reader.GetInt32(0),
                BranchCode = reader.GetString(1),
                BadgeId = reader.GetString(2),
                Barcode = reader.GetString(3),
                Status = reader.GetString(4),
                Cycle = reader.GetInt32(5),
                TaxistaId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                TaxistaName = reader.IsDBNull(7) ? null : reader.GetString(7),
                CreatedAt = reader.GetDateTime(8),
                UpdatedAt = reader.GetDateTime(9)
            };
        }

        return null;
    }

    private async Task<UpsertAction> InsertGafeteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string branchCode,
        GafeteDto gafeteDto,
        DateTime createdAt,
        DateTime now,
        CancellationToken cancellationToken)
    {
        const string insertQuery = @"
            INSERT INTO casco_gafetes
                (BranchCode, BadgeId, Barcode, Status, Cycle, TaxistaId, TaxistaName, CreatedAt, UpdatedAt, IsActive)
            VALUES
                (@BranchCode, @BadgeId, @Barcode, @Status, @Cycle, @TaxistaId, @TaxistaName, @CreatedAt, @UpdatedAt, 1)
        ";

        using var command = new SqlCommand(insertQuery, connection, transaction);
        command.Parameters.AddWithValue("@BranchCode", branchCode);
        command.Parameters.AddWithValue("@BadgeId", gafeteDto.BadgeId);
        command.Parameters.AddWithValue("@Barcode", gafeteDto.Barcode);
        command.Parameters.AddWithValue("@Status", gafeteDto.Status);
        command.Parameters.AddWithValue("@Cycle", gafeteDto.Cycle);
        command.Parameters.AddWithValue("@TaxistaId", gafeteDto.TaxistaId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@TaxistaName", gafeteDto.TaxistaName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAt", createdAt);
        command.Parameters.AddWithValue("@UpdatedAt", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation(
            "Gafete insertado: BranchCode={BranchCode}, BadgeId={BadgeId}, Cycle={Cycle}",
            branchCode, gafeteDto.BadgeId, gafeteDto.Cycle);

        return UpsertAction.Inserted;
    }

    private async Task<UpsertAction> UpdateGafeteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int gafeteId,
        GafeteDto gafeteDto,
        DateTime now,
        CancellationToken cancellationToken)
    {
        const string updateQuery = @"
            UPDATE casco_gafetes
            SET
                Barcode = @Barcode,
                Status = @Status,
                TaxistaId = @TaxistaId,
                TaxistaName = @TaxistaName,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id
        ";

        using var command = new SqlCommand(updateQuery, connection, transaction);
        command.Parameters.AddWithValue("@Id", gafeteId);
        command.Parameters.AddWithValue("@Barcode", gafeteDto.Barcode);
        command.Parameters.AddWithValue("@Status", gafeteDto.Status);
        command.Parameters.AddWithValue("@TaxistaId", gafeteDto.TaxistaId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@TaxistaName", gafeteDto.TaxistaName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@UpdatedAt", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation(
            "Gafete actualizado: Id={GafeteId}",
            gafeteId);

        return UpsertAction.Updated;
    }

    private enum UpsertAction
    {
        Inserted,
        Updated,
        Unchanged
    }
}

/// <summary>
/// Factory para crear conexiones SQL.
/// </summary>
public interface IDbConnectionFactory
{
    SqlConnection CreateConnection();
}

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public SqlConnection CreateConnection() => new(_connectionString);
}
