namespace Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;

/// <summary>表示不暴露服务端路径的日志文件摘要。</summary>
public sealed record LogFileSummary(
    string Id,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);

/// <summary>表示一次有界尾读结果。</summary>
public sealed record LogFileTail(
    string Id,
    string FileName,
    string Content,
    int BytesRead,
    bool IsTruncated);

/// <summary>表示已按共享读取方式打开的日志下载句柄。</summary>
public sealed record LogFileDownload(
    Stream Content,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);
