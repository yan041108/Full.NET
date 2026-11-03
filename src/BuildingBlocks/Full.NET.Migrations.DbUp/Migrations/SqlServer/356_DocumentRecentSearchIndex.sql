-- 固定近期窗口索引；新建与软删除文档增加一棵索引的维护成本，标题更新需同步包含列。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_document_item') AND name=N'IX_fn_document_item_Recent_Search')
    CREATE INDEX IX_fn_document_item_Recent_Search ON dbo.fn_document_item(TenantId,IsDeleted,CreatedAtUtc DESC,Id DESC)
        INCLUDE (Title,DocumentNo,CreatedByUserId,Status,VisibilityMode,IsPurging);
