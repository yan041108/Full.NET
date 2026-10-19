using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Platform;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：PlatformApiSqlServer。</summary>
[TestClass]
public sealed class PlatformApiSqlServerTests
{
    [TestMethod]
    public async Task Release_note_lifecycle_with_sql_server()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());

        await PlatformReleaseNoteAssertions.VerifyAsync(factory);
    }
}
