-- 295：新激活记录选人依据，旧步骤没有可证明的历史组织快照，保留空值。
IF COL_LENGTH(N'dbo.fn_workflow_step', N'AssigneeSnapshotJson') IS NULL
    ALTER TABLE dbo.fn_workflow_step ADD AssigneeSnapshotJson nvarchar(max) NULL;
IF COL_LENGTH(N'dbo.fn_workflow_step', N'AssigneeSnapshotHash') IS NULL
    ALTER TABLE dbo.fn_workflow_step ADD AssigneeSnapshotHash char(64) COLLATE Latin1_General_100_BIN2 NULL;
-- SQL Server 先编译整个批次；约束必须在新增列实际生效后另行编译。
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'CK_fn_workflow_step_AssigneeSnapshot')
    ALTER TABLE dbo.fn_workflow_step ADD CONSTRAINT CK_fn_workflow_step_AssigneeSnapshot CHECK (
        (AssigneeSnapshotJson IS NULL AND AssigneeSnapshotHash IS NULL) OR
        (AssigneeSnapshotJson IS NOT NULL AND AssigneeSnapshotHash IS NOT NULL AND ISJSON(AssigneeSnapshotJson)=1));
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'),N'AssigneeSnapshotJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本次激活的来源配置、实际成员及解析依据', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'AssigneeSnapshotJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_step') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_step'),N'AssigneeSnapshotHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'冻结选人依据 JSON 的 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_step', @level2type=N'COLUMN', @level2name=N'AssigneeSnapshotHash';
