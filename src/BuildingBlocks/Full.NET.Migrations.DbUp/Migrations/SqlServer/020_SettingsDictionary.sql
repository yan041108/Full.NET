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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系统设置字典类型表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'Code', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'Code';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'Description', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'Description';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'DisplayOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'Name', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'Name';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_type')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_type'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_type', @level2type=N'COLUMN', @level2name=N'Version';
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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系统设置字典项表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'Color', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'颜色', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'Color';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'DictTypeId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Dict Type标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'DictTypeId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'DisplayOrder', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'IsActive', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'IsActive';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'Label', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示标签', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'Label';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'Value', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'Value';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_dict_item')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_dict_item'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_dict_item', @level2type=N'COLUMN', @level2name=N'Version';
    CREATE UNIQUE INDEX UX_fn_settings_dict_item_Type_Value
        ON dbo.fn_settings_dict_item(DictTypeId, Value);
END;
