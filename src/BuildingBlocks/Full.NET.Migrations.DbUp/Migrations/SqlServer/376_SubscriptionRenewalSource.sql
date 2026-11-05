-- 376：独立续期来源；逐列探测与约束收敛允许部分 DDL 未记账后恢复。
IF COL_LENGTH(N'dbo.fn_tenancy_subscription_invoice',N'RenewalOfSubscriptionId') IS NULL
 ALTER TABLE dbo.fn_tenancy_subscription_invoice ADD RenewalOfSubscriptionId uniqueidentifier NULL;
GO
IF COL_LENGTH(N'dbo.fn_tenancy_subscription_invoice',N'RenewalSourceVersion') IS NULL
 ALTER TABLE dbo.fn_tenancy_subscription_invoice ADD RenewalSourceVersion int NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND name=N'ck_fn_tenancy_subscription_invoice_renewal_source')
 ALTER TABLE dbo.fn_tenancy_subscription_invoice WITH CHECK ADD CONSTRAINT ck_fn_tenancy_subscription_invoice_renewal_source CHECK ((RenewalOfSubscriptionId IS NULL AND RenewalSourceVersion IS NULL) OR (RenewalOfSubscriptionId IS NOT NULL AND RenewalSourceVersion IS NOT NULL AND RenewalSourceVersion>0));
GO
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'RenewalOfSubscriptionId','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'本次独立账单的原续期订阅',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'RenewalOfSubscriptionId';
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE major_id=OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice') AND minor_id=COLUMNPROPERTY(OBJECT_ID(N'dbo.fn_tenancy_subscription_invoice'),N'RenewalSourceVersion','ColumnId') AND name=N'MS_Description')
 EXEC sys.sp_addextendedproperty @name=N'MS_Description',@value=N'创建时核验的原订阅版本',@level0type=N'SCHEMA',@level0name=N'dbo',@level1type=N'TABLE',@level1name=N'fn_tenancy_subscription_invoice',@level2type=N'COLUMN',@level2name=N'RenewalSourceVersion';
GO
