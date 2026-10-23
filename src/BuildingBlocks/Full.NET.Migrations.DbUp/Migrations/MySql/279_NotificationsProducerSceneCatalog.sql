-- 278：Notifications Producer/Scene 受控目录。
-- 由受控目录声明政策类别、合法渠道与紧急覆盖规则，避免仅靠可编辑模板把消息提升为强制通知。

CREATE TABLE IF NOT EXISTS fn_notifications_producer_scene (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    ProducerKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务生产者键',
    SceneKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务场景键',
    PolicyCategoryKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '目录声明的政策类别键',
    AllowedChannelKeysJson longtext NOT NULL COMMENT '目录声明的合法渠道键(JSON)',
    EmergencyOverrideAllowed tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否允许在静默时段紧急覆盖',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '目录项是否启用；停用后新意图失败关闭',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_notifications_producer_scene PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_producer_scene_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_notifications_producer_scene_Policy CHECK (PolicyCategoryKey IN ('mandatory', 'transactional', 'informational', 'marketing')),
    CONSTRAINT CK_fn_notifications_producer_scene_Version CHECK (Version > 0),
    CONSTRAINT UX_fn_notifications_producer_scene_Scene UNIQUE (TenantScopeKey, ProducerKey, SceneKey)
) COMMENT='通知业务生产者与场景受控目录表' ENGINE=InnoDB;
