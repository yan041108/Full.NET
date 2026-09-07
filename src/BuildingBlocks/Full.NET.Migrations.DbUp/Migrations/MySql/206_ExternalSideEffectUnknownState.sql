-- 206：外部渠道未知状态。支付/退款/金蝶/OCR 在事务外调用后必须能持久化 provider_unknown。
-- MySQL CHECK 变更会隐式提交；按约束是否存在分别 DROP/ADD，未记账时可重跑。

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND CONSTRAINT_NAME = 'CK_fn_payment_order_TradeStateKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_payment_order DROP CHECK CK_fn_payment_order_TradeStateKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_order'
      AND CONSTRAINT_NAME = 'CK_fn_payment_order_TradeStateKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_payment_order ADD CONSTRAINT CK_fn_payment_order_TradeStateKey CHECK (TradeStateKey IN (''created'', ''awaiting_payment'', ''succeeded'', ''closed'', ''failed'', ''refunding'', ''refunded'', ''provider_unknown''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_refund'
      AND CONSTRAINT_NAME = 'CK_fn_payment_refund_RefundStateKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_payment_refund DROP CHECK CK_fn_payment_refund_RefundStateKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_payment_refund'
      AND CONSTRAINT_NAME = 'CK_fn_payment_refund_RefundStateKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_payment_refund ADD CONSTRAINT CK_fn_payment_refund_RefundStateKey CHECK (RefundStateKey IN (''created'', ''processing'', ''succeeded'', ''failed'', ''closed'', ''provider_unknown''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_k3cloud_document_sync'
      AND CONSTRAINT_NAME = 'CK_fn_k3cloud_document_sync_StatusKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_k3cloud_document_sync DROP CHECK CK_fn_k3cloud_document_sync_StatusKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_k3cloud_document_sync'
      AND CONSTRAINT_NAME = 'CK_fn_k3cloud_document_sync_StatusKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_k3cloud_document_sync ADD CONSTRAINT CK_fn_k3cloud_document_sync_StatusKey CHECK (StatusKey IN (''pending'', ''save_succeeded'', ''submitted'', ''save_failed'', ''submit_failed'', ''provider_unknown''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ocr_id_card_task'
      AND CONSTRAINT_NAME = 'CK_fn_ocr_id_card_task_StatusKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists > 0,
    'ALTER TABLE fn_ocr_id_card_task DROP CHECK CK_fn_ocr_id_card_task_StatusKey',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @constraint_exists := (
    SELECT COUNT(*)
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_ocr_id_card_task'
      AND CONSTRAINT_NAME = 'CK_fn_ocr_id_card_task_StatusKey'
      AND CONSTRAINT_TYPE = 'CHECK');
SET @ddl := IF(
    @constraint_exists = 0,
    'ALTER TABLE fn_ocr_id_card_task ADD CONSTRAINT CK_fn_ocr_id_card_task_StatusKey CHECK (StatusKey IN (''pending'', ''recognized'', ''failed'', ''confirmed'', ''rejected'', ''provider_unknown''))',
    'SELECT 1');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
