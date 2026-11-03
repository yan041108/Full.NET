-- 逐表创建包含完整索引，任务输入列按元数据恢复，支持部分 DDL 提交后重跑。
-- 固定 DDL 的 PREPARE 跨越无 ADD COLUMN IF NOT EXISTS 的限制，仅部署连接允许用户变量。
-- 不回填旧队列授权；删除结构会丢失配置和冻结输入，回退须先停用消费方并保存数据。
CREATE TABLE IF NOT EXISTS fn_import_export_profile_scope (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7 逻辑主键',
    TenantId binary(16) NOT NULL COMMENT '可信租户标识',
    UserId binary(16) NOT NULL COMMENT '可信用户标识',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '作用域首次创建 UTC 时间',
    CONSTRAINT PK_fn_import_export_profile_scope PRIMARY KEY (Id),
    CONSTRAINT UX_fn_import_export_profile_scope_Tenant_User UNIQUE (TenantId,UserId)
) COMMENT='个人导入配置写入互斥作用域，不参与业务模块事务' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
CREATE TABLE IF NOT EXISTS fn_import_export_profile (
    Id binary(16) NOT NULL COMMENT '应用端 UUID v7 逻辑主键',
    TenantId binary(16) NOT NULL COMMENT '可信租户标识',
    OwnerUserId binary(16) NOT NULL COMMENT '不可变本人配置所有者',
    SchemaKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '静态导入来源键',
    WorksheetKey varchar(128) COLLATE utf8mb4_bin NOT NULL COMMENT '静态工作表键',
    Name varchar(64) NOT NULL COMMENT '本人定义的有界配置名称',
    HeaderFingerprint char(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL COMMENT '有序目标表头的 SHA256 指纹，不表示业务规则版本',
    ConfigurationJson text NOT NULL COMMENT '规范化表头与字面列映射，不含业务行数据',
    CreatedAtUtc datetime(6) NOT NULL COMMENT '创建 UTC 时间',
    UpdatedAtUtc datetime(6) NULL COMMENT '最后修改 UTC 时间',
    Version bigint NOT NULL COMMENT '正整数并发版本',
    CONSTRAINT PK_fn_import_export_profile PRIMARY KEY (Id),
    CONSTRAINT CK_fn_import_export_profile_Version CHECK (Version>0),
    CONSTRAINT CK_fn_import_export_profile_Config CHECK (JSON_VALID(ConfigurationJson) AND OCTET_LENGTH(ConfigurationJson)<=16384),
    INDEX IX_fn_import_export_profile_Owner_Source (TenantId,OwnerUserId,SchemaKey,WorksheetKey,CreatedAtUtc DESC,Id DESC)
) COMMENT='按可信租户用户与静态来源隔离的个人导入列映射配置' ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
SET @fn_import_mapping_column = IF(EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='fn_import_export_task' AND column_name='ColumnMappingJson'),
    'SELECT 1', 'ALTER TABLE fn_import_export_task ADD ColumnMappingJson text NULL COMMENT ''创建时固化的有界列映射输入，后台不读取当前个人配置''');
PREPARE fn_import_mapping_column_stmt FROM @fn_import_mapping_column;
EXECUTE fn_import_mapping_column_stmt;
DEALLOCATE PREPARE fn_import_mapping_column_stmt;
SET @fn_import_mapping_check = IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='fn_import_export_task' AND constraint_name='CK_fn_import_export_task_ColumnMappingJson'),
    'SELECT 1', 'ALTER TABLE fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_ColumnMappingJson CHECK (ColumnMappingJson IS NULL OR (JSON_VALID(ColumnMappingJson) AND OCTET_LENGTH(ColumnMappingJson)<=16896))');
PREPARE fn_import_mapping_check_stmt FROM @fn_import_mapping_check;
EXECUTE fn_import_mapping_check_stmt;
DEALLOCATE PREPARE fn_import_mapping_check_stmt;
SET @fn_import_actor_column = IF(EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='fn_import_export_task' AND column_name='ActorSessionBindingJson'),
    'SELECT 1', 'ALTER TABLE fn_import_export_task ADD ActorSessionBindingJson text NULL COMMENT ''本人显式排队操作绑定的会话，后台每阶段重新授权''');
PREPARE fn_import_actor_column_stmt FROM @fn_import_actor_column;
EXECUTE fn_import_actor_column_stmt;
DEALLOCATE PREPARE fn_import_actor_column_stmt;
SET @fn_import_actor_check = IF(EXISTS(SELECT 1 FROM information_schema.table_constraints WHERE constraint_schema=DATABASE() AND table_name='fn_import_export_task' AND constraint_name='CK_fn_import_export_task_ActorSessionBindingJson'),
    'SELECT 1', 'ALTER TABLE fn_import_export_task ADD CONSTRAINT CK_fn_import_export_task_ActorSessionBindingJson CHECK (ActorSessionBindingJson IS NULL OR (JSON_VALID(ActorSessionBindingJson) AND OCTET_LENGTH(ActorSessionBindingJson)<=4096))');
PREPARE fn_import_actor_check_stmt FROM @fn_import_actor_check;
EXECUTE fn_import_actor_check_stmt;
DEALLOCATE PREPARE fn_import_actor_check_stmt;
