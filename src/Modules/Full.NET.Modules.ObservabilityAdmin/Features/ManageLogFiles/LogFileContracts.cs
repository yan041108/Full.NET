namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;

/// <summary>表示不暴露服务端路径的日志文件摘要。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">日志文件稳定标识；由服务端生成，不暴露磁盘路径。</param>
/// <param name="FileName">日志文件名（不含目录）。</param>
/// <param name="SizeBytes">文件大小（字节）。</param>
/// <param name="LastModifiedUtc">文件最后修改时间（UTC）。</param>
public sealed record LogFileSummary(
    string Id,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);

/// <summary>表示一次有界尾读结果。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">日志文件稳定标识。</param>
/// <param name="FileName">日志文件名（不含目录）。</param>
/// <param name="Content">从文件尾部读取的文本内容；可能被截断。</param>
/// <param name="BytesRead">本次实际读取的字节数。</param>
/// <param name="IsTruncated">是否因超出单次读取上限而被截断；为 <see langword="true"/> 时 Content 不完整。</param>
public sealed record LogFileTail(
    string Id,
    string FileName,
    string Content,
    int BytesRead,
    bool IsTruncated);

/// <summary>表示已按共享读取方式打开的日志下载句柄。</summary>
/// <remarks>
/// 字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。
/// Content 流由调用方负责释放；下载句柄仅持有共享读锁，不应长期持有。
/// </remarks>
/// <param name="Content">已打开的只读文件流；调用方须在读取完成后释放。</param>
/// <param name="FileName">日志文件名（不含目录）。</param>
/// <param name="SizeBytes">文件大小（字节）。</param>
/// <param name="LastModifiedUtc">文件最后修改时间（UTC）。</param>
public sealed record LogFileDownload(
    Stream Content,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);
