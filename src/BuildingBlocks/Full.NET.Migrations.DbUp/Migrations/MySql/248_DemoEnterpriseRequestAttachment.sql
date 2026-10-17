-- 248：单次建表含完整索引及注释；未记账重入保留既有引用和字节数。
CREATE TABLE IF NOT EXISTS demo_enterprise_request_request_attachment (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NOT NULL COMMENT '所属租户标识',
    RequestId BINARY(16) NOT NULL COMMENT '所属申请标识',
    FileId BINARY(16) NOT NULL COMMENT '文件标识',
    OriginalFileName varchar(255) NOT NULL COMMENT '下载文件名',
    SizeBytes bigint NOT NULL COMMENT '文件字节数',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CreatedById BINARY(16) NOT NULL COMMENT '创建人标识',
    StateKey varchar(16) NOT NULL COMMENT '附件绑定状态键',
    UploadExpiresAtUtc datetime(6) NOT NULL COMMENT '上传保护过期时间(UTC)',
    CONSTRAINT PK_demo_enterprise_request_request_attachment PRIMARY KEY (Id),
    KEY IX_demo_enterprise_request_request_attachment_Resource (TenantId, RequestId, CreatedAtUtc, Id),
    UNIQUE KEY UX_demo_enterprise_request_request_attachment_FileId (FileId)
) COMMENT='企业申请附件引用表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
