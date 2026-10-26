-- 系统组没有个人席位；先添加包含旧事实的新约束，再移除旧约束，支持记账前中断恢复。
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND name=N'ck_fn_workflow_approval_group_state_306')
    ALTER TABLE dbo.fn_workflow_approval_group WITH CHECK ADD CONSTRAINT ck_fn_workflow_approval_group_state_306 CHECK (
        SequenceNo BETWEEN 1 AND 8 AND Revision>0 AND (
            (ModeKey='system' AND SlotCount=0 AND RequiredApprovalCount=0 AND StatusKey='approved' AND CompletedAtUtc IS NOT NULL)
            OR (ModeKey IN ('single','all','any','nOfM','percent','sequential') AND SlotCount BETWEEN 1 AND 20
                AND RequiredApprovalCount BETWEEN 1 AND SlotCount AND (ModeKey<>'single' OR SlotCount=1)
                AND (ModeKey NOT IN ('all','sequential') OR RequiredApprovalCount=SlotCount) AND (ModeKey<>'any' OR RequiredApprovalCount=1)
                AND ((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('approved','rejected','cancelled') AND CompletedAtUtc IS NOT NULL)))
        ));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_approval_group') AND name=N'ck_fn_workflow_approval_group_state')
    ALTER TABLE dbo.fn_workflow_approval_group DROP CONSTRAINT ck_fn_workflow_approval_group_state;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'ck_fn_workflow_step_approval_groups_306')
    ALTER TABLE dbo.fn_workflow_step WITH CHECK ADD CONSTRAINT ck_fn_workflow_step_approval_groups_306 CHECK (
        (ApprovalGroupCount IS NULL AND (ApprovalModeKey IS NULL OR ApprovalModeKey<>'group')) OR
        (ApprovalGroupCount IS NOT NULL AND ApprovalModeKey IS NOT NULL AND ApprovalModeKey='group'
            AND ApprovalGroupCount BETWEEN 2 AND 8 AND RequiredApprovalCount IS NOT NULL AND RequiredApprovalCount BETWEEN 1 AND ApprovalGroupCount
            AND ApprovalSlotCount IS NOT NULL AND ApprovalSlotCount BETWEEN 0 AND 20));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_step') AND name=N'ck_fn_workflow_step_approval_groups')
    ALTER TABLE dbo.fn_workflow_step DROP CONSTRAINT ck_fn_workflow_step_approval_groups;
GO
