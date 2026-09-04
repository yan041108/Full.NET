-- 106：扩展 Host 公告类型、受众与发布/撤回生命周期，并引入规范化受众子表。
IF COL_LENGTH(N'dbo.fn_notifications_announcement', N'Kind') IS NULL
    ALTER TABLE dbo.fn_notifications_announcement
        ADD Kind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_announcement_Kind DEFAULT ('announcement');

IF COL_LENGTH(N'dbo.fn_notifications_announcement', N'AudienceKind') IS NULL
    ALTER TABLE dbo.fn_notifications_announcement
        ADD AudienceKind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT DF_fn_notifications_announcement_AudienceKind DEFAULT ('all');

IF COL_LENGTH(N'dbo.fn_notifications_announcement', N'PublishedByUserId') IS NULL
    ALTER TABLE dbo.fn_notifications_announcement
        ADD PublishedByUserId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.fn_notifications_announcement', N'RetractedAtUtc') IS NULL
    ALTER TABLE dbo.fn_notifications_announcement
        ADD RetractedAtUtc datetimeoffset(7) NULL;

IF COL_LENGTH(N'dbo.fn_notifications_announcement', N'RetractedByUserId') IS NULL
    ALTER TABLE dbo.fn_notifications_announcement
        ADD RetractedByUserId uniqueidentifier NULL;

-- 新增列与引用该列的 CHECK 必须分批编译，否则恢复路径会在 ALTER 执行前解析失败。
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
      AND name = N'CK_fn_notifications_announcement_Kind')
    ALTER TABLE dbo.fn_notifications_announcement
        ADD CONSTRAINT CK_fn_notifications_announcement_Kind
        CHECK (Kind IN ('notice', 'announcement'));

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
      AND name = N'CK_fn_notifications_announcement_AudienceKind')
    ALTER TABLE dbo.fn_notifications_announcement
        ADD CONSTRAINT CK_fn_notifications_announcement_AudienceKind
        CHECK (AudienceKind IN ('all', 'users', 'organizations'));

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_notifications_announcement')
      AND name = N'CK_fn_notifications_announcement_Status')
    ALTER TABLE dbo.fn_notifications_announcement
        ADD CONSTRAINT CK_fn_notifications_announcement_Status
        CHECK (Status IN ('draft', 'published', 'retracted'));

IF OBJECT_ID(N'dbo.fn_notifications_announcement_target_user', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_announcement_target_user
    (
        Id uniqueidentifier NOT NULL,
        AnnouncementId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_notifications_announcement_target_user PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_announcement_target_user_Announcement
            FOREIGN KEY (AnnouncementId) REFERENCES dbo.fn_notifications_announcement(Id)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_notifications_announcement_target_user
        ON dbo.fn_notifications_announcement_target_user(AnnouncementId, UserId);
END;

IF OBJECT_ID(N'dbo.fn_notifications_announcement_target_organization', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_announcement_target_organization
    (
        Id uniqueidentifier NOT NULL,
        AnnouncementId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NOT NULL,
        OrganizationUnitId uniqueidentifier NOT NULL,
        CONSTRAINT PK_fn_notifications_announcement_target_organization PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT FK_fn_notifications_announcement_target_organization_Announcement
            FOREIGN KEY (AnnouncementId) REFERENCES dbo.fn_notifications_announcement(Id)
    );

    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_notifications_announcement_target_organization
        ON dbo.fn_notifications_announcement_target_organization(AnnouncementId, TenantId, OrganizationUnitId);
END;
