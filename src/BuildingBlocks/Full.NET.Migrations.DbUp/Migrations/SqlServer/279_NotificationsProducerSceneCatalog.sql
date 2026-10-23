-- 278：Notifications Producer/Scene 受控目录。
-- 由受控目录声明政策类别、合法渠道与紧急覆盖规则，避免仅靠可编辑模板把消息提升为强制通知。

IF OBJECT_ID(N'dbo.fn_notifications_producer_scene', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_producer_scene
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ScopeKey varchar(16) NOT NULL,
        TenantScopeKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProducerKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SceneKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PolicyCategoryKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AllowedChannelKeysJson nvarchar(max) NOT NULL,
        EmergencyOverrideAllowed bit NOT NULL CONSTRAINT DF_fn_notifications_producer_scene_Emergency DEFAULT (0),
        IsEnabled bit NOT NULL CONSTRAINT DF_fn_notifications_producer_scene_Enabled DEFAULT (1),
        Version bigint NOT NULL CONSTRAINT DF_fn_notifications_producer_scene_Version DEFAULT (1),
        CreatedById uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2(6) NOT NULL,
        UpdatedAtUtc datetime2(6) NOT NULL,
        CONSTRAINT PK_fn_notifications_producer_scene PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_producer_scene_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
        CONSTRAINT CK_fn_notifications_producer_scene_Policy CHECK (PolicyCategoryKey IN ('mandatory', 'transactional', 'informational', 'marketing')),
        CONSTRAINT CK_fn_notifications_producer_scene_Version CHECK (Version > 0),
        CONSTRAINT UX_fn_notifications_producer_scene_Scene UNIQUE (TenantScopeKey, ProducerKey, SceneKey)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知业务生产者与场景受控目录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'AllowedChannelKeysJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目录声明的合法渠道键(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'AllowedChannelKeysJson';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'CreatedById', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'CreatedById';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'EmergencyOverrideAllowed', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否允许在静默时段紧急覆盖', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'EmergencyOverrideAllowed';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'PolicyCategoryKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目录声明的政策类别键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'PolicyCategoryKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'ProducerKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务生产者键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'ProducerKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'SceneKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'业务场景键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'SceneKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'Version';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知业务生产者与场景受控目录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'PolicyCategoryKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目录声明的政策类别键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'PolicyCategoryKey';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'AllowedChannelKeysJson', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目录声明的合法渠道键(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'AllowedChannelKeysJson';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'EmergencyOverrideAllowed', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否允许在静默时段紧急覆盖', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'EmergencyOverrideAllowed';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'IsEnabled', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'目录项是否启用；停用后新意图失败关闭', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'IsEnabled';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_producer_scene')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_producer_scene'), N'Version', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_producer_scene', @level2type=N'COLUMN', @level2name=N'Version';
END;
