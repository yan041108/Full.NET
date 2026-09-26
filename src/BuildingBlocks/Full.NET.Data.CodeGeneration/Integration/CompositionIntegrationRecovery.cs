using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 登记未完成的 Composition 补偿材料，并在人工审查前拒绝重新接入。
/// </summary>
internal static class CompositionIntegrationRecovery
{
    internal const string MarkerRelativePath = ".fullnet/codegeneration-composition-recovery.pending";
    private const string TemporaryPrefix = ".fullnet-composition-";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void RejectPending(string root, string projectPath, string catalogPath)
    {
        var marker = GenerationWorkspacePath.ResolveFile(root, MarkerRelativePath);
        if (File.Exists(marker))
        {
            throw new GenerationWorkspaceConflictException(
                "Composition 存在待审查的恢复登记，拒绝重新接入。", MarkerRelativePath);
        }

        var parents = new[] { projectPath, catalogPath }
            .Select(path => Path.GetDirectoryName(GenerationWorkspacePath.RevalidateFile(root, path))!)
            .Distinct(StringComparer.Ordinal);
        foreach (var parent in parents)
        {
            if (!Directory.Exists(parent)) continue;
            // 未登记的旧版材料、登记写入失败及进程中断残留也必须阻断；不读取或跟随残留链接。
            var pending = Directory.EnumerateFileSystemEntries(parent)
                .FirstOrDefault(path => Path.GetFileName(path).StartsWith(TemporaryPrefix, StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(path).EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
            if (pending is not null)
            {
                throw new GenerationWorkspaceConflictException(
                    "Composition 存在待审查的恢复或暂存材料，拒绝重新接入。", Relative(root, pending));
            }
        }
    }

    internal static async Task RegisterAsync(
        string root, string projectPath, string catalogPath, string recoveryPath,
        string originalProject, string originalCatalog)
    {
        // 登记只记录受约束的词法路径与可信原文摘要，不读取可能已漂移、被删除或变成链接的恢复副本。
        var content = "fullnet-composition-recovery-v1\n"
            + $"Project={Relative(root, projectPath)}\n"
            + $"Catalog={Relative(root, catalogPath)}\n"
            + $"Recovery={Relative(root, recoveryPath)}\n"
            + $"ExpectedProjectSha256={Hash(originalProject)}\n"
            + $"ExpectedCatalogSha256={Hash(originalCatalog)}\n";
        GenerationWorkspacePath.ResolveFile(root, MarkerRelativePath);
        GenerationWorkspacePath.EnsureParentDirectory(root, MarkerRelativePath);
        var marker = GenerationWorkspacePath.ResolveFile(root, MarkerRelativePath);
        await using var stream = new FileStream(marker, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        });
        // 补偿已失败，取消不得打断登记；即使留下部分登记，后续入口也会保守阻断。
        await stream.WriteAsync(StrictUtf8.GetBytes(content), CancellationToken.None);
        await stream.FlushAsync(CancellationToken.None);
        stream.Flush(flushToDisk: true);
    }

    private static string Relative(string root, string path) =>
        GenerationArtifactPath.Validate(
            Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), nameof(path));

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(content))).ToLowerInvariant();
}

