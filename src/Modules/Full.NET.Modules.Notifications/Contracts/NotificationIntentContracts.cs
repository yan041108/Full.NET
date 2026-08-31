using System.Text.Json;

namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>创建通知意图；TenantId 不得出现在请求体，幂等键不含 SceneKey。</summary>
public sealed record CreateNotificationIntentRequest(
    string ProducerKey,
    string SceneKey,
    string TemplateKey,
    IReadOnlyList<NotificationRecipientInput> Recipients,
    JsonElement Parameters,
    string IdempotencyKey);

/// <summary>收件人输入；本切片仅支持 <c>user</c>，RecipientKey 为用户 Id 的 32 位十六进制。</summary>
public sealed record NotificationRecipientInput(
    string RecipientTypeKey,
    string RecipientKey);

/// <summary>意图受理结果；幂等回放返回同一 Id 与同一 Recipient 集合。</summary>
public sealed record NotificationIntentResponse(
    Guid Id,
    string ProducerKey,
    string SceneKey,
    string IdempotencyKey,
    Guid TemplateVersionId,
    Guid? BindingVersionId,
    string PolicyCategoryKey,
    string DispatchModeKey,
    string StatusKey,
    string RouteSnapshotJson,
    string ParameterSnapshotJson,
    IReadOnlyList<NotificationRecipientResponse> Recipients,
    DateTimeOffset CreatedAtUtc);

/// <summary>已解析收件人快照；不回显地址原文。</summary>
public sealed record NotificationRecipientResponse(
    Guid Id,
    string RecipientTypeKey,
    string RecipientKey,
    Guid? UserId,
    string ResolutionStatusKey);
