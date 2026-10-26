-- 288：启动回执独立于运行状态；创建式 DDL 可安全重跑。
CREATE TABLE IF NOT EXISTS fn_workflow_start_receipt (
    InstanceId BINARY(16) NOT NULL COMMENT '实例标识',
    TenantScopeKey VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户作用域',
    ActorUserId BINARY(16) NOT NULL COMMENT '发起人标识',
    IdempotencyKey VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '启动幂等键',
    RequestHash VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '启动请求摘要',
    ResponseJson LONGTEXT NOT NULL COMMENT '不可变启动响应',
    CONSTRAINT pk_fn_workflow_start_receipt PRIMARY KEY (InstanceId),
    CONSTRAINT uq_fn_workflow_start_receipt_request UNIQUE (TenantScopeKey, ActorUserId, IdempotencyKey),
    CONSTRAINT fk_fn_workflow_start_receipt_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance (Id)
) COMMENT='工作流不可变启动回执' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_activity_work (
    Id BINARY(16) NOT NULL COMMENT '耐久工作标识',
    InstanceId BINARY(16) NOT NULL COMMENT '实例标识',
    TenantScopeKey VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户作用域',
    NodeKey VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '续办节点键',
    InputJson LONGTEXT NOT NULL COMMENT '固定执行输入',
    InputHash VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '输入摘要',
    InputFormRevision BIGINT NOT NULL COMMENT '输入表单修订号',
    ExecutionSequence BIGINT NOT NULL COMMENT '排队时执行序号',
    ParallelJoinId BINARY(16) NULL COMMENT '所属分叉激活标识',
    ParallelBranchKey VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '所属分支键',
    DueAtUtc DATETIME(6) NOT NULL COMMENT '最早续办时间',
    StatusKey VARCHAR(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '工作状态',
    LeaseGeneration BIGINT NOT NULL COMMENT '租约代次',
    AttemptCount INT NOT NULL COMMENT '领取次数',
    LeaseOwnerKey VARCHAR(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '租约持有者',
    LeaseExpiresAtUtc DATETIME(6) NULL COMMENT '租约截止时间',
    CompletedAtUtc DATETIME(6) NULL COMMENT '工作结束时间',
    ResultStatusKey VARCHAR(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '提交结果状态',
    ResultTodoId BINARY(16) NULL COMMENT '结果首待办标识',
    CONSTRAINT pk_fn_workflow_activity_work PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_activity_work_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance (Id),
    INDEX ix_fn_workflow_activity_work_due (StatusKey, DueAtUtc, Id, InstanceId, LeaseExpiresAtUtc)
) COMMENT='工作流耐久续办活动' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_branch_state (
    ParallelJoinId BINARY(16) NOT NULL COMMENT '所属分叉激活标识',
    BranchKey VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '稳定分支键',
    InputJson LONGTEXT NOT NULL COMMENT '共同冻结输入',
    InputFormRevision BIGINT NOT NULL COMMENT '共同输入表单修订号',
    OutputJson LONGTEXT NOT NULL COMMENT '分支局部输出补丁',
    Revision BIGINT NOT NULL COMMENT '分支输出修订号',
    CONSTRAINT pk_fn_workflow_branch_state PRIMARY KEY (ParallelJoinId, BranchKey),
    CONSTRAINT fk_fn_workflow_branch_state_join FOREIGN KEY (ParallelJoinId) REFERENCES fn_workflow_parallel_join (Id)
) COMMENT='工作流分支输入与独立输出' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_branch_merge (
    ParallelJoinId BINARY(16) NOT NULL COMMENT '所属分叉激活标识',
    FormRevision BIGINT NOT NULL COMMENT '合并后的表单修订号',
    OutputJson LONGTEXT NOT NULL COMMENT '固定合并结果',
    MergedAtUtc DATETIME(6) NOT NULL COMMENT '合并时间',
    CONSTRAINT pk_fn_workflow_branch_merge PRIMARY KEY (ParallelJoinId),
    CONSTRAINT fk_fn_workflow_branch_merge_join FOREIGN KEY (ParallelJoinId) REFERENCES fn_workflow_parallel_join (Id)
) COMMENT='工作流分支原子合并回执' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_fork_context (
    ParallelJoinId BINARY(16) NOT NULL COMMENT '分叉激活标识',
    ParentParallelJoinId BINARY(16) NULL COMMENT '父分叉激活标识',
    ParentBranchKey VARCHAR(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT '父分支稳定键',
    CONSTRAINT pk_fn_workflow_fork_context PRIMARY KEY (ParallelJoinId),
    CONSTRAINT fk_fn_workflow_fork_context_join FOREIGN KEY (ParallelJoinId) REFERENCES fn_workflow_parallel_join (Id),
    CONSTRAINT fk_fn_workflow_fork_context_parent FOREIGN KEY (ParentParallelJoinId) REFERENCES fn_workflow_parallel_join (Id)
) COMMENT='工作流分叉激活所属执行范围' ENGINE=InnoDB;
