using System.Text.Json.Serialization;

namespace Full.NET.Modules.EnterpriseRequest.Contracts;

/// <summary>业务附件只暴露不透明引用，不包含存储路径或可绕过权限的地址。</summary>
public sealed record EnterpriseRequestAttachmentResponse(Guid Id, Guid FileId, string OriginalFileName,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long SizeBytes,
    DateTimeOffset CreatedAtUtc);

/// <summary>附件列表绑定当前主表快照，便于后续写入使用正确版本。</summary>
public sealed record EnterpriseRequestAttachmentsResponse(Guid RequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RequestVersion,
    string RequestStatus, IReadOnlyList<EnterpriseRequestAttachmentResponse> Items);

/// <summary>上传并绑定成功后的精确附件及主表新版本。</summary>
public sealed record EnterpriseRequestAttachmentMutationResponse(Guid RequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RequestVersion,
    EnterpriseRequestAttachmentResponse Attachment);

/// <summary>移除附件时必须持有读取到的主表版本。</summary>
public sealed record RemoveEnterpriseRequestAttachmentRequest(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version);

/// <summary>移除完成后返回主表新版本，不把存储清理延迟误报成业务回滚。</summary>
public sealed record EnterpriseRequestAttachmentRemovedResponse(Guid RequestId,
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long RequestVersion);
