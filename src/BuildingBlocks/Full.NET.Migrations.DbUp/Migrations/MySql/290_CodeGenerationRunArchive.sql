-- CREATE IF NOT EXISTS 支持 DbUp 未记账后的重跑，不依赖运行或模板的外键。
CREATE TABLE IF NOT EXISTS fn_codegeneration_run_archive (
    Id binary(16) NOT NULL COMMENT '与生成运行相同的应用 UUID v7',
    SchemaJson longtext NOT NULL COMMENT '当次规范 Schema，包含冻结关系输入',
    SchemaSha256 varchar(64) NOT NULL COMMENT '规范 Schema 摘要',
    GeneratorVersion varchar(64) NOT NULL COMMENT '生成器版本',
    ManifestSha256 varchar(64) NOT NULL COMMENT '当次产物或完整工作区清单摘要',
    ZipSha256 varchar(64) NOT NULL COMMENT '保存的精确 zip 字节摘要',
    FileName varchar(256) NOT NULL COMMENT '鉴权下载的安全文件名',
    ByteLength bigint NOT NULL COMMENT '原始 zip 字节数',
    ExpiresAtUtc datetime(6) NOT NULL COMMENT '冻结的到期时刻，到期只删除包',
    CONSTRAINT PK_fn_codegeneration_run_archive PRIMARY KEY (Id)
) COMMENT='代码生成不可变下载包元数据及独立保留期限';
