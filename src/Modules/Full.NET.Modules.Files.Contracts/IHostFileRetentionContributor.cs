namespace Full.NET.Modules.Files.Contracts;

/// <summary>声明某 Host 文件仍被模块引用，阻止 Files 在宽限期前清理未就绪对象。</summary>
public interface IHostFileRetentionContributor
{
    Task<bool> IsFileReferencedAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);
}