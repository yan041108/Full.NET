-- 009 显式治理 SQL Server UUID 主键与聚集索引；数据类型继续保持 uniqueidentifier。
-- 该配对迁移共享维护窗口门禁，批准标识只用于审计，不得包含 Secret。
IF N'$UuidContractMaintenanceMode$' <> N'1'
    THROW 51000, 'UUID contract gate missing: maintenance mode', 1;
IF N'$UuidContractBackupVerified$' <> N'1'
    THROW 51000, 'UUID contract gate missing: verified backup', 1;
IF N'$UuidContractLegacyWritersStopped$' <> N'1'
    THROW 51000, 'UUID contract gate missing: legacy writers stopped', 1;
IF N'$UuidContractDestructiveDdlApprovalId$' = N''
    THROW 51000, 'UUID contract gate missing: destructive DDL approval', 1;
IF NOT EXISTS(
    SELECT 1 FROM dbo.SchemaVersions
    WHERE ScriptName LIKE '%008_UuidBinaryExpand.sql')
    THROW 51000, 'UUID contract prerequisite missing: 008 expand journal', 1;

IF (SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo'
      AND DATA_TYPE = 'uniqueidentifier'
      AND
      (
          (TABLE_NAME = 'fn_tenant_tenant' AND COLUMN_NAME = 'Id')
          OR (TABLE_NAME = 'fn_outbox_message' AND COLUMN_NAME IN ('Id', 'TenantId', 'LockId'))
          OR (TABLE_NAME = 'fn_identity_user' AND COLUMN_NAME IN ('Id', 'TenantId'))
          OR (TABLE_NAME = 'fn_identity_refresh_session'
              AND COLUMN_NAME IN ('Id', 'UserId', 'FamilyId', 'ReplacedById', 'ActiveTenantId'))
          OR (TABLE_NAME = 'fn_identity_auth_audit'
              AND COLUMN_NAME IN ('Id', 'UserId', 'SessionId', 'ContextTenantId', 'ActorUserId'))
          OR (TABLE_NAME = 'fn_identity_role' AND COLUMN_NAME IN ('Id', 'TenantId'))
          OR (TABLE_NAME = 'fn_identity_user_role' AND COLUMN_NAME IN ('UserId', 'RoleId'))
          OR (TABLE_NAME = 'fn_identity_role_permission' AND COLUMN_NAME = 'RoleId')
          OR (TABLE_NAME = 'fn_seed_run' AND COLUMN_NAME = 'Id')
          OR (TABLE_NAME = 'fn_seed_run_item' AND COLUMN_NAME = 'RunId')
      )) <> 23
    THROW 51000, 'UUID contract prerequisite missing: SQL Server UUID schema', 1;

IF OBJECT_ID(N'dbo.fn_uuid_contract_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_uuid_contract_state
    (
        Id tinyint NOT NULL,
        SchemaMode varchar(16) NOT NULL,
        DestructiveDdlApprovalId varchar(64) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_uuid_contract_state PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_fn_uuid_contract_state_Id CHECK (Id = 1),
        CONSTRAINT CK_fn_uuid_contract_state_SchemaMode
            CHECK (SchemaMode IN ('Contracting', 'Binary16'))
    );
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'UUID 二进制契约迁移状态', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_uuid_contract_state';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'破坏性 DDL 审批标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_uuid_contract_state', @level2type=N'COLUMN', @level2name=N'DestructiveDdlApprovalId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'逻辑主键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_uuid_contract_state', @level2type=N'COLUMN', @level2name=N'Id';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Schema 模式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_uuid_contract_state', @level2type=N'COLUMN', @level2name=N'SchemaMode';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'更新时间(UTC)', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_uuid_contract_state', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
END;

IF EXISTS(
    SELECT 1 FROM dbo.fn_uuid_contract_state
    WHERE Id = 1
      AND DestructiveDdlApprovalId <> '$UuidContractDestructiveDdlApprovalId$')
    THROW 51000, 'UUID contract approval mismatch', 1;

MERGE dbo.fn_uuid_contract_state AS target
USING
(
    SELECT CAST(1 AS tinyint) AS Id,
           CAST('Contracting' AS varchar(16)) AS SchemaMode,
           CAST('$UuidContractDestructiveDdlApprovalId$' AS varchar(64)) AS DestructiveDdlApprovalId,
           SYSDATETIMEOFFSET() AS UpdatedAtUtc
) AS source
ON target.Id = source.Id
WHEN MATCHED THEN
    UPDATE SET SchemaMode = source.SchemaMode,
               UpdatedAtUtc = source.UpdatedAtUtc
WHEN NOT MATCHED THEN
    INSERT (Id, SchemaMode, DestructiveDdlApprovalId, UpdatedAtUtc)
    VALUES (source.Id, source.SchemaMode, source.DestructiveDdlApprovalId, source.UpdatedAtUtc);

-- 引用约束必须先移除，随后才能重建被引用主键的聚集属性。
IF OBJECT_ID(N'dbo.FK_fn_identity_refresh_session_User', N'F') IS NOT NULL
    ALTER TABLE dbo.fn_identity_refresh_session DROP CONSTRAINT FK_fn_identity_refresh_session_User;
IF OBJECT_ID(N'dbo.FK_fn_identity_auth_audit_User', N'F') IS NOT NULL
    ALTER TABLE dbo.fn_identity_auth_audit DROP CONSTRAINT FK_fn_identity_auth_audit_User;
IF OBJECT_ID(N'dbo.FK_fn_identity_user_role_User', N'F') IS NOT NULL
    ALTER TABLE dbo.fn_identity_user_role DROP CONSTRAINT FK_fn_identity_user_role_User;
IF OBJECT_ID(N'dbo.FK_fn_identity_user_role_Role', N'F') IS NOT NULL
    ALTER TABLE dbo.fn_identity_user_role DROP CONSTRAINT FK_fn_identity_user_role_Role;
IF OBJECT_ID(N'dbo.FK_fn_identity_role_permission_Role', N'F') IS NOT NULL
    ALTER TABLE dbo.fn_identity_role_permission DROP CONSTRAINT FK_fn_identity_role_permission_Role;
IF OBJECT_ID(N'dbo.FK_fn_seed_run_item_Run', N'F') IS NOT NULL
    ALTER TABLE dbo.fn_seed_run_item DROP CONSTRAINT FK_fn_seed_run_item_Run;

IF EXISTS(SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.fn_outbox_message')
            AND name = N'IX_fn_outbox_message_OccurredAt_Id')
    DROP INDEX IX_fn_outbox_message_OccurredAt_Id ON dbo.fn_outbox_message;
IF EXISTS(SELECT 1 FROM sys.indexes
          WHERE object_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
            AND name = N'IX_fn_identity_auth_audit_OccurredAtUtc_Id')
    DROP INDEX IX_fn_identity_auth_audit_OccurredAtUtc_Id ON dbo.fn_identity_auth_audit;

DECLARE @uuidPkTable sysname;
DECLARE @uuidPkName sysname;
DECLARE @dropUuidPkSql nvarchar(max);
DECLARE uuid_pk_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT tableObject.name, keyObject.name
    FROM sys.key_constraints AS keyObject
    INNER JOIN sys.tables AS tableObject ON tableObject.object_id = keyObject.parent_object_id
    WHERE keyObject.type = 'PK'
      AND tableObject.name IN
          ('fn_tenant_tenant', 'fn_outbox_message', 'fn_identity_user',
           'fn_identity_refresh_session', 'fn_identity_auth_audit', 'fn_identity_role',
           'fn_identity_user_role', 'fn_identity_role_permission',
           'fn_seed_run', 'fn_seed_run_item');
OPEN uuid_pk_cursor;
FETCH NEXT FROM uuid_pk_cursor INTO @uuidPkTable, @uuidPkName;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @dropUuidPkSql = N'ALTER TABLE dbo.' + QUOTENAME(@uuidPkTable)
        + N' DROP CONSTRAINT ' + QUOTENAME(@uuidPkName);
    EXEC sys.sp_executesql @dropUuidPkSql;
    FETCH NEXT FROM uuid_pk_cursor INTO @uuidPkTable, @uuidPkName;
END;
CLOSE uuid_pk_cursor;
DEALLOCATE uuid_pk_cursor;

ALTER TABLE dbo.fn_tenant_tenant
    ADD CONSTRAINT PK_fn_tenant_tenant PRIMARY KEY CLUSTERED (Id);
ALTER TABLE dbo.fn_outbox_message
    ADD CONSTRAINT PK_fn_outbox_message PRIMARY KEY NONCLUSTERED (Id);
ALTER TABLE dbo.fn_identity_user
    ADD CONSTRAINT PK_fn_identity_user PRIMARY KEY CLUSTERED (Id);
ALTER TABLE dbo.fn_identity_refresh_session
    ADD CONSTRAINT PK_fn_identity_refresh_session PRIMARY KEY CLUSTERED (Id);
ALTER TABLE dbo.fn_identity_auth_audit
    ADD CONSTRAINT PK_fn_identity_auth_audit PRIMARY KEY NONCLUSTERED (Id);
ALTER TABLE dbo.fn_identity_role
    ADD CONSTRAINT PK_fn_identity_role PRIMARY KEY CLUSTERED (Id);
ALTER TABLE dbo.fn_identity_user_role
    ADD CONSTRAINT PK_fn_identity_user_role PRIMARY KEY CLUSTERED (UserId, RoleId);
ALTER TABLE dbo.fn_identity_role_permission
    ADD CONSTRAINT PK_fn_identity_role_permission PRIMARY KEY CLUSTERED (RoleId, PermissionCode);
ALTER TABLE dbo.fn_seed_run
    ADD CONSTRAINT PK_fn_seed_run PRIMARY KEY CLUSTERED (Id);
ALTER TABLE dbo.fn_seed_run_item
    ADD CONSTRAINT PK_fn_seed_run_item PRIMARY KEY CLUSTERED (RunId, Contributor);

CREATE CLUSTERED INDEX IX_fn_outbox_message_OccurredAt_Id
    ON dbo.fn_outbox_message(OccurredAt, Id);
CREATE CLUSTERED INDEX IX_fn_identity_auth_audit_OccurredAtUtc_Id
    ON dbo.fn_identity_auth_audit(OccurredAtUtc, Id);

ALTER TABLE dbo.fn_identity_refresh_session
    ADD CONSTRAINT FK_fn_identity_refresh_session_User
    FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id);
ALTER TABLE dbo.fn_identity_auth_audit
    ADD CONSTRAINT FK_fn_identity_auth_audit_User
    FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id);
ALTER TABLE dbo.fn_identity_user_role
    ADD CONSTRAINT FK_fn_identity_user_role_User
        FOREIGN KEY (UserId) REFERENCES dbo.fn_identity_user(Id),
        CONSTRAINT FK_fn_identity_user_role_Role
        FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role(Id);
ALTER TABLE dbo.fn_identity_role_permission
    ADD CONSTRAINT FK_fn_identity_role_permission_Role
    FOREIGN KEY (RoleId) REFERENCES dbo.fn_identity_role(Id);
ALTER TABLE dbo.fn_seed_run_item
    ADD CONSTRAINT FK_fn_seed_run_item_Run
    FOREIGN KEY (RunId) REFERENCES dbo.fn_seed_run(Id);

UPDATE dbo.fn_uuid_contract_state
SET SchemaMode = 'Binary16',
    UpdatedAtUtc = SYSDATETIMEOFFSET()
WHERE Id = 1;
