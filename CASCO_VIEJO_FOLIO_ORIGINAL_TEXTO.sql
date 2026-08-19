IF OBJECT_ID('dbo.dejadas', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID('dbo.dejadas')
          AND c.name = 'idstaff'
          AND t.name NOT IN ('nvarchar', 'varchar', 'nchar', 'char')
    )
    BEGIN
        ALTER TABLE dbo.dejadas ALTER COLUMN idstaff NVARCHAR(50) NULL;
    END;
END;
GO

IF OBJECT_ID('dbo.gafete', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON c.user_type_id = t.user_type_id
        WHERE c.object_id = OBJECT_ID('dbo.gafete')
          AND c.name = 'matricula'
          AND t.name NOT IN ('nvarchar', 'varchar', 'nchar', 'char')
    )
    BEGIN
        ALTER TABLE dbo.gafete ALTER COLUMN matricula NVARCHAR(50) NULL;
    END;
END;
GO

SELECT
    'dbo.dejadas.idstaff' AS Campo,
    TYPE_NAME(c.user_type_id) AS Tipo,
    c.max_length AS Longitud
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.dejadas')
  AND c.name = 'idstaff';
GO

SELECT
    'dbo.dejadas.codigorecepcion' AS Campo,
    TYPE_NAME(c.user_type_id) AS Tipo,
    c.max_length AS Longitud
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.dejadas')
  AND c.name = 'codigorecepcion';
GO

SELECT
    'dbo.gafete.matricula' AS Campo,
    TYPE_NAME(c.user_type_id) AS Tipo,
    c.max_length AS Longitud
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.gafete')
  AND c.name = 'matricula';
GO
