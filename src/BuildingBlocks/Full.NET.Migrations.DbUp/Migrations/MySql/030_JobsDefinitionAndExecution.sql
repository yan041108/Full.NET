-- 030：Host 作用域任务定义与执行记录。

CREATE TABLE IF NOT EXISTS fn_jobs_definition (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    JobKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '任务键',
    DisplayName varchar(200) NOT NULL COMMENT '显示名称',
    Description varchar(500) NULL COMMENT '描述',
    IsEnabled tinyint(1) NOT NULL COMMENT '是否启用',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CreatedByUserId BINARY(16) NOT NULL COMMENT '创建人用户标识',
    UpdatedByUserId BINARY(16) NULL COMMENT '更新人用户标识',
    Version int NOT NULL DEFAULT 1 COMMENT '乐观并发版本号',
    CONSTRAINT PK_fn_jobs_definition PRIMARY KEY (Id),
    UNIQUE KEY UX_fn_jobs_definition_JobKey (JobKey)
) COMMENT='后台任务定义表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_jobs_execution (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
    JobDefinitionId BINARY(16) NOT NULL COMMENT '任务定义标识',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '状态',
    TriggerKind varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '触发类型',
    ErrorMessage varchar(2000) NULL COMMENT '错误消息',
    StartedAtUtc datetime(6) NULL COMMENT '开始时间(UTC)',
    FinishedAtUtc datetime(6) NULL COMMENT '结束时间(UTC)',
    LeaseId BINARY(16) NULL COMMENT '租约标识',
    LeaseExpiresAtUtc datetime(6) NULL COMMENT '租约过期时间(UTC)',
    AttemptCount int NOT NULL DEFAULT 0 COMMENT '尝试次数',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_jobs_execution PRIMARY KEY (Id),
    KEY IX_fn_jobs_execution_JobDefinitionCreatedAtUtc (JobDefinitionId, CreatedAtUtc, Id),
    KEY IX_fn_jobs_execution_PendingLease (Status, LeaseExpiresAtUtc, CreatedAtUtc)
) COMMENT='后台任务执行记录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
