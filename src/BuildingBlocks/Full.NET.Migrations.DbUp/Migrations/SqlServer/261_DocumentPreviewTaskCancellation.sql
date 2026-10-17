-- 只展开取消终态；保留原任务、源文件和输出引用，缺约束及已展开未记账阶段均可重入。
-- 回退须停用取消入口并保留已取消行，不收窄历史状态约束。
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_document_preview_task') AND name = N'CK_fn_document_preview_task_StatusKey')
    ALTER TABLE dbo.fn_document_preview_task DROP CONSTRAINT CK_fn_document_preview_task_StatusKey;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.fn_document_preview_task') AND name = N'CK_fn_document_preview_task_StatusKey')
    ALTER TABLE dbo.fn_document_preview_task ADD CONSTRAINT CK_fn_document_preview_task_StatusKey
        CHECK (StatusKey IN (N'pending', N'processing', N'succeeded', N'failed', N'cancelled'));
