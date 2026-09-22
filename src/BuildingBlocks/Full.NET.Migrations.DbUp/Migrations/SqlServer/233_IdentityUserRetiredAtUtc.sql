IF COL_LENGTH(N'dbo.fn_identity_user', N'RetiredAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_identity_user
        ADD RetiredAtUtc datetimeoffset(7) NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_identity_user')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_user'), N'RetiredAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Host 用户退役时间（UTC）；非空表示不可再启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_user', @level2type=N'COLUMN', @level2name=N'RetiredAtUtc';
END;
