using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 的候选目录必须由资源所有者和当前可信作用域共同保护。</summary>
[TestClass]
public sealed class AiKnowledgeOrganizationCandidatesApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_organization_candidates_require_owner_exact_permission_and_active_scope()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiKnowledgeOrganizationCandidatesApiAssertions.VerifyAsync(factory);
    }
}
