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
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'微信小程序订阅消息授权表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_wechat_miniprogram_subscription';
END;
