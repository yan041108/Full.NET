namespace Full.NET.Modules.Files.Storage;

/// <summary>文件对象存储 Provider；对象键由 Files 模块生成，Provider 不得接受客户端提供的物理路径。</summary>
public interface IFileStorageProvider
{
    /// <summary>用于配置和持久化路由的稳定机器码。</summary>
    string ProviderKey { get; }

    /// <summary>以流方式保存对象；成功返回前不得发布部分写入的最终对象。</summary>
    /// <param name="storageKey">由 Files 模块生成的稳定对象键；Provider 不得接受客户端提供的物理路径。</param>
    /// <param name="content">待保存的对象内容流；调用方负责释放，实现方不得缓存流引用。</param>
    /// <param name="cancellationToken">用于取消保存操作的令牌。</param>
    Task SaveAsync(
        string storageKey,
        Stream content,
        CancellationToken cancellationToken);

    /// <summary>打开对象的只读流。</summary>
    /// <summary>探测最终对象是否存在；不得把暂存对象或部分写入视为已发布。</summary>
    /// <param name="storageKey">由 Files 模块生成的稳定对象键。</param>
    /// <param name="cancellationToken">用于取消探测操作的令牌。</param>
    /// <returns>true 表示最终对象已发布且可读；false 表示对象不存在或读取失败。</returns>
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

    /// <summary>
    /// 打开对象的只读流；调用方负责释放流，流读取期间对象可能被并发删除。
    /// </summary>
    /// <param name="storageKey">由 Files 模块生成的稳定对象键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对象的只读流；对象不存在时抛出 <see cref="FileNotFoundException"/>。</returns>
    Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken);

    /// <summary>幂等删除对象；对象不存在时仍视为成功。</summary>
    /// <param name="storageKey">由 Files 模块生成的稳定对象键。</param>
    /// <param name="cancellationToken">用于取消删除操作的令牌。</param>
    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
