-- 188：Agent Tool 调用审计表。

IF OBJECT_ID(N'dbo.fn_ai_agent_tool_call', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_tool_call
    (
        Id uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        ActorUserId uniqueidentifier NOT NULL,
        ToolName varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PermissionCode varchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        StatusKey varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
        DurationMs int NULL,
        InputSummary nvarchar(512) NOT NULL,
        OutputSummary nvarchar(512) NULL,
        ErrorCode varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        TraceId varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
        CreatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_tool_call PRIMARY KEY NONCLUSTERED (Id),
        CONSTRAINT CK_fn_ai_agent_tool_call_StatusKey
            CHECK (StatusKey IN (N'succeeded', N'failed', N'denied'))
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
      AND name = N'IX_fn_ai_agent_tool_call_CreatedAtUtc')
    CREATE CLUSTERED INDEX IX_fn_ai_agent_tool_call_CreatedAtUtc
        ON dbo.fn_ai_agent_tool_call(CreatedAtUtc DESC, Id);

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
      AND name = N'IX_fn_ai_agent_tool_call_TenantId_CreatedAtUtc')
    CREATE INDEX IX_fn_ai_agent_tool_call_TenantId_CreatedAtUtc
        ON dbo.fn_ai_agent_tool_call(TenantId, CreatedAtUtc DESC, Id);

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
      AND name = N'IX_fn_ai_agent_tool_call_ToolName_CreatedAtUtc')
    CREATE INDEX IX_fn_ai_agent_tool_call_ToolName_CreatedAtUtc
        ON dbo.fn_ai_agent_tool_call(ToolName, CreatedAtUtc DESC, Id);
