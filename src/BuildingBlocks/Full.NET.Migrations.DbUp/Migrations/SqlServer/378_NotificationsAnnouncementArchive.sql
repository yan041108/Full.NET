-- 归档只影响 Host 管理列表，保留公告业务状态和历史回执。
IF COL_LENGTH(N'dbo.fn_notifications_announcement', N'ArchivedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.fn_notifications_announcement ADD ArchivedAtUtc datetimeoffset(7) NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'ArchivedAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'公告移出日常管理列表的时间(UTC)；NULL 表示未归档', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'ArchivedAtUtc';
