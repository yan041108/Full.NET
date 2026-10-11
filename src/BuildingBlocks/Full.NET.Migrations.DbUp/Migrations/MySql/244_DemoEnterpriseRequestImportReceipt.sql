-- 244：企业申请导入回执与企业申请写入同事务，唯一任务/行键防止崩溃重放重复写入。只增加表，不修改存量企业申请。
CREATE TABLE IF NOT EXISTS demo_enterprise_request_import_receipt (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '所属租户标识',
    TaskId BINARY(16) NOT NULL COMMENT '导入任务关联标识，不建立跨模块外键',
    LineNumber int NOT NULL COMMENT '原始工作簿行号，从一开始',
    PayloadHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '不可变导入行载荷 SHA256 摘要',
    EntityId BINARY(16) NULL COMMENT '已完成的企业申请标识，占位与业务写入同事务',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_demo_enterprise_request_import_receipt PRIMARY KEY (Id),
    CONSTRAINT UX_demo_enterprise_request_import_receipt_Tenant_Task_Line UNIQUE (TenantId, TaskId, LineNumber)
) COMMENT='企业申请导入幂等回执' ENGINE=InnoDB;
