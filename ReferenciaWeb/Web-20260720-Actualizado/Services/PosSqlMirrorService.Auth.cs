using System.Globalization;
using System.Security.Cryptography;
using ControlTaxiWeb.Data;
using ControlTaxiWeb.Models.PosTriton;
using Microsoft.EntityFrameworkCore;

namespace ControlTaxiWeb.Services
{
    public partial class PosSqlMirrorService
    {
        public async Task<bool> ValidateUserAsync(string usuario, string password)
        {
            var normalizedUser = Normalize(usuario);
            if (normalizedUser == null || string.IsNullOrWhiteSpace(password))
                return false;

            var user = await _appContext.Usuarios
                .FirstOrDefaultAsync(x => x.Usuario != null
                    && x.Usuario.ToUpper() == normalizedUser.ToUpper()
                    && x.Estatus == "Activo");
            if (user == null)
                return false;

            var valid = VerifyPassword(password.Trim(), user.PasswordHash);
            if (valid && !IsModernPasswordHash(user.PasswordHash))
            {
                user.PasswordHash = HashPassword(password.Trim());
                await _appContext.SaveChangesAsync();
            }

            return valid;
        }

        public async Task<PosUsuariosViewModel?> TryGetUsuariosAsync(string? usuario = null)
        {
            var rows = await _appContext.Usuarios
                .AsNoTracking()
                .OrderBy(x => x.Usuario)
                .ToListAsync();

            var usuarios = new List<PosUsuarioRowViewModel>();
            foreach (var row in rows)
            {
                var clave = row.Usuario ?? string.Empty;
                usuarios.Add(new PosUsuarioRowViewModel
                {
                    Usuario = clave,
                    Rol = row.Rol ?? string.Empty,
                    Estatus = row.Estatus ?? string.Empty,
                    FechaAlta = row.FechaAlta.HasValue
                        ? row.FechaAlta.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                        : string.Empty,
                    Permisos = (await GetUserPermissionsAsync(clave)).ToList()
                });
            }

            var seleccionado = usuarios.FirstOrDefault(x => string.Equals(x.Usuario, Normalize(usuario), StringComparison.OrdinalIgnoreCase));
            return new PosUsuariosViewModel
            {
                UsuarioEdicion = seleccionado?.Usuario ?? string.Empty,
                RolEdicion = seleccionado?.Rol ?? "Cajero",
                EstatusEdicion = seleccionado?.Estatus ?? "Activo",
                PermisosEdicion = seleccionado?.Permisos ?? new List<string>(),
                Usuarios = usuarios
            };
        }

        public async Task<IReadOnlyCollection<string>> GetUserPermissionsAsync(string usuario)
        {
            var normalized = Normalize(usuario);
            if (normalized == null)
                return Array.Empty<string>();

            return await _appContext.UsuarioPermisos
                .AsNoTracking()
                .Where(x => x.Usuario != null
                    && x.Usuario.ToUpper() == normalized.ToUpper()
                    && x.PuedeVer)
                .OrderBy(x => x.Modulo)
                .Select(x => x.Modulo ?? string.Empty)
                .Where(x => x != string.Empty)
                .ToArrayAsync();
        }

        public async Task<bool> SaveUsuarioAsync(string usuario, string password, string rol, string estatus, IEnumerable<string> permisos, string usuarioActor)
        {
            var normalized = Normalize(usuario);
            if (normalized == null)
                return false;

            var existing = await _appContext.Usuarios
                .FirstOrDefaultAsync(x => x.Usuario != null && x.Usuario.ToUpper() == normalized.ToUpper());
            if (existing == null && string.IsNullOrWhiteSpace(password))
                return false;

            var finalRol = string.IsNullOrWhiteSpace(rol) ? "Cajero" : rol.Trim();
            var finalEstatus = string.Equals(estatus, "Inactivo", StringComparison.OrdinalIgnoreCase) ? "Inactivo" : "Activo";
            if (existing == null)
            {
                existing = new AppUsuario
                {
                    Usuario = normalized,
                    PasswordHash = HashPassword(password.Trim()),
                    Rol = finalRol,
                    Estatus = finalEstatus,
                    FechaAlta = DateTime.UtcNow
                };
                await _appContext.Usuarios.AddAsync(existing);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(password))
                    existing.PasswordHash = HashPassword(password.Trim());
                existing.Rol = finalRol;
                existing.Estatus = finalEstatus;
            }

            var permitidos = PosModuloPermisoViewModel.Catalogo()
                .Select(x => x.Clave)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var seleccionados = permisos
                .Where(x => permitidos.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var actuales = await _appContext.UsuarioPermisos
                .Where(x => x.Usuario != null && x.Usuario.ToUpper() == normalized.ToUpper())
                .ToListAsync();
            _appContext.UsuarioPermisos.RemoveRange(actuales);
            foreach (var permiso in seleccionados)
            {
                await _appContext.UsuarioPermisos.AddAsync(new AppUsuarioPermiso
                {
                    Usuario = normalized,
                    Modulo = permiso,
                    PuedeVer = true
                });
            }

            await _appContext.SaveChangesAsync();
            await InsertPosAuditoriaAsync(usuarioActor, "Usuarios", "Guardar", normalized, "Usuario POS guardado",
                BuildAuditJson(("Usuario", normalized), ("Rol", rol), ("Estatus", estatus), ("Permisos", seleccionados)));
            return true;
        }

        private static string HashPassword(string password)
        {
            const int iterations = 100_000;
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPassword(string password, string? storedPassword)
        {
            if (string.IsNullOrWhiteSpace(storedPassword))
                return false;
            if (!IsModernPasswordHash(storedPassword))
                return string.Equals(password, storedPassword, StringComparison.Ordinal);

            var parts = storedPassword.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
                return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static bool IsModernPasswordHash(string? value) =>
            !string.IsNullOrWhiteSpace(value) && value.StartsWith("PBKDF2$", StringComparison.Ordinal);
    }
}
