-- 结构恢复不会重置投递次数、幂等键或失败事实。
CREATE TABLE IF NOT EXISTS fn_identity_oidc_logout_delivery (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7，亦为 logout_token 的稳定 jti',
    CenterSessionId binary(16) NOT NULL COMMENT '应撤销的中心浏览器会话 sid',
    UserId binary(16) NOT NULL COMMENT '会话所属用户 sub',
    ClientId varchar(256) NOT NULL COMMENT '接收方 client_id 和令牌 audience',
    TargetUri varchar(2048) NOT NULL COMMENT '撤销事务冻结的部署许可 HTTPS 接收端',
    StatusKey varchar(16) NOT NULL COMMENT 'pending delivered 或 failed',
    AttemptCount int NOT NULL COMMENT '本轮已领取投递次数',
    NextAttemptAtUtc datetime(6) NOT NULL COMMENT '下次可重试时间',
    LeaseId binary(16) NULL COMMENT 'Worker 本次投递租约标识',
    LeaseUntilUtc datetime(6) NULL COMMENT 'Worker 租约截止时间',
    LastErrorCode varchar(64) NULL COMMENT '不含令牌或异常详情的稳定失败码',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '首次投递意图时间',
    DeliveredAtUtc datetime(6) NULL COMMENT '确认送达时间',
    CONSTRAINT PK_fn_identity_oidc_logout_delivery PRIMARY KEY (Id),
    CONSTRAINT UQ_fn_identity_oidc_logout_delivery_session_client UNIQUE (CenterSessionId, ClientId),
    INDEX IX_fn_identity_oidc_logout_delivery_due (StatusKey, NextAttemptAtUtc, Id)
) COMMENT='OIDC 标准退出通知投递状态';
