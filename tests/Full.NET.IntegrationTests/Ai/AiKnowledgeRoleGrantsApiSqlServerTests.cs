using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>SQL Server 提供与 MySQL 一致的角色授权语义。</summary>
[TestClass]
public sealed class AiKnowledgeRoleGrantsApiSqlServerTests
{
    [TestMethod]
    public async Task SqlServer_role_grants_enforce_two_layers_and_authoritative_membership()
    {
        var faults = new AiKnowledgeRoleGrantFaults();
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(), configureTestServices: faults.Configure);
        await AiKnowledgeRoleGrantsApiAssertions.VerifyAsync(factory, faults);
    }
}
