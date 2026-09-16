-- 220：持久化 Agent 运行绑定的会话来源，供 Worker 恢复时选择 OIDC 或旧刷新会话校验路径。
IF COL_LENGTH(N'dbo.fn_ai_agent_run', N'SessionKind') IS NULL
BEGIN
    ALTER TABLE dbo.fn_ai_agent_run
        ADD SessionKind varchar(32) NOT NULL
            CONSTRAINT DF_fn_ai_agent_run_SessionKind DEFAULT ('refresh');
END;
