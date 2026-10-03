using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 与 SQL Server 必须提供一致的角色候选隔离及分页语义。</summary>
[TestClass]
public sealed class AiKnowledgeRoleCandidatesApiMySqlTests
{
    [TestMethod]
    public async Task MySql_role_candidates_require_owner_exact_permission_and_active_scope()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiKnowledgeRoleCandidatesApiAssertions.VerifyAsync(factory);
    }
}
