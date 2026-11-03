-- 共享票据使多实例使用同一个刷新协调点；重跑不修改已有登录状态。
IF OBJECT_ID('dbo.fn_identity_browser_session', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_browser_session (
        Id uniqueidentifier NOT NULL,
        HandleHash varchar(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
        Ciphertext nvarchar(max) NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        Version bigint NOT NULL,
        RefreshLeaseId uniqueidentifier NULL,
        RefreshLeaseUntilUtc datetimeoffset(7) NULL,
        CONSTRAINT PK_fn_identity_browser_session PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_fn_identity_browser_session_handle UNIQUE (HandleHash)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND name = N'IX_fn_identity_browser_session_expires')
    CREATE INDEX IX_fn_identity_browser_session_expires ON dbo.fn_identity_browser_session(ExpiresAtUtc);

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OIDC 浏览器服务端票据与刷新租约', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_browser_session'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用端 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session', @level2type=N'COLUMN', @level2name=N'Id';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_browser_session'), N'HandleHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'随机 Cookie 句柄的 SHA256 摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session', @level2type=N'COLUMN', @level2name=N'HandleHash';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_browser_session'), N'Ciphertext', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Data Protection 保护的令牌与防伪载荷', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session', @level2type=N'COLUMN', @level2name=N'Ciphertext';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_browser_session'), N'ExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'浏览器会话绝对过期时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_browser_session'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'票据凭据版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session', @level2type=N'COLUMN', @level2name=N'Version';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_browser_session'), N'RefreshLeaseId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'刷新或上下文变更租约标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session', @level2type=N'COLUMN', @level2name=N'RefreshLeaseId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_browser_session') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_browser_session'), N'RefreshLeaseUntilUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'租约过期时间，过期后必须重新登录', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_browser_session', @level2type=N'COLUMN', @level2name=N'RefreshLeaseUntilUtc';
