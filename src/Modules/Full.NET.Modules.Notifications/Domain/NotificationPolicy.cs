using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>Producer/Scene 声明的通知政策类别，决定偏好能否关闭或延迟发送。</summary>
internal enum NotificationPolicyCategory
{
    Mandatory,
    Transactional,
    Informational,
    Marketing,
}

/// <summary>用户偏好快照；政策求值只读取已解析结果，不查询其他模块。</summary>
internal sealed record NotificationPreferenceSnapshot(
    bool ChannelOptedOut,
    bool MarketingConsentGranted,
    bool InQuietHours);

/// <summary>政策求值结果；Suppressed 的消息不得创建外部 Delivery。</summary>
internal sealed record NotificationPolicyEvaluation(
    bool ShouldDispatchNow,
    bool ShouldDelayForQuietHours,
    bool IsSuppressed,
    string? SuppressionReasonCode);

/// <summary>
/// 按强制、交易、普通、营销优先级求值偏好。
/// </summary>
/// <remarks>
/// 强制消息可绕过普通渠道关闭；营销必须有明确同意，紧急覆盖也不能强行开启。
/// 静默时段只延迟允许延迟的消息，紧急强制场景立即发送。
/// </remarks>
internal static class NotificationPolicy
{
    public static NotificationPolicyEvaluation Evaluate(
        NotificationPolicyCategory category,
        NotificationPreferenceSnapshot preference,
        bool emergencyOverride)
    {
        if (category == NotificationPolicyCategory.Marketing && !preference.MarketingConsentGranted)
        {
            return Suppressed(NotificationsErrorCodes.PolicyMarketingConsentRequired);
        }

        if (preference.ChannelOptedOut && category is NotificationPolicyCategory.Informational or NotificationPolicyCategory.Marketing)
        {
            return Suppressed(NotificationsErrorCodes.PolicySuppressed);
        }

        var delayForQuietHours = preference.InQuietHours
            && !(category == NotificationPolicyCategory.Mandatory && emergencyOverride);
        return new NotificationPolicyEvaluation(
            ShouldDispatchNow: !delayForQuietHours,
            ShouldDelayForQuietHours: delayForQuietHours,
            IsSuppressed: false,
            SuppressionReasonCode: null);
    }

    private static NotificationPolicyEvaluation Suppressed(string reasonCode) =>
        new(false, false, true, reasonCode);
}
