-- 376：续期来源成对保存；隐式提交后的部分列按结构探测恢复。
DROP PROCEDURE IF EXISTS fn_tenancy_renewal_source_ensure;
DELIMITER $$
CREATE PROCEDURE fn_tenancy_renewal_source_ensure()
BEGIN
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_tenancy_subscription_invoice' AND COLUMN_NAME='RenewalOfSubscriptionId') THEN
  ALTER TABLE fn_tenancy_subscription_invoice ADD COLUMN RenewalOfSubscriptionId binary(16) NULL COMMENT '本次独立账单的原续期订阅';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_tenancy_subscription_invoice' AND COLUMN_NAME='RenewalSourceVersion') THEN
  ALTER TABLE fn_tenancy_subscription_invoice ADD COLUMN RenewalSourceVersion int NULL COMMENT '创建时核验的原订阅版本';
 END IF;
 IF NOT EXISTS (SELECT 1 FROM information_schema.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_tenancy_subscription_invoice' AND CONSTRAINT_NAME='ck_fn_tenancy_subscription_invoice_renewal_source') THEN
  ALTER TABLE fn_tenancy_subscription_invoice ADD CONSTRAINT ck_fn_tenancy_subscription_invoice_renewal_source CHECK ((RenewalOfSubscriptionId IS NULL AND RenewalSourceVersion IS NULL) OR (RenewalOfSubscriptionId IS NOT NULL AND RenewalSourceVersion IS NOT NULL AND RenewalSourceVersion>0));
 END IF;
END$$
DELIMITER ;
CALL fn_tenancy_renewal_source_ensure();
DROP PROCEDURE IF EXISTS fn_tenancy_renewal_source_ensure;
