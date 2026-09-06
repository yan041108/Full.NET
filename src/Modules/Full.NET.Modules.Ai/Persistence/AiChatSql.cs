using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Ai.Persistence;

/// <summary>AI 聊天会话与消息 SQL。</summary>
internal static class AiChatSql
{
    private const string SessionColumns = """
        session.Id,
                  session.TenantId,
                  session.OwnerUserId,
                  session.ModelConfigId,
                  session.ModelName,
                  session.Title,
                  session.MessageCount,
                  session.LastMessageAtUtc,
                  session.IsGenerating,
                  session.CreatedAtUtc,
                  session.UpdatedAtUtc,
                  session.Version
        """;

    private const string MessageColumns = """
        message.Id,
                  message.SessionId,
                  message.RoleKey,
                  message.Content,
                  message.StatusKey,
                  message.PromptTokens,
                  message.CompletionTokens,
                  message.CreatedAtUtc
        """;

    private const string OwnerScopeClause = """
        session.OwnerUserId = @OwnerUserId
          AND (
            (@ScopeTenantId IS NULL AND session.TenantId IS NULL)
            OR session.TenantId = @ScopeTenantId
          )
        """;

    public static readonly SqlStatement InsertSession = new(
        "ai.insert_chat_session",
        """
        INSERT INTO fn_ai_chat_session
            (Id, TenantId, OwnerUserId, ModelConfigId, ModelName, Title, MessageCount,
             LastMessageAtUtc, IsGenerating, CreatedAtUtc, UpdatedAtUtc, Version)
        VALUES
            (@Id, @TenantId, @OwnerUserId, @ModelConfigId, @ModelName, @Title, 0,
             NULL, 0, @CreatedAtUtc, NULL, 1)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement FindSessionById = new(
        "ai.find_chat_session_by_id",
        $"""
        SELECT {SessionColumns}
        FROM fn_ai_chat_session AS session
        WHERE session.Id = @SessionId
          AND {OwnerScopeClause}
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateSessionTitle = new(
        "ai.update_chat_session_title",
        $"""
        UPDATE fn_ai_chat_session
        SET Title = @Title,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @SessionId
          AND OwnerUserId = @OwnerUserId
          AND Version = @Version
          AND (
            (@ScopeTenantId IS NULL AND TenantId IS NULL)
            OR TenantId = @ScopeTenantId
          )
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement DeleteSession = new(
        "ai.delete_chat_session",
        $"""
        DELETE FROM fn_ai_chat_session
        WHERE Id = @SessionId
          AND OwnerUserId = @OwnerUserId
          AND (
            (@ScopeTenantId IS NULL AND TenantId IS NULL)
            OR TenantId = @ScopeTenantId
          )
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement DeleteMessagesBySession = new(
        "ai.delete_chat_messages_by_session",
        """
        DELETE FROM fn_ai_chat_message
        WHERE SessionId = @SessionId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement SetSessionGenerating = new(
        "ai.set_chat_session_generating",
        """
        UPDATE fn_ai_chat_session
        SET IsGenerating = @IsGenerating,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @SessionId
          AND OwnerUserId = @OwnerUserId
          AND (
            (@ScopeTenantId IS NULL AND TenantId IS NULL)
            OR TenantId = @ScopeTenantId
          )
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement TouchSessionAfterMessage = new(
        "ai.touch_chat_session_after_message",
        """
        UPDATE fn_ai_chat_session
        SET MessageCount = MessageCount + @MessageDelta,
            LastMessageAtUtc = @LastMessageAtUtc,
            Title = CASE WHEN @ReplaceTitle = 1 THEN @Title ELSE Title END,
            UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @SessionId
        """,
        SqlDataScope.Global);

    public static readonly string ListSessionsSqlServer = $"""
        SELECT {SessionColumns}
        FROM fn_ai_chat_session AS session
        WHERE {OwnerScopeClause}
        ORDER BY session.UpdatedAtUtc DESC, session.CreatedAtUtc DESC, session.Id
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """;

    public static readonly string CountSessionsSqlServer = $"""
        SELECT COUNT(1)
        FROM fn_ai_chat_session AS session
        WHERE {OwnerScopeClause}
        """;

    public static readonly string ListSessionsMySql = $"""
        SELECT {SessionColumns}
        FROM fn_ai_chat_session AS session
        WHERE {OwnerScopeClause}
        ORDER BY session.UpdatedAtUtc DESC, session.CreatedAtUtc DESC, session.Id
        LIMIT @PageSize OFFSET @Offset
        """;

    public static readonly string CountSessionsMySql = $"""
        SELECT COUNT(1)
        FROM fn_ai_chat_session AS session
        WHERE {OwnerScopeClause}
        """;

    public static readonly SqlStatement InsertMessage = new(
        "ai.insert_chat_message",
        """
        INSERT INTO fn_ai_chat_message
            (Id, SessionId, RoleKey, Content, StatusKey, PromptTokens, CompletionTokens, CreatedAtUtc)
        VALUES
            (@Id, @SessionId, @RoleKey, @Content, @StatusKey, @PromptTokens, @CompletionTokens, @CreatedAtUtc)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement UpdateMessage = new(
        "ai.update_chat_message",
        """
        UPDATE fn_ai_chat_message
        SET Content = @Content,
            StatusKey = @StatusKey,
            PromptTokens = @PromptTokens,
            CompletionTokens = @CompletionTokens
        WHERE Id = @MessageId
          AND SessionId = @SessionId
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListMessagesBySession = new(
        "ai.list_chat_messages_by_session",
        $"""
        SELECT {MessageColumns}
        FROM fn_ai_chat_message AS message
        WHERE message.SessionId = @SessionId
        ORDER BY message.CreatedAtUtc ASC, message.Id
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ListRecentMessagesForModel = new(
        "ai.list_recent_chat_messages_for_model",
        $"""
        SELECT {MessageColumns}
        FROM fn_ai_chat_message AS message
        WHERE message.SessionId = @SessionId
          AND message.StatusKey IN ('completed', 'cancelled')
        ORDER BY message.CreatedAtUtc DESC, message.Id DESC
        OFFSET 0 ROWS FETCH NEXT @Take ROWS ONLY
        """,
        SqlDataScope.Global);

    public static readonly string ListRecentMessagesForModelMySql = $"""
        SELECT {MessageColumns}
        FROM fn_ai_chat_message AS message
        WHERE message.SessionId = @SessionId
          AND message.StatusKey IN ('completed', 'cancelled')
        ORDER BY message.CreatedAtUtc DESC, message.Id DESC
        LIMIT @Take
        """;
}
