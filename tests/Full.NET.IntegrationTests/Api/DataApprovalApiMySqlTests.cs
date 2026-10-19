using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Identity;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：DataApprovalApiMySql。</summary>
[TestClass]
public sealed class DataApprovalApiMySqlTests
{
    [TestMethod]
    public async Task DataApproval_scenarios_return_standard_contract()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await DataApprovalApiAssertions.VerifyAsync(factory);
    }
}
