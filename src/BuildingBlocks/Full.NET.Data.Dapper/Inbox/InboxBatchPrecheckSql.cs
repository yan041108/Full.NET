using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper.Inbox;

internal static class InboxBatchPrecheckSql
{
    public static readonly SqlStatement SqlServer = new(
        "messaging.inbox.precheck_batch.sql_server",
        """
        SELECT requested.Ordinal,
               inbox.Status,
               inbox.PayloadHash
        FROM OPENJSON(@MessagesJson)
        WITH
        (
            Ordinal int '$.ordinal',
            MessageId uniqueidentifier '$.messageId'
        ) AS requested
        LEFT JOIN fn_messaging_inbox_message AS inbox
          ON inbox.ConsumerName = @ConsumerName
         AND inbox.MessageId = requested.MessageId
        ORDER BY requested.Ordinal;
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement MySql = new(
        "messaging.inbox.precheck_batch.my_sql",
        """
        SELECT requested.Ordinal,
               inbox.Status,
               inbox.PayloadHash
        FROM JSON_TABLE(
            @MessagesJson,
            '$[*]' COLUMNS
            (
                Ordinal int PATH '$.ordinal',
                MessageIdText char(36) PATH '$.messageId'
            )) AS requested
        LEFT JOIN fn_messaging_inbox_message AS inbox
          ON inbox.ConsumerName = @ConsumerName
         AND inbox.MessageId = UUID_TO_BIN(requested.MessageIdText, 0)
        ORDER BY requested.Ordinal;
        """,
        SqlDataScope.Global);
}

internal sealed record InboxBatchPrecheckRow(
    int Ordinal,
    string? Status,
    byte[]? PayloadHash);
