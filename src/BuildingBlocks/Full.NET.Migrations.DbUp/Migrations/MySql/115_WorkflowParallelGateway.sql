-- 115：为并行网关分叉与汇合引入汇合状态表、分支到达事实表，并为步骤补充并行上下文列。
CREATE TABLE IF NOT EXISTS fn_workflow_parallel_join (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    ForkNodeKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '分叉节点键',
    JoinNodeKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '汇合节点键',
    RequiredBranchCount int NOT NULL COMMENT '需要到达汇合的分支总数',
    ArrivedBranchCount int NOT NULL COMMENT '已到达汇合的分支数',
    StatusKey varchar(16) NOT NULL COMMENT '汇合状态键',
    Revision bigint NOT NULL COMMENT '乐观并发修订号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '汇合完成时间(UTC)',
    CONSTRAINT PK_fn_workflow_parallel_join PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_parallel_join_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT CK_fn_workflow_parallel_join_RequiredBranchCount CHECK (RequiredBranchCount >= 2 AND RequiredBranchCount <= 8),
    CONSTRAINT CK_fn_workflow_parallel_join_ArrivedBranchCount CHECK (ArrivedBranchCount >= 0 AND ArrivedBranchCount <= RequiredBranchCount),
    CONSTRAINT CK_fn_workflow_parallel_join_Revision CHECK (Revision > 0),
    CONSTRAINT CK_fn_workflow_parallel_join_Status CHECK (StatusKey IN ('waiting', 'completed', 'cancelled'))
) COMMENT='工作流并行汇合状态表' ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_workflow_parallel_branch_arrival (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ParallelJoinId BINARY(16) NOT NULL COMMENT '所属汇合状态标识',
    BranchKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '稳定分支键',
    ArrivedAtUtc datetime(6) NOT NULL COMMENT '到达汇合时间(UTC)',
    CONSTRAINT PK_fn_workflow_parallel_branch_arrival PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_parallel_branch_arrival_Join FOREIGN KEY (ParallelJoinId) REFERENCES fn_workflow_parallel_join(Id),
    CONSTRAINT UQ_fn_workflow_parallel_branch_arrival_Join_Branch UNIQUE (ParallelJoinId, BranchKey)
) COMMENT='工作流并行分支到达事实表' ENGINE=InnoDB;

SET @hasParallelJoinId := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_step'
      AND COLUMN_NAME = 'ParallelJoinId');
SET @addParallelJoinId := IF(
    @hasParallelJoinId = 0,
    'ALTER TABLE fn_workflow_step ADD COLUMN ParallelJoinId BINARY(16) NULL COMMENT ''并行汇合状态标识；非并行步骤为空''',
    'SELECT 1');
PREPARE stmt FROM @addParallelJoinId;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasParallelBranchKey := (
    SELECT COUNT(1)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_step'
      AND COLUMN_NAME = 'ParallelBranchKey');
SET @addParallelBranchKey := IF(
    @hasParallelBranchKey = 0,
    'ALTER TABLE fn_workflow_step ADD COLUMN ParallelBranchKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL COMMENT ''并行分支键；非并行步骤为空''',
    'SELECT 1');
PREPARE stmt FROM @addParallelBranchKey;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasJoinIndex := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_parallel_join'
      AND INDEX_NAME = 'IX_fn_workflow_parallel_join_Instance_Status');
SET @addJoinIndex := IF(
    @hasJoinIndex = 0,
    'CREATE INDEX IX_fn_workflow_parallel_join_Instance_Status ON fn_workflow_parallel_join (InstanceId, StatusKey)',
    'SELECT 1');
PREPARE stmt FROM @addJoinIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasStepParallelIndex := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_step'
      AND INDEX_NAME = 'IX_fn_workflow_step_ParallelJoin');
SET @addStepParallelIndex := IF(
    @hasStepParallelIndex = 0,
    'CREATE INDEX IX_fn_workflow_step_ParallelJoin ON fn_workflow_step (ParallelJoinId)',
    'SELECT 1');
PREPARE stmt FROM @addStepParallelIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
