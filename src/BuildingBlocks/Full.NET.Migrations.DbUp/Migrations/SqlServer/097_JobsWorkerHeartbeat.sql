-- 097：Worker 心跳表，供管理端只读健康观测。

IF OBJECT_ID(N'dbo.fn_jobs_worker_instance', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_jobs_worker_instance
    (
        InstanceId uniqueidentifier NOT NULL,
        TenantId uniqueidentifier NULL,
        HostProfile nvarchar(128) NOT NULL,
        StartedAtUtc datetimeoffset(7) NOT NULL,
        LastHeartbeatAtUtc datetimeoffset(7) NOT NULL,
        WorkerVersion nvarchar(64) NULL,
        CONSTRAINT PK_fn_jobs_worker_instance PRIMARY KEY CLUSTERED (InstanceId)
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'后台任务 Worker 实例心跳表', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Worker 实例标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance', @level2type=N'COLUMN', @level2name=N'InstanceId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租户标识；NULL 表示 Host 级', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance', @level2type=N'COLUMN', @level2name=N'TenantId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'主机标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance', @level2type=N'COLUMN', @level2name=N'HostProfile';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'启动时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance', @level2type=N'COLUMN', @level2name=N'StartedAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近心跳(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance', @level2type=N'COLUMN', @level2name=N'LastHeartbeatAtUtc';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Worker 版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance', @level2type=N'COLUMN', @level2name=N'WorkerVersion';
END;

INSERT INTO dbo.fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, N'jobs.health.read'
FROM dbo.fn_identity_role AS roles
WHERE roles.ScopeKey = N'host'
  AND roles.Code = N'host-administrator'
  AND NOT EXISTS (
    SELECT 1
    FROM dbo.fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = N'jobs.health.read'
  );
