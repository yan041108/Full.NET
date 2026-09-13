using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产 MySQL 数据路径上的只读工具执行。</summary>
[TestClass]
public sealed class AiToolExecutionApiMySqlTests
{
    [TestMethod]
    public async Task Tool_execution_preserves_owner_and_operation_boundaries_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
        await AiToolExecutionAssertions.VerifyAsync(factory);
    }
}
