-- 该夹具用于数据库对象注释门禁：表与列均带 COMMENT / MS_Description。
CREATE TABLE IF NOT EXISTS fn_identity_user
(
    Id char(36) NOT NULL COMMENT '逻辑主键',
    NormalizedUsername varchar(256) NOT NULL COMMENT '规范化用户名',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建时间(UTC)',
    CONSTRAINT PK_fn_identity_user PRIMARY KEY (Id),
    CONSTRAINT UX_fn_identity_user_NormalizedUsername UNIQUE (NormalizedUsername)
) COMMENT='身份认证用户表' ENGINE=InnoDB;
