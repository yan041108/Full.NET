using System.Text.Json;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>
/// 定位 linux-x64 Native AOT publish 产物；运行期仅在 Linux 且产物存在时启用外部进程测试。
/// </summary>
internal static class NativeApiArtifactLocator
{
    public const string PublishDirectoryEnvironmentVariable =
        "FULLNET_NATIVE_AOT_PUBLISH_DIR";

    public const string ExecutableFileName = "Full.NET.Host.Api";

    public const string DefaultPublishRelativeDirectory =
        "artifacts/native-aot/linux-x64/publish";

    public static bool TryResolve(out NativeApiArtifact artifact, out string? skipReason)
    {
        if (!OperatingSystem.IsLinux())
        {
            artifact = default!;
            skipReason =
                "Native AOT 外部进程测试需要 Linux 运行时；请在 Linux CI 或 WSL 中执行。";
            return false;
        }

        var repositoryRoot = FindRepositoryRoot();
        var publishDirectory = Environment.GetEnvironmentVariable(
            PublishDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(publishDirectory))
        {
            publishDirectory = Path.Combine(
                repositoryRoot,
                DefaultPublishRelativeDirectory);
        }

        var executablePath = Path.Combine(publishDirectory, ExecutableFileName);
        if (!File.Exists(executablePath))
        {
            artifact = default!;
            skipReason =
                $"未找到 Native AOT 产物：{executablePath}。请先运行 pnpm test:aot:publish:linux。";
            return false;
        }

        var executableBytes = new FileInfo(executablePath).Length;
        if (executableBytes < 8_000_000)
        {
            artifact = default!;
            skipReason =
                $"Native AOT 产物过小（{executableBytes} bytes）：{executablePath}。";
            return false;
        }

        artifact = new NativeApiArtifact(
            repositoryRoot,
            publishDirectory,
            executablePath,
            executableBytes);
        skipReason = null;
        return true;
    }

    public static NativeApiArtifact RequireArtifact()
    {
        if (!TryResolve(out var artifact, out var skipReason))
        {
            Assert.Inconclusive(skipReason ?? "Native AOT artifact unavailable.");
        }

        return artifact;
    }

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Full.NET repository root.");
    }

    public static bool TryReadPublishManifest(out JsonDocument? manifest)
    {
        manifest = null;
        if (!TryResolve(out var artifact, out _))
        {
            return false;
        }

        var manifestPath = Path.Combine(
            Path.GetDirectoryName(artifact.ExecutablePath)!,
            "..",
            "publish-manifest.json");
        manifestPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return true;
    }
}

internal sealed record NativeApiArtifact(
    string RepositoryRoot,
    string PublishDirectory,
    string ExecutablePath,
    long ExecutableBytes);
