-- 独立验证双库建表语法，避免依赖业务迁移的内容或编号。
CREATE TABLE dbo.fn_identity_fixture_application (
    Id uniqueidentifier NOT NULL,
    ClientId nvarchar(256) NOT NULL,
    CONSTRAINT PK_fn_identity_fixture_application PRIMARY KEY (Id)
);

CREATE TABLE IF NOT EXISTS fn_identity_fixture_session (
    Id binary(16) NOT NULL COMMENT '会话标识',
    ActiveSessionKey varchar(64) GENERATED ALWAYS AS (NULL) STORED COMMENT '活动会话键',
    PRIMARY KEY (Id)
) ENGINE=InnoDB COMMENT='会话夹具';

CREATE TABLE fn_identity_fixture_token (
    Id binary(16) NOT NULL COMMENT '令牌标识',
    Payload text NULL COMMENT '载荷',
    PRIMARY KEY (Id)
) COMMENT='令牌夹具' ENGINE=InnoDB;
