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
        var selectStatement = _databaseOptions.Provider switch
        {
            DatabaseProvider.SqlServer => InboxSql.SelectExistingSqlServer,
            DatabaseProvider.MySql => InboxSql.SelectExistingMySql,
            _ => throw new NotSupportedException(
                $"Database provider '{_databaseOptions.Provider}' is not supported."),
        };

        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<InboxExistingRow>(
                selectStatement,
                new
                {
                    ConsumerName = consumerName,
                    MessageId = envelope.EventId,
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            if (!payloadHash.AsSpan().SequenceEqual(existing.PayloadHash))
            {
                return new InboxClaimResult(InboxClaimStatus.PayloadMismatch);
            }

            if (string.Equals(
                    existing.Status,
                    InboxSql.StatusProcessed,
                    StringComparison.Ordinal))
            {
                return new InboxClaimResult(InboxClaimStatus.AlreadyProcessed);
            }

            if (string.Equals(
                    existing.Status,
                    InboxSql.StatusFailed,
                    StringComparison.Ordinal))
            {
                var resetRows = await commandExecutor
                    .ExecuteAsync(
                        InboxSql.ResetFailedToProcessing,
                        new
                        {
                            ConsumerName = consumerName,
                            MessageId = envelope.EventId,
                            StatusProcessing = InboxSql.StatusProcessing,
                            StatusFailed = InboxSql.StatusFailed,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (resetRows != 1)
                {
                    throw new InvalidOperationException(
                        $"Inbox failed replay reset affected {resetRows} rows instead of one.");
                }

                return new InboxClaimResult(InboxClaimStatus.Claimed);
            }
        }
        else
        {
            var insertRow = new InboxInsertRow(
                consumerName,
                envelope.EventId,
                envelope.MessageType,
                envelope.SchemaVersion,
                envelope.TenantId,
                payloadHash,
                InboxSql.StatusProcessing,
                1,
                clock.UtcNow);

            var inserted = await commandExecutor
                .ExecuteAsync(InboxSql.InsertProcessing, insertRow, cancellationToken)
                .ConfigureAwait(false);
            if (inserted != 1)
            {
                throw new InvalidOperationException(
                    $"Inbox insert affected {inserted} rows instead of one.");
            }

            return new InboxClaimResult(InboxClaimStatus.Claimed);
        }

        // processing/failed 且 PayloadHash 一致：同事务内重试路径，复用现有行继续处理。
        return new InboxClaimResult(InboxClaimStatus.Claimed);
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

    private sealed record InboxExistingRow(string Status, byte[] PayloadHash);

    private sealed record InboxInsertRow(
        string ConsumerName,
        Guid MessageId,
        string MessageType,
        int SchemaVersion,
        Guid? TenantId,
        byte[] PayloadHash,
        string Status,
        int Attempts,
        DateTimeOffset ReceivedAtUtc);
}