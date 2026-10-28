-- 326：有效活动按真实步骤绑定；每项DDL单独探测以恢复未记账的部分迁移。
CREATE TABLE IF NOT EXISTS fn_workflow_activation_binding (
 Id binary(16) NOT NULL COMMENT '绑定UUID v7身份',
 InstanceId binary(16) NOT NULL COMMENT '实际根实例身份',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '根实例可信作用域',
 ExecutionEpochId binary(16) NOT NULL COMMENT '消费此绑定的真实执行代次',
 NodeKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '固定定义节点引用',
 StepId binary(16) NOT NULL COMMENT '唯一有效的实际活动步骤',
 SourceEpochId binary(16) NOT NULL COMMENT '实际步骤原始执行代次；继承不得改写',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '绑定创建UTC时间',
 CONSTRAINT pk_fn_workflow_activation_binding PRIMARY KEY (Id)
) COMMENT='工作流有效活动绑定；区域继承保留实际来源步骤身份' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_activation_binding_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_activation_binding_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activation_binding' AND INDEX_NAME='ux_fn_workflow_activation_binding_node') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_activation_binding_node ON fn_workflow_activation_binding(ExecutionEpochId,NodeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activation_binding' AND CONSTRAINT_NAME='fk_fn_workflow_activation_binding_epoch') THEN
  ALTER TABLE fn_workflow_activation_binding ADD CONSTRAINT fk_fn_workflow_activation_binding_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activation_binding' AND CONSTRAINT_NAME='fk_fn_workflow_activation_binding_step') THEN
  ALTER TABLE fn_workflow_activation_binding ADD CONSTRAINT fk_fn_workflow_activation_binding_step FOREIGN KEY (StepId,InstanceId) REFERENCES fn_workflow_step(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activation_binding' AND CONSTRAINT_NAME='fk_fn_workflow_activation_binding_source') THEN
  ALTER TABLE fn_workflow_activation_binding ADD CONSTRAINT fk_fn_workflow_activation_binding_source FOREIGN KEY (StepId,SourceEpochId) REFERENCES fn_workflow_step(Id,ExecutionEpochId);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_activation_binding_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_activation_binding_ensure;
