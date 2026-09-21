namespace Full.NET.Modules.Files.Contracts;

/// <summary>Host 文件批量操作的有界上限；防止单次请求占用过多连接与存储写入。</summary>
public static class HostFileBatchLimits
{
    /// <summary>单次批量上传允许的最大文件数。</summary>
    public const int MaxUploadCount = 20;

    /// <summary>单次批量删除允许的最大文件标识数。</summary>
    public const int MaxDeleteCount = 100;
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>批量删除 Host 文件请求。</summary>
/// <param name="FileIds">待删除的文件标识集合；顺序决定结果回显顺序。</param>
public sealed record BatchDeleteHostFilesRequest(
    IReadOnlyList<Guid> FileIds);

/// <summary>批量删除单条结果。</summary>
/// <param name="FileId">本条结果对应的文件标识。</param>
/// <param name="Succeeded">本条是否删除成功。</param>
/// <param name="ErrorCode">失败时返回稳定错误码；成功时为 <see langword="null"/>。</param>
/// <param name="Message">失败时的可读说明；成功时为 <see langword="null"/>。</param>
public sealed record BatchDeleteHostFileItem(
    Guid FileId,
    bool Succeeded,
    string? ErrorCode,
    string? Message);

/// <summary>批量删除汇总。</summary>
/// <param name="SucceededCount">实际删除成功的文件数。</param>
/// <param name="Results">逐条结果；顺序与请求集合一致。</param>
public sealed record BatchDeleteHostFilesResponse(
    int SucceededCount,
    IReadOnlyList<BatchDeleteHostFileItem> Results);

/// <summary>批量上传单条结果。</summary>
/// <param name="OriginalFileName">客户端提交的文件名；用于回显队列状态。</param>
/// <param name="Succeeded">本条是否上传成功。</param>
/// <param name="File">上传成功时返回文件元数据；失败时为 <see langword="null"/>。</param>
/// <param name="ErrorCode">失败时返回稳定错误码；成功时为 <see langword="null"/>。</param>
/// <param name="Message">失败时的可读说明；成功时为 <see langword="null"/>。</param>
public sealed record BatchUploadHostFileItem(
    string OriginalFileName,
    bool Succeeded,
    HostFileResponse? File,
    string? ErrorCode,
    string? Message);

/// <summary>批量上传汇总。</summary>
/// <param name="SucceededCount">实际上传成功的文件数。</param>
/// <param name="Results">逐条结果；顺序与表单文件顺序一致。</param>
public sealed record BatchUploadHostFilesResponse(
    int SucceededCount,
    IReadOnlyList<BatchUploadHostFileItem> Results);
