-- 247：只增加可靠提交日志；单个建表 DDL 包含全部约束，重入不修改存量业务。
CREATE TABLE IF NOT EXISTS demo_enterprise_request_approval_submission (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '所属租户标识',
    RequestId BINARY(16) NOT NULL COMMENT '所属企业申请标识',
    RequestVersion bigint NOT NULL COMMENT '提交时固定的申请版本',
    WorkflowDefinitionVersionId BINARY(16) NOT NULL COMMENT '固定流程定义版本，不建立跨模块外键',
    WorkflowInstanceId BINARY(16) NOT NULL COMMENT '预分配的流程实例标识，不建立跨模块外键',
    SubmittedById BINARY(16) NOT NULL COMMENT '提交人标识',
    OrganizationUnitId BINARY(16) NOT NULL COMMENT '提交时固定的机构单元标识',
    BusinessTitle varchar(200) NOT NULL COMMENT '提交时固定的业务标题',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    StartedAtUtc datetime(6) NULL COMMENT '流程启动回执时间(UTC)',
    FinalStatus varchar(32) NULL COMMENT '已封存的审批终态',
    CompletedAtUtc datetime(6) NULL COMMENT '终态回写时间(UTC)',
    LastMessageId BINARY(16) NULL COMMENT '终态事件消息标识',
    CONSTRAINT PK_demo_enterprise_request_approval_submission PRIMARY KEY (Id),
    CONSTRAINT UX_demo_enterprise_request_approval_submission_Tenant_Request UNIQUE (TenantId, RequestId),
    CONSTRAINT UX_demo_enterprise_request_approval_submission_Instance UNIQUE (WorkflowInstanceId)
) COMMENT='企业申请可靠审批提交日志' ENGINE=InnoDB;
