using Full.NET.Abstractions.Results;
using Full.NET.Modules.Files.Contracts;

namespace Full.NET.Modules.Files.Storage;

/// <summary>统一已授权文件的对象读取结果；缺失对象返回稳定错误，故障和取消继续传播。</summary>
internal static class FileStorageContentReader
{
    /// <summary>不预先探测对象存在性，避免检查与读取之间的删除竞争及额外存储请求。</summary>
    internal static async Task<Result<Stream>> OpenAsync(
        IFileStorageProvider provider, string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream;
        try
        {
            stream = await provider.OpenReadAsync(storageKey, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 内部路径、对象键和供应商异常不能进入公开 ProblemDetails。
            return Result<Stream>.Failure(new Error(
                FilesErrorCodes.FileNotFound, "The file was not found.", ErrorType.NotFound));
        }
        if (cancellationToken.IsCancellationRequested)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return Result<Stream>.Success(stream);
    }
}
