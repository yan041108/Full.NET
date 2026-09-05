-- 118：DataApproval 首个纵向切片请求表。
CREATE TABLE IF NOT EXISTS fn_data_approval_request (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；Host 级为 NULL',
    ScopeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '租户作用域键',
    ScenarioKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '场景键',
    TargetEntityId BINARY(16) NOT NULL COMMENT '目标实体标识',
    StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态键',
    BeforeSnapshotJson longtext NULL COMMENT '变更前快照 JSON',
    AfterSnapshotJson longtext NOT NULL COMMENT '变更后快照 JSON',
    WorkflowInstanceId BINARY(16) NULL COMMENT '关联工作流实例标识',
    WorkflowRevision bigint NULL COMMENT '关联工作流实例修订号',
    WorkflowDefinitionVersionId BINARY(16) NOT NULL COMMENT '工作流定义版本标识',
    SubmittedByUserId BINARY(16) NOT NULL COMMENT '提交人用户标识',
    SubmittedAtUtc datetime(6) NOT NULL COMMENT '提交时间(UTC)',
    ResolvedAtUtc datetime(6) NULL COMMENT '结案时间(UTC)',
    IdempotencyKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '幂等键',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    Version bigint NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_data_approval_request PRIMARY KEY (Id),
    CONSTRAINT CK_fn_data_approval_request_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_data_approval_request_StatusKey
        CHECK (StatusKey IN ('pending', 'in_review', 'approved', 'rejected', 'cancelled')),
    CONSTRAINT CK_fn_data_approval_request_Version CHECK (Version > 0),
    UNIQUE KEY UX_fn_data_approval_request_Idempotency (TenantScopeKey, IdempotencyKey),
    KEY IX_fn_data_approval_request_SubmittedAtUtc (TenantScopeKey, SubmittedAtUtc, Id)
) COMMENT='数据审批请求表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
