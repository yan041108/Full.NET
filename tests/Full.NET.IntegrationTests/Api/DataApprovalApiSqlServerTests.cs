using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class DataApprovalApiSqlServerTests
{
    [TestMethod]
    public async Task DataApproval_scenarios_return_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await DataApprovalApiAssertions.VerifyAsync(factory);
    }
}
