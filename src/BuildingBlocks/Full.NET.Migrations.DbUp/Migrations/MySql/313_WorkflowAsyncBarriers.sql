-- 313：异步消费者耐久屏障与服务器一次性续办证明。
CREATE TABLE IF NOT EXISTS fn_workflow_async_barrier (
  Id binary(16) NOT NULL COMMENT '内部耐久等待步骤及屏障标识',
  InstanceId binary(16) NOT NULL COMMENT '所属原工作流实例',
  TenantScopeKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信Host或租户作用域',
  NodeKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '依赖结果的原消费者节点键',
  DependencyCallIdsJson longtext NOT NULL COMMENT '本次等待绑定的原异步激活标识集合',
  DependencyHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定依赖集合的SHA256摘要',
  InputJson longtext NOT NULL COMMENT '到达屏障时的原表单或分支快照',
  InputHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原屏障输入的SHA256摘要',
  InputFormRevision bigint NOT NULL COMMENT '到达屏障时原主表单或分支修订',
  ParallelJoinId binary(16) NULL COMMENT '原并行或包容分叉执行身份',
  ParallelBranchKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '原分叉中的稳定分支键',
  StatusKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '屏障的耐久闭合状态',
  LeaseGeneration bigint NOT NULL COMMENT '阻止旧Worker释放屏障的租约世代',
  LeaseOwnerKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '当前接纳Worker的唯一领取身份',
  LeaseExpiresAtUtc datetime(6) NULL COMMENT '当前接纳租约的UTC截止',
  FailureCount int NOT NULL COMMENT '连续准备或提交失败次数，正常等待不计失败',
  Revision bigint NOT NULL COMMENT '屏障乐观修订',
  ResumeWorkId binary(16) NULL COMMENT '已释放屏障唯一允许续办的原工作项',
  OutputJson longtext NULL COMMENT '释放时实际当前表单或分支输入',
  OutputHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '唯一续办工作项输入摘要',
  OutputFormRevision bigint NULL COMMENT '唯一续办工作项绑定的实际表单或分支修订',
  DueAtUtc datetime(6) NOT NULL COMMENT '下次有界等待或安全接纳的UTC时间',
  CreatedAtUtc datetime(6) NOT NULL COMMENT '原屏障建立时间UTC',
  CompletedAtUtc datetime(6) NULL COMMENT '释放、取消或不可恢复失败时间UTC',
  ErrorKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '稳定错误码，不保存异常或原始载荷',
  CONSTRAINT pk_fn_workflow_async_barrier PRIMARY KEY (Id),
  CONSTRAINT fk_fn_workflow_async_barrier_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
  CONSTRAINT fk_fn_workflow_async_barrier_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id),
  CONSTRAINT fk_fn_workflow_async_barrier_resume_work FOREIGN KEY (ResumeWorkId) REFERENCES fn_workflow_activity_work(Id),
  CONSTRAINT ck_fn_workflow_async_barrier_state CHECK (StatusKey IN ('pending','applying','released','cancelled','failed') AND LeaseGeneration>=0 AND FailureCount BETWEEN 0 AND 8 AND Revision>0 AND InputFormRevision>0),
  CONSTRAINT ck_fn_workflow_async_barrier_lease CHECK (((StatusKey='applying' AND LeaseOwnerKey IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR (StatusKey<>'applying' AND LeaseOwnerKey IS NULL AND LeaseExpiresAtUtc IS NULL))),
  CONSTRAINT ck_fn_workflow_async_barrier_release CHECK ((StatusKey<>'released' OR (ResumeWorkId IS NOT NULL AND OutputJson IS NOT NULL AND OutputHash IS NOT NULL AND OutputFormRevision>0 AND CompletedAtUtc IS NOT NULL)))
) COMMENT='工作流异步结果消费者的耐久等待、租约与唯一续办回执' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_restore_async_313;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_async_313()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_activity_work' AND COLUMN_NAME='ResumeBarrierId') THEN
  ALTER TABLE fn_workflow_activity_work ADD COLUMN ResumeBarrierId binary(16) NULL COMMENT '服务器原屏障续办证明；普通工作项为空，不能由客户端提供';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_activity_work_resume_barrier') THEN
  ALTER TABLE fn_workflow_activity_work ADD CONSTRAINT fk_fn_workflow_activity_work_resume_barrier FOREIGN KEY (ResumeBarrierId) REFERENCES fn_workflow_async_barrier(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_async_barrier' AND INDEX_NAME='ix_fn_workflow_async_barrier_due') THEN
  CREATE INDEX ix_fn_workflow_async_barrier_due ON fn_workflow_async_barrier(StatusKey,DueAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_async_barrier' AND INDEX_NAME='ix_fn_workflow_async_barrier_instance') THEN
  CREATE INDEX ix_fn_workflow_async_barrier_instance ON fn_workflow_async_barrier(TenantScopeKey,InstanceId,CreatedAtUtc,Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_restore_async_313();
DROP PROCEDURE IF EXISTS fn_workflow_restore_async_313;
