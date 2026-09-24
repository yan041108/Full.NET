using System.Data.Common;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>写入确定性的 Workflow 提醒 Outbox，并等待 Worker 投影至 Notifications Inbox。</summary>
internal static class NativeWorkerWorkflowNotificationProbe
{
    private const string InsertOutboxSql =
        """
        INSERT INTO fn_outbox_message
            (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId,
             Payload, OccurredAtUtc, Attempts)
        VALUES
            (@Id, @MessageType, 1, @ContentType, NULL, @TraceId,
             @Payload, @OccurredAtUtc, 0)
        """;

    private const string SelectProjectionSql =
        """
        SELECT CASE WHEN o.ProcessedAtUtc IS NULL THEN 0 ELSE 1 END AS IsProcessed,
               o.DeadLetterReasonCode,
               i.ProducerKey,
               i.SceneKey,
               i.IdempotencyKey,
               message.Id AS InboxMessageId,
               message.RecipientUserId,
               message.Status AS InboxStatus
        FROM fn_outbox_message o
        LEFT JOIN fn_notifications_intent i
          ON i.TenantScopeKey = 'host'
         AND i.ProducerKey = 'workflow'
         AND i.IdempotencyKey = @IdempotencyKey
        LEFT JOIN fn_notifications_inbox_message message
          ON message.TenantScopeKey = 'host'
         AND message.IntentId = i.Id
        WHERE o.Id = @MessageId
        """;

    /// <summary>写入发往活动 Host 用户的待办提醒事件。</summary>
    /// <param name="provider">目标数据库提供程序。</param>
    /// <param name="connectionString">独立 Integration 数据库连接。</param>
    /// <param name="cancellationToken">取消探针准备的令牌。</param>
    /// <returns>本次 Outbox 与收件人标识。</returns>
    public static async Task<NativeWorkerWorkflowNotificationScenario> EnqueueAsync(
        DatabaseProvider provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(provider, connectionString);
        var recipientUserId = await FindActiveHostUserAsync(connection, provider, cancellationToken)
            .ConfigureAwait(false);
        var messageId = Guid.CreateVersion7();
        var idempotencyKey = $"workflow-{messageId:N}";
        var now = DateTimeOffset.UtcNow;
        var serializer = new MemoryPackIntegrationEventSerializer();
        var payload = serializer.Serialize(new WorkflowTodoAssignedIntegrationEvent(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            recipientUserId,
            "native.worker.notification",
            Guid.CreateVersion7().ToString("N"),
            now));
        var occurredAtUtc = provider == DatabaseProvider.MySql
            ? (object)now.UtcDateTime
            : now;

        await connection.ExecuteAsync(new CommandDefinition(
                InsertOutboxSql,
                new
                {
                    Id = messageId,
                    MessageType = WorkflowNotificationIntegrationEventTypes.TodoAssigned,
                    ContentType = serializer.ContentType,
                    TraceId = messageId.ToString("N"),
                    Payload = payload,
                    OccurredAtUtc = occurredAtUtc,
                },
                cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        return new NativeWorkerWorkflowNotificationScenario(
            messageId,
            recipientUserId,
            idempotencyKey);
    }

    /// <summary>轮询同一 Outbox 消息的终态和 Intent 到 Inbox 的完整投影。</summary>
    /// <param name="provider">目标数据库提供程序。</param>
    /// <param name="connectionString">独立 Integration 数据库连接。</param>
    /// <param name="scenario">待验证消息标识和收件人。</param>
    /// <param name="timeout">允许 Worker 完成处理的时限。</param>
    /// <param name="logFilePath">Worker 进程日志路径。</param>
    /// <param name="cancellationToken">取消轮询的令牌。</param>
    /// <returns>Outbox、Intent 与 Inbox 的联合状态。</returns>
    public static async Task<NativeWorkerWorkflowNotificationProjection> WaitForProjectionAsync(
        DatabaseProvider provider,
        string connectionString,
        NativeWorkerWorkflowNotificationScenario scenario,
        TimeSpan timeout,
        string logFilePath,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var connection = CreateConnection(provider, connectionString);
            var projection = await connection.QuerySingleAsync<NativeWorkerWorkflowNotificationProjection>(
                    new CommandDefinition(
                        SelectProjectionSql,
                        new
                        {
                            scenario.MessageId,
                            scenario.IdempotencyKey,
                        },
                        cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            if (projection.IsProcessed == 1
                || projection.DeadLetterReasonCode is not null)
            {
                return projection;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Native Worker 未在 {timeout} 内将 Workflow 提醒投影至 Intent 与 Inbox。日志：{logFilePath}");
    }

    private static async Task<Guid> FindActiveHostUserAsync(
        DbConnection connection,
        DatabaseProvider provider,
        CancellationToken cancellationToken)
    {
        var sql = provider switch
        {
            DatabaseProvider.SqlServer =>
                "SELECT TOP (1) Id FROM fn_identity_user WHERE ScopeKey = 'host' AND TenantId IS NULL AND IsActive = 1 ORDER BY Id",
            DatabaseProvider.MySql =>
                "SELECT Id FROM fn_identity_user WHERE ScopeKey = 'host' AND TenantId IS NULL AND IsActive = 1 ORDER BY Id LIMIT 1",
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'."),
        };
        var userId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                new CommandDefinition(sql, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
        return userId ?? throw new InvalidOperationException(
            "Notifications Workflow 投影探针需要迁移后至少一个活动的 Host 用户。");
    }

    private static DbConnection CreateConnection(
        DatabaseProvider provider,
        string connectionString) => provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'."),
        };
}

internal sealed record NativeWorkerWorkflowNotificationScenario(
    Guid MessageId,
    Guid RecipientUserId,
    string IdempotencyKey);

internal sealed class NativeWorkerWorkflowNotificationProjection
{
    public long IsProcessed { get; init; }

    public string? DeadLetterReasonCode { get; init; }

    public string? ProducerKey { get; init; }

    public string? SceneKey { get; init; }

    public string? IdempotencyKey { get; init; }

    public Guid? InboxMessageId { get; init; }

    public Guid? RecipientUserId { get; init; }

    public string? InboxStatus { get; init; }
}
