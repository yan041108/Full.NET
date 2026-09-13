using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

[TestClass]
public sealed class AiApprovalRecoveryMySqlTests
{
    [TestMethod]
    public async Task Approval_consume_is_single_use_under_concurrency_async()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiApprovalRecoveryAssertions.VerifyAsync(factory);
    }
}
