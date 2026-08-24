using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 只通过 JIT Migrator 完成迁移与 Development seed，避免在原生产物门禁前启动 JIT API。
/// </summary>
internal static class NativeApiDatabaseBootstrap
{
    public static async Task BootstrapAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await NativeApiMigratorRunner.MigrateAsync(
                provider,
                connectionString,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
