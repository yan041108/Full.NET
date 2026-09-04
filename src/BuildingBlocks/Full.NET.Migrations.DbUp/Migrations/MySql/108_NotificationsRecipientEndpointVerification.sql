-- 108：收件端点邮件验证码挑战表；支持 pending → verified 的受控升级，不暴露验证码原文。
CREATE TABLE IF NOT EXISTS fn_notifications_recipient_endpoint_challenge (
    Id BINARY(16) NOT NULL COMMENT '逻辑主键',
    RecipientEndpointId BINARY(16) NOT NULL COMMENT '收件端点标识',
    TenantScopeKey varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL COMMENT '租户作用域唯一键',
    UserId BINARY(16) NOT NULL COMMENT '用户标识',
    CodeHash char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '验证码哈希',
    AttemptCount int NOT NULL DEFAULT 0 COMMENT '已尝试次数',
    MaxAttempts int NOT NULL DEFAULT 5 COMMENT '最大尝试次数',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '过期时间(UTC)',
    ConsumedAtUtc datetime(6) NULL COMMENT '消费时间(UTC)',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_notifications_recipient_endpoint_challenge PRIMARY KEY (Id),
    CONSTRAINT FK_fn_notifications_endpoint_challenge_Endpoint
        FOREIGN KEY (RecipientEndpointId) REFERENCES fn_notifications_recipient_endpoint(Id)
) COMMENT='收件端点验证码挑战表' ENGINE=InnoDB;

SET @hasEndpointChallengeActiveIndex := (
    SELECT COUNT(1)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'fn_notifications_recipient_endpoint_challenge'
      AND INDEX_NAME = 'IX_fn_notifications_endpoint_challenge_Endpoint_Active');

SET @addEndpointChallengeActiveIndex := IF(
    @hasEndpointChallengeActiveIndex = 0,
    'CREATE INDEX IX_fn_notifications_endpoint_challenge_Endpoint_Active ON fn_notifications_recipient_endpoint_challenge(RecipientEndpointId, ConsumedAtUtc, ExpiresAtUtc)',
    'SELECT 1');
PREPARE stmt FROM @addEndpointChallengeActiveIndex;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
