namespace Full.NET.Modules.Notifications.Providers.DingTalk;

/// <summary>钉钉开放平台 HTTP 边界；实现不得记录 userId、卡片参数或 Secret。</summary>
internal interface IDingTalkTransport
{
    /// <summary>获取 access_token；失败时抛出分类后的 <see cref="DingTalkTransportException"/>。</summary>
    /// <param name="appKey">应用 AppKey。</param>
    /// <param name="appSecret">应用 AppSecret。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<DingTalkAccessToken> GetAccessTokenAsync(
        string appKey,
        string appSecret,
        CancellationToken cancellationToken);

    /// <summary>调用 createAndDeliver 并返回 outTrackId；失败时抛出分类后的异常。</summary>
    /// <param name="accessToken">当前有效的 access_token。</param>
    /// <param name="command">闭合投放命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<string> CreateAndDeliverAsync(
        string accessToken,
        DingTalkCreateAndDeliverCommand command,
        CancellationToken cancellationToken);

    /// <summary>发起钉钉 OA 审批实例并返回 processInstanceId。</summary>
    /// <param name="accessToken">当前有效的 access_token。</param>
    /// <param name="command">闭合创建命令。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<string> CreateProcessInstanceAsync(
        string accessToken,
        DingTalkCreateProcessInstanceCommand command,
        CancellationToken cancellationToken);

    /// <summary>查询钉钉 OA 审批实例镜像状态。</summary>
    /// <param name="accessToken">当前有效的 access_token。</param>
    /// <param name="processInstanceId">钉钉审批实例标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    ValueTask<DingTalkProcessInstanceSnapshot> GetProcessInstanceAsync(
        string accessToken,
        string processInstanceId,
        CancellationToken cancellationToken);
}

/// <summary>钉钉 access_token 及其绝对过期时间。</summary>
internal sealed record DingTalkAccessToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>一次互动卡片 createAndDeliver 所需的闭合参数。</summary>
internal sealed record DingTalkCreateAndDeliverCommand(
    string CardTemplateId,
    string OutTrackId,
    string OpenSpaceId,
    string RobotCode,
    string? CallbackRouteKey,
    IReadOnlyDictionary<string, string> CardParamMap);

/// <summary>一次钉钉 OA 审批实例创建所需的闭合参数。</summary>
internal sealed record DingTalkCreateProcessInstanceCommand(
    string OriginatorUserId,
    string ProcessCode,
    long DeptId,
    long AgentId,
    string Title,
    string? Summary,
    string RequestId);

/// <summary>钉钉审批实例查询投影；只保留镜像同步所需字段。</summary>
internal sealed record DingTalkProcessInstanceSnapshot(
    string ProcessInstanceId,
    string Status,
    string? Result,
    string? BusinessId);

/// <summary>钉钉传输失败分类；Adapter 映射为 Worker 重试语义。</summary>
internal enum DingTalkTransportFailureKind
{
    Permanent,
    Transient,
    RateLimited,
}

/// <summary>分类后的钉钉传输异常。</summary>
internal sealed class DingTalkTransportException(
    DingTalkTransportFailureKind failureKind,
    string message) : Exception(message)
{
    public DingTalkTransportFailureKind FailureKind { get; } = failureKind;
}
