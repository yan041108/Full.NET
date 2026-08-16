-- 021：Host 作用域系统配置项主数据。
CREATE TABLE IF NOT EXISTS fn_settings_config_entry (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ConfigKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '配置键',
    DisplayName varchar(128) NOT NULL COMMENT '显示名称',
    Description varchar(512) NULL COMMENT '描述',
    ValueKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '值类型',
    Value varchar(4000) NOT NULL COMMENT '值',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_settings_config_entry PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_settings_config_entry_ConfigKey (ConfigKey)
) COMMENT='系统设置配置项表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
