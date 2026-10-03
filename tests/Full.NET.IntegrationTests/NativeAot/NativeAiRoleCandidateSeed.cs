using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>仅原生测试供给租户角色，确保候选查询真实物化非空行而非只验证空响应。</summary>
internal static class NativeAiRoleCandidateSeed
{
    internal static async Task<Guid> InsertAsync(DatabaseProvider provider, string connectionString, Guid tenantId, CancellationToken token)
    {
        await using var connection = CreateConnection(provider, connectionString);
        var id = Guid.CreateVersion7();
        var parameters = new { Id = id, TenantId = tenantId, ScopeKey = $"tenant:{tenantId:N}", Code = $"aot-role-{id:N}",
            CreatedAtUtc = provider == DatabaseProvider.MySql ? (object)DateTime.UtcNow : DateTimeOffset.UtcNow };
        Assert.AreEqual(1, await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO fn_identity_role (Id, TenantId, ScopeKey, Code, Name, IsSystem, IsActive, IsSuperAdministrator, DataScopeKind, CreatedAtUtc, Version)
            VALUES (@Id, @TenantId, @ScopeKey, @Code, 'Native role candidate', 0, 1, 0, 'all', @CreatedAtUtc, 1)
            """, parameters, cancellationToken: token)).ConfigureAwait(false));
        return id;
    }

    /// <summary>保持原会话有效，仅移除权威权限，以单独验证旧 JWT 声明不能绕过端点的实时复核。</summary>
    internal static async Task RevokeCandidatePermissionsAsync(DatabaseProvider provider, string connectionString, Guid roleId, CancellationToken token)
    {
        await using var connection = CreateConnection(provider, connectionString);
        Assert.AreEqual(2, await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM fn_identity_role_permission WHERE RoleId = @RoleId AND PermissionCode IN (@KnowledgePermission, @DocumentPermission)",
            new { RoleId = roleId, KnowledgePermission = AiKnowledgePermissions.RoleCandidatesRead,
                DocumentPermission = AiKnowledgeDocumentPermissions.RoleCandidatesRead }, cancellationToken: token)).ConfigureAwait(false));
    }

    private static DbConnection CreateConnection(DatabaseProvider provider, string connectionString) => provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(connectionString),
        DatabaseProvider.MySql => new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, false)),
        _ => throw new InvalidOperationException("Unsupported native role candidate provider."),
    };
}
