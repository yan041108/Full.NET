using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Jobs;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class JobsApiSqlServerTests
{
    [TestMethod]
    public async Task Host_job_definition_and_trigger_follow_contract_with_sql_server()
    {
        var concurrencyProbe = new JobsBoundedConcurrencyProbe();
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            new Dictionary<string, string?> { ["Jobs:Http:AllowPrivateNetwork"] = "true" },
            configureTestServices: services =>
            {
                JobsBoundedConcurrencyAssertions.ConfigureServices(
                    services,
                    concurrencyProbe);
                JobsRetrySchedulingAssertions.ConfigureServices(services);
            });

        await JobsHostDefinitionAssertions.VerifyAsync(
            factory,
            concurrencyProbe);
    }
}
