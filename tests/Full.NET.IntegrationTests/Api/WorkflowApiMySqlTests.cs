using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Workflow;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class WorkflowApiMySqlTests
{
    [TestMethod]
    public async Task Draft_and_publish_contracts_hold_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await WorkflowApiAssertions.VerifyAsync(factory);
    }
}
