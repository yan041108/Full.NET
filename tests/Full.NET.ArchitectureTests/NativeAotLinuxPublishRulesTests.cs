using System.Text.Json;

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 验证 linux-x64 Native AOT publish 门禁脚本、产物路径与 test-matrix 阈值保持一致。
/// </summary>
[TestClass]
public sealed class NativeAotLinuxPublishRulesTests
{
    [TestMethod]
    public void PublishScript_Exists_AndReferencesSharedContract()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var scriptPath = Path.Combine(
            root,
            "scripts",
            "testing",
            "run-api-aot-publish-linux.mjs");
        var contractPath = Path.Combine(
            root,
            "scripts",
            "testing",
            "api-native-aot-publish-contract.mjs");

        Assert.IsTrue(File.Exists(scriptPath), "缺少 run-api-aot-publish-linux.mjs。");
        Assert.IsTrue(File.Exists(contractPath), "缺少 api-native-aot-publish-contract.mjs。");

        var script = File.ReadAllText(scriptPath);
        var contract = File.ReadAllText(contractPath);
        Assert.Contains("api-native-aot-publish-contract.mjs", script, StringComparison.Ordinal);
        Assert.Contains("FullNetPublishMode", contract, StringComparison.Ordinal);
        Assert.Contains("linux-x64", contract, StringComparison.Ordinal);
    }

    [TestMethod]
    public void PackageJson_ExposesLinuxNativeAotPublishCommand()
    {
        var root = ArchitectureRepositoryRoot.Find();
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "package.json")));
        var scripts = document.RootElement.GetProperty("scripts");
        Assert.IsTrue(
            scripts.TryGetProperty("test:aot:publish:linux", out var command),
            "package.json 必须定义 test:aot:publish:linux。");
        Assert.AreEqual(
            "node scripts/testing/run-api-aot-publish-linux.mjs",
            command.GetString());
    }

    [TestMethod]
    public void TestMatrix_NativeAotPublishGate_MatchesContract()
    {
        var root = ArchitectureRepositoryRoot.Find();
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "eng", "testing", "test-matrix.json")));
        var publishGate = document.RootElement.GetProperty("nativeAotPublish");

        Assert.AreEqual("linux-x64", publishGate.GetProperty("runtimeIdentifier").GetString());
        Assert.AreEqual(
            "artifacts/native-aot/linux-x64/publish/Full.NET.Host.Api",
            publishGate.GetProperty("executableRelativePath").GetString());
        Assert.IsTrue(publishGate.GetProperty("minimumExecutableBytes").GetInt64() > 0);
    }

    [TestMethod]
    public void LinuxNativeAotCiWorkflow_Exists_OnUbuntuRunner()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var workflowPath = Path.Combine(
            root,
            ".github",
            "workflows",
            "api-native-aot-linux.yml");
        Assert.IsTrue(File.Exists(workflowPath), "缺少 api-native-aot-linux.yml CI 工作流。");
        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("pnpm test:aot:publish:linux", workflow, StringComparison.Ordinal);
        Assert.Contains("ubuntu-latest", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("windows-latest", workflow, StringComparison.Ordinal);
    }

    [TestMethod]
    public void LinuxNativeAotCiWorkflow_WatchesPublishToolchainAndDependencyInputs()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var workflow = File.ReadAllText(Path.Combine(
            root,
            ".github",
            "workflows",
            "api-native-aot-linux.yml"));

        string[] requiredPaths =
        [
            "package.json",
            "pnpm-lock.yaml",
            "Directory.Packages.props",
            "eng/docker/Dockerfile.api-native-aot-linux-sdk",
        ];
        foreach (var requiredPath in requiredPaths)
        {
            Assert.Contains(
                $"- '{requiredPath}'",
                workflow,
                StringComparison.Ordinal,
                $"Native AOT CI paths 必须覆盖 {requiredPath}。");
        }
    }

    [TestMethod]
    public void NativeAotDatabaseBootstrap_UsesOnlyMigratorForSchemaAndSeed()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var bootstrap = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Full.NET.IntegrationTests",
            "NativeAot",
            "NativeApiDatabaseBootstrap.cs"));
        var migratorRunner = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Full.NET.IntegrationTests",
            "NativeAot",
            "NativeApiMigratorRunner.cs"));

        Assert.DoesNotContain(
            "FullNetApiFactory",
            bootstrap,
            StringComparison.Ordinal,
            "Native E2E 数据准备不得启动 JIT API 测试宿主。");
        Assert.Contains(
            "SeedProfile.Development.ToCanonicalName()",
            migratorRunner,
            StringComparison.Ordinal,
            "Native E2E 必须通过 JIT Migrator 显式执行 Development seed。");
    }

    [TestMethod]
    public void WorkerNativeAot_HasIsolatedPublishAndDualDatabaseE2EGates()
    {
        var root = ArchitectureRepositoryRoot.Find();
        using var package = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "package.json")));
        using var matrix = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(root, "eng", "testing", "test-matrix.json")));
        var scripts = package.RootElement.GetProperty("scripts");

        Assert.AreEqual(
            "node scripts/testing/run-api-aot-publish-linux.mjs --host worker",
            scripts.GetProperty("test:aot:worker:publish:linux").GetString());
        Assert.AreEqual(
            "node scripts/testing/run-native-aot-worker-e2e.mjs",
            scripts.GetProperty("test:aot:worker:native:e2e").GetString());

        var publishGate = matrix.RootElement.GetProperty("workerNativeAotPublish");
        Assert.AreEqual(
            "artifacts/native-aot/worker/linux-x64/publish/Full.NET.Host.Worker",
            publishGate.GetProperty("executableRelativePath").GetString());
        var integrationGate = matrix.RootElement.GetProperty("workerNativeAotIntegration");
        Assert.AreEqual(12, integrationGate.GetProperty("minimum").GetInt32());

        string[] requiredFiles =
        [
            "scripts/testing/run-native-aot-worker-e2e.mjs",
            ".github/workflows/worker-native-aot-linux.yml",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerArtifactLocator.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessRunner.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerProcessHost.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerOutboxProbe.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerJobsProbe.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerFilesProbe.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerE2EAssertions.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs",
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs",
        ];
        foreach (var relativePath in requiredFiles)
        {
            Assert.IsTrue(
                File.Exists(Path.Combine(root, relativePath)),
                $"Worker Native AOT 发布门禁缺少 {relativePath}。");
        }

        var sqlServerTests = File.ReadAllText(Path.Combine(
            root,
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerSqlServerE2ETests.cs"));
        var mySqlTests = File.ReadAllText(Path.Combine(
            root,
            "tests/Full.NET.IntegrationTests/NativeAot/NativeWorkerMySqlE2ETests.cs"));
        StringAssert.Contains(
            sqlServerTests,
            "SqlServer_native_worker_processes_pending_ping_job");
        StringAssert.Contains(
            mySqlTests,
            "MySql_native_worker_processes_pending_ping_job");
        StringAssert.Contains(
            sqlServerTests,
            "SqlServer_native_worker_reconciles_pending_local_files");
        StringAssert.Contains(
            mySqlTests,
            "MySql_native_worker_reconciles_pending_local_files");
        StringAssert.Contains(
            sqlServerTests,
            "SqlServer_native_worker_cleans_deleted_local_files");
        StringAssert.Contains(
            mySqlTests,
            "MySql_native_worker_cleans_deleted_local_files");
    }
}
