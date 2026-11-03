-- 查询方案为偏好事实；不保存业务结果，无跨模块外键。逐表创建包含完整索引，支持未记账重跑。
CREATE TABLE IF NOT EXISTS fn_settings_query_scheme (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7',
    TenantId binary(16) NULL COMMENT '可信租户；Host上下文为空',
    ScopeKey varchar(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信作用域键；Host为host，租户为tenant加冒号及无连字符UUID',
    GridKey varchar(128) NOT NULL COMMENT '业务所有者发布的稳定Grid键',
    OwnerUserId binary(16) NOT NULL COMMENT '不可变创建主体',
    Name varchar(64) NOT NULL COMMENT '创建者定义的有界方案名',
    SchemaVersion int NOT NULL COMMENT '保存时的业务查询Schema版本',
    ConfigurationJson text NOT NULL COMMENT '有界筛选排序和展示配置，不含查询结果',
    IsShared boolean NOT NULL COMMENT '仅当前租户可共享；Host必须为否',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建UTC时间',
    UpdatedAtUtc datetime(6) NULL COMMENT '最后修改UTC时间',
    Version bigint NOT NULL COMMENT '正整数并发版本',
    CONSTRAINT PK_fn_settings_query_scheme PRIMARY KEY (Id),
    CONSTRAINT CK_fn_settings_query_scheme_Scope CHECK ((TenantId IS NULL AND ScopeKey='host') OR (TenantId IS NOT NULL AND ScopeKey=CONCAT('tenant:',LOWER(REPLACE(BIN_TO_UUID(TenantId),'-',''))))),
    CONSTRAINT CK_fn_settings_query_scheme_Version CHECK (Version>0),
    CONSTRAINT CK_fn_settings_query_scheme_Shared CHECK (IsShared=0 OR TenantId IS NOT NULL),
    CONSTRAINT CK_fn_settings_query_scheme_Config CHECK (OCTET_LENGTH(ConfigurationJson)<=16384),
    INDEX IX_fn_settings_query_scheme_Owner_Created (ScopeKey,GridKey,OwnerUserId,CreatedAtUtc DESC,Id DESC),
    INDEX IX_fn_settings_query_scheme_Shared_Created (ScopeKey,GridKey,IsShared,CreatedAtUtc DESC,Id DESC)
) COMMENT='按可信用户和Host或租户上下文隔离的受控查询方案' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS fn_settings_query_scheme_default (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7',
    TenantId binary(16) NULL COMMENT '可信租户；Host上下文为空',
    ScopeKey varchar(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '可信作用域键；Host为host，租户为tenant加冒号及无连字符UUID',
    GridKey varchar(128) NOT NULL COMMENT '业务所有者发布的稳定Grid键',
    UserId binary(16) NOT NULL COMMENT '当前用户身份',
    DefaultSchemeId binary(16) NULL COMMENT '个人选择；撤销共享或删除后读取时重新核对可见性',
    Version bigint NOT NULL COMMENT '默认选择自己的并发版本；未选择时可为零',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '首次建立用户上下文记录的UTC时间',
    CONSTRAINT PK_fn_settings_query_scheme_default PRIMARY KEY (Id),
    CONSTRAINT CK_fn_settings_query_scheme_default_Scope CHECK ((TenantId IS NULL AND ScopeKey='host') OR (TenantId IS NOT NULL AND ScopeKey=CONCAT('tenant:',LOWER(REPLACE(BIN_TO_UUID(TenantId),'-',''))))),
    CONSTRAINT CK_fn_settings_query_scheme_default_Version CHECK (Version>=0),
    UNIQUE INDEX UX_fn_settings_query_scheme_default_User_Scope_Grid (UserId,ScopeKey,GridKey)
) COMMENT='每用户上下文与Grid的查询方案默认及写入互斥记录' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
