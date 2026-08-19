USE [mkt];
GO

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilRegistro (
        id_app_movil_registro INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistro PRIMARY KEY,
        folio_app NVARCHAR(60) NOT NULL,
        folio_app_original NVARCHAR(60) NOT NULL DEFAULT '',
        folio_pos NVARCHAR(100) NOT NULL DEFAULT '',
        fecha_operacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        id_catalogo INT NULL,
        folio_gafete NVARCHAR(300) NOT NULL DEFAULT '',
        vendedor_clave NVARCHAR(50) NOT NULL DEFAULT '',
        vendedor_nombre NVARCHAR(150) NOT NULL DEFAULT '',
        telefono_taxista NVARCHAR(30) NOT NULL DEFAULT '',
        telefono_contacto NVARCHAR(30) NOT NULL DEFAULT '',
        nacionalidad NVARCHAR(120) NOT NULL DEFAULT '',
        placas NVARCHAR(50) NOT NULL DEFAULT '',
        modelo_vehiculo NVARCHAR(150) NOT NULL DEFAULT '',
        unidad NVARCHAR(50) NOT NULL DEFAULT '',
        hotel NVARCHAR(200) NOT NULL DEFAULT '',
        origen NVARCHAR(150) NOT NULL DEFAULT '',
        sitio NVARCHAR(150) NOT NULL DEFAULT '',
        destino NVARCHAR(150) NOT NULL DEFAULT '',
        pax INT NOT NULL DEFAULT 0,
        tipo_operacion NVARCHAR(80) NOT NULL DEFAULT '',
        total DECIMAL(18,2) NOT NULL DEFAULT 0,
        efectivo DECIMAL(18,2) NOT NULL DEFAULT 0,
        tarjeta DECIMAL(18,2) NOT NULL DEFAULT 0,
        estado_pago_dejada NVARCHAR(20) NOT NULL DEFAULT 'pendiente',
        fecha_pago_dejada DATETIME2 NULL,
        usuario_pago_dejada NVARCHAR(180) NOT NULL DEFAULT '',
        ticket_pago_dejada NVARCHAR(80) NOT NULL DEFAULT '',
        usuario_movil NVARCHAR(80) NOT NULL DEFAULT 'hostinger',
        notas NVARCHAR(MAX) NOT NULL DEFAULT '',
        detalle_json NVARCHAR(MAX) NOT NULL DEFAULT '',
        estado_sync NVARCHAR(30) NOT NULL DEFAULT 'SINCRONIZADO',
        fecha_creacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioApp ON dbo.AppMovilRegistro(folio_app);
END;
GO

IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_app_original') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD folio_app_original NVARCHAR(60) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_pos') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD folio_pos NVARCHAR(100) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'id_catalogo') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD id_catalogo INT NULL;
IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_gafete') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD folio_gafete NVARCHAR(300) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_taxista') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD telefono_taxista NVARCHAR(30) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_contacto') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD telefono_contacto NVARCHAR(30) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'nacionalidad') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD nacionalidad NVARCHAR(120) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'placas') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD placas NVARCHAR(50) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'modelo_vehiculo') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD modelo_vehiculo NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'unidad') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD unidad NVARCHAR(50) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'sitio') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD sitio NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'destino') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD destino NVARCHAR(150) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'estado_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD estado_pago_dejada NVARCHAR(20) NOT NULL DEFAULT 'pendiente';
IF COL_LENGTH('dbo.AppMovilRegistro', 'fecha_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD fecha_pago_dejada DATETIME2 NULL;
IF COL_LENGTH('dbo.AppMovilRegistro', 'usuario_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD usuario_pago_dejada NVARCHAR(180) NOT NULL DEFAULT '';
IF COL_LENGTH('dbo.AppMovilRegistro', 'ticket_pago_dejada') IS NULL ALTER TABLE dbo.AppMovilRegistro ADD ticket_pago_dejada NVARCHAR(80) NOT NULL DEFAULT '';
GO

IF OBJECT_ID('dbo.AppMovilFolioControl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilFolioControl (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilFolioControl PRIMARY KEY,
        FolioAppOriginal NVARCHAR(60) NOT NULL,
        FolioControl NVARCHAR(60) NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilFolioControl_Fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Original ON dbo.AppMovilFolioControl(FolioAppOriginal);
    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Control ON dbo.AppMovilFolioControl(FolioControl);
END;
GO

IF OBJECT_ID('dbo.AppDejadaEquivalencia', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppDejadaEquivalencia (
        id_dejada_equivalencia INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppDejadaEquivalencia PRIMARY KEY,
        unidad_texto NVARCHAR(100) NOT NULL,
        unidad_normalizada AS UPPER(LTRIM(RTRIM(unidad_texto))),
        tipo_transporte NVARCHAR(20) NOT NULL DEFAULT '',
        dejada DECIMAL(18,2) NOT NULL DEFAULT 0,
        fuente NVARCHAR(80) NOT NULL DEFAULT ''
    );
END;
GO

IF OBJECT_ID('dbo.AppMovilHoteles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilHoteles (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilHoteles PRIMARY KEY,
        HotelNombre NVARCHAR(200) NOT NULL,
        HotelNormalizado NVARCHAR(200) NOT NULL,
        Activo BIT NOT NULL DEFAULT 1,
        Fuente NVARCHAR(80) NOT NULL DEFAULT '',
        FechaActualizacion DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID('dbo.gafete', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.gafete', 'folioperacion') IS NULL
        ALTER TABLE dbo.gafete ADD folioperacion NVARCHAR(50) NULL;

    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID('dbo.gafete')
          AND c.name = 'folioperacion'
          AND t.name NOT IN ('nvarchar', 'varchar', 'nchar', 'char')
    )
        ALTER TABLE dbo.gafete ALTER COLUMN folioperacion NVARCHAR(50) NULL;
END;
GO
