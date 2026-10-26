-- 301：独立投票表单、个人票据与最终数量事实；不把选票写为审批同意票。
CREATE TABLE IF NOT EXISTS fn_workflow_vote_form (
        Id BINARY(16) NOT NULL COMMENT '表单绑定标识',
        DefinitionVersionId BINARY(16) NOT NULL COMMENT '所属不可变流程版本',
        NodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '固定投票节点键',
        BallotFormVersionId BINARY(16) NOT NULL COMMENT '独立投票表单版本',
        FormSchemaHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定表单架构摘要',
        CONSTRAINT pk_fn_workflow_vote_form PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_vote_form_definition_version FOREIGN KEY (DefinitionVersionId) REFERENCES fn_workflow_definition_version(Id) ON DELETE CASCADE,
        CONSTRAINT fk_fn_workflow_vote_form_ballot_form_version FOREIGN KEY (BallotFormVersionId) REFERENCES fn_workflow_form_version(Id)
) COMMENT='工作流发布版本固定的独立投票表单绑定' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_vote (
        Id BINARY(16) NOT NULL COMMENT '所属投票步骤标识',
        InstanceId BINARY(16) NOT NULL COMMENT '所属实例标识',
        FormBindingId BINARY(16) NOT NULL COMMENT '已发布独立表单绑定标识',
        MemberCount int NOT NULL COMMENT '实际固定候选人数',
        ParticipationPercentage int NOT NULL COMMENT '固定参与率百分比',
        ApprovalPercentage int NOT NULL COMMENT '固定有效票通过率百分比',
        AllowAbstain tinyint(1) NOT NULL COMMENT '是否允许弃权',
        ClosesAtUtc datetime(6) NULL COMMENT '可信绝对截止时间UTC',
        StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '活动及结果状态',
        Revision bigint NOT NULL COMMENT '乐观锁修订',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '激活时间UTC',
        CompletedAtUtc datetime(6) NULL COMMENT '结果或取消时间UTC',
        FinalYesCount int NULL COMMENT '最终赞成人数',
        FinalNoCount int NULL COMMENT '最终反对人数',
        FinalAbstainCount int NULL COMMENT '最终弃权人数',
        FinalUnsubmittedCount int NULL COMMENT '最终未提交人数',
        CONSTRAINT pk_fn_workflow_vote PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_vote_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id),
        CONSTRAINT fk_fn_workflow_vote_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_vote_form_binding FOREIGN KEY (FormBindingId) REFERENCES fn_workflow_vote_form(Id)
) COMMENT='工作流投票活动及固定分母与最终结果事实' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_vote_ballot (
        Id BINARY(16) NOT NULL COMMENT '个人票据标识',
        InstanceId BINARY(16) NOT NULL COMMENT '所属实例标识',
        VoteId BINARY(16) NOT NULL COMMENT '所属投票活动标识',
        TodoId BINARY(16) NOT NULL COMMENT '当前实际个人待办标识',
        OriginalAssigneeUserId BINARY(16) NOT NULL COMMENT '激活时固定的原候选身份',
        AssigneeUserId BINARY(16) NOT NULL COMMENT '当前受信投票人身份',
        StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '个人投票状态',
        ChoiceKey varchar(16) COLLATE utf8mb4_bin NULL COMMENT '赞成反对或弃权',
        InputJson longtext COLLATE utf8mb4_bin NOT NULL COMMENT '个人表单固定初始输入',
        SubmissionJson longtext COLLATE utf8mb4_bin NOT NULL COMMENT '个人表单提交内容',
        Revision bigint NOT NULL COMMENT '个人表单乐观修订',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '个人票据创建时间UTC',
        SubmittedAtUtc datetime(6) NULL COMMENT '实际提交时间UTC',
        CancelledAtUtc datetime(6) NULL COMMENT '未提交票据取消时间UTC',
        CONSTRAINT pk_fn_workflow_vote_ballot PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_vote_ballot_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_vote_ballot_vote FOREIGN KEY (VoteId) REFERENCES fn_workflow_vote(Id),
        CONSTRAINT fk_fn_workflow_vote_ballot_todo FOREIGN KEY (TodoId) REFERENCES fn_workflow_todo(Id)
) COMMENT='工作流候选个人投票与独立不可变提交事实' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_vote_view (
        Id BINARY(16) NOT NULL COMMENT '个人待办标识',
        BallotId BINARY(16) NOT NULL COMMENT '所属固定个人票据',
        InstanceId BINARY(16) NOT NULL COMMENT '所属实例标识',
        InputJson longtext COLLATE utf8mb4_bin NOT NULL COMMENT '该次投票待办的个人初始输入',
        InputRevision bigint NOT NULL COMMENT '个人输入修订',
        OutputJson longtext COLLATE utf8mb4_bin NULL COMMENT '本人最终提交内容',
        OutputRevision bigint NULL COMMENT '本人最终提交修订',
        CONSTRAINT pk_fn_workflow_vote_view PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_vote_view_step FOREIGN KEY (Id) REFERENCES fn_workflow_todo(Id),
        CONSTRAINT fk_fn_workflow_vote_view_ballot FOREIGN KEY (BallotId) REFERENCES fn_workflow_vote_ballot(Id),
        CONSTRAINT fk_fn_workflow_vote_view_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id)
) COMMENT='工作流每次投票待办保留的个人表单输入输出' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_vote_attachment (
        Id BINARY(16) NOT NULL COMMENT '附件引用及Files认领身份',
        BallotId BINARY(16) NOT NULL COMMENT '所属个人票据标识',
        InstanceId BINARY(16) NOT NULL COMMENT '所属实例标识',
        TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户作用域',
        FieldKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '独立表单字段键',
        FileId BINARY(16) NOT NULL COMMENT 'Files不透明文件标识',
        OwnerResourceId BINARY(16) NULL COMMENT '租户文件资源所有者',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '引用写入时间UTC',
        CONSTRAINT pk_fn_workflow_vote_attachment PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_vote_attachment_ballot FOREIGN KEY (BallotId) REFERENCES fn_workflow_vote_ballot(Id),
        CONSTRAINT fk_fn_workflow_vote_attachment_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id)
) COMMENT='工作流个人票据保留的字段附件引用事实' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_ensure_vote_facts;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_vote_facts()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_form' AND INDEX_NAME='uq_fn_workflow_vote_form_node') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_vote_form_node ON fn_workflow_vote_form(DefinitionVersionId, NodeKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_form' AND INDEX_NAME='ix_fn_workflow_vote_form_version') THEN
        CREATE INDEX ix_fn_workflow_vote_form_version ON fn_workflow_vote_form(BallotFormVersionId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote' AND INDEX_NAME='ix_fn_workflow_vote_instance_status') THEN
        CREATE INDEX ix_fn_workflow_vote_instance_status ON fn_workflow_vote(InstanceId, StatusKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote' AND INDEX_NAME='ix_fn_workflow_vote_deadline') THEN
        CREATE INDEX ix_fn_workflow_vote_deadline ON fn_workflow_vote(StatusKey, ClosesAtUtc, InstanceId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote' AND CONSTRAINT_NAME='ck_fn_workflow_vote_policy') THEN
        ALTER TABLE fn_workflow_vote ADD CONSTRAINT ck_fn_workflow_vote_policy CHECK (MemberCount BETWEEN 1 AND 20 AND ParticipationPercentage BETWEEN 1 AND 100 AND ApprovalPercentage BETWEEN 1 AND 100 AND Revision>0 AND AllowAbstain IN (0,1));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote' AND CONSTRAINT_NAME='ck_fn_workflow_vote_state') THEN
        ALTER TABLE fn_workflow_vote ADD CONSTRAINT ck_fn_workflow_vote_state CHECK ((StatusKey='active' AND CompletedAtUtc IS NULL AND FinalYesCount IS NULL AND FinalNoCount IS NULL AND FinalAbstainCount IS NULL AND FinalUnsubmittedCount IS NULL) OR (StatusKey IN ('approved','rejected','suspended','cancelled') AND CompletedAtUtc IS NOT NULL AND FinalYesCount IS NOT NULL AND FinalNoCount IS NOT NULL AND FinalAbstainCount IS NOT NULL AND FinalUnsubmittedCount IS NOT NULL AND FinalYesCount>=0 AND FinalNoCount>=0 AND FinalAbstainCount>=0 AND FinalUnsubmittedCount>=0 AND FinalYesCount+FinalNoCount+FinalAbstainCount+FinalUnsubmittedCount=MemberCount));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_ballot' AND INDEX_NAME='uq_fn_workflow_vote_ballot_member') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_vote_ballot_member ON fn_workflow_vote_ballot(VoteId, AssigneeUserId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_ballot' AND INDEX_NAME='uq_fn_workflow_vote_ballot_todo') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_vote_ballot_todo ON fn_workflow_vote_ballot(TodoId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_ballot' AND INDEX_NAME='ix_fn_workflow_vote_ballot_state') THEN
        CREATE INDEX ix_fn_workflow_vote_ballot_state ON fn_workflow_vote_ballot(InstanceId, VoteId, StatusKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_ballot' AND CONSTRAINT_NAME='ck_fn_workflow_vote_ballot_state') THEN
        ALTER TABLE fn_workflow_vote_ballot ADD CONSTRAINT ck_fn_workflow_vote_ballot_state CHECK (Revision>0 AND ((StatusKey='pending' AND ChoiceKey IS NULL AND SubmittedAtUtc IS NULL AND CancelledAtUtc IS NULL) OR (StatusKey='submitted' AND ChoiceKey IS NOT NULL AND ChoiceKey IN ('yes','no','abstain') AND SubmittedAtUtc IS NOT NULL AND CancelledAtUtc IS NULL) OR (StatusKey='cancelled' AND ChoiceKey IS NULL AND SubmittedAtUtc IS NULL AND CancelledAtUtc IS NOT NULL)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_ballot' AND CONSTRAINT_NAME='ck_fn_workflow_vote_ballot_json') THEN
        ALTER TABLE fn_workflow_vote_ballot ADD CONSTRAINT ck_fn_workflow_vote_ballot_json CHECK (JSON_VALID(InputJson) AND JSON_VALID(SubmissionJson));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_view' AND INDEX_NAME='ix_fn_workflow_vote_view_ballot') THEN
        CREATE INDEX ix_fn_workflow_vote_view_ballot ON fn_workflow_vote_view(InstanceId, BallotId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_view' AND CONSTRAINT_NAME='ck_fn_workflow_vote_view_state') THEN
        ALTER TABLE fn_workflow_vote_view ADD CONSTRAINT ck_fn_workflow_vote_view_state CHECK (InputRevision>0 AND ((OutputJson IS NULL AND OutputRevision IS NULL) OR (OutputJson IS NOT NULL AND OutputRevision IS NOT NULL AND OutputRevision>InputRevision)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_view' AND CONSTRAINT_NAME='ck_fn_workflow_vote_view_json') THEN
        ALTER TABLE fn_workflow_vote_view ADD CONSTRAINT ck_fn_workflow_vote_view_json CHECK (JSON_VALID(InputJson) AND (OutputJson IS NULL OR JSON_VALID(OutputJson)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_attachment' AND INDEX_NAME='uq_fn_workflow_vote_attachment_field') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_vote_attachment_field ON fn_workflow_vote_attachment(BallotId, FieldKey, FileId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_vote_attachment' AND INDEX_NAME='ix_fn_workflow_vote_attachment_file') THEN
        CREATE INDEX ix_fn_workflow_vote_attachment_file ON fn_workflow_vote_attachment(TenantScopeKey, FileId, InstanceId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND COLUMN_NAME='VoteBallotId') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD COLUMN VoteBallotId BINARY(16) NULL COMMENT '独立投票票据交接身份；非投票为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='fk_fn_workflow_todo_transfer_ballot') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT fk_fn_workflow_todo_transfer_ballot FOREIGN KEY (VoteBallotId) REFERENCES fn_workflow_vote_ballot(Id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND INDEX_NAME='ix_fn_workflow_todo_transfer_ballot') THEN
        CREATE INDEX ix_fn_workflow_todo_transfer_ballot ON fn_workflow_todo_transfer(VoteBallotId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_human_member') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_human_member CHECK (CASE WHEN SlotId IS NULL THEN 0 ELSE 1 END + CASE WHEN TaskMemberId IS NULL THEN 0 ELSE 1 END + CASE WHEN VoteBallotId IS NULL THEN 0 ELSE 1 END <= 1);
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_member_kind') THEN
        ALTER TABLE fn_workflow_todo_transfer DROP CHECK ck_fn_workflow_todo_transfer_member_kind;
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_vote_facts();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_vote_facts;
