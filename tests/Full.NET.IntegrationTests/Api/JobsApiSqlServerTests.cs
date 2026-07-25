using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Jobs;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class JobsApiSqlServerTests
{
    [TestMethod]
    public async Task Host_job_definition_and_trigger_follow_contract_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await JobsHostDefinitionAssertions.VerifyAsync(factory);
    }
}
