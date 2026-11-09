-- 在暂停业务写入的迁移窗口重建生成键和索引；每个隐式提交步骤可独立重跑。
DROP PROCEDURE IF EXISTS fn_tenancy_subscription_period_slots;
DELIMITER $$
CREATE PROCEDURE fn_tenancy_subscription_period_slots()
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE()
            AND TABLE_NAME='fn_tenancy_tenant_subscription' AND INDEX_NAME='UX_fn_tenancy_tenant_subscription_TenantActive'
    ) THEN
        ALTER TABLE fn_tenancy_tenant_subscription DROP INDEX UX_fn_tenancy_tenant_subscription_TenantActive;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE()
            AND TABLE_NAME='fn_tenancy_tenant_subscription' AND COLUMN_NAME='ActiveSubscriptionKey'
            AND GENERATION_EXPRESSION LIKE '%pending%'
    ) THEN
        ALTER TABLE fn_tenancy_tenant_subscription
            MODIFY COLUMN ActiveSubscriptionKey varchar(8) GENERATED ALWAYS AS (
                CASE WHEN Status IN ('Trial', 'Active') THEN 'active'
                     WHEN Status='PastDue' THEN 'pending' ELSE NULL END
            ) STORED COMMENT '当前周期与待付款周期的唯一槽';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE()
            AND TABLE_NAME='fn_tenancy_tenant_subscription' AND INDEX_NAME='UX_fn_tenancy_tenant_subscription_TenantPeriodSlot'
    ) THEN
        CREATE UNIQUE INDEX UX_fn_tenancy_tenant_subscription_TenantPeriodSlot
            ON fn_tenancy_tenant_subscription (TenantId, ActiveSubscriptionKey);
    END IF;
END$$
DELIMITER ;
CALL fn_tenancy_subscription_period_slots();
DROP PROCEDURE IF EXISTS fn_tenancy_subscription_period_slots;
