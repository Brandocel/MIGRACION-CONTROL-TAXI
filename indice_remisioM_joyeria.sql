SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF COL_LENGTH('dbo.remisioM', 'folio_registro_txt') IS NULL
BEGIN
    ALTER TABLE dbo.remisioM ADD folio_registro_txt AS CAST(folio_registro AS nvarchar(60)) PERSISTED;
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_remisioM_folio_registro_txt')
BEGIN
    CREATE NONCLUSTERED INDEX IX_remisioM_folio_registro_txt ON dbo.remisioM(folio_registro_txt);
END
