using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Inbox;

/// <summary>
/// 消费端集成事件 Inbox，实现 <see cref="IIntegrationEventInbox"/>，
/// 通过 <c>(ConsumerName, MessageId)</c> 唯一索引提供 Exactly-Once 语义的去重门禁，
/// 配合 SHA-256 PayloadHash 检测同 MessageId 下负载被篡改或重放不同内容的攻击。
/// </summary>
/// <remarks>
/// <para><b>Idempotency 不变量（幂等性）：</b>
/// 主键为 (ConsumerName, MessageId)，同一条消息在同一 ConsumerGroup 内只会被 Claim 一次。
/// Claim 操作由 Provider 特定 SQL（SQL Server MERGE / MySQL INSERT ... ON DUPLICATE KEY UPDATE）
/// 在单次往返内完成原子"首次插入或 Failed 状态重置 + 行锁读取"，避免并发下的竞争条件。</para>
/// <para><b>状态机：</b>
/// Processing（持有中，排他处理）→ Processed（终态，再次 Claim 返回 AlreadyProcessed）/
/// Failed（异常终态，允许同 MessageId 重置后重新处理）。
/// 每次状态迁移均带 ExpectedStatus 条件更新，影响行数 != 1 视为并发冲突。</para>
/// <para><b>Payload 完整性：</b>
/// 写入时对原始二进制 Payload 计算 SHA-256（<see cref="SHA256.HashData"/>）并存入 PayloadHash；
/// Claim 时将库内 Hash 与本次收到的 Payload 再计算值做 Span 级逐字节比较。
/// 不一致时返回 <see cref="InboxClaimStatus.PayloadMismatch"/>，阻止中间人篡改或同 ID 复用于不同内容。</para>
/// <para><b>批量预检查（PrecheckBatch）：</b>
/// 消费端拉取到一批消息后先以 JSON 数组形式批量查询 Inbox 状态，
/// 过滤 AlreadyProcessed + PayloadMismatch 后再逐条 Claim，减少单条往返次数。
/// 单批上限 100 条，防止 JSON 解析与 IN 子查询性能退化。</para>
/// </remarks>
internal sealed class DapperIntegrationEventInbox(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions) : IIntegrationEventInbox
{
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;

    /// <summary>
    /// 批量预检查一批消息在当前 Consumer 的 Inbox 状态，
    /// 在不 Claim 行锁的前提下过滤已处理/哈希不匹配项。
    /// </summary>
    /// <param name="consumerName">消费端唯一名称（Consumer Group 内唯一）。</param>
    /// <param name="messages">消息指纹集合（MessageId + 预计算 PayloadHash）。最多 100 条，且 MessageId 必须唯一。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与输入等长且按 Ordinal 对齐的 <see cref="InboxPrecheckResult"/> 数组。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 messages 超过 100 条时抛出。</exception>
    /// <exception cref="ArgumentException">当 messages 含重复 MessageId 时抛出。</exception>
    /// <exception cref="InvalidOperationException">当数据库返回行数与输入不对齐时抛出（数据完整性异常）。</exception>
    public async Task<IReadOnlyList<InboxPrecheckResult>> PrecheckBatchAsync(
        string consumerName,
        IReadOnlyList<InboxMessageFingerprint> messages,
        CancellationToken cancellationToken)
    {
        ValidateConsumerName(consumerName);
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(messages),
                "Inbox batch precheck accepts at most 100 messages.");
        }

        if (messages.Count == 0)
        {
            return [];
        }

        if (messages.Select(message => message.MessageId).Distinct().Count() != messages.Count)
        {
            throw new ArgumentException(
                "Inbox batch precheck cannot contain duplicate MessageId values.",
                nameof(messages));
        }

        var statement = _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => InboxBatchPrecheckSql.SqlServer,
            DatabaseProvider.MySql => InboxBatchPrecheckSql.MySql,
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported."),
        };
        var rows = await queryExecutor.QueryAsync<InboxBatchPrecheckRow>(
                statement,
                new Dictionary<string, object?>
                {
                    ["ConsumerName"] = consumerName,
                    ["MessagesJson"] = BuildMessagesJson(messages),
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count != messages.Count
            || rows.Select(row => row.Ordinal).Distinct().Count() != messages.Count
            || rows.Any(row => row.Ordinal < 0 || row.Ordinal >= messages.Count))
        {
            throw new InvalidOperationException(
                "Inbox batch precheck did not return one unique row for every input message.");
        }

        var rowsByOrdinal = rows.ToDictionary(row => row.Ordinal);
        return messages.Select((message, ordinal) =>
        {
            var row = rowsByOrdinal[ordinal];
            var status = ClassifyPrecheck(message, row);
            return new InboxPrecheckResult(message.MessageId, status);
        }).ToArray();
    }

    /// <summary>
    /// 领取单条消息的 Inbox 处理权：若为新消息则插入并锁定为 Processing，
    /// 若为已处理则返回 AlreadyProcessed，若为 Failed 状态则重置并重新锁定。
    /// </summary>
    /// <param name="consumerName">消费端唯一名称。</param>
    /// <param name="envelope">集成事件信封（含 EventId / MessageType / SchemaVersion / TenantId / Payload）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>
    /// Claim 结果：<see cref="InboxClaimStatus.Claimed"/> /
    /// <see cref="InboxClaimStatus.AlreadyProcessed"/> /
    /// <see cref="InboxClaimStatus.PayloadMismatch"/>。
    /// </returns>
    /// <remarks>
    /// Provider 特定 SQL 在服务端完成"锁读 + 首次插入或 Failed 重置 + 返回行"，
    /// 仅产生一次数据库网络往返。
    /// </remarks>
    public async Task<InboxClaimResult> ClaimAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ValidateConsumerName(consumerName);
        ArgumentNullException.ThrowIfNull(envelope);

        var payloadHash = SHA256.HashData(envelope.Payload.Span);
        var claimStatement = _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => InboxSql.ClaimSqlServer,
            DatabaseProvider.MySql => InboxSql.ClaimMySql,
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported."),
        };

        // Provider SQL 在服务端完成锁定读取、首次插入或 failed 重置，并只产生一个网络往返。
        var claim = await queryExecutor
            .QuerySingleOrDefaultAsync<InboxClaimRow>(
                claimStatement,
                new Dictionary<string, object?>
                {
                    ["ConsumerName"] = consumerName,
                    ["MessageId"] = envelope.EventId,
                    ["MessageType"] = envelope.MessageType,
                    ["SchemaVersion"] = envelope.SchemaVersion,
                    ["TenantId"] = envelope.TenantId,
                    ["PayloadHash"] = payloadHash,
                    ["StatusProcessing"] = InboxSql.StatusProcessing,
                    ["StatusFailed"] = InboxSql.StatusFailed,
                    ["ReceivedAtUtc"] = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (claim is null)
        {
            throw new InvalidOperationException("Inbox claim did not return its locked row.");
        }

        if (!payloadHash.AsSpan().SequenceEqual(claim.PayloadHash))
        {
            return new InboxClaimResult(InboxClaimStatus.PayloadMismatch);
        }

        if (string.Equals(claim.Status, InboxSql.StatusProcessed, StringComparison.Ordinal))
        {
            return new InboxClaimResult(InboxClaimStatus.AlreadyProcessed);
        }

        if (string.Equals(claim.Status, InboxSql.StatusProcessing, StringComparison.Ordinal))
        {
            return new InboxClaimResult(InboxClaimStatus.Claimed);
        }

        throw new InvalidOperationException(
            $"Inbox claim returned unsupported status '{claim.Status}'.");
    }

    /// <summary>
    /// 将 Inbox 行从 Processing 原子迁移至 Processed，标记消费完成。
    /// 以 (ConsumerName, MessageId, ExpectedStatus=Processing) 复合条件更新，
    /// 防止重复标记或跳过 Claim 直接调用。
    /// </summary>
    /// <param name="consumerName">消费端唯一名称。</param>
    /// <param name="messageId">集成事件唯一标识（对应 Envelope.EventId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="InvalidOperationException">当影响行数不为 1 时抛出（行锁已丢失或已被他人标记）。</exception>
    public async Task MarkProcessedAsync(
        string consumerName,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("MessageId must be assigned.", nameof(messageId));
        }

        var affected = await commandExecutor
            .ExecuteAsync(
                InboxSql.MarkProcessed,
                new Dictionary<string, object?>
                {
                    ["ConsumerName"] = consumerName,
                    ["MessageId"] = messageId,
                    ["Status"] = InboxSql.StatusProcessed,
                    ["ExpectedStatus"] = InboxSql.StatusProcessing,
                    ["ProcessedAtUtc"] = clock.UtcNow,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Inbox mark processed affected {affected} rows instead of one.");
        }
    }

    private static InboxPrecheckStatus ClassifyPrecheck(
        InboxMessageFingerprint message,
        InboxBatchPrecheckRow row)
    {
        if (row.Status is null)
        {
            return InboxPrecheckStatus.Unknown;
        }

        if (row.PayloadHash is null)
        {
            throw new InvalidOperationException(
                "Inbox batch precheck returned an existing row without PayloadHash.");
        }

        if (!message.PayloadHash.Span.SequenceEqual(row.PayloadHash))
        {
            return InboxPrecheckStatus.PayloadMismatch;
        }

        return string.Equals(row.Status, InboxSql.StatusProcessed, StringComparison.Ordinal)
            ? InboxPrecheckStatus.AlreadyProcessed
            : InboxPrecheckStatus.Unknown;
    }

    private static string BuildMessagesJson(IReadOnlyList<InboxMessageFingerprint> messages)
    {
        using var stream = new MemoryStream(messages.Count * 80);
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            for (var ordinal = 0; ordinal < messages.Count; ordinal++)
            {
                writer.WriteStartObject();
                writer.WriteNumber("ordinal", ordinal);
                writer.WriteString("messageId", messages[ordinal].MessageId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    private static void ValidateConsumerName(string consumerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        if (consumerName.Length > MessagingNames.ConsumerNameMaxLength
            || !MessagingNames.ConsumerNamePattern.IsMatch(consumerName))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.ConsumerNameInvalid,
                nameof(consumerName));
        }
    }
}
