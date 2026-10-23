namespace Full.NET.Modules.Notifications.Contracts;

/// <summary>用户通知发送偏好；不包含收件端点原值或验证状态。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
public sealed record NotificationPreferenceResponse(
    Guid Id,
    Guid UserId,
    string ChannelKey,
    bool ChannelOptedOut,
    bool MarketingConsentGranted,
    string? QuietHoursJson,
    DateTimeOffset UpdatedAtUtc,
    long Version);

/// <summary>更新本人指定渠道发送偏好；端点管理走独立 API。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
public sealed record UpdateMyNotificationPreferenceRequest(
    string ChannelKey,
    bool ChannelOptedOut,
    bool MarketingConsentGranted,
    string? QuietHoursJson);
