-- 104：建立 Notifications 平台内核表（模板版本、Intent/Recipient、投递/尝试/回执、Profile/Binding、偏好与领域审计）。
-- MySQL 以 InnoDB 主键作为聚集路径；发布版本表用 BEFORE UPDATE 触发器禁止变更。
CREATE TABLE IF NOT EXISTS fn_notifications_template (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    TemplateKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '模板稳定键',
    ChannelKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '渠道键',
    ContentCategoryKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容政策类别键',
    DraftSubject varchar(256) NOT NULL COMMENT '草稿主题',
    DraftBodyJson longtext NOT NULL COMMENT '草稿正文(JSON)',
    DraftParameterSchemaJson longtext NOT NULL COMMENT '草稿参数结构(JSON)',
    DraftRevision bigint NOT NULL COMMENT '草稿修订号',
    LatestPublishedVersionId BINARY(16) NULL COMMENT '最新已发布版本标识',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_notifications_template PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_template_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_notifications_template_Category CHECK (ContentCategoryKey IN ('mandatory', 'transactional', 'informational', 'marketing')),
    CONSTRAINT CK_fn_notifications_template_Revision CHECK (DraftRevision > 0),
    CONSTRAINT CK_fn_notifications_template_Version CHECK (Version > 0),
    CONSTRAINT UX_fn_notifications_template_Scope_Key UNIQUE (TenantScopeKey, TemplateKey)
) COMMENT='通知模板表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_template_version (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TemplateId BINARY(16) NOT NULL COMMENT '模板标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    SchemaVersion int NOT NULL COMMENT 'Schema 版本',
    Subject varchar(256) NOT NULL COMMENT '主题',
    BodyJson longtext NOT NULL COMMENT 'Body(JSON)',
    ParameterSchemaJson longtext NOT NULL COMMENT '参数结构(JSON)',
    ContentClassificationKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容安全分级键',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容哈希',
    PublishedById BINARY(16) NOT NULL COMMENT '发布人标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    CONSTRAINT PK_fn_notifications_template_version PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_template_version_Template FOREIGN KEY (TemplateId) REFERENCES fn_notifications_template(Id),
    CONSTRAINT CK_fn_notifications_template_version_Number CHECK (VersionNumber > 0),
    CONSTRAINT CK_fn_notifications_template_version_Schema CHECK (SchemaVersion > 0),
    CONSTRAINT UX_fn_notifications_template_version_Number UNIQUE (TemplateId, VersionNumber),
    CONSTRAINT UX_fn_notifications_template_version_Hash UNIQUE (TemplateId, ContentHash)
) COMMENT='通知模板版本表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_provider_profile (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    ProfileKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '渠道配置稳定键',
    ProviderTypeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '渠道类型键',
    NonSecretConfigJson longtext NOT NULL COMMENT '非密钥配置(JSON)',
    SecretReference varchar(256) NULL COMMENT '密钥引用',
    IsEnabled tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否启用',
    DraftRevision bigint NOT NULL COMMENT '草稿修订号',
    LatestPublishedVersionId BINARY(16) NULL COMMENT '最新已发布版本标识',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_notifications_provider_profile PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_provider_profile_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_notifications_provider_profile_Revision CHECK (DraftRevision > 0),
    CONSTRAINT CK_fn_notifications_provider_profile_Version CHECK (Version > 0),
    CONSTRAINT UX_fn_notifications_provider_profile_Scope_Key UNIQUE (TenantScopeKey, ProfileKey)
) COMMENT='通知渠道配置表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_provider_profile_version (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ProfileId BINARY(16) NOT NULL COMMENT '渠道配置标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    ProviderTypeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '渠道类型键',
    AdapterVersion varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '适配器版本',
    NonSecretConfigJson longtext NOT NULL COMMENT '非密钥配置(JSON)',
    SecretReference varchar(256) NULL COMMENT '密钥引用',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容哈希',
    PublishedById BINARY(16) NOT NULL COMMENT '发布人标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    CONSTRAINT PK_fn_notifications_provider_profile_version PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_profile_version_Profile FOREIGN KEY (ProfileId) REFERENCES fn_notifications_provider_profile(Id),
    CONSTRAINT CK_fn_notifications_profile_version_Number CHECK (VersionNumber > 0),
    CONSTRAINT UX_fn_notifications_profile_version_Number UNIQUE (ProfileId, VersionNumber),
    CONSTRAINT UX_fn_notifications_profile_version_Hash UNIQUE (ProfileId, ContentHash)
) COMMENT='通知渠道配置版本表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_binding (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    BindingKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '场景绑定稳定键',
    DraftDispatchModeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '草稿路由模式键',
    DraftJson longtext NOT NULL COMMENT '草稿(JSON)',
    DraftRevision bigint NOT NULL COMMENT '草稿修订号',
    LatestPublishedVersionId BINARY(16) NULL COMMENT '最新已发布版本标识',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_notifications_binding PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_binding_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_notifications_binding_Mode CHECK (DraftDispatchModeKey IN ('single', 'fan_out', 'failover', 'match')),
    CONSTRAINT CK_fn_notifications_binding_Revision CHECK (DraftRevision > 0),
    CONSTRAINT CK_fn_notifications_binding_Version CHECK (Version > 0),
    CONSTRAINT UX_fn_notifications_binding_Scope_Key UNIQUE (TenantScopeKey, BindingKey)
) COMMENT='通知场景绑定表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_binding_version (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    BindingId BINARY(16) NOT NULL COMMENT '场景绑定标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    ProducerKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务生产者键',
    SceneKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务场景键',
    ChannelKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '渠道键',
    DispatchModeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '路由模式键',
    BindingTargetsJson longtext NOT NULL COMMENT '绑定目标(JSON)',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容哈希',
    PublishedById BINARY(16) NOT NULL COMMENT '发布人标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    CONSTRAINT PK_fn_notifications_binding_version PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_binding_version_Binding FOREIGN KEY (BindingId) REFERENCES fn_notifications_binding(Id),
    CONSTRAINT CK_fn_notifications_binding_version_Number CHECK (VersionNumber > 0),
    CONSTRAINT CK_fn_notifications_binding_version_Mode CHECK (DispatchModeKey IN ('single', 'fan_out', 'failover', 'match')),
    CONSTRAINT UX_fn_notifications_binding_version_Number UNIQUE (BindingId, VersionNumber),
    CONSTRAINT UX_fn_notifications_binding_version_Hash UNIQUE (BindingId, ContentHash)
) COMMENT='通知场景绑定版本表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_intent (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    ProducerKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务生产者键',
    SceneKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务场景键',
    IdempotencyKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '幂等键',
    TemplateVersionId BINARY(16) NOT NULL COMMENT '模板版本标识',
    BindingVersionId BINARY(16) NULL COMMENT '场景绑定版本标识',
    PolicyCategoryKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '政策类别键',
    DispatchModeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '路由模式键',
    RouteSnapshotJson longtext NOT NULL COMMENT '路由快照(JSON)',
    ParameterSnapshotJson longtext NOT NULL COMMENT '参数快照(JSON)',
    StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '意图状态键',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    Revision bigint NOT NULL DEFAULT 1 COMMENT '修订号',
    CONSTRAINT PK_fn_notifications_intent PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_intent_TemplateVersion FOREIGN KEY (TemplateVersionId) REFERENCES fn_notifications_template_version(Id),
    CONSTRAINT FK_fn_notifications_intent_BindingVersion FOREIGN KEY (BindingVersionId) REFERENCES fn_notifications_binding_version(Id),
    CONSTRAINT CK_fn_notifications_intent_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_notifications_intent_Policy CHECK (PolicyCategoryKey IN ('mandatory', 'transactional', 'informational', 'marketing')),
    CONSTRAINT CK_fn_notifications_intent_Mode CHECK (DispatchModeKey IN ('single', 'fan_out', 'failover', 'match')),
    CONSTRAINT CK_fn_notifications_intent_Revision CHECK (Revision > 0),
    CONSTRAINT UX_fn_notifications_intent_Idempotency UNIQUE (TenantScopeKey, ProducerKey, IdempotencyKey),
    INDEX IX_fn_notifications_intent_Created (CreatedAtUtc, Id)
) COMMENT='通知意图表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_recipient (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    IntentId BINARY(16) NOT NULL COMMENT '通知意图标识',
    RecipientTypeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '收件人类型键',
    RecipientKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '收件人稳定键',
    UserId BINARY(16) NULL COMMENT '用户标识',
    AddressDigest char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '地址摘要',
    ResolutionStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '解析状态键',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_notifications_recipient PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_recipient_Intent FOREIGN KEY (IntentId) REFERENCES fn_notifications_intent(Id),
    CONSTRAINT CK_fn_notifications_recipient_Status CHECK (ResolutionStatusKey IN ('pending', 'resolved', 'failed')),
    CONSTRAINT UX_fn_notifications_recipient_Intent_Key UNIQUE (IntentId, RecipientTypeKey, RecipientKey)
) COMMENT='通知收件人表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_recipient_endpoint (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    ProviderProfileVersionId BINARY(16) NOT NULL COMMENT '渠道配置版本标识',
    EndpointKindKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '端点类型键',
    ProtectedValue varchar(1024) NOT NULL COMMENT '受保护的端点原值',
    MaskedValue varchar(128) NOT NULL COMMENT '脱敏后的端点值',
    VerificationStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '端点验证状态键',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_notifications_recipient_endpoint PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_endpoint_ProfileVersion FOREIGN KEY (ProviderProfileVersionId) REFERENCES fn_notifications_provider_profile_version(Id),
    CONSTRAINT CK_fn_notifications_endpoint_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT UX_fn_notifications_endpoint_Scope_User_Profile_Kind UNIQUE (TenantScopeKey, UserId, ProviderProfileVersionId, EndpointKindKey)
) COMMENT='通知收件端点表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_preference (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    ChannelKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '渠道键',
    ChannelOptedOut tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否关闭该渠道',
    MarketingConsentGranted tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否授予营销同意',
    QuietHoursJson longtext NULL COMMENT '静默时段(JSON)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_notifications_preference PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_preference_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_notifications_preference_Version CHECK (Version > 0),
    CONSTRAINT UX_fn_notifications_preference_Scope_User_Channel UNIQUE (TenantScopeKey, UserId, ChannelKey)
) COMMENT='通知偏好表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_delivery (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    IntentId BINARY(16) NOT NULL COMMENT '通知意图标识',
    RecipientId BINARY(16) NOT NULL COMMENT '收件人标识',
    ChannelKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '渠道键',
    ProviderProfileVersionId BINARY(16) NULL COMMENT '渠道配置版本标识',
    BindingVersionId BINARY(16) NULL COMMENT '场景绑定版本标识',
    StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '投递状态键',
    Revision bigint NOT NULL DEFAULT 1 COMMENT '修订号',
    LeaseOwnerKey varchar(128) NULL COMMENT '租约持有者键',
    LeaseExpiresAtUtc datetime(6) NULL COMMENT '租约过期时间(UTC)',
    LeaseGeneration bigint NOT NULL DEFAULT 1 COMMENT '租约世代',
    NextAttemptAtUtc datetime(6) NULL COMMENT '下次重试时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_notifications_delivery PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_delivery_Intent FOREIGN KEY (IntentId) REFERENCES fn_notifications_intent(Id),
    CONSTRAINT FK_fn_notifications_delivery_Recipient FOREIGN KEY (RecipientId) REFERENCES fn_notifications_recipient(Id),
    CONSTRAINT FK_fn_notifications_delivery_ProfileVersion FOREIGN KEY (ProviderProfileVersionId) REFERENCES fn_notifications_provider_profile_version(Id),
    CONSTRAINT FK_fn_notifications_delivery_BindingVersion FOREIGN KEY (BindingVersionId) REFERENCES fn_notifications_binding_version(Id),
    CONSTRAINT CK_fn_notifications_delivery_Status CHECK (StatusKey IN ('persisted', 'accepted', 'sent', 'delivered', 'unknown', 'read', 'failed', 'suppressed', 'dead_lettered')),
    CONSTRAINT CK_fn_notifications_delivery_Revision CHECK (Revision > 0),
    CONSTRAINT CK_fn_notifications_delivery_LeaseGen CHECK (LeaseGeneration > 0),
    CONSTRAINT UX_fn_notifications_delivery_Recipient_Channel_Profile UNIQUE (RecipientId, ChannelKey, ProviderProfileVersionId),
    INDEX IX_fn_notifications_delivery_Created (CreatedAtUtc, Id),
    INDEX IX_fn_notifications_delivery_Lease (StatusKey, NextAttemptAtUtc, LeaseExpiresAtUtc)
) COMMENT='通知渠道投递表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_delivery_attempt (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    DeliveryId BINARY(16) NOT NULL COMMENT '投递标识',
    AttemptNumber int NOT NULL COMMENT '尝试序号',
    LeaseOwnerKey varchar(128) NULL COMMENT '租约持有者键',
    LeaseGeneration bigint NOT NULL COMMENT '租约世代',
    LeaseExpiresAtUtc datetime(6) NULL COMMENT '租约过期时间(UTC)',
    ResultCategoryKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '结果类别键',
    StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '尝试状态键',
    ProviderMessageId varchar(128) NULL COMMENT '厂商消息标识',
    ErrorCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '错误码',
    ReceiptDigest char(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '回执摘要',
    StartedAtUtc datetime(6) NOT NULL COMMENT '开始时间(UTC)',
    FinishedAtUtc datetime(6) NULL COMMENT '结束时间(UTC)',
    CONSTRAINT PK_fn_notifications_delivery_attempt PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_attempt_Delivery FOREIGN KEY (DeliveryId) REFERENCES fn_notifications_delivery(Id),
    CONSTRAINT CK_fn_notifications_attempt_Number CHECK (AttemptNumber > 0),
    CONSTRAINT CK_fn_notifications_attempt_LeaseGen CHECK (LeaseGeneration > 0),
    CONSTRAINT UX_fn_notifications_attempt_Delivery_Number UNIQUE (DeliveryId, AttemptNumber),
    INDEX IX_fn_notifications_attempt_Started (StartedAtUtc, Id)
) COMMENT='通知投递尝试表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_receipt (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ProviderTypeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '渠道类型键',
    ProviderMessageId varchar(128) NULL COMMENT '厂商消息标识',
    ReceiptIdempotencyKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '回执幂等键',
    DeliveryId BINARY(16) NULL COMMENT '投递标识',
    ExternalStatusKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '外部回执状态键',
    MappedStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '映射后的投递状态键',
    PayloadDigest char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '载荷摘要',
    ReceivedAtUtc datetime(6) NOT NULL COMMENT '接收时间(UTC)',
    ProcessedAtUtc datetime(6) NULL COMMENT '处理完成时间(UTC)',
    ProcessStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '回执处理状态键',
    CONSTRAINT PK_fn_notifications_receipt PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_receipt_Delivery FOREIGN KEY (DeliveryId) REFERENCES fn_notifications_delivery(Id),
    CONSTRAINT CK_fn_notifications_receipt_Mapped CHECK (MappedStatusKey IN ('persisted', 'accepted', 'sent', 'delivered', 'unknown', 'read', 'failed', 'suppressed', 'dead_lettered')),
    CONSTRAINT UX_fn_notifications_receipt_Idempotency UNIQUE (ProviderTypeKey, ReceiptIdempotencyKey),
    INDEX IX_fn_notifications_receipt_Received (ReceivedAtUtc, Id)
) COMMENT='通知投递回执表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_notifications_domain_audit (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    IntentId BINARY(16) NULL COMMENT '通知意图标识',
    OperationKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作键',
    ActorUserId BINARY(16) NOT NULL COMMENT '操作者用户标识',
    ResourceTypeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '资源类型键',
    ResourceId BINARY(16) NOT NULL COMMENT '资源标识',
    OutcomeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作结果键',
    DetailJson longtext NULL COMMENT '审计详情(JSON)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_notifications_domain_audit PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_domain_audit_Intent FOREIGN KEY (IntentId) REFERENCES fn_notifications_intent(Id),
    CONSTRAINT CK_fn_notifications_domain_audit_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    INDEX IX_fn_notifications_domain_audit_Created (CreatedAtUtc, Id),
    INDEX IX_fn_notifications_domain_audit_Resource (ResourceTypeKey, ResourceId, CreatedAtUtc)
) COMMENT='通知领域审计表' ENGINE=InnoDB;

DROP TRIGGER IF EXISTS TR_fn_notifications_template_version_Immutable;
DROP TRIGGER IF EXISTS TR_fn_notifications_profile_version_Immutable;
DROP TRIGGER IF EXISTS TR_fn_notifications_binding_version_Immutable;
DELIMITER $$
CREATE TRIGGER TR_fn_notifications_template_version_Immutable
BEFORE UPDATE ON fn_notifications_template_version
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Published notification template versions are immutable.';
END$$
CREATE TRIGGER TR_fn_notifications_profile_version_Immutable
BEFORE UPDATE ON fn_notifications_provider_profile_version
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Published notification provider profile versions are immutable.';
END$$
CREATE TRIGGER TR_fn_notifications_binding_version_Immutable
BEFORE UPDATE ON fn_notifications_binding_version
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Published notification binding versions are immutable.';
END$$
DELIMITER ;
