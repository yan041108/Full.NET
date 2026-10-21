-- 273：按 TraceId 定位域审计，避免仅有时间索引时扫描全部历史；回退可保留该非唯一索引。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_settings_domain_audit')
    AND name = N'ix_fn_settings_domain_audit_trace_time')
    CREATE INDEX ix_fn_settings_domain_audit_trace_time
        ON dbo.fn_settings_domain_audit (TraceId, OccurredAtUtc DESC);
