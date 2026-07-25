using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Files.Storage;

/// <summary>基于本地目录的 Host 文件二进制读写；对象键由模块生成，禁止客户端指定绝对路径。</summary>
internal interface IHostFileBlobStorage
{
    Task SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

internal sealed class LocalHostFileBlobStorage(IOptions<LocalFileStorageOptions> options)
    : IHostFileBlobStorage
{
    public async Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken)
    {
        var fullPath = ResolvePath(storageKey);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Storage key must include a directory segment.");
        Directory.CreateDirectory(directory);
        await using var fileStream = new FileStream(
            fullPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        await content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored blob was not found.", fullPath);
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolvePath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string storageKey)
    {
        var rootPath = options.Value.RootPath?.Trim() ?? string.Empty;
        if (rootPath.Length == 0)
        {
            throw new InvalidOperationException("Files:Local:RootPath is not configured.");
        }

        var normalizedKey = storageKey.Replace('\\', '/').Trim('/');
        if (normalizedKey.Length == 0
            || normalizedKey.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(normalizedKey))
        {
            throw new InvalidOperationException("Storage key is invalid.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(rootPath);
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Storage key escapes the configured root path.");
        }

        return fullPath;
    }
}
