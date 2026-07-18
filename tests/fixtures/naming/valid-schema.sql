-- 该夹具只覆盖命名门禁支持的静态 SQL 子集，不模拟数据库方言执行。
CREATE TABLE fn_identity_user
(
    Id char(36) NOT NULL,
    NormalizedUsername varchar(256) NOT NULL,
    CreatedAtUtc datetime(6) NOT NULL,
    CONSTRAINT PK_fn_identity_user PRIMARY KEY (Id),
    CONSTRAINT UX_fn_identity_user_NormalizedUsername UNIQUE (NormalizedUsername)
);

CREATE INDEX IX_fn_identity_user_CreatedAtUtc
    ON fn_identity_user(CreatedAtUtc);

SELECT Id, NormalizedUsername
FROM fn_identity_user;

SELECT columnObject.name
FROM sys.columns AS columnObject;

SELECT tableObject.TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES AS tableObject;

CREATE TABLE IF NOT EXISTS fn_identity_session
(
    Id char(36) NOT NULL,
    CONSTRAINT PK_fn_identity_session PRIMARY KEY (Id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS fn_identity_audit
(
    Id char(36) NOT NULL,
    CONSTRAINT PK_fn_identity_audit PRIMARY KEY (Id)
) ENGINE=InnoDB;
