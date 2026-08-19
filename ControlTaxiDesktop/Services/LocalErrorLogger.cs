using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Services;

public sealed class LocalErrorLogger(LocalDatabase database)
{
    public async Task LogAsync(string user, string module, string action, Exception exception)
    {
        try
        {
            await using var connection = database.Open();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO LocalAuditoria
                  (Fecha,Usuario,Modulo,Accion,Referencia,Importe,Detalles,IdRegistro,Descripcion,BaseDatos,Tabla,Exito,Equipo,Aplicacion)
                VALUES
                  ($date,$user,$module,$action,'',NULL,$details,'',$description,'SQLite','LocalAuditoria',0,$machine,'ControlTaxiDesktop');
                """;
            command.Parameters.AddWithValue("$date", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$user", string.IsNullOrWhiteSpace(user) ? "Sistema" : user.Trim());
            command.Parameters.AddWithValue("$module", string.IsNullOrWhiteSpace(module) ? "Desktop" : module.Trim());
            command.Parameters.AddWithValue("$action", string.IsNullOrWhiteSpace(action) ? "Error" : action.Trim());
            command.Parameters.AddWithValue("$details", exception.ToString());
            command.Parameters.AddWithValue("$description", exception.Message);
            command.Parameters.AddWithValue("$machine", Environment.MachineName);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // Evita que un fallo al registrar el error oculte el mensaje principal al usuario.
        }
    }
}
