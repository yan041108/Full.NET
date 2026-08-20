-- 097：MySQL Worker 心跳表，与 SqlServer 097 同构。

DROP PROCEDURE IF EXISTS fn_jobs_worker_instance_table;
DELIMITER $$
CREATE PROCEDURE fn_jobs_worker_instance_table()
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'fn_jobs_worker_instance'
    ) THEN
        CREATE TABLE fn_jobs_worker_instance (
            InstanceId BINARY(16) NOT NULL COMMENT 'Worker 实例标识',
            TenantId BINARY(16) NULL COMMENT '租户标识；NULL 表示 Host 级',
            HostProfile varchar(128) NOT NULL COMMENT '主机标识',
            StartedAtUtc datetime(6) NOT NULL COMMENT '启动时间(UTC)',
            LastHeartbeatAtUtc datetime(6) NOT NULL COMMENT '最近心跳(UTC)',
            WorkerVersion varchar(64) NULL COMMENT 'Worker 版本',
            CONSTRAINT PK_fn_jobs_worker_instance PRIMARY KEY (InstanceId)
        ) COMMENT='后台任务 Worker 实例心跳表' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
    END IF;
END$$
DELIMITER ;
CALL fn_jobs_worker_instance_table();
DROP PROCEDURE fn_jobs_worker_instance_table;

INSERT INTO fn_identity_role_permission (RoleId, PermissionCode)
SELECT roles.Id, 'jobs.health.read'
FROM fn_identity_role AS roles
WHERE roles.ScopeKey = 'host'
  AND roles.Code = 'host-administrator'
  AND NOT EXISTS (
    SELECT 1
    FROM fn_identity_role_permission AS existing
    WHERE existing.RoleId = roles.Id
      AND existing.PermissionCode = 'jobs.health.read'
  );
