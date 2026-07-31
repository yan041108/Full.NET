namespace Full.NET.Modules.Files.Storage;

/// <summary>文件对象存储 Provider；对象键由 Files 模块生成，Provider 不得接受客户端提供的物理路径。</summary>
public interface IFileStorageProvider
{
    /// <summary>用于配置和持久化路由的稳定机器码。</summary>
    string ProviderKey { get; }

    /// <summary>以流方式保存对象；成功返回前不得发布部分写入的最终对象。</summary>
    Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken);

    /// <summary>打开对象的只读流。</summary>
    /// <summary>探测最终对象是否存在；不得把暂存对象或部分写入视为已发布。</summary>
    async Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await OpenReadAsync(storageKey, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>幂等删除对象；对象不存在时仍视为成功。</summary>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
