using System.Text;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>覆盖规划、文件、数据库终态与 Git 的文件租约；恢复者必须先取得同一个仍存在的锁文件。</summary>
public sealed class GenerationWorkspaceLease : IAsyncDisposable
{
    private const string LockPath = ".fullnet/codegeneration-operation.lock";
    private const string IdentityPath = ".fullnet/codegeneration-workspace.id";
    private readonly FileStream _stream;
    private readonly string _root;
    private bool _disposed;

    private GenerationWorkspaceLease(FileStream stream, Guid workspaceId, string root)
    {
        _stream = stream;
        WorkspaceId = workspaceId;
        _root = root;
    }

    /// <summary>共享卷持久化的身份，与实例内的挂载绝对路径无关。</summary>
    public Guid WorkspaceId { get; }

    /// <summary>非阻塞取得整个操作的租约；占用时返回 null，文件始终保留以避免 Unix 锁文件换 inode。</summary>
    public static async Task<GenerationWorkspaceLease?> TryAcquireAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var root = GenerationWorkspacePath.NormalizeRoot(workspaceRoot);
        GenerationWorkspacePath.EnsureParentDirectory(root, LockPath);
        var path = GenerationWorkspacePath.ResolveFile(root, LockPath);
        FileStream stream;
        try { stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.Asynchronous); }
        catch (IOException) { return null; }
        try
        {
            var identity = await GenerationWorkspaceMetadata.ReadAsync(root, IdentityPath, cancellationToken);
            Guid id;
            if (identity is null)
            {
                var head = GenerationWorkspacePath.ResolveFile(root, ".fullnet/codegeneration-head.json");
                var hasRecordedIdentity = File.Exists(head);
                foreach (var relative in new[] { ".fullnet/codegeneration-operations", ".fullnet/codegeneration-plans" })
                {
                    var directory = GenerationWorkspacePath.Resolve(root, relative);
                    hasRecordedIdentity |= Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any();
                }
                if (hasRecordedIdentity)
                    throw new GenerationWorkspaceConflictException("工作区身份缺失但仍有已绑定的计划或操作，禁止自动重新初始化。", IdentityPath);
                id = Guid.CreateVersion7();
                await GenerationWorkspaceMetadata.WriteAsync(root, IdentityPath, id.ToString("D") + "\n", cancellationToken);
            }
            else if (!Guid.TryParseExact(identity.TrimEnd('\r', '\n'), "D", out id) || id == Guid.Empty)
                throw new GenerationWorkspaceConflictException("持久化工作区身份无效，禁止自动重新初始化。", IdentityPath);
            var owner = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("D"));
            stream.SetLength(0);
            await stream.WriteAsync(owner, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return new GenerationWorkspaceLease(stream, id, root);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    /// <summary>整个操作完成且没有遗留写任务后才允许释放，不删除锁文件。</summary>
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return _stream.DisposeAsync();
    }

    internal string RequireRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GenerationWorkspacePath.NormalizeRoot(_root);
    }
}
