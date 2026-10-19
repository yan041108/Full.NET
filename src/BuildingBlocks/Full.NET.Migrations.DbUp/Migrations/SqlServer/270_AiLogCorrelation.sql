-- 270：为 Ai 日志增加关联快照；可空增量列，历史不回填，回退保留列。

IF COL_LENGTH(N'dbo.fn_ai_agent_tool_call', N'CorrelationJson') IS NULL
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD CorrelationJson nvarchar(2048) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
    AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'),N'CorrelationJson','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'事件产生时的关联快照；历史缺失为空，不含凭据或原始IP',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call',
        @level2type=N'COLUMN', @level2name=N'CorrelationJson';
