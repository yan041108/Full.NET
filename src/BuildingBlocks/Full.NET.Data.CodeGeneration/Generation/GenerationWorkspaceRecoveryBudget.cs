using System.Text;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>在检查点和任何源码写入前为恢复材料做总量准入；容量不足时拒绝新操作，不能清理活动证据腾空间。</summary>
public static class GenerationWorkspaceRecoveryBudget
{
    /// <summary>控制面恢复文件总量上限，包含检查点、冻结操作、暂存文件及发布收据。</summary>
    public const long MaximumBytes = 64 * 1024 * 1024;

    /// <summary>按现有文件和本次旧/新正文预留最坏 JSON 转义与双份原子发布空间；只扫描受控元数据目录。</summary>
    public static async Task EnsureAvailableAsync(GenerationWorkspaceLease lease, GenerationWritePlan plan, CancellationToken token = default)
    {
        var root = lease.RequireRoot();
        var snapshot = await GenerationWorkspaceStore.CapturePathsAsync(root, plan.Actions.Select(action => action.RelativePath), token);
        long raw = Encoding.UTF8.GetByteCount(plan.NextManifest?.ToJson() ?? "") + Encoding.UTF8.GetByteCount(plan.PreviousManifest?.ToJson() ?? "");
        foreach (var action in plan.Actions)
        {
            raw = checked(raw + Encoding.UTF8.GetByteCount(action.RelativePath));
            if (action.Kind == GenerationWriteActionKind.Unchanged) continue;
            raw = checked(raw + Encoding.UTF8.GetByteCount(action.Content ?? "")
                + Encoding.UTF8.GetByteCount(snapshot.ExistingFiles.GetValueOrDefault(action.RelativePath) ?? ""));
        }
        var reserved = checked(raw * 24 + 1024 * 1024);
        long used = 0;
        var count = 0;
        var directories = new Stack<string>();
        var metadataRoot = GenerationWorkspacePath.Resolve(root, ".fullnet");
        if (Directory.Exists(metadataRoot)) directories.Push(".fullnet");
        while (directories.TryPop(out var relative))
        {
            token.ThrowIfCancellationRequested();
            var directory = GenerationWorkspacePath.Resolve(root, relative);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 8192) throw new IOException("恢复控制面超过 8192 个条目，禁止继续堆积。");
                var child = Path.GetRelativePath(root, path).Replace('\\', '/');
                var validated = GenerationWorkspacePath.Resolve(root, child);
                if (Directory.Exists(validated)) directories.Push(child);
                else used = checked(used + new FileInfo(validated).Length);
            }
        }
        if (reserved > MaximumBytes || used > MaximumBytes - reserved)
            throw new IOException("恢复材料总量预算不足；请先按保留策略归档已失效记录，活动与回滚链证据不得删除。");
    }
}
