/*
    Control Taxi - Casco Viejo
    Tabla preparada para persistir comisiones calculadas en una fase posterior.

    IMPORTANTE:
    - Este script es idempotente.
    - No inserta registros.
    - No activa calculos.
    - No toca Plaza 28.
*/

IF OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ControlTaxiComisionesGeneradas
    (
        Id BIGINT IDENTITY(1,1) NOT NULL,
        BranchCode NVARCHAR(10) NOT NULL,
        FolioOriginal NVARCHAR(120) NOT NULL,
        FolioOperacion INT NOT NULL,
        TicketPos NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_TicketPos DEFAULT (N''),
        Taxista NVARCHAR(200) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Taxista DEFAULT (N''),
        Gafete NVARCHAR(50) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Gafete DEFAULT (N''),
        Transporte NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Transporte DEFAULT (N''),
        FormaPago NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FormaPago DEFAULT (N''),
        VentaCompuadmo DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaCompuadmo DEFAULT ((0)),
        VentaJoyeria DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaJoyeria DEFAULT ((0)),
        VentaTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaTotal DEFAULT ((0)),
        ReglaId INT NOT NULL,
        NombreRegla NVARCHAR(200) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_NombreRegla DEFAULT (N''),
        PorcentajeAplicado DECIMAL(9,6) NULL,
        ComisionDeportiva DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_ComisionDeportiva DEFAULT ((0)),
        ComisionCalculada DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_ComisionCalculada DEFAULT ((0)),
        Estatus NVARCHAR(40) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Estatus DEFAULT (N'PENDIENTE'),
        FechaCalculo DATETIME2(0) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FechaCalculo DEFAULT (SYSDATETIME()),
        FechaPago DATETIME2(0) NULL,
        Usuario NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Usuario DEFAULT (N''),
        Observaciones NVARCHAR(1000) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Observaciones DEFAULT (N''),
        CONSTRAINT PK_ControlTaxiComisionesGeneradas PRIMARY KEY CLUSTERED (Id)
    );
END;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'BranchCode') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD BranchCode NVARCHAR(10) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_BranchCode DEFAULT (N'CV');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FolioOriginal') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FolioOriginal NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FolioOriginal DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FolioOperacion') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FolioOperacion INT NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FolioOperacion DEFAULT ((0));
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'TicketPos') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD TicketPos NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_TicketPos_Add DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'Taxista') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD Taxista NVARCHAR(200) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Taxista_Add DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'Gafete') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD Gafete NVARCHAR(50) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Gafete_Add DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'Transporte') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD Transporte NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Transporte_Add DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FormaPago') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FormaPago NVARCHAR(120) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FormaPago_Add DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'VentaCompuadmo') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD VentaCompuadmo DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaCompuadmo_Add DEFAULT ((0));
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'VentaJoyeria') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD VentaJoyeria DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaJoyeria_Add DEFAULT ((0));
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'VentaTotal') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD VentaTotal DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_VentaTotal_Add DEFAULT ((0));
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'ReglaId') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD ReglaId INT NULL;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'NombreRegla') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD NombreRegla NVARCHAR(200) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_NombreRegla_Add DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'PorcentajeAplicado') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD PorcentajeAplicado DECIMAL(9,6) NULL;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'ComisionDeportiva') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD ComisionDeportiva DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_ComisionDeportiva_Add DEFAULT ((0));
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'ComisionCalculada') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD ComisionCalculada DECIMAL(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_ComisionCalculada_Add DEFAULT ((0));
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'Estatus') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD Estatus NVARCHAR(40) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Estatus_Add DEFAULT (N'PENDIENTE');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FechaCalculo') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FechaCalculo DATETIME2(0) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_FechaCalculo_Add DEFAULT (SYSDATETIME());
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'FechaPago') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD FechaPago DATETIME2(0) NULL;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'Usuario') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD Usuario NVARCHAR(160) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Usuario_Add DEFAULT (N'');
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisionesGeneradas', N'Observaciones') IS NULL
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas ADD Observaciones NVARCHAR(1000) NOT NULL CONSTRAINT DF_ControlTaxiComisionesGeneradas_Observaciones_Add DEFAULT (N'');
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_ControlTaxiComisionesGeneradas_BranchCode_CV'
      AND parent_object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
)
BEGIN
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas
    ADD CONSTRAINT CK_ControlTaxiComisionesGeneradas_BranchCode_CV
        CHECK (BranchCode = N'CV');
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_ControlTaxiComisionesGeneradas_FolioOperacion_Positive'
      AND parent_object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
)
BEGIN
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas
    ADD CONSTRAINT CK_ControlTaxiComisionesGeneradas_FolioOperacion_Positive
        CHECK (FolioOperacion > 0);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_ControlTaxiComisionesGeneradas_Amounts_NonNegative'
      AND parent_object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
)
BEGIN
    ALTER TABLE dbo.ControlTaxiComisionesGeneradas
    ADD CONSTRAINT CK_ControlTaxiComisionesGeneradas_Amounts_NonNegative
        CHECK (VentaCompuadmo >= 0 AND VentaJoyeria >= 0 AND VentaTotal >= 0 AND ComisionCalculada >= 0);
END;
GO

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_ControlTaxiComisionesGeneradas_CV_Folio'
      AND object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
)
BEGIN
    DROP INDEX UX_ControlTaxiComisionesGeneradas_CV_Folio ON dbo.ControlTaxiComisionesGeneradas;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_ControlTaxiComisionesGeneradas_CV_Folio_Regla'
      AND object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
)
BEGIN
    CREATE UNIQUE INDEX UX_ControlTaxiComisionesGeneradas_CV_Folio_Regla
    ON dbo.ControlTaxiComisionesGeneradas (BranchCode, FolioOriginal, FolioOperacion, ReglaId);
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_ControlTaxiComisionesGeneradas_BranchCode_Estatus'
      AND object_id = OBJECT_ID(N'dbo.ControlTaxiComisionesGeneradas')
)
BEGIN
    CREATE INDEX IX_ControlTaxiComisionesGeneradas_BranchCode_Estatus
    ON dbo.ControlTaxiComisionesGeneradas (BranchCode, Estatus, FechaCalculo);
END;
GO
