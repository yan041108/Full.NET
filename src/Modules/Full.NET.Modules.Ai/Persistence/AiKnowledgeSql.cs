using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>知识库读权限由可信范围及所有者/明确成员过滤；写入仍仅限所有者。</summary>
internal static class AiKnowledgeSql
{
    private const string Columns = "Id, TenantId, OwnerUserId, Name, Description, IsEnabled, DataClassification, EmbeddingModelConfigId, EmbeddingModelVersion, GenerationModelConfigId, GenerationModelVersion, CreatedAtUtc, UpdatedAtUtc, Version";
    private const string TenantFilter = "TenantId = @TenantId AND OwnerUserId = @OwnerUserId";
    private const string HostFilter = "TenantId IS NULL AND OwnerUserId = @OwnerUserId";
    private const string ReadAccess = "(OwnerUserId = @OwnerUserId OR (IsEnabled = 1 AND EXISTS (SELECT 1 FROM fn_ai_knowledge_member AS member WHERE member.KnowledgeBaseId = fn_ai_knowledge_base.Id AND member.UserId = @OwnerUserId)))";
    private const string TenantReadFilter = "TenantId = @TenantId AND " + ReadAccess;
    private const string HostReadFilter = "TenantId IS NULL AND " + ReadAccess;
    private const string InsertPrefix = "INSERT INTO fn_ai_knowledge_base (Id, TenantId, OwnerUserId, Name, Description, IsEnabled, DataClassification, CreatedAtUtc, Version) VALUES";
    private const string MetadataUpdate = "UPDATE fn_ai_knowledge_base SET Name = @Name, Description = @Description, IsEnabled = @IsEnabled, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1 WHERE Id = @Id AND Version = @Version AND Version < 2147483647 AND ";
    private const string PolicyUpdate = "UPDATE fn_ai_knowledge_base SET DataClassification = @DataClassification, EmbeddingModelConfigId = @EmbeddingModelConfigId, EmbeddingModelVersion = @EmbeddingModelVersion, GenerationModelConfigId = @GenerationModelConfigId, GenerationModelVersion = @GenerationModelVersion, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1 WHERE Id = @Id AND Version = @Version AND Version < 2147483647 AND ";

    internal static readonly SqlStatement InsertTenant = new("ai.knowledge.insert_tenant", InsertPrefix + " (@Id, @TenantId, @OwnerUserId, @Name, @Description, 1, 'internal', @CreatedAtUtc, 1)", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement InsertHost = new("ai.knowledge.insert_host", InsertPrefix + " (@Id, NULL, @OwnerUserId, @Name, @Description, 1, 'internal', @CreatedAtUtc, 1)", SqlDataScope.HostOnly);
    internal static readonly SqlStatement FindTenant = new("ai.knowledge.find_tenant", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE Id = @Id AND {TenantFilter}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement FindHost = new("ai.knowledge.find_host", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE Id = @Id AND {HostFilter}", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ReadTenant = new("ai.knowledge.read_tenant", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE Id = @Id AND {TenantReadFilter}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ReadHost = new("ai.knowledge.read_host", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE Id = @Id AND {HostReadFilter}", SqlDataScope.HostOnly);
    internal static readonly SqlStatement CountTenant = new("ai.knowledge.count_tenant", $"SELECT COUNT(*) FROM fn_ai_knowledge_base WHERE {TenantReadFilter}", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement CountHost = new("ai.knowledge.count_host", $"SELECT COUNT(*) FROM fn_ai_knowledge_base WHERE {HostReadFilter}", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListSqlServerTenant = new("ai.knowledge.list_sqlserver_tenant", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {TenantReadFilter} ORDER BY CreatedAtUtc DESC, Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListSqlServerHost = new("ai.knowledge.list_sqlserver_host", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {HostReadFilter} ORDER BY CreatedAtUtc DESC, Id DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", SqlDataScope.HostOnly);
    internal static readonly SqlStatement ListMySqlTenant = new("ai.knowledge.list_mysql_tenant", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {TenantReadFilter} ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement ListMySqlHost = new("ai.knowledge.list_mysql_host", $"SELECT {Columns} FROM fn_ai_knowledge_base WHERE {HostReadFilter} ORDER BY CreatedAtUtc DESC, Id DESC LIMIT @PageSize OFFSET @Offset", SqlDataScope.HostOnly);
    internal static readonly SqlStatement UpdateTenant = new("ai.knowledge.update_tenant", MetadataUpdate + TenantFilter, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement UpdateHost = new("ai.knowledge.update_host", MetadataUpdate + HostFilter, SqlDataScope.HostOnly);
    internal static readonly SqlStatement PolicyTenant = new("ai.knowledge.policy_tenant", PolicyUpdate + TenantFilter, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    internal static readonly SqlStatement PolicyHost = new("ai.knowledge.policy_host", PolicyUpdate + HostFilter, SqlDataScope.HostOnly);
}
