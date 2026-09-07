-- 163：微信小程序 OpenId 绑定与订阅授权表。

IF OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_wechat_miniprogram_binding
    (
        Id uniqueidentifier NOT NULL,
        TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        UserId uniqueidentifier NOT NULL,
        AppId varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ProviderProfileVersionId uniqueidentifier NOT NULL,
        OpenIdProtected nvarchar(max) NOT NULL,
        OpenIdMask varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        OpenIdSha256Hex char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        UnionIdProtected nvarchar(max) NULL,
        UnionIdMask varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        VerificationStatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RecipientEndpointId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_notifications_wechat_miniprogram_binding PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_notifications_wechat_miniprogram_binding_StatusKey
            CHECK (VerificationStatusKey IN (N'verified', N'revoked'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知微信小程序绑定表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'AppId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'AppId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'OpenIdMask', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OpenId 掩码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'OpenIdMask';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'OpenIdProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的 OpenId', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'OpenIdProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'OpenIdSha256Hex', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OpenId SHA-256 十六进制摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'OpenIdSha256Hex';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'ProviderProfileVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'渠道配置版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'ProviderProfileVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'RecipientEndpointId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Recipient Endpoint标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'RecipientEndpointId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'TenantScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户作用域唯一键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'UnionIdMask', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'UnionId 掩码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'UnionIdMask';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'UnionIdProtected', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受保护的 UnionId', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'UnionIdProtected';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'UpdatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'UserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'UserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding'), N'VerificationStatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'端点验证状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding', @level2type=N'COLUMN', @level2name=N'VerificationStatusKey';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'微信小程序 OpenId 绑定表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_binding';
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
      AND indexObject.name = N'UX_fn_notifications_wechat_miniprogram_binding_Scope_User_App'
)
    CREATE UNIQUE INDEX UX_fn_notifications_wechat_miniprogram_binding_Scope_User_App
        ON dbo.fn_notifications_wechat_miniprogram_binding(TenantScopeKey, UserId, AppId);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_binding')
      AND indexObject.name = N'UX_fn_notifications_wechat_miniprogram_binding_Scope_App_OpenId'
)
    CREATE UNIQUE INDEX UX_fn_notifications_wechat_miniprogram_binding_Scope_App_OpenId
        ON dbo.fn_notifications_wechat_miniprogram_binding(TenantScopeKey, AppId, OpenIdSha256Hex);

IF OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_notifications_wechat_miniprogram_subscription
    (
        BindingId uniqueidentifier NOT NULL,
        TemplateId varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StatusKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        AuthorizedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_notifications_wechat_miniprogram_subscription PRIMARY KEY CLUSTERED (BindingId, TemplateId),
        CONSTRAINT CK_fn_notifications_wechat_miniprogram_subscription_StatusKey
            CHECK (StatusKey IN (N'accepted', N'rejected'))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'通知微信小程序订阅表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_subscription';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription'), N'AuthorizedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Authorized At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_subscription', @level2type=N'COLUMN', @level2name=N'AuthorizedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription'), N'BindingId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'场景绑定标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_subscription', @level2type=N'COLUMN', @level2name=N'BindingId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_subscription', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription'), N'TemplateId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模板标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_subscription', @level2type=N'COLUMN', @level2name=N'TemplateId';

    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_notifications_wechat_miniprogram_subscription')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'微信小程序订阅消息授权表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_subscription';
END;
