-- 311：CREATE TABLE原子创建，重复部署保留全部活动结果。
DROP PROCEDURE IF EXISTS fn_workflow_restore_field_audit_311;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_field_audit_311()
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
CALL fn_workflow_restore_field_audit_311();
DROP PROCEDURE IF EXISTS fn_workflow_restore_field_audit_311;
CREATE TABLE IF NOT EXISTS fn_workflow_activity_result (
    Id binary(16) NOT NULL COMMENT '复用自动字段活动步骤的UUID v7标识',
    InstanceId binary(16) NOT NULL COMMENT '所属Workflow实例标识',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '活动绑定的可信租户作用域',
    InputFormRevision bigint NOT NULL COMMENT '固定输入主表单或分支修订号',
    OutputFormRevision bigint NOT NULL COMMENT '接纳输出后的表单或分支修订号',
    InputHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定输入的SHA256摘要',
    OutputHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '接纳输出的SHA256摘要',
    InputJson longtext NOT NULL COMMENT '本次活动不可变输入快照',
    OutputJson longtext NOT NULL COMMENT '本次活动接纳的完整输出快照',
    PatchJson longtext NOT NULL COMMENT '本次活动显式授权的字段补丁',
    OutcomeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'applied已执行或skipped条件未命中',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '本次活动结果接纳时间',
    CONSTRAINT pk_fn_workflow_activity_result PRIMARY KEY (Id),
    CONSTRAINT fk_fn_workflow_activity_result_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
    CONSTRAINT fk_fn_workflow_activity_result_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id),
    CONSTRAINT ck_fn_workflow_activity_result_outcome CHECK ((OutcomeKey='applied' AND OutputFormRevision=InputFormRevision+1) OR (OutcomeKey='skipped' AND OutputFormRevision=InputFormRevision))
) COMMENT='工作流自动字段活动的不可变输入、输出与补丁事实' ENGINE=InnoDB;
