using Full.NET.Data.Abstractions;

namespace Full.NET.IntegrationTests.NativeAot;

[TestClass]
[DoNotParallelize]
public sealed class NativeWorkerSqlServerE2ETests
{
    [TestMethod]
    public async Task SqlServer_native_worker_runs_version_retirement_scan()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeWorkerE2EAssertions.VerifyVersionRetirementAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_worker_runs_persistent_background_runtime()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeWorkerE2EAssertions.VerifyPersistentRuntimeAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_worker_processes_legacy_outbox_terminal_states()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeWorkerE2EAssertions.VerifyLegacyOutboxDeliveryAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_worker_processes_pending_ping_job()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeWorkerE2EAssertions.VerifyJobsPingExecutionAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_worker_reconciles_pending_local_files()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeWorkerE2EAssertions.VerifyFilesUploadReconciliationAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_worker_cleans_deleted_local_files()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeWorkerE2EAssertions.VerifyFilesDeletedBlobCleanupAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task SqlServer_native_worker_reconciles_pending_file_reference_claims()
    {
        if (!NativeWorkerArtifactLocator.TryResolve(out _, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native Worker artifact unavailable.");
        }

        await NativeWorkerE2EAssertions.VerifyFilesReferenceClaimReconciliationAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }
}
