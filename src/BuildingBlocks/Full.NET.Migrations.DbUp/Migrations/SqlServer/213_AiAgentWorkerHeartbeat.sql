-- 213：Agent Worker 心跳，供运行就绪门禁与观测。
IF OBJECT_ID(N'dbo.fn_ai_agent_worker_instance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_ai_agent_worker_instance (
        InstanceId uniqueidentifier NOT NULL,
        WorkerRole varchar(32) NOT NULL,
        RuntimeVersion varchar(16) NOT NULL,
        HostProfile nvarchar(128) NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        LastHeartbeatAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_ai_agent_worker_instance PRIMARY KEY CLUSTERED (InstanceId)
    );
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance')
          AND minor_id = 0
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'人工智能agent worker instance表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_worker_instance';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_worker_instance'), N'HostProfile', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Host Profile', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_worker_instance', @level2type=N'COLUMN', @level2name=N'HostProfile';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_worker_instance'), N'InstanceId', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_worker_instance', @level2type=N'COLUMN', @level2name=N'InstanceId';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_worker_instance'), N'LastHeartbeatAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Last Heartbeat At(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_worker_instance', @level2type=N'COLUMN', @level2name=N'LastHeartbeatAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_worker_instance'), N'RuntimeVersion', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Runtime Version', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_worker_instance', @level2type=N'COLUMN', @level2name=N'RuntimeVersion';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_worker_instance'), N'StartedAtUtc', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'开始时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_worker_instance', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    IF NOT EXISTS (
        SELECT 1
        FROM sys.extended_properties
        WHERE class = 1
          AND major_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance')
          AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_ai_agent_worker_instance'), N'WorkerRole', 'ColumnId')
          AND name = N'MS_Description'
    )
        EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Worker Role', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_ai_agent_worker_instance', @level2type=N'COLUMN', @level2name=N'WorkerRole';

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance') AND name = N'IX_fn_ai_agent_worker_instance_Heartbeat')
    CREATE NONCLUSTERED INDEX IX_fn_ai_agent_worker_instance_Heartbeat ON dbo.fn_ai_agent_worker_instance (WorkerRole, RuntimeVersion, LastHeartbeatAtUtc);
