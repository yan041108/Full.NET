-- 324：分叉主键即该次ForkActivationId；结构探测支持隐式DDL提交后的重跑。
DROP PROCEDURE IF EXISTS fn_workflow_fork_epoch_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_fork_epoch_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_join' AND COLUMN_NAME='ExecutionEpochId') THEN
  ALTER TABLE fn_workflow_parallel_join ADD COLUMN ExecutionEpochId binary(16) NULL COMMENT '真实分叉执行代次；Id即该次ForkActivationId，旧到达不得跨代次';
 END IF;
 -- 323之前只允许DAG，初始绑定只补空值，不覆盖已持久的新一轮事实。
 UPDATE fn_workflow_parallel_join SET ExecutionEpochId=InstanceId WHERE ExecutionEpochId IS NULL;
 IF EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_join' AND COLUMN_NAME='ExecutionEpochId' AND IS_NULLABLE='YES') THEN
  ALTER TABLE fn_workflow_parallel_join MODIFY COLUMN ExecutionEpochId binary(16) NOT NULL COMMENT '真实分叉执行代次；Id即该次ForkActivationId，旧到达不得跨代次';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_join' AND CONSTRAINT_NAME='fk_fn_workflow_parallel_join_epoch') THEN
  ALTER TABLE fn_workflow_parallel_join ADD CONSTRAINT fk_fn_workflow_parallel_join_epoch FOREIGN KEY (ExecutionEpochId,InstanceId) REFERENCES fn_workflow_execution_epoch(Id,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_parallel_join' AND INDEX_NAME='ix_fn_workflow_parallel_join_epoch') THEN
  CREATE INDEX ix_fn_workflow_parallel_join_epoch ON fn_workflow_parallel_join(InstanceId,ExecutionEpochId,StatusKey,Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_fork_epoch_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_fork_epoch_ensure;
