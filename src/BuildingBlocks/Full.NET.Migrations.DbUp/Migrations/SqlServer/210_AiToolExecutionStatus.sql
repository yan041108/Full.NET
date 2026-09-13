-- 210：扩展工具执行意图与取消状态，保留所有历史审计行。
-- 约束删除后中断也可重跑；旧状态仍合法，回退应用前应先停止新工具派发。
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_ai_agent_tool_call_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call'))
    ALTER TABLE dbo.fn_ai_agent_tool_call DROP CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_ai_agent_tool_call_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call'))
    ALTER TABLE dbo.fn_ai_agent_tool_call ADD CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey
        CHECK (StatusKey IN (N'started', N'succeeded', N'failed', N'denied', N'cancelled'));
