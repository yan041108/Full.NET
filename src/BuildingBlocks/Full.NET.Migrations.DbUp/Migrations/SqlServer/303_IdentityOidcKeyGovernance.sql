-- 只登记公开材料与短期进程声明；DDL 重跑不重置活动、保留或退役状态。
IF OBJECT_ID(N'dbo.fn_identity_oidc_signing_key', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_signing_key (
        Id uniqueidentifier NOT NULL,
        KeyId nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL,
        PublicKeyHash char(64) NOT NULL,
        PublishedAtUtc datetimeoffset(7) NOT NULL,
        LastActivatedAtUtc datetimeoffset(7) NULL,
        RetireAfterUtc datetimeoffset(7) NULL,
        IsRetired bit NOT NULL,
        Version int NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_signing_key PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_fn_identity_oidc_signing_key_KeyId UNIQUE (KeyId)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OIDC 签名公钥指纹与受控生命周期', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用端 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'KeyId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'不可重新绑定材料的 kid', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'KeyId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'PublicKeyHash', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'规范公钥 DER 的 SHA256', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'PublicKeyHash';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'PublishedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'公钥登记时间 UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'PublishedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'LastActivatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近激活时间 UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'LastActivatedAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'RetireAfterUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'所有合法协议令牌覆盖后的最早退役时间 UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'RetireAfterUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'IsRetired', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'已退役标志且不可重新激活', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'IsRetired';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_key') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_key'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'治理行并发版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_key', @level2type=N'COLUMN', @level2name=N'Version';
IF OBJECT_ID(N'dbo.fn_identity_oidc_signing_node', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_signing_node (
        Id uniqueidentifier NOT NULL,
        RoleKey varchar(16) NOT NULL,
        SigningManifest char(64) NOT NULL,
        EncryptionFingerprint char(64) NOT NULL,
        PrivateKeysJson nvarchar(max) NOT NULL,
        PolicyVersion int NOT NULL,
        ExpiresAtUtc datetimeoffset(7) NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_signing_node PRIMARY KEY CLUSTERED (Id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND name = N'IX_fn_identity_oidc_signing_node_Expires')
    CREATE INDEX IX_fn_identity_oidc_signing_node_Expires ON dbo.fn_identity_oidc_signing_node(ExpiresAtUtc);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OIDC 签名进程的短期就绪租约', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'进程独立 UUID v7，重启不得继承', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'Id';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'RoleKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'签名角色 api 或 worker', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'RoleKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'SigningManifest', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本节点全部公开签名材料摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'SigningManifest';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'EncryptionFingerprint', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'共享协议加密与令牌期限配置摘要', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'EncryptionFingerprint';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'PrivateKeysJson', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本节点持有私钥的 kid 列表，不含私钥', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'PrivateKeysJson';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'PolicyVersion', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'本节点已观察的持久化策略版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'PolicyVersion';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'ExpiresAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'就绪租约到期时间 UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'ExpiresAtUtc';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_node') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_node'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近就绪确认时间 UTC', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_node', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
