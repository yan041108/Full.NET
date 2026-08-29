namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>定位 Worker linux-x64 Native AOT 产物，并在非 Linux 环境保持发现但跳过执行。</summary>
internal static class NativeWorkerArtifactLocator
{
    public const string PublishDirectoryEnvironmentVariable =
        "FULLNET_WORKER_NATIVE_AOT_PUBLISH_DIR";

    public static bool TryResolve(out NativeWorkerArtifact artifact, out string? skipReason)
    {
        if (!OperatingSystem.IsLinux())
        {
            artifact = default!;
            skipReason = "Worker Native AOT 外部进程测试只在 Linux 执行。";
            return false;
        }

        var repositoryRoot = NativeApiArtifactLocator.FindRepositoryRoot();
        var publishDirectory = Environment.GetEnvironmentVariable(
            PublishDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(publishDirectory))
        {
            publishDirectory = Path.Combine(
                repositoryRoot,
                "artifacts",
                "native-aot",
                "worker",
                "linux-x64",
                "publish");
        }

        var executablePath = Path.Combine(publishDirectory, "Full.NET.Host.Worker");
        if (!File.Exists(executablePath))
        {
            artifact = default!;
            skipReason = $"未找到 Worker Native AOT 产物：{executablePath}。";
            return false;
        }

        if (new FileInfo(executablePath).Length < 8_000_000)
        {
            artifact = default!;
            skipReason = $"Worker Native AOT 产物过小：{executablePath}。";
            return false;
        }

        artifact = new NativeWorkerArtifact(
            repositoryRoot,
            publishDirectory,
            executablePath);
        skipReason = null;
        return true;
    }
}

internal sealed record NativeWorkerArtifact(
    string RepositoryRoot,
    string PublishDirectory,
    string ExecutablePath);
