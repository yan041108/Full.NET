-- 领取与开始记录同事务；首次完成后不再更新该尝试，不为历史行补造已经发出的请求。
-- MySQL DDL 非事务；表与索引分别探测，创建完成未记账时可重入，回退须保留已有证据。
CREATE TABLE IF NOT EXISTS fn_webhooks_delivery_attempt (
    Id binary(16) NOT NULL COMMENT '应用端UUID v7尝试标识',
    TenantId binary(16) NOT NULL COMMENT '可信订阅所属企业',
    SubscriptionId binary(16) NOT NULL COMMENT '原订阅标识',
    DeliveryId binary(16) NOT NULL COMMENT '原投递标识',
    ClaimVersion int NOT NULL COMMENT '成功领取时的投递修订，作为本次执行隔离标识',
    StartedAtUtc datetime(6) NOT NULL COMMENT '领取提交的开始时间，不代表HTTP已发送',
    CompletedAtUtc datetime(6) NULL COMMENT '首次确认结果的UTC时间，未确认时为空',
    OutcomeKey varchar(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT 'started、delivered或failed的首次完成状态',
    HttpStatus int NULL COMMENT '已知HTTP应答状态，非HTTP故障为空',
    ErrorCode varchar(128) CHARACTER SET ascii COLLATE ascii_bin NULL COMMENT '安全失败分类，不存原始异常或正文',
    AppliedToDelivery tinyint(1) NULL COMMENT '该完成是否推进原租约版本的投递状态',
    CONSTRAINT PK_fn_webhooks_delivery_attempt PRIMARY KEY (Id),
    CONSTRAINT CK_fn_webhooks_delivery_attempt_Outcome CHECK (
        (OutcomeKey='started' AND CompletedAtUtc IS NULL AND AppliedToDelivery IS NULL)
        OR (OutcomeKey IN ('delivered','failed') AND CompletedAtUtc IS NOT NULL AND AppliedToDelivery IS NOT NULL))
) COMMENT='Webhook逐次尝试的开始及首次完成事实，完成后不可变' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
DROP PROCEDURE IF EXISTS fn_webhooks_attempt_indexes;
DELIMITER $$
CREATE PROCEDURE fn_webhooks_attempt_indexes()
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='fn_webhooks_delivery_attempt' AND INDEX_NAME='UX_fn_webhooks_delivery_attempt_DeliveryClaim') THEN
        CREATE UNIQUE INDEX UX_fn_webhooks_delivery_attempt_DeliveryClaim ON fn_webhooks_delivery_attempt(DeliveryId,ClaimVersion);
    END IF;
END$$
DELIMITER ;
CALL fn_webhooks_attempt_indexes();
DROP PROCEDURE IF EXISTS fn_webhooks_attempt_indexes;
