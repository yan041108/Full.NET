using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>知识库查询以可信租户与所有者共同过滤；仅使用封闭的静态 SQL 片段。</summary>
internal static class AiKnowledgeSql
{
    private const string Columns = "Id, TenantId, OwnerUserId, Name, Description, IsEnabled, DataClassification, EmbeddingModelConfigId, EmbeddingModelVersion, GenerationModelConfigId, GenerationModelVersion, CreatedAtUtc, UpdatedAtUtc, Version";
    private const string TenantFilter = "TenantId = @TenantId AND OwnerUserId = @OwnerUserId";
    private const string HostFilter = "TenantId IS NULL AND OwnerUserId = @OwnerUserId";
    private const string InsertPrefix = "INSERT INTO fn_ai_knowledge_base (Id, TenantId, OwnerUserId, Name, Description, IsEnabled, DataClassification, CreatedAtUtc, Version) VALUES";
    private const string MetadataUpdate = "UPDATE fn_ai_knowledge_base SET Name = @Name, Description = @Description, IsEnabled = @IsEnabled, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1 WHERE Id = @Id AND Version = @Version AND Version < 2147483647 AND ";
    private const string PolicyUpdate = "UPDATE fn_ai_knowledge_base SET DataClassification = @DataClassification, EmbeddingModelConfigId = @EmbeddingModelConfigId, EmbeddingModelVersion = @EmbeddingModelVersion, GenerationModelConfigId = @GenerationModelConfigId, GenerationModelVersion = @GenerationModelVersion, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1 WHERE Id = @Id AND Version = @Version AND Version < 2147483647 AND ";

    internal static readonly SqlStatement InsertTenant = new("ai.knowledge.insert_tenant", InsertPrefix + " (@Id, @TenantId, @OwnerUserId, @Name, @Description, 1, 'internal', @CreatedAtUtc, 1)", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge.insert_host", InsertPrefix + " (@Id, NULL, @OwnerUserId, @Name, @Description, 1, 'internal', @CreatedAtUtc, 1)", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindTenant = new("ai.knowledge.find_tenant", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE Id = @Id AND {TenantFilter}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindHost = new("ai.knowledge.find_host", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE Id = @Id AND {HostFilter}", SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountTenant = new("ai.knowledge.count_tenant", $"SELECT COUNT(*) FROM fn_ai_knowledge_base WHERE {TenantFilter}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountHost = new("ai.knowledge.count_host", $"SELECT COUNT(*) FROM fn_ai_knowledge_base WHERE {HostFilter}", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServerTenant = new("ai.knowledge.list_sqlserver_tenant", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {TenantFilter} ORDER BY CreatedAtUtc DESC, Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServerHost = new("ai.knowledge.list_sqlserver_host", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {HostFilter} ORDER BY CreatedAtUtc DESC, Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySqlTenant = new("ai.knowledge.list_mysql_tenant", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {TenantFilter} ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySqlHost = new("ai.knowledge.list_mysql_host", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {HostFilter} ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    internal static readonly SqlStatement UpdateTenant = new("ai.knowledge.update_tenant", MetadataUpdate + TenantFilter, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UpdateHost = new("ai.knowledge.update_host", MetadataUpdate + HostFilter, SqlDataScope.HostOnly);
    internal static readonly SqlStatement PolicyTenant = new("ai.knowledge.policy_tenant", PolicyUpdate + TenantFilter, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PolicyHost = new("ai.knowledge.policy_host", PolicyUpdate + HostFilter, SqlDataScope.HostOnly);
}
