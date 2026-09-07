-- 204：调用外部模型前持久化配额预留；取消或崩溃不得无条件退还未知用量。
CREATE TABLE IF NOT EXISTS fn_ai_quota_reservation (
    Id binary(16) NOT NULL COMMENT '生成预留 UUID v7 标识',
    TenantId binary(16) NOT NULL COMMENT '配额所属租户',
    QuotaMonthKey varchar(7) NOT NULL COMMENT '预留所属 UTC 月份',
    ReservedTokens bigint NOT NULL COMMENT '已占用的保守 Token 预算',
    IsSettled boolean NOT NULL COMMENT '是否已领取结算权',
    ActualTokens bigint NULL COMMENT '完整提供程序计量，未知时为空',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '预留创建时间 UTC',
    SettledAtUtc datetime(6) NULL COMMENT '结算完成时间 UTC',
    CONSTRAINT PK_fn_ai_quota_reservation PRIMARY KEY (Id),
    CONSTRAINT CK_fn_ai_quota_reservation_Tokens CHECK (ReservedTokens > 0 AND (ActualTokens IS NULL OR ActualTokens >= 0))
) COMMENT='人工智能配额预留表' ENGINE=InnoDB COMMENT='租户 AI 用量预留与一次性结算凭据';
