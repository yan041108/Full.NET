-- 301：独立投票表单、个人票据与最终数量事实；不把选票写为审批同意票。
IF OBJECT_ID(N'dbo.fn_workflow_vote_form',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_vote_form (
        Id uniqueidentifier NOT NULL,
        DefinitionVersionId uniqueidentifier NOT NULL,
        NodeKey nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        BallotFormVersionId uniqueidentifier NOT NULL,
        FormSchemaHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        CONSTRAINT pk_fn_workflow_vote_form PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_vote_form_definition_version FOREIGN KEY (DefinitionVersionId) REFERENCES dbo.fn_workflow_definition_version(Id) ON DELETE CASCADE,
        CONSTRAINT fk_fn_workflow_vote_form_ballot_form_version FOREIGN KEY (BallotFormVersionId) REFERENCES dbo.fn_workflow_form_version(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流发布版本固定的独立投票表单绑定', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_form';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_form'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'表单绑定标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_form', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_form'),N'DefinitionVersionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属不可变流程版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_form', @level2type=N'COLUMN', @level2name=N'DefinitionVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_form'),N'NodeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定投票节点键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_form', @level2type=N'COLUMN', @level2name=N'NodeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_form'),N'BallotFormVersionId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'独立投票表单版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_form', @level2type=N'COLUMN', @level2name=N'BallotFormVersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_form'),N'FormSchemaHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定表单架构摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_form', @level2type=N'COLUMN', @level2name=N'FormSchemaHash';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND name=N'uq_fn_workflow_vote_form_node')
    CREATE UNIQUE INDEX uq_fn_workflow_vote_form_node ON dbo.fn_workflow_vote_form(DefinitionVersionId, NodeKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_form') AND name=N'ix_fn_workflow_vote_form_version')
    CREATE INDEX ix_fn_workflow_vote_form_version ON dbo.fn_workflow_vote_form(BallotFormVersionId);
GO
IF OBJECT_ID(N'dbo.fn_workflow_vote',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_vote (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        FormBindingId uniqueidentifier NOT NULL,
        MemberCount int NOT NULL,
        ParticipationPercentage int NOT NULL,
        ApprovalPercentage int NOT NULL,
        AllowAbstain bit NOT NULL,
        ClosesAtUtc datetimeoffset(7) NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Revision bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CompletedAtUtc datetimeoffset(7) NULL,
        FinalYesCount int NULL,
        FinalNoCount int NULL,
        FinalAbstainCount int NULL,
        FinalUnsubmittedCount int NULL,
        CONSTRAINT pk_fn_workflow_vote PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_vote_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_step(Id),
        CONSTRAINT fk_fn_workflow_vote_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_vote_form_binding FOREIGN KEY (FormBindingId) REFERENCES dbo.fn_workflow_vote_form(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流投票活动及固定分母与最终结果事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属投票步骤标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'FormBindingId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已发布独立表单绑定标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'FormBindingId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'MemberCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际固定候选人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'MemberCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'ParticipationPercentage','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定参与率百分比', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'ParticipationPercentage';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'ApprovalPercentage','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定有效票通过率百分比', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'ApprovalPercentage';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'AllowAbstain','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否允许弃权', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'AllowAbstain';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'ClosesAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信绝对截止时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'ClosesAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'活动及结果状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观锁修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'激活时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'CompletedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果或取消时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'CompletedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'FinalYesCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最终赞成人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'FinalYesCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'FinalNoCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最终反对人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'FinalNoCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'FinalAbstainCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最终弃权人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'FinalAbstainCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote'),N'FinalUnsubmittedCount','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最终未提交人数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote', @level2type=N'COLUMN', @level2name=N'FinalUnsubmittedCount';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND name=N'ix_fn_workflow_vote_instance_status')
    CREATE INDEX ix_fn_workflow_vote_instance_status ON dbo.fn_workflow_vote(InstanceId, StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND name=N'ix_fn_workflow_vote_deadline')
    CREATE INDEX ix_fn_workflow_vote_deadline ON dbo.fn_workflow_vote(StatusKey, ClosesAtUtc, InstanceId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND name=N'ck_fn_workflow_vote_policy')
    ALTER TABLE dbo.fn_workflow_vote ADD CONSTRAINT ck_fn_workflow_vote_policy CHECK (MemberCount BETWEEN 1 AND 20 AND ParticipationPercentage BETWEEN 1 AND 100 AND ApprovalPercentage BETWEEN 1 AND 100 AND Revision>0 AND AllowAbstain IN (0,1));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_vote') AND name=N'ck_fn_workflow_vote_state')
    ALTER TABLE dbo.fn_workflow_vote ADD CONSTRAINT ck_fn_workflow_vote_state CHECK ((StatusKey='active' AND CompletedAtUtc IS NULL AND FinalYesCount IS NULL AND FinalNoCount IS NULL AND FinalAbstainCount IS NULL AND FinalUnsubmittedCount IS NULL) OR (StatusKey IN ('approved','rejected','suspended','cancelled') AND CompletedAtUtc IS NOT NULL AND FinalYesCount IS NOT NULL AND FinalNoCount IS NOT NULL AND FinalAbstainCount IS NOT NULL AND FinalUnsubmittedCount IS NOT NULL AND FinalYesCount>=0 AND FinalNoCount>=0 AND FinalAbstainCount>=0 AND FinalUnsubmittedCount>=0 AND FinalYesCount+FinalNoCount+FinalAbstainCount+FinalUnsubmittedCount=MemberCount));
GO
IF OBJECT_ID(N'dbo.fn_workflow_vote_ballot',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_vote_ballot (
        Id uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        VoteId uniqueidentifier NOT NULL,
        TodoId uniqueidentifier NOT NULL,
        OriginalAssigneeUserId uniqueidentifier NOT NULL,
        AssigneeUserId uniqueidentifier NOT NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ChoiceKey varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
        InputJson nvarchar(max) NOT NULL,
        SubmissionJson nvarchar(max) NOT NULL,
        Revision bigint NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        SubmittedAtUtc datetimeoffset(7) NULL,
        CancelledAtUtc datetimeoffset(7) NULL,
        CONSTRAINT pk_fn_workflow_vote_ballot PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_vote_ballot_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_vote_ballot_vote FOREIGN KEY (VoteId) REFERENCES dbo.fn_workflow_vote(Id),
        CONSTRAINT fk_fn_workflow_vote_ballot_todo FOREIGN KEY (TodoId) REFERENCES dbo.fn_workflow_todo(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流候选个人投票与独立不可变提交事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人票据标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'VoteId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属投票活动标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'VoteId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'TodoId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前实际个人待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'TodoId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'OriginalAssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'激活时固定的原候选身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'OriginalAssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'AssigneeUserId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前受信投票人身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'AssigneeUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'StatusKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人投票状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'StatusKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'ChoiceKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'赞成反对或弃权', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'ChoiceKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'InputJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人表单固定初始输入', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'SubmissionJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人表单提交内容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'SubmissionJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'Revision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人表单乐观修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'Revision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人票据创建时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'SubmittedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实际提交时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'SubmittedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_ballot'),N'CancelledAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'未提交票据取消时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_ballot', @level2type=N'COLUMN', @level2name=N'CancelledAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND name=N'uq_fn_workflow_vote_ballot_member')
    CREATE UNIQUE INDEX uq_fn_workflow_vote_ballot_member ON dbo.fn_workflow_vote_ballot(VoteId, AssigneeUserId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND name=N'uq_fn_workflow_vote_ballot_todo')
    CREATE UNIQUE INDEX uq_fn_workflow_vote_ballot_todo ON dbo.fn_workflow_vote_ballot(TodoId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND name=N'ix_fn_workflow_vote_ballot_state')
    CREATE INDEX ix_fn_workflow_vote_ballot_state ON dbo.fn_workflow_vote_ballot(InstanceId, VoteId, StatusKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND name=N'ck_fn_workflow_vote_ballot_state')
    ALTER TABLE dbo.fn_workflow_vote_ballot ADD CONSTRAINT ck_fn_workflow_vote_ballot_state CHECK (Revision>0 AND ((StatusKey='pending' AND ChoiceKey IS NULL AND SubmittedAtUtc IS NULL AND CancelledAtUtc IS NULL) OR (StatusKey='submitted' AND ChoiceKey IS NOT NULL AND ChoiceKey IN ('yes','no','abstain') AND SubmittedAtUtc IS NOT NULL AND CancelledAtUtc IS NULL) OR (StatusKey='cancelled' AND ChoiceKey IS NULL AND SubmittedAtUtc IS NULL AND CancelledAtUtc IS NOT NULL)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_vote_ballot') AND name=N'ck_fn_workflow_vote_ballot_json')
    ALTER TABLE dbo.fn_workflow_vote_ballot ADD CONSTRAINT ck_fn_workflow_vote_ballot_json CHECK (ISJSON(InputJson)=1 AND ISJSON(SubmissionJson)=1);
GO
IF OBJECT_ID(N'dbo.fn_workflow_vote_view',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_vote_view (
        Id uniqueidentifier NOT NULL,
        BallotId uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        InputJson nvarchar(max) NOT NULL,
        InputRevision bigint NOT NULL,
        OutputJson nvarchar(max) NULL,
        OutputRevision bigint NULL,
        CONSTRAINT pk_fn_workflow_vote_view PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_vote_view_step FOREIGN KEY (Id) REFERENCES dbo.fn_workflow_todo(Id),
        CONSTRAINT fk_fn_workflow_vote_view_ballot FOREIGN KEY (BallotId) REFERENCES dbo.fn_workflow_vote_ballot(Id),
        CONSTRAINT fk_fn_workflow_vote_view_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流每次投票待办保留的个人表单输入输出', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_view'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人待办标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_view'),N'BallotId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属固定个人票据', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view', @level2type=N'COLUMN', @level2name=N'BallotId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_view'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_view'),N'InputJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'该次投票待办的个人初始输入', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view', @level2type=N'COLUMN', @level2name=N'InputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_view'),N'InputRevision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'个人输入修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view', @level2type=N'COLUMN', @level2name=N'InputRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_view'),N'OutputJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本人最终提交内容', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view', @level2type=N'COLUMN', @level2name=N'OutputJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_view'),N'OutputRevision','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本人最终提交修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_view', @level2type=N'COLUMN', @level2name=N'OutputRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND name=N'ix_fn_workflow_vote_view_ballot')
    CREATE INDEX ix_fn_workflow_vote_view_ballot ON dbo.fn_workflow_vote_view(InstanceId, BallotId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND name=N'ck_fn_workflow_vote_view_state')
    ALTER TABLE dbo.fn_workflow_vote_view ADD CONSTRAINT ck_fn_workflow_vote_view_state CHECK (InputRevision>0 AND ((OutputJson IS NULL AND OutputRevision IS NULL) OR (OutputJson IS NOT NULL AND OutputRevision IS NOT NULL AND OutputRevision>InputRevision)));
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_vote_view') AND name=N'ck_fn_workflow_vote_view_json')
    ALTER TABLE dbo.fn_workflow_vote_view ADD CONSTRAINT ck_fn_workflow_vote_view_json CHECK (ISJSON(InputJson)=1 AND (OutputJson IS NULL OR ISJSON(OutputJson)=1));
GO
IF OBJECT_ID(N'dbo.fn_workflow_vote_attachment',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_workflow_vote_attachment (
        Id uniqueidentifier NOT NULL,
        BallotId uniqueidentifier NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        TenantScopeKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FieldKey nvarchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        FileId uniqueidentifier NOT NULL,
        OwnerResourceId uniqueidentifier NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT pk_fn_workflow_vote_attachment PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT fk_fn_workflow_vote_attachment_ballot FOREIGN KEY (BallotId) REFERENCES dbo.fn_workflow_vote_ballot(Id),
        CONSTRAINT fk_fn_workflow_vote_attachment_instance FOREIGN KEY (InstanceId) REFERENCES dbo.fn_workflow_instance(Id)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=0 AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工作流个人票据保留的字段附件引用事实', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'Id','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'附件引用及Files认领身份', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'BallotId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属个人票据标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'BallotId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'InstanceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所属实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'InstanceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'TenantScopeKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'可信租户作用域', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'TenantScopeKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'FieldKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'独立表单字段键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'FieldKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'FileId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Files不透明文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'FileId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'OwnerResourceId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户文件资源所有者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'OwnerResourceId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_vote_attachment'),N'CreatedAtUtc','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'引用写入时间UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_vote_attachment', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND name=N'uq_fn_workflow_vote_attachment_field')
    CREATE UNIQUE INDEX uq_fn_workflow_vote_attachment_field ON dbo.fn_workflow_vote_attachment(BallotId, FieldKey, FileId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_vote_attachment') AND name=N'ix_fn_workflow_vote_attachment_file')
    CREATE INDEX ix_fn_workflow_vote_attachment_file ON dbo.fn_workflow_vote_attachment(TenantScopeKey, FileId, InstanceId);
GO
IF COL_LENGTH(N'dbo.fn_workflow_todo_transfer',N'VoteBallotId') IS NULL
    ALTER TABLE dbo.fn_workflow_todo_transfer ADD VoteBallotId uniqueidentifier NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_workflow_todo_transfer'),N'VoteBallotId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'独立投票票据交接身份；非投票为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_workflow_todo_transfer', @level2type=N'COLUMN', @level2name=N'VoteBallotId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'fk_fn_workflow_todo_transfer_ballot')
    ALTER TABLE dbo.fn_workflow_todo_transfer ADD CONSTRAINT fk_fn_workflow_todo_transfer_ballot FOREIGN KEY (VoteBallotId) REFERENCES dbo.fn_workflow_vote_ballot(Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ix_fn_workflow_todo_transfer_ballot')
    CREATE INDEX ix_fn_workflow_todo_transfer_ballot ON dbo.fn_workflow_todo_transfer(VoteBallotId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_human_member')
    ALTER TABLE dbo.fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_human_member CHECK (CASE WHEN SlotId IS NULL THEN 0 ELSE 1 END + CASE WHEN TaskMemberId IS NULL THEN 0 ELSE 1 END + CASE WHEN VoteBallotId IS NULL THEN 0 ELSE 1 END <= 1);
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_workflow_todo_transfer') AND name=N'ck_fn_workflow_todo_transfer_member_kind')
    ALTER TABLE dbo.fn_workflow_todo_transfer DROP CONSTRAINT ck_fn_workflow_todo_transfer_member_kind;
GO
