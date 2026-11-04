-- KeyId 与秘密分离；历史初始身份复用应用端生成的订阅 UUID，不在数据库生成新 UUID。
-- 分阶段探测可恢复，已有非空身份不重写，秘密、正文及版本均不改变。
IF COL_LENGTH(N'dbo.fn_webhooks_subscription',N'SigningKeyId') IS NULL
    ALTER TABLE dbo.fn_webhooks_subscription ADD SigningKeyId uniqueidentifier NULL;
GO
UPDATE dbo.fn_webhooks_subscription SET SigningKeyId=Id WHERE SigningKeyId IS NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_subscription') AND name=N'SigningKeyId' AND is_nullable=1)
    ALTER TABLE dbo.fn_webhooks_subscription ALTER COLUMN SigningKeyId uniqueidentifier NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_webhooks_subscription') AND name=N'UX_fn_webhooks_subscription_SigningKeyId')
    CREATE UNIQUE INDEX UX_fn_webhooks_subscription_SigningKeyId ON dbo.fn_webhooks_subscription(SigningKeyId);
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_webhooks_subscription') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_webhooks_subscription'),N'SigningKeyId','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'当前签名密钥的公开标识；秘密仍受保护保存',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_webhooks_subscription',@level2type=N'COLUMN',@level2name=N'SigningKeyId';
