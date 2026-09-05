-- 112：为工作流步骤补充实例内单调执行序号，退回时不再依赖可能回拨或截断的时间戳判断旧执行链。
DROP PROCEDURE IF EXISTS add_workflow_step_execution_sequence;
DELIMITER $$
CREATE PROCEDURE add_workflow_step_execution_sequence()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_workflow_step'
          AND COLUMN_NAME = 'ExecutionSequence') THEN
        ALTER TABLE fn_workflow_step
            ADD ExecutionSequence bigint NULL COMMENT '实例内单调执行序号';
    END IF;

    -- 动作记录的 InstanceRevision 是实例 CAS 后的权威单调事实；每个修订预留一百万个自动节点位置。
    UPDATE fn_workflow_step AS step
    INNER JOIN fn_workflow_action_record AS action
        ON action.StepId = step.Id
       AND action.ActionKey = 'approve'
    SET step.ExecutionSequence = action.InstanceRevision * 1000000
    WHERE step.ExecutionSequence IS NULL
      AND step.NodeTypeKey = 'human.approval'
      AND step.StatusKey = 'completed';

    UPDATE fn_workflow_step AS step
    INNER JOIN (
        SELECT step_source.Id,
               action.InstanceRevision * 1000000
                 + ROW_NUMBER() OVER (PARTITION BY action.Id ORDER BY log.Id) AS SequenceValue
        FROM fn_workflow_step AS step_source
        INNER JOIN fn_workflow_execution_log AS log ON log.StepId = step_source.Id
        INNER JOIN fn_workflow_action_record AS action
            ON action.InstanceId = step_source.InstanceId
           AND action.CreatedAtUtc = log.CreatedAtUtc
           AND action.ActionKey IN ('start', 'approve')
        WHERE step_source.ExecutionSequence IS NULL
          AND step_source.NodeTypeKey IN ('notify.cc', 'gateway.exclusive')
    ) AS ranked ON ranked.Id = step.Id
    SET step.ExecutionSequence = ranked.SequenceValue;

    UPDATE fn_workflow_step AS step
    INNER JOIN fn_workflow_instance AS instance ON instance.Id = step.InstanceId
    SET step.ExecutionSequence = (instance.Revision + 1) * 1000000
    WHERE step.ExecutionSequence IS NULL
      AND step.NodeTypeKey = 'human.approval'
      AND step.StatusKey = 'active'
      AND instance.StatusKey IN ('active', 'suspended');

    -- Expand 阶段保持可空，使旧版本 API 在滚动升级期间仍可写入；无法证明顺序的异常存量行由查询失败关闭。
END$$
DELIMITER ;
CALL add_workflow_step_execution_sequence();
DROP PROCEDURE add_workflow_step_execution_sequence;

SET @hasWorkflowStepSequenceIndex := (
    SELECT COUNT(1) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_step'
      AND INDEX_NAME = 'IX_fn_workflow_step_Instance_ExecutionSequence');
SET @addWorkflowStepSequenceIndex := IF(
    @hasWorkflowStepSequenceIndex = 0,
    'ALTER TABLE fn_workflow_step ADD INDEX IX_fn_workflow_step_Instance_ExecutionSequence (InstanceId, ExecutionSequence, StatusKey)',
    'SELECT 1');
PREPARE stmt FROM @addWorkflowStepSequenceIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
