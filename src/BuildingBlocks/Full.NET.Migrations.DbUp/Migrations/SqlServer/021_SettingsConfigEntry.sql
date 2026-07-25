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
    CREATE UNIQUE INDEX UX_fn_settings_config_entry_ConfigKey
        ON dbo.fn_settings_config_entry(ConfigKey);
END;
