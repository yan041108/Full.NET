-- 039：Host 流水号规则、作用域计数器与事务内幂等分配记录。
IF OBJECT_ID(N'dbo.fn_serialnumbers_rule', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_serialnumbers_rule
    (
        Id uniqueidentifier NOT NULL,
        RuleKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DisplayName nvarchar(128) NOT NULL,
        Description nvarchar(512) NULL,
        Scope tinyint NOT NULL,
        ResetInterval tinyint NOT NULL,
        Pattern nvarchar(128) NOT NULL,
        MinimumValue bigint NOT NULL,
        MaximumValue bigint NOT NULL,
        DisplayOrder int NOT NULL,
        IsEnabled bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NULL,
        UpdatedByUserId uniqueidentifier NULL,
        Version bigint NOT NULL CONSTRAINT DF_fn_serialnumbers_rule_Version
            DEFAULT (1),
        CONSTRAINT PK_fn_serialnumbers_rule PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_serialnumbers_rule_Scope CHECK (Scope IN (0, 1)),
        CONSTRAINT CK_fn_serialnumbers_rule_ResetInterval
            CHECK (ResetInterval IN (0, 1, 2, 3)),
        CONSTRAINT CK_fn_serialnumbers_rule_ValueRange
            CHECK (MinimumValue >= 1 AND MaximumValue >= MinimumValue)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'序列号规则表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'CreatedByUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'描述', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'Description';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'DisplayName';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'显示顺序', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'DisplayOrder';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否启用', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'IsEnabled';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最大值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'MaximumValue';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最小值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'MinimumValue';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'编号模式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'Pattern';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重置周期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'ResetInterval';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规则键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'RuleKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'Scope';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新人用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'UpdatedByUserId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'乐观并发版本号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_rule', @level2type=N'COLUMN', @level2name=N'Version';
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_serialnumbers_rule')
      AND name = N'UX_fn_serialnumbers_rule_RuleKey'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_serialnumbers_rule_RuleKey
        ON dbo.fn_serialnumbers_rule(RuleKey);
END;

IF OBJECT_ID(N'dbo.fn_serialnumbers_counter', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_serialnumbers_counter
    (
        Id uniqueidentifier NOT NULL,
        RuleId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ResetBucket varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
        LastValue bigint NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_serialnumbers_counter PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_serialnumbers_counter_Rule
            FOREIGN KEY (RuleId) REFERENCES dbo.fn_serialnumbers_rule(Id),
        CONSTRAINT CK_fn_serialnumbers_counter_LastValue
            CHECK (LastValue >= 1)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'序列号计数器表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_counter';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_counter', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最后计数值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_counter', @level2type=N'COLUMN', @level2name=N'LastValue';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重置桶', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_counter', @level2type=N'COLUMN', @level2name=N'ResetBucket';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规则标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_counter', @level2type=N'COLUMN', @level2name=N'RuleId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_counter', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_counter', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_serialnumbers_counter')
      AND name = N'UX_fn_serialnumbers_counter_HostBucket'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_serialnumbers_counter_HostBucket
        ON dbo.fn_serialnumbers_counter(RuleId, ResetBucket)
        WHERE TenantId IS NULL;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_serialnumbers_counter')
      AND name = N'UX_fn_serialnumbers_counter_TenantBucket'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_serialnumbers_counter_TenantBucket
        ON dbo.fn_serialnumbers_counter(TenantId, RuleId, ResetBucket)
        WHERE TenantId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.fn_serialnumbers_allocation', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_serialnumbers_allocation
    (
        Id uniqueidentifier NOT NULL,
        RuleId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        RuleKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ResetBucket varchar(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
        IdempotencyKey varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        SequenceValue bigint NOT NULL,
        SerialNumber nvarchar(128) NOT NULL,
        AllocatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_serialnumbers_allocation PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_fn_serialnumbers_allocation_Rule
            FOREIGN KEY (RuleId) REFERENCES dbo.fn_serialnumbers_rule(Id),
        CONSTRAINT CK_fn_serialnumbers_allocation_SequenceValue
            CHECK (SequenceValue >= 1)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'序列号分配记录表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分配时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'AllocatedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'IdempotencyKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'重置桶', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'ResetBucket';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规则标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'RuleId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规则键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'RuleKey';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'序列值', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'SequenceValue';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'序列号', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'SerialNumber';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_serialnumbers_allocation', @level2type=N'COLUMN', @level2name=N'TenantId';
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_serialnumbers_allocation')
      AND name = N'UX_fn_serialnumbers_allocation_HostIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_HostIdempotency
        ON dbo.fn_serialnumbers_allocation(RuleId, IdempotencyKey)
        WHERE TenantId IS NULL;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_serialnumbers_allocation')
      AND name = N'UX_fn_serialnumbers_allocation_TenantIdempotency'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_TenantIdempotency
        ON dbo.fn_serialnumbers_allocation(
            TenantId, RuleId, IdempotencyKey)
        WHERE TenantId IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_serialnumbers_allocation')
      AND name = N'UX_fn_serialnumbers_allocation_HostSequence'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_HostSequence
        ON dbo.fn_serialnumbers_allocation(
            RuleId, ResetBucket, SequenceValue)
        WHERE TenantId IS NULL;
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_serialnumbers_allocation')
      AND name = N'UX_fn_serialnumbers_allocation_TenantSequence'
)
BEGIN
    CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_TenantSequence
        ON dbo.fn_serialnumbers_allocation(
            TenantId, RuleId, ResetBucket, SequenceValue)
        WHERE TenantId IS NOT NULL;
END;

-- 条件创建只能覆盖缺失状态；下面继续收敛同名但列序、唯一性或过滤器错误的半完成状态。
DECLARE @ExpectedIndexes TABLE
(
    TableName sysname NOT NULL,
    IndexName sysname NOT NULL,
    KeyColumns nvarchar(512) NOT NULL,
    FilterKey varchar(32) NOT NULL,
    CreateSql nvarchar(max) NOT NULL
);

INSERT INTO @ExpectedIndexes
    (TableName, IndexName, KeyColumns, FilterKey, CreateSql)
VALUES
    (N'fn_serialnumbers_rule',
     N'UX_fn_serialnumbers_rule_RuleKey',
     N'RuleKey',
     '',
     N'CREATE UNIQUE INDEX UX_fn_serialnumbers_rule_RuleKey ON dbo.fn_serialnumbers_rule(RuleKey);'),
    (N'fn_serialnumbers_counter',
     N'UX_fn_serialnumbers_counter_HostBucket',
     N'RuleId,ResetBucket',
     'tenantidisnull',
     N'CREATE UNIQUE INDEX UX_fn_serialnumbers_counter_HostBucket ON dbo.fn_serialnumbers_counter(RuleId, ResetBucket) WHERE TenantId IS NULL;'),
    (N'fn_serialnumbers_counter',
     N'UX_fn_serialnumbers_counter_TenantBucket',
     N'TenantId,RuleId,ResetBucket',
     'tenantidisnotnull',
     N'CREATE UNIQUE INDEX UX_fn_serialnumbers_counter_TenantBucket ON dbo.fn_serialnumbers_counter(TenantId, RuleId, ResetBucket) WHERE TenantId IS NOT NULL;'),
    (N'fn_serialnumbers_allocation',
     N'UX_fn_serialnumbers_allocation_HostIdempotency',
     N'RuleId,IdempotencyKey',
     'tenantidisnull',
     N'CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_HostIdempotency ON dbo.fn_serialnumbers_allocation(RuleId, IdempotencyKey) WHERE TenantId IS NULL;'),
    (N'fn_serialnumbers_allocation',
     N'UX_fn_serialnumbers_allocation_TenantIdempotency',
     N'TenantId,RuleId,IdempotencyKey',
     'tenantidisnotnull',
     N'CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_TenantIdempotency ON dbo.fn_serialnumbers_allocation(TenantId, RuleId, IdempotencyKey) WHERE TenantId IS NOT NULL;'),
    (N'fn_serialnumbers_allocation',
     N'UX_fn_serialnumbers_allocation_HostSequence',
     N'RuleId,ResetBucket,SequenceValue',
     'tenantidisnull',
     N'CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_HostSequence ON dbo.fn_serialnumbers_allocation(RuleId, ResetBucket, SequenceValue) WHERE TenantId IS NULL;'),
    (N'fn_serialnumbers_allocation',
     N'UX_fn_serialnumbers_allocation_TenantSequence',
     N'TenantId,RuleId,ResetBucket,SequenceValue',
     'tenantidisnotnull',
     N'CREATE UNIQUE INDEX UX_fn_serialnumbers_allocation_TenantSequence ON dbo.fn_serialnumbers_allocation(TenantId, RuleId, ResetBucket, SequenceValue) WHERE TenantId IS NOT NULL;');

DECLARE @TableName sysname;
DECLARE @IndexName sysname;
DECLARE @KeyColumns nvarchar(512);
DECLARE @FilterKey varchar(32);
DECLARE @CreateSql nvarchar(max);
DECLARE @ActualColumns nvarchar(512);
DECLARE @ActualFilterKey varchar(32);
DECLARE @IsUnique bit;
DECLARE @IsDisabled bit;
DECLARE @RepairSql nvarchar(max);

DECLARE serial_number_index_cursor CURSOR LOCAL FAST_FORWARD FOR
SELECT TableName, IndexName, KeyColumns, FilterKey, CreateSql
FROM @ExpectedIndexes;

OPEN serial_number_index_cursor;
FETCH NEXT FROM serial_number_index_cursor
INTO @TableName, @IndexName, @KeyColumns, @FilterKey, @CreateSql;

WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT
        @ActualColumns = STRING_AGG(
            CONVERT(nvarchar(max), columnObject.name),
            N',') WITHIN GROUP (ORDER BY indexColumn.key_ordinal),
        @ActualFilterKey = LOWER(REPLACE(REPLACE(REPLACE(REPLACE(
            COALESCE(indexObject.filter_definition, N''),
            N'[', N''), N']', N''), N'(', N''), N')', N'')),
        @IsUnique = indexObject.is_unique,
        @IsDisabled = indexObject.is_disabled
    FROM sys.indexes AS indexObject
    INNER JOIN sys.index_columns AS indexColumn
        ON indexColumn.object_id = indexObject.object_id
       AND indexColumn.index_id = indexObject.index_id
       AND indexColumn.key_ordinal > 0
    INNER JOIN sys.columns AS columnObject
        ON columnObject.object_id = indexColumn.object_id
       AND columnObject.column_id = indexColumn.column_id
    WHERE indexObject.object_id =
          OBJECT_ID(N'dbo.' + @TableName)
      AND indexObject.name = @IndexName
    GROUP BY
        indexObject.filter_definition,
        indexObject.is_unique,
        indexObject.is_disabled;

    SET @ActualFilterKey = REPLACE(
        COALESCE(@ActualFilterKey, ''),
        ' ',
        '');

    IF @ActualColumns IS NOT NULL
       AND
       (
           @ActualColumns <> @KeyColumns
           OR @ActualFilterKey <> @FilterKey
           OR @IsUnique <> 1
           OR @IsDisabled <> 0
       )
    BEGIN
        SET @RepairSql =
            N'DROP INDEX ' + QUOTENAME(@IndexName)
            + N' ON dbo.' + QUOTENAME(@TableName) + N';';
        EXEC sys.sp_executesql @RepairSql;
        SET @ActualColumns = NULL;
    END;

    IF @ActualColumns IS NULL
    BEGIN
        EXEC sys.sp_executesql @CreateSql;
    END;

    SET @ActualColumns = NULL;
    SET @ActualFilterKey = NULL;
    SET @IsUnique = NULL;
    SET @IsDisabled = NULL;
    FETCH NEXT FROM serial_number_index_cursor
    INTO @TableName, @IndexName, @KeyColumns, @FilterKey, @CreateSql;
END;

CLOSE serial_number_index_cursor;
DEALLOCATE serial_number_index_cursor;
