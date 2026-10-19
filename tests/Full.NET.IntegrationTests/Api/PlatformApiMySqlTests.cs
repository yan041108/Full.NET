using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Platform;

namespace Full.NET.IntegrationTests.Api;

/// <summary>集成测试：PlatformApiMySql。</summary>
[TestClass]
public sealed class PlatformApiMySqlTests
{
    [TestMethod]
    public async Task Release_note_lifecycle_with_mysql()
    {
        using var factory = new FullNetApiFactory(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

        await PlatformReleaseNoteAssertions.VerifyAsync(factory);
    }
}
