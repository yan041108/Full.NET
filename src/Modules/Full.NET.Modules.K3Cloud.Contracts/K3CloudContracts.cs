namespace Full.NET.Modules.K3Cloud.Contracts;

/// <summary>K3Cloud 连接配置响应。</summary>
public sealed record K3CloudConnectionConfigResponse(
    Guid Id,
    string Name,
    string BaseUrl,
    string AcctId,
    string Username,
    int Lcid,
    bool HasPassword,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset? LastTestedAtUtc,
    string? LastTestStatusKey,
    string? LastTestMessage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int Version);

/// <summary>创建 K3Cloud 连接配置请求。</summary>
public sealed record CreateK3CloudConnectionConfigRequest(
    string Name,
    string BaseUrl,
    string AcctId,
    string Username,
    string Password,
    int Lcid,
    bool IsDefault,
    bool IsEnabled);

/// <summary>更新 K3Cloud 连接配置请求。</summary>
public sealed record UpdateK3CloudConnectionConfigRequest(
    string Name,
    string BaseUrl,
    string AcctId,
    string Username,
    string? Password,
    int Lcid,
    bool IsDefault,
    bool IsEnabled,
    int Version);

/// <summary>K3Cloud 连接测试结果。</summary>
public sealed record TestK3CloudConnectionConfigResult(
    bool Succeeded,
    string Message);

/// <summary>K3Cloud 单据同步响应。</summary>
public sealed record K3CloudDocumentSyncResponse(
    Guid Id,
    Guid ConnectionConfigId,
    string DocumentTypeKey,
    string BusinessKey,
    string StatusKey,
    string? LastStepKey,
    string? ExternalBillId,
    string? ExternalBillNo,
    string? LastErrorCode,
    string? LastErrorMessage,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid CreatedByUserId,
    int Version);

/// <summary>创建 K3Cloud 单据同步请求；仅允许首种固定单据类型。</summary>
public sealed record CreateK3CloudDocumentSyncRequest(
    Guid ConnectionConfigId,
    string DocumentTypeKey,
    string BusinessKey,
    string PayloadJson);
