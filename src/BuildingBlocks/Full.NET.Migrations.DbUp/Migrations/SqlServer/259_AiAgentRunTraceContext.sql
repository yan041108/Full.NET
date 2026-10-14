-- 259：新增可空诊断列；旧运行不回填，应用回退保留此列，重放不得改写运行及租约。
IF COL_LENGTH(N'dbo.fn_ai_agent_run', N'TraceParent') IS NULL
    ALTER TABLE dbo.fn_ai_agent_run ADD TraceParent varchar(55) COLLATE Latin1_General_100_BIN2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_agent_run')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_run'),N'TraceParent','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'运行创建时的 W3C 上下文；仅作恢复追踪链接',
        @level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_ai_agent_run',
        @level2type=N'COLUMN',@level2name=N'TraceParent';
GO
