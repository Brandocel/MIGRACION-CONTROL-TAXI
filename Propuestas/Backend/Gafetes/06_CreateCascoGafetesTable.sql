-- Migración SQL para tabla de gafetes de Casco
-- Nombre: 2026_07_20_001_CreateCascoGafetesTable.sql
-- Propósito: Crear tabla casco_gafetes para sincronización de gafetes
-- Autenticación: NO DESPLEGAR TODAVÍA - SOLO PROPUESTA

-- ============================================================
-- 1. Crear tabla principal casco_gafetes
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'casco_gafetes' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.casco_gafetes
    (
        Id INT PRIMARY KEY IDENTITY(1,1),
        
        -- Claves de negocio
        BranchCode NVARCHAR(10) NOT NULL,
        BadgeId NVARCHAR(50) NOT NULL,
        Barcode NVARCHAR(50) NOT NULL,
        
        -- Estado del gafete
        Status NCHAR(1) NOT NULL CHECK (Status IN ('A', 'R', 'S')), -- A=Activo, R=Retirado, S=Suspendido
        Cycle INT NOT NULL CHECK (Cycle >= 1),
        
        -- Información del taxista (opcional)
        TaxistaId INT NULL,
        TaxistaName NVARCHAR(255) NULL,
        
        -- Auditoría temporal
        CreatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        UpdatedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        IsActive BIT NOT NULL DEFAULT 1,
        
        -- Campos adicionales para auditoría
        SyncedAt DATETIME2 NULL,
        SyncSource NVARCHAR(50) NULL, -- ej: 'CascoSync', 'WebUI'
        
        CONSTRAINT UQ_CascoGafetes_BranchBadgeCycle UNIQUE (BranchCode, BadgeId, Cycle)
    );
    
    PRINT 'Tabla casco_gafetes creada exitosamente';
END
ELSE
BEGIN
    PRINT 'Tabla casco_gafetes ya existe';
END

-- ============================================================
-- 2. Crear índices para rendimiento
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CascoGafetes_BranchCode' AND object_id = OBJECT_ID('dbo.casco_gafetes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CascoGafetes_BranchCode
    ON dbo.casco_gafetes (BranchCode, BadgeId, Cycle)
    INCLUDE (Status, UpdatedAt, IsActive);
    
    PRINT 'Índice IX_CascoGafetes_BranchCode creado';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CascoGafetes_Status' AND object_id = OBJECT_ID('dbo.casco_gafetes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CascoGafetes_Status
    ON dbo.casco_gafetes (Status, BranchCode, IsActive);
    
    PRINT 'Índice IX_CascoGafetes_Status creado';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CascoGafetes_UpdatedAt' AND object_id = OBJECT_ID('dbo.casco_gafetes'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_CascoGafetes_UpdatedAt
    ON dbo.casco_gafetes (UpdatedAt DESC)
    WHERE IsActive = 1;
    
    PRINT 'Índice IX_CascoGafetes_UpdatedAt creado';
END

-- ============================================================
-- 3. Crear tabla de auditoría
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'casco_gafetes_audit' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.casco_gafetes_audit
    (
        AuditId BIGINT PRIMARY KEY IDENTITY(1,1),
        
        -- Referencia al gafete
        GafeteId INT NOT NULL,
        
        -- Operación realizada
        Operation NVARCHAR(10) NOT NULL CHECK (Operation IN ('INSERT', 'UPDATE', 'DELETE')),
        
        -- Valores anteriores
        OldStatus NCHAR(1) NULL,
        OldTaxistaId INT NULL,
        OldTaxistaName NVARCHAR(255) NULL,
        
        -- Valores nuevos
        NewStatus NCHAR(1) NULL,
        NewTaxistaId INT NULL,
        NewTaxistaName NVARCHAR(255) NULL,
        
        -- Metadatos
        ChangedAt DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        ChangedBy NVARCHAR(255) NULL,
        ChangeReason NVARCHAR(500) NULL,
        
        INDEX IX_CascoGafetesAudit_GafeteId (GafeteId),
        INDEX IX_CascoGafetesAudit_ChangedAt (ChangedAt DESC)
    );
    
    PRINT 'Tabla casco_gafetes_audit creada exitosamente';
END
ELSE
BEGIN
    PRINT 'Tabla casco_gafetes_audit ya existe';
END

-- ============================================================
-- 4. Crear trigger para actualizar UpdatedAt
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'TR_CascoGafetes_UpdatedAt' AND parent_id = OBJECT_ID('dbo.casco_gafetes'))
BEGIN
    EXEC ('
        CREATE TRIGGER dbo.TR_CascoGafetes_UpdatedAt
        ON dbo.casco_gafetes
        AFTER UPDATE
        AS
        BEGIN
            UPDATE dbo.casco_gafetes
            SET UpdatedAt = GETUTCDATE()
            WHERE Id IN (SELECT DISTINCT Id FROM inserted);
        END
    ');
    
    PRINT 'Trigger TR_CascoGafetes_UpdatedAt creado';
END

-- ============================================================
-- 5. Crear trigger para auditoría
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'TR_CascoGafetes_Audit' AND parent_id = OBJECT_ID('dbo.casco_gafetes'))
BEGIN
    EXEC ('
        CREATE TRIGGER dbo.TR_CascoGafetes_Audit
        ON dbo.casco_gafetes
        AFTER INSERT, UPDATE, DELETE
        AS
        BEGIN
            -- Registrar INSERTs
            INSERT INTO dbo.casco_gafetes_audit (GafeteId, Operation, NewStatus, NewTaxistaId, NewTaxistaName)
            SELECT Id, ''INSERT'', Status, TaxistaId, TaxistaName
            FROM inserted
            WHERE NOT EXISTS (SELECT 1 FROM deleted WHERE deleted.Id = inserted.Id);
            
            -- Registrar UPDATEs
            INSERT INTO dbo.casco_gafetes_audit (GafeteId, Operation, OldStatus, OldTaxistaId, OldTaxistaName, NewStatus, NewTaxistaId, NewTaxistaName)
            SELECT i.Id, ''UPDATE'', d.Status, d.TaxistaId, d.TaxistaName, i.Status, i.TaxistaId, i.TaxistaName
            FROM inserted i
            INNER JOIN deleted d ON i.Id = d.Id
            WHERE i.Status <> d.Status OR i.TaxistaId <> d.TaxistaId OR ISNULL(i.TaxistaName, '''') <> ISNULL(d.TaxistaName, '''');
            
            -- Registrar DELETEs (marcados como inactivos)
            INSERT INTO dbo.casco_gafetes_audit (GafeteId, Operation, OldStatus, OldTaxistaId, OldTaxistaName)
            SELECT Id, ''DELETE'', Status, TaxistaId, TaxistaName
            FROM deleted
            WHERE NOT EXISTS (SELECT 1 FROM inserted WHERE inserted.Id = deleted.Id);
        END
    ');
    
    PRINT 'Trigger TR_CascoGafetes_Audit creado';
END

-- ============================================================
-- 6. Crear procedimiento almacenado para UPSERT
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.procedures WHERE name = 'sp_UpsertGafete')
BEGIN
    EXEC ('
        CREATE PROCEDURE dbo.sp_UpsertGafete
            @BranchCode NVARCHAR(10),
            @BadgeId NVARCHAR(50),
            @Barcode NVARCHAR(50),
            @Status NCHAR(1),
            @Cycle INT,
            @TaxistaId INT = NULL,
            @TaxistaName NVARCHAR(255) = NULL
        AS
        BEGIN
            SET NOCOUNT ON;
            
            DECLARE @GafeteId INT;
            DECLARE @ExistingStatus NCHAR(1);
            
            -- Buscar gafete existente
            SELECT TOP 1 @GafeteId = Id, @ExistingStatus = Status
            FROM dbo.casco_gafetes
            WHERE BranchCode = @BranchCode
              AND BadgeId = @BadgeId
              AND Cycle = @Cycle
              AND IsActive = 1;
            
            IF @GafeteId IS NULL
            BEGIN
                -- INSERT
                INSERT INTO dbo.casco_gafetes
                    (BranchCode, BadgeId, Barcode, Status, Cycle, TaxistaId, TaxistaName, SyncedAt, SyncSource)
                VALUES
                    (@BranchCode, @BadgeId, @Barcode, @Status, @Cycle, @TaxistaId, @TaxistaName, GETUTCDATE(), ''API'');
                    
                SELECT 1 AS Result; -- 1 = Inserted
            END
            ELSE IF @ExistingStatus <> @Status
            BEGIN
                -- UPDATE
                UPDATE dbo.casco_gafetes
                SET
                    Barcode = @Barcode,
                    Status = @Status,
                    TaxistaId = @TaxistaId,
                    TaxistaName = @TaxistaName,
                    SyncedAt = GETUTCDATE(),
                    SyncSource = ''API''
                WHERE Id = @GafeteId;
                
                SELECT 2 AS Result; -- 2 = Updated
            END
            ELSE
            BEGIN
                -- No cambió nada
                SELECT 0 AS Result; -- 0 = Unchanged
            END
        END
    ');
    
    PRINT 'Procedimiento sp_UpsertGafete creado';
END

-- ============================================================
-- 7. Crear vista para consultas comunes
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM sys.views WHERE name = 'vw_CascoGafetesActivos')
BEGIN
    EXEC ('
        CREATE VIEW dbo.vw_CascoGafetesActivos AS
        SELECT
            Id,
            BranchCode,
            BadgeId,
            Barcode,
            Status,
            Cycle,
            TaxistaId,
            TaxistaName,
            CreatedAt,
            UpdatedAt,
            SyncedAt,
            SyncSource
        FROM dbo.casco_gafetes
        WHERE IsActive = 1
          AND Status IN (''A'', ''R'') -- Excluye suspendidos
    ');
    
    PRINT 'Vista vw_CascoGafetesActivos creada';
END

-- ============================================================
-- 8. Crear estadísticas iniciales
-- ============================================================

EXEC sp_updatestats;
PRINT 'Estadísticas actualizadas';

-- ============================================================
-- 9. Verificación final
-- ============================================================

SELECT
    t.name AS TableName,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = t.object_id) AS ColumnCount,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = t.object_id) AS IndexCount
FROM sys.tables t
WHERE t.name IN ('casco_gafetes', 'casco_gafetes_audit');

PRINT '
====================================================
Migración completada exitosamente.

Tabla: casco_gafetes
- Almacena gafetes de Casco
- Clave única: (BranchCode, BadgeId, Cycle)
- Triggers: UpdatedAt automático + Auditoría

Tabla: casco_gafetes_audit
- Registro de cambios para auditoría
- Campos: Operation, OldStatus, NewStatus, ChangedAt

Procedimiento: sp_UpsertGafete
- Implementa lógica de UPSERT
- Retorna 0 (Unchanged), 1 (Inserted), 2 (Updated)

Índices:
- IX_CascoGafetes_BranchCode: (BranchCode, BadgeId, Cycle)
- IX_CascoGafetes_Status: (Status, BranchCode)
- IX_CascoGafetes_UpdatedAt: (UpdatedAt DESC)

Vista: vw_CascoGafetesActivos
- Consulta gafetes activos sin suspendidos

IMPORTANTE:
✓ Plaza 28 está protegida por lógica de aplicación
✓ No modifica tablas de viajes, tarifas ni pagos
✓ Solo sincroniza gafetes de CV

====================================================
';
