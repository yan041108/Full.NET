-- 030：Host 作用域任务定义与执行记录。

IF OBJECT_ID(N'dbo.fn_jobs_definition', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_jobs_definition
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        JobKey varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(200) NOT NULL,
        Description nvarchar(500) NULL,
        IsEnabled bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedByUserId uniqueidentifier NULL,
        Version int NOT NULL
            CONSTRAINT DF_fn_jobs_definition_Version DEFAULT (1),
        CONSTRAINT PK_fn_jobs_definition PRIMARY KEY CLUSTERED (Id)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'后台任务定义表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'Description';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'DisplayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任务键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'JobKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_definition', @level2type=N'COLUMN', @level2name=N'Version';

    CREATE UNIQUE INDEX UX_fn_jobs_definition_JobKey
        ON dbo.fn_jobs_definition(JobKey)
        WHERE TenantId IS NULL;
END;

IF OBJECT_ID(N'dbo.fn_jobs_execution', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_jobs_execution
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        JobDefinitionId uniqueidentifier NOT NULL,
        Status varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        TriggerKind varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ErrorMessage nvarchar(2000) NULL,
        StartedAtUtc datetimeoffset(7) NULL,
        FinishedAtUtc datetimeoffset(7) NULL,
        LeaseId uniqueidentifier NULL,
        LeaseExpiresAtUtc datetimeoffset(7) NULL,
        AttemptCount int NOT NULL
            CONSTRAINT DF_fn_jobs_execution_AttemptCount DEFAULT (0),
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_jobs_execution PRIMARY KEY CLUSTERED (Id)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'后台任务执行记录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'尝试次数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'AttemptCount';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误消息', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'ErrorMessage';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结束时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'FinishedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'任务定义标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'JobDefinitionId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'LeaseExpiresAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'LeaseId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'Status';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'触发类型', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_execution', @level2type=N'COLUMN', @level2name=N'TriggerKind';

    CREATE INDEX IX_fn_jobs_execution_JobDefinitionCreatedAtUtc
        ON dbo.fn_jobs_execution(JobDefinitionId, CreatedAtUtc DESC, Id);

    CREATE INDEX IX_fn_jobs_execution_PendingLease
        ON dbo.fn_jobs_execution(Status, LeaseExpiresAtUtc, CreatedAtUtc)
        WHERE Status = 'pending';
END;
