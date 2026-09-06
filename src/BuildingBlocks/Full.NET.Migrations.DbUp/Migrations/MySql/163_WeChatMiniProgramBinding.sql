-- 163：微信小程序 OpenId 绑定与订阅授权表。

CREATE TABLE IF NOT EXISTS fn_notifications_wechat_miniprogram_binding (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '租户作用域键',
    UserId BINARY(16) NOT NULL COMMENT '平台用户标识',
    AppId varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '小程序 AppId',
    ProviderProfileVersionId BINARY(16) NOT NULL COMMENT '渠道配置版本',
    OpenIdProtected text NOT NULL COMMENT '受保护 OpenId',
    OpenIdMask varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'OpenId 掩码',
    OpenIdSha256Hex char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'OpenId 指纹',
    UnionIdProtected text NULL COMMENT '受保护 UnionId',
    UnionIdMask varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT 'UnionId 掩码',
    VerificationStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '绑定状态',
    RecipientEndpointId BINARY(16) NULL COMMENT '关联收件端点',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_notifications_wechat_miniprogram_binding PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_wechat_miniprogram_binding_StatusKey
        CHECK (VerificationStatusKey IN ('verified', 'revoked')),
    CONSTRAINT UX_fn_notifications_wechat_miniprogram_binding_Scope_User_App
        UNIQUE (TenantScopeKey, UserId, AppId),
    CONSTRAINT UX_fn_notifications_wechat_miniprogram_binding_Scope_App_OpenId
        UNIQUE (TenantScopeKey, AppId, OpenIdSha256Hex)
) COMMENT='微信小程序 OpenId 绑定表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_notifications_wechat_miniprogram_subscription (
    BindingId BINARY(16) NOT NULL COMMENT '绑定标识',
    TemplateId varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '订阅模板标识',
    StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '授权状态',
    AuthorizedAtUtc datetime(6) NOT NULL COMMENT '授权时间(UTC)',
    CONSTRAINT PK_fn_notifications_wechat_miniprogram_subscription PRIMARY KEY (BindingId, TemplateId),
    CONSTRAINT CK_fn_notifications_wechat_miniprogram_subscription_StatusKey
        CHECK (StatusKey IN ('accepted', 'rejected'))
) COMMENT='微信小程序订阅消息授权表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
