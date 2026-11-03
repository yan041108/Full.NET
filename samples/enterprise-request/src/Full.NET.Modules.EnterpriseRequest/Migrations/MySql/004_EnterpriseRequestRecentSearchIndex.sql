-- 只依据索引存在性选择两条固定语句；不接受外部对象名或业务输入。
SET @demo_request_search_index_sql = IF(EXISTS(SELECT 1 FROM information_schema.statistics WHERE table_schema=DATABASE()
    AND table_name='demo_enterprise_request_enterprise_request' AND index_name='IX_demo_enterprise_request_enterprise_request_Search_Recent'),
    'SELECT 1', 'CREATE INDEX IX_demo_enterprise_request_enterprise_request_Search_Recent ON demo_enterprise_request_enterprise_request(TenantId,IsDeleted,CreatedAtUtc DESC,Id DESC)');
PREPARE demo_request_search_index FROM @demo_request_search_index_sql;
EXECUTE demo_request_search_index;
DEALLOCATE PREPARE demo_request_search_index;
