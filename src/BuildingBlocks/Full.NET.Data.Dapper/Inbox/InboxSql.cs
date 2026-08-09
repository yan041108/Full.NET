using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper.Inbox;

internal static class InboxSql
{
    internal const string StatusProcessing = "processing";
    internal const string StatusProcessed = "processed";
    internal const string StatusFailed = "failed";

    public static readonly SqlStatement ClaimSqlServer = new(
        "messaging.inbox.claim.sql_server",
        """
        DECLARE @ExistingStatus varchar(16);
        DECLARE @ExistingPayloadHash varbinary(32);

        SELECT
            @ExistingStatus = Status,
            @ExistingPayloadHash = PayloadHash
        FROM fn_messaging_inbox_message WITH (UPDLOCK, HOLDLOCK)
        WHERE ConsumerName = @ConsumerName AND MessageId = @MessageId;

        IF @ExistingStatus IS NULL
        BEGIN
            INSERT INTO fn_messaging_inbox_message
                (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
                 PayloadHash, Status, Attempts, ReceivedAtUtc)
            VALUES
                (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, @TenantId,
                 @PayloadHash, @StatusProcessing, 1, @ReceivedAtUtc);
            SET @ExistingStatus = @StatusProcessing;
            SET @ExistingPayloadHash = @PayloadHash;
        END
        ELSE IF @ExistingStatus = @StatusFailed AND @ExistingPayloadHash = @PayloadHash
        BEGIN
            UPDATE fn_messaging_inbox_message
            SET Status = @StatusProcessing,
                Attempts = Attempts + 1
            WHERE ConsumerName = @ConsumerName AND MessageId = @MessageId;
            SET @ExistingStatus = @StatusProcessing;
        END;

        SELECT @ExistingStatus AS Status, @ExistingPayloadHash AS PayloadHash;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ClaimMySql = new(
        "messaging.inbox.claim.my_sql",
        """
        INSERT INTO fn_messaging_inbox_message
            (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
             PayloadHash, Status, Attempts, ReceivedAtUtc)
        VALUES
            (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, @TenantId,
             @PayloadHash, @StatusProcessing, 1, @ReceivedAtUtc)
        ON DUPLICATE KEY UPDATE
            Attempts = IF(
                Status = @StatusFailed AND PayloadHash = @PayloadHash,
                Attempts + 1,
                Attempts),
            Status = IF(
                Status = @StatusFailed AND PayloadHash = @PayloadHash,
                @StatusProcessing,
                Status);

        SELECT Status, PayloadHash
        FROM fn_messaging_inbox_message
        WHERE ConsumerName = @ConsumerName
          AND MessageId = @MessageId
        FOR UPDATE;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement MarkProcessed = new(
        "messaging.inbox.mark_processed",
        """
        UPDATE fn_messaging_inbox_message
        SET Status = @Status,
            ProcessedAtUtc = @ProcessedAtUtc,
            LastErrorCode = NULL,
            LastError = NULL
        WHERE ConsumerName = @ConsumerName
          AND MessageId = @MessageId
          AND Status = @ExpectedStatus;
        """,
        SqlDataScope.Global);
}

internal sealed record InboxClaimRow(string Status, byte[] PayloadHash);
