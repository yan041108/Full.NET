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

    CREATE INDEX IX_fn_notifications_announcement_CreatedAtUtc
        ON dbo.fn_notifications_announcement(CreatedAtUtc DESC, Id)
        WHERE TenantId IS NULL;
END;
