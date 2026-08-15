using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 传统 Outbox Writer（Legacy Polling Outbox），写入 <c>fn_outbox_message</c> 表。
/// 该表由后台 Polling Job 定期扫描领取并投递，适用于切流前的 LegacyPolling 交付模式。
/// </summary>
/// <remarks>
/// <para><b>数据模型：</b>每条消息包含 Id（应用层预分配）、MessageType、SchemaVersion、
/// ContentType、TenantId、TraceId、序列化 Payload、OccurredAtUtc、Attempts（初始 0）。</para>
/// <para><b>Scope：</b>SQL 语句声明为 <see cref="SqlDataScope.Global"/>，
/// 原因：Outbox 表为全局共享表，TenantId 以普通列形式携带而非通过 WHERE 过滤。</para>
/// <para><b>Metadata overload 退化：</b>带 <see cref="IntegrationEventMetadata"/> 的重载直接
/// 退化调用无 metadata 版本写入——原因参见 <see cref="DapperRoutedOutboxWriter"/> 类注释。</para>
/// </remarks>
internal class DapperOutboxWriter(
    ICommandExecutor commandExecutor,
    IIntegrationEventSerializer serializer,
    IIdGenerator idGenerator,
    ICurrentTenant currentTenant,
    IClock clock) : IOutboxWriter
{
    private static readonly SqlStatement InsertStatement = new(
        "outbox.insert",
        """
        INSERT INTO fn_outbox_message
            (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAtUtc, Attempts)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @TraceId, @Payload, @OccurredAtUtc, 0)
        """,
        SqlDataScope.Global);

    /// <summary>
    /// 将集成事件序列化后写入传统 Outbox 表（fn_outbox_message）。
    /// </summary>
    /// <typeparam name="TEvent">事件负载类型。</typeparam>
    /// <param name="eventType">事件类型全限定名，与消息契约一一对应。</param>
    /// <param name="schemaVersion">事件 Schema 版本号，从 1 开始。</param>
    /// <param name="payload">事件负载对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ArgumentException">当 eventType 为空、schemaVersion 小于 1 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当 INSERT 影响行数不为 1 时抛出（并发或触发器异常）。</exception>
    public virtual async Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);
        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "The schema version must be at least 1.");
        }

        var message = new OutboxMessage(
            idGenerator.NewId(),
            eventType,
            schemaVersion,
            serializer.ContentType,
            currentTenant.Id,
            Activity.Current?.TraceId.ToString(),
            serializer.Serialize(payload),
            clock.UtcNow);

        var affectedRows = await commandExecutor
            .ExecuteAsync(InsertStatement, message, cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Outbox insert affected {affectedRows} rows instead of one.");
        }
    }

    /// <summary>
    /// 带 IntegrationEventMetadata 的重载——传统 Outbox 不支持扩展元数据，
    /// 直接退化调用无 metadata 版本写入。
    /// </summary>
    /// <typeparam name="TEvent">事件负载类型。</typeparam>
    /// <param name="eventType">事件类型全限定名。</param>
    /// <param name="schemaVersion">事件 Schema 版本号。</param>
    /// <param name="payload">事件负载对象。</param>
    /// <param name="metadata">扩展元数据（PartitionKey / Producer 等），本实现直接忽略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 设计意图：切流前业务模块可以提前使用 metadata overload 编程（在
    /// <see cref="DapperRoutedOutboxWriter"/> 中 owner 仍为 LegacyPolling 时走到这里），
    /// 此时直接退化为无 metadata 版本写入，以便切流当天仅通过 owner 切换即可生效，
    /// 无需修改业务代码。切流后的 CdcKafka 流由
    /// <see cref="DapperAppendOnlyOutboxWriter"/> 单独处理 metadata。
    /// </remarks>
    public virtual Task AddAsync<TEvent>(
        string eventType,
        int schemaVersion,
        TEvent payload,
        IntegrationEventMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        return AddAsync(eventType, schemaVersion, payload, cancellationToken);
    }
}
