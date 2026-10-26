-- 300：办理提交与审批票独立持久化；依次办理保留未来成员而不伪造待办。
CREATE TABLE IF NOT EXISTS fn_workflow_task (
        Id BINARY(16) NOT NULL COMMENT '办理步骤标识',
        InstanceId BINARY(16) NOT NULL COMMENT '所属实例标识',
        CompletionModeKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '固定办理完成规则',
        MemberCount int NOT NULL COMMENT '实际固定办理人数',
        RequiredCompletionCount int NOT NULL COMMENT '所需提交完成人数',
        StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '办理活动状态',
        Revision bigint NOT NULL COMMENT '乐观锁修订',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '激活时间UTC',
        CompletedAtUtc datetime(6) NULL COMMENT '收敛时间UTC',
        CONSTRAINT pk_fn_workflow_task PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_task_instance FOREIGN KEY(InstanceId) REFERENCES fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_task_step FOREIGN KEY(Id) REFERENCES fn_workflow_step(Id)
) COMMENT='工作流办理活动事实' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_task_member (
        Id BINARY(16) NOT NULL COMMENT '办理成员标识',
        InstanceId BINARY(16) NOT NULL COMMENT '所属实例标识',
        TaskId BINARY(16) NOT NULL COMMENT '所属办理活动标识',
        TodoId BINARY(16) NULL COMMENT '实际激活待办标识',
        AssigneeUserId BINARY(16) NOT NULL COMMENT '固定办理人标识',
        SequenceNo int NOT NULL COMMENT '固定办理顺序',
        StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT '办理成员状态',
        Revision bigint NOT NULL COMMENT '成员乐观锁修订',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '成员冻结时间UTC',
        CompletedAtUtc datetime(6) NULL COMMENT '完成或取消时间UTC',
        CONSTRAINT pk_fn_workflow_task_member PRIMARY KEY (Id),
        CONSTRAINT fk_fn_workflow_task_member_instance FOREIGN KEY(InstanceId) REFERENCES fn_workflow_instance(Id),
        CONSTRAINT fk_fn_workflow_task_member_task FOREIGN KEY(TaskId) REFERENCES fn_workflow_task(Id),
        CONSTRAINT fk_fn_workflow_task_member_todo FOREIGN KEY(TodoId) REFERENCES fn_workflow_todo(Id)
) COMMENT='工作流办理成员及不可变待办绑定' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_ensure_human_tasks;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_human_tasks()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task' AND INDEX_NAME='ix_fn_workflow_task_instance_status') THEN
        CREATE INDEX ix_fn_workflow_task_instance_status ON fn_workflow_task(InstanceId, StatusKey);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task_member' AND INDEX_NAME='uq_fn_workflow_task_member_assignee') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_task_member_assignee ON fn_workflow_task_member(TaskId, AssigneeUserId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task_member' AND INDEX_NAME='uq_fn_workflow_task_member_sequence') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_task_member_sequence ON fn_workflow_task_member(TaskId, SequenceNo);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task_member' AND INDEX_NAME='uq_fn_workflow_task_member_todo') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_task_member_todo ON fn_workflow_task_member(TodoId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task_member' AND INDEX_NAME='ix_fn_workflow_task_member_status') THEN
        CREATE INDEX ix_fn_workflow_task_member_status ON fn_workflow_task_member(TaskId, StatusKey, SequenceNo);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task' AND CONSTRAINT_NAME='ck_fn_workflow_task_state') THEN
        ALTER TABLE fn_workflow_task ADD CONSTRAINT ck_fn_workflow_task_state CHECK (MemberCount BETWEEN 1 AND 20 AND RequiredCompletionCount BETWEEN 1 AND MemberCount AND Revision>0 AND CompletionModeKey IN ('all','any','sequential') AND ((CompletionModeKey='any' AND RequiredCompletionCount=1) OR (CompletionModeKey IN ('all','sequential') AND RequiredCompletionCount=MemberCount)) AND ((StatusKey='active' AND CompletedAtUtc IS NULL) OR (StatusKey IN ('completed','cancelled') AND CompletedAtUtc IS NOT NULL)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_task_member' AND CONSTRAINT_NAME='ck_fn_workflow_task_member_state') THEN
        ALTER TABLE fn_workflow_task_member ADD CONSTRAINT ck_fn_workflow_task_member_state CHECK (SequenceNo BETWEEN 1 AND 20 AND Revision>0 AND ((StatusKey='pending' AND CompletedAtUtc IS NULL) OR (StatusKey='completed' AND TodoId IS NOT NULL AND CompletedAtUtc IS NOT NULL) OR (StatusKey='cancelled' AND CompletedAtUtc IS NOT NULL)));
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND COLUMN_NAME='TaskMemberId') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD COLUMN TaskMemberId BINARY(16) NULL COMMENT '独立办理成员交接身份；审批转办为空';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND INDEX_NAME='ix_fn_workflow_todo_transfer_task_member') THEN
        CREATE INDEX ix_fn_workflow_todo_transfer_task_member ON fn_workflow_todo_transfer(TaskMemberId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='fk_fn_workflow_todo_transfer_task_member') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT fk_fn_workflow_todo_transfer_task_member FOREIGN KEY(TaskMemberId) REFERENCES fn_workflow_task_member(Id);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_member_kind') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_member_kind CHECK (SlotId IS NULL OR TaskMemberId IS NULL);
    END IF;
    -- 新的显式授权约束先完成验证，再移除旧个人转办约束。
    IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_handoff') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_handoff CHECK (Depth BETWEEN 1 AND 32 AND FormRevision>0 AND FromAssigneeUserId<>ToAssigneeUserId AND (AuthorizationKey='workflow.instances.recover' OR (AuthorizationKey='workflow.todos.transfer' AND ActorUserId=FromAssigneeUserId)));
    END IF;
    IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_identity') THEN
        ALTER TABLE fn_workflow_todo_transfer DROP CHECK ck_fn_workflow_todo_transfer_identity;
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_human_tasks();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_human_tasks;
