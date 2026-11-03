-- 仅按索引元数据选择固定 DDL，支持 MySQL 隐式提交后迁移未记账时恢复。
SET @fn_document_search_index_sql = IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE()
    AND table_name='fn_document_item' AND index_name='IX_fn_document_item_Recent_Search'),
    'SELECT 1', 'CREATE INDEX IX_fn_document_item_Recent_Search ON fn_document_item(TenantId,IsDeleted,CreatedAtUtc DESC,Id DESC)');
PREPARE fn_document_search_index FROM @fn_document_search_index_sql;
EXECUTE fn_document_search_index;
DEALLOCATE PREPARE fn_document_search_index;
