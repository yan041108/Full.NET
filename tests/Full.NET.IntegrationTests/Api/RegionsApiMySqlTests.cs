using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Regions;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class RegionsApiMySqlTests
{
    [TestMethod]
    public async Task Administrative_regions_lifecycle_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await RegionsAdministrativeRegionAssertions.VerifyAsync(factory);
    }
}
