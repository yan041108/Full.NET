-- 高风险角色变更必须保留明确的操作者标识；列保持可空以兼容历史认证审计。
IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'ActorUserId') IS NULL
    ALTER TABLE dbo.fn_identity_auth_audit ADD ActorUserId uniqueidentifier NULL;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
      AND name = N'IX_fn_identity_auth_audit_EventType_OccurredAt'
)
    CREATE INDEX IX_fn_identity_auth_audit_EventType_OccurredAt
        ON dbo.fn_identity_auth_audit(EventType, OccurredAtUtc DESC);
