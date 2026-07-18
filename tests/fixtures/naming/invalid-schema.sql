-- 每一类违规都必须产生稳定规则码，便于 CI 与债务清单精确匹配。
CREATE TABLE sys_identity_User
(
    Id char(36) NOT NULL,
    bad_column varchar(32) NULL,
    PRIMARY KEY (Id)
);

CREATE INDEX IX_fn_notifications_delivery_attempt_SubscriptionId_RequestedAtUtc_ChannelProvider
    ON sys_identity_User(Id);

SELECT * FROM sys_identity_User;

EXEC(N'ALTER TABLE dbo.sys_identity_User ADD LegacyColumn int NULL;');

CREATE VIEW sys_identity_user_view AS SELECT Id FROM sys_identity_User;
