-- 038：当前用户 Grid 列展示偏好。

IF OBJECT_ID(N'dbo.fn_settings_user_grid_preference', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_settings_user_grid_preference
    (
        Id uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        GridKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SchemaVersion int NOT NULL,
        ColumnsJson nvarchar(max) NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_settings_user_grid_preference_Version DEFAULT (1),
        CONSTRAINT PK_fn_settings_user_grid_preference PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_settings_user_grid_preference_SchemaVersion
            CHECK (SchemaVersion > 0),
        CONSTRAINT CK_fn_settings_user_grid_preference_ColumnsJson
            CHECK (ISJSON(ColumnsJson) = 1)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系统设置user grid preference表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'ColumnsJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'列配置(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'ColumnsJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'GridKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表格键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'GridKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'SchemaVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'SchemaVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_settings_user_grid_preference'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_user_grid_preference', @level2type=N'COLUMN', @level2name=N'Version';

END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id =
          OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
      AND indexObject.name =
          N'UX_fn_settings_user_grid_preference_UserGrid'
      AND
      (
          indexObject.is_unique = 0
          OR indexObject.has_filter = 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 2
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'UserId'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 2
                AND columnObject.name = N'GridKey'
          )
      )
)
BEGIN
    DROP INDEX UX_fn_settings_user_grid_preference_UserGrid
        ON dbo.fn_settings_user_grid_preference;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_settings_user_grid_preference')
      AND name = N'UX_fn_settings_user_grid_preference_UserGrid'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_settings_user_grid_preference_UserGrid
        ON dbo.fn_settings_user_grid_preference(UserId, GridKey);
END;
