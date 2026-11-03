-- 申请单导入回执与业务行同事务提交；单条原子 DDL 可在迁移未记账时重跑。
-- 本表由示例应用拥有，不引用 ImportExport 表；保留回执以阻止检查点丢失后的重复写入。
CREATE TABLE IF NOT EXISTS demo_enterprise_request_import_row (
    Id BINARY(16) NOT NULL COMMENT '应用生成的 UUID v7 回执标识',
    TenantId BINARY(16) NOT NULL COMMENT '可信租户标识',
    TaskId BINARY(16) NOT NULL COMMENT '调度方持久化导入任务标识',
    LineNumber int NOT NULL COMMENT '源工作簿原始行号',
    Digest char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '绑定源文件、主体和行内容的 SHA256 摘要',
    RequestId BINARY(16) NOT NULL COMMENT '同事务创建的业务申请标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '回执提交时间',
    CONSTRAINT PK_demo_enterprise_request_import_row PRIMARY KEY (Id),
    CONSTRAINT UX_demo_enterprise_request_import_row_TenantId_TaskId_LineNumber UNIQUE (TenantId,TaskId,LineNumber)
) COMMENT='企业申请导入行幂等回执' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
