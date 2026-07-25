-- 020：Host 作用域数据字典类型与字典项主数据。
CREATE TABLE IF NOT EXISTS fn_settings_dict_type
(
    Id BINARY(16) NOT NULL,
    Code varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Name varchar(128) NOT NULL,
    Description varchar(512) NULL,
    DisplayOrder int NOT NULL,
    IsActive boolean NOT NULL DEFAULT true,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_settings_dict_type PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_settings_dict_type_Code (Code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_settings_dict_item
(
    Id BINARY(16) NOT NULL,
    DictTypeId BINARY(16) NOT NULL,
    Label varchar(128) NOT NULL,
    Value varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    Color varchar(32) NULL,
    DisplayOrder int NOT NULL,
    IsActive boolean NOT NULL DEFAULT true,
    CreatedAtUtc datetime(6) NOT NULL,
    UpdatedAtUtc datetime(6) NULL,
    Version int NOT NULL DEFAULT 1,
    CONSTRAINT PK_fn_settings_dict_item PRIMARY KEY (Id),
    CONSTRAINT FK_fn_settings_dict_item_Type
        FOREIGN KEY (DictTypeId) REFERENCES fn_settings_dict_type(Id),
    UNIQUE KEY UX_fn_settings_dict_item_Type_Value (DictTypeId, Value)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
