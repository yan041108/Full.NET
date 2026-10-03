using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 提供与 MySQL 一致的角色授权语义。</summary>
[TestClass]
public sealed class AiKnowledgeOrganizationGrantsApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_organization_grants_enforce_two_layers_and_authoritative_membership()
    {
        var faults = new AiKnowledgeOrganizationGrantFaults();
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), configureTestServices: faults.Configure);
        await AiKnowledgeOrganizationGrantsApiAssertions.VerifyAsync(factory, faults);
    }
}
