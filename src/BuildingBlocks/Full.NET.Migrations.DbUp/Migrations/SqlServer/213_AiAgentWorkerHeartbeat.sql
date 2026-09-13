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

END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_ai_agent_worker_instance') AND name = N'IX_fn_ai_agent_worker_instance_Heartbeat')
    CREATE NONCLUSTERED INDEX IX_fn_ai_agent_worker_instance_Heartbeat ON dbo.fn_ai_agent_worker_instance (WorkerRole, RuntimeVersion, LastHeartbeatAtUtc);
