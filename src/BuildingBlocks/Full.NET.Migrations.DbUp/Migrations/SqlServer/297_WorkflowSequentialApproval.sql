-- 297：席位先固定，只有实际轮到办理人时才建立待办和输入事实。
IF COL_LENGTH(N'dbo.fn_workflow_approval_slot', N'SequenceNo') IS NULL
    ALTER TABLE dbo.fn_workflow_approval_slot ADD SequenceNo int NULL;
GO
-- 新列先独立生效；同一事务锁住席位，再调整索引和可空性，失败原样回滚。
SET XACT_ABORT ON;
BEGIN TRY
BEGIN TRANSACTION;
DECLARE @LockedSlot uniqueidentifier;
SELECT TOP (1) @LockedSlot=Id FROM dbo.fn_workflow_approval_slot WITH (TABLOCKX, HOLDLOCK);
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'UQ_fn_workflow_approval_slot_Todo')
    ALTER TABLE dbo.fn_workflow_approval_slot DROP CONSTRAINT UQ_fn_workflow_approval_slot_Todo;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'TodoId' AND is_nullable=0)
BEGIN
    -- 早先半完成 DDL 可能已建此索引；ALTER COLUMN 前必须释放其列依赖。
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'UX_fn_workflow_approval_slot_BoundTodo')
        DROP INDEX UX_fn_workflow_approval_slot_BoundTodo ON dbo.fn_workflow_approval_slot;
    ALTER TABLE dbo.fn_workflow_approval_slot ALTER COLUMN TodoId uniqueidentifier NULL;
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'UX_fn_workflow_approval_slot_BoundTodo')
    CREATE UNIQUE INDEX UX_fn_workflow_approval_slot_BoundTodo ON dbo.fn_workflow_approval_slot(TodoId) WHERE TodoId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'UX_fn_workflow_approval_slot_Sequence')
    CREATE UNIQUE INDEX UX_fn_workflow_approval_slot_Sequence ON dbo.fn_workflow_approval_slot(StepId, SequenceNo) WHERE SequenceNo IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND name=N'CK_fn_workflow_approval_slot_Activation')
    ALTER TABLE dbo.fn_workflow_approval_slot ADD CONSTRAINT CK_fn_workflow_approval_slot_Activation CHECK (
        (SequenceNo IS NULL OR SequenceNo > 0) AND
        (TodoId IS NOT NULL OR (SequenceNo IS NOT NULL AND (DecisionKey IS NULL OR DecisionKey='cancelled'))));
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_approval_slot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_approval_slot'),N'SequenceNo','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次激活固定的审批顺序；旧席位为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_approval_slot', @level2type=N'COLUMN', @level2name=N'SequenceNo';
COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
