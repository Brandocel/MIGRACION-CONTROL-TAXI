USE [mktCasco];
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

/*
    CASCO_VIEJO_ESQUEMA_COMPLETO.sql
    Inventario consolidado e idempotente para replicar el esquema SQL Server usado por Casco Viejo.

    Alcance:
    - Objetos confirmados en mktCasco
    - Ajustes documentados en el codigo fuente de Casco
    - Sin Plaza 28
*/

/* ============================================================
   1. TABLAS NUEVAS / CONTROL APP MOVIL
   ============================================================ */

IF OBJECT_ID('dbo.AppMovilFolioControl', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilFolioControl
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilFolioControl PRIMARY KEY,
        FolioAppOriginal NVARCHAR(120) NOT NULL,
        FolioControl NVARCHAR(120) NOT NULL,
        FechaCreacion DATETIME2(7) NOT NULL CONSTRAINT DF_AppMovilFolioControl_Fecha DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilFolioControl') AND name = 'UX_AppMovilFolioControl_Original')
BEGIN
    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Original
        ON dbo.AppMovilFolioControl(FolioAppOriginal);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilFolioControl') AND name = 'UX_AppMovilFolioControl_Control')
BEGIN
    CREATE UNIQUE INDEX UX_AppMovilFolioControl_Control
        ON dbo.AppMovilFolioControl(FolioControl);
END;
GO

IF OBJECT_ID('dbo.AppMovilRegistro', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilRegistro
    (
        id_app_movil_registro INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistro PRIMARY KEY,
        folio_app NVARCHAR(120) NOT NULL,
        folio_pos NVARCHAR(200) NOT NULL CONSTRAINT DF_AppMovilRegistro_folio_pos DEFAULT '',
        fecha_operacion DATETIME2(7) NOT NULL CONSTRAINT DF_AppMovilRegistro_fecha_operacion DEFAULT SYSUTCDATETIME(),
        vendedor_clave NVARCHAR(100) NOT NULL CONSTRAINT DF_AppMovilRegistro_vendedor_clave DEFAULT '',
        vendedor_nombre NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_vendedor_nombre DEFAULT '',
        hotel NVARCHAR(400) NOT NULL CONSTRAINT DF_AppMovilRegistro_hotel DEFAULT '',
        pax INT NOT NULL CONSTRAINT DF_AppMovilRegistro_pax DEFAULT 0,
        tipo_operacion NVARCHAR(160) NOT NULL CONSTRAINT DF_AppMovilRegistro_tipo_operacion DEFAULT '',
        subtotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_subtotal DEFAULT 0,
        iva DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_iva DEFAULT 0,
        total DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_total DEFAULT 0,
        efectivo DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_efectivo DEFAULT 0,
        tarjeta DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_tarjeta DEFAULT 0,
        dolares DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_dolares DEFAULT 0,
        tipo_cambio DECIMAL(18,2) NOT NULL CONSTRAINT DF_AppMovilRegistro_tipo_cambio DEFAULT 1,
        usuario_movil NVARCHAR(160) NOT NULL CONSTRAINT DF_AppMovilRegistro_usuario_movil DEFAULT '',
        notas NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AppMovilRegistro_notas DEFAULT '',
        detalle_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AppMovilRegistro_detalle_json DEFAULT '',
        pagos_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_AppMovilRegistro_pagos_json DEFAULT '',
        origen NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_origen DEFAULT '',
        estado_sync NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_estado_sync DEFAULT 'pendiente',
        fecha_creacion DATETIME2(7) NOT NULL CONSTRAINT DF_AppMovilRegistro_fecha_creacion DEFAULT SYSUTCDATETIME(),
        folio_app_original NVARCHAR(120) NOT NULL CONSTRAINT DF_AppMovilRegistro_folio_app_original DEFAULT '',
        folio_gafete NVARCHAR(600) NOT NULL CONSTRAINT DF_AppMovilRegistro_folio_gafete DEFAULT '',
        id_catalogo INT NULL,
        telefono_taxista NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_telefono_taxista DEFAULT '',
        telefono_contacto NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_telefono_contacto DEFAULT '',
        placas NVARCHAR(100) NOT NULL CONSTRAINT DF_AppMovilRegistro_placas DEFAULT '',
        modelo_vehiculo NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_modelo_vehiculo DEFAULT '',
        unidad NVARCHAR(100) NOT NULL CONSTRAINT DF_AppMovilRegistro_unidad DEFAULT '',
        sitio NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_sitio DEFAULT '',
        destino NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_destino DEFAULT '',
        comision_calculada DECIMAL(18,2) NULL,
        pago_comision DECIMAL(18,2) NULL,
        fecha_pago_comision DATETIME2(7) NULL,
        nacionalidad NVARCHAR(240) NOT NULL CONSTRAINT DF_AppMovilRegistro_nacionalidad DEFAULT '',
        estado_pago_dejada NVARCHAR(40) NOT NULL CONSTRAINT DF_AppMovilRegistro_estado_pago_dejada DEFAULT 'pendiente',
        fecha_pago_dejada DATETIME2(7) NULL,
        usuario_pago_dejada NVARCHAR(360) NOT NULL CONSTRAINT DF_AppMovilRegistro_usuario_pago_dejada DEFAULT '',
        ticket_pago_dejada NVARCHAR(160) NOT NULL CONSTRAINT DF_AppMovilRegistro_ticket_pago_dejada DEFAULT '',
        payout_status NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_payout_status DEFAULT 'pending',
        payout_date DATETIME2(7) NULL,
        payout_user NVARCHAR(160) NOT NULL CONSTRAINT DF_AppMovilRegistro_payout_user DEFAULT '',
        payout_ticket NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilRegistro_payout_ticket DEFAULT ''
    );
END;
GO

IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_app_original') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD folio_app_original NVARCHAR(120) NOT NULL CONSTRAINT DF_AppMovilRegistro_folio_app_original_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_pos') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD folio_pos NVARCHAR(200) NOT NULL CONSTRAINT DF_AppMovilRegistro_folio_pos_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'id_catalogo') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD id_catalogo INT NULL;
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'folio_gafete') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD folio_gafete NVARCHAR(600) NOT NULL CONSTRAINT DF_AppMovilRegistro_folio_gafete_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_taxista') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD telefono_taxista NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_telefono_taxista_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'telefono_contacto') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD telefono_contacto NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_telefono_contacto_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'nacionalidad') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD nacionalidad NVARCHAR(240) NOT NULL CONSTRAINT DF_AppMovilRegistro_nacionalidad_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'placas') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD placas NVARCHAR(100) NOT NULL CONSTRAINT DF_AppMovilRegistro_placas_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'modelo_vehiculo') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD modelo_vehiculo NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_modelo_vehiculo_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'unidad') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD unidad NVARCHAR(100) NOT NULL CONSTRAINT DF_AppMovilRegistro_unidad_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'sitio') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD sitio NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_sitio_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'destino') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD destino NVARCHAR(300) NOT NULL CONSTRAINT DF_AppMovilRegistro_destino_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'estado_pago_dejada') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD estado_pago_dejada NVARCHAR(40) NOT NULL CONSTRAINT DF_AppMovilRegistro_estado_pago_dejada_alt DEFAULT 'pendiente';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'fecha_pago_dejada') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD fecha_pago_dejada DATETIME2(7) NULL;
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'usuario_pago_dejada') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD usuario_pago_dejada NVARCHAR(360) NOT NULL CONSTRAINT DF_AppMovilRegistro_usuario_pago_dejada_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'ticket_pago_dejada') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD ticket_pago_dejada NVARCHAR(160) NOT NULL CONSTRAINT DF_AppMovilRegistro_ticket_pago_dejada_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_status') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD payout_status NVARCHAR(60) NOT NULL CONSTRAINT DF_AppMovilRegistro_payout_status_alt DEFAULT 'pending';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_date') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD payout_date DATETIME2(7) NULL;
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_user') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD payout_user NVARCHAR(160) NOT NULL CONSTRAINT DF_AppMovilRegistro_payout_user_alt DEFAULT '';
GO
IF COL_LENGTH('dbo.AppMovilRegistro', 'payout_ticket') IS NULL
    ALTER TABLE dbo.AppMovilRegistro ADD payout_ticket NVARCHAR(80) NOT NULL CONSTRAINT DF_AppMovilRegistro_payout_ticket_alt DEFAULT '';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistro') AND name = 'UX_AppMovilRegistro_folio_app')
BEGIN
    IF NOT EXISTS (
        SELECT folio_app
        FROM dbo.AppMovilRegistro
        WHERE NULLIF(LTRIM(RTRIM(folio_app)), '') IS NOT NULL
        GROUP BY folio_app
        HAVING COUNT(1) > 1
    )
    BEGIN
        CREATE UNIQUE INDEX UX_AppMovilRegistro_folio_app
            ON dbo.AppMovilRegistro(folio_app);
    END;
END;
GO

/* Intentado por codigo fuente; en la base actual no aparece materializado */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistro') AND name = 'UX_AppMovilRegistro_FolioOriginal')
BEGIN
    IF NOT EXISTS (
        SELECT folio_app_original
        FROM dbo.AppMovilRegistro
        WHERE NULLIF(LTRIM(RTRIM(folio_app_original)), '') IS NOT NULL
        GROUP BY folio_app_original
        HAVING COUNT(1) > 1
    )
    BEGIN
        CREATE UNIQUE INDEX UX_AppMovilRegistro_FolioOriginal
            ON dbo.AppMovilRegistro(folio_app_original)
            WHERE folio_app_original IS NOT NULL AND folio_app_original <> '';
    END;
END;
GO

IF OBJECT_ID('dbo.AppMovilRegistroGafetes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilRegistroGafetes
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppMovilRegistroGafetes PRIMARY KEY,
        FolioApp NVARCHAR(120) NOT NULL,
        IdCatalogo INT NULL,
        FolioGafete NVARCHAR(40) NOT NULL,
        FechaCreacion DATETIME2(7) NOT NULL CONSTRAINT DF_AppMovilRegistroGafetes_Fecha DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistroGafetes') AND name = 'UX_AppMovilRegistroGafetes_FolioApp_Gafete')
BEGIN
    CREATE UNIQUE INDEX UX_AppMovilRegistroGafetes_FolioApp_Gafete
        ON dbo.AppMovilRegistroGafetes(FolioApp, FolioGafete);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AppMovilRegistroGafetes') AND name = 'IX_AppMovilRegistroGafetes_IdCatalogo_FolioGafete')
BEGIN
    CREATE INDEX IX_AppMovilRegistroGafetes_IdCatalogo_FolioGafete
        ON dbo.AppMovilRegistroGafetes(IdCatalogo, FolioGafete);
END;
GO

/* Fuente definida en script de sincronizacion; no confirmada hoy en mktCasco */
IF OBJECT_ID('dbo.AppMovilFoliosBloqueados', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppMovilFoliosBloqueados
    (
        Folio NVARCHAR(60) NOT NULL CONSTRAINT PK_AppMovilFoliosBloqueados PRIMARY KEY,
        Motivo NVARCHAR(200) NOT NULL CONSTRAINT DF_AppMovilFoliosBloqueados_Motivo DEFAULT '',
        FechaCreacion DATETIME2(7) NOT NULL CONSTRAINT DF_AppMovilFoliosBloqueados_Fecha DEFAULT SYSUTCDATETIME()
    );
END;
GO

/* ============================================================
   2. RELACION TICKET / TAXISTA
   ============================================================ */

IF OBJECT_ID('dbo.RelacionTicketTaxista', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelacionTicketTaxista
    (
        Id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_RelacionTicketTaxista PRIMARY KEY,
        FolioApp NVARCHAR(120) NOT NULL,
        FolioOperacion NVARCHAR(120) NOT NULL,
        FolioPos NVARCHAR(240) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_FolioPos DEFAULT '',
        Gafete NVARCHAR(600) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_Gafete DEFAULT '',
        TaxistaId BIGINT NOT NULL,
        TaxistaNombre NVARCHAR(300) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_TaxistaNombre DEFAULT '',
        Vendedor NVARCHAR(300) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_Vendedor DEFAULT '',
        TransporteTipo NVARCHAR(40) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_TransporteTipo DEFAULT '',
        Dejada DECIMAL(18,2) NULL,
        Observaciones NVARCHAR(600) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_Observaciones DEFAULT '',
        Usuario NVARCHAR(100) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_Usuario DEFAULT '',
        FechaCreacion DATETIME2(7) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_FechaCreacion DEFAULT SYSUTCDATETIME(),
        FechaActualizacion DATETIME2(7) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_FechaActualizacion DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF COL_LENGTH('dbo.RelacionTicketTaxista', 'Vendedor') IS NULL
    ALTER TABLE dbo.RelacionTicketTaxista ADD Vendedor NVARCHAR(300) NOT NULL CONSTRAINT DF_RelacionTicketTaxista_Vendedor_Alt DEFAULT '';
GO
IF COL_LENGTH('dbo.RelacionTicketTaxista', 'Dejada') IS NULL
    ALTER TABLE dbo.RelacionTicketTaxista ADD Dejada DECIMAL(18,2) NULL;
GO
IF COL_LENGTH('dbo.RelacionTicketTaxista', 'Gafete') IS NOT NULL
BEGIN
    DECLARE @gafeteMaxLengthRelTaxi INT;
    SELECT @gafeteMaxLengthRelTaxi = max_length
    FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.RelacionTicketTaxista')
      AND name = 'Gafete';

    IF COALESCE(@gafeteMaxLengthRelTaxi, 0) > 0 AND @gafeteMaxLengthRelTaxi < 600
        ALTER TABLE dbo.RelacionTicketTaxista ALTER COLUMN Gafete NVARCHAR(600) NOT NULL;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RelacionTicketTaxista') AND name = 'UX_RelacionTicketTaxista_FolioApp')
BEGIN
    CREATE UNIQUE INDEX UX_RelacionTicketTaxista_FolioApp
        ON dbo.RelacionTicketTaxista(FolioApp);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RelacionTicketTaxista') AND name = 'IX_RelacionTicketTaxista_FolioOperacion')
BEGIN
    CREATE INDEX IX_RelacionTicketTaxista_FolioOperacion
        ON dbo.RelacionTicketTaxista(FolioOperacion);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.RelacionTicketTaxista') AND name = 'IX_RelacionTicketTaxista_TaxistaId')
BEGIN
    CREATE INDEX IX_RelacionTicketTaxista_TaxistaId
        ON dbo.RelacionTicketTaxista(TaxistaId);
END;
GO

/* ============================================================
   3. AJUSTES SOBRE TABLAS YA EXISTENTES
   ============================================================ */

IF OBJECT_ID('dbo.dejadas', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'nacionalidad') IS NULL
BEGIN
    ALTER TABLE dbo.dejadas ADD nacionalidad NVARCHAR(120) NULL;
END;
GO

/* Intentado por codigo fuente; no confirmado en la base actual */
IF OBJECT_ID('dbo.dejadas', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'folioregistrostr') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'gafete') IS NOT NULL
   AND COL_LENGTH('dbo.dejadas', 'fecha') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.dejadas') AND name = 'UX_dejadas_AppMovil_Folio_Gafete_Fecha')
BEGIN
    IF NOT EXISTS (
        SELECT folioregistrostr, gafete, CONVERT(date, fecha)
        FROM dbo.dejadas
        WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioregistrostr, ''))), '') IS NOT NULL
          AND NULLIF(LTRIM(RTRIM(COALESCE(gafete, ''))), '') IS NOT NULL
        GROUP BY folioregistrostr, gafete, CONVERT(date, fecha)
        HAVING COUNT(1) > 1
    )
    BEGIN
        CREATE UNIQUE INDEX UX_dejadas_AppMovil_Folio_Gafete_Fecha
            ON dbo.dejadas(folioregistrostr, gafete, fecha)
            WHERE folioregistrostr IS NOT NULL AND gafete IS NOT NULL;
    END;
END;
GO

/* Intentado por codigo fuente; no confirmado en la base actual */
IF OBJECT_ID('dbo.gafete', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.gafete', 'folioperacion') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.gafete') AND name = 'UX_gafete_AppMovil_Folio_Gafete_Fecha_A')
BEGIN
    IF NOT EXISTS (
        SELECT folioperacion, gafete, CONVERT(date, fecha)
        FROM dbo.gafete
        WHERE NULLIF(LTRIM(RTRIM(COALESCE(folioperacion, ''))), '') IS NOT NULL
          AND UPPER(LTRIM(RTRIM(COALESCE(venta, '')))) = 'A'
        GROUP BY folioperacion, gafete, CONVERT(date, fecha)
        HAVING COUNT(1) > 1
    )
    BEGIN
        CREATE UNIQUE INDEX UX_gafete_AppMovil_Folio_Gafete_Fecha_A
            ON dbo.gafete(folioperacion, gafete, fecha)
            WHERE folioperacion IS NOT NULL AND venta = 'A';
    END;
END;
GO

/* ============================================================
   4. VISTA DE APOYO
   ============================================================ */

IF OBJECT_ID('dbo.vw_AppMovilRegistrosViajes', 'V') IS NOT NULL
    DROP VIEW dbo.vw_AppMovilRegistrosViajes;
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

