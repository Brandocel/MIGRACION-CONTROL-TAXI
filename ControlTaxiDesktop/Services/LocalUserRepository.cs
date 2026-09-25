using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using ControlTaxiDesktop.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;

namespace ControlTaxiDesktop.Services;

public sealed class LocalUserRepository(LocalDatabase database)
{
    private readonly BranchConfigurationService _branchService = new();

    public static readonly string[] AllModules =
    [
        "RegistroDiario", "Comisiones", "Transportes", "Guias", "Taxistas", "Gafetes",
        "Relaciones", "Gastos", "Cortes", "Reportes", "ReporteTaxis", "ControlDejadas",
        "DejadasComisiones", "ConcentradoGeneral", "ConfiguracionComisiones", "Usuarios", "Navieras",
        // No es una pantalla, es una facultad: quien la tenga puede darle AUTORIZAR en Comisiones.
        // Sin esta autorizacion el boton PAGAR de ese folio no se habilita. Confirmado con el
        // usuario el 2026-08-24.
        "AutorizarPagoComision"
    ];

    public sealed record AuthenticationResult(bool Success, DesktopSession? Session, AuthenticationFailureReason FailureReason)
    {
        public static AuthenticationResult SuccessResult(DesktopSession session) => new(true, session, AuthenticationFailureReason.None);
        public static AuthenticationResult Failure(AuthenticationFailureReason reason) => new(false, null, reason);
    }

    // Construye el SqlConnectionStringBuilder para una sucursal sin abrir la conexión.
    // Útil para verificar hacia dónde apuntará la conexión (DataSource, InitialCatalog)
    // antes de intentar conectar o realizar escrituras. No incluye la contraseña.
    private SqlConnectionStringBuilder BuildBranchConnectionBuilder(string branchCode)
    {
        var branch = _branchService.GetBranch(branchCode);
        // When a local override is active prefer branch values from branches.local.json
        var isLocalOverride = _branchService.IsLocalOverrideActive();
        string? sqlServer = null;
        string? sqlDatabase = null;
        string? sqlUser = null;
        if (!isLocalOverride)
        {
            // Only apply credential stores to populate environment variables when not using local override
            if (string.Equals(branchCode, "P28", StringComparison.OrdinalIgnoreCase))
                Plaza28CredentialStore.TryApplyToEnvironment(out _);
            if (string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase))
                CascoCredentialStore.TryApplyToEnvironment(out _);

            sqlServer = Environment.GetEnvironmentVariable($"{(branchCode == "CV" ? "CASCO" : "PLAZA28")}_SQL_SERVER");
            sqlDatabase = Environment.GetEnvironmentVariable($"{(branchCode == "CV" ? "CASCO" : "PLAZA28")}_SQL_DATABASE");
            sqlUser = Environment.GetEnvironmentVariable($"{(branchCode == "CV" ? "CASCO" : "PLAZA28")}_SQL_USER");
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(sqlServer) ? branch.SqlServer : sqlServer,
            InitialCatalog = string.IsNullOrWhiteSpace(sqlDatabase) ? branch.Database : sqlDatabase,
            UserID = string.IsNullOrWhiteSpace(sqlUser) ? (string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser.Trim()) : sqlUser,
            // No se establece Password aquí para no exponer secretos.
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 8
        };

        return builder;
    }

    // Exponer información de conexion calculada (sin contraseña) para la UI.
    public (string DataSource, string InitialCatalog, string UserId) GetBranchConnectionInfo(string branchCode)
    {
        var builder = BuildBranchConnectionBuilder(branchCode);
        return (builder.DataSource, builder.InitialCatalog, builder.UserID);
    }

    public enum AuthenticationFailureReason
    {
        None,
        UserNotFound,
        UserInactive,
        IncorrectPassword,
        DatabaseStructureError,
        BranchNotAllowed,
        RemoteServerUnavailable
    }

    public async Task EnsureTestUserAsync()
    {
        if (!database.IsTestDatabase)
            return;
        await using var connection = database.Open();
        await using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM DesktopUsers;";
        if (Convert.ToInt64(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0)
            return;

        await using var transaction = connection.BeginTransaction();
        await using var user = connection.CreateCommand();
        user.Transaction = transaction;
        user.CommandText = "INSERT INTO DesktopUsers (Usuario, PasswordHash, Rol, Estatus, FechaAlta) VALUES ('admin', $hash, 'Administrador', 'Activo', $date);";
        user.Parameters.AddWithValue("$hash", HashPassword("admin"));
        user.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await user.ExecuteNonQueryAsync();

        foreach (var module in AllModules)
        {
            await using var permission = connection.CreateCommand();
            permission.Transaction = transaction;
            permission.CommandText = "INSERT INTO DesktopPermissions (Usuario, Modulo, PuedeVer) VALUES ('admin', $module, 1);";
            permission.Parameters.AddWithValue("$module", module);
            await permission.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    public async Task<AuthenticationResult> AuthenticateAsync(string userName, string password, string? requestedBranchCode = null)
    {
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            return AuthenticationResult.Failure(AuthenticationFailureReason.UserNotFound);

        var requestedBranch = NormalizeBranchCode(requestedBranchCode);
        if (string.Equals(requestedBranch, "CV", StringComparison.OrdinalIgnoreCase))
            return await AuthenticateCascoAsync(userName.Trim(), password.Trim(), requestedBranch);
        if (string.Equals(requestedBranch, "P28", StringComparison.OrdinalIgnoreCase))
            return await AuthenticatePlaza28Async(userName.Trim(), password.Trim(), requestedBranch);

        await using var connection = database.Open();
        var source = "Desktop";
        (string UserName, string PasswordHash, string Role, string Status, string BranchCode)? user;

        try
        {
            user = await ReadUserAsync(connection, "Desktop", userName.Trim());
            if (user is null && !database.IsTestDatabase && await HasTableAsync(connection, "ControlTaxis__dbo__Usuarios"))
            {
                user = await ReadUserAsync(connection, "Imported", userName.Trim());
                source = "Imported";
            }
        }
        catch (SqliteException)
        {
            return AuthenticationResult.Failure(AuthenticationFailureReason.DatabaseStructureError);
        }

        if (user is null)
            return AuthenticationResult.Failure(AuthenticationFailureReason.UserNotFound);

        if (!string.Equals(user.Value.Status, "Activo", StringComparison.OrdinalIgnoreCase))
            return AuthenticationResult.Failure(AuthenticationFailureReason.UserInactive);

        if (!VerifyPassword(password.Trim(), user.Value.PasswordHash))
            return AuthenticationResult.Failure(AuthenticationFailureReason.IncorrectPassword);

        IReadOnlySet<string> permissions;
        try
        {
            permissions = await ReadPermissionsAsync(connection, source, user.Value.UserName);
        }
        catch (SqliteException)
        {
            return AuthenticationResult.Failure(AuthenticationFailureReason.DatabaseStructureError);
        }

        var branchCode = NormalizeBranchCode(user.Value.BranchCode);
        if (!string.Equals(requestedBranch, "ALL", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(branchCode, "ALL", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(branchCode, requestedBranch, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticationResult.Failure(AuthenticationFailureReason.BranchNotAllowed);
        }

        if (string.Equals(branchCode, "ALL", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requestedBranch, "ALL", StringComparison.OrdinalIgnoreCase))
            branchCode = requestedBranch;
        var session = new DesktopSession(user.Value.UserName, user.Value.Role, permissions, database.IsTestDatabase, branchCode);
        return AuthenticationResult.SuccessResult(session);
    }

    private async Task<AuthenticationResult> AuthenticateCascoAsync(string userName, string password, string requestedBranch)
    {
        var branch = _branchService.GetBranch("CV");
        if (branch is null || string.IsNullOrWhiteSpace(branch.SqlServer) || string.IsNullOrWhiteSpace(branch.Database))
            return AuthenticationResult.Failure(AuthenticationFailureReason.DatabaseStructureError);

        // Ensure we have a saved credential or prompt the user to create one.
        var sqlPassword = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD");
        if (string.IsNullOrWhiteSpace(sqlPassword))
        {
            // First try to load an existing saved credential and apply it to the process
            if (!CascoCredentialStore.TryApplyToEnvironment(out var loadError))
            {
                // No saved credential: prompt the user with the existing setup window to capture and save one.
                try
                {
                    var cascoBranch = _branchService.GetBranch("CV");
                    var setupWindow = new CascoConnectionSetupWindow(cascoBranch);
                    var configured = setupWindow.ShowDialog();
                    if (configured == true)
                    {
                        // After save, try to apply again
                        CascoCredentialStore.TryApplyToEnvironment(out _);
                        sqlPassword = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD");
                    }
                }
                catch
                {
                    // fallthrough to failure below
                }
            }
            else
            {
                // Applied saved credential, pick up password
                sqlPassword = Environment.GetEnvironmentVariable("CASCO_SQL_PASSWORD");
            }

            if (string.IsNullOrWhiteSpace(sqlPassword))
                return AuthenticationResult.Failure(AuthenticationFailureReason.RemoteServerUnavailable);
        }

        var sqlUser = string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser.Trim();
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = branch.SqlServer,
            InitialCatalog = branch.Database,
            UserID = sqlUser,
            Password = sqlPassword,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        };

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            var user = await ReadCascoUserAsync(connection, userName);
            if (user is null)
                return AuthenticationResult.Failure(AuthenticationFailureReason.UserNotFound);

            if (!user.Value.Active)
                return AuthenticationResult.Failure(AuthenticationFailureReason.UserInactive);

            if (!VerifyPassword(password, user.Value.PasswordHash))
                return AuthenticationResult.Failure(AuthenticationFailureReason.IncorrectPassword);

            var branchCode = NormalizeBranchCode(user.Value.BranchCode);
            if (!string.Equals(branchCode, "ALL", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(branchCode, requestedBranch, StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticationResult.Failure(AuthenticationFailureReason.BranchNotAllowed);
            }

            var permissions = BuildCascoPermissions(user.Value);
            var session = new DesktopSession(user.Value.UserName, user.Value.Role, permissions, false, "CV");
            return AuthenticationResult.SuccessResult(session);
        }
        catch (SqlException)
        {
            return AuthenticationResult.Failure(AuthenticationFailureReason.RemoteServerUnavailable);
        }
        catch (InvalidOperationException)
        {
            return AuthenticationResult.Failure(AuthenticationFailureReason.RemoteServerUnavailable);
        }
    }

    private async Task<AuthenticationResult> AuthenticatePlaza28Async(string userName, string password, string requestedBranch)
    {
        var branch = _branchService.GetBranch("P28");
        if (branch is null || string.IsNullOrWhiteSpace(branch.SqlServer) || string.IsNullOrWhiteSpace(branch.Database))
            return AuthenticationResult.Failure(AuthenticationFailureReason.DatabaseStructureError);

        Plaza28CredentialStore.TryApplyToEnvironment(out var credentialLoadError);

        var sqlPassword = Environment.GetEnvironmentVariable("PLAZA28_SQL_PASSWORD");
        if (string.IsNullOrWhiteSpace(sqlPassword))
        {
            await new LocalErrorLogger(database).LogAsync("Sistema", "Login", "AuthenticatePlaza28Async.TryApplyToEnvironment",
                new InvalidOperationException(string.IsNullOrWhiteSpace(credentialLoadError) ? "Credencial vacia sin detalle." : credentialLoadError));
            return AuthenticationResult.Failure(AuthenticationFailureReason.RemoteServerUnavailable);
        }

        var sqlServer = Environment.GetEnvironmentVariable("PLAZA28_SQL_SERVER");
        var sqlDatabase = Environment.GetEnvironmentVariable("PLAZA28_SQL_DATABASE");
        var sqlUser = Environment.GetEnvironmentVariable("PLAZA28_SQL_USER");

        if (string.IsNullOrWhiteSpace(sqlServer))
            sqlServer = branch.SqlServer;
        if (string.IsNullOrWhiteSpace(sqlDatabase))
            sqlDatabase = branch.Database;
        if (string.IsNullOrWhiteSpace(sqlUser))
            sqlUser = string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser.Trim();

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = sqlServer,
            InitialCatalog = sqlDatabase,
            UserID = sqlUser,
            Password = sqlPassword,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 5
        };

        try
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();

            var user = await ReadCascoUserAsync(connection, userName);
            if (user is null)
                return AuthenticationResult.Failure(AuthenticationFailureReason.UserNotFound);

            if (!user.Value.Active)
                return AuthenticationResult.Failure(AuthenticationFailureReason.UserInactive);

            if (!VerifyPassword(password, user.Value.PasswordHash))
                return AuthenticationResult.Failure(AuthenticationFailureReason.IncorrectPassword);

            var branchCode = NormalizeBranchCode(user.Value.BranchCode);
            if (!string.Equals(branchCode, "ALL", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(branchCode, requestedBranch, StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticationResult.Failure(AuthenticationFailureReason.BranchNotAllowed);
            }

            var permissions = BuildCascoPermissions(user.Value);
            var session = new DesktopSession(user.Value.UserName, user.Value.Role, permissions, false, "P28");
            return AuthenticationResult.SuccessResult(session);
        }
        catch (SqlException ex)
        {
            await new LocalErrorLogger(database).LogAsync("Sistema", "Login", "AuthenticatePlaza28Async", ex);
            return AuthenticationResult.Failure(AuthenticationFailureReason.RemoteServerUnavailable);
        }
        catch (InvalidOperationException ex)
        {
            await new LocalErrorLogger(database).LogAsync("Sistema", "Login", "AuthenticatePlaza28Async", ex);
            return AuthenticationResult.Failure(AuthenticationFailureReason.RemoteServerUnavailable);
        }
    }

    /// <summary>
    /// Chequeo puntual de una sola facultad, para pantallas que necesitan saber "puede este
    /// usuario hacer X" sin cargar el catalogo completo de usuarios. Usado por Comisiones para
    /// decidir si se muestra el boton AUTORIZAR.
    /// </summary>
    public async Task<bool> HasPermissionAsync(string userName, string module, string? branchCode = null)
    {
        if (string.IsNullOrWhiteSpace(userName)) return false;

        // Plaza 28 y Casco Viejo guardan sus usuarios REALES en dbo.ControlTaxiUsuarios (SQL
        // Server), no en DesktopPermissions (SQLite, solo aplica a la base de prueba local).
        // Sin esto la facultad de autorizar nunca se veia como concedida aunque ya estuviera
        // guardada, porque se estaba mirando la tabla equivocada. Detectado el 2026-08-24.
        var normalizedBranch = NormalizeBranchCode(branchCode);
        if (!database.IsTestDatabase && IsRemoteUserBranch(normalizedBranch))
        {
            try
            {
                await using var remoteConnection = await OpenBranchSqlConnectionAsync(normalizedBranch);
                var remoteUser = await ReadCascoUserAsync(remoteConnection, userName);
                if (remoteUser is null) return false;
                return string.Equals(module, "AutorizarPagoComision", StringComparison.OrdinalIgnoreCase)
                    ? remoteUser.Value.CanAuthorizeComision
                    : BuildCascoPermissions(remoteUser.Value).Contains(module);
            }
            catch
            {
                return false;
            }
        }

        await using var connection = database.Open();
        var hasDesktopUsers = await HasTableAsync(connection, "DesktopUsers");
        var hasImportedUsers = !hasDesktopUsers && await HasTableAsync(connection, "ControlTaxis__dbo__Usuarios");
        var source = database.IsTestDatabase || hasDesktopUsers ? "Desktop" : hasImportedUsers ? "Imported" : "Desktop";
        var modules = await ReadPermissionsAsync(connection, source, userName);
        return modules.Contains(module);
    }

    public async Task<IReadOnlyList<LocalUserRow>> GetUsersAsync(string? branchCode = null)
    {
        var normalizedBranch = NormalizeBranchCode(branchCode);
        if (!database.IsTestDatabase && IsRemoteUserBranch(normalizedBranch))
            return await GetRemoteUsersAsync(normalizedBranch);

        return await GetLocalUsersAsync();
    }

    private async Task<IReadOnlyList<LocalUserRow>> GetLocalUsersAsync()
    {
        await using var connection = database.Open();
        var hasDesktopUsers = await HasTableAsync(connection, "DesktopUsers");
        var hasImportedUsers = !hasDesktopUsers && await HasTableAsync(connection, "ControlTaxis__dbo__Usuarios");
        var source = database.IsTestDatabase || hasDesktopUsers ? "Desktop" : hasImportedUsers ? "Imported" : "Desktop";

        var usersSql = source == "Desktop"
            ? "SELECT Usuario, Rol, Estatus, FechaAlta, BranchCode FROM DesktopUsers ORDER BY Usuario;"
            : "SELECT Usuario, Rol, Estatus, FechaAlta, 'P28' AS BranchCode FROM \"ControlTaxis__dbo__Usuarios\" ORDER BY Usuario;";

        await using var users = connection.CreateCommand();
        users.CommandText = usersSql;
        await using var reader = await users.ExecuteReaderAsync();
        var result = new List<LocalUserRow>();
        while (await reader.ReadAsync())
        {
            var userName = reader.GetString(0);
            var modules = await ReadPermissionsAsync(connection, source, userName);
            var branchCode = NormalizeBranchCode(reader.GetString(4));
            result.Add(new LocalUserRow(userName, reader.GetString(1), reader.GetString(2), reader.GetString(3), string.Join(", ", modules), branchCode));
        }
        return result;
    }

    public async Task SaveUserAsync(string userName, string password, string role, string status, string branchCode, IEnumerable<string> permissions, string? adminBranchCode = null)
    {
        var normalizedAdminBranch = NormalizeBranchCode(adminBranchCode);
        if (!database.IsTestDatabase && IsRemoteUserBranch(normalizedAdminBranch))
        {
            await SaveRemoteUserAsync(normalizedAdminBranch, userName, password, role, status, branchCode, permissions);
            return;
        }

        await SaveLocalUserAsync(userName, password, role, status, branchCode, permissions);
    }

    private async Task SaveLocalUserAsync(string userName, string password, string role, string status, string branchCode, IEnumerable<string> permissions)
    {
        userName = Require(userName, "El usuario");
        role = Require(role, "El rol");
        status = Require(status, "El estatus");
        branchCode = NormalizeBranchCode(branchCode);
        await using var connection = database.Open();
        await using var transaction = connection.BeginTransaction();
        await using var user = connection.CreateCommand();
        user.Transaction = transaction;
        user.CommandText = string.IsNullOrWhiteSpace(password)
            ? "UPDATE DesktopUsers SET Rol=$role,Estatus=$status,BranchCode=$branch WHERE upper(Usuario)=upper($user);"
            : "INSERT INTO DesktopUsers (Usuario,PasswordHash,Rol,Estatus,BranchCode,FechaAlta) VALUES ($user,$hash,$role,$status,$branch,$date) ON CONFLICT(Usuario) DO UPDATE SET PasswordHash=excluded.PasswordHash,Rol=excluded.Rol,Estatus=excluded.Estatus,BranchCode=excluded.BranchCode;";
        user.Parameters.AddWithValue("$user", userName);
        user.Parameters.AddWithValue("$role", role);
        user.Parameters.AddWithValue("$status", status);
        user.Parameters.AddWithValue("$branch", branchCode);
        if (!string.IsNullOrWhiteSpace(password)) user.Parameters.AddWithValue("$hash", HashPassword(password.Trim()));
        if (!string.IsNullOrWhiteSpace(password)) user.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var affected = await user.ExecuteNonQueryAsync();
        if (affected == 0 && string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Para crear un usuario nuevo se requiere contraseña.");

        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM DesktopPermissions WHERE upper(Usuario)=upper($user);";
        delete.Parameters.AddWithValue("$user", userName);
        await delete.ExecuteNonQueryAsync();

        foreach (var module in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!AllModules.Contains(module, StringComparer.OrdinalIgnoreCase)) continue;
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO DesktopPermissions (Usuario,Modulo,PuedeVer) VALUES ($user,$module,1);";
            insert.Parameters.AddWithValue("$user", userName);
            insert.Parameters.AddWithValue("$module", module);
            await insert.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }

    /// <summary>
    /// dbo.ControlTaxiUsuarios (SQL Server) es la tabla REAL de usuarios de Plaza 28 y Casco
    /// Viejo -- DesktopPermissions (SQLite) solo aplica a la base local de prueba. La facultad
    /// de autorizar pago de comision se agrego aqui el 2026-08-24, autoprovisionada igual que
    /// las demas columnas de Casco (IF COL_LENGTH ... ADD) para no depender de un script aparte.
    /// </summary>
    private static async Task EnsureAuthorizeColumnAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF COL_LENGTH(N'dbo.ControlTaxiUsuarios', N'PuedeAutorizarComision') IS NULL
                ALTER TABLE dbo.ControlTaxiUsuarios ADD PuedeAutorizarComision BIT NOT NULL CONSTRAINT DF_ControlTaxiUsuarios_PuedeAutorizarComision DEFAULT (0);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<IReadOnlyList<LocalUserRow>> GetRemoteUsersAsync(string branchCode)
    {
        await using var connection = await OpenBranchSqlConnectionAsync(branchCode);
        await EnsureAuthorizeColumnAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Username,
                Rol,
                Activo,
                BranchCode,
                FechaCreacion,
                PuedeVerInicio,
                PuedeVerVentas,
                PuedeVerGafetes,
                PuedeVerRelaciones,
                PuedeVerReportes,
                PuedeVerComisiones,
                PuedePagarDejadas,
                PuedeAutorizarComision
            FROM dbo.ControlTaxiUsuarios
            ORDER BY Username;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<LocalUserRow>();
        while (await reader.ReadAsync())
        {
            var user = (
                UserName: reader.GetString(0),
                PasswordHash: string.Empty,
                Role: reader.GetString(1),
                Active: reader.GetBoolean(2),
                BranchCode: reader.GetString(3),
                CanViewHome: reader.GetBoolean(5),
                CanViewVentas: reader.GetBoolean(6),
                CanViewGafetes: reader.GetBoolean(7),
                CanViewRelaciones: reader.GetBoolean(8),
                CanViewReportes: reader.GetBoolean(9),
                CanViewComisiones: reader.GetBoolean(10),
                CanPayPayouts: reader.GetBoolean(11),
                CanAuthorizeComision: reader.GetBoolean(12));
            var createdAt = reader.IsDBNull(4)
                ? string.Empty
                : reader.GetDateTime(4).ToString("O", CultureInfo.InvariantCulture);
            result.Add(new LocalUserRow(
                user.UserName,
                user.Role,
                user.Active ? "Activo" : "Inactivo",
                createdAt,
                string.Join(", ", BuildCascoPermissions(user)),
                NormalizeBranchCode(user.BranchCode)));
        }
        return result;
    }

    private async Task SaveRemoteUserAsync(string adminBranchCode, string userName, string password, string role, string status, string branchCode, IEnumerable<string> permissions)
    {
        userName = Require(userName, "El usuario");
        role = Require(role, "El rol");
        status = Require(status, "El estatus");
        branchCode = NormalizeBranchCode(branchCode);
        var permissionSet = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Abrir la conexion hacia la SUCURSAL DESTINO (branchCode), no hacia la sucursal del administrador.
        // Si se usa la conexion del administrador (adminBranchCode) se termina escribiendo el usuario
        // en la base de datos del admin en lugar de la base de la sucursal objetivo.
        await using var connection = await OpenBranchSqlConnectionAsync(branchCode);
        await EnsureAuthorizeColumnAsync(connection);
        var existing = await ReadCascoUserAsync(connection, userName);
        if (existing is null && string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Para crear un usuario nuevo se requiere contrasena.");

        var active = string.Equals(status, "Activo", StringComparison.OrdinalIgnoreCase);
        var canViewInicio = permissionSet.Contains("RegistroDiario");
        var canViewVentas = permissionSet.Overlaps(new[] { "Comisiones", "Taxistas", "Transportes" });
        var canViewGafetes = permissionSet.Contains("Gafetes");
        var canViewRelaciones = permissionSet.Contains("Relaciones");
        var canViewReportes = permissionSet.Overlaps(new[] { "Reportes", "Portal", "ReporteTaxis", "ControlDejadas", "ConcentradoGeneral" });
        var canViewComisiones = permissionSet.Overlaps(new[] { "ConfiguracionComisiones", "Cortes", "Gastos", "DejadasComisiones" });
        var isElevated = string.Equals(role, "Administrador", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "Supervisor", StringComparison.OrdinalIgnoreCase);
        var canPayPayouts = canViewRelaciones && (isElevated || existing?.CanPayPayouts == true);
        // Facultad explicita, sin heredarla de ningun rol: se guarda tal cual viene marcada en
        // el checkbox. Confirmado con el usuario el 2026-08-24.
        var canAuthorize = permissionSet.Contains("AutorizarPagoComision");
        var passwordHash = string.IsNullOrWhiteSpace(password) ? existing?.PasswordHash ?? string.Empty : HashPassword(password.Trim());

        await using var command = connection.CreateCommand();
        if (existing is null)
        {
            command.CommandText = """
                INSERT INTO dbo.ControlTaxiUsuarios
                    (Username, PasswordHash, NombreCompleto, Rol, BranchCode, Activo,
                     PuedeVerInicio, PuedeVerRelaciones, PuedeVerGafetes, PuedeVerReportes,
                     PuedeVerVentas, PuedePagarDejadas, PuedeVerComisiones, PuedeAutorizarComision,
                     FechaCreacion, FechaActualizacion)
                VALUES
                    (@username, @passwordHash, @fullName, @role, @branchCode, @active,
                     @home, @relations, @badges, @reports, @sales, @payPayouts, @commissions, @authorize,
                     SYSDATETIME(), SYSDATETIME());
                """;
        }
        else
        {
            command.CommandText = """
                UPDATE dbo.ControlTaxiUsuarios
                SET PasswordHash = @passwordHash,
                    NombreCompleto = @fullName,
                    Rol = @role,
                    BranchCode = @branchCode,
                    Activo = @active,
                    PuedeVerInicio = @home,
                    PuedeVerRelaciones = @relations,
                    PuedeVerGafetes = @badges,
                    PuedeVerReportes = @reports,
                    PuedeVerVentas = @sales,
                    PuedePagarDejadas = @payPayouts,
                    PuedeVerComisiones = @commissions,
                    PuedeAutorizarComision = @authorize,
                    FechaActualizacion = SYSDATETIME()
                WHERE UPPER(Username) = UPPER(@username);
                """;
        }

        command.Parameters.AddWithValue("@username", userName);
        command.Parameters.AddWithValue("@passwordHash", passwordHash);
        command.Parameters.AddWithValue("@fullName", userName);
        command.Parameters.AddWithValue("@role", role);
        command.Parameters.AddWithValue("@branchCode", branchCode);
        command.Parameters.AddWithValue("@active", active);
        command.Parameters.AddWithValue("@home", canViewInicio);
        command.Parameters.AddWithValue("@relations", canViewRelaciones);
        command.Parameters.AddWithValue("@badges", canViewGafetes);
        command.Parameters.AddWithValue("@reports", canViewReportes);
        command.Parameters.AddWithValue("@sales", canViewVentas);
        command.Parameters.AddWithValue("@payPayouts", canPayPayouts);
        command.Parameters.AddWithValue("@commissions", canViewComisiones);
        command.Parameters.AddWithValue("@authorize", canAuthorize);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<SqlConnection> OpenBranchSqlConnectionAsync(string branchCode)
    {
        var branch = _branchService.GetBranch(branchCode);
        var passwordEnvironmentName = string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase)
            ? "CASCO_SQL_PASSWORD"
            : "PLAZA28_SQL_PASSWORD";
        // If local override active for CV, do NOT apply CascoCredentialStore.ApplyToEnvironment to avoid
        // overriding server/database from branches.local.json. We still need the password: try to load it
        // directly from the credential store without applying server/database overrides.
        var isLocalOverride = _branchService.IsLocalOverrideActive();
        if (string.Equals(branchCode, "P28", StringComparison.OrdinalIgnoreCase))
            Plaza28CredentialStore.TryApplyToEnvironment(out _);
        if (string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase) && !isLocalOverride)
            CascoCredentialStore.TryApplyToEnvironment(out _);

        var sqlServer = Environment.GetEnvironmentVariable($"{(branchCode == "CV" ? "CASCO" : "PLAZA28")}_SQL_SERVER");
        var sqlDatabase = Environment.GetEnvironmentVariable($"{(branchCode == "CV" ? "CASCO" : "PLAZA28")}_SQL_DATABASE");
        var sqlUser = Environment.GetEnvironmentVariable($"{(branchCode == "CV" ? "CASCO" : "PLAZA28")}_SQL_USER");

        string? sqlPassword = null;
        if (string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase) && isLocalOverride)
        {
            // Try to read only the password from the CascoCredentialStore without overriding server/database
            if (!CascoCredentialStore.TryLoad(out var cred, out var _ ) || cred is null || string.IsNullOrWhiteSpace(cred.SqlPassword))
                sqlPassword = Environment.GetEnvironmentVariable(passwordEnvironmentName);
            else
                sqlPassword = cred.SqlPassword;
        }
        else
        {
            sqlPassword = Environment.GetEnvironmentVariable(passwordEnvironmentName);
        }

        if (string.IsNullOrWhiteSpace(sqlPassword))
            throw new InvalidOperationException("No se encontro la credencial cifrada de SQL para guardar usuarios.");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(sqlServer) ? branch.SqlServer : sqlServer,
            InitialCatalog = string.IsNullOrWhiteSpace(sqlDatabase) ? branch.Database : sqlDatabase,
            UserID = string.IsNullOrWhiteSpace(sqlUser) ? (string.IsNullOrWhiteSpace(branch.SqlUser) ? "sa" : branch.SqlUser.Trim()) : sqlUser,
            Password = sqlPassword,
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 8
        };

        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static bool IsRemoteUserBranch(string branchCode) =>
        string.Equals(branchCode, "P28", StringComparison.OrdinalIgnoreCase)
        || string.Equals(branchCode, "CV", StringComparison.OrdinalIgnoreCase);

    private static async Task<(string UserName, string PasswordHash, string Role, string Status, string BranchCode)?> ReadUserAsync(SqliteConnection connection, string source, string userName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = source == "Desktop"
            ? "SELECT Usuario, PasswordHash, Rol, Estatus, BranchCode FROM DesktopUsers WHERE upper(Usuario) = upper($user) LIMIT 1;"
            : "SELECT Usuario, PasswordHash, Rol, Estatus, 'P28' AS BranchCode FROM \"ControlTaxis__dbo__Usuarios\" WHERE upper(Usuario) = upper($user) LIMIT 1;";
        command.Parameters.AddWithValue("$user", userName);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    private static async Task<(string UserName, string PasswordHash, string Role, bool Active, string BranchCode, bool CanViewHome, bool CanViewVentas, bool CanViewGafetes, bool CanViewRelaciones, bool CanViewReportes, bool CanViewComisiones, bool CanPayPayouts, bool CanAuthorizeComision)?> ReadCascoUserAsync(SqlConnection connection, string userName)
    {
        await EnsureAuthorizeColumnAsync(connection);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1)
                Username,
                PasswordHash,
                Rol,
                Activo,
                BranchCode,
                PuedeVerInicio,
                PuedeVerVentas,
                PuedeVerGafetes,
                PuedeVerRelaciones,
                PuedeVerReportes,
                PuedeVerComisiones,
                PuedePagarDejadas,
                PuedeAutorizarComision
            FROM dbo.ControlTaxiUsuarios
            WHERE UPPER(Username) = UPPER(@user);
            """;
        command.Parameters.AddWithValue("@user", userName);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetBoolean(11),
                reader.GetBoolean(12))
            : null;
    }

    private static async Task<bool> HasTableAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    private static async Task<IReadOnlySet<string>> ReadPermissionsAsync(SqliteConnection connection, string source, string userName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = source == "Desktop"
            ? "SELECT Modulo FROM DesktopPermissions WHERE upper(Usuario) = upper($user) AND PuedeVer = 1;"
            : "SELECT Modulo FROM \"ControlTaxis__dbo__UsuarioPermisos\" WHERE upper(Usuario) = upper($user) AND PuedeVer = 1;";
        command.Parameters.AddWithValue("$user", userName);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result;
    }

    private static IReadOnlySet<string> BuildCascoPermissions((string UserName, string PasswordHash, string Role, bool Active, string BranchCode, bool CanViewHome, bool CanViewVentas, bool CanViewGafetes, bool CanViewRelaciones, bool CanViewReportes, bool CanViewComisiones, bool CanPayPayouts, bool CanAuthorizeComision) user)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (user.CanViewHome)
            result.Add("RegistroDiario");
        if (user.CanViewVentas)
        {
            result.Add("Comisiones");
            result.Add("Taxistas");
            result.Add("Transportes");
        }
        if (user.CanViewGafetes)
            result.Add("Gafetes");
        if (user.CanViewRelaciones || user.CanPayPayouts)
            result.Add("Relaciones");
        if (user.CanViewReportes)
        {
            result.Add("Reportes");
            result.Add("Portal");
        }
        if (user.CanViewComisiones)
        {
            result.Add("Comisiones");
            result.Add("ConfiguracionComisiones");
            result.Add("Cortes");
            result.Add("Gastos");
        }
        if (user.CanAuthorizeComision)
            result.Add("AutorizarPagoComision");

        if (string.Equals(user.Role, "Administrador", StringComparison.OrdinalIgnoreCase)
            || string.Equals(user.Role, "Supervisor", StringComparison.OrdinalIgnoreCase))
        {
            result.Add("Usuarios");
            result.Add("Taxistas");
            result.Add("Transportes");
            result.Add("Reportes");
            result.Add("ConfiguracionComisiones");
            result.Add("Portal");
        }

        return result;
    }

    private static string NormalizeBranchCode(string? branchCode)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            return "ALL";

        var normalized = branchCode.Trim().ToUpperInvariant();
        var compact = new string(normalized
            .Where(character => !char.IsWhiteSpace(character) && character is not '-' and not '_')
            .ToArray());

        return compact switch
        {
            "28" => "P28",
            "P28" => "P28",
            "PLAZA" => "P28",
            "PLAZA28" => "P28",
            "PLAZAVEINTIOCHO" => "P28",
            "CV" => "CV",
            "CASCO" => "CV",
            "CASCOVIEJO" => "CV",
            "ALL" => "ALL",
            "*" => "ALL",
            "AMBAS" => "ALL",
            _ => normalized
        };
    }

    private static string HashPassword(string password)
    {
        const int iterations = 100_000;
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedPassword)
    {
        if (!storedPassword.StartsWith("PBKDF2$", StringComparison.Ordinal))
            return string.Equals(password, storedPassword, StringComparison.Ordinal);
        var parts = storedPassword.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
            return false;
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static string Require(string value, string label) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"{label} es obligatorio.");
}
