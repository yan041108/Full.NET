-- 102：建立 Workflow 首个纵向切片的模块自有数据模型与并发不变量。
CREATE TABLE IF NOT EXISTS fn_workflow_definition (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    DefinitionKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '流程定义稳定键',
    DraftId BINARY(16) NULL COMMENT '当前草稿标识',
    LatestPublishedVersionId BINARY(16) NULL COMMENT '最新已发布版本标识',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_workflow_definition PRIMARY KEY (Id),
    CONSTRAINT CK_fn_workflow_definition_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_workflow_definition_Version CHECK (Version > 0),
    CONSTRAINT UX_fn_workflow_definition_Scope_DefinitionKey UNIQUE (TenantScopeKey, DefinitionKey)
) COMMENT='工作流定义表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_definition_draft (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    DefinitionId BINARY(16) NOT NULL COMMENT '流程定义标识',
    DraftJson longtext NOT NULL COMMENT '流程定义草稿(JSON)',
    DraftRevision bigint NOT NULL COMMENT '草稿修订号',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容哈希',
    UpdatedById BINARY(16) NOT NULL COMMENT '更新人标识',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_workflow_definition_draft PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_definition_draft_Definition FOREIGN KEY (DefinitionId) REFERENCES fn_workflow_definition(Id),
    CONSTRAINT CK_fn_workflow_definition_draft_Revision CHECK (DraftRevision > 0),
    CONSTRAINT UX_fn_workflow_definition_draft_DefinitionId UNIQUE (DefinitionId)
) COMMENT='工作流定义草稿表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_definition_version (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    DefinitionId BINARY(16) NOT NULL COMMENT '流程定义标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    SchemaVersion int NOT NULL COMMENT '结构版本',
    CanonicalJson longtext NOT NULL COMMENT '规范化流程定义(JSON)',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容哈希',
    PublishedById BINARY(16) NOT NULL COMMENT '发布人标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    CONSTRAINT PK_fn_workflow_definition_version PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_definition_version_Definition FOREIGN KEY (DefinitionId) REFERENCES fn_workflow_definition(Id),
    CONSTRAINT CK_fn_workflow_definition_version_Number CHECK (VersionNumber > 0),
    CONSTRAINT CK_fn_workflow_definition_version_Schema CHECK (SchemaVersion > 0),
    CONSTRAINT UX_fn_workflow_definition_version_Definition_Number UNIQUE (DefinitionId, VersionNumber),
    CONSTRAINT UX_fn_workflow_definition_version_Definition_Hash UNIQUE (DefinitionId, ContentHash)
) COMMENT='工作流定义发布版本表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_form_definition (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    FormKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '表单稳定键',
    DraftSchemaJson longtext NOT NULL COMMENT '表单草稿结构(JSON)',
    DraftRevision bigint NOT NULL COMMENT '草稿修订号',
    LatestPublishedVersionId BINARY(16) NULL COMMENT '最新已发布版本标识',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_workflow_form_definition PRIMARY KEY (Id),
    CONSTRAINT CK_fn_workflow_form_definition_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_workflow_form_definition_Revision CHECK (DraftRevision > 0),
    CONSTRAINT UX_fn_workflow_form_definition_Scope_FormKey UNIQUE (TenantScopeKey, FormKey)
) COMMENT='工作流表单定义表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_form_version (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    FormDefinitionId BINARY(16) NOT NULL COMMENT '表单定义标识',
    VersionNumber int NOT NULL COMMENT '版本号',
    SchemaVersion int NOT NULL COMMENT '结构版本',
    AdapterVersion int NOT NULL COMMENT '表单适配器版本',
    ComponentCatalogVersion int NOT NULL COMMENT '组件目录版本',
    FormSchemaJson longtext NOT NULL COMMENT '表单结构(JSON)',
    WebRenderSchemaJson longtext NOT NULL COMMENT 'Web 渲染结构(JSON)',
    ContentHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '内容哈希',
    PublishedById BINARY(16) NOT NULL COMMENT '发布人标识',
    PublishedAtUtc datetime(6) NOT NULL COMMENT '发布时间(UTC)',
    CONSTRAINT PK_fn_workflow_form_version PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_form_version_Definition FOREIGN KEY (FormDefinitionId) REFERENCES fn_workflow_form_definition(Id),
    CONSTRAINT CK_fn_workflow_form_version_Number CHECK (VersionNumber > 0),
    CONSTRAINT UX_fn_workflow_form_version_Definition_Number UNIQUE (FormDefinitionId, VersionNumber),
    CONSTRAINT UX_fn_workflow_form_version_Definition_Hash UNIQUE (FormDefinitionId, ContentHash)
) COMMENT='工作流表单发布版本表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_instance (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    DefinitionVersionId BINARY(16) NOT NULL COMMENT '流程定义版本标识',
    FormVersionId BINARY(16) NULL COMMENT '表单版本标识',
    BusinessType varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务对象类型',
    BusinessId varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '业务对象标识',
    StatusKey varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态键',
    Revision bigint NOT NULL COMMENT '修订号',
    StartedById BINARY(16) NOT NULL COMMENT '发起人标识',
    StartedAtUtc datetime(6) NOT NULL COMMENT '开始时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    CancelledById BINARY(16) NULL COMMENT '取消人标识',
    CancelledAtUtc datetime(6) NULL COMMENT '取消时间(UTC)',
    CancellationReason varchar(512) NULL COMMENT '取消原因',
    LeaseOwnerKey varchar(128) NULL COMMENT '执行租约持有者键',
    LeaseExpiresAtUtc datetime(6) NULL COMMENT '租约过期时间(UTC)',
    ActiveBusinessKey varchar(258) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
        GENERATED ALWAYS AS (CASE WHEN StatusKey = 'active' THEN CONCAT(TenantScopeKey, '|', BusinessType, '|', BusinessId) ELSE NULL END) STORED COMMENT '活动实例业务唯一键',
    CONSTRAINT PK_fn_workflow_instance PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_instance_DefinitionVersion FOREIGN KEY (DefinitionVersionId) REFERENCES fn_workflow_definition_version(Id),
    CONSTRAINT FK_fn_workflow_instance_FormVersion FOREIGN KEY (FormVersionId) REFERENCES fn_workflow_form_version(Id),
    CONSTRAINT CK_fn_workflow_instance_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_workflow_instance_Status CHECK (StatusKey IN ('active', 'completed', 'rejected', 'cancelled', 'suspended')),
    CONSTRAINT CK_fn_workflow_instance_Revision CHECK (Revision > 0),
    CONSTRAINT UX_fn_workflow_instance_ActiveBusinessKey UNIQUE (ActiveBusinessKey),
    INDEX IX_fn_workflow_instance_Scope_Status (TenantScopeKey, StatusKey, StartedAtUtc)
) COMMENT='工作流实例表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_step (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    NodeKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '流程节点键',
    NodeTypeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '流程节点类型键',
    StatusKey varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态键',
    AssignedUserId BINARY(16) NULL COMMENT '当前处理人标识',
    DueAtUtc datetime(6) NULL COMMENT '处理截止时间(UTC)',
    AttemptCount int NOT NULL DEFAULT 0 COMMENT '尝试次数',
    Revision bigint NOT NULL COMMENT '修订号',
    StartedAtUtc datetime(6) NOT NULL COMMENT '开始时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    CONSTRAINT PK_fn_workflow_step PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_step_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT CK_fn_workflow_step_Revision CHECK (Revision > 0),
    CONSTRAINT CK_fn_workflow_step_Attempts CHECK (AttemptCount >= 0),
    INDEX IX_fn_workflow_step_Instance_Status (InstanceId, StatusKey)
) COMMENT='工作流步骤表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_todo (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NOT NULL COMMENT '流程步骤标识',
    AssigneeUserId BINARY(16) NOT NULL COMMENT '待办处理人标识',
    StatusKey varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态键',
    Revision bigint NOT NULL COMMENT '修订号',
    ArrivedAtUtc datetime(6) NOT NULL COMMENT '待办到达时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    ResultActionKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '处理结果动作键',
    CONSTRAINT PK_fn_workflow_todo PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_todo_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT FK_fn_workflow_todo_Step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id),
    CONSTRAINT CK_fn_workflow_todo_Revision CHECK (Revision > 0),
    CONSTRAINT UX_fn_workflow_todo_Step_Assignee UNIQUE (StepId, AssigneeUserId),
    INDEX IX_fn_workflow_todo_Assignee_Status (AssigneeUserId, StatusKey, ArrivedAtUtc)
) COMMENT='工作流待办表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_cc (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NULL COMMENT '流程步骤标识',
    RecipientUserId BINARY(16) NOT NULL COMMENT '接收人用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    ReadAtUtc datetime(6) NULL COMMENT '已读时间(UTC)',
    CONSTRAINT PK_fn_workflow_cc PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_cc_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT FK_fn_workflow_cc_Step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id),
    CONSTRAINT UX_fn_workflow_cc_Instance_Recipient UNIQUE (InstanceId, RecipientUserId)
) COMMENT='工作流抄送记录表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_form_submission (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    FormVersionId BINARY(16) NOT NULL COMMENT '表单版本标识',
    SubmissionJson longtext NOT NULL COMMENT '表单提交数据(JSON)',
    DataClassificationSummary varchar(512) NOT NULL COMMENT '数据分级摘要',
    Revision bigint NOT NULL COMMENT '修订号',
    UpdatedById BINARY(16) NOT NULL COMMENT '更新人标识',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_workflow_form_submission PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_form_submission_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT FK_fn_workflow_form_submission_FormVersion FOREIGN KEY (FormVersionId) REFERENCES fn_workflow_form_version(Id),
    CONSTRAINT CK_fn_workflow_form_submission_Revision CHECK (Revision > 0),
    CONSTRAINT UX_fn_workflow_form_submission_Instance UNIQUE (InstanceId)
) COMMENT='工作流表单提交表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_action_record (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NULL COMMENT '流程步骤标识',
    TodoId BINARY(16) NULL COMMENT '待办标识',
    ActionKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作键',
    ActorUserId BINARY(16) NOT NULL COMMENT '操作者用户标识',
    InstanceRevision bigint NOT NULL COMMENT '流程实例修订号',
    IdempotencyKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '幂等键',
    CommentSummary varchar(512) NULL COMMENT '动作意见摘要',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_workflow_action_record PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_action_record_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT FK_fn_workflow_action_record_Step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id),
    CONSTRAINT FK_fn_workflow_action_record_Todo FOREIGN KEY (TodoId) REFERENCES fn_workflow_todo(Id),
    CONSTRAINT UX_fn_workflow_action_record_Instance_Idempotency UNIQUE (InstanceId, IdempotencyKey)
) COMMENT='工作流动作记录表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_execution_log (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NULL COMMENT '流程步骤标识',
    TransitionKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态迁移键',
    FromStatusKey varchar(24) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '迁移前状态键',
    ToStatusKey varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '迁移后状态键',
    IdempotencyKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '幂等键',
    Summary varchar(1024) NULL COMMENT '执行摘要',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_workflow_execution_log PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_execution_log_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT FK_fn_workflow_execution_log_Step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id),
    INDEX IX_fn_workflow_execution_log_Instance_Created (InstanceId, CreatedAtUtc)
) COMMENT='工作流执行日志表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_domain_audit (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    InstanceId BINARY(16) NULL COMMENT '流程实例标识',
    OperationKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作键',
    ActorUserId BINARY(16) NOT NULL COMMENT '操作者用户标识',
    ResourceTypeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '资源类型键',
    ResourceId BINARY(16) NOT NULL COMMENT '资源标识',
    OutcomeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '操作结果键',
    DetailJson longtext NULL COMMENT '审计详情(JSON)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_workflow_domain_audit PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_domain_audit_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT CK_fn_workflow_domain_audit_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    INDEX IX_fn_workflow_domain_audit_Resource (ResourceTypeKey, ResourceId, CreatedAtUtc)
) COMMENT='工作流领域审计表' ENGINE=InnoDB;

-- 发布版本是追加式事实；任何业务列更新都必须失败关闭。
DROP TRIGGER IF EXISTS TR_fn_workflow_definition_version_Immutable;
DROP TRIGGER IF EXISTS TR_fn_workflow_form_version_Immutable;
DELIMITER $$
CREATE TRIGGER TR_fn_workflow_definition_version_Immutable
BEFORE UPDATE ON fn_workflow_definition_version
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Published workflow definition versions are immutable.';
END$$
CREATE TRIGGER TR_fn_workflow_form_version_Immutable
BEFORE UPDATE ON fn_workflow_form_version
FOR EACH ROW
BEGIN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Published workflow form versions are immutable.';
END$$
DELIMITER ;
