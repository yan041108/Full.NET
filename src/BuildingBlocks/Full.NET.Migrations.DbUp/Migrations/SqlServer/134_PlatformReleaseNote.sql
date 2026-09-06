-- 134：平台更新日志主表与用户已读表。

IF OBJECT_ID(N'dbo.fn_platform_release_note', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_platform_release_note
    (
        Id uniqueidentifier NOT NULL,
        VersionLabel nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        VersionSortKey bigint NOT NULL,
        Title nvarchar(200) NOT NULL,
        Content nvarchar(max) NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublishedAtUtc datetimeoffset(7) NULL,
        PublishedByUserId uniqueidentifier NULL,
        RetractedAtUtc datetimeoffset(7) NULL,
        RetractedByUserId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedByUserId uniqueidentifier NULL,
        Version int NOT NULL CONSTRAINT DF_fn_platform_release_note_Version DEFAULT (1),
        CONSTRAINT PK_fn_platform_release_note PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_platform_release_note_VersionLabel UNIQUE (VersionLabel)
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台更新日志表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note';

    CREATE INDEX IX_fn_platform_release_note_VersionSortKey
        ON dbo.fn_platform_release_note(VersionSortKey DESC, Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_platform_release_note')
      AND indexObject.name = N'IX_fn_platform_release_note_VersionSortKey'
)
    CREATE INDEX IX_fn_platform_release_note_VersionSortKey
        ON dbo.fn_platform_release_note(VersionSortKey DESC, Id);

IF OBJECT_ID(N'dbo.fn_platform_release_note_read', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_platform_release_note_read
    (
        Id uniqueidentifier NOT NULL,
        ReleaseNoteId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        ReadAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_platform_release_note_read PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UX_fn_platform_release_note_read_User_ReleaseNote UNIQUE (UserId, ReleaseNoteId),
        CONSTRAINT FK_fn_platform_release_note_read_ReleaseNote
            FOREIGN KEY (ReleaseNoteId) REFERENCES dbo.fn_platform_release_note (Id)
    );

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note_read')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台更新日志用户已读表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note_read';
END;
