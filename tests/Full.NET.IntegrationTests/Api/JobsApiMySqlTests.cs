using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Jobs;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class JobsApiMySqlTests
{
    [TestMethod]
    public async Task Host_job_definition_and_trigger_follow_contract_with_mysql()
    {
        var concurrencyProbe = new JobsBoundedConcurrencyProbe();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            configureTestServices: services =>
                JobsBoundedConcurrencyAssertions.ConfigureServices(
                    services,
                    concurrencyProbe));

        await JobsHostDefinitionAssertions.VerifyAsync(
            factory,
            concurrencyProbe);
    }
}
