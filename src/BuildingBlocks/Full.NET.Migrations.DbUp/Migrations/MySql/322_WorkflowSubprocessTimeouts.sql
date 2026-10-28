-- 322：子流程截止与取消收敛事实；辅助过程只做幂等结构探测。
CREATE TABLE IF NOT EXISTS fn_workflow_subprocess_timeout (
 CallId binary(16) NOT NULL COMMENT '实际父调用UUID v7身份',
 InstanceId binary(16) NOT NULL COMMENT '父实例身份',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '父实例可信作用域',
 DueAtUtc datetime(6) NOT NULL COMMENT '固定或恢复后的截止UTC时间',
 PauseModeKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '暂停时freeze冻结或elapsed流逝',
 RemainingMilliseconds bigint NULL COMMENT '冻结剩余毫秒',
 PausedAtUtc datetime(6) NULL COMMENT '冻结UTC时间',
 StatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '等待清理就绪及关闭状态',
 Revision bigint NOT NULL COMMENT '乐观并发修订',
 CONSTRAINT pk_fn_workflow_subprocess_timeout PRIMARY KEY (CallId)
) COMMENT='工作流子流程截止、冻结剩余时长及清理收敛状态' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_timeout_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_subprocess_timeout_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_timeout' AND CONSTRAINT_NAME='ck_fn_workflow_subprocess_timeout_state') THEN
  ALTER TABLE fn_workflow_subprocess_timeout ADD CONSTRAINT ck_fn_workflow_subprocess_timeout_state CHECK (Revision>0 AND PauseModeKey IN ('freeze','elapsed') AND StatusKey IN ('waiting','closing','ready','applied','cancelled') AND ((RemainingMilliseconds IS NULL AND PausedAtUtc IS NULL) OR (PauseModeKey='freeze' AND RemainingMilliseconds IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31536000000 AND PausedAtUtc IS NOT NULL)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_timeout' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_timeout_call') THEN
  ALTER TABLE fn_workflow_subprocess_timeout ADD CONSTRAINT fk_fn_workflow_subprocess_timeout_call FOREIGN KEY (CallId) REFERENCES fn_workflow_subprocess_call(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_subprocess_timeout' AND CONSTRAINT_NAME='fk_fn_workflow_subprocess_timeout_instance') THEN
  ALTER TABLE fn_workflow_subprocess_timeout ADD CONSTRAINT fk_fn_workflow_subprocess_timeout_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_subprocess_timeout_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_subprocess_timeout_ensure;
