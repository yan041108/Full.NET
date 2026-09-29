-- 239：为操作日志预留可空受限详情与绝对到期时间，并建立独立清理检查点；不回填历史记录。
-- SQL Server 的列添加与说明独立探测，允许 DbUp 未记账或部分完成后重跑；大表 DDL 锁需在发布窗口评估。
IF COL_LENGTH(N'dbo.fn_auditing_operation_log', N'ContextJson') IS NULL
    ALTER TABLE dbo.fn_auditing_operation_log ADD ContextJson nvarchar(max) NULL;

IF COL_LENGTH(N'dbo.fn_auditing_operation_log', N'DetailsExpiresAtUtc') IS NULL
    ALTER TABLE dbo.fn_auditing_operation_log ADD DetailsExpiresAtUtc datetimeoffset(7) NULL;

-- 新列创建后单独编译过滤索引，避免首次迁移时 SQL Server 在同一批次提前绑定列名。
GO

-- 仅索引有详情到期时间的行，避免旧摘要行占用索引空间；创建时可能短暂锁表。
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
      AND name = N'IX_fn_auditing_operation_log_DetailsExpiresAtUtc_Id'
)
    CREATE INDEX IX_fn_auditing_operation_log_DetailsExpiresAtUtc_Id
        ON dbo.fn_auditing_operation_log (DetailsExpiresAtUtc, Id)
        WHERE DetailsExpiresAtUtc IS NOT NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'ContextJson', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本化且受限的操作详情 JSON',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'ContextJson';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1
      AND major_id = OBJECT_ID(N'dbo.fn_auditing_operation_log')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_operation_log'), N'DetailsExpiresAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作详情绝对到期时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_auditing_operation_log', @level2type=N'COLUMN', @level2name=N'DetailsExpiresAtUtc';

-- 单行状态由 Worker 在首次成功清理后写入；没有检查点时，详情采集必须失败关闭。
IF OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_auditing_details_cleanup_state
    (
        Id uniqueidentifier NOT NULL,
        StateKey tinyint NOT NULL,
        LastSuccessfulCleanupAtUtc datetimeoffset(7) NOT NULL,
        OldestExpiredAtUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_auditing_details_cleanup_state PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_fn_auditing_details_cleanup_state_StateKey UNIQUE (StateKey)
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state')
      AND minor_id = 0 AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作详情清理共享检查点',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_auditing_details_cleanup_state';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state'), N'Id', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_auditing_details_cleanup_state', @level2type=N'COLUMN', @level2name=N'Id';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state'), N'StateKey', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'唯一状态键；固定为1',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_auditing_details_cleanup_state', @level2type=N'COLUMN', @level2name=N'StateKey';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state'), N'LastSuccessfulCleanupAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近成功清理并检查积压的时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_auditing_details_cleanup_state', @level2type=N'COLUMN', @level2name=N'LastSuccessfulCleanupAtUtc';

IF NOT EXISTS (
    SELECT 1 FROM sys.extended_properties
    WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state')
      AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_auditing_details_cleanup_state'), N'OldestExpiredAtUtc', 'ColumnId')
      AND name = N'MS_Description'
)
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'检查时最早未清理详情的到期时间(UTC)',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_auditing_details_cleanup_state', @level2type=N'COLUMN', @level2name=N'OldestExpiredAtUtc';
