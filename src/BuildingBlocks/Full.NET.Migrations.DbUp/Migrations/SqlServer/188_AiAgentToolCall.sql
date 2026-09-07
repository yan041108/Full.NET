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
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能智能体工具调用表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'ActorUserId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'操作者用户标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'ActorUserId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'CreatedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'创建时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'DurationMs', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'耗时(毫秒)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'DurationMs';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'ErrorCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'错误码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'ErrorCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'Id', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'Id';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'InputSummary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输入摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'InputSummary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'OutputSummary', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输出摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'OutputSummary';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'PermissionCode', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'权限码', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'PermissionCode';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'StatusKey', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'状态键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'StatusKey';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'TenantId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'TenantId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'ToolName', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'工具名称', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'ToolName';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_tool_call')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_tool_call'), N'TraceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'追踪标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_tool_call', @level2type=N'COLUMN', @level2name=N'TraceId';
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
