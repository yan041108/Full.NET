using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>生产 SQL Server 数据路径上的只读工具执行。</summary>
[TestClass]
public sealed class AiToolExecutionApiSqlServerTests
{
    [TestMethod]
    public async Task Tool_execution_preserves_owner_and_operation_boundaries_async()
    {
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        await AiToolExecutionAssertions.VerifyAsync(factory);
    }
}
