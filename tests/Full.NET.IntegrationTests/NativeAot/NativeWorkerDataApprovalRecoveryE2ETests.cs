using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.DataApproval;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实原生 Worker 领取中断的 DataApproval 请求并启动工作流，防止空扫描掩盖后台闭包缺失。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerDataApprovalRecoveryE2ETests
{
    [TestMethod]
    public Task SqlServer_native_worker_recovers_pending_data_approval() => VerifyAsync(DatabaseProvider.SqlServer);
    [TestMethod]
    public Task MySql_native_worker_recovers_pending_data_approval() => VerifyAsync(DatabaseProvider.MySql);

    private static async Task VerifyAsync(DatabaseProvider provider)
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var reason)) Assert.Inconclusive(reason ?? "Native Worker artifact unavailable.");
        var connection = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        await DataApprovalRecoveryRestartAssertions.VerifyAsync(provider, connection, nativeWorker: true);
    }
}
