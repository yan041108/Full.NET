-- 282：重试预算独立于单调递增的租约世代，治理延迟不消耗预算。
IF COL_LENGTH(N'dbo.fn_notifications_delivery', N'AttemptBudgetUsed') IS NULL
BEGIN
    ALTER TABLE dbo.fn_notifications_delivery ADD AttemptBudgetUsed bigint NULL;
END;
GO

-- 建列已提交但注释未完成时也须恢复，不能仅在首次建列分支补齐。
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'AttemptBudgetUsed', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已消耗的自动尝试预算', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_notifications_delivery', @level2type=N'COLUMN', @level2name=N'AttemptBudgetUsed';
GO

-- 分批编译避免 SQL Server 在建列前绑定新列；历史领取无法证明未执行，保守计入预算。
UPDATE dbo.fn_notifications_delivery
SET AttemptBudgetUsed = CASE WHEN LeaseGeneration > 1 THEN LeaseGeneration - 1 ELSE 0 END
WHERE AttemptBudgetUsed IS NULL;
GO

ALTER TABLE dbo.fn_notifications_delivery ALTER COLUMN AttemptBudgetUsed bigint NOT NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_notifications_delivery')
      AND parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_notifications_delivery'), N'AttemptBudgetUsed', 'ColumnId'))
    ALTER TABLE dbo.fn_notifications_delivery
        ADD CONSTRAINT DF_fn_notifications_delivery_AttemptBudgetUsed DEFAULT (0) FOR AttemptBudgetUsed;
