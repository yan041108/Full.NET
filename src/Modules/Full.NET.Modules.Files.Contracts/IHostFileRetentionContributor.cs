namespace Full.NET.Modules.Files.Contracts;

/// <summary>声明某 Host 文件仍被模块引用，阻止 Files 在宽限期前清理未就绪对象。</summary>
public interface IHostFileRetentionContributor
{
    /// <summary>
    /// 判断指定 Host 文件是否仍被当前模块引用。
    /// </summary>
    /// <param name="fileId">待检查的 Host 文件标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// 若当前模块仍存在指向该文件的持久化引用则为 <see langword="true"/>，Files 将跳过本轮清理；
    /// 否则为 <see langword="false"/>，允许 Files 在宽限期后清理。实现不得误报引用以避免泄漏未就绪对象。
    /// </returns>
    Task<bool> IsFileReferencedAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);
}