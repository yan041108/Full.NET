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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台发布说明表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'Content', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'内容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'Content';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'CreatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'PublishedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'PublishedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'PublishedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'RetractedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Retracted At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'RetractedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'RetractedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Retracted By User标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'RetractedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'Status', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'Status';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'Title', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'标题', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'Title';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'UpdatedByUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'Version';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'VersionLabel', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本标签', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'VersionLabel';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note'), N'VersionSortKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本排序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note', @level2type=N'COLUMN', @level2name=N'VersionSortKey';

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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'平台发布说明已读表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note_read';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note_read')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note_read'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note_read', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note_read')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note_read'), N'ReadAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已读时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note_read', @level2type=N'COLUMN', @level2name=N'ReadAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note_read')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note_read'), N'ReleaseNoteId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发布说明标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note_read', @level2type=N'COLUMN', @level2name=N'ReleaseNoteId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_platform_release_note_read')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_platform_release_note_read'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_platform_release_note_read', @level2type=N'COLUMN', @level2name=N'UserId';

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
