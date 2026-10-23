-- 一次交互最多一份本地许可；历史 accepted 不推导库存。
CREATE TABLE IF NOT EXISTS fn_notifications_subscription_grant (
    Id BINARY(16) NOT NULL COMMENT '应用生成的授权台账标识',
    BindingId BINARY(16) NOT NULL COMMENT '用户与应用绑定标识',
    TemplateId varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '微信订阅模板标识',
    AuthorizationId BINARY(16) NOT NULL COMMENT '客户端订阅交互幂等标识，不代表厂商可信库存',
    ReportedStatusKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '首次客户端上报的不可变接受或拒绝状态，用于拒绝冲突重放',
    StateKey varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '本地授权可用、预留、核销或待对账状态',
    DeliveryId BINARY(16) NULL COMMENT '预留该授权的投递标识',
    LeaseGeneration bigint NULL COMMENT '预留投递租约世代',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '本地授权事实登记时间（UTC）',
    CONSTRAINT PK_fn_notifications_subscription_grant PRIMARY KEY (Id),
    CONSTRAINT UX_fn_notifications_subscription_grant_Authorization UNIQUE (BindingId, TemplateId, AuthorizationId),
    INDEX IX_fn_notifications_subscription_grant_DeliveryId (DeliveryId, StateKey)
) COMMENT='一次性本地订阅同意台账，客户端记录不是厂商库存证明';
