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
