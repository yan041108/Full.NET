-- 197：K3Cloud 连接配置与单据同步表。

CREATE TABLE IF NOT EXISTS fn_k3cloud_connection_config (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    Name varchar(128) NOT NULL COMMENT '名称',
    BaseUrl varchar(512) NOT NULL COMMENT '基础地址',
    AcctId varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '账套标识',
    Username varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '用户名',
    PasswordProtected longtext NOT NULL COMMENT '受保护的密码',
    Lcid int NOT NULL DEFAULT 2052 COMMENT '区域语言标识',
    IsDefault tinyint(1) NOT NULL DEFAULT 0 COMMENT '是否默认',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    LastTestedAtUtc datetime(6) NULL COMMENT 'Last Tested At(UTC)',
    LastTestStatusKey varchar(32) COLLATE utf8mb4_bin NULL COMMENT '最近一次探测状态键',
    LastTestMessage varchar(512) NULL COMMENT '最近一次探测消息',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id)
) COMMENT='金蝶云星空连接配置表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_k3cloud_document_sync (
    Id char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '逻辑主键',
    ConnectionConfigId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '连接配置标识',
    DocumentTypeKey varchar(64) COLLATE utf8mb4_bin NOT NULL COMMENT '单据类型键',
    BusinessKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '业务键',
    PayloadJson longtext NOT NULL COMMENT 'Payload(JSON)',
    StatusKey varchar(32) COLLATE utf8mb4_bin NOT NULL COMMENT '状态键',
    LastStepKey varchar(16) COLLATE utf8mb4_bin NULL COMMENT '最近步骤键',
    ExternalBillId varchar(64) COLLATE utf8mb4_bin NULL COMMENT '外部单据标识',
    ExternalBillNo varchar(64) COLLATE utf8mb4_bin NULL COMMENT '外部单据编号',
    LastErrorCode varchar(64) COLLATE utf8mb4_bin NULL COMMENT '最后错误码',
    LastErrorMessage varchar(512) NULL COMMENT '最近一次错误消息',
    SubmittedAtUtc datetime(6) NULL COMMENT 'Submitted At(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CreatedByUserId char(36) COLLATE utf8mb4_bin NOT NULL COMMENT '创建人用户标识',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    PRIMARY KEY (Id),
    CONSTRAINT CK_fn_k3cloud_document_sync_StatusKey
        CHECK (StatusKey IN (
            'pending',
            'save_succeeded',
            'submitted',
            'save_failed',
            'submit_failed'))
) COMMENT='金蝶云星空单据同步表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE UNIQUE INDEX UX_fn_k3cloud_document_sync_Connection_BusinessKey
    ON fn_k3cloud_document_sync (ConnectionConfigId, DocumentTypeKey, BusinessKey);

CREATE INDEX IX_fn_k3cloud_document_sync_StatusKey
    ON fn_k3cloud_document_sync (StatusKey, CreatedAtUtc, Id);
