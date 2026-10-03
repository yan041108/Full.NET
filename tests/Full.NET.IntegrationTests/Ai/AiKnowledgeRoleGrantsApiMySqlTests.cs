using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>MySQL 真实授权与事务验证。</summary>
[TestClass]
public sealed class AiKnowledgeRoleGrantsApiMySqlTests
{
    [TestMethod]
    public async Task MySql_role_grants_enforce_two_layers_and_authoritative_membership()
    {
        var faults = new AiKnowledgeRoleGrantFaults();
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync(), configureTestServices: faults.Configure);
        await AiKnowledgeRoleGrantsApiAssertions.VerifyAsync(factory, faults);
    }
}
