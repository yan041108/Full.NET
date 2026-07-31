-- 033：数据字典类型扩展租户作用域（Host 行 TenantId 为 NULL）。

IF COL_LENGTH(N'dbo.fn_settings_dict_type', N'TenantId') IS NULL
BEGIN
    ALTER TABLE dbo.fn_settings_dict_type
        ADD TenantId uniqueidentifier NULL;
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_fn_settings_dict_type_Code'
      AND object_id = OBJECT_ID(N'dbo.fn_settings_dict_type'))
BEGIN
    DROP INDEX UX_fn_settings_dict_type_Code ON dbo.fn_settings_dict_type;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_fn_settings_dict_type_Host_Code'
      AND object_id = OBJECT_ID(N'dbo.fn_settings_dict_type'))
BEGIN
    EXEC(N'
        CREATE UNIQUE INDEX UX_fn_settings_dict_type_Host_Code
            ON dbo.fn_settings_dict_type (Code)
            WHERE TenantId IS NULL');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'UX_fn_settings_dict_type_TenantScope_Code'
      AND object_id = OBJECT_ID(N'dbo.fn_settings_dict_type'))
BEGIN
    EXEC(N'
        CREATE UNIQUE INDEX UX_fn_settings_dict_type_TenantScope_Code
            ON dbo.fn_settings_dict_type (TenantId, Code)
            WHERE TenantId IS NOT NULL');
END;
