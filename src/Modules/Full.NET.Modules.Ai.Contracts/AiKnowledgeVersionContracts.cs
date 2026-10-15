namespace Full.NET.Modules.Ai.Contracts;

/// <summary>不可变文件版本；文件已上传不代表解析或索引完成，不公开存储引用。</summary>
/// <param name="Id">服务端生成的版本标识。</param>
/// <param name="DocumentId">所属文档。</param>
/// <param name="VersionNumber">文档内连续文件版本号。</param>
/// <param name="OriginalFileName">安全文件名。</param>
/// <param name="ContentType">经校验的内容类型。</param>
/// <param name="ContentHash">实际字节 SHA-256。</param>
/// <param name="SizeBytes">实际字节数。</param>
/// <param name="ProcessingStatus">来源待处理、待确认索引或任务已创建的稳定机器码；均不代表活动索引就绪。</param>
/// <param name="CreatedAtUtc">创建时间。</param>
public sealed record AiKnowledgeVersionResponse(Guid Id, Guid DocumentId, int VersionNumber,
    string OriginalFileName, string ContentType, string ContentHash, long SizeBytes,
    string ProcessingStatus, DateTimeOffset CreatedAtUtc);

/// <summary>所有者的持久化导入结果；完成仅表示文件生命周期完成。</summary>
/// <param name="Id">导入操作标识。</param>
/// <param name="DocumentId">所属文档。</param>
/// <param name="VersionId">预先生成的不可变版本标识。</param>
/// <param name="State">稳定操作阶段。</param>
/// <param name="FailureCode">稳定失败码，不包含异常或存储细节。</param>
/// <param name="CreatedAtUtc">操作创建时间。</param>
/// <param name="UpdatedAtUtc">操作更新时间。</param>
public sealed record AiKnowledgeImportResponse(Guid Id, Guid DocumentId, Guid VersionId, string State,
    string? FailureCode, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
