USE [ControlTaxis];
GO

DECLARE @Usuario NVARCHAR(50) = N'Guadalupe';
DECLARE @PasswordHash NVARCHAR(200) = N'PBKDF2$100000$4kztumgP5b1KUoz/Pd8rkw==$8zCiSTo4k6pGk2S4uuKPQtphjMyuN7chAYGTBKv6wW4=';

IF EXISTS (SELECT 1 FROM dbo.Usuarios WHERE UPPER(Usuario) = UPPER(@Usuario))
BEGIN
    UPDATE dbo.Usuarios
    SET PasswordHash = @PasswordHash,
        Rol = N'Administrador',
        Estatus = N'Activo'
    WHERE UPPER(Usuario) = UPPER(@Usuario);
END
ELSE
BEGIN
    INSERT INTO dbo.Usuarios (Usuario, PasswordHash, Rol, Estatus, FechaAlta)
    VALUES (@Usuario, @PasswordHash, N'Administrador', N'Activo', SYSUTCDATETIME());
END;

DELETE FROM dbo.UsuarioPermisos
WHERE UPPER(Usuario) = UPPER(@Usuario);

INSERT INTO dbo.UsuarioPermisos (Usuario, Modulo, PuedeVer)
VALUES
    (@Usuario, N'RegistroDiario', 1),
    (@Usuario, N'Ventas', 1),
    (@Usuario, N'Pagos', 1),
    (@Usuario, N'Comisiones', 1),
    (@Usuario, N'Transportes', 1),
    (@Usuario, N'Guias', 1),
    (@Usuario, N'Taxistas', 1),
    (@Usuario, N'Gafetes', 1),
    (@Usuario, N'Relaciones', 1),
    (@Usuario, N'Gastos', 1),
    (@Usuario, N'Cortes', 1),
    (@Usuario, N'Reportes', 1),
    (@Usuario, N'Usuarios', 1);
GO
