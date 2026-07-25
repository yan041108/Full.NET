using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Jobs;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class JobsApiMySqlTests
{
    [TestMethod]
    public async Task Host_job_definition_and_trigger_follow_contract_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await JobsHostDefinitionAssertions.VerifyAsync(factory);
    }
}
