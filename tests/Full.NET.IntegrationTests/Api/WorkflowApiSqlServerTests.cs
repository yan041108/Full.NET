using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Workflow;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class WorkflowApiSqlServerTests
{
    [TestMethod]
    public async Task Draft_and_publish_contracts_hold_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await WorkflowApiAssertions.VerifyAsync(factory);
    }
}
