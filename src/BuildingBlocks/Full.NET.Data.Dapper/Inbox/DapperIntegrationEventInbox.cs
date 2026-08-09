using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Inbox;

internal sealed class DapperIntegrationEventInbox(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions) : IIntegrationEventInbox
{
    private readonly DatabaseOptions _databaseOptions = databaseOptions.Value;

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
                new
                {
                    ConsumerName = consumerName,
                    MessagesJson = BuildMessagesJson(messages),
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
                new
                {
                    ConsumerName = consumerName,
                    MessageId = envelope.EventId,
                    MessageType = envelope.MessageType,
                    SchemaVersion = envelope.SchemaVersion,
                    TenantId = envelope.TenantId,
                    PayloadHash = payloadHash,
                    StatusProcessing = InboxSql.StatusProcessing,
                    StatusFailed = InboxSql.StatusFailed,
                    ReceivedAtUtc = clock.UtcNow,
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
                new
                {
                    ConsumerName = consumerName,
                    MessageId = messageId,
                    Status = InboxSql.StatusProcessed,
                    ExpectedStatus = InboxSql.StatusProcessing,
                    ProcessedAtUtc = clock.UtcNow,
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
