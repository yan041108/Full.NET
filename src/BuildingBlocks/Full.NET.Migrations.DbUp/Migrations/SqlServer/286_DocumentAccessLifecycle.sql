-- 文档第一批：字段、并发与分享会话；逐步探测支持未记账恢复。
IF COL_LENGTH('dbo.fn_document_item', 'VisibilityMode') IS NULL
    ALTER TABLE dbo.fn_document_item ADD VisibilityMode varchar(16) NOT NULL CONSTRAINT DF_fn_document_item_VisibilityMode DEFAULT 'host';
GO
IF COL_LENGTH('dbo.fn_document_item', 'AccessRevision') IS NULL
    ALTER TABLE dbo.fn_document_item ADD AccessRevision bigint NOT NULL CONSTRAINT DF_fn_document_item_AccessRevision DEFAULT 1;
GO
IF COL_LENGTH('dbo.fn_document_item', 'LastVersionNumber') IS NULL
    ALTER TABLE dbo.fn_document_item ADD LastVersionNumber int NOT NULL CONSTRAINT DF_fn_document_item_LastVersionNumber DEFAULT 0;
GO
IF COL_LENGTH('dbo.fn_document_permission', 'Actions') IS NULL
    ALTER TABLE dbo.fn_document_permission ADD Actions int NOT NULL CONSTRAINT DF_fn_document_permission_Actions DEFAULT 0;
GO
IF COL_LENGTH('dbo.fn_document_share', 'PolicyRevision') IS NULL
    ALTER TABLE dbo.fn_document_share ADD PolicyRevision bigint NOT NULL CONSTRAINT DF_fn_document_share_PolicyRevision DEFAULT 1;
GO
IF COL_LENGTH('dbo.fn_document_item', 'IsPurging') IS NULL
    ALTER TABLE dbo.fn_document_item ADD IsPurging bit NOT NULL CONSTRAINT DF_fn_document_item_IsPurging DEFAULT 0;
GO
-- 编号由持久化 Id 确定，重复执行不会改变已有编号。
UPDATE dbo.fn_document_item SET DocumentNo = 'DOC-' + REPLACE(CONVERT(varchar(36), Id), '-', '') WHERE DocumentNo IS NULL OR DocumentNo = '';
UPDATE dbo.fn_document_item SET LastVersionNumber = history.MaxNumber FROM dbo.fn_document_item
CROSS APPLY (SELECT COALESCE(MAX(n.VersionNumber), 0) AS MaxNumber FROM
 (SELECT VersionNumber FROM dbo.fn_document_version WHERE DocumentItemId = fn_document_item.Id
  UNION ALL SELECT VersionNumber FROM dbo.fn_document_version_deletion_audit WHERE DocumentItemId = fn_document_item.Id) n) history
WHERE fn_document_item.LastVersionNumber < history.MaxNumber;
GO
IF COL_LENGTH('dbo.fn_document_permission', 'PermissionLevel') IS NOT NULL
BEGIN
    EXEC(N'UPDATE dbo.fn_document_permission SET Actions = CASE PermissionLevel WHEN ''read'' THEN 1 WHEN ''write'' THEN 3 WHEN ''delete'' THEN 5 WHEN ''share'' THEN 9 WHEN ''manage'' THEN 15 ELSE Actions END WHERE Actions = 0');
    IF EXISTS (SELECT 1 FROM dbo.fn_document_permission WHERE Actions = 0) THROW 51000, 'Unknown document permission action', 1;
    ALTER TABLE dbo.fn_document_permission DROP COLUMN PermissionLevel;
END;
GO
UPDATE dbo.fn_document_item SET VisibilityMode = 'restricted' WHERE VisibilityMode = 'host' AND EXISTS (SELECT 1 FROM dbo.fn_document_permission p WHERE p.DocumentId = fn_document_item.Id);
GO
IF OBJECT_ID('dbo.fn_document_share_session', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_document_share_session (
 Id uniqueidentifier NOT NULL,
 ShareId uniqueidentifier NOT NULL,
 DocumentId uniqueidentifier NOT NULL,
 VersionId uniqueidentifier NOT NULL,
 RequestKey uniqueidentifier NOT NULL,
 AccessRevision bigint NOT NULL,
 PolicyRevision bigint NOT NULL,
 CreatedAtUtc datetimeoffset(7) NOT NULL,
 ExpiresAtUtc datetimeoffset(7) NOT NULL,
 CONSTRAINT PK_fn_document_share_session PRIMARY KEY NONCLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_share_session') AND name = 'UX_fn_document_share_session_ShareId_RequestKey') CREATE UNIQUE INDEX UX_fn_document_share_session_ShareId_RequestKey ON dbo.fn_document_share_session (ShareId, RequestKey);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_share_session') AND name = 'IX_fn_document_share_session_ExpiresAtUtc') CREATE INDEX IX_fn_document_share_session_ExpiresAtUtc ON dbo.fn_document_share_session (ExpiresAtUtc, Id);
GO
IF OBJECT_ID('dbo.fn_document_version_operation', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_document_version_operation (
 Id uniqueidentifier NOT NULL,
 RequestKey uniqueidentifier NOT NULL,
 DocumentId uniqueidentifier NOT NULL,
 ActorUserId uniqueidentifier NOT NULL,
 VersionId uniqueidentifier NOT NULL,
 FileId uniqueidentifier NOT NULL,
 ChangeDescription nvarchar(2000) NULL,
 CreatedAtUtc datetimeoffset(7) NOT NULL,
 CONSTRAINT PK_fn_document_version_operation PRIMARY KEY NONCLUSTERED (Id)
 );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_version_operation') AND name = 'UX_fn_document_version_operation_Request') CREATE UNIQUE INDEX UX_fn_document_version_operation_Request ON dbo.fn_document_version_operation (DocumentId, ActorUserId, RequestKey);
GO
IF OBJECT_ID('dbo.fn_document_permission_audit', 'U') IS NULL
BEGIN
 CREATE TABLE dbo.fn_document_permission_audit (
 Id uniqueidentifier NOT NULL,
 DocumentId uniqueidentifier NOT NULL,
 ActorUserId uniqueidentifier NOT NULL,
 PreviousMode varchar(16) NOT NULL,
 NewMode varchar(16) NOT NULL,
 BeforeActionsJson nvarchar(max) NOT NULL,
 AfterActionsJson nvarchar(max) NOT NULL,
 CreatedAtUtc datetimeoffset(7) NOT NULL,
 CONSTRAINT PK_fn_document_permission_audit PRIMARY KEY NONCLUSTERED (Id)
 );
END;
GO
IF EXISTS (SELECT 1 FROM dbo.fn_document_permission WHERE TenantId IS NULL GROUP BY DocumentId, UserId HAVING COUNT(1) > 1) THROW 51000, 'Duplicate document ACL subjects', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.fn_document_permission') AND name = 'UX_fn_document_permission_HostSubject') CREATE UNIQUE INDEX UX_fn_document_permission_HostSubject ON dbo.fn_document_permission (DocumentId, UserId) WHERE TenantId IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_item') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_item'), 'VisibilityMode', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档可见模式，host 或 restricted', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'VisibilityMode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_item') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_item'), 'AccessRevision', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档访问修订号，撤销历史分享会话', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'AccessRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_item') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_item'), 'LastVersionNumber', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已提交版本最大编号，删除后不回退', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'LastVersionNumber';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission'), 'Actions', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'用户动作位集合，读写删分享独立组合', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission', @level2type=N'COLUMN', @level2name=N'Actions';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share'), 'PolicyRevision', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分享策略修订号，不随访问计数变化', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share', @level2type=N'COLUMN', @level2name=N'PolicyRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_item') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_item'), 'IsPurging', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'永久删除正在分批清理，禁止恢复', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_item', @level2type=N'COLUMN', @level2name=N'IsPurging';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'Id', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的分享会话标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'ShareId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分享标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'ShareId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'DocumentId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'VersionId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定文件版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'VersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'RequestKey', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'一次打开的请求幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'RequestKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'AccessRevision', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档授权修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'AccessRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'PolicyRevision', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'分享策略修订', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'PolicyRevision';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'CreatedAtUtc', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'首次扣次时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_share_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_share_session'), 'ExpiresAtUtc', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可延长的会话到期时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_share_session', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'Id', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的操作标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'RequestKey', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'调用者提供的请求幂等键', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'RequestKey';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'DocumentId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'ActorUserId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受信调用者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'VersionId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已提交版本标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'VersionId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'FileId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'输入文件标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'FileId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'ChangeDescription', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'版本变更说明', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'ChangeDescription';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_version_operation') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_version_operation'), 'CreatedAtUtc', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'提交时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_version_operation', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'Id', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用生成的治理审计标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'Id';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'DocumentId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'文档标识，不级联删除审计', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'DocumentId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'ActorUserId', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'受信任治理操作者', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'ActorUserId';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'PreviousMode', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更前可见模式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'PreviousMode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'NewMode', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更后可见模式', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'NewMode';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'BeforeActionsJson', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更前动作集合', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'BeforeActionsJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'AfterActionsJson', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'变更后动作集合', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'AfterActionsJson';
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id = OBJECT_ID('dbo.fn_document_permission_audit') AND minor_id = COLUMNPROPERTY(OBJECT_ID('dbo.fn_document_permission_audit'), 'CreatedAtUtc', 'ColumnId') AND name = 'MS_Description') EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'治理提交时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_document_permission_audit', @level2type=N'COLUMN', @level2name=N'CreatedAtUtc';
GO
