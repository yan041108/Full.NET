-- 154：平台授权备份执行器任务目录与运行结果表。

CREATE TABLE IF NOT EXISTS fn_platform_backup_task (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TaskKey varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '任务键',
    DisplayName varchar(200) NOT NULL COMMENT '展示名称',
    Description varchar(1000) NULL COMMENT '说明',
    DatabaseProvider varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '数据库提供程序',
    IsEnabled tinyint(1) NOT NULL DEFAULT 1 COMMENT '是否启用',
    SortOrder int NOT NULL DEFAULT 0 COMMENT '排序',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    UpdatedAtUtc datetime(6) NULL COMMENT '更新时间(UTC)',
    CONSTRAINT PK_fn_platform_backup_task PRIMARY KEY (Id),
    CONSTRAINT UX_fn_platform_backup_task_TaskKey UNIQUE (TaskKey),
    CONSTRAINT CK_fn_platform_backup_task_DatabaseProvider
        CHECK (DatabaseProvider IN ('sql_server', 'mysql', 'all')),
    KEY IX_fn_platform_backup_task_SortOrder (SortOrder, TaskKey)
) COMMENT='平台授权备份任务目录表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS fn_platform_backup_run (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    TaskId BINARY(16) NOT NULL COMMENT '任务标识',
    Status varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '运行状态',
    StartedAtUtc datetime(6) NOT NULL COMMENT '开始时间(UTC)',
    CompletedAtUtc datetime(6) NULL COMMENT '完成时间(UTC)',
    ArtifactFileName varchar(260) NULL COMMENT '产物文件名',
    ArtifactSizeBytes bigint NULL COMMENT '产物大小(字节)',
    ArtifactContentType varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '产物内容类型',
    SummaryMessage varchar(2000) NULL COMMENT '摘要或错误信息',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_platform_backup_run PRIMARY KEY (Id),
    CONSTRAINT FK_fn_platform_backup_run_Task
        FOREIGN KEY (TaskId) REFERENCES fn_platform_backup_task (Id),
    CONSTRAINT CK_fn_platform_backup_run_Status
        CHECK (Status IN ('pending', 'running', 'succeeded', 'failed')),
    KEY IX_fn_platform_backup_run_TaskId_StartedAtUtc (TaskId, StartedAtUtc DESC, Id)
) COMMENT='平台授权备份运行结果表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

INSERT INTO fn_platform_backup_task
    (Id, TaskKey, DisplayName, Description, DatabaseProvider, IsEnabled, SortOrder, CreatedAtUtc)
SELECT UUID_TO_BIN('01954f00-0001-7000-8000-000000000001', 0),
       'sql-server-full',
       'SQL Server 全库备份',
       '由授权备份执行器在受控凭据与对象存储前置条件下执行的 SQL Server 全库备份任务。',
       'sql_server',
       1,
       10,
       UTC_TIMESTAMP(6)
WHERE NOT EXISTS (
    SELECT 1 FROM fn_platform_backup_task WHERE TaskKey = 'sql-server-full'
);

INSERT INTO fn_platform_backup_task
    (Id, TaskKey, DisplayName, Description, DatabaseProvider, IsEnabled, SortOrder, CreatedAtUtc)
SELECT UUID_TO_BIN('01954f00-0001-7000-8000-000000000002', 0),
       'mysql-full',
       'MySQL 全库备份',
       '由授权备份执行器在受控凭据与对象存储前置条件下执行的 MySQL 全库备份任务。',
       'mysql',
       1,
       20,
       UTC_TIMESTAMP(6)
WHERE NOT EXISTS (
    SELECT 1 FROM fn_platform_backup_task WHERE TaskKey = 'mysql-full'
);
