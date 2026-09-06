-- 161：钉钉审批镜像同步记录表。

CREATE TABLE IF NOT EXISTS fn_notifications_dingtalk_approval_sync (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '租户作用域键',
    WorkflowInstanceId BINARY(16) NOT NULL COMMENT '工作流实例标识',
    IdempotencyKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '幂等键',
    DingTalkProcessInstanceId varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '钉钉审批实例标识',
    ProcessCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '审批模板编码',
    OriginatorUserId varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '发起人 userId',
    DeptId bigint NOT NULL COMMENT '部门标识',
    Title varchar(256) NOT NULL COMMENT '审批标题',
    Summary varchar(2000) NULL COMMENT '审批摘要',
    StatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '镜像状态键',
    ExternalStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '钉钉状态',
    ExternalResultKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '钉钉结果',
    LastErrorCode varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '最近错误码',
    LastSyncedAtUtc datetime(6) NULL COMMENT '最近同步时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人',
    CONSTRAINT PK_fn_notifications_dingtalk_approval_sync PRIMARY KEY (Id),
    CONSTRAINT CK_fn_notifications_dingtalk_approval_sync_StatusKey
        CHECK (StatusKey IN (
            'pending_outbound', 'outbound_failed', 'running', 'completed', 'terminated')),
    CONSTRAINT UX_fn_notifications_dingtalk_approval_sync_Scope_Workflow
        UNIQUE (TenantScopeKey, WorkflowInstanceId),
    KEY IX_fn_notifications_dingtalk_approval_sync_StatusKey (StatusKey, UpdatedAtUtc, CreatedAtUtc),
    KEY IX_fn_notifications_dingtalk_approval_sync_ProcessInstance (DingTalkProcessInstanceId)
) COMMENT='钉钉审批镜像同步记录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
