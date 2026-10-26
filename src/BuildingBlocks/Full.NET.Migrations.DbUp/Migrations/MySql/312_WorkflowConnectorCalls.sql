-- 312：每张表原子创建，索引与系统审计约束按现存结构恢复。
CREATE TABLE IF NOT EXISTS fn_workflow_connector_call (
  Id binary(16) NOT NULL COMMENT '复用连接器步骤的UUID v7激活标识及稳定业务幂等键',
  InstanceId binary(16) NOT NULL COMMENT '所属Workflow实例标识',
  TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '激活时绑定的可信租户作用域',
  NodeKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '不可变定义中的连接器节点键',
  ContinuationNodeKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '结果接纳后才推进的固定后继节点键',
  ConnectorKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '部署侧固定连接器键',
  ConnectorVersion int NOT NULL COMMENT '激活绑定的连接器配置版本',
  ConnectorHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定连接器完整配置的SHA256摘要',
  ModeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'sync同步等待或async耐久分发',
  InputJson longtext NOT NULL COMMENT '激活不可变表单输入快照',
  InputHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '原表单输入的SHA256摘要',
  InputFormRevision bigint NOT NULL COMMENT '固定输入所属主表单或分支修订号',
  RequestJson longtext NOT NULL COMMENT '按发布映射形成的最小请求体，不含凭据',
  RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '固定业务请求体的SHA256摘要',
  ParallelJoinId binary(16) NULL COMMENT '激活所属分叉汇合身份',
  ParallelBranchKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '激活所属稳定分支键',
  StatusKey varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '分发、结果等待、接纳、拒绝、未知或取消状态',
  CurrentAttemptId binary(16) NULL COMMENT '当前HTTP尝试身份，读取时必须同时匹配本激活',
  SelectedAttemptId binary(16) NULL COMMENT '被接纳的固定响应尝试身份',
  LeaseGeneration bigint NOT NULL COMMENT 'HTTP分发或结果接纳的租约世代',
  LeaseOwnerKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '当前Worker租约所有者',
  LeaseExpiresAtUtc datetime(6) NULL COMMENT '当前租约UTC截止时间',
  AttemptCount int NOT NULL COMMENT '已持久建立的HTTP尝试次数',
  ApplyAttemptCount int NOT NULL COMMENT '结果接纳准备的有界尝试次数',
  LastErrorKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '激活当前稳定错误码，不含异常或凭据原文',
  DueAtUtc datetime(6) NOT NULL COMMENT '首次分发或退避后下一次可执行时间',
  CancelRequestedAtUtc datetime(6) NULL COMMENT '取消请求时间，已在途副作用仍保留对账事实',
  Revision bigint NOT NULL COMMENT '激活当前状态的并发裁决修订号',
  CreatedAtUtc datetime(6) NOT NULL COMMENT '连接器激活创建时间',
  CompletedAtUtc datetime(6) NULL COMMENT '当前激活收敛时间',
  AppliedFormRevision bigint NULL COMMENT '结果接纳后的主表单或分支修订号',
  CONSTRAINT pk_fn_workflow_connector_call PRIMARY KEY (Id),
  CONSTRAINT fk_fn_workflow_connector_call_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
  CONSTRAINT fk_fn_workflow_connector_call_step FOREIGN KEY (Id) REFERENCES fn_workflow_step(Id),
  CONSTRAINT ck_fn_workflow_connector_call_state CHECK (StatusKey IN ('pending','dispatched','retryable','result_ready','applying','applied','outcome_unknown','failed','rejected','cancelled') AND ModeKey IN ('sync','async') AND ConnectorVersion>0 AND InputFormRevision>0 AND LeaseGeneration>=0 AND AttemptCount BETWEEN 0 AND 5 AND Revision>0),
  CONSTRAINT ck_fn_workflow_connector_call_lease CHECK ((StatusKey IN ('dispatched','applying') AND LeaseOwnerKey IS NOT NULL AND LeaseExpiresAtUtc IS NOT NULL) OR (StatusKey NOT IN ('dispatched','applying') AND LeaseOwnerKey IS NULL AND LeaseExpiresAtUtc IS NULL))
) COMMENT='工作流受控连接器激活与当前分发接纳状态' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_workflow_connector_attempt (
  Id binary(16) NOT NULL COMMENT '本次HTTP尝试UUID v7标识',
  CallId binary(16) NOT NULL COMMENT '本次尝试所属固定连接器激活',
  TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '原激活可信租户作用域',
  LeaseGeneration bigint NOT NULL COMMENT '发起HTTP时固定的租约世代',
  LeaseOwnerKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '发起HTTP的固定Worker身份',
  RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '本次尝试固定请求摘要',
  StatusKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'inflight在途或received已收到结果',
  OutcomeKey varchar(24) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '确定成功、拒绝、失败、可重试或未知结果',
  ResponseJson longtext NULL COMMENT '有界原始响应，仅归本次尝试且不进入普通日志',
  ResponseHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '有界响应的SHA256摘要',
  HttpStatusCode int NULL COMMENT '实际收到的HTTP状态码',
  ErrorKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '无凭据和异常原文的稳定错误机器码',
  StartedAtUtc datetime(6) NOT NULL COMMENT '本次外部调用持久建立时间',
  CompletedAtUtc datetime(6) NULL COMMENT '收到本次结果的时间，可能晚于租约或实例取消',
  CONSTRAINT pk_fn_workflow_connector_attempt PRIMARY KEY (Id),
  CONSTRAINT fk_fn_workflow_connector_attempt_call FOREIGN KEY (CallId) REFERENCES fn_workflow_connector_call(Id),
  CONSTRAINT uq_fn_workflow_connector_attempt_lease UNIQUE (CallId,LeaseGeneration),
  CONSTRAINT ck_fn_workflow_connector_attempt_state CHECK (LeaseGeneration>0 AND ((StatusKey='inflight' AND OutcomeKey IS NULL AND CompletedAtUtc IS NULL) OR (StatusKey='received' AND OutcomeKey IN ('succeeded','rejected','failed','retryable','outcome_unknown') AND CompletedAtUtc IS NOT NULL)))
) COMMENT='工作流连接器每次外部调用的不可变请求与迟到响应事实' ENGINE=InnoDB;
DROP PROCEDURE IF EXISTS fn_workflow_restore_connector_312;
DELIMITER $$
CREATE PROCEDURE fn_workflow_restore_connector_312()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_connector_call' AND INDEX_NAME='ix_fn_workflow_connector_call_due') THEN
  CREATE INDEX ix_fn_workflow_connector_call_due ON fn_workflow_connector_call(StatusKey,DueAtUtc,Id);
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_workflow_connector_call' AND INDEX_NAME='ix_fn_workflow_connector_call_instance') THEN
  CREATE INDEX ix_fn_workflow_connector_call_instance ON fn_workflow_connector_call(TenantScopeKey,InstanceId,CreatedAtUtc,Id);
 END IF;
END$$
DELIMITER ;
CALL fn_workflow_restore_connector_312();
DROP PROCEDURE IF EXISTS fn_workflow_restore_connector_312;
CREATE TABLE IF NOT EXISTS fn_workflow_connector_resolution (
  Id binary(16) NOT NULL COMMENT '对账操作UUID v7标识',
  CallId binary(16) NOT NULL COMMENT '被对账的原连接器激活',
  InstanceId binary(16) NOT NULL COMMENT '原激活所属实例',
  TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '可信租户作用域',
  ActorUserId binary(16) NOT NULL COMMENT '具有精确对账权限的真实操作人',
  ActionKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '闭合对账动作键',
  AttemptId binary(16) NULL COMMENT '接纳的真实原尝试标识',
  EvidenceReference varchar(256) CHARACTER SET utf8mb4 NULL COMMENT '人工核对的凭据或工单引用',
  Reason varchar(512) CHARACTER SET utf8mb4 NOT NULL COMMENT '操作人明确填写的对账理由',
  IdempotencyKey varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '同活动同操作人的语义幂等键',
  RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '规范对账请求SHA256摘要',
  ResponseJson longtext NOT NULL COMMENT '首次已提交对账响应快照',
  CreatedAtUtc datetime(6) NOT NULL COMMENT '首次对账提交时间',
  CONSTRAINT pk_fn_workflow_connector_resolution PRIMARY KEY (Id),
  CONSTRAINT fk_fn_workflow_connector_resolution_call FOREIGN KEY (CallId) REFERENCES fn_workflow_connector_call(Id),
  CONSTRAINT fk_fn_workflow_connector_resolution_instance FOREIGN KEY (InstanceId) REFERENCES fn_workflow_instance(Id),
  CONSTRAINT uq_fn_workflow_connector_resolution_request UNIQUE (TenantScopeKey,CallId,ActorUserId,IdempotencyKey)
) COMMENT='工作流连接器人工对账证据与不可变首次操作回执' ENGINE=InnoDB;
