-- 116：为包容网关复用汇合状态表，补充网关类型并放宽最少激活分支数。
IF COL_LENGTH(N'dbo.fn_workflow_parallel_join', N'GatewayTypeKey') IS NULL
BEGIN
    ALTER TABLE dbo.fn_workflow_parallel_join
        ADD GatewayTypeKey varchar(16) NOT NULL
            CONSTRAINT DF_fn_workflow_parallel_join_GatewayTypeKey DEFAULT ('parallel');
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'网关类型键：parallel 或 inclusive',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_parallel_join', @level2type=N'COLUMN', @level2name=N'GatewayTypeKey';
END;

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_workflow_parallel_join_RequiredBranchCount'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join'))
BEGIN
    ALTER TABLE dbo.fn_workflow_parallel_join
        DROP CONSTRAINT CK_fn_workflow_parallel_join_RequiredBranchCount;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_workflow_parallel_join_RequiredBranchCount'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join'))
BEGIN
    ALTER TABLE dbo.fn_workflow_parallel_join
        ADD CONSTRAINT CK_fn_workflow_parallel_join_RequiredBranchCount
            CHECK (RequiredBranchCount >= 1 AND RequiredBranchCount <= 8);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_workflow_parallel_join_GatewayTypeKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_workflow_parallel_join'))
BEGIN
    ALTER TABLE dbo.fn_workflow_parallel_join
        ADD CONSTRAINT CK_fn_workflow_parallel_join_GatewayTypeKey
            CHECK (GatewayTypeKey IN ('parallel', 'inclusive'));
END;
