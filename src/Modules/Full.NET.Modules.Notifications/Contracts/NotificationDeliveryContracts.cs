namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>投递只读视图；不回显收件地址或 Provider 原文。</summary>
public sealed record NotificationDeliveryResponse(
    Guid Id,
    Guid IntentId,
    Guid RecipientId,
    string ChannelKey,
    Guid? ProviderProfileVersionId,
    Guid? BindingVersionId,
    string StatusKey,
    long Revision,
    DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<NotificationDeliveryAttemptResponse> Attempts,
    IReadOnlyList<NotificationDeliveryReceiptResponse> Receipts);

/// <summary>外部渠道回执只读视图；退信原因使用稳定外部状态键，不含原始载荷。</summary>
public sealed record NotificationDeliveryReceiptResponse(
    Guid Id,
    string ProviderTypeKey,
    string? ProviderMessageId,
    string ExternalStatusKey,
    string MappedStatusKey,
    string ProcessStatusKey,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc);

/// <summary>单次 Provider 调用记录；错误码为闭合类别，不含异常正文。</summary>
public sealed record NotificationDeliveryAttemptResponse(
    Guid Id,
    int AttemptNumber,
    string StatusKey,
    string? ResultCategoryKey,
    string? ProviderMessageId,
    string? ErrorCode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc);

/// <summary>人工重试；<c>Revision</c> 为 CAS 期望值，理由只允许短稳定文本。</summary>
public sealed record RetryNotificationDeliveryRequest(
    long Revision,
    string Reason);

/// <summary>回执受理结果；重复与乱序不回退终态。</summary>
public sealed record NotificationReceiptAcceptedResponse(
    Guid Id,
    string ProcessStatusKey,
    string MappedStatusKey);
