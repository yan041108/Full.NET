-- 316：规则写入与审批命令凭据同事务；取消墓碑不携带虚构应用修订。
CREATE TABLE IF NOT EXISTS fn_serialnumbers_approval_cmd (
 Id binary(16) NOT NULL COMMENT '应用端UUID v7命令身份；重投复用同一标识',
 ApprovalRequestId binary(16) NOT NULL COMMENT 'DataApproval请求关联快照；不建立跨模块外键',
 RuleId binary(16) NOT NULL COMMENT 'SerialNumbers拥有的目标规则标识',
 RequestedByUserId binary(16) NOT NULL COMMENT '原始审批授权主体；不代表个人再次执行审批',
 ScenarioKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '固定审批场景键',
 InputHash char(64) COLLATE utf8mb4_bin NOT NULL COMMENT '规范化授权输入的SHA256摘要',
 BeforeSnapshotJson longtext NULL COMMENT '本次命令应用前的所有者规则快照',
 StatusKey varchar(16) COLLATE utf8mb4_bin NOT NULL COMMENT 'applied已应用、cancelled无效果取消、compensated已补偿',
 AppliedVersion bigint NULL COMMENT '规则实际应用后的修订；无效果取消为空',
 CompensatedVersion bigint NULL COMMENT '规则实际补偿后的修订；未补偿为空',
 Revision bigint NOT NULL COMMENT '凭据乐观并发修订',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '命令凭据创建UTC微秒时间',
 UpdatedAtUtc datetime(6) NOT NULL COMMENT '凭据最近变更UTC微秒时间',
 CONSTRAINT pk_fn_serialnumbers_approval_cmd PRIMARY KEY (Id)
) COMMENT='流水号审批命令的应用及精确补偿凭据' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_serialnumbers_ensure_approval_cmd;
DELIMITER $$
CREATE PROCEDURE fn_serialnumbers_ensure_approval_cmd()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_serialnumbers_approval_cmd' AND CONSTRAINT_NAME='ck_fn_serialnumbers_approval_cmd_state') THEN
  ALTER TABLE fn_serialnumbers_approval_cmd ADD CONSTRAINT ck_fn_serialnumbers_approval_cmd_state CHECK (Revision>0 AND ScenarioKey IN ('serial_numbers.host_rule.update','serial_numbers.host_rule.disable') AND ((StatusKey='cancelled' AND BeforeSnapshotJson IS NULL AND AppliedVersion IS NULL AND CompensatedVersion IS NULL) OR (StatusKey='applied' AND BeforeSnapshotJson IS NOT NULL AND AppliedVersion IS NOT NULL AND AppliedVersion>0 AND CompensatedVersion IS NULL) OR (StatusKey='compensated' AND BeforeSnapshotJson IS NOT NULL AND AppliedVersion IS NOT NULL AND AppliedVersion>0 AND CompensatedVersion IS NOT NULL AND CompensatedVersion>AppliedVersion)));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_serialnumbers_approval_cmd' AND CONSTRAINT_NAME='ck_fn_serialnumbers_approval_cmd_snapshot') THEN
  ALTER TABLE fn_serialnumbers_approval_cmd ADD CONSTRAINT ck_fn_serialnumbers_approval_cmd_snapshot CHECK (BeforeSnapshotJson IS NULL OR JSON_VALID(BeforeSnapshotJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_serialnumbers_approval_cmd' AND INDEX_NAME='ix_fn_serialnumbers_approval_cmd_rule') THEN
  CREATE INDEX ix_fn_serialnumbers_approval_cmd_rule ON fn_serialnumbers_approval_cmd(RuleId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_serialnumbers_approval_cmd' AND CONSTRAINT_NAME='fk_fn_serialnumbers_approval_cmd_rule') THEN
  ALTER TABLE fn_serialnumbers_approval_cmd ADD CONSTRAINT fk_fn_serialnumbers_approval_cmd_rule FOREIGN KEY (RuleId) REFERENCES fn_serialnumbers_rule(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_serialnumbers_ensure_approval_cmd();
DROP PROCEDURE IF EXISTS fn_serialnumbers_ensure_approval_cmd;
