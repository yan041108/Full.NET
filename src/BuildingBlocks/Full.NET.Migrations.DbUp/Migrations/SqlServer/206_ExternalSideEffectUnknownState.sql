-- 206：外部渠道未知状态。支付/退款/金蝶/OCR 在事务外调用后必须能持久化 provider_unknown。
-- SQL Server 以 DROP/ADD CHECK 扩展允许值；脚本可在未记账时重跑。

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_order_TradeStateKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_order'))
    ALTER TABLE dbo.fn_payment_order DROP CONSTRAINT CK_fn_payment_order_TradeStateKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_order_TradeStateKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_order'))
    ALTER TABLE dbo.fn_payment_order
        ADD CONSTRAINT CK_fn_payment_order_TradeStateKey
            CHECK (TradeStateKey IN (
                N'created',
                N'awaiting_payment',
                N'succeeded',
                N'closed',
                N'failed',
                N'refunding',
                N'refunded',
                N'provider_unknown'));

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_refund_RefundStateKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_refund'))
    ALTER TABLE dbo.fn_payment_refund DROP CONSTRAINT CK_fn_payment_refund_RefundStateKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_payment_refund_RefundStateKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_payment_refund'))
    ALTER TABLE dbo.fn_payment_refund
        ADD CONSTRAINT CK_fn_payment_refund_RefundStateKey
            CHECK (RefundStateKey IN (
                N'created',
                N'processing',
                N'succeeded',
                N'failed',
                N'closed',
                N'provider_unknown'));

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_k3cloud_document_sync_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync'))
    ALTER TABLE dbo.fn_k3cloud_document_sync DROP CONSTRAINT CK_fn_k3cloud_document_sync_StatusKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_k3cloud_document_sync_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_k3cloud_document_sync'))
    ALTER TABLE dbo.fn_k3cloud_document_sync
        ADD CONSTRAINT CK_fn_k3cloud_document_sync_StatusKey
            CHECK (StatusKey IN (
                N'pending',
                N'save_succeeded',
                N'submitted',
                N'save_failed',
                N'submit_failed',
                N'provider_unknown'));

IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_ocr_id_card_task_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task'))
    ALTER TABLE dbo.fn_ocr_id_card_task DROP CONSTRAINT CK_fn_ocr_id_card_task_StatusKey;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_fn_ocr_id_card_task_StatusKey'
      AND parent_object_id = OBJECT_ID(N'dbo.fn_ocr_id_card_task'))
    ALTER TABLE dbo.fn_ocr_id_card_task
        ADD CONSTRAINT CK_fn_ocr_id_card_task_StatusKey
            CHECK (StatusKey IN (
                N'pending',
                N'recognized',
                N'failed',
                N'confirmed',
                N'rejected',
                N'provider_unknown'));
