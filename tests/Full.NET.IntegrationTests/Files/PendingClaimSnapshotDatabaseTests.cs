using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.Files;

/// <summary>独立覆盖探测期间确认或刷新的真实双库边界，避免为该修复运行整个文件管理旅程。</summary>
[TestClass]
public sealed class PendingClaimSnapshotDatabaseTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Pending_release_respects_the_observed_state_and_revision(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection);
        await factory.InitializeAsync();
        await PendingClaimSnapshotAssertions.VerifyAsync(factory, TestContext.CancellationToken);
    }

    public TestContext TestContext { get; set; } = null!;
}
