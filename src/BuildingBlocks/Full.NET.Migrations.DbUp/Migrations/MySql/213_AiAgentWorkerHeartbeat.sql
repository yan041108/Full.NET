-- 213：Agent Worker 心跳，供运行就绪门禁与观测。
CREATE TABLE IF NOT EXISTS fn_ai_agent_worker_instance (
        InstanceId binary(16) NOT NULL COMMENT '实例标识',
        WorkerRole varchar(32) NOT NULL COMMENT 'Worker 角色键',
        RuntimeVersion varchar(16) NOT NULL COMMENT '运行时版本',
        HostProfile varchar(128) NOT NULL COMMENT '宿主标识',
        StartedAtUtc datetime(6) NOT NULL COMMENT '启动 UTC',
        LastHeartbeatAtUtc datetime(6) NOT NULL COMMENT '最近心跳 UTC',
        CONSTRAINT PK_fn_ai_agent_worker_instance PRIMARY KEY (InstanceId),
        KEY IX_fn_ai_agent_worker_instance_Heartbeat (WorkerRole, RuntimeVersion, LastHeartbeatAtUtc)
) COMMENT='人工智能 Agent Worker 实例表' ENGINE=InnoDB;
