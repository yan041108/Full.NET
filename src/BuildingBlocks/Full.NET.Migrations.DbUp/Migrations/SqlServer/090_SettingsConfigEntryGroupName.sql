-- 090：对齐 Admin.NET 参数配置分组能力，为系统配置项表补充 GroupName 列。
-- 列允许为空，存量行迁移后保持 NULL，由代码层将空值规范化为 null，不破坏既有读写。
IF COL_LENGTH(N'dbo.fn_settings_config_entry', N'GroupName') IS NULL
BEGIN
    ALTER TABLE dbo.fn_settings_config_entry
        ADD GroupName nvarchar(64) NULL;
END;
