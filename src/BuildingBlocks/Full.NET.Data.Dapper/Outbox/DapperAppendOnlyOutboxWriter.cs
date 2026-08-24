using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 追加式 Outbox Writer（Append-Only Outbox），写入 <c>fn_messaging_outbox_event</c> 表。
/// 该表为仅追加（Append-Only）模式，由 Debezium CDC Connector 捕获 Binlog 变更事件
/// 并直接投递到 Kafka，不经过应用层 Polling Job，属于 CdcKafka 交付模式。
/// </summary>
/// <remarks>
/// <para><b>仅追加不变量（Append-Only Invariant）：</b>
/// 该表仅允许 INSERT，不允许 UPDATE / DELETE。消息状态由下游 Kafka Consumer 在 Inbox 侧管理，
/// Outbox 侧由 CDC 以至少一次语义发布，并由消费端 Inbox 幂等门禁处理重复消息
/// （与 LegacyPolling 的 Lease 机制不同）。</para>
/// <para><b>扩展列：</b>相比传统表，多出 PartitionKey、CorrelationId、CausationId、TraceParent、Producer
/// 等列，支持分布式追踪（W3C TraceContext）、因果链追溯与 Kafka 分区路由。</para>
/// <para><b>无 Metadata 重载：</b>直接抛出 <see cref="NotSupportedException"/>。
/// 原因：CDC Kafka 模式下 PartitionKey 决定消息顺序保序边界，Producer 标识来源实例，
/// 两者缺失将导致下游消费端无法正确路由与追踪，属于不可降级的强约束。</para>
/// <para><b>Scope：</b>SQL 语句声明为 <see cref="SqlDataScope.Global"/>，同传统 Outbox。</para>
/// </remarks>
internal class DapperAppendOnlyOutboxWriter(
    ICommandExecutor commandExecutor,
    IIntegrationEventSerializer serializer,
    IIdGenerator idGenerator,
    ICurrentTenant currentTenant,
    IClock clock) : IOutboxWriter
{
    private static readonly SqlStatement InsertStatement = new(
        "messaging.outbox.append",
        """
        INSERT INTO fn_messaging_outbox_event
            (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
             CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @PartitionKey,
             @CorrelationId, @CausationId, @TraceParent, @Producer, @Payload, @OccurredAtUtc)
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 无 Metadata 重载——追加式 Outbox 强制要求携带 PartitionKey / Producer 等元数据，
    /// 直接抛出 NotSupportedException。
    /// </summary>
    /// <typeparam name="TEvent">事件负载类型。</typeparam>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">事件 Schema 版本号。</param>
    /// <param name="payload">事件负载对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="NotSupportedException">始终抛出；请使用带 metadata 的重载。</exception>
    public virtual Task AddAsync<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Append-only messaging outbox requires IntegrationEventMetadata; use the metadata overload.");

    /// <summary>
    /// 将集成事件与元数据一同写入追加式 Outbox 表（fn_messaging_outbox_event）。
    /// </summary>
    /// <typeparam name="TEvent">事件负载类型。</typeparam>
    /// <param name="eventType">事件类型全限定名，需匹配 <see cref="MessagingNames.MessageTypePattern"/> 且不超过最大长度。</param>
    /// <param name="schemaVersion">事件 Schema 版本号，从 1 开始。</param>
    /// <param name="payload">事件负载对象。</param>
    /// <param name="metadata">集成事件扩展元数据（PartitionKey / Producer / CorrelationId 等），不可 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ArgumentException">当 eventType 格式/长度非法、schemaVersion 小于 1 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当 INSERT 影响行数不为 1 时抛出。</exception>
    public virtual async Task AddAsync<
        [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All)] TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(eventType)
            || eventType.Length > MessagingNames.MessageTypeMaxLength
            || !MessagingNames.MessageTypePattern.IsMatch(eventType))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.MessageTypeInvalid,
                nameof(eventType));
        }

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "The schema version must be at least 1.");
        }

        var message = new AppendOnlyOutboxMessage(
            idGenerator.NewId(),
            eventType,
            schemaVersion,
            serializer.ContentType,
            currentTenant.Id,
            metadata.PartitionKey,
            metadata.CorrelationId,
            metadata.CausationId,
            ResolveTraceParent(),
            metadata.Producer,
            serializer.Serialize(payload),
            clock.UtcNow);

        var affectedRows = await commandExecutor
            .ExecuteAsync(InsertStatement, message, cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Append-only outbox insert affected {affectedRows} rows instead of one.");
        }
    }

    /// <summary>
    /// 仅写入符合 W3C traceparent 格式的当前 Activity 标识；非法格式忽略以免污染 CDC Header。
    /// </summary>
    private static string? ResolveTraceParent()
    {
        var activityId = Activity.Current?.Id;
        if (activityId is null
            || activityId.Length > MessagingNames.TraceParentMaxLength
            || !MessagingNames.TraceParentPattern.IsMatch(activityId))
        {
            return null;
        }

        return activityId;
    }
}
