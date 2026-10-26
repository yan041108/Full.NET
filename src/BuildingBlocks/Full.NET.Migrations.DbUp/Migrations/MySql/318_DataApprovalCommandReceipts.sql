-- 318：审批节点所有者效果回执及结果Outbox提交凭据；重跑分别探测约束、索引与外键。
CREATE TABLE IF NOT EXISTS fn_dataapproval_cmd_receipt (
 Id binary(16) NOT NULL COMMENT '应用端UUID v7回执标识',
 RequestId binary(16) NOT NULL COMMENT '本模块拥有的审批请求',
 InstanceId binary(16) NOT NULL COMMENT '可信工作流根关联；无跨模块外键',
 TenantScopeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '审批请求可信作用域',
 CallId binary(16) NOT NULL COMMENT '真实Workflow节点调用标识',
 PhaseKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT 'execute执行或compensate补偿',
 Generation int NOT NULL COMMENT '固定协议请求代次',
 InputHash char(64) COLLATE utf8mb4_bin NOT NULL COMMENT 'Workflow绑定输入摘要',
 OutcomeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '实际所有者应用、取消、补偿或失败结果',
 ResultJson longtext NOT NULL COMMENT '最小回执；不含规则快照',
 CreatedAtUtc datetime(6) NOT NULL COMMENT '本地回执及Outbox提交UTC时间',
 CONSTRAINT pk_fn_dataapproval_cmd_receipt PRIMARY KEY (Id)
) COMMENT='审批节点所有者效果回执及结果Outbox提交凭据' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
DROP PROCEDURE IF EXISTS fn_dataapproval_cmd_receipt_ensure;
DELIMITER $$
CREATE PROCEDURE fn_dataapproval_cmd_receipt_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_dataapproval_cmd_receipt' AND CONSTRAINT_NAME='ck_fn_dataapproval_cmd_receipt_state') THEN
  ALTER TABLE fn_dataapproval_cmd_receipt ADD CONSTRAINT ck_fn_dataapproval_cmd_receipt_state CHECK (Generation BETWEEN 1 AND 1000 AND PhaseKey IN ('execute','compensate') AND OutcomeKey IN ('applied','cancelled','compensated','declined','unknown'));
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_dataapproval_cmd_receipt' AND CONSTRAINT_NAME='ck_fn_dataapproval_cmd_receipt_resultjson') THEN
  ALTER TABLE fn_dataapproval_cmd_receipt ADD CONSTRAINT ck_fn_dataapproval_cmd_receipt_resultjson CHECK (ResultJson IS NULL OR JSON_VALID(ResultJson)=1);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_dataapproval_cmd_receipt' AND INDEX_NAME='ux_fn_dataapproval_cmd_receipt_call') THEN
  CREATE UNIQUE INDEX ux_fn_dataapproval_cmd_receipt_call ON fn_dataapproval_cmd_receipt(CallId,PhaseKey,Generation,TenantScopeKey);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_dataapproval_cmd_receipt' AND INDEX_NAME='ix_fn_dataapproval_cmd_receipt_request') THEN
  CREATE INDEX ix_fn_dataapproval_cmd_receipt_request ON fn_dataapproval_cmd_receipt(RequestId,InstanceId);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_dataapproval_cmd_receipt' AND CONSTRAINT_NAME='fk_fn_dataapproval_cmd_receipt_request') THEN
  ALTER TABLE fn_dataapproval_cmd_receipt ADD CONSTRAINT fk_fn_dataapproval_cmd_receipt_request FOREIGN KEY (RequestId) REFERENCES fn_dataapproval_request(Id);
 END IF;
END$$
DELIMITER ;
CALL fn_dataapproval_cmd_receipt_ensure();
DROP PROCEDURE IF EXISTS fn_dataapproval_cmd_receipt_ensure;
