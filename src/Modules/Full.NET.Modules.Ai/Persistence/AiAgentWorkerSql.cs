using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>Agent Worker 心跳；Global 作用域供跨租户领取与就绪门禁共用。</summary>
internal static class AiAgentWorkerSql
{
    public const string WorkerRole = "ai-agent";

    public static readonly SqlStatement UpsertSqlServer = new("ai.agent_worker.upsert_heartbeat.sqlserver", """
        MERGE fn_ai_agent_worker_instance AS target
        USING (SELECT @InstanceId AS InstanceId) AS source ON target.InstanceId = source.InstanceId
        WHEN MATCHED THEN UPDATE SET
            LastHeartbeatAtUtc = @LastHeartbeatAtUtc,
            RuntimeVersion = @RuntimeVersion,
            HostProfile = @HostProfile
        WHEN NOT MATCHED THEN INSERT
            (InstanceId, WorkerRole, RuntimeVersion, HostProfile, StartedAtUtc, LastHeartbeatAtUtc)
        VALUES (@InstanceId, @WorkerRole, @RuntimeVersion, @HostProfile, @StartedAtUtc, @LastHeartbeatAtUtc);
        """, SqlDataScope.Global);

    public static readonly SqlStatement UpsertMySql = new("ai.agent_worker.upsert_heartbeat.mysql", """
        INSERT INTO fn_ai_agent_worker_instance
            (InstanceId, WorkerRole, RuntimeVersion, HostProfile, StartedAtUtc, LastHeartbeatAtUtc)
        VALUES (@InstanceId, @WorkerRole, @RuntimeVersion, @HostProfile, @StartedAtUtc, @LastHeartbeatAtUtc)
        ON DUPLICATE KEY UPDATE
            LastHeartbeatAtUtc = @LastHeartbeatAtUtc,
            RuntimeVersion = @RuntimeVersion,
            HostProfile = @HostProfile
        """, SqlDataScope.Global);

    public static readonly SqlStatement HasFreshWorker = new("ai.agent_worker.has_fresh", """
        SELECT CASE WHEN EXISTS (
            SELECT 1 FROM fn_ai_agent_worker_instance
            WHERE WorkerRole = @WorkerRole AND RuntimeVersion = @RuntimeVersion
              AND LastHeartbeatAtUtc > @StaleBefore
        ) THEN 1 ELSE 0 END
        """, SqlDataScope.Global);
}
