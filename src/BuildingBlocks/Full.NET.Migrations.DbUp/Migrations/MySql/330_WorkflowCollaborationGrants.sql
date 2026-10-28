-- 330：双边同意仅建立共享授权；不得在同一业务事务写入两个作用域。
CREATE TABLE IF NOT EXISTS fn_workflow_collaboration_grant (
 Id binary(16) NOT NULL COMMENT '双边协作授权UUID v7',
 SourceTenantId binary(16) NULL COMMENT '源租户；Host为空',
 TargetTenantId binary(16) NULL COMMENT '目标租户；Host为空',
 SourceTenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '源方可信规范作用域',
 TargetTenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '目标方可信规范作用域',
 SourceDefinitionId binary(16) NOT NULL COMMENT '源方同意的流程定义',
 SourceFormVersionId binary(16) NOT NULL COMMENT '源方同意的固定表单版本',
 TargetDefinitionVersionId binary(16) NULL COMMENT '目标方接受时固定的已发布版本',
 SourceActorId binary(16) NOT NULL COMMENT '源方提出同意的真实操作人',
 TargetActorId binary(16) NULL COMMENT '目标方接受同意的真实操作人',
 OfferJson longtext NOT NULL COMMENT '源方固定字段及执行额度政策',
 AcceptanceJson longtext NULL COMMENT '目标方固定映射人员及版本政策',
 PolicyHash varchar(64) COLLATE utf8mb4_bin NULL COMMENT '绑定双方身份及完整政策的SHA256摘要',
 TargetSummaryJson longtext NULL COMMENT '仅含获准投影的目标版本安全摘要',
 TargetSummaryHash varchar(64) COLLATE utf8mb4_bin NULL COMMENT '目标版本安全摘要的SHA256',
 ProposalKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '源操作人的提出幂等键',
 ProposalHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '源提出请求的规范摘要',
 Revision bigint NOT NULL COMMENT '不可变同意及撤销的CAS修订',
 StatusKey varchar(24) COLLATE utf8mb4_bin NOT NULL COMMENT 'proposed或active或revoked',
 ExpiresAtUtc datetime(6) NOT NULL COMMENT '授权绝对期限；微秒UTC',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '源提出时间',
 AcceptedAtUtc datetime(6) NULL COMMENT '目标接受时间',
 RevokedAtUtc datetime(6) NULL COMMENT '任一参与方撤销时间',
 RevokedById binary(16) NULL COMMENT '实际撤销操作人',
 RevokedByScopeKey varchar(64) COLLATE utf8mb4_bin NULL COMMENT '实际撤销操作的可信参与方作用域',
 RevocationReason varchar(512) COLLATE utf8mb4_bin NULL COMMENT '撤销或拒绝的审计原因',
 CONSTRAINT pk_fn_workflow_collaboration_grant PRIMARY KEY (Id)
) COMMENT='工作流双边共享授权控制面；业务执行各在自己租户事务内' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
CREATE TABLE IF NOT EXISTS fn_workflow_collaboration_grant_action (
 Id binary(16) NOT NULL COMMENT '授权操作收据UUID v7',
 GrantId binary(16) NOT NULL COMMENT '所属双边授权',
 ActorTenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '实际操作人的可信作用域',
 ActorUserId binary(16) NOT NULL COMMENT '实际操作人',
 OperationKey varchar(24) COLLATE utf8mb4_bin NOT NULL COMMENT 'propose或accept或revoke',
 IdempotencyKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '本授权内操作人的请求幂等键',
 RequestHash varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '包含动作及修订的规范请求摘要',
 ResponseJson longtext NOT NULL COMMENT '首次成功响应原文；重放不读取后续状态',
 ResultRevision bigint NOT NULL COMMENT '首次成功响应修订',
 ResultStatusKey varchar(24) COLLATE utf8mb4_bin NOT NULL COMMENT '首次成功响应状态',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '收据提交时间',
 CONSTRAINT pk_fn_workflow_collaboration_grant_action PRIMARY KEY (Id)
) COMMENT='工作流授权同意撤销审计及稳定幂等响应' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_grant_ensure;
DELIMITER $$
CREATE PROCEDURE fn_workflow_collaboration_grant_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant' AND INDEX_NAME='ux_fn_workflow_collaboration_grant_proposal') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_grant_proposal ON fn_workflow_collaboration_grant(SourceTenantScopeKey,SourceActorId,ProposalKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant' AND INDEX_NAME='ix_fn_workflow_collaboration_grant_source') THEN
  CREATE INDEX ix_fn_workflow_collaboration_grant_source ON fn_workflow_collaboration_grant(SourceTenantScopeKey,SourceDefinitionId,CreatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant' AND INDEX_NAME='ix_fn_workflow_collaboration_grant_target') THEN
  CREATE INDEX ix_fn_workflow_collaboration_grant_target ON fn_workflow_collaboration_grant(TargetTenantScopeKey,StatusKey,CreatedAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant_action' AND INDEX_NAME='ux_fn_workflow_collaboration_grant_action_key') THEN
  CREATE UNIQUE INDEX ux_fn_workflow_collaboration_grant_action_key ON fn_workflow_collaboration_grant_action(GrantId,ActorTenantScopeKey,ActorUserId,IdempotencyKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_grant_definition') THEN
  ALTER TABLE fn_workflow_collaboration_grant ADD CONSTRAINT fk_fn_workflow_collaboration_grant_definition FOREIGN KEY (SourceDefinitionId) REFERENCES fn_workflow_definition(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_grant_form') THEN
  ALTER TABLE fn_workflow_collaboration_grant ADD CONSTRAINT fk_fn_workflow_collaboration_grant_form FOREIGN KEY (SourceFormVersionId) REFERENCES fn_workflow_form_version(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_grant_target') THEN
  ALTER TABLE fn_workflow_collaboration_grant ADD CONSTRAINT fk_fn_workflow_collaboration_grant_target FOREIGN KEY (TargetDefinitionVersionId) REFERENCES fn_workflow_definition_version(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='fk_fn_workflow_collaboration_grant_action_grant') THEN
  ALTER TABLE fn_workflow_collaboration_grant_action ADD CONSTRAINT fk_fn_workflow_collaboration_grant_action_grant FOREIGN KEY (GrantId) REFERENCES fn_workflow_collaboration_grant(Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_grant_scope' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_grant ADD CONSTRAINT ck_fn_workflow_collaboration_grant_scope CHECK (SourceTenantScopeKey<>TargetTenantScopeKey AND (SourceTenantId IS NULL OR TargetTenantId IS NULL OR SourceTenantId<>TargetTenantId) AND ((SourceTenantId IS NULL AND SourceTenantScopeKey='host') OR (SourceTenantId IS NOT NULL AND SourceTenantScopeKey LIKE 'tenant:%')) AND ((TargetTenantId IS NULL AND TargetTenantScopeKey='host') OR (TargetTenantId IS NOT NULL AND TargetTenantScopeKey LIKE 'tenant:%')));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_grant_state' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_grant ADD CONSTRAINT ck_fn_workflow_collaboration_grant_state CHECK (((StatusKey='proposed' AND Revision=1 AND TargetActorId IS NULL AND TargetDefinitionVersionId IS NULL AND AcceptanceJson IS NULL AND PolicyHash IS NULL AND TargetSummaryJson IS NULL AND TargetSummaryHash IS NULL AND AcceptedAtUtc IS NULL AND RevokedAtUtc IS NULL AND RevokedById IS NULL AND RevokedByScopeKey IS NULL AND RevocationReason IS NULL) OR (StatusKey='active' AND Revision=2 AND TargetActorId IS NOT NULL AND TargetDefinitionVersionId IS NOT NULL AND AcceptanceJson IS NOT NULL AND PolicyHash IS NOT NULL AND TargetSummaryJson IS NOT NULL AND TargetSummaryHash IS NOT NULL AND AcceptedAtUtc IS NOT NULL AND RevokedAtUtc IS NULL AND RevokedById IS NULL AND RevokedByScopeKey IS NULL AND RevocationReason IS NULL) OR (StatusKey='revoked' AND RevokedAtUtc IS NOT NULL AND RevokedById IS NOT NULL AND RevokedByScopeKey IN (SourceTenantScopeKey,TargetTenantScopeKey) AND RevocationReason IS NOT NULL AND ((Revision=2 AND TargetActorId IS NULL AND TargetDefinitionVersionId IS NULL AND AcceptanceJson IS NULL AND PolicyHash IS NULL AND TargetSummaryJson IS NULL AND TargetSummaryHash IS NULL AND AcceptedAtUtc IS NULL) OR (Revision=3 AND TargetActorId IS NOT NULL AND TargetDefinitionVersionId IS NOT NULL AND AcceptanceJson IS NOT NULL AND PolicyHash IS NOT NULL AND TargetSummaryJson IS NOT NULL AND TargetSummaryHash IS NOT NULL AND AcceptedAtUtc IS NOT NULL)))));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_grant_expiry' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_grant ADD CONSTRAINT ck_fn_workflow_collaboration_grant_expiry CHECK (ExpiresAtUtc>CreatedAtUtc AND (AcceptedAtUtc IS NULL OR (AcceptedAtUtc>=CreatedAtUtc AND AcceptedAtUtc<ExpiresAtUtc)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_collaboration_grant_action' AND CONSTRAINT_NAME='ck_fn_workflow_collaboration_grant_action_state' AND CONSTRAINT_TYPE='CHECK') THEN
  ALTER TABLE fn_workflow_collaboration_grant_action ADD CONSTRAINT ck_fn_workflow_collaboration_grant_action_state CHECK (OperationKey IN ('propose','accept','revoke') AND ResultRevision>0 AND ResultStatusKey IN ('proposed','active','revoked'));
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_collaboration_grant_ensure();
DROP PROCEDURE IF EXISTS fn_workflow_collaboration_grant_ensure;
