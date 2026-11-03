-- 搜索只扫描可信租户的固定近期窗口，额外索引增加创建及软删除维护成本。
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.demo_enterprise_request_enterprise_request') AND name=N'IX_demo_enterprise_request_enterprise_request_Search_Recent')
    CREATE INDEX IX_demo_enterprise_request_enterprise_request_Search_Recent
        ON dbo.demo_enterprise_request_enterprise_request(TenantId,IsDeleted,CreatedAtUtc DESC,Id DESC)
        INCLUDE (OrganizationUnitId,Title,RequestNumber);
