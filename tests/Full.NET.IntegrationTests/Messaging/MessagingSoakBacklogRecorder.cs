using System.Data.Common;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Messaging;

/// <summary>独享单样本、零预热长跑的实际数据库对账；明确排除正式负载前唯一的 CDC 就绪探针。</summary>
internal sealed class MessagingSoakBacklogRecorder(DatabaseOptions database, string eventType, string consumer, string path) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<Snapshot> CaptureAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await using DbConnection connection = database.Provider == DatabaseProvider.SqlServer
                ? new SqlConnection(database.ConnectionString)
                : new MySqlConnection(MySqlConnectionStringPolicy.Create(database.ConnectionString, database.MySqlGuidStorageMode, false));
            await connection.OpenAsync(deadline.Token);
            var count = database.Provider == DatabaseProvider.SqlServer ? "COUNT_BIG" : "COUNT";
            var oldest = database.Provider == DatabaseProvider.SqlServer
                ? "CONVERT(float, DATEDIFF_BIG(MILLISECOND, MIN(CASE WHEN inbox.Status IS NULL OR inbox.Status<>'processed' THEN sample.OccurredAtUtc END), SYSUTCDATETIME()))/1000.0"
                : "TIMESTAMPDIFF(MICROSECOND, MIN(CASE WHEN inbox.Status IS NULL OR inbox.Status<>'processed' THEN sample.OccurredAtUtc END), UTC_TIMESTAMP(6))/1000000.0";
            var row = await connection.QuerySingleAsync<(long Produced, long Processed, long Pending, double OldestSeconds)>(new CommandDefinition($"""
                WITH ordered AS (
                    SELECT Id, OccurredAtUtc, ROW_NUMBER() OVER (ORDER BY OccurredAtUtc, Id) AS SampleRow
                    FROM fn_messaging_outbox_event WHERE MessageType=@EventType
                )
                SELECT {count}(*), {count}(CASE WHEN inbox.Status='processed' THEN 1 END),
                    {count}(CASE WHEN inbox.Status IS NULL OR inbox.Status<>'processed' THEN 1 END), COALESCE({oldest}, 0)
                FROM ordered sample
                LEFT JOIN fn_messaging_inbox_message inbox ON inbox.MessageId=sample.Id AND inbox.ConsumerName=@Consumer
                WHERE sample.SampleRow>1
                """, new { EventType = eventType, Consumer = consumer }, commandTimeout: 10, cancellationToken: deadline.Token));
            var snapshot = new Snapshot(DateTimeOffset.UtcNow, row.Produced, row.Processed, row.Pending, Math.Max(0, row.OldestSeconds));
            await File.AppendAllTextAsync(path, JsonSerializer.Serialize(snapshot) + Environment.NewLine, deadline.Token);
            return snapshot;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
    internal sealed record Snapshot(DateTimeOffset ObservedAtUtc, long OutboxSamples, long ProcessedInboxSamples, long PendingSamples, double OldestPendingSeconds);
}
