using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 在 Migrator 迁移之后执行 Host 管理员与导航引导，复用既有集成测试种子语义。
/// </summary>
internal static class NativeApiDatabaseBootstrap
{
    public static async Task BootstrapAsync(
        DatabaseProvider provider,
        string connectionString,
        IReadOnlyDictionary<string, string?>? settingsOverrides = null,
        CancellationToken cancellationToken = default)
    {
        await NativeApiMigratorRunner.MigrateAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);

        using var factory = new FullNetApiFactory(
            provider,
            connectionString,
            settingsOverrides);
        await factory.InitializeAsync(
                cancellationToken,
                useSchemaTemplate: false)
            .ConfigureAwait(false);
    }
}
