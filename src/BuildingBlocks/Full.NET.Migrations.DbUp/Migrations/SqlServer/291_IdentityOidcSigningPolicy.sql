-- 活动签名指针由 Migrator 初始化，API 管理操作更新；重跑不重置已有密钥。
IF OBJECT_ID('dbo.fn_identity_oidc_signing_policy', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.fn_identity_oidc_signing_policy (
        Id uniqueidentifier NOT NULL,
        PolicyKey varchar(32) NOT NULL,
        ActiveKeyId varchar(128) NOT NULL,
        Version int NOT NULL,
        UpdatedAtUtc datetimeoffset(7) NOT NULL,
        CONSTRAINT PK_fn_identity_oidc_signing_policy PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_fn_identity_oidc_signing_policy_key UNIQUE (PolicyKey)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy') AND minor_id = 0 AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'OIDC 集群活动签名策略', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_policy';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy'), N'Id', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'应用端 UUID v7', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_policy', @level2type=N'COLUMN', @level2name=N'Id';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy'), N'PolicyKey', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'固定 default 策略标识', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_policy', @level2type=N'COLUMN', @level2name=N'PolicyKey';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy'), N'ActiveKeyId', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'当前用于签发的密钥标识，不包含秘密', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_policy', @level2type=N'COLUMN', @level2name=N'ActiveKeyId';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy'), N'Version', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'持久化策略版本', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_policy', @level2type=N'COLUMN', @level2name=N'Version';

IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 1 AND major_id = OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy') AND minor_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_identity_oidc_signing_policy'), N'UpdatedAtUtc', 'ColumnId') AND name = N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'最近激活时间', @level0type=N'SCHEMA', @level0name=N'dbo', @level1type=N'TABLE', @level1name=N'fn_identity_oidc_signing_policy', @level2type=N'COLUMN', @level2name=N'UpdatedAtUtc';
