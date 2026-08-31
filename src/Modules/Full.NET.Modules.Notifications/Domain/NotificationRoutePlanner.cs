using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Domain;

/// <summary>绑定声明的投递路由模式；多个 Enabled Profile 不会自动变成 FanOut。</summary>
internal enum NotificationDispatchMode
{
    Single,
    FanOut,
    Failover,
    Match,
}

/// <summary>Provider 调用失败分类，供 Failover 判断是否允许换厂商。</summary>
internal enum NotificationFailureCategory
{
    Transient,
    RateLimited,
    PermanentContent,
    PermanentRecipient,
    Authentication,
    Unknown,
}

/// <summary>Binding 显式列出的候选 Profile，不是当前启用全集。</summary>
internal sealed record NotificationRouteCandidate(
    string ProfileKey,
    string ChannelKey,
    int Order,
    bool IsEnabled,
    bool MatchesCondition);

/// <summary>路由命中的确定目标；Intent 创建后固定这些键。</summary>
internal sealed record NotificationRouteTarget(string ProfileKey, string ChannelKey);

/// <summary>路由计划；失败时不返回部分目标，避免隐式多发。</summary>
internal sealed record NotificationRoutePlan(
    bool IsSuccess,
    IReadOnlyList<NotificationRouteTarget> Targets,
    string? ErrorCode)
{
    public static NotificationRoutePlan Success(params NotificationRouteTarget[] targets) =>
        new(true, targets, null);

    public static NotificationRoutePlan Failure(string errorCode) =>
        new(false, [], errorCode);
}

/// <summary>
/// 按 Binding 显式候选计算 Delivery 目标。
/// </summary>
/// <remarks>
/// Single 必须恰好一个可用 Profile；FanOut 只对显式列表中启用项多发；
/// Failover 仅在瞬时或频控失败时切换；Match 必须恰好一个条件命中，否则失败关闭。
/// </remarks>
internal static class NotificationRoutePlanner
{
    public static NotificationRoutePlan Plan(
        NotificationDispatchMode mode,
        IReadOnlyList<NotificationRouteCandidate> explicitCandidates,
        NotificationFailureCategory? previousFailure = null,
        string? failedProfileKey = null)
    {
        return mode switch
        {
            NotificationDispatchMode.Single => PlanSingle(explicitCandidates),
            NotificationDispatchMode.FanOut => PlanFanOut(explicitCandidates),
            NotificationDispatchMode.Failover => PlanFailover(explicitCandidates, previousFailure, failedProfileKey),
            NotificationDispatchMode.Match => PlanMatch(explicitCandidates),
            _ => NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteProfileUnavailable),
        };
    }

    private static NotificationRoutePlan PlanSingle(IReadOnlyList<NotificationRouteCandidate> candidates)
    {
        var enabled = Enabled(candidates);
        return enabled.Count == 1
            ? NotificationRoutePlan.Success(ToTarget(enabled[0]))
            : NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteProfileUnavailable);
    }

    private static NotificationRoutePlan PlanFanOut(IReadOnlyList<NotificationRouteCandidate> candidates)
    {
        var enabled = Enabled(candidates);
        return enabled.Count == 0
            ? NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteFanOutEmpty)
            : new NotificationRoutePlan(true, enabled.Select(ToTarget).ToArray(), null);
    }

    private static NotificationRoutePlan PlanFailover(
        IReadOnlyList<NotificationRouteCandidate> candidates,
        NotificationFailureCategory? previousFailure,
        string? failedProfileKey)
    {
        var enabled = Enabled(candidates)
            .OrderBy(candidate => candidate.Order)
            .ThenBy(candidate => candidate.ProfileKey, StringComparer.Ordinal)
            .ToArray();
        if (enabled.Length == 0)
        {
            return NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteProfileUnavailable);
        }

        if (previousFailure is null)
        {
            return NotificationRoutePlan.Success(ToTarget(enabled[0]));
        }

        if (previousFailure is NotificationFailureCategory.PermanentContent
            or NotificationFailureCategory.PermanentRecipient
            or NotificationFailureCategory.Authentication)
        {
            return NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteFailoverPermanent);
        }

        if (previousFailure is not (NotificationFailureCategory.Transient or NotificationFailureCategory.RateLimited))
        {
            return NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteFailoverPermanent);
        }

        var next = enabled
            .SkipWhile(candidate => !string.Equals(candidate.ProfileKey, failedProfileKey, StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault();
        return next is null
            ? NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteFailoverExhausted)
            : NotificationRoutePlan.Success(ToTarget(next));
    }

    private static NotificationRoutePlan PlanMatch(IReadOnlyList<NotificationRouteCandidate> candidates)
    {
        var matched = Enabled(candidates).Where(candidate => candidate.MatchesCondition).ToArray();
        return matched.Length switch
        {
            1 => NotificationRoutePlan.Success(ToTarget(matched[0])),
            0 => NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteMatchNone),
            _ => NotificationRoutePlan.Failure(NotificationsErrorCodes.RouteMatchAmbiguous),
        };
    }

    private static IReadOnlyList<NotificationRouteCandidate> Enabled(
        IReadOnlyList<NotificationRouteCandidate> candidates) =>
        candidates.Where(candidate => candidate.IsEnabled).ToArray();

    private static NotificationRouteTarget ToTarget(NotificationRouteCandidate candidate) =>
        new(candidate.ProfileKey, candidate.ChannelKey);
}
