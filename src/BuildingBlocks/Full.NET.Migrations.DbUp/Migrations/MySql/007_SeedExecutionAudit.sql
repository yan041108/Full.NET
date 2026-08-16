CREATE TABLE IF NOT EXISTS fn_seed_run (
    Id char(36) NOT NULL COMMENT '逻辑主键',
    Profile varchar(16) NOT NULL COMMENT '种子配置档',
    EnvironmentName varchar(64) NOT NULL COMMENT '环境名称',
    Status varchar(16) NOT NULL COMMENT '状态',
    ApplicationVersion varchar(64) NOT NULL COMMENT '应用版本',
    CorrelationId varchar(64) NOT NULL COMMENT '关联标识',
    StartedAt datetime(6) NOT NULL COMMENT '开始时间',
    CompletedAt datetime(6) NULL COMMENT '完成时间',
    ErrorCode varchar(128) NULL COMMENT '错误码',
    CONSTRAINT PK_fn_seed_run PRIMARY KEY (Id),
    CONSTRAINT CK_fn_seed_run_Status
        CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled')),
    KEY IX_fn_seed_run_StartedAt (StartedAt)
) COMMENT='种子数据执行运行记录' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_seed_run_item (
    RunId char(36) NOT NULL COMMENT '运行标识',
    Contributor varchar(128) NOT NULL COMMENT '贡献者名称',
    ContributorVersion int NOT NULL COMMENT '贡献者版本',
    Status varchar(16) NOT NULL COMMENT '状态',
    CreatedCount int NOT NULL COMMENT '新建数量',
    UpdatedCount int NOT NULL COMMENT '更新数量',
    SkippedCount int NOT NULL COMMENT '跳过数量',
    StartedAt datetime(6) NOT NULL COMMENT '开始时间',
    CompletedAt datetime(6) NULL COMMENT '完成时间',
    ErrorCode varchar(128) NULL COMMENT '错误码',
    CONSTRAINT PK_fn_seed_run_item PRIMARY KEY (RunId, Contributor),
    CONSTRAINT FK_fn_seed_run_item_Run
        FOREIGN KEY (RunId) REFERENCES fn_seed_run(Id),
    CONSTRAINT CK_fn_seed_run_item_Status
        CHECK (Status IN ('Running', 'Succeeded', 'Failed', 'Cancelled')),
    CONSTRAINT CK_fn_seed_run_item_Counts
        CHECK (CreatedCount >= 0 AND UpdatedCount >= 0 AND SkippedCount >= 0)
) COMMENT='种子数据贡献者执行明细' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
