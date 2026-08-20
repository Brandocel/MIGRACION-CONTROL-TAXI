SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF COL_LENGTH('dbo.remisioM', 'folioregistro_txt') IS NULL
BEGIN
    ALTER TABLE dbo.remisioM ADD folioregistro_txt AS CAST(folioregistro AS nvarchar(60)) PERSISTED;
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_remisioM_folioregistro_txt')
BEGIN
    CREATE NONCLUSTERED INDEX IX_remisioM_folioregistro_txt ON dbo.remisioM(folioregistro_txt);
END
