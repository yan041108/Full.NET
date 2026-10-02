namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>投递只读视图；不回显收件地址或 Provider 原文。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。StatusKey、ChannelKey 等稳定键发布后不得改名或删除。</remarks>
/// <param name="Id">投递记录唯一标识。</param>
/// <param name="IntentId">关联通知意图 Id。</param>
/// <param name="RecipientId">收件人用户标识。</param>
/// <param name="ChannelKey">稳定渠道键（如 email、sms）。</param>
/// <param name="ProviderProfileVersionId">本次投递绑定的 Provider Profile 发布版本 Id；未绑定时为 <see langword="null"/>。</param>
/// <param name="BindingVersionId">本次投递使用的收件渠道绑定版本 Id；未绑定时为 <see langword="null"/>。</param>
/// <param name="StatusKey">稳定投递状态键，发布后不得改名。</param>
/// <param name="Revision">CAS 乐观并发期望值；每次状态迁移递增。</param>
/// <param name="NextAttemptAtUtc">下次尝试时间（UTC）；无计划时为 <see langword="null"/>。</param>
/// <param name="CreatedAtUtc">记录创建时间（UTC）。</param>
/// <param name="UpdatedAtUtc">记录最近更新时间（UTC）；未更新时为 <see langword="null"/>。</param>
/// <param name="Attempts">按时间顺序排列的 Provider 调用尝试。</param>
/// <param name="Receipts">按接收时间排列的外部渠道回执。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ExternalStatusKey、MappedStatusKey、ProcessStatusKey 等状态键发布后不得改名。</remarks>
/// <param name="Id">回执记录唯一标识。</param>
/// <param name="ProviderTypeKey">稳定 Provider 类型键，用于选择适配器。</param>
/// <param name="ProviderMessageId">Provider 返回的消息 Id；用于消息级幂等与追踪。</param>
/// <param name="ExternalStatusKey">Provider 原始状态键，保留外部命名空间。</param>
/// <param name="MappedStatusKey">按本模块状态机映射后的状态键。</param>
/// <param name="ProcessStatusKey">回执处理状态键，标识受理结果（如重复、乱序、终态）。</param>
/// <param name="ReceivedAtUtc">回执到达时间（UTC）。</param>
/// <param name="ProcessedAtUtc">回执处理完成时间（UTC）；尚未处理时为 <see langword="null"/>。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。StatusKey、ResultCategoryKey、ErrorCode 等闭合类别发布后不得改名或删除。</remarks>
/// <param name="Id">尝试记录唯一标识。</param>
/// <param name="AttemptNumber">同一投递下的尝试序号，从 1 起递增。</param>
/// <param name="StatusKey">稳定尝试状态键（如 succeeded、failed、timeout）。</param>
/// <param name="ResultCategoryKey">闭合结果类别键；用于失败原因聚合，不含原始异常文本。</param>
/// <param name="ProviderMessageId">Provider 返回的消息 Id；用于幂等去重与回执关联。</param>
/// <param name="ErrorCode">闭合错误码前缀；不含可变异常正文。</param>
/// <param name="StartedAtUtc">调用开始时间（UTC）。</param>
/// <param name="FinishedAtUtc">调用完成时间（UTC）；进行中或未记录时为 <see langword="null"/>。</param>
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
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Revision">CAS 乐观并发期望值；不匹配当前投递 Revision 时拒绝。</param>
/// <param name="Reason">人工重试理由，仅接受短稳定文本，不得携带异常正文或敏感数据。</param>
public sealed record RetryNotificationDeliveryRequest(
    long Revision,
    string Reason);

/// <summary>回执受理结果；重复与乱序不回退终态。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。ProcessStatusKey、MappedStatusKey 等状态键发布后不得改名。</remarks>
/// <param name="Id">回执受理记录唯一标识。</param>
/// <param name="ProcessStatusKey">受理处理状态键（如 accepted、duplicate、outdated）。</param>
/// <param name="MappedStatusKey">映射到投递状态机后的稳定状态键。</param>
public sealed record NotificationReceiptAcceptedResponse(
    Guid Id,
    string ProcessStatusKey,
    string MappedStatusKey);
