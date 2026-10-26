-- 310：CREATE TABLE原子创建；索引和注释在DbUp未记账时仍可补齐。
CREATE TABLE IF NOT EXISTS fn_workflow_delay (
    Id binary(16) NOT NULL COMMENT '复用延时激活步骤的UUID v7标识',
    InstanceId binary(16) NOT NULL COMMENT '所属Workflow实例标识',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '激活时的可信租户作用域',
    NodeKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '发布版本中的延时节点键',
    ContinuationNodeKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '等待结束后才解析的固定后继节点键',
    InputJson longtext NOT NULL COMMENT '本次激活不可变表单输入快照',
    InputHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定输入的SHA256摘要',
    InputFormRevision bigint NOT NULL COMMENT '输入所属主表单或分支修订号',
    ParallelJoinId binary(16) NULL COMMENT '本次激活所属分叉汇合身份',
    ParallelBranchKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '本次激活所属稳定分支键',
    ModeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'relative墙钟时长或absolute绝对时间',
    TimeZoneKey varchar(128) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '配置原时区标识，绝对输入必须另带明确偏移',
    DueAtUtc datetime(6) NOT NULL COMMENT '已解析的UTC唤醒时间',
    RemainingMilliseconds bigint NULL COMMENT '暂停时冻结的非负相对等待毫秒数',
    PausedAtUtc datetime(6) NULL COMMENT '相对等待最近暂停时间，绝对时间不平移',
    StatusKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'waiting等待、fired已唤醒或cancelled已取消',
    Revision bigint NOT NULL COMMENT '唤醒与暂停恢复并发裁决修订号',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '激活创建时间',
    CompletedAtUtc datetime(6) NULL COMMENT '唤醒或取消完成时间',
    CONSTRAINT pk_fn_workflow_delay PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_delay_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_delay_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id),
    CONSTRAINT ck_fn_workflow_delay_state CHECK (StatusKey IN ('waiting','fired','cancelled') AND ModeKey IN ('relative','absolute') AND Revision>0 AND InputFormRevision>0),
    CONSTRAINT ck_fn_workflow_delay_pause CHECK ((PausedAtUtc IS NULL AND RemainingMilliseconds IS NULL) OR (ModeKey='relative' AND PausedAtUtc IS NOT NULL AND RemainingMilliseconds BETWEEN 0 AND 31622400000))
) COMMENT='工作流耐久延时激活事实，唤醒与后继续办原子收敛' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_ensure_delay;
DELIMITER $$
CREATE PROCEDURE fn_workflow_ensure_delay()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_delay' AND INDEX_NAME='ix_fn_workflow_delay_due') THEN
  CREATE INDEX ix_fn_workflow_delay_due ON fn_workflow_delay(StatusKey,DueAtUtc,Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_ensure_delay();
DROP PROCEDURE IF EXISTS fn_workflow_ensure_delay;
