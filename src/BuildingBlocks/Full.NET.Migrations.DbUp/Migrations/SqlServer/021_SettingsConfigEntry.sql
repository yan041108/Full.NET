-- 021：Host 作用域系统配置项主数据。
IF OBJECT_ID(N'dbo.fn_settings_config_entry', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_settings_config_entry
    (
        Id uniqueidentifier NOT NULL,
        ConfigKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        ValueKind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Value nvarchar(4000) NOT NULL,
        DisplayOrder int NOT NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_fn_settings_config_entry_IsActive DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_settings_config_entry_Version DEFAULT (1),
        CONSTRAINT PK_fn_settings_config_entry PRIMARY KEY CLUSTERED (Id)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系统设置配置项表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配置键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'ConfigKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'Description';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'DisplayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'IsActive';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'Value';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'值类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'ValueKind';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_config_entry', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_settings_config_entry_ConfigKey
        ON dbo.fn_settings_config_entry(ConfigKey);
END;
