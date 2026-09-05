-- 112：为工作流步骤补充实例内单调执行序号，退回时不再依赖可能回拨或截断的时间戳判断旧执行链。
IF COL_LENGTH(N'dbo.fn_workflow_step', N'ExecutionSequence') IS NULL
    ALTER TABLE dbo.fn_workflow_step ADD ExecutionSequence bigint NULL;

GO

-- 动作记录的 InstanceRevision 是实例 CAS 后的权威单调事实；每个修订预留一百万个自动节点位置。
UPDATE dbo.fn_workflow_step
SET ExecutionSequence = action.InstanceRevision * 1000000
FROM dbo.fn_workflow_action_record AS action
WHERE action.StepId = dbo.fn_workflow_step.Id
   AND action.ActionKey = 'approve'
  AND dbo.fn_workflow_step.ExecutionSequence IS NULL
  AND dbo.fn_workflow_step.NodeTypeKey = 'human.approval'
  AND dbo.fn_workflow_step.StatusKey = 'completed';

;WITH ranked_automatic_steps AS
(
    SELECT step.Id,
           action.InstanceRevision * 1000000
             + ROW_NUMBER() OVER (PARTITION BY action.Id ORDER BY log.Id) AS SequenceValue
    FROM dbo.fn_workflow_step AS step
    INNER JOIN dbo.fn_workflow_execution_log AS log ON log.StepId = step.Id
    INNER JOIN dbo.fn_workflow_action_record AS action
        ON action.InstanceId = step.InstanceId
       AND action.CreatedAtUtc = log.CreatedAtUtc
       AND action.ActionKey IN ('start', 'approve')
    WHERE step.ExecutionSequence IS NULL
      AND step.NodeTypeKey IN ('notify.cc', 'gateway.exclusive')
)
UPDATE dbo.fn_workflow_step
SET ExecutionSequence = ranked.SequenceValue
FROM ranked_automatic_steps AS ranked
WHERE ranked.Id = dbo.fn_workflow_step.Id;

UPDATE dbo.fn_workflow_step
SET ExecutionSequence = (instance.Revision + 1) * 1000000
FROM dbo.fn_workflow_instance AS instance
WHERE instance.Id = dbo.fn_workflow_step.InstanceId
  AND dbo.fn_workflow_step.ExecutionSequence IS NULL
  AND dbo.fn_workflow_step.NodeTypeKey = 'human.approval'
  AND dbo.fn_workflow_step.StatusKey = 'active'
  AND instance.StatusKey IN ('active', 'suspended');

-- Expand 阶段保持可空，使旧版本 API 在滚动升级期间仍可写入；无法证明顺序的异常存量行由查询失败关闭。

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_step')
      AND name = N'IX_fn_workflow_step_Instance_ExecutionSequence')
    CREATE INDEX IX_fn_workflow_step_Instance_ExecutionSequence
        ON dbo.fn_workflow_step(InstanceId, ExecutionSequence, StatusKey);

IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_workflow_step')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'), N'ExecutionSequence', 'ColumnId')
      AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例内单调执行序号',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step',
        @level2type=N'COLUMN', @level2name=N'ExecutionSequence';
