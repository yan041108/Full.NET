-- 020：Host 作用域数据字典类型与字典项主数据。
CREATE TABLE IF NOT EXISTS fn_settings_dict_type (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '编码',
    Name varchar(128) NOT NULL COMMENT '名称',
    Description varchar(512) NULL COMMENT '描述',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_settings_dict_type PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_settings_dict_type_Code (Code)
) COMMENT='系统设置字典类型表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_settings_dict_item (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    DictTypeId BINARY(16) NOT NULL COMMENT 'Dict Type标识',
    Label varchar(128) NOT NULL COMMENT '显示标签',
    Value varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '值',
    Color varchar(32) NULL COMMENT '颜色',
    DisplayOrder int NOT NULL COMMENT '显示顺序',
    IsActive boolean NOT NULL DEFAULT true COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_settings_dict_item PRIMARY KEY (Id),
    CONSTRAINT FK_fn_settings_dict_item_Type
        FOREIGN KEY (DictTypeId) REFERENCES fn_settings_dict_type(Id),
    UNIQUE KEY UX_fn_settings_dict_item_Type_Value (DictTypeId, Value)
) COMMENT='系统设置字典项表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
