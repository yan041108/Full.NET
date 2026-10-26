-- 289：签名证据采用独立活动身份，创建式 DDL 可在未记账后安全重跑。
CREATE TABLE IF NOT EXISTS fn_workflow_action_evidence (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NOT NULL COMMENT '活动步骤标识',
    TodoId BINARY(16) NOT NULL COMMENT '实际办理待办标识',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户作用域键',
    ActorUserId BINARY(16) NOT NULL COMMENT '实际执行人标识',
    OriginalAssigneeUserId BINARY(16) NOT NULL COMMENT '原办理人标识快照',
    ActionKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '稳定办理动作键',
    IdempotencyKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '动作幂等键',
    FormVersionId BINARY(16) NOT NULL COMMENT '固定表单版本标识',
    FormRevision bigint NOT NULL COMMENT '签署提交表单修订号',
    SubmissionHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '签署提交内容摘要',
    FileId BINARY(16) NOT NULL COMMENT '签名文件标识',
    FileHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '实际签名文件内容摘要',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '证据创建时间UTC',
    CONSTRAINT pk_fn_workflow_action_evidence PRIMARY KEY (Id),
    CONSTRAINT uq_fn_workflow_action_evidence_request UNIQUE (InstanceId, IdempotencyKey),
    CONSTRAINT fk_fn_workflow_action_evidence_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance (Id),
    CONSTRAINT fk_fn_workflow_action_evidence_step FOREIGN KEY (StepId) REFERENCES fn_workflow_step (Id),
    CONSTRAINT fk_fn_workflow_action_evidence_todo FOREIGN KEY (TodoId) REFERENCES fn_workflow_todo (Id)
) COMMENT='工作流动作签名证据' ENGINE=InnoDB;
