using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper.Inbox;

internal static class InboxSql
{
    internal const string StatusProcessing = "processing";
    internal const string StatusProcessed = "processed";

    public static readonly SqlStatement SelectExistingSqlServer = new(
        "messaging.inbox.select_existing.sql_server",
        """
        SELECT Status, PayloadHash
        FROM fn_messaging_inbox_message WITH (UPDLOCK, HOLDLOCK)
        WHERE ConsumerName = @ConsumerName
          AND MessageId = @MessageId;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SelectExistingMySql = new(
        "messaging.inbox.select_existing.my_sql",
        """
        SELECT Status, PayloadHash
        FROM fn_messaging_inbox_message
        WHERE ConsumerName = @ConsumerName
          AND MessageId = @MessageId
        FOR UPDATE;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement InsertProcessing = new(
        "messaging.inbox.insert_processing",
        """
        INSERT INTO fn_messaging_inbox_message
            (ConsumerName, MessageId, MessageType, SchemaVersion, TenantId,
             PayloadHash, Status, Attempts, ReceivedAtUtc)
        VALUES
            (@ConsumerName, @MessageId, @MessageType, @SchemaVersion, @TenantId,
             @PayloadHash, @Status, @Attempts, @ReceivedAtUtc);
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