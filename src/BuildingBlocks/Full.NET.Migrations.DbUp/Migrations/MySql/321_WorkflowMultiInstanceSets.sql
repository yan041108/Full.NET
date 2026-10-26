-- 321：逐对象探测多实例集合及选择关系，支持非事务DDL部分完成后恢复。
CREATE TABLE IF NOT EXISTS fn_workflow_subprocess_set (
 CallId binary(16) NOT NULL COMMENT '复用真实父调用的UUID v7身份',
 InstanceId binary(16) NOT NULL COMMENT '拥有调用的父实例',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '父实例可信作用域',
 ModeKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT 'serial依次或parallel并行',
 SourceKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '已发布的闭合项来源类型',
 MaximumCount int NOT NULL COMMENT '固定最大项数及预留额度上限',
 RequiredPercentage int NOT NULL COMMENT '固定成功百分比；不能使用变化分母',
 ItemCount int NULL COMMENT '实际固化项数；准备完成前为空',
 RequiredSuccesses int NULL COMMENT '按固化项数向上取整的成功门槛',
 SourceJson longtext NULL COMMENT '一次固化的来源项及角色成员依据',
 SourceHash char(64) COLLATE utf8mb4_bin NULL COMMENT '完整来源快照SHA256摘要',
 SelectionHash char(64) COLLATE utf8mb4_bin NULL COMMENT '按ItemKey排序的固化成功项身份摘要',
 OutputJson longtext NULL COMMENT '只归约固化成功集合的显式父字段补丁',
 OutputHash char(64) COLLATE utf8mb4_bin NULL COMMENT '合并输出SHA256摘要',
 StatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '准备、运行、收尾、结果停车及终态',
 Revision bigint NOT NULL COMMENT '父锁内集合状态CAS修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '父调用激活UTC时间',
 UpdatedAtUtc datetime(6) NOT NULL COMMENT '最近集合更新UTC时间',
 CONSTRAINT pk_fn_workflow_subprocess_set PRIMARY KEY (CallId)
) COMMENT='工作流多实例来源快照、完成门槛及固化结果集合' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_set_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_subprocess_set_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_state') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_state CHECK (ModeKey IN ('serial','parallel') AND SourceKey IN ('fixed_count','number_field','collection_field','role_members') AND MaximumCount BETWEEN 1 AND 32 AND RequiredPercentage BETWEEN 1 AND 100 AND Revision>=1 AND StatusKey IN ('preparing','running','closing','result_ready','applied','faulted','cancelled'));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_count') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_count CHECK ((ItemCount IS NULL AND RequiredSuccesses IS NULL) OR (ItemCount IS NOT NULL AND RequiredSuccesses IS NOT NULL AND ItemCount BETWEEN 0 AND MaximumCount AND RequiredSuccesses=FLOOR((ItemCount*RequiredPercentage+99)/100)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_source') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_source CHECK ((SourceJson IS NULL AND SourceHash IS NULL) OR (SourceJson IS NOT NULL AND SourceHash IS NOT NULL));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_output') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_output CHECK ((OutputJson IS NULL AND OutputHash IS NULL) OR (OutputJson IS NOT NULL AND OutputHash IS NOT NULL));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_ready') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_ready CHECK (StatusKey NOT IN ('running','closing','result_ready','applied') OR (ItemCount IS NOT NULL AND SourceJson IS NOT NULL AND SourceHash IS NOT NULL));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_selected') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_selected CHECK (StatusKey NOT IN ('closing','result_ready','applied') OR SelectionHash IS NOT NULL);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_result') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_result CHECK (StatusKey NOT IN ('result_ready','applied') OR (OutputJson IS NOT NULL AND OutputHash IS NOT NULL));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_sourcejson') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_sourcejson CHECK (SourceJson IS NULL OR JSON_VALID(SourceJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_set_outputjson') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT ck_fn_workflow_subprocess_set_outputjson CHECK (OutputJson IS NULL OR JSON_VALID(OutputJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND INDEX_NAME='ix_fn_workflow_subprocess_set_instance') THEN
  CREATE INDEX ix_fn_workflow_subprocess_set_instance ON fn_workflow_subprocess_set(InstanceId,TenantScopeKey,StatusKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_set_call') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT fk_fn_workflow_subprocess_set_call FOREIGN KEY (CallId) REFERENCES fn_workflow_subprocess_call(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_set' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_set_instance') THEN
  ALTER TABLE fn_workflow_subprocess_set ADD CONSTRAINT fk_fn_workflow_subprocess_set_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_subprocess_set_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_set_ensure;
CREATE TABLE IF NOT EXISTS fn_workflow_subprocess_choice (
 CallId binary(16) NOT NULL COMMENT '归属集合的真实调用身份',
 ItemId binary(16) NOT NULL COMMENT '已成功并被固化选择的真实项身份',
 InstanceId binary(16) NOT NULL COMMENT '实际父实例身份',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '父实例可信作用域',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '首次固化选择UTC时间',
 CONSTRAINT pk_fn_workflow_subprocess_choice PRIMARY KEY (CallId,ItemId)
) COMMENT='工作流首次达标固化的成功子项；迟到结果不得追加' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_choice_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_subprocess_choice_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_choice' AND INDEX_NAME='ux_fn_workflow_subprocess_choice_item') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_subprocess_choice_item ON fn_workflow_subprocess_choice(ItemId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_choice' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_choice_set') THEN
  ALTER TABLE fn_workflow_subprocess_choice ADD CONSTRAINT fk_fn_workflow_subprocess_choice_set FOREIGN KEY (CallId) REFERENCES fn_workflow_subprocess_set(CallId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_choice' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_choice_item') THEN
  ALTER TABLE fn_workflow_subprocess_choice ADD CONSTRAINT fk_fn_workflow_subprocess_choice_item FOREIGN KEY (ItemId) REFERENCES fn_workflow_subprocess_item(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_choice' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_choice_instance') THEN
  ALTER TABLE fn_workflow_subprocess_choice ADD CONSTRAINT fk_fn_workflow_subprocess_choice_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_subprocess_choice_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_choice_ensure;
