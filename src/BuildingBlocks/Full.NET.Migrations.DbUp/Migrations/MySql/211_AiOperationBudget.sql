-- 211：新增持久操作预算与追加式价格；不改写既有配额或历史调用。
CREATE TABLE IF NOT EXISTS fn_ai_budget_scope (
        Id binary(16) NOT NULL COMMENT '逻辑主键',
        ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信租户 UUID 或 host',
        CONSTRAINT PK_fn_ai_budget_scope PRIMARY KEY (Id),
        UNIQUE KEY UX_fn_ai_budget_scope_ScopeKey (ScopeKey)
) COMMENT='人工智能预算作用域表' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_ai_model_price (
        Id binary(16) NOT NULL COMMENT '价格版本标识',
        ModelConfigId binary(16) NOT NULL COMMENT '所属模型配置',
        ProviderKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '提供程序键',
        ModelId varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '供应商模型名称',
        Currency varchar(3) NOT NULL COMMENT '价格币种',
        InputPerMillion decimal(20,8) NOT NULL COMMENT '每百万普通输入 Token 单价',
        OutputPerMillion decimal(20,8) NOT NULL COMMENT '每百万输出 Token 单价',
        CachedInputPerMillion decimal(20,8) NOT NULL COMMENT '每百万缓存输入 Token 单价',
        ValidFromUtc datetime(6) NOT NULL COMMENT '价格生效 UTC 时间',
        CONSTRAINT PK_fn_ai_model_price PRIMARY KEY (Id),
        KEY IX_fn_ai_model_price_ModelVersion (ModelConfigId, ValidFromUtc, Id),
        CONSTRAINT CK_fn_ai_model_price_Rates CHECK (InputPerMillion BETWEEN 0 AND 1000000 AND OutputPerMillion BETWEEN 0 AND 1000000 AND CachedInputPerMillion BETWEEN 0 AND InputPerMillion)
) COMMENT='人工智能模型价格版本表' ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS fn_ai_operation_budget (
        Id binary(16) NOT NULL COMMENT '操作幂等标识',
        ScopeKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信租户 UUID 或 host',
        RunId binary(16) NULL COMMENT '可空持久运行标识',
        ModelConfigId binary(16) NOT NULL COMMENT '模型配置标识',
        ProviderKey varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '调用时提供程序键',
        ModelId varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '调用时模型名称',
        RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '完整请求绑定摘要',
        QuotaMonthKey varchar(7) NOT NULL COMMENT '原预留 UTC 月份',
        ReservedTokens bigint NOT NULL COMMENT '保守预留 Token',
        ReservedCost decimal(20,8) NULL COMMENT '预留费用且未知为空',
        Currency varchar(3) NULL COMMENT '固化价格币种',
        PriceVersionId binary(16) NULL COMMENT '固化价格版本',
        InputPerMillion decimal(20,8) NULL COMMENT '固化普通输入单价',
        OutputPerMillion decimal(20,8) NULL COMMENT '固化输出单价',
        CachedInputPerMillion decimal(20,8) NULL COMMENT '固化缓存输入单价',
        ChargedTokens bigint NOT NULL COMMENT '当前保守或已知 Token',
        ChargedCost decimal(20,8) NULL COMMENT '当前费用且未知为空',
        InputTokens bigint NULL COMMENT '已知普通及缓存输入 Token',
        OutputTokens bigint NULL COMMENT '已知输出 Token',
        CachedInputTokens bigint NULL COMMENT '已知缓存输入 Token',
        UsageStatus varchar(16) NOT NULL COMMENT 'reserved unknown known 计量状态',
        Outcome varchar(16) NOT NULL COMMENT '模型调用结果',
        LegacyTracked tinyint(1) NOT NULL COMMENT '是否占用兼容租户配额',
        CreatedAtUtc datetime(6) NOT NULL COMMENT '预留创建 UTC 时间',
        SettledAtUtc datetime(6) NULL COMMENT '最近回执 UTC 时间',
        TraceId varchar(32) NULL COMMENT '受控跟踪标识',
        CONSTRAINT PK_fn_ai_operation_budget PRIMARY KEY (Id),
        KEY IX_fn_ai_operation_budget_Month (ScopeKey, QuotaMonthKey),
        KEY IX_fn_ai_operation_budget_Run (ScopeKey, RunId),
        CONSTRAINT CK_fn_ai_operation_budget_Usage CHECK (UsageStatus IN ('reserved', 'unknown', 'known') AND Outcome IN ('pending', 'succeeded', 'failed', 'cancelled')),
        CONSTRAINT CK_fn_ai_operation_budget_Tokens CHECK (ReservedTokens > 0 AND ChargedTokens >= 0 AND (InputTokens IS NULL OR InputTokens >= 0) AND (OutputTokens IS NULL OR OutputTokens >= 0) AND (CachedInputTokens IS NULL OR (CachedInputTokens >= 0 AND CachedInputTokens <= InputTokens)))
) COMMENT='人工智能操作预算账本表' ENGINE=InnoDB;
