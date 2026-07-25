-- 020：Host 作用域数据字典类型与字典项主数据。
IF OBJECT_ID(N'dbo.fn_settings_dict_type', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_settings_dict_type
    (
        Id uniqueidentifier NOT NULL,
        Code varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Name nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        DisplayOrder int NOT NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_fn_settings_dict_type_IsActive DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_settings_dict_type_Version DEFAULT (1),
        CONSTRAINT PK_fn_settings_dict_type PRIMARY KEY CLUSTERED (Id)
    );
    CREATE UNIQUE INDEX UX_fn_settings_dict_type_Code
        ON dbo.fn_settings_dict_type(Code);
END;

IF OBJECT_ID(N'dbo.fn_settings_dict_item', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_settings_dict_item
    (
        Id uniqueidentifier NOT NULL,
        DictTypeId uniqueidentifier NOT NULL,
        Label nvarchar(128) NOT NULL,
        Value varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Color varchar(32) NULL,
        DisplayOrder int NOT NULL,
        IsActive bit NOT NULL
            CONSTRAINT DF_fn_settings_dict_item_IsActive DEFAULT (1),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_settings_dict_item_Version DEFAULT (1),
        CONSTRAINT PK_fn_settings_dict_item PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_settings_dict_item_Type
            FOREIGN KEY (DictTypeId) REFERENCES dbo.fn_settings_dict_type(Id)
    );
    CREATE UNIQUE INDEX UX_fn_settings_dict_item_Type_Value
        ON dbo.fn_settings_dict_item(DictTypeId, Value);
END;
