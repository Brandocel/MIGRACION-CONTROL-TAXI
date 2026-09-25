using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Trae las reglas de comision de Casco Viejo desde SQL Server (dbo.ControlTaxiComisiones) a la
/// base local SQLite la primera vez que la maquina las necesita.
///
/// POR QUE EXISTE
/// El calculo de Casco paso de leer SQL Server a leer SQLite. Sin este puente, una maquina que se
/// actualiza se queda sin ninguna regla y TODAS las comisiones de Casco saldrian con el respaldo
/// del 10 %: se perderia el trabajo del 19/09/2026 (las 14 reglas confirmadas con el negocio).
///
/// COMO TRADUCE
/// En SQL Server cada proveedor tiene DOS renglones, uno con tarjeta y otro sin tarjeta, que solo
/// se diferencian en si quitan el 19 % del banco. En SQLite una regla guarda las dos retenciones
/// a la vez, asi que los dos renglones se juntan en uno solo:
///   - porcentaje de comision = agencia + taxista + vendedor (el motor anterior los sumaba);
///   - retencion de efectivo   = la del renglon SIN tarjeta (MAJESTIC es el unico que la trae);
///   - retencion de tarjeta y AMEX = la del renglon CON tarjeta;
///   - dejada y gasto = las banderas AplicaDejada / AplicaGasto, que son iguales en ambos.
///
/// Solo importa lo que falta: si alguien ya capturo a mano la regla de un proveedor, no se toca.
/// </summary>
public static class CascoCommissionRuleImporter
{
    /// <summary>
    /// Vigencia con la que entran las reglas importadas. Se usa una fecha vieja a proposito: las
    /// reglas de SQL Server no tenian vigencia y aplicaban a todo el historico, asi que con una
    /// fecha reciente los viajes anteriores se irian al respaldo del 10 % sin motivo.
    /// </summary>
    private static readonly DateTime ImportedFrom = new(2000, 1, 1);

    private const decimal BankRetentionPercent = 19m;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _alreadyChecked;

    public sealed record ImportOutcome(bool Ran, int Imported, string Detail);

    /// <summary>
    /// Importa una sola vez por corrida del programa. Si algo falla (SQL Server apagado, tabla que
    /// no existe) NO se interrumpe nada: el calculo sigue con lo que haya en SQLite.
    /// </summary>
    public static async Task<ImportOutcome> EnsureImportedAsync(
        CommissionSettingsRepository settings,
        BranchConfiguration branch,
        string sqlPassword,
        string user,
        CancellationToken cancellationToken = default)
    {
        if (_alreadyChecked) return new ImportOutcome(false, 0, "Ya se reviso en esta corrida.");
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_alreadyChecked) return new ImportOutcome(false, 0, "Ya se reviso en esta corrida.");
            var outcome = await ImportAsync(settings, branch, sqlPassword, user, cancellationToken);
            _alreadyChecked = true;
            return outcome;
        }
        catch (Exception ex)
        {
            _alreadyChecked = true;
            return new ImportOutcome(false, 0, "No se pudieron traer las reglas de Casco desde SQL Server: " + ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Repite la importacion aunque ya se haya hecho en esta corrida.</summary>
    public static async Task<ImportOutcome> ImportAsync(
        CommissionSettingsRepository settings,
        BranchConfiguration branch,
        string sqlPassword,
        string user,
        CancellationToken cancellationToken = default)
    {
        if (await settings.HasCascoTransportRulesAsync())
            return new ImportOutcome(false, 0, "Casco ya tiene reglas capturadas en esta maquina.");

        var rules = await ReadSqlServerRulesAsync(branch, sqlPassword, cancellationToken);
        if (rules.Count == 0)
            return new ImportOutcome(false, 0, "SQL Server no tiene reglas activas de Casco que traer.");

        var translated = Translate(rules);
        var imported = await settings.ImportCascoTransportRulesAsync(translated, user,
            $"Importacion automatica de {translated.Count} reglas de Casco desde dbo.ControlTaxiComisiones.");
        return new ImportOutcome(true, imported, $"Se trajeron {imported} reglas de Casco desde SQL Server.");
    }

    /// <summary>Junta los renglones con y sin tarjeta de cada proveedor en una sola regla.</summary>
    public static IReadOnlyList<CommissionSettingsRule> Translate(IReadOnlyList<CascoCommissionRule> rules)
    {
        var result = new List<CommissionSettingsRule>();
        foreach (var group in rules.GroupBy(rule => rule.Proveedor.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var withCard = group.FirstOrDefault(rule => rule.ConTarjeta);
            var withoutCard = group.FirstOrDefault(rule => !rule.ConTarjeta);
            var reference = withCard ?? withoutCard ?? group.First();

            var percent = ((reference.ComisionAgencia ?? 0m)
                + (reference.ComisionTaxista ?? 0m)
                + (reference.ComisionVendedor ?? 0m)) * 100m;

            var cashRetention = (withoutCard ?? reference).AplicaRetencion ? BankRetentionPercent : 0m;
            var cardRetention = (withCard ?? reference).AplicaRetencion ? BankRetentionPercent : 0m;

            var notes = string.Join(" ", new[]
            {
                $"Importada de dbo.ControlTaxiComisiones ({reference.ReglaNombre}).",
                percent <= 0m ? "Sin porcentaje de comision en el Excel del negocio." : string.Empty,
                group.Any(rule => rule.ComisionDeportiva is > 0m)
                    ? "OJO: la regla original traia comision deportiva, que este calculo no aplica."
                    : string.Empty
            }.Where(text => !string.IsNullOrWhiteSpace(text)));

            result.Add(new CommissionSettingsRule(
                Id: 0,
                Category: "TRANSPORTE",
                Code: reference.Proveedor.Trim(),
                Name: reference.Proveedor.Trim(),
                CommissionPercent: decimal.Round(percent, 4),
                CashRetentionPercent: cashRetention,
                CardRetentionPercent: cardRetention,
                AmexRetentionPercent: cardRetention,
                PaymentKind: string.Empty,
                AppliesPayout: reference.AplicaDejada,
                AppliesExpense: reference.AplicaGasto,
                Active: true,
                EffectiveFrom: ImportedFrom,
                EffectiveTo: null,
                UpdatedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                UpdatedBy: "IMPORTACION_CV",
                Notes: notes)
            {
                Branch = "CV"
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<CascoCommissionRule>> ReadSqlServerRulesAsync(
        BranchConfiguration branch,
        string sqlPassword,
        CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = branch.SqlServer,
            InitialCatalog = branch.Database,
            UserID = string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser.Trim(),
            Password = sqlPassword,
            TrustServerCertificate = true,
            Encrypt = false,
            ConnectTimeout = 15
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var exists = new SqlCommand("SELECT OBJECT_ID(N'dbo.ControlTaxiComisiones', N'U');", connection))
        {
            var objectId = await exists.ExecuteScalarAsync(cancellationToken);
            if (objectId is null || objectId == DBNull.Value)
                return Array.Empty<CascoCommissionRule>();
        }

        // Las banderas se agregaron el 19/09/2026 y pueden no existir en una base vieja. COL_LENGTH
        // en un lote aparte porque SQL Server compila todo el lote antes de ejecutarlo.
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        await using (var columns = new SqlCommand("""
            SELECT
                CASE WHEN COL_LENGTH(N'dbo.ControlTaxiComisiones', N'AplicaRetencion')   IS NULL THEN 0 ELSE 1 END,
                CASE WHEN COL_LENGTH(N'dbo.ControlTaxiComisiones', N'AplicaDejada')      IS NULL THEN 0 ELSE 1 END,
                CASE WHEN COL_LENGTH(N'dbo.ControlTaxiComisiones', N'AplicaGasto')       IS NULL THEN 0 ELSE 1 END;
            """, connection))
        await using (var reader = await columns.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                flags["AplicaRetencion"] = reader.GetInt32(0) == 1;
                flags["AplicaDejada"] = reader.GetInt32(1) == 1;
                flags["AplicaGasto"] = reader.GetInt32(2) == 1;
            }
        }

        string Flag(string column, string fallback) => flags.TryGetValue(column, out var exists) && exists
            ? $"CAST(COALESCE({column}, {fallback}) AS bit)"
            : $"CAST({fallback} AS bit)";

        var sql = $"""
            SELECT Id, BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, VentaMaxima,
                   ComisionAgencia, ComisionTaxista, ComisionVendedor, ComisionDeportiva,
                   Activo, ReglaNombre, RequiereValidacion,
                   {Flag("AplicaRetencion", "ConTarjeta")} AS AplicaRetencion,
                   {Flag("AplicaDejada", "1")} AS AplicaDejada,
                   {Flag("AplicaGasto", "1")} AS AplicaGasto
            FROM dbo.ControlTaxiComisiones
            WHERE BranchCode = N'CV' AND Activo = 1
            ORDER BY Id;
            """;

        await using var command = new SqlCommand(sql, connection);
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<CascoCommissionRule>();
        while (await rows.ReadAsync(cancellationToken))
        {
            result.Add(new CascoCommissionRule(
                rows.GetInt32(0),
                rows.GetString(1),
                rows.GetString(2),
                rows.GetString(3),
                rows.GetBoolean(4),
                rows.GetDecimal(5),
                rows.IsDBNull(6) ? null : rows.GetDecimal(6),
                rows.IsDBNull(7) ? null : rows.GetDecimal(7),
                rows.IsDBNull(8) ? null : rows.GetDecimal(8),
                rows.IsDBNull(9) ? null : rows.GetDecimal(9),
                rows.IsDBNull(10) ? null : rows.GetDecimal(10),
                rows.GetBoolean(11),
                rows.GetString(12),
                rows.GetBoolean(13),
                rows.GetBoolean(14),
                rows.GetBoolean(15),
                rows.GetBoolean(16)));
        }

        return result;
    }
}
