-- 当前周期和待付款周期分别唯一；业务事务仍按租户根锁核验续期来源。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_tenant_subscription') AND name=N'UX_fn_tenancy_tenant_subscription_TenantCurrent')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_tenancy_tenant_subscription_TenantCurrent
        ON dbo.fn_tenancy_tenant_subscription (TenantId) WHERE Status IN (N'Trial', N'Active');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_tenant_subscription') AND name=N'UX_fn_tenancy_tenant_subscription_TenantPending')
    CREATE UNIQUE NONCLUSTERED INDEX UX_fn_tenancy_tenant_subscription_TenantPending
        ON dbo.fn_tenancy_tenant_subscription (TenantId) WHERE Status=N'PastDue';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.fn_tenancy_tenant_subscription') AND name=N'UX_fn_tenancy_tenant_subscription_TenantActive')
    DROP INDEX UX_fn_tenancy_tenant_subscription_TenantActive ON dbo.fn_tenancy_tenant_subscription;
