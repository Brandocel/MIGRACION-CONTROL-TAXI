USE mkt;
GO

IF OBJECT_ID('dbo.AppMovilFolioControl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilFolioControl
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilFolioControl PRIMARY KEY,
        FolioAppOriginal NVARCHAR(60) NOT NULL,
        FolioControl NVARCHAR(20) NOT NULL,
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilFolioControl_Fecha DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Original
        ON dbo.AppMovilFolioControl(FolioAppOriginal);

    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Control
        ON dbo.AppMovilFolioControl(FolioControl);
END;
GO

IF OBJECT_ID('dbo.AppMovilGafeteControl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilGafeteControl
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilGafeteControl PRIMARY KEY,
        UltimoNumero INT NOT NULL CONSTRAINT DF_AppMovilGafeteControl_Ultimo DEFAULT 0,
        Ciclo INT NOT NULL CONSTRAINT DF_AppMovilGafeteControl_Ciclo DEFAULT 1,
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilGafeteControl_Fecha DEFAULT SYSUTCDATETIME()
    );

    INSERT INTO dbo.AppMovilGafeteControl (UltimoNumero)
    VALUES (0);
END;
GO

IF OBJECT_ID('dbo.AppMovilGafetes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilGafetes
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilGafetes PRIMARY KEY,
        FolioGafete NVARCHAR(20) NOT NULL,
        CodigoBarras NVARCHAR(80) NOT NULL,
        Ciclo INT NOT NULL CONSTRAINT DF_AppMovilGafetes_Ciclo DEFAULT 1,
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_AppMovilGafetes_Estatus DEFAULT 'Disponible',
        TaxistaId INT NULL,
        TaxistaNombre NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilGafetes_Taxista DEFAULT '',
        Usuario NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilGafetes_Usuario DEFAULT '',
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilGafetes_Fecha DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_AppMovilGafetes_Folio_Ciclo
        ON dbo.AppMovilGafetes(FolioGafete, Ciclo);
END;
GO

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilRegistro
    (
        id_app_movil_registro INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistro PRIMARY KEY,
        folio_app NVARCHAR(20) NOT NULL,
        folio_app_original NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_Original DEFAULT '',
        folio_gafete NVARCHAR(20) NOT NULL CONSTRAINT DF_AppMovilRegistro_Gafete DEFAULT '',
        folio_pos NVARCHAR(100) NOT NULL CONSTRAINT DF_AppMovilRegistro_Pos DEFAULT '',
        fecha_operacion DATETIME2 NOT NULL,
        id_catalogo INT NULL,
        vendedor_clave NVARCHAR(50) NOT NULL CONSTRAINT DF_AppMovilRegistro_VendedorClave DEFAULT '',
        vendedor_nombre NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_VendedorNombre DEFAULT '',
        telefono_taxista NVARCHAR(30) NOT NULL CONSTRAINT DF_AppMovilRegistro_TelefonoTaxista DEFAULT '',
        telefono_contacto NVARCHAR(30) NOT NULL CONSTRAINT DF_AppMovilRegistro_TelefonoContacto DEFAULT '',
        nacionalidad NVARCHAR(120) NOT NULL CONSTRAINT DF_AppMovilRegistro_Nacionalidad DEFAULT '',
        placas NVARCHAR(50) NOT NULL CONSTRAINT DF_AppMovilRegistro_Placas DEFAULT '',
        modelo_vehiculo NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_Modelo DEFAULT '',
        unidad NVARCHAR(50) NOT NULL CONSTRAINT DF_AppMovilRegistro_Unidad DEFAULT '',
        hotel NVARCHAR(200) NOT NULL CONSTRAINT DF_AppMovilRegistro_Hotel DEFAULT '',
        origen NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_OrigenViaje DEFAULT '',
        sitio NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_Sitio DEFAULT '',
        destino NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_Destino DEFAULT '',
        pax INT NOT NULL CONSTRAINT DF_AppMovilRegistro_Pax DEFAULT 0,
        tipo_operacion NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilRegistro_Tipo DEFAULT '',
        total DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_Total DEFAULT 0,
        efectivo DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_Efectivo DEFAULT 0,
        tarjeta DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_Tarjeta DEFAULT 0,
        usuario_movil NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilRegistro_Usuario DEFAULT '',
        notas NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AppMovilRegistro_Notas DEFAULT '',
        detalle_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AppMovilRegistro_Detalle DEFAULT '',
        estado_sync NVARCHAR(30) NOT NULL CONSTRAINT DF_AppMovilRegistro_Sync DEFAULT 'GUARDADO',
        fecha_creacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilRegistro_Creacion DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioApp
        ON dbo.AppMovilRegistro(folio_app);

    CREATE INDEX IX_AppMovilRegistro_Fecha
        ON dbo.AppMovilRegistro(fecha_operacion DESC);

    CREATE INDEX IX_AppMovilRegistro_Taxista
        ON dbo.AppMovilRegistro(vendedor_nombre);
END;
GO

IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_app_original') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD folio_app_original NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_Original_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_gafete') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD folio_gafete NVARCHAR(20) NOT NULL CONSTRAINT DF_AppMovilRegistro_Gafete_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'id_catalogo') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD id_catalogo INT NULL;
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_taxista') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD telefono_taxista NVARCHAR(30) NOT NULL CONSTRAINT DF_AppMovilRegistro_TelTaxista_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_contacto') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD telefono_contacto NVARCHAR(30) NOT NULL CONSTRAINT DF_AppMovilRegistro_TelContacto_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'nacionalidad') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD nacionalidad NVARCHAR(120) NOT NULL CONSTRAINT DF_AppMovilRegistro_Nacionalidad_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'placas') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD placas NVARCHAR(50) NOT NULL CONSTRAINT DF_AppMovilRegistro_Placas_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'modelo_vehiculo') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD modelo_vehiculo NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_Modelo_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'unidad') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD unidad NVARCHAR(50) NOT NULL CONSTRAINT DF_AppMovilRegistro_Unidad_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'sitio') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD sitio NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_Sitio_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'destino') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD destino NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilRegistro_Destino_Alter DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'origen') IS NOT NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ALTER COLUMN origen NVARCHAR(150) NOT NULL;
END;
GO

IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_status') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD payout_status NVARCHAR(30) NOT NULL CONSTRAINT DF_AppMovilRegistro_PayoutStatus DEFAULT 'pendiente';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_date') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD payout_date DATETIME2 NULL;
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_user') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD payout_user NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilRegistro_PayoutUser DEFAULT '';
END;

IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_ticket') IS NULL
BEGIN
    ALTER TABLE dbo.AppMovilRegistro ADD payout_ticket NVARCHAR(40) NOT NULL CONSTRAINT DF_AppMovilRegistro_PayoutTicket DEFAULT '';
END;
GO

IF OBJECT_ID('dbo.AppMovilTicketTaxista', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilTicketTaxista
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilTicketTaxista PRIMARY KEY,
        FolioApp NVARCHAR(20) NOT NULL,
        FolioOperacion NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Operacion DEFAULT '',
        FolioPos NVARCHAR(120) NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Pos DEFAULT '',
        Gafete NVARCHAR(20) NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Gafete DEFAULT '',
        TaxistaId INT NULL,
        TaxistaNombre NVARCHAR(150) NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Taxista DEFAULT '',
        TransporteTipo NVARCHAR(50) NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Transporte DEFAULT '',
        Observaciones NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Obs DEFAULT '',
        Usuario NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Usuario DEFAULT '',
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Fecha DEFAULT SYSUTCDATETIME(),
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_AppMovilTicketTaxista_Actualiza DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UX_AppMovilTicketTaxista_FolioApp
        ON dbo.AppMovilTicketTaxista(FolioApp);
END;
GO

IF OBJECT_ID('dbo.vw_AppMovilRegistrosViajes', 'V') IS NOT NULL
BEGIN
    DROP VIEW dbo.vw_AppMovilRegistrosViajes;
END;
GO

CREATE VIEW dbo.vw_AppMovilRegistrosViajes
AS
    SELECT
        folio_app AS id_registro,
        folio_app_original AS id_registro_original,
        id_catalogo,
        folio_gafete,
        vendedor_nombre AS nombre_taxista,
        telefono_taxista,
        telefono_contacto,
        nacionalidad,
        placas,
        modelo_vehiculo,
        unidad,
        hotel,
        origen,
        sitio,
        destino,
        pax AS numero_personas,
        tipo_operacion AS tipo_servicio,
        total AS costo_viaje,
        CASE WHEN tarjeta > 0 THEN 'Tarjeta' ELSE 'Efectivo' END AS metodo_pago,
        notas,
        detalle_json AS datos_escaneo,
        fecha_operacion AS fecha_registro
    FROM dbo.AppMovilRegistro;
GO

IF DB_ID('ControlTaxis') IS NOT NULL
BEGIN
    INSERT INTO dbo.AppMovilFolioControl (FolioAppOriginal, FolioControl)
    SELECT
        x.id_registro,
        RIGHT('0000' + CONVERT(VARCHAR(10), ROW_NUMBER() OVER (ORDER BY x.fecha_registro, x.id_registro)), 4)
    FROM ControlTaxis.dbo.RegistrosViajes x
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.AppMovilFolioControl c
        WHERE c.FolioAppOriginal = x.id_registro
    );

    INSERT INTO dbo.AppMovilRegistro
    (
        folio_app,
        folio_app_original,
        id_catalogo,
        folio_gafete,
        fecha_operacion,
        vendedor_nombre,
        telefono_taxista,
        telefono_contacto,
        nacionalidad,
        placas,
        modelo_vehiculo,
        unidad,
        hotel,
        origen,
        sitio,
        destino,
        pax,
        tipo_operacion,
        total,
        efectivo,
        tarjeta,
        notas,
        detalle_json,
        estado_sync
    )
    SELECT
        c.FolioControl,
        v.id_registro,
        v.id_catalogo,
        v.folio_gafete,
        v.fecha_registro,
        v.nombre_taxista,
        v.telefono_taxista,
        v.telefono_contacto,
        '',
        v.placas,
        v.modelo_vehiculo,
        v.unidad,
        v.hotel,
        v.origen,
        v.sitio,
        v.destino,
        v.numero_personas,
        v.tipo_servicio,
        v.costo_viaje,
        CASE WHEN UPPER(v.metodo_pago) LIKE '%TARJ%' THEN 0 ELSE v.costo_viaje END,
        CASE WHEN UPPER(v.metodo_pago) LIKE '%TARJ%' THEN v.costo_viaje ELSE 0 END,
        v.notas,
        v.datos_escaneo,
        'MIGRADO'
    FROM ControlTaxis.dbo.RegistrosViajes v
    INNER JOIN dbo.AppMovilFolioControl c
        ON c.FolioAppOriginal = v.id_registro
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.AppMovilRegistro r
        WHERE r.folio_app = c.FolioControl
           OR r.folio_app_original = v.id_registro
    );
END;
GO
