-- 132：用户个人日程表。

IF OBJECT_ID(N'dbo.fn_calendar_personal_schedule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_calendar_personal_schedule
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        OwnerUserId uniqueidentifier NOT NULL,
        Content nvarchar(256) NOT NULL,
        StartAtUtc datetimeoffset(7) NOT NULL,
        EndAtUtc datetimeoffset(7) NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        Version int NOT NULL CONSTRAINT DF_fn_calendar_personal_schedule_Version DEFAULT (1),
        CONSTRAINT PK_fn_calendar_personal_schedule PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_calendar_personal_schedule_TimeRange CHECK (EndAtUtc >= StartAtUtc)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'日历个人日程表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'CompletedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完成时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'Content', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'Content';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'EndAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'End At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'EndAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'OwnerUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所有者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'OwnerUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'StartAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Start At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'StartAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_calendar_personal_schedule'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule', @level2type=N'COLUMN', @level2name=N'Version';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户个人日程表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_calendar_personal_schedule';

    CREATE INDEX IX_fn_calendar_personal_schedule_OwnerStartAtUtc
        ON dbo.fn_calendar_personal_schedule(OwnerUserId, StartAtUtc, Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_calendar_personal_schedule')
      AND indexObject.name = N'IX_fn_calendar_personal_schedule_OwnerStartAtUtc'
)
    CREATE INDEX IX_fn_calendar_personal_schedule_OwnerStartAtUtc
        ON dbo.fn_calendar_personal_schedule(OwnerUserId, StartAtUtc, Id);
