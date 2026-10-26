-- 298：创建式DDL与独立唯一索引/检查可在未记账的半完成状态重跑。
CREATE TABLE IF NOT EXISTS fn_workflow_todo_transfer (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NOT NULL COMMENT '固定审批步骤标识',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户作用域键',
    SlotId BINARY(16) NULL COMMENT '原审批席位标识',
    OriginTodoId BINARY(16) NOT NULL COMMENT '转办链起始待办标识',
    FromTodoId BINARY(16) NOT NULL COMMENT '关闭的原待办标识',
    ToTodoId BINARY(16) NOT NULL COMMENT '新激活待办标识',
    OriginalAssigneeUserId BINARY(16) NOT NULL COMMENT '链起始办理人快照',
    FromAssigneeUserId BINARY(16) NOT NULL COMMENT '交出待办的办理人',
    ToAssigneeUserId BINARY(16) NOT NULL COMMENT '接收待办的办理人',
    ActorUserId BINARY(16) NOT NULL COMMENT '实际执行转办的用户',
    Depth int NOT NULL COMMENT '有界转办链深度',
    AuthorizationKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '执行时的精确授权依据',
    FormRevision bigint NOT NULL COMMENT '新激活实际输入修订号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '转办事实创建时间UTC',
    CONSTRAINT pk_fn_workflow_todo_transfer PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_todo_transfer_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance (Id),
    CONSTRAINT fk_fn_workflow_todo_transfer_step FOREIGN KEY (StepId) REFERENCES fn_workflow_step (Id),
    CONSTRAINT fk_fn_workflow_todo_transfer_slot FOREIGN KEY (SlotId) REFERENCES fn_workflow_approval_slot (Id),
    CONSTRAINT fk_fn_workflow_todo_transfer_origin FOREIGN KEY (OriginTodoId) REFERENCES fn_workflow_todo (Id),
    CONSTRAINT fk_fn_workflow_todo_transfer_from FOREIGN KEY (FromTodoId) REFERENCES fn_workflow_todo (Id),
    CONSTRAINT fk_fn_workflow_todo_transfer_to FOREIGN KEY (ToTodoId) REFERENCES fn_workflow_todo (Id)
) COMMENT='工作流本人转办交接事实' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_todo_transfer_recover;
DELIMITER $$
CREATE PROCEDURE fn_workflow_todo_transfer_recover()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND INDEX_NAME='uq_fn_workflow_todo_transfer_from') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_todo_transfer_from ON fn_workflow_todo_transfer (FromTodoId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND INDEX_NAME='uq_fn_workflow_todo_transfer_to') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_todo_transfer_to ON fn_workflow_todo_transfer (ToTodoId);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.statistics WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND INDEX_NAME='uq_fn_workflow_todo_transfer_origin_depth') THEN
        CREATE UNIQUE INDEX uq_fn_workflow_todo_transfer_origin_depth ON fn_workflow_todo_transfer (OriginTodoId, Depth);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.table_constraints WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_todo_transfer' AND CONSTRAINT_NAME='ck_fn_workflow_todo_transfer_identity') THEN
        ALTER TABLE fn_workflow_todo_transfer ADD CONSTRAINT ck_fn_workflow_todo_transfer_identity CHECK (Depth BETWEEN 1 AND 8 AND FormRevision>0 AND FromTodoId<>ToTodoId AND FromAssigneeUserId<>ToAssigneeUserId AND ActorUserId=FromAssigneeUserId AND AuthorizationKey='workflow.todos.transfer');
    END IF;
END$$
DELIMITER ;
CALL fn_workflow_todo_transfer_recover();
DROP PROCEDURE IF EXISTS fn_workflow_todo_transfer_recover;
