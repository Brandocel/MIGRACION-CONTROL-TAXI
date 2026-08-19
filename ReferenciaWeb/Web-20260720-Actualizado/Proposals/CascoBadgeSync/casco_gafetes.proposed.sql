IF OBJECT_ID(N'dbo.casco_gafetes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.casco_gafetes
    (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        branch_code NVARCHAR(10) NOT NULL,
        badge_id NVARCHAR(50) NOT NULL,
        barcode NVARCHAR(50) NOT NULL,
        status NCHAR(1) NOT NULL,
        cycle INT NOT NULL,
        taxista_id INT NULL,
        taxista_name NVARCHAR(200) NOT NULL CONSTRAINT DF_casco_gafetes_taxista_name DEFAULT N'',
        created_at DATETIME2(0) NOT NULL,
        updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_casco_gafetes_updated_at DEFAULT SYSUTCDATETIME()
    );
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_casco_gafetes_branch_badge_cycle'
      AND object_id = OBJECT_ID(N'dbo.casco_gafetes')
)
BEGIN
    CREATE UNIQUE INDEX UX_casco_gafetes_branch_badge_cycle
        ON dbo.casco_gafetes(branch_code, badge_id, cycle);
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_casco_gafetes_status'
)
BEGIN
    ALTER TABLE dbo.casco_gafetes
        ADD CONSTRAINT CK_casco_gafetes_status
        CHECK (status IN (N'A', N'R', N'S'));
END
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_casco_gafetes_cycle'
)
BEGIN
    ALTER TABLE dbo.casco_gafetes
        ADD CONSTRAINT CK_casco_gafetes_cycle
        CHECK (cycle >= 1);
END
GO
