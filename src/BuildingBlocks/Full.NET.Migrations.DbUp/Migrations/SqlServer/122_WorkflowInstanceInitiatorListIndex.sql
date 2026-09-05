-- 122：为“我发起的”实例分页查询补充发起人索引。
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_workflow_instance')
      AND name = N'IX_fn_workflow_instance_Scope_StartedBy')
    CREATE INDEX IX_fn_workflow_instance_Scope_StartedBy
        ON dbo.fn_workflow_instance(TenantScopeKey, StartedById, StartedAtUtc DESC, Id ASC);
