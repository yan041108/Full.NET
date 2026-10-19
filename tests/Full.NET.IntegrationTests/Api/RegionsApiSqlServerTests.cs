using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Regions;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：RegionsApiSqlServer。</summary>
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
