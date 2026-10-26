-- 317：工作流真实业务命令等待与精确补偿意图；重跑分别探测约束、索引与外键。
CREATE TABLE IF NOT EXISTS fn_workflow_business_cmd (
 Id binary(16) NOT NULL COMMENT '复用真实业务步骤的应用端UUID v7标识',
 InstanceId binary(16) NOT NULL COMMENT '本模块根实例标识',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '可信实例租户作用域',
 NodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '发布资产中的业务节点键',
 ContinuationNodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '发布资产中的唯一结束节点',
 ActionKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '实际所有者静态注册的动作键',
 ActionVersion int NOT NULL COMMENT '固定动作版本',
 ActionHash char(64) COLLATE utf8mb4_bin NOT NULL COMMENT '静态动作元数据SHA256摘要',
 BindingJson longtext NOT NULL COMMENT '固定根业务绑定；不含业务快照',
 InputHash char(64) COLLATE utf8mb4_bin NOT NULL COMMENT '绑定的规范化协议摘要',
 StatusKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '请求、效果等待、已应用、取消、补偿或故障状态',
 CompensationRequested boolean NOT NULL COMMENT '根取消后固化的补偿意图',
 Generation int NOT NULL COMMENT '可靠执行与补偿投递代次',
 OwnerInputHash char(64) COLLATE utf8mb4_bin NULL COMMENT '所有者授权业务输入摘要',
 AppliedVersion bigint NULL COMMENT '实际应用后的所有者版本',
 CompensatedVersion bigint NULL COMMENT '实际补偿后的所有者版本',
 ErrorCode varchar(128) COLLATE utf8mb4_bin NULL COMMENT '受控机器错误码；不记录业务快照',
 ResultJson longtext NULL COMMENT '实际所有者最小结果回执',
 Revision bigint NOT NULL COMMENT '本地乐观并发修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '创建UTC微秒时间',
 UpdatedAtUtc datetime(6) NOT NULL COMMENT '更新UTC微秒时间',
 CONSTRAINT pk_fn_workflow_business_cmd PRIMARY KEY (Id)
) COMMENT='工作流真实业务命令等待与精确补偿意图' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_business_cmd_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_business_cmd_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND CONSTRAINT_NAME='ck_fn_workflow_business_cmd_state') THEN
  ALTER TABLE fn_workflow_business_cmd ADD CONSTRAINT ck_fn_workflow_business_cmd_state CHECK (Revision>0 AND ActionVersion BETWEEN 1 AND 65535 AND Generation BETWEEN 1 AND 1000 AND StatusKey IN ('requested','result_ready','applied','compensating','cancelled','compensated','declined','faulted') AND (AppliedVersion IS NULL OR AppliedVersion>0) AND (CompensatedVersion IS NULL OR (AppliedVersion IS NOT NULL AND CompensatedVersion>AppliedVersion)) AND (StatusKey NOT IN ('applied','result_ready') OR (OwnerInputHash IS NOT NULL AND AppliedVersion IS NOT NULL)) AND (StatusKey<>'cancelled' OR (AppliedVersion IS NULL AND CompensatedVersion IS NULL)) AND (StatusKey<>'compensated' OR (OwnerInputHash IS NOT NULL AND AppliedVersion IS NOT NULL AND CompensatedVersion IS NOT NULL)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND CONSTRAINT_NAME='ck_fn_workflow_business_cmd_bindingjson') THEN
  ALTER TABLE fn_workflow_business_cmd ADD CONSTRAINT ck_fn_workflow_business_cmd_bindingjson CHECK (BindingJson IS NULL OR JSON_VALID(BindingJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND CONSTRAINT_NAME='ck_fn_workflow_business_cmd_resultjson') THEN
  ALTER TABLE fn_workflow_business_cmd ADD CONSTRAINT ck_fn_workflow_business_cmd_resultjson CHECK (ResultJson IS NULL OR JSON_VALID(ResultJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND INDEX_NAME='ux_fn_workflow_business_cmd_node') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_business_cmd_node ON fn_workflow_business_cmd(InstanceId,NodeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND INDEX_NAME='ix_fn_workflow_business_cmd_ready') THEN
  CREATE INDEX ix_fn_workflow_business_cmd_ready ON fn_workflow_business_cmd(StatusKey,UpdatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND INDEX_NAME='ix_fn_workflow_business_cmd_compensation') THEN
  CREATE INDEX ix_fn_workflow_business_cmd_compensation ON fn_workflow_business_cmd(CompensationRequested,UpdatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND CONSTRAINT_NAME='fk_fn_workflow_business_cmd_step') THEN
  ALTER TABLE fn_workflow_business_cmd ADD CONSTRAINT fk_fn_workflow_business_cmd_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_business_cmd' AND CONSTRAINT_NAME='fk_fn_workflow_business_cmd_instance') THEN
  ALTER TABLE fn_workflow_business_cmd ADD CONSTRAINT fk_fn_workflow_business_cmd_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_business_cmd_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_business_cmd_ensure;
