-- 209：岗位导入回执与岗位写入同事务，唯一任务/行键防止崩溃重放重复写入。只增加表，不修改存量岗位。
CREATE TABLE IF NOT EXISTS fn_organization_position_import (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '所属租户标识',
    TaskId BINARY(16) NOT NULL COMMENT '导入任务关联标识，不建立跨模块外键',
    LineNumber int NOT NULL COMMENT '原始工作簿行号，从一开始',
    PayloadHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '不可变导入行载荷 SHA256 摘要',
    PositionId BINARY(16) NULL COMMENT '已完成的岗位标识，占位与业务写入同事务',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_organization_position_import PRIMARY KEY (Id),
    CONSTRAINT UX_fn_organization_position_import_Tenant_Task_Line UNIQUE (TenantId, TaskId, LineNumber)
) COMMENT='岗位导入幂等回执' ENGINE=InnoDB;
