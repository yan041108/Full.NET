using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Regions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class RegionsApiSqlServerTests
{
    [TestMethod]
    public async Task Administrative_regions_lifecycle_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await RegionsAdministrativeRegionAssertions.VerifyAsync(factory);
    }
}
