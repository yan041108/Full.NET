-- 140：为 OpenAccess 接入方应用增加每日请求配额上限（NULL 表示不限）。

IF COL_LENGTH(N'dbo.fn_identity_open_access_client', N'DailyRequestQuota') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_open_access_client
        ADD DailyRequestQuota int NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_open_access_client')
          AND minor_id = COLUMNPROPERTY(
              OBJECT_ID(N'dbo.fn_identity_open_access_client'),
              N'DailyRequestQuota',
              'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty
            @name = N'MS_Description',
            @value = N'每日成功认证请求配额上限；NULL 表示不限',
            @level0type = N'SCHEMA',
            @level0name = N'dbo',
            @level1type = N'TABLE',
            @level1name = N'fn_identity_open_access_client',
            @level2type = N'COLUMN',
            @level2name = N'DailyRequestQuota';
END;
