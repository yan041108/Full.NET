IF COL_LENGTH(N'dbo.fn_identity_user', N'AccountType') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user ADD AccountType varchar(32) NULL;
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'AccountType', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'账户类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'AccountType';
END;

EXEC(N'UPDATE dbo.fn_identity_user SET AccountType = ''normal_user'' WHERE AccountType IS NULL;');

EXEC(N'
UPDATE dbo.fn_identity_user
SET AccountType = ''sys_admin''
WHERE ScopeKey = ''host''
  AND TenantId IS NULL
  AND NormalizedUsername = ''ADMIN''
  AND AccountType = ''normal_user'';');

IF EXISTS (
    SELECT 1
    FROM sys.columns AS columnObject
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_user'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'AccountType'
      AND columnObject.is_nullable = 1)
BEGIN
    ALTER TABLE dbo.fn_identity_user ALTER COLUMN AccountType varchar(32) NOT NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints AS defaultObject
    INNER JOIN sys.columns AS columnObject
        ON defaultObject.parent_object_id = columnObject.object_id
       AND defaultObject.parent_column_id = columnObject.column_id
    INNER JOIN sys.tables AS tableObject ON columnObject.object_id = tableObject.object_id
    WHERE tableObject.name = N'fn_identity_user'
      AND SCHEMA_NAME(tableObject.schema_id) = N'dbo'
      AND columnObject.name = N'AccountType')
BEGIN
    ALTER TABLE dbo.fn_identity_user
        ADD CONSTRAINT DF_fn_identity_user_AccountType
        DEFAULT ('normal_user') FOR AccountType;
END;
