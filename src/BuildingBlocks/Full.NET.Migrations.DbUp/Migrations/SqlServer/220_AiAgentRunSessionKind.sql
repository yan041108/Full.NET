-- 220：持久化 Agent 运行绑定的会话来源，供 Worker 恢复时选择 OIDC 或旧刷新会话校验路径。
IF COL_LENGTH(N'dbo.fn_ai_agent_run', N'SessionKind') IS NULL
BEGIN
    ALTER TABLE dbo.fn_ai_agent_run
        ADD SessionKind varchar(32) NOT NULL
            CONSTRAINT DF_fn_ai_agent_run_SessionKind DEFAULT ('refresh');
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_run')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'), N'SessionKind', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Session Kind', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_run', @level2type=N'COLUMN', @level2name=N'SessionKind';
END;
