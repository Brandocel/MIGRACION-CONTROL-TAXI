IF OBJECT_ID(N'dbo.ControlTaxiComisiones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ControlTaxiComisiones
    (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ControlTaxiComisiones PRIMARY KEY,
        BranchCode nvarchar(10) NOT NULL,
        Proveedor nvarchar(120) NOT NULL,
        TipoServicio nvarchar(120) NOT NULL,
        ConTarjeta bit NOT NULL,
        VentaMinima decimal(18,2) NOT NULL CONSTRAINT DF_ControlTaxiComisiones_VentaMinima DEFAULT (0),
        VentaMaxima decimal(18,2) NULL,
        ComisionAgencia decimal(9,4) NULL,
        ComisionTaxista decimal(9,4) NULL,
        ComisionVendedor decimal(9,4) NULL,
        ComisionDeportiva decimal(18,4) NULL,
        AplicaDegustacion bit NOT NULL CONSTRAINT DF_ControlTaxiComisiones_AplicaDegustacion DEFAULT (0),
        AplicaGasto bit NOT NULL CONSTRAINT DF_ControlTaxiComisiones_AplicaGasto DEFAULT (0),
        Activo bit NOT NULL CONSTRAINT DF_ControlTaxiComisiones_Activo DEFAULT (1),
        ReglaNombre nvarchar(180) NOT NULL CONSTRAINT DF_ControlTaxiComisiones_ReglaNombre DEFAULT (N''),
        OrigenExcel nvarchar(120) NOT NULL CONSTRAINT DF_ControlTaxiComisiones_OrigenExcel DEFAULT (N''),
        RequiereValidacion bit NOT NULL CONSTRAINT DF_ControlTaxiComisiones_RequiereValidacion DEFAULT (0),
        Observaciones nvarchar(800) NULL,
        FechaActualizacion datetime2(0) NOT NULL CONSTRAINT DF_ControlTaxiComisiones_FechaActualizacion DEFAULT (sysdatetime())
    );
END;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'ReglaNombre') IS NULL
BEGIN
    ALTER TABLE dbo.ControlTaxiComisiones
        ADD ReglaNombre nvarchar(180) NOT NULL
            CONSTRAINT DF_ControlTaxiComisiones_ReglaNombre DEFAULT (N'');
END;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'OrigenExcel') IS NULL
BEGIN
    ALTER TABLE dbo.ControlTaxiComisiones
        ADD OrigenExcel nvarchar(120) NOT NULL
            CONSTRAINT DF_ControlTaxiComisiones_OrigenExcel DEFAULT (N'');
END;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'RequiereValidacion') IS NULL
BEGIN
    ALTER TABLE dbo.ControlTaxiComisiones
        ADD RequiereValidacion bit NOT NULL
            CONSTRAINT DF_ControlTaxiComisiones_RequiereValidacion DEFAULT (0);
END;
GO

IF COL_LENGTH(N'dbo.ControlTaxiComisiones', N'Observaciones') IS NULL
BEGIN
    ALTER TABLE dbo.ControlTaxiComisiones
        ADD Observaciones nvarchar(800) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ControlTaxiComisiones')
      AND name = N'UX_ControlTaxiComisiones_Regla'
)
BEGIN
    CREATE UNIQUE INDEX UX_ControlTaxiComisiones_Regla
        ON dbo.ControlTaxiComisiones (BranchCode, Proveedor, TipoServicio, ConTarjeta, VentaMinima, ReglaNombre);
END;
GO
