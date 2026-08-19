IF OBJECT_ID('dbo.Staff_Vendedores', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Staff_Vendedores
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Clave NVARCHAR(20) NOT NULL,
        Nombre NVARCHAR(150) NOT NULL,
        Telefono NVARCHAR(30) NOT NULL CONSTRAINT DF_triton_staff_telefono DEFAULT '',
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_staff_estatus DEFAULT 'Activo',
        FechaAlta DATETIME2 NOT NULL CONSTRAINT DF_triton_staff_fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_triton_staff_clave ON dbo.Staff_Vendedores(Clave);
END;
GO

IF OBJECT_ID('dbo.Ventas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Ventas
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Folio NVARCHAR(30) NOT NULL,
        Fecha DATETIME2 NOT NULL,
        StaffId INT NULL,
        Pax INT NOT NULL CONSTRAINT DF_triton_ventas_pax DEFAULT 0,
        Hotel NVARCHAR(150) NOT NULL CONSTRAINT DF_triton_ventas_hotel DEFAULT '',
        TipoOperacion NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_ventas_tipo DEFAULT 'VENTA',
        Subtotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_subtotal DEFAULT 0,
        Iva DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_iva DEFAULT 0,
        Total DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_total DEFAULT 0,
        Saldo DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_saldo DEFAULT 0,
        Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_ventas_usuario DEFAULT '',
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_ventas_estatus DEFAULT 'Abierta',
        CONSTRAINT FK_triton_ventas_staff FOREIGN KEY (StaffId) REFERENCES dbo.Staff_Vendedores(Id)
    );
    CREATE UNIQUE INDEX UX_triton_ventas_folio ON dbo.Ventas(Folio);
    CREATE INDEX IX_triton_ventas_fecha ON dbo.Ventas(Fecha DESC);
END;
GO

IF OBJECT_ID('dbo.Ventas', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Ventas', 'Id') IS NULL
        ALTER TABLE dbo.Ventas ADD Id INT IDENTITY(1,1) NOT NULL;
    IF COL_LENGTH('dbo.Ventas', 'Folio') IS NULL
        ALTER TABLE dbo.Ventas ADD Folio NVARCHAR(30) NULL;
    IF COL_LENGTH('dbo.Ventas', 'Fecha') IS NULL
        ALTER TABLE dbo.Ventas ADD Fecha DATETIME2 NULL;
    IF COL_LENGTH('dbo.Ventas', 'StaffId') IS NULL
        ALTER TABLE dbo.Ventas ADD StaffId INT NULL;
    IF COL_LENGTH('dbo.Ventas', 'Pax') IS NULL
        ALTER TABLE dbo.Ventas ADD Pax INT NOT NULL CONSTRAINT DF_triton_ventas_pax_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Ventas', 'Hotel') IS NULL
        ALTER TABLE dbo.Ventas ADD Hotel NVARCHAR(150) NOT NULL CONSTRAINT DF_triton_ventas_hotel_existing DEFAULT '';
    IF COL_LENGTH('dbo.Ventas', 'TipoOperacion') IS NULL
        ALTER TABLE dbo.Ventas ADD TipoOperacion NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_ventas_tipo_existing DEFAULT 'VENTA';
    IF COL_LENGTH('dbo.Ventas', 'Subtotal') IS NULL
        ALTER TABLE dbo.Ventas ADD Subtotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_subtotal_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Ventas', 'Iva') IS NULL
        ALTER TABLE dbo.Ventas ADD Iva DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_iva_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Ventas', 'Total') IS NULL
        ALTER TABLE dbo.Ventas ADD Total DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_total_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Ventas', 'Saldo') IS NULL
        ALTER TABLE dbo.Ventas ADD Saldo DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_ventas_saldo_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Ventas', 'Usuario') IS NULL
        ALTER TABLE dbo.Ventas ADD Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_ventas_usuario_existing DEFAULT '';
    IF COL_LENGTH('dbo.Ventas', 'Estatus') IS NULL
        ALTER TABLE dbo.Ventas ADD Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_ventas_estatus_existing DEFAULT 'Abierta';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_triton_ventas_id' AND object_id = OBJECT_ID('dbo.Ventas'))
BEGIN
    CREATE UNIQUE INDEX UX_triton_ventas_id ON dbo.Ventas(Id);
END;
GO

IF COL_LENGTH('dbo.Ventas', 'Folio') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_triton_ventas_folio' AND object_id = OBJECT_ID('dbo.Ventas'))
BEGIN
    CREATE UNIQUE INDEX UX_triton_ventas_folio ON dbo.Ventas(Folio) WHERE Folio IS NOT NULL AND Folio <> '';
END;
GO

IF OBJECT_ID('dbo.VentaDetalle', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.VentaDetalle
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        VentaId INT NOT NULL,
        ProductoId INT NULL,
        Cantidad DECIMAL(18,2) NOT NULL,
        Descripcion NVARCHAR(250) NOT NULL,
        Precio DECIMAL(18,2) NOT NULL,
        Importe DECIMAL(18,2) NOT NULL,
        Descuento DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_detalle_desc DEFAULT 0,
        Departamento NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_detalle_depto DEFAULT '',
        CONSTRAINT FK_triton_detalle_venta FOREIGN KEY (VentaId) REFERENCES dbo.Ventas(Id)
    );
END;
GO

IF OBJECT_ID('dbo.Pagos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Pagos
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        VentaId INT NOT NULL,
        FormaPago NVARCHAR(40) NOT NULL,
        Moneda NVARCHAR(10) NOT NULL CONSTRAINT DF_triton_pagos_moneda DEFAULT 'MXN',
        TipoCambio DECIMAL(18,4) NOT NULL CONSTRAINT DF_triton_pagos_tc DEFAULT 1,
        Importe DECIMAL(18,2) NOT NULL,
        Referencia NVARCHAR(100) NOT NULL CONSTRAINT DF_triton_pagos_ref DEFAULT '',
        FechaPago DATETIME2 NOT NULL CONSTRAINT DF_triton_pagos_fecha DEFAULT SYSUTCDATETIME(),
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_pagos_estatus DEFAULT 'Aplicado',
        CONSTRAINT FK_triton_pagos_venta FOREIGN KEY (VentaId) REFERENCES dbo.Ventas(Id)
    );
END;
GO

IF OBJECT_ID('dbo.Comisiones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Comisiones
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        VentaId INT NOT NULL,
        BeneficiarioTipo NVARCHAR(30) NOT NULL,
        BeneficiarioId INT NULL,
        BaseCalculo DECIMAL(18,2) NOT NULL,
        Porcentaje DECIMAL(9,4) NOT NULL CONSTRAINT DF_triton_comisiones_porcentaje DEFAULT 0,
        Importe DECIMAL(18,2) NOT NULL,
        Pagado BIT NOT NULL CONSTRAINT DF_triton_comisiones_pagado DEFAULT 0,
        FechaPago DATETIME2 NULL,
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_comisiones_estatus DEFAULT 'Pendiente',
        CONSTRAINT FK_triton_comisiones_venta FOREIGN KEY (VentaId) REFERENCES dbo.Ventas(Id)
    );
END;
GO

IF OBJECT_ID('dbo.Transportes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Transportes
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Clave NVARCHAR(20) NOT NULL,
        Nombre NVARCHAR(150) NOT NULL,
        Minimo DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_transportes_min DEFAULT 0,
        Maximo DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_transportes_max DEFAULT 0,
        Comision DECIMAL(9,4) NOT NULL CONSTRAINT DF_triton_transportes_com DEFAULT 0,
        DescEfectivo DECIMAL(9,4) NOT NULL CONSTRAINT DF_triton_transportes_desc_ef DEFAULT 0,
        DescTarjeta DECIMAL(9,4) NOT NULL CONSTRAINT DF_triton_transportes_desc_tj DEFAULT 0,
        DescAmex DECIMAL(9,4) NOT NULL CONSTRAINT DF_triton_transportes_desc_ax DEFAULT 0,
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_transportes_est DEFAULT 'Activo'
    );
    CREATE UNIQUE INDEX UX_triton_transportes_clave ON dbo.Transportes(Clave);
END;
GO

IF OBJECT_ID('dbo.Guias', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Guias
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Clave NVARCHAR(20) NOT NULL,
        Nombre NVARCHAR(150) NOT NULL,
        Telefono NVARCHAR(30) NOT NULL CONSTRAINT DF_triton_guias_tel DEFAULT '',
        Comision DECIMAL(9,4) NOT NULL CONSTRAINT DF_triton_guias_com DEFAULT 0,
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_guias_est DEFAULT 'Activo'
    );
    CREATE UNIQUE INDEX UX_triton_guias_clave ON dbo.Guias(Clave);
END;
GO

IF OBJECT_ID('dbo.Taxistas', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Taxistas
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Clave NVARCHAR(20) NOT NULL,
        Nombre NVARCHAR(150) NOT NULL,
        Telefono NVARCHAR(30) NOT NULL CONSTRAINT DF_triton_taxistas_tel DEFAULT '',
        Unidad NVARCHAR(80) NOT NULL CONSTRAINT DF_triton_taxistas_unidad DEFAULT '',
        Placas NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_taxistas_placas DEFAULT '',
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_taxistas_est DEFAULT 'Activo'
    );
    CREATE UNIQUE INDEX UX_triton_taxistas_clave ON dbo.Taxistas(Clave);
END;
GO

IF OBJECT_ID('dbo.Gafetes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Gafetes
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Numero NVARCHAR(30) NOT NULL,
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_gafetes_est DEFAULT 'Disponible'
    );
    CREATE UNIQUE INDEX UX_triton_gafetes_numero ON dbo.Gafetes(Numero);
END;
GO

IF OBJECT_ID('dbo.GafeteAsignacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GafeteAsignacion
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        GafeteId INT NOT NULL,
        StaffId INT NOT NULL,
        FechaEntrega DATETIME2 NOT NULL CONSTRAINT DF_triton_gaf_asig_entrega DEFAULT SYSUTCDATETIME(),
        FechaRegreso DATETIME2 NULL,
        Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_gaf_asig_usuario DEFAULT '',
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_gaf_asig_est DEFAULT 'Asignado',
        CONSTRAINT FK_triton_gaf_asig_gafete FOREIGN KEY (GafeteId) REFERENCES dbo.Gafetes(Id),
        CONSTRAINT FK_triton_gaf_asig_staff FOREIGN KEY (StaffId) REFERENCES dbo.Staff_Vendedores(Id)
    );
    CREATE INDEX IX_triton_gaf_asig_activa ON dbo.GafeteAsignacion(GafeteId, Estatus);
END;
GO

IF OBJECT_ID('dbo.Gastos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Gastos
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Fecha DATETIME2 NOT NULL CONSTRAINT DF_triton_gastos_fecha DEFAULT SYSUTCDATETIME(),
        Concepto NVARCHAR(150) NOT NULL,
        Importe DECIMAL(18,2) NOT NULL,
        Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_gastos_usuario DEFAULT '',
        Observaciones NVARCHAR(500) NOT NULL CONSTRAINT DF_triton_gastos_obs DEFAULT ''
    );
END;
GO

IF OBJECT_ID('dbo.PosOperacionBeneficiarios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PosOperacionBeneficiarios
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FolioOperacion NVARCHAR(30) NOT NULL,
        StaffClave NVARCHAR(20) NOT NULL CONSTRAINT DF_posbenef_staffclave DEFAULT '',
        StaffNombre NVARCHAR(100) NOT NULL CONSTRAINT DF_posbenef_staff DEFAULT '',
        TransporteTipo NVARCHAR(10) NOT NULL CONSTRAINT DF_posbenef_trans DEFAULT '',
        GuiaMatricula INT NOT NULL CONSTRAINT DF_posbenef_guia DEFAULT 0,
        TaxistaId BIGINT NOT NULL CONSTRAINT DF_posbenef_taxista DEFAULT 0,
        TaxistaNombre NVARCHAR(80) NOT NULL CONSTRAINT DF_posbenef_taxistanombre DEFAULT '',
        Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_posbenef_usuario DEFAULT '',
        Fecha DATETIME2 NOT NULL CONSTRAINT DF_posbenef_fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_posbenef_folio ON dbo.PosOperacionBeneficiarios(FolioOperacion);
END;
GO

IF OBJECT_ID('dbo.RelacionTicketTaxista', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RelacionTicketTaxista
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FolioApp NVARCHAR(60) NOT NULL,
        FolioOperacion NVARCHAR(60) NOT NULL,
        FolioPos NVARCHAR(120) NOT NULL CONSTRAINT DF_reltaxi_foliopos DEFAULT '',
        Gafete NVARCHAR(30) NOT NULL CONSTRAINT DF_reltaxi_gafete DEFAULT '',
        TaxistaId BIGINT NOT NULL,
        TaxistaNombre NVARCHAR(150) NOT NULL CONSTRAINT DF_reltaxi_taxista DEFAULT '',
        TransporteTipo NVARCHAR(20) NOT NULL CONSTRAINT DF_reltaxi_transporte DEFAULT '',
        Observaciones NVARCHAR(300) NOT NULL CONSTRAINT DF_reltaxi_obs DEFAULT '',
        Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_reltaxi_usuario DEFAULT '',
        FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_reltaxi_fecha DEFAULT SYSUTCDATETIME(),
        FechaActualizacion DATETIME2 NOT NULL CONSTRAINT DF_reltaxi_actualiza DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_reltaxi_folioapp ON dbo.RelacionTicketTaxista(FolioApp);
    CREATE INDEX IX_reltaxi_foliooperacion ON dbo.RelacionTicketTaxista(FolioOperacion);
    CREATE INDEX IX_reltaxi_taxista ON dbo.RelacionTicketTaxista(TaxistaId);
END;
GO

IF OBJECT_ID('dbo.Cortes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Cortes
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Fecha DATE NOT NULL,
        Usuario NVARCHAR(50) NOT NULL,
        Efectivo DECIMAL(18,2) NOT NULL,
        Tarjeta DECIMAL(18,2) NOT NULL,
        Total DECIMAL(18,2) NOT NULL,
        Diferencia DECIMAL(18,2) NOT NULL,
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_cortes_estatus DEFAULT 'Cerrado',
        FechaCierre DATETIME2 NOT NULL CONSTRAINT DF_triton_cortes_cierre DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_triton_cortes_fecha ON dbo.Cortes(Fecha);
END;
GO

IF OBJECT_ID('dbo.Cortes', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Cortes', 'Id') IS NULL
        ALTER TABLE dbo.Cortes ADD Id INT IDENTITY(1,1) NOT NULL;
    IF COL_LENGTH('dbo.Cortes', 'Usuario') IS NULL
        ALTER TABLE dbo.Cortes ADD Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_cortes_usuario_existing DEFAULT '';
    IF COL_LENGTH('dbo.Cortes', 'Efectivo') IS NULL
        ALTER TABLE dbo.Cortes ADD Efectivo DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_cortes_efectivo_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Cortes', 'Tarjeta') IS NULL
        ALTER TABLE dbo.Cortes ADD Tarjeta DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_cortes_tarjeta_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Cortes', 'Total') IS NULL
        ALTER TABLE dbo.Cortes ADD Total DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_cortes_total_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Cortes', 'Diferencia') IS NULL
        ALTER TABLE dbo.Cortes ADD Diferencia DECIMAL(18,2) NOT NULL CONSTRAINT DF_triton_cortes_diferencia_existing DEFAULT 0;
    IF COL_LENGTH('dbo.Cortes', 'Estatus') IS NULL
        ALTER TABLE dbo.Cortes ADD Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_cortes_estatus_existing DEFAULT 'Cerrado';
    IF COL_LENGTH('dbo.Cortes', 'FechaCierre') IS NULL
        ALTER TABLE dbo.Cortes ADD FechaCierre DATETIME2 NOT NULL CONSTRAINT DF_triton_cortes_cierre_existing DEFAULT SYSUTCDATETIME();
END;
GO

IF OBJECT_ID('dbo.Auditoria', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Auditoria
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Usuario NVARCHAR(50) NOT NULL,
        Fecha DATETIME2 NOT NULL CONSTRAINT DF_triton_aud_fecha DEFAULT SYSUTCDATETIME(),
        Modulo NVARCHAR(50) NOT NULL,
        Accion NVARCHAR(50) NOT NULL,
        IdRegistro NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_aud_id DEFAULT '',
        Descripcion NVARCHAR(500) NOT NULL CONSTRAINT DF_triton_aud_desc DEFAULT '',
        Equipo NVARCHAR(100) NOT NULL CONSTRAINT DF_triton_aud_equipo DEFAULT HOST_NAME(),
        Exito BIT NOT NULL CONSTRAINT DF_triton_aud_exito DEFAULT 1,
        DetalleJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_triton_aud_json DEFAULT '{}'
    );
END;
GO

IF OBJECT_ID('dbo.Auditoria', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.Auditoria', 'Id') IS NULL
        ALTER TABLE dbo.Auditoria ADD Id INT IDENTITY(1,1) NOT NULL;
    IF COL_LENGTH('dbo.Auditoria', 'Usuario') IS NULL
        ALTER TABLE dbo.Auditoria ADD Usuario NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_aud_usuario_existing DEFAULT '';
    IF COL_LENGTH('dbo.Auditoria', 'Fecha') IS NULL
        ALTER TABLE dbo.Auditoria ADD Fecha DATETIME2 NOT NULL CONSTRAINT DF_triton_aud_fecha_existing DEFAULT SYSUTCDATETIME();
    IF COL_LENGTH('dbo.Auditoria', 'Modulo') IS NULL
        ALTER TABLE dbo.Auditoria ADD Modulo NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_aud_modulo_existing DEFAULT '';
    IF COL_LENGTH('dbo.Auditoria', 'Accion') IS NULL
        ALTER TABLE dbo.Auditoria ADD Accion NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_aud_accion_existing DEFAULT '';
    IF COL_LENGTH('dbo.Auditoria', 'IdRegistro') IS NULL
        ALTER TABLE dbo.Auditoria ADD IdRegistro NVARCHAR(50) NOT NULL CONSTRAINT DF_triton_aud_id_existing DEFAULT '';
    IF COL_LENGTH('dbo.Auditoria', 'Descripcion') IS NULL
        ALTER TABLE dbo.Auditoria ADD Descripcion NVARCHAR(500) NOT NULL CONSTRAINT DF_triton_aud_desc_existing DEFAULT '';
    IF COL_LENGTH('dbo.Auditoria', 'Equipo') IS NULL
        ALTER TABLE dbo.Auditoria ADD Equipo NVARCHAR(100) NOT NULL CONSTRAINT DF_triton_aud_equipo_existing DEFAULT HOST_NAME();
    IF COL_LENGTH('dbo.Auditoria', 'Exito') IS NULL
        ALTER TABLE dbo.Auditoria ADD Exito BIT NOT NULL CONSTRAINT DF_triton_aud_exito_existing DEFAULT 1;
    IF COL_LENGTH('dbo.Auditoria', 'DetalleJson') IS NULL
        ALTER TABLE dbo.Auditoria ADD DetalleJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_triton_aud_json_existing DEFAULT '{}';
END;
GO

IF OBJECT_ID('dbo.Usuarios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Usuarios
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Usuario NVARCHAR(50) NOT NULL,
        PasswordHash NVARCHAR(200) NOT NULL,
        Rol NVARCHAR(30) NOT NULL CONSTRAINT DF_triton_usuarios_rol DEFAULT 'Cajero',
        Estatus NVARCHAR(20) NOT NULL CONSTRAINT DF_triton_usuarios_est DEFAULT 'Activo',
        FechaAlta DATETIME2 NOT NULL CONSTRAINT DF_triton_usuarios_fecha DEFAULT SYSUTCDATETIME()
    );
    CREATE UNIQUE INDEX UX_triton_usuarios_usuario ON dbo.Usuarios(Usuario);
    INSERT INTO dbo.Usuarios (Usuario, PasswordHash, Rol, Estatus)
    VALUES ('WEB', 'WEB', 'Administrador', 'Activo');
END;
GO

IF OBJECT_ID('dbo.UsuarioPermisos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.UsuarioPermisos
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Usuario NVARCHAR(50) NOT NULL,
        Modulo NVARCHAR(50) NOT NULL,
        PuedeVer BIT NOT NULL CONSTRAINT DF_triton_usuariopermisos_ver DEFAULT 1
    );
    CREATE UNIQUE INDEX UX_triton_usuariopermisos_usuario_modulo ON dbo.UsuarioPermisos(Usuario, Modulo);
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.UsuarioPermisos WHERE UPPER(Usuario) = 'WEB')
BEGIN
    INSERT INTO dbo.UsuarioPermisos (Usuario, Modulo, PuedeVer)
    VALUES
        ('WEB', 'RegistroDiario', 1),
        ('WEB', 'Ventas', 1),
        ('WEB', 'Pagos', 1),
        ('WEB', 'Comisiones', 1),
        ('WEB', 'Transportes', 1),
        ('WEB', 'Guias', 1),
        ('WEB', 'Taxistas', 1),
        ('WEB', 'Gafetes', 1),
        ('WEB', 'Relaciones', 1),
        ('WEB', 'Gastos', 1),
        ('WEB', 'Cortes', 1),
        ('WEB', 'Reportes', 1),
        ('WEB', 'Usuarios', 1);
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.UsuarioPermisos WHERE UPPER(Usuario) = 'WEB' AND Modulo = 'Relaciones')
BEGIN
    INSERT INTO dbo.UsuarioPermisos (Usuario, Modulo, PuedeVer)
    VALUES ('WEB', 'Relaciones', 1);
END;
GO


