-- 050：Settings 模块 B0 域内同事务审计（高写入：非聚集主键 + 时间路径聚集索引）。
-- 该表只承接与业务写入同事务提交的域内审计记录，不经过 Outbox；Outcome 固定为
-- success/failure 两种取值，禁止承载其他审计可靠性等级的数据。

IF OBJECT_ID(N'dbo.fn_settings_domain_audit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_settings_domain_audit
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ActionKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        EntityId uniqueidentifier NOT NULL,
        Outcome varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ActorUserId uniqueidentifier NULL,
        ActorDisplayName nvarchar(128) NULL,
        TraceId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        DiffSummaryJson nvarchar(max) NULL,
        OccurredAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_settings_domain_audit PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_settings_domain_audit_Outcome
            CHECK (Outcome IN ('success', 'failure'))
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'系统设置领域审计表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'ActionKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者显示名', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorDisplayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'差异摘要(JSON)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'DiffSummaryJson';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实体标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'EntityId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'发生时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'OccurredAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'Outcome';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_settings_domain_audit', @level2type=N'COLUMN', @level2name=N'TraceId';
    CREATE CLUSTERED INDEX IX_fn_settings_domain_audit_OccurredAtUtc_Id
        ON dbo.fn_settings_domain_audit(OccurredAtUtc, Id);
END;

IF EXISTS
(
    SELECT 1
    FROM sys.indexes AS indexObject
    WHERE indexObject.object_id = OBJECT_ID(N'dbo.fn_settings_domain_audit')
      AND indexObject.name = N'IX_fn_settings_domain_audit_TenantId_OccurredAtUtc_Id'
      AND
      (
          indexObject.is_unique = 1
          OR indexObject.is_disabled = 1
          OR
          (
              SELECT COUNT(*)
              FROM sys.index_columns AS indexColumn
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal > 0
          ) <> 3
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 1
                AND columnObject.name = N'TenantId'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 2
                AND columnObject.name = N'OccurredAtUtc'
          )
          OR NOT EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS indexColumn
              INNER JOIN sys.columns AS columnObject
                  ON columnObject.object_id = indexColumn.object_id
                 AND columnObject.column_id = indexColumn.column_id
              WHERE indexColumn.object_id = indexObject.object_id
                AND indexColumn.index_id = indexObject.index_id
                AND indexColumn.key_ordinal = 3
                AND columnObject.name = N'Id'
          )
      )
)
BEGIN
    DROP INDEX IX_fn_settings_domain_audit_TenantId_OccurredAtUtc_Id
        ON dbo.fn_settings_domain_audit;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_settings_domain_audit')
      AND name = N'IX_fn_settings_domain_audit_TenantId_OccurredAtUtc_Id'
)
BEGIN
    CREATE INDEX IX_fn_settings_domain_audit_TenantId_OccurredAtUtc_Id
        ON dbo.fn_settings_domain_audit(TenantId, OccurredAtUtc, Id);
END;
