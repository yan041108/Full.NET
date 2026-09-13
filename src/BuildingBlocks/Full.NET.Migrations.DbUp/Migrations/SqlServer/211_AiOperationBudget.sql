-- 211：新增持久操作预算与追加式价格；不改写既有配额或历史调用。
IF OBJECT_ID(N'dbo.fn_ai_budget_scope', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_budget_scope (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        CONSTRAINT PK_fn_ai_budget_scope PRIMARY KEY CLUSTERED (Id)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_budget_scope')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能预算作用域表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_budget_scope';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_budget_scope')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_budget_scope'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_budget_scope', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_budget_scope')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_budget_scope'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_budget_scope', @level2type=N'COLUMN', @level2name=N'ScopeKey';

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_budget_scope') AND name = N'UX_fn_ai_budget_scope_ScopeKey')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_ai_budget_scope_ScopeKey ON dbo.fn_ai_budget_scope (ScopeKey);
    
IF OBJECT_ID(N'dbo.fn_ai_model_price', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_model_price (
        Id uniqueidentifier NOT NULL,
        ModelConfigId uniqueidentifier NOT NULL,
        ProviderKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ModelId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Currency varchar(3) NOT NULL,
        InputPerMillion decimal(20,8) NOT NULL,
        OutputPerMillion decimal(20,8) NOT NULL,
        CachedInputPerMillion decimal(20,8) NOT NULL,
        ValidFromUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_model_price PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_ai_model_price_Rates CHECK (InputPerMillion BETWEEN 0 AND 1000000 AND OutputPerMillion BETWEEN 0 AND 1000000 AND CachedInputPerMillion BETWEEN 0 AND InputPerMillion)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能模型价格版本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'CachedInputPerMillion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每百万缓存输入 Token 单价', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'CachedInputPerMillion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'Currency', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'币种', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'Currency';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'InputPerMillion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每百万普通输入 Token 单价', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'InputPerMillion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'ModelConfigId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模型配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'ModelConfigId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'ModelId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模型标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'ModelId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'OutputPerMillion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每百万输出 Token 单价', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'OutputPerMillion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_model_price')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_model_price'), N'ValidFromUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Valid From(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_model_price', @level2type=N'COLUMN', @level2name=N'ValidFromUtc';

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_model_price') AND name = N'IX_fn_ai_model_price_ModelVersion')
    CREATE NONCLUSTERED INDEX IX_fn_ai_model_price_ModelVersion ON dbo.fn_ai_model_price (ModelConfigId, ValidFromUtc, Id);
    
IF OBJECT_ID(N'dbo.fn_ai_operation_budget', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_operation_budget (
        Id uniqueidentifier NOT NULL,
        ScopeKey varchar(32) NOT NULL,
        RunId uniqueidentifier NULL,
        ModelConfigId uniqueidentifier NOT NULL,
        ProviderKey varchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
        ModelId nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL,
        RequestHash varchar(64) NOT NULL,
        QuotaMonthKey varchar(7) NOT NULL,
        ReservedTokens bigint NOT NULL,
        ReservedCost decimal(20,8) NULL,
        Currency varchar(3) NULL,
        PriceVersionId uniqueidentifier NULL,
        InputPerMillion decimal(20,8) NULL,
        OutputPerMillion decimal(20,8) NULL,
        CachedInputPerMillion decimal(20,8) NULL,
        ChargedTokens bigint NOT NULL,
        ChargedCost decimal(20,8) NULL,
        InputTokens bigint NULL,
        OutputTokens bigint NULL,
        CachedInputTokens bigint NULL,
        UsageStatus varchar(16) NOT NULL,
        Outcome varchar(16) NOT NULL,
        LegacyTracked bit NOT NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        SettledAtUtc datetimeoffset(7) NULL,
        TraceId varchar(32) NULL,
        CONSTRAINT PK_fn_ai_operation_budget PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_operation_budget_Usage CHECK (UsageStatus IN ('reserved', 'unknown', 'known') AND Outcome IN ('pending', 'succeeded', 'failed', 'cancelled')),
        CONSTRAINT CK_fn_ai_operation_budget_Tokens CHECK (ReservedTokens > 0 AND ChargedTokens >= 0 AND (InputTokens IS NULL OR InputTokens >= 0) AND (OutputTokens IS NULL OR OutputTokens >= 0) AND (CachedInputTokens IS NULL OR (CachedInputTokens >= 0 AND CachedInputTokens <= InputTokens)))
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能操作预算账本表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'CachedInputPerMillion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每百万缓存输入 Token 单价', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'CachedInputPerMillion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'CachedInputTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'缓存输入 Token 数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'CachedInputTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ChargedCost', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已知或保守记账费用；未知时为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ChargedCost';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ChargedTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已知或保守记账 Token 数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ChargedTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'Currency', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'币种', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'Currency';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'InputPerMillion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每百万普通输入 Token 单价', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'InputPerMillion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'InputTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Input Tokens', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'InputTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'LegacyTracked', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'是否同时占用兼容租户配额', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'LegacyTracked';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ModelConfigId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模型配置标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ModelConfigId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ModelId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'模型标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ModelId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'Outcome', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'结果', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'Outcome';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'OutputPerMillion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'每百万输出 Token 单价', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'OutputPerMillion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'OutputTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Output Tokens', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'OutputTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'PriceVersionId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'价格版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'PriceVersionId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ProviderKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'存储提供程序键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ProviderKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'QuotaMonthKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'配额月份键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'QuotaMonthKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'RequestHash', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'完整请求绑定摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'RequestHash';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ReservedCost', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留费用；未知时为空', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ReservedCost';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ReservedTokens', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'预留 Token 数', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ReservedTokens';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'RunId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'RunId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'ScopeKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'作用域键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'ScopeKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'SettledAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Settled At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'SettledAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'TraceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_operation_budget')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_operation_budget'), N'UsageStatus', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用量计量状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_operation_budget', @level2type=N'COLUMN', @level2name=N'UsageStatus';

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_operation_budget') AND name = N'IX_fn_ai_operation_budget_Month')
    CREATE NONCLUSTERED INDEX IX_fn_ai_operation_budget_Month ON dbo.fn_ai_operation_budget (ScopeKey, QuotaMonthKey);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_operation_budget') AND name = N'IX_fn_ai_operation_budget_Run')
    CREATE NONCLUSTERED INDEX IX_fn_ai_operation_budget_Run ON dbo.fn_ai_operation_budget (ScopeKey, RunId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_operation_budget') AND name = N'IX_fn_ai_operation_budget_CreatedAt')
    CREATE CLUSTERED INDEX IX_fn_ai_operation_budget_CreatedAt ON dbo.fn_ai_operation_budget (CreatedAtUtc, Id);
    
