-- 021：Host 作用域系统配置项主数据。
CREATE TABLE IF NOT EXISTS fn_settings_config_entry
(
    Id BINARY(16) NOT NULL,
    ConfigKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    DisplayName varchar(128) NOT NULL,
    Description varchar(512) NULL,
    ValueKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Value varchar(4000) NOT NULL,
    DisplayOrder int NOT NULL,
    IsActive boolean NOT NULL DEFAULT true,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_settings_config_entry PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_settings_config_entry_ConfigKey (ConfigKey)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
