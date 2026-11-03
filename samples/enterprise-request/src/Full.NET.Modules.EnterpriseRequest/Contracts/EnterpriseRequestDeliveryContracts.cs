using System.Text.Json.Serialization;
using Full.NET.Hosting.Serialization;

namespace Full.NET.Modules.EnterpriseRequest.Contracts;

/// <summary>附件、交付结果及敏感金额的精确权限，均限定可信租户和当前对象。</summary>
public static class EnterpriseRequestDeliveryPermissions
{
    /// <summary>列出和下载有权读取单据的附件。</summary>
    public const string AttachmentsRead = "enterprise_request.attachments.read";
    /// <summary>在可写草稿上上传和删除附件。</summary>
    public const string AttachmentsManage = "enterprise_request.attachments.manage";
    /// <summary>创建单据及明细工作簿。</summary>
    public const string ReportsCreate = "enterprise_request.reports.create";
    /// <summary>读取本人任务和下载经再次授权的结果。</summary>
    public const string ReportsRead = "enterprise_request.reports.read";
    /// <summary>取消本人导出任务。</summary>
    public const string ReportsCancel = "enterprise_request.reports.cancel";
    /// <summary>按当前字段权限预览单据打印内容。</summary>
    public const string Print = "enterprise_request.documents.print";
    /// <summary>在交付结果中读取合计、单价及行金额。</summary>
    public const string AmountsRead = "enterprise_request.documents.amounts.read";
}

/// <summary>不透明附件元数据；文件位置和对象键不得进入响应。</summary>
public sealed record EnterpriseRequestAttachmentResponse(Guid Id, Guid RequestId, string FileName, string ContentType,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long SizeBytes, string Phase,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long Version, DateTimeOffset CreatedAtUtc);

/// <summary>通过主单当前版本删除附件。</summary>
public sealed record DeleteEnterpriseRequestAttachmentRequest(
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long RequestVersion,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long AttachmentVersion);

/// <summary>创建固定主单版本的报表；用户身份、字段策略和文件名由服务端确定。</summary>
public sealed record CreateEnterpriseRequestReportRequest(Guid IdempotencyKey,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long RequestVersion);

/// <summary>按已见任务版本取消本人报表。</summary>
public sealed record CancelEnterpriseRequestReportRequest([property: JsonConverter(typeof(StringInt64JsonConverter))] long Version);

/// <summary>报表任务安全状态；就绪与可下载分开，下载仍按当前主体再次授权。</summary>
public sealed record EnterpriseRequestReportResponse(Guid Id, Guid RequestId, string Phase, string? ErrorCode,
    int Attempt, bool IncludesAmounts, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long Version);

/// <summary>服务端固定布局产生的打印预览；客户端须净化并放入隔离帧。</summary>
public sealed record EnterpriseRequestPrintResponse(Guid RequestId,
    [property: JsonConverter(typeof(StringInt64JsonConverter))] long RequestVersion, string Html,
    bool IncludesAmounts, DateTimeOffset GeneratedAtUtc);
