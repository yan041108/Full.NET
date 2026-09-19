using System.Security.Cryptography;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// SHA-256 payload digest for shadow comparison; same algorithm as Inbox.
/// </summary>
public static class IntegrationEventPayloadHash
{
    /// <summary>计算指定载荷的 SHA-256 摘要；与 Inbox 端 PayloadHash 共用同一算法，确保跨进程比对结果一致。</summary>
    /// <param name="payload">原始事件载荷字节；不应为空。</param>
    /// <returns>32 字节 SHA-256 摘要；同载荷必返回同字节。</returns>
    public static byte[] Compute(ReadOnlySpan<byte> payload) => SHA256.HashData(payload);

    /// <summary>对两个载荷摘要做字节级相等比较；用于 Shadow 比对路径，禁止退化为字符串比较。</summary>
    /// <param name="left">左侧摘要。</param>
    /// <param name="right">右侧摘要。</param>
    /// <returns>字节序列完全相等返回 <see langword="true"/>；否则 <see langword="false"/>。</returns>
    public static bool Equals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.SequenceEqual(right);
}

/// <summary>
/// Shadow comparison fingerprint without broker position metadata.
/// </summary>
public sealed class ShadowEventFingerprint
{
    /// <summary>事件稳定标识；与 Outbox/Inbox 端的 EventId 严格一致。</summary>
    public Guid EventId { get; }

    /// <summary>规范化事件类型机器码（小写点分四段式）。</summary>
    public string MessageType { get; }

    /// <summary>事件 Schema 版本号，从 1 开始单调递增。</summary>
    public int SchemaVersion { get; }

    /// <summary>Broker 分区键；同一逻辑流应保持稳定以保证顺序。</summary>
    public string PartitionKey { get; }

    /// <summary>载荷 SHA-256 摘要；用于 Shadow 比对中识别内容差异。</summary>
    public byte[] PayloadHash { get; }

    /// <summary>事件发生 UTC 时间；与权威 Outbox OccurredAtUtc 严格对齐。</summary>
    public DateTimeOffset OccurredAtUtc { get; }

    private ShadowEventFingerprint(
        Guid eventId,
        string messageType,
        int schemaVersion,
        string partitionKey,
        byte[] payloadHash,
        DateTimeOffset occurredAtUtc)
    {
        EventId = eventId;
        MessageType = messageType;
        SchemaVersion = schemaVersion;
        PartitionKey = partitionKey;
        PayloadHash = payloadHash;
        OccurredAtUtc = occurredAtUtc;
    }

    /// <summary>从权威 Outbox 信封构造 Shadow 比对指纹；自动计算 PayloadHash。</summary>
    /// <param name="envelope">权威 Outbox 事件信封，不可为 <see langword="null"/>。</param>
    /// <returns>包含稳定标识、类型、版本、分区键、PayloadHash 与时间的指纹实例。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> 为 <see langword="null"/>。</exception>
    public static ShadowEventFingerprint FromEnvelope(IntegrationEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new ShadowEventFingerprint(
            envelope.EventId,
            envelope.MessageType,
            envelope.SchemaVersion,
            envelope.PartitionKey,
            IntegrationEventPayloadHash.Compute(envelope.Payload.Span),
            envelope.OccurredAtUtc);
    }

    /// <summary>
    /// 显式构造 Shadow 比对指纹；对 messageType/schemaVersion/partitionKey 做契约校验，并计算 PayloadHash。
    /// </summary>
    /// <param name="eventId">事件稳定标识。</param>
    /// <param name="messageType">规范化事件类型机器码，需通过 <c>IntegrationEventEnvelope.ValidateMessageType</c>。</param>
    /// <param name="schemaVersion">事件 Schema 版本号，必须为正整数。</param>
    /// <param name="partitionKey">Broker 分区键，需通过 <c>IntegrationEventMetadata.ValidatePartitionKey</c>。</param>
    /// <param name="payload">事件载荷原始字节；不可为空。</param>
    /// <param name="occurredAtUtc">事件发生 UTC 时间。</param>
    /// <returns>已计算 PayloadHash 的指纹实例。</returns>
    /// <exception cref="ArgumentException"><paramref name="messageType"/>、<paramref name="schemaVersion"/>、<paramref name="partitionKey"/> 不符合稳定机器码约束，或 <paramref name="payload"/> 为空。</exception>
    public static ShadowEventFingerprint Create(
        Guid eventId,
        string messageType,
        int schemaVersion,
        string partitionKey,
        ReadOnlySpan<byte> payload,
        DateTimeOffset occurredAtUtc)
    {
        IntegrationEventEnvelope.ValidateMessageType(messageType);
        IntegrationEventEnvelope.ValidateSchemaVersion(schemaVersion);
        IntegrationEventMetadata.ValidatePartitionKey(partitionKey);
        if (payload.IsEmpty)
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.PayloadRequired,
                nameof(payload));
        }

        return new ShadowEventFingerprint(
            eventId,
            messageType,
            schemaVersion,
            partitionKey,
            IntegrationEventPayloadHash.Compute(payload),
            occurredAtUtc);
    }
}

/// <summary>
/// Monotonic CDC or shadow-consumer source position within a provider stream.
/// </summary>
public readonly struct ShadowSourcePosition
    : IComparable<ShadowSourcePosition>, IEquatable<ShadowSourcePosition>
{
    /// <summary>位置来源 Provider 机器码，如 <c>kafka</c> / <c>debezium</c>。</summary>
    public string Provider { get; }

    /// <summary>Provider 内的逻辑流键，如 Topic-Partition。</summary>
    public string StreamKey { get; }

    /// <summary>流内单调非递减序列号；用于检测位置回退。</summary>
    public long Sequence { get; }

    /// <summary>
    /// 构造 Shadow 来源位置；Provider/StreamKey 不可为空，Sequence 必须非负。
    /// </summary>
    /// <param name="provider">位置来源 Provider 机器码。</param>
    /// <param name="streamKey">逻辑流键。</param>
    /// <param name="sequence">流内序列号，必须非负。</param>
    /// <exception cref="ArgumentException"><paramref name="provider"/> 或 <paramref name="streamKey"/> 为空或仅空白字符。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sequence"/> 为负数。</exception>
    public ShadowSourcePosition(string provider, string streamKey, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                sequence,
                "The sequence must be non-negative.");
        }

        Provider = provider;
        StreamKey = streamKey;
        Sequence = sequence;
    }

    /// <summary>
    /// 按 Provider → StreamKey → Sequence 的字典序比较两个位置；同一 (Provider, StreamKey) 内 Sequence 单调递增视为正常。
    /// </summary>
    /// <param name="other">另一位置。</param>
    /// <returns>小于零表示当前在前，零表示相同，大于零表示当前在后。</returns>
    public int CompareTo(ShadowSourcePosition other)
    {
        var providerCompare = StringComparer.Ordinal.Compare(Provider, other.Provider);
        if (providerCompare != 0)
        {
            return providerCompare;
        }

        var streamCompare = StringComparer.Ordinal.Compare(StreamKey, other.StreamKey);
        if (streamCompare != 0)
        {
            return streamCompare;
        }

        return Sequence.CompareTo(other.Sequence);
    }

    /// <summary>按 Provider、StreamKey、Sequence 三元组做相等比较；用于去重与位置回退检测。</summary>
    /// <param name="other">另一位置。</param>
    /// <returns>三元组全部相等返回 <see langword="true"/>；否则 <see langword="false"/>。</returns>
    public bool Equals(ShadowSourcePosition other) =>
        string.Equals(Provider, other.Provider, StringComparison.Ordinal)
        && string.Equals(StreamKey, other.StreamKey, StringComparison.Ordinal)
        && Sequence == other.Sequence;

    /// <summary>按 <see cref="Equals(ShadowSourcePosition)"/> 语义比较；非 <see cref="ShadowSourcePosition"/> 类型直接返回 <see langword="false"/>。</summary>
    /// <param name="obj">待比较对象。</param>
    /// <returns>类型匹配且三元组相等返回 <see langword="true"/>；否则 <see langword="false"/>。</returns>
    public override bool Equals(object? obj) =>
        obj is ShadowSourcePosition other && Equals(other);

    /// <summary>合并三元组哈希；与 <see cref="Equals"/> 语义保持一致。</summary>
    /// <returns>组合哈希值。</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Provider, StreamKey, Sequence);
}

/// <summary>
/// Shadow 比对结果分类；机器码顺序不变，新增类别只能追加，不得重排或重命名既有成员。
/// </summary>
public enum ShadowComparisonOutcome
{
    /// <summary>权威与观测完全一致；可安全推进位置游标。</summary>
    Match = 0,

    /// <summary>观测端出现权威 Outbox 未发布的事件；通常意味着数据丢失或未授权写入。</summary>
    MissingExpected = 1,

    /// <summary>EventId / MessageType / SchemaVersion / PartitionKey 等关键字段不一致；需人工介入定位漂移。</summary>
    FieldMismatch = 2,

    /// <summary>稳定字段一致但 PayloadHash 不一致；通常为序列化非确定性或代码版本错配。</summary>
    PayloadMismatch = 3,

    /// <summary>观测端出现重复事件；可能是 Broker 重排或消费者回退。</summary>
    DuplicateObserved = 4,

    /// <summary>同一 (Provider, StreamKey) 内 Sequence 出现回退；可能为副本切换或客户端断线重连导致。</summary>
    PositionRegression = 5,
}

/// <summary>
/// Shadow comparison evidence; never invokes business handlers.
/// </summary>
public sealed class ShadowEventComparisonResult
{
    /// <summary>本次比对结果分类；用于驱动告警与人工介入策略。</summary>
    public ShadowComparisonOutcome Outcome { get; }

    /// <summary>不一致字段名；非字段级不一致时为 <see langword="null"/>。</summary>
    public string? MismatchField { get; }

    /// <summary>权威 Outbox 端指纹；观测端出现未发布事件时为 <see langword="null"/>。</summary>
    public ShadowEventFingerprint? Expected { get; }

    /// <summary>观测端指纹；位置回退场景下可能为 <see langword="null"/>。</summary>
    public ShadowEventFingerprint? Observed { get; }

    /// <summary>观测到的来源位置；用于位置回退告警的根因定位。</summary>
    public ShadowSourcePosition? ObservedPosition { get; }

    private ShadowEventComparisonResult(
        ShadowComparisonOutcome outcome,
        string? mismatchField,
        ShadowEventFingerprint? expected,
        ShadowEventFingerprint? observed,
        ShadowSourcePosition? observedPosition)
    {
        Outcome = outcome;
        MismatchField = mismatchField;
        Expected = expected;
        Observed = observed;
        ObservedPosition = observedPosition;
    }

    /// <summary>指示本次比对是否完全一致；与 <see cref="ShadowComparisonOutcome.Match"/> 等价。</summary>
    public bool IsMatch => Outcome == ShadowComparisonOutcome.Match;

    /// <summary>构造 Match 结果；权威与观测指纹指向同一实例。</summary>
    /// <param name="fingerprint">一致指纹；同时作为 Expected 与 Observed。</param>
    /// <param name="position">观测位置；用于推进游标。</param>
    /// <returns>Match 类型结果实例。</returns>
    public static ShadowEventComparisonResult Match(
        ShadowEventFingerprint fingerprint,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.Match,
            mismatchField: null,
            expected: fingerprint,
            observed: fingerprint,
            observedPosition: position);

    /// <summary>构造 MissingExpected 结果；用于观测端出现权威未发布事件的场景。</summary>
    /// <param name="observed">观测端指纹。</param>
    /// <param name="position">观测位置。</param>
    /// <returns>MissingExpected 类型结果实例，Expected 字段为 <see langword="null"/>。</returns>
    public static ShadowEventComparisonResult MissingExpected(
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.MissingExpected,
            mismatchField: null,
            expected: null,
            observed: observed,
            observedPosition: position);

    /// <summary>构造 FieldMismatch 结果；用于 EventId/MessageType/SchemaVersion/PartitionKey 等字段不一致。</summary>
    /// <param name="mismatchField">不一致字段名。</param>
    /// <param name="expected">权威端指纹。</param>
    /// <param name="observed">观测端指纹。</param>
    /// <param name="position">观测位置。</param>
    /// <returns>FieldMismatch 类型结果实例。</returns>
    public static ShadowEventComparisonResult FieldMismatch(
        string mismatchField,
        ShadowEventFingerprint expected,
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.FieldMismatch,
            mismatchField: mismatchField,
            expected: expected,
            observed: observed,
            observedPosition: position);

    /// <summary>构造 PayloadMismatch 结果；稳定字段一致但 PayloadHash 不一致。</summary>
    /// <param name="expected">权威端指纹。</param>
    /// <param name="observed">观测端指纹。</param>
    /// <param name="position">观测位置。</param>
    /// <returns>PayloadMismatch 类型结果实例，MismatchField 固定为 PayloadHash。</returns>
    public static ShadowEventComparisonResult PayloadMismatch(
        ShadowEventFingerprint expected,
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.PayloadMismatch,
            mismatchField: nameof(ShadowEventFingerprint.PayloadHash),
            expected: expected,
            observed: observed,
            observedPosition: position);

    /// <summary>构造 DuplicateObserved 结果；观测端出现重复事件。</summary>
    /// <param name="observed">观测端指纹。</param>
    /// <param name="position">观测位置。</param>
    /// <returns>DuplicateObserved 类型结果实例，Expected 字段为 <see langword="null"/>。</returns>
    public static ShadowEventComparisonResult DuplicateObserved(
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.DuplicateObserved,
            mismatchField: null,
            expected: null,
            observed: observed,
            observedPosition: position);

    /// <summary>构造 PositionRegression 结果；同一流内 Sequence 出现回退。</summary>
    /// <param name="previous">先前位置；用于审计与告警对比。</param>
    /// <param name="current">回退到的当前位置。</param>
    /// <returns>PositionRegression 类型结果实例，ObservedPosition 为 <paramref name="current"/>。</returns>
    public static ShadowEventComparisonResult PositionRegression(
        ShadowSourcePosition previous,
        ShadowSourcePosition current) =>
        new(
            ShadowComparisonOutcome.PositionRegression,
            mismatchField: nameof(ShadowSourcePosition.Sequence),
            expected: null,
            observed: null,
            observedPosition: current);
}

/// <summary>
/// Compares authoritative outbox fingerprints with shadow topic observations.
/// </summary>
public sealed class ShadowEventComparator
{
    /// <summary>
    /// 将权威 Outbox 指纹与观测端指纹逐字段比对，并按结果分类返回；不调用任何业务 Handler。
    /// </summary>
    /// <param name="expected">权威 Outbox 指纹；为 <see langword="null"/> 表示权威端未发布该事件。</param>
    /// <param name="observed">观测端指纹，不可为 <see langword="null"/>。</param>
    /// <param name="observedPosition">观测位置；用于位置回退告警与游标推进。</param>
    /// <param name="duplicateObserved">是否已知该观测为重复事件；为 <see langword="true"/> 时直接返回 DuplicateObserved。</param>
    /// <returns>分类后的比对结果；调用方据此决定是否告警、重放或忽略。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="observed"/> 为 <see langword="null"/>。</exception>
    /// <remarks>
    /// 比对顺序固定为：duplicateObserved → expected null → EventId → MessageType → SchemaVersion → PartitionKey → PayloadHash。
    /// 任一字段不一致立即返回 FieldMismatch/PayloadMismatch，避免后续误判。该方法仅做证据收集，绝不触发业务副作用。
    /// </remarks>
    public ShadowEventComparisonResult CompareExpectedToObserved(
        ShadowEventFingerprint? expected,
        ShadowEventFingerprint observed,
        ShadowSourcePosition? observedPosition,
        bool duplicateObserved = false)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (duplicateObserved)
        {
            return ShadowEventComparisonResult.DuplicateObserved(observed, observedPosition);
        }

        if (expected is null)
        {
            return ShadowEventComparisonResult.MissingExpected(observed, observedPosition);
        }

        if (expected.EventId != observed.EventId)
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.EventId),
                expected,
                observed,
                observedPosition);
        }

        if (!string.Equals(
                expected.MessageType,
                observed.MessageType,
                StringComparison.Ordinal))
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.MessageType),
                expected,
                observed,
                observedPosition);
        }

        if (expected.SchemaVersion != observed.SchemaVersion)
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.SchemaVersion),
                expected,
                observed,
                observedPosition);
        }

        if (!string.Equals(
                expected.PartitionKey,
                observed.PartitionKey,
                StringComparison.Ordinal))
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.PartitionKey),
                expected,
                observed,
                observedPosition);
        }

        if (!IntegrationEventPayloadHash.Equals(
                expected.PayloadHash.AsSpan(),
                observed.PayloadHash.AsSpan()))
        {
            return ShadowEventComparisonResult.PayloadMismatch(
                expected,
                observed,
                observedPosition);
        }

        return ShadowEventComparisonResult.Match(expected, observedPosition);
    }

    /// <summary>
    /// 校验同一 (Provider, StreamKey) 内 Sequence 是否单调非递减；回退时返回 PositionRegression。
    /// </summary>
    /// <param name="previous">上一已知位置；为 <see langword="null"/> 表示首次见到该流，直接放行。</param>
    /// <param name="current">当前位置。</param>
    /// <returns>
    /// PositionRegression 表示位置回退；Match 表示正常推进，同时附带一个合成的指纹用于统一调用路径。
    /// </returns>
    /// <remarks>
    /// 跨 (Provider, StreamKey) 不做 Sequence 比较，避免跨流误判；该方法仅做位置守卫，不参与内容比对。
    /// </remarks>
    public ShadowEventComparisonResult ValidateMonotonicPosition(
        ShadowSourcePosition? previous,
        ShadowSourcePosition current)
    {
        if (previous is ShadowSourcePosition previousPosition
            && string.Equals(previousPosition.Provider, current.Provider, StringComparison.Ordinal)
            && string.Equals(previousPosition.StreamKey, current.StreamKey, StringComparison.Ordinal)
            && current.Sequence <= previousPosition.Sequence)
        {
            return ShadowEventComparisonResult.PositionRegression(previousPosition, current);
        }

        return ShadowEventComparisonResult.Match(
            ShadowEventFingerprint.Create(
                Guid.Empty,
                "fullnet.messaging.shadow.position",
                1,
                "shadow-position",
                [0x00],
                DateTimeOffset.UnixEpoch),
            current);
    }
}
