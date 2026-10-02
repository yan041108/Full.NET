namespace Full.NET.Agents.AgUi;

/// <summary>AG-UI 重放所需的运行快照；身份范围由调用方在 Port 实现中校验。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。RunId/SessionId 必须由可信上下文签发，Port 实现须重新校验归属，不能仅凭 runId 放行。</remarks>
/// <param name="RunId">运行标识；与可信上下文中的 RunId 必须一致。</param>
/// <param name="SessionId">会话标识；用于 AG-UI 通道归属校验。</param>
/// <param name="StatusKey">稳定运行状态键；发布后不可改名。</param>
/// <param name="DefinitionKey">运行所用定义键；用于回放时定位定义版本。</param>
/// <param name="MaxEventSequence">当前已持久化事件的最大序列号，作为重放起点游标。</param>
public sealed record AgentRunAgUiSnapshot(
    Guid RunId,
    Guid SessionId,
    string StatusKey,
    string DefinitionKey,
    long MaxEventSequence);

/// <summary>持久化运行事件；Sequence 为游标去重键。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。Sequence 单调递增且唯一，重放端按 afterSequence 游标去重，不可依赖 Payload 内字段排序。</remarks>
/// <param name="Sequence">事件序号；同一运行内单调递增的去重游标。</param>
/// <param name="EventType">AG-UI 事件类型键；发布后不可改名。</param>
/// <param name="PayloadVersion">Payload 载荷的 Schema 版本；用于反序列化兼容判断。</param>
/// <param name="Payload">序列化后的事件载荷；编码与 Schema 由 EventType 决定。</param>
/// <param name="CreatedAtUtc">事件持久化时间（UTC），仅用于展示，不作为去重键。</param>
public sealed record AgentRunPersistedEvent(
    long Sequence,
    string EventType,
    int PayloadVersion,
    string Payload,
    DateTimeOffset CreatedAtUtc);

/// <summary>步骤与预算摘要，供 STATE_SNAPSHOT 与工作台展示。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。所有数值可为 null，表示尚未汇总；Steps 顺序与执行顺序一致，不应在序列化端重排。</remarks>
/// <param name="InputTokens">累计输入 Token 用量；尚未汇总时为 <see langword="null"/>。</param>
/// <param name="OutputTokens">累计输出 Token 用量；尚未汇总时为 <see langword="null"/>。</param>
/// <param name="UsageStatus">用量状态键；用于前端展示限流或超限提示。</param>
/// <param name="Outcome">运行结局键；运行未结束时为 <see langword="null"/>。</param>
/// <param name="Steps">按执行顺序排列的步骤摘要集合。</param>
public sealed record AgentRunAgUiProgress(
    long? InputTokens,
    long? OutputTokens,
    string? UsageStatus,
    string? Outcome,
    IReadOnlyList<AgentRunAgUiStepSummary> Steps);

/// <summary>AG-UI 运行中单步骤的摘要投影，供进度展示与错误定位使用。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。StepKey/StatusKey/ErrorCode 为稳定机器码，发布后不可改名或删除。</remarks>
/// <param name="StepKey">步骤定义键；用于关联定义与展示文案。</param>
/// <param name="Attempt">步骤第几次尝试；从 1 开始计数，用于重试展示。</param>
/// <param name="StatusKey">步骤状态键；发布后不可改名。</param>
/// <param name="InputTokens">步骤输入 Token 用量；尚未汇总时为 <see langword="null"/>。</param>
/// <param name="OutputTokens">步骤输出 Token 用量；尚未汇总时为 <see langword="null"/>。</param>
/// <param name="ErrorCode">步骤失败时的错误码；成功时为 <see langword="null"/>。</param>
public sealed record AgentRunAgUiStepSummary(
    string StepKey,
    int Attempt,
    string StatusKey,
    long? InputTokens,
    long? OutputTokens,
    string? ErrorCode);

/// <summary>读取本人可访问运行的 AG-UI 重放数据；不得触发工具或模型执行。</summary>
public interface IAgentRunAgUiReader
{
    /// <summary>
    /// 读取当前用户可访问运行的 AG-UI 重放快照；身份与作用域校验失败返回 <see langword="null"/>，不抛出越权异常。
    /// </summary>
    /// <remarks>实现必须基于 actorUserId 与 scopeKey 重新校验归属，不能仅凭 runId 放行；该方法只读，不得触发工具或模型执行。</remarks>
    /// <param name="runId">运行标识。</param>
    /// <param name="scopeKey">作用域键；用于限定可访问的运行集合。</param>
    /// <param name="actorUserId">可信当前用户标识；由调用方注入，不可取自请求体。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当前用户可访问运行的快照；不可访问或不存在时为 <see langword="null"/>。</returns>
    ValueTask<AgentRunAgUiSnapshot?> TryGetOwnedSnapshotAsync(
        Guid runId,
        string scopeKey,
        Guid actorUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 按 afterSequence 游标读取运行事件列表，按 Sequence 升序返回；不校验归属，调用方须先通过 <see cref="TryGetOwnedSnapshotAsync"/> 校验。
    /// </summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="afterSequence">游标序号；返回 Sequence 大于此值的事件。</param>
    /// <param name="limit">单次返回上限；调用方应使用受控值避免一次读取过多。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>Sequence 升序排列的事件列表；游标之后无事件时返回空集合。</returns>
    ValueTask<IReadOnlyList<AgentRunPersistedEvent>> ListEventsAfterAsync(
        Guid runId,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取运行的步骤与预算摘要；身份与作用域校验失败返回 <see langword="null"/>，不抛出越权异常。
    /// </summary>
    /// <param name="runId">运行标识。</param>
    /// <param name="scopeKey">作用域键；用于限定可访问的运行集合。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>当前用户可访问运行的进度摘要；不可访问或不存在时为 <see langword="null"/>。</returns>
    ValueTask<AgentRunAgUiProgress?> TryGetProgressAsync(
        Guid runId,
        string scopeKey,
        CancellationToken cancellationToken = default);
}
