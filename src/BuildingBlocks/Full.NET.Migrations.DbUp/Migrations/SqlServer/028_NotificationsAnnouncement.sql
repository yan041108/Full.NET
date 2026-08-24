-- 028：Host 作用域公告主数据。

IF OBJECT_ID(N'dbo.fn_notifications_announcement', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_announcement
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        Title nvarchar(200) NOT NULL,
        Content nvarchar(4000) NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedByUserId uniqueidentifier NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_notifications_announcement_Version DEFAULT (1),
        CONSTRAINT PK_fn_notifications_announcement PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知公告表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'Content', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'Content';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'Title', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'Title';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'UpdatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_announcement'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_announcement', @level2type=N'COLUMN', @level2name=N'Version';

    CREATE INDEX IX_fn_notifications_announcement_CreatedAtUtc
        ON dbo.fn_notifications_announcement(CreatedAtUtc DESC, Id)
        WHERE TenantId IS NULL;
END;
