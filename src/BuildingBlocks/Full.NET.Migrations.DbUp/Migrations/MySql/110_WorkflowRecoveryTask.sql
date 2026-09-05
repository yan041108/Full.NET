-- 110：新增工作流恢复任务表，供 Worker 扫描过期租约、卡住实例和未完成步骤后领取、续租、重试与死信。
-- MySQL 使用 STORED generated column 加 UNIQUE 表达“同一实例/种类/步骤最多一条未关闭任务”。
-- 不可逆风险：删除表会丢失未完成的恢复任务；已死信任务在人工重试前不会再自动入队。
CREATE TABLE IF NOT EXISTS fn_workflow_recovery_task (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识',
    ScopeKey varchar(16) NOT NULL COMMENT '作用域键',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NULL COMMENT '流程步骤标识',
    KindKey varchar(32) NOT NULL COMMENT '恢复种类键',
    StatusKey varchar(24) NOT NULL COMMENT '恢复任务状态键',
    AttemptCount int NOT NULL COMMENT '已尝试次数',
    Revision bigint NOT NULL COMMENT '修订号',
    LeaseOwnerKey varchar(128) NULL COMMENT '执行租约持有者键',
    LeaseExpiresAtUtc datetime(6) NULL COMMENT '租约过期时间(UTC)',
    LeaseGeneration int NOT NULL COMMENT '租约世代',
    NextAttemptAtUtc datetime(6) NULL COMMENT '下次尝试时间(UTC)',
    LastError varchar(512) NULL COMMENT '最后错误摘要',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新时间(UTC)',
    OpenOccupancyKey varchar(200) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
        GENERATED ALWAYS AS (
            CASE WHEN StatusKey IN ('pending', 'failed', 'dead_lettered')
                THEN CONCAT(TenantScopeKey, '|', HEX(InstanceId), '|', KindKey, '|', IFNULL(HEX(StepId), 'NONE'))
                ELSE NULL END) STORED COMMENT '未关闭恢复任务占用键',
    CONSTRAINT PK_fn_workflow_recovery_task PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_recovery_task_Instance FOREIGN KEY (InstanceId)
        REFERENCES fn_workflow_instance(Id),
    CONSTRAINT FK_fn_workflow_recovery_task_Step FOREIGN KEY (StepId)
        REFERENCES fn_workflow_step(Id),
    CONSTRAINT CK_fn_workflow_recovery_task_ScopeKey CHECK (ScopeKey IN ('host', 'tenant')),
    CONSTRAINT CK_fn_workflow_recovery_task_Kind CHECK (KindKey IN ('expired_lease', 'stuck_instance', 'incomplete_step')),
    CONSTRAINT CK_fn_workflow_recovery_task_Status CHECK (StatusKey IN ('pending', 'succeeded', 'failed', 'dead_lettered', 'cancelled')),
    CONSTRAINT CK_fn_workflow_recovery_task_Attempt CHECK (AttemptCount >= 0),
    CONSTRAINT CK_fn_workflow_recovery_task_Revision CHECK (Revision > 0),
    CONSTRAINT CK_fn_workflow_recovery_task_Generation CHECK (LeaseGeneration >= 0)
) COMMENT='工作流恢复任务表' ENGINE=InnoDB;

SET @hasOpenOccupancy := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_recovery_task'
      AND INDEX_NAME = 'UX_fn_workflow_recovery_task_OpenOccupancy');
SET @addOpenOccupancy := IF(
    @hasOpenOccupancy = 0,
    'ALTER TABLE fn_workflow_recovery_task ADD CONSTRAINT UX_fn_workflow_recovery_task_OpenOccupancy UNIQUE (OpenOccupancyKey)',
    'SELECT 1');
PREPARE stmt FROM @addOpenOccupancy;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasClaim := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_recovery_task'
      AND INDEX_NAME = 'IX_fn_workflow_recovery_task_Claim');
SET @addClaim := IF(
    @hasClaim = 0,
    'ALTER TABLE fn_workflow_recovery_task ADD INDEX IX_fn_workflow_recovery_task_Claim (StatusKey, NextAttemptAtUtc, CreatedAtUtc, Id)',
    'SELECT 1');
PREPARE stmt FROM @addClaim;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
