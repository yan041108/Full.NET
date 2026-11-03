using System.Text;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>在受控工作区元数据目录原子保存恢复材料；调用方必须持有整个操作的工作区租约。</summary>
public static class GenerationWorkspaceMetadata
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>安全读取内部元数据；不存在时返回 null，拒绝链接、目录和路径别名。</summary>
    public static async Task<string?> ReadAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken = default)
    {
        var root = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        Validate(relativePath);
        var path = GenerationWorkspacePath.ResolveFile(root, relativePath);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new IOException("单份工作区恢复元数据超出上限。");
        return await File.ReadAllTextAsync(path, Utf8, cancellationToken);
    }

    /// <summary>先耐久写入同卷临时文件，再原子替换指定元数据，不覆盖业务文件。</summary>
    public static Task WriteAsync(string workspaceRoot, string relativePath, string content,
        CancellationToken cancellationToken = default) => WriteCoreAsync(workspaceRoot, relativePath, content, true, cancellationToken);

    /// <summary>以非覆盖移动发布不可变元数据；其他写入者抢先创建时必须失败，不能覆盖当次证据。</summary>
    public static Task CreateAsync(string workspaceRoot, string relativePath, string content,
        CancellationToken cancellationToken = default) => WriteCoreAsync(workspaceRoot, relativePath, content, false, cancellationToken);

    private static async Task WriteCoreAsync(string workspaceRoot, string relativePath, string content, bool overwrite, CancellationToken cancellationToken)
    {
        var root = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        Validate(relativePath);
        var bytes = Utf8.GetBytes(content);
        if (bytes.Length > 16 * 1024 * 1024) throw new IOException("单份工作区恢复元数据超出上限。");
        GenerationWorkspacePath.EnsureParentDirectory(root, relativePath);
        var target = GenerationWorkspacePath.ResolveFile(root, relativePath);
        var directory = relativePath[..(relativePath.LastIndexOf('/') + 1)];
        var pendingRelative = directory + "." + Path.GetFileName(relativePath) + "." + Guid.NewGuid().ToString("N") + ".pending";
        var pending = GenerationWorkspacePath.ResolveFile(root, pendingRelative);
        await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        GenerationWorkspacePath.RevalidateFile(root, pending);
        target = GenerationWorkspacePath.ResolveFile(root, relativePath);
        try { File.Move(pending, target, overwrite); }
        finally
        {
            // 临时文件从未对外成为权威，移动失败可删除自己创建的原文件，保留目标原文。
            if (File.Exists(pending)) { GenerationWorkspacePath.RevalidateFile(root, pending); File.Delete(pending); }
        }
    }

    private static void Validate(string relativePath)
    {
        GenerationArtifactPath.Validate(relativePath, nameof(relativePath));
        if (!relativePath.StartsWith(".fullnet/", StringComparison.Ordinal)
            || relativePath.Equals(GenerationWorkspaceStore.ManifestRelativePath, StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("操作元数据只能写入受控内部目录，不能代替产物清单提交。");
    }

    /// <summary>仅删除已经通过相同路径防护的控制元数据，不跟随目录链接；调用方负责保留引用关系。</summary>
    public static void Delete(string workspaceRoot, string relativePath)
    {
        Validate(relativePath);
        File.Delete(GenerationWorkspacePath.ResolveFile(GenerationWorkspacePath.NormalizeRoot(workspaceRoot), relativePath));
    }
}
