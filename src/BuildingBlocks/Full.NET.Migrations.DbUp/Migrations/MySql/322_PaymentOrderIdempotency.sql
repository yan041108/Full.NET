-- MySQL DDL 隐式提交，逐对象探测保证重复执行不会丢失原订单或创建身份。
DROP PROCEDURE IF EXISTS fn_payments_ensure_idempotency;
DELIMITER $$
CREATE PROCEDURE fn_payments_ensure_idempotency()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_payment_order' AND COLUMN_NAME='IdempotencyKey') THEN
        ALTER TABLE fn_payment_order ADD IdempotencyKey binary(16) NULL COMMENT '调用方固定的订单创建身份';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_payment_order' AND COLUMN_NAME='RequestHash') THEN
        ALTER TABLE fn_payment_order ADD RequestHash varchar(64) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '创建请求摘要';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_payment_order' AND INDEX_NAME='UX_fn_payment_order_TenantId_IdempotencyKey') THEN
        CREATE UNIQUE INDEX UX_fn_payment_order_TenantId_IdempotencyKey ON fn_payment_order(TenantId,IdempotencyKey);
    END IF;
END$$
DELIMITER ;
CALL fn_payments_ensure_idempotency();
DROP PROCEDURE IF EXISTS fn_payments_ensure_idempotency;
