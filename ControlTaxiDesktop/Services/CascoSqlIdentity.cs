using System;

namespace ControlTaxiDesktop.Services;

/// <summary>
/// Con que usuario de SQL Server se conecta Casco Viejo.
///
/// En produccion es <c>sa</c> y asi seguira: la credencial cifrada de la maquina guarda ese
/// usuario y lo publica en <c>CASCO_SQL_USER</c>. El nombre estaba escrito a mano en varios
/// servicios, asi que no se podia probar el sistema contra otro servidor con un usuario distinto
/// (por ejemplo uno de solo lectura en una copia local): entraba bien al programa y despues
/// fallaba al consultar con "Login failed for user".
///
/// Orden: la variable de entorno, luego lo que diga la sucursal en branches.production.json, y
/// al final <c>sa</c>.
/// </summary>
public static class CascoSqlIdentity
{
    public const string DefaultUser = "sa";

    public static string ResolveUser(BranchConfiguration? branch = null)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("CASCO_SQL_USER");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();

        return string.IsNullOrWhiteSpace(branch?.SqlUser) ? DefaultUser : branch!.SqlUser.Trim();
    }
}
