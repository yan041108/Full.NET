-- 249：恢复记录和申请本地事务共同提交，保留操作者、原因与权威流程状态。
CREATE TABLE IF NOT EXISTS demo_enterprise_request_approval_repair (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键及对账逻辑消息标识',
    TenantId BINARY(16) NOT NULL COMMENT '可信租户标识',
    RequestId BINARY(16) NOT NULL COMMENT '申请标识',
    WorkflowInstanceId BINARY(16) NOT NULL COMMENT '已核对原启动回执的流程实例标识',
    RequestVersion bigint NOT NULL COMMENT '恢复前申请版本',
    KindKey varchar(32) NOT NULL COMMENT 'binding补绑定、start_receipt补启动回执或reconcile终态对账',
    WorkflowStatusKey varchar(32) NOT NULL COMMENT '读取时的权威流程状态',
    ActorUserId BINARY(16) NOT NULL COMMENT '恢复操作者标识，与原发起人区分',
    Reason varchar(500) NOT NULL COMMENT '操作者提供的恢复原因',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '恢复事务记录时间(UTC)',
    CONSTRAINT PK_demo_enterprise_request_approval_repair PRIMARY KEY (Id),
    CONSTRAINT UX_demo_enterprise_request_approval_repair_Request_Version_Kind UNIQUE (TenantId, RequestId, RequestVersion, KindKey)
) COMMENT='企业申请受控审批恢复记录' ENGINE=InnoDB;
-- 恢复未记账脚本时也修复缺失索引，不修改已有恢复记录。
SET @fn_approval_repair_index = (
    SELECT IF(COUNT(*) = 0,
        'CREATE UNIQUE INDEX UX_demo_enterprise_request_approval_repair_Request_Version_Kind ON demo_enterprise_request_approval_repair (TenantId, RequestId, RequestVersion, KindKey)',
        'SELECT 1')
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'demo_enterprise_request_approval_repair'
      AND INDEX_NAME = 'UX_demo_enterprise_request_approval_repair_Request_Version_Kind'
);
PREPARE fn_approval_repair_index_stmt FROM @fn_approval_repair_index;
EXECUTE fn_approval_repair_index_stmt;
DEALLOCATE PREPARE fn_approval_repair_index_stmt;
