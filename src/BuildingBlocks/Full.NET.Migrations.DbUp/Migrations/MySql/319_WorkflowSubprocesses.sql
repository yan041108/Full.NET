-- 319：工作流固定子版本的真实父活动等待及预算预留；重跑分别探测约束、索引与外键。
CREATE TABLE IF NOT EXISTS fn_workflow_subprocess_call (
 Id binary(16) NOT NULL COMMENT '复用真实父步骤的应用端UUID v7标识',
 InstanceId binary(16) NOT NULL COMMENT '拥有该活动的父实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信父实例作用域',
 NodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '固定父节点键',
 ContinuationNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '该次调用的后继节点',
 ChildDefinitionVersionId binary(16) NOT NULL COMMENT '固定子定义版本',
 ChildFormVersionId binary(16) NOT NULL COMMENT '固定子表单版本',
 DefinitionHash char(64) COLLATE utf8mb4_bin NOT NULL COMMENT '子定义内容摘要',
 FormHash char(64) COLLATE utf8mb4_bin NOT NULL COMMENT '子表单内容摘要',
 PolicyJson longtext NOT NULL COMMENT '固定父节点策略及服务端子版本绑定',
 ParentInputJson longtext NOT NULL COMMENT '父活动原始输入；只向子实例传输显式映射字段',
 InputFormRevision bigint NOT NULL COMMENT '父输入表单或分支修订',
 ParallelJoinId binary(16) NULL COMMENT '原并行分叉身份',
 ParallelBranchKey varchar(128) COLLATE utf8mb4_bin NULL COMMENT '原并行分支键',
 Depth int NOT NULL COMMENT '当前子根在整组实例中的深度',
 ChildActivityBudget int NOT NULL COMMENT '从父额度固定预留的子活动总额',
 StatusKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '请求、运行、结果等待、完成、故障或取消状态',
 CancellationRequested boolean NOT NULL COMMENT '父活动取消意图',
 ErrorCode varchar(128) COLLATE utf8mb4_bin NULL COMMENT '不包含业务快照的稳定故障码',
 Revision bigint NOT NULL COMMENT '乐观并发修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '创建UTC微秒时间',
 UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新UTC微秒时间',
 CONSTRAINT pk_fn_workflow_subprocess_call PRIMARY KEY (Id)
) COMMENT='工作流固定子版本的真实父活动等待及预算预留' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_call_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_subprocess_call_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_call_state') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT ck_fn_workflow_subprocess_call_state CHECK (InputFormRevision>0 AND Depth BETWEEN 1 AND 8 AND ChildActivityBudget BETWEEN 2 AND 4096 AND Revision>0 AND StatusKey IN ('requested','running','result_ready','applied','faulted','cancelled') AND ((ParallelJoinId IS NULL AND ParallelBranchKey IS NULL) OR (ParallelJoinId IS NOT NULL AND ParallelBranchKey IS NOT NULL)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_call_policyjson') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT ck_fn_workflow_subprocess_call_policyjson CHECK (PolicyJson IS NULL OR JSON_VALID(PolicyJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_call_parentinputjson') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT ck_fn_workflow_subprocess_call_parentinputjson CHECK (ParentInputJson IS NULL OR JSON_VALID(ParentInputJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND COLUMN_NAME='ExecutionEpochId') AND NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND INDEX_NAME='ux_fn_workflow_subprocess_call_node') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_subprocess_call_node ON fn_workflow_subprocess_call(InstanceId,NodeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND INDEX_NAME='ix_fn_workflow_subprocess_call_pending') THEN
  CREATE INDEX ix_fn_workflow_subprocess_call_pending ON fn_workflow_subprocess_call(StatusKey,UpdatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_call_step') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_call_instance') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_call_version') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_version FOREIGN KEY (ChildDefinitionVersionId) REFERENCES fn_workflow_definition_version(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_call' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_call_form') THEN
  ALTER TABLE fn_workflow_subprocess_call ADD CONSTRAINT fk_fn_workflow_subprocess_call_form FOREIGN KEY (ChildFormVersionId) REFERENCES fn_workflow_form_version(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_subprocess_call_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_call_ensure;

-- 319：工作流真实子项输入、原子根关联及不可变结果；重跑分别探测约束、索引与外键。
CREATE TABLE IF NOT EXISTS fn_workflow_subprocess_item (
 Id binary(16) NOT NULL COMMENT '应用端UUID v7子项身份',
 CallId binary(16) NOT NULL COMMENT '真实父步骤调用',
 InstanceId binary(16) NOT NULL COMMENT '拥有该项的父实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信父子同作用域',
 ItemKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定子项机器键',
 SequenceNo int NOT NULL COMMENT '稳定子项顺序',
 StarterUserId binary(16) NOT NULL COMMENT '激活时固定并验证的子发起人',
 InputJson longtext NOT NULL COMMENT '已授权且已校验的最小子输入',
 InputHash char(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定子输入摘要',
 ChildInstanceId binary(16) NULL COMMENT '与子根创建同事务保存的关联',
 StatusKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '待启动、运行、成功、拒绝、取消或未知状态',
 ResultJson longtext NULL COMMENT '只包含已授权映射的固定子结果',
 ResultHash char(64) COLLATE utf8mb4_bin NULL COMMENT '固定子结果摘要',
 ChildRevision bigint NULL COMMENT '确认子结果时的真实根修订',
 Revision bigint NOT NULL COMMENT '项乐观并发修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '创建UTC微秒时间',
 UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新UTC微秒时间',
 CONSTRAINT pk_fn_workflow_subprocess_item PRIMARY KEY (Id)
) COMMENT='工作流真实子项输入、原子根关联及不可变结果' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_item_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_subprocess_item_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_item_state') THEN
  ALTER TABLE fn_workflow_subprocess_item ADD CONSTRAINT ck_fn_workflow_subprocess_item_state CHECK (SequenceNo BETWEEN 1 AND 32 AND Revision>0 AND StatusKey IN ('pending','running','succeeded','rejected','cancelled','unknown') AND (StatusKey<>'pending' OR ChildInstanceId IS NULL) AND (StatusKey NOT IN ('running','succeeded','rejected') OR ChildInstanceId IS NOT NULL) AND ((ResultJson IS NULL AND ResultHash IS NULL) OR (ResultJson IS NOT NULL AND ResultHash IS NOT NULL)) AND (ChildRevision IS NULL OR ChildRevision>0));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_item_inputjson') THEN
  ALTER TABLE fn_workflow_subprocess_item ADD CONSTRAINT ck_fn_workflow_subprocess_item_inputjson CHECK (InputJson IS NULL OR JSON_VALID(InputJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_item_resultjson') THEN
  ALTER TABLE fn_workflow_subprocess_item ADD CONSTRAINT ck_fn_workflow_subprocess_item_resultjson CHECK (ResultJson IS NULL OR JSON_VALID(ResultJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND INDEX_NAME='ux_fn_workflow_subprocess_item_sequence') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_subprocess_item_sequence ON fn_workflow_subprocess_item(CallId,SequenceNo);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND INDEX_NAME='ux_fn_workflow_subprocess_item_key') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_subprocess_item_key ON fn_workflow_subprocess_item(CallId,ItemKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND INDEX_NAME='ux_fn_workflow_subprocess_item_child') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_subprocess_item_child ON fn_workflow_subprocess_item(ChildInstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_item_call') THEN
  ALTER TABLE fn_workflow_subprocess_item ADD CONSTRAINT fk_fn_workflow_subprocess_item_call FOREIGN KEY (CallId) REFERENCES fn_workflow_subprocess_call(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_item_instance') THEN
  ALTER TABLE fn_workflow_subprocess_item ADD CONSTRAINT fk_fn_workflow_subprocess_item_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_item' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_item_child') THEN
  ALTER TABLE fn_workflow_subprocess_item ADD CONSTRAINT fk_fn_workflow_subprocess_item_child FOREIGN KEY (ChildInstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_subprocess_item_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_item_ensure;

-- 319：工作流根及子根的层级活动额度；子额度不重复分配；重跑分别探测约束、索引与外键。
CREATE TABLE IF NOT EXISTS fn_workflow_exec_budget (
 InstanceId binary(16) NOT NULL COMMENT '复用应用端UUID v7实例身份',
 Granted int NOT NULL COMMENT '由父项预留或普通根默认分配的总额',
 Reserved int NOT NULL COMMENT '已预留给所有子项的总额；取消不自动返还',
 Revision bigint NOT NULL COMMENT '预算乐观并发修订',
 CONSTRAINT pk_fn_workflow_exec_budget PRIMARY KEY (InstanceId)
) COMMENT='工作流根及子根的层级活动额度；子额度不重复分配' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_exec_budget_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_exec_budget_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_exec_budget' AND CONSTRAINT_NAME='ck_fn_workflow_exec_budget_state') THEN
  ALTER TABLE fn_workflow_exec_budget ADD CONSTRAINT ck_fn_workflow_exec_budget_state CHECK (Granted BETWEEN 2 AND 4096 AND Reserved BETWEEN 0 AND Granted AND Revision>0);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_exec_budget' AND CONSTRAINT_NAME='fk_fn_workflow_exec_budget_instance') THEN
  ALTER TABLE fn_workflow_exec_budget ADD CONSTRAINT fk_fn_workflow_exec_budget_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_exec_budget_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_exec_budget_ensure;

-- 固定节点键与服务端128字符上限一致；扩大类型不改变已有命令身份。
DROP PROCEDURE IF EXISTS fn_workflow_ensure_business_node_keys;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_business_node_keys()
BEGIN
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND COLUMN_NAME='NodeKey' AND CHARACTER_MAXIMUM_LENGTH<128) THEN
  ALTER TABLE fn_workflow_business_cmd MODIFY NodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '发布资产中的业务节点键';
 END IF;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND COLUMN_NAME='ContinuationNodeKey' AND CHARACTER_MAXIMUM_LENGTH<128) THEN
  ALTER TABLE fn_workflow_business_cmd MODIFY ContinuationNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '发布资产中的唯一结束节点';
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_business_node_keys();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_business_node_keys;

-- 319：只允许带真实父项来源的系统级实例取消，不伪造个人动作。
-- 311：CREATE TABLE原子创建，重复部署保留全部活动结果。
DROP PROCEDURE IF EXISTS fn_workflow_restore_subprocess_audit_319;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_subprocess_audit_319()
BEGIN
 IF EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS AS tc INNER JOIN information_schema.CHECK_CONSTRAINTS AS cc ON cc.CONSTRAINT_SCHEMA=tc.CONSTRAINT_SCHEMA AND cc.CONSTRAINT_NAME=tc.CONSTRAINT_NAME WHERE tc.CONSTRAINT_SCHEMA=DATABASE() AND tc.TABLE_NAME='fn_workflow_domain_audit' AND tc.CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND (LOCATE('activity.execute',cc.CHECK_CLAUSE)=0 OR LOCATE('parent_subprocess',cc.CHECK_CLAUSE)=0)) THEN
  ALTER TABLE fn_workflow_domain_audit DROP CHECK ck_fn_workflow_domain_audit_actor;
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_domain_audit' AND CONSTRAINT_NAME='ck_fn_workflow_domain_audit_actor' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_domain_audit ADD CONSTRAINT ck_fn_workflow_domain_audit_actor CHECK (ActorUserId IS NOT NULL OR (OperationKey IN ('assignee.handle','activity.execute') AND ResourceTypeKey='step') OR
   (OperationKey='instance.cancel' AND ResourceTypeKey='instance' AND JSON_EXTRACT(DetailJson,'$.sourceKey') IS NOT NULL AND
    JSON_UNQUOTE(JSON_EXTRACT(DetailJson,'$.sourceKey'))='parent_subprocess' AND JSON_EXTRACT(DetailJson,'$.parentInstanceId') IS NOT NULL AND JSON_EXTRACT(DetailJson,'$.parentStepId') IS NOT NULL AND
    JSON_TYPE(JSON_EXTRACT(DetailJson,'$.parentInstanceId'))='STRING' AND JSON_TYPE(JSON_EXTRACT(DetailJson,'$.parentStepId'))='STRING'));
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_restore_subprocess_audit_319();
DROP PROCEDURE IF EXISTS fn_workflow_restore_subprocess_audit_319;
