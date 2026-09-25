-- 235：兼容扩展 Identity 认证审计上下文；旧记录的新增列保持空值。
IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'TraceId') IS NULL
    ALTER TABLE dbo.fn_identity_auth_audit ADD TraceId varchar(64) NULL;
IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'AuthenticationMethod') IS NULL
    ALTER TABLE dbo.fn_identity_auth_audit ADD AuthenticationMethod varchar(40) NULL;
IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'ClientId') IS NULL
    ALTER TABLE dbo.fn_identity_auth_audit ADD ClientId varchar(128) NULL;
IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'CenterSessionId') IS NULL
    ALTER TABLE dbo.fn_identity_auth_audit ADD CenterSessionId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.fn_identity_auth_audit', N'ApplicationSessionId') IS NULL
    ALTER TABLE dbo.fn_identity_auth_audit ADD ApplicationSessionId uniqueidentifier NULL;

-- 认证历史不能因用户删除而消失；UserId 继续作为历史标识保留。
IF OBJECT_ID(N'dbo.FK_fn_identity_auth_audit_User', N'F') IS NOT NULL
    ALTER TABLE dbo.fn_identity_auth_audit DROP CONSTRAINT FK_fn_identity_auth_audit_User;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1
    AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'TraceId', 'ColumnId')
    AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'请求追踪标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'TraceId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1
    AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'AuthenticationMethod', 'ColumnId')
    AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'认证方式',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'AuthenticationMethod';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1
    AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'ClientId', 'ColumnId')
    AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OIDC 客户端标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'ClientId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1
    AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'CenterSessionId', 'ColumnId')
    AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'中心会话标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'CenterSessionId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1
    AND major_id = OBJECT_ID(N'dbo.fn_identity_auth_audit')
    AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_auth_audit'), N'ApplicationSessionId', 'ColumnId')
    AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用会话标识',
        @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE',
        @level1name=N'fn_identity_auth_audit', @level2type=N'COLUMN', @level2name=N'ApplicationSessionId';
