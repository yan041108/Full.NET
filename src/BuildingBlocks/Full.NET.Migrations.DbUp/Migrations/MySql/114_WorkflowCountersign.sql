-- 114：为工作流待办加签引入链与有序加签项事实表；前加签会挂起原办理人待办，后加签在原办理人同意后依次激活。
CREATE TABLE IF NOT EXISTS fn_workflow_countersign_chain (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    InstanceId BINARY(16) NOT NULL COMMENT '流程实例标识',
    StepId BINARY(16) NOT NULL COMMENT '流程步骤标识',
    OriginTodoId BINARY(16) NOT NULL COMMENT '发起加签的原待办标识',
    DirectionKey varchar(16) NOT NULL COMMENT '加签方向键',
    StatusKey varchar(16) NOT NULL COMMENT '加签链状态键',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '发起加签的用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    ActiveOriginKey BINARY(16) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin
        GENERATED ALWAYS AS (CASE WHEN StatusKey = 'active' THEN OriginTodoId ELSE NULL END) STORED COMMENT '活动加签链占用键',
    CONSTRAINT PK_fn_workflow_countersign_chain PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_countersign_chain_Instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT FK_fn_workflow_countersign_chain_Step FOREIGN KEY (StepId) REFERENCES fn_workflow_step(Id),
    CONSTRAINT FK_fn_workflow_countersign_chain_OriginTodo FOREIGN KEY (OriginTodoId) REFERENCES fn_workflow_todo(Id),
    CONSTRAINT CK_fn_workflow_countersign_chain_Direction CHECK (DirectionKey IN ('before', 'after')),
    CONSTRAINT CK_fn_workflow_countersign_chain_Status CHECK (StatusKey IN ('active', 'completed', 'cancelled'))
) COMMENT='工作流加签链表' ENGINE=InnoDB;

SET @hasActiveOrigin := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_countersign_chain'
      AND INDEX_NAME = 'UX_fn_workflow_countersign_chain_ActiveOrigin');
SET @addActiveOrigin := IF(
    @hasActiveOrigin = 0,
    'ALTER TABLE fn_workflow_countersign_chain ADD CONSTRAINT UX_fn_workflow_countersign_chain_ActiveOrigin UNIQUE (ActiveOriginKey)',
    'SELECT 1');
PREPARE stmt FROM @addActiveOrigin;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

CREATE TABLE IF NOT EXISTS fn_workflow_countersign_item (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    ChainId BINARY(16) NOT NULL COMMENT '所属加签链标识',
    SequenceNo int NOT NULL COMMENT '加签顺序号',
    AssigneeUserId BINARY(16) NOT NULL COMMENT '加签办理人标识',
    TodoId BINARY(16) NULL COMMENT '关联待办标识',
    StatusKey varchar(16) NOT NULL COMMENT '加签项状态键',
    CONSTRAINT PK_fn_workflow_countersign_item PRIMARY KEY (Id),
    CONSTRAINT FK_fn_workflow_countersign_item_Chain FOREIGN KEY (ChainId) REFERENCES fn_workflow_countersign_chain(Id),
    CONSTRAINT FK_fn_workflow_countersign_item_Todo FOREIGN KEY (TodoId) REFERENCES fn_workflow_todo(Id),
    CONSTRAINT UQ_fn_workflow_countersign_item_Chain_Sequence UNIQUE (ChainId, SequenceNo),
    CONSTRAINT CK_fn_workflow_countersign_item_Sequence CHECK (SequenceNo > 0),
    CONSTRAINT CK_fn_workflow_countersign_item_Status CHECK (StatusKey IN ('pending', 'active', 'completed', 'cancelled'))
) COMMENT='工作流加签项表' ENGINE=InnoDB;

SET @hasTodoUnique := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_countersign_item'
      AND INDEX_NAME = 'UX_fn_workflow_countersign_item_Todo');
SET @addTodoUnique := IF(
    @hasTodoUnique = 0,
    'CREATE UNIQUE INDEX UX_fn_workflow_countersign_item_Todo ON fn_workflow_countersign_item (TodoId)',
    'SELECT 1');
PREPARE stmt FROM @addTodoUnique;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @hasChainStatus := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_workflow_countersign_item'
      AND INDEX_NAME = 'IX_fn_workflow_countersign_item_Chain_Status');
SET @addChainStatus := IF(
    @hasChainStatus = 0,
    'CREATE INDEX IX_fn_workflow_countersign_item_Chain_Status ON fn_workflow_countersign_item (ChainId, StatusKey, SequenceNo)',
    'SELECT 1');
PREPARE stmt FROM @addChainStatus;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
