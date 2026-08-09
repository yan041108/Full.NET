using System.Security.Cryptography;
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

    public async Task<InboxClaimResult> ClaimAsync(
        string consumerName,
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentNullException.ThrowIfNull(envelope);
        if (consumerName.Length > MessagingNames.ConsumerNameMaxLength
            || !MessagingNames.ConsumerNamePattern.IsMatch(consumerName))
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.ConsumerNameInvalid,
                nameof(consumerName));
        }

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
}
