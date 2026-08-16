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
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Jobs Worker 实例心跳', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_jobs_worker_instance';
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
