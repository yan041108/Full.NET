-- 创建身份与订单意图同事务提交；历史订单没有调用方身份，保留 NULL 而不伪造可重放凭据。
IF COL_LENGTH('dbo.fn_payment_order', 'IdempotencyKey') IS NULL
    ALTER TABLE dbo.fn_payment_order ADD IdempotencyKey uniqueidentifier NULL;
IF COL_LENGTH('dbo.fn_payment_order', 'RequestHash') IS NULL
    ALTER TABLE dbo.fn_payment_order ADD RequestHash varchar(64) NULL;
GO
-- 分批编译在补列完成后创建索引；逐对象探测允许部分 DDL 完成后恢复。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.fn_payment_order') AND name='UX_fn_payment_order_TenantId_IdempotencyKey')
    CREATE UNIQUE INDEX UX_fn_payment_order_TenantId_IdempotencyKey ON dbo.fn_payment_order(TenantId,IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;

-- 字段说明与 MySQL COMMENT 配对，重复迁移保留已有元数据。
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_payment_order') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_payment_order'),'IdempotencyKey','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'调用方固定的订单创建身份',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_order',@level2type=N'COLUMN',@level2name=N'IdempotencyKey';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID('dbo.fn_payment_order') AND minor_id=COLUMNPROPERTY(OBJECT_ID('dbo.fn_payment_order'),'RequestHash','ColumnId') AND name=N'MS_Description')
    EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建请求摘要',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_payment_order',@level2type=N'COLUMN',@level2name=N'RequestHash';
