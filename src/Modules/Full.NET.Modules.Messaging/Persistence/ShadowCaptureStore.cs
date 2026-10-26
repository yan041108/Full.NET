using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Messaging.Persistence;

/// <summary>一条流保留一个当前控制行；历史载荷只保留到下一次显式开始，旧证明随代次失效。</summary>
internal sealed record ShadowCaptureRow(Guid Id, Guid Generation, Guid StartRequestId, string MessageType, int SchemaVersion,
    string State, int MaxEvents, long MaxPayloadBytes, int CapturedCount, long CapturedBytes, int MissingMetadataCount,
    Guid? LastReservedEventId, DateTimeOffset StartedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset? ClosedAtUtc,
    string? EndReason, Guid OperatorUserId, string OperatorDisplayName, string Reason, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid? StartExpectedGeneration);

/// <summary>控制操作须在全局会话锁和按流所有权排他锁内调用，禁止把 Broker 调用放入此事务。</summary>
internal sealed class ShadowCaptureStore(IQueryExecutor query, ICommandExecutor command)
{
    internal Task<ShadowCaptureRow?> FindAsync(string eventType, int version, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<ShadowCaptureRow>(ShadowCaptureSql.Find,
            new Dictionary<string, object?> { ["EventType"] = eventType, ["SchemaVersion"] = version }, token);
    internal Task<ShadowCaptureRow?> FindGenerationAsync(Guid generation, CancellationToken token) =>
        query.QuerySingleOrDefaultAsync<ShadowCaptureRow>(ShadowCaptureSql.FindGeneration,
            new Dictionary<string, object?> { ["Generation"] = generation }, token);
    internal Task<int> InsertAsync(ShadowCaptureRow row, CancellationToken token) => command.ExecuteAsync(ShadowCaptureSql.Insert, Parameters(row), token);
    internal async Task<int> ReplaceAsync(ShadowCaptureRow previous, ShadowCaptureRow next, CancellationToken token)
    {
        // 旧样本是受控诊断副本；新代次开始会使旧证明失效，不触碰 Legacy 或正式追加源。
        await command.ExecuteAsync(ShadowCaptureSql.PrunePrevious, new Dictionary<string, object?> { ["Generation"] = previous.Generation }, token).ConfigureAwait(false);
        var parameters = Parameters(next); parameters["ExpectedGeneration"] = previous.Generation;
        return await command.ExecuteAsync(ShadowCaptureSql.Replace, parameters, token).ConfigureAwait(false);
    }
    internal Task<int> CloseAsync(ShadowCaptureRow row, DateTimeOffset now, CancellationToken token) =>
        command.ExecuteAsync(ShadowCaptureSql.Close, new Dictionary<string, object?> { ["Generation"] = row.Generation, ["NowUtc"] = now }, token);
    private static Dictionary<string, object?> Parameters(ShadowCaptureRow row) => new()
    {
        ["StartExpectedGeneration"] = row.StartExpectedGeneration, ["Id"] = row.Id, ["Generation"] = row.Generation, ["StartRequestId"] = row.StartRequestId,
        ["EventType"] = row.MessageType, ["SchemaVersion"] = row.SchemaVersion, ["MaxEvents"] = row.MaxEvents,
        ["MaxPayloadBytes"] = row.MaxPayloadBytes, ["StartedAtUtc"] = row.StartedAtUtc, ["ExpiresAtUtc"] = row.ExpiresAtUtc,
        ["OperatorUserId"] = row.OperatorUserId, ["OperatorDisplayName"] = row.OperatorDisplayName, ["Reason"] = row.Reason,
        ["CreatedAtUtc"] = row.CreatedAtUtc, ["UpdatedAtUtc"] = row.UpdatedAtUtc,
    };
}

internal static class ShadowCaptureSql
{
    internal const string Columns = "Id, Generation, StartRequestId, MessageType, SchemaVersion, State, MaxEvents, MaxPayloadBytes, CapturedCount, CapturedBytes, MissingMetadataCount, LastReservedEventId, StartedAtUtc, ExpiresAtUtc, ClosedAtUtc, EndReason, OperatorUserId, OperatorDisplayName, Reason, CreatedAtUtc, UpdatedAtUtc, StartExpectedGeneration";
    internal static readonly SqlStatement Find = new("messaging.shadow_capture.find", $"SELECT {Columns} FROM fn_messaging_shadow_capture WHERE MessageType = @EventType AND SchemaVersion = @SchemaVersion", SqlDataScope.Global);
    internal static readonly SqlStatement FindGeneration = new("messaging.shadow_capture.find_generation", $"SELECT {Columns} FROM fn_messaging_shadow_capture WHERE Generation = @Generation", SqlDataScope.Global);
    internal static readonly SqlStatement Insert = new("messaging.shadow_capture.insert", """
        INSERT INTO fn_messaging_shadow_capture (Id, Generation, StartRequestId, MessageType, SchemaVersion, State,
            MaxEvents, MaxPayloadBytes, CapturedCount, CapturedBytes, MissingMetadataCount, LastReservedEventId,
            StartedAtUtc, ExpiresAtUtc, ClosedAtUtc, EndReason, OperatorUserId, OperatorDisplayName, Reason, CreatedAtUtc, UpdatedAtUtc, StartExpectedGeneration)
        VALUES (@Id, @Generation, @StartRequestId, @EventType, @SchemaVersion, 'active', @MaxEvents, @MaxPayloadBytes, 0, 0, 0,
            NULL, @StartedAtUtc, @ExpiresAtUtc, NULL, NULL, @OperatorUserId, @OperatorDisplayName, @Reason, @CreatedAtUtc, @UpdatedAtUtc, @StartExpectedGeneration)
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Replace = new("messaging.shadow_capture.replace", """
        UPDATE fn_messaging_shadow_capture SET Generation = @Generation, StartRequestId = @StartRequestId, State = 'active',
            MaxEvents = @MaxEvents, MaxPayloadBytes = @MaxPayloadBytes, CapturedCount = 0, CapturedBytes = 0,
            MissingMetadataCount = 0, LastReservedEventId = NULL, StartedAtUtc = @StartedAtUtc, ExpiresAtUtc = @ExpiresAtUtc,
            ClosedAtUtc = NULL, EndReason = NULL, OperatorUserId = @OperatorUserId, OperatorDisplayName = @OperatorDisplayName,
            Reason = @Reason, UpdatedAtUtc = @UpdatedAtUtc, StartExpectedGeneration = @StartExpectedGeneration
        WHERE Id = @Id AND Generation = @ExpectedGeneration
        """, SqlDataScope.Global);
    internal static readonly SqlStatement PrunePrevious = new("messaging.shadow_capture.prune_previous", "DELETE FROM fn_messaging_shadow_event WHERE CaptureGeneration = @Generation", SqlDataScope.Global);
    internal static readonly SqlStatement Close = new("messaging.shadow_capture.close", """
        UPDATE fn_messaging_shadow_capture SET State = 'closed', ClosedAtUtc = @NowUtc,
            EndReason = CASE WHEN ExpiresAtUtc <= @NowUtc THEN 'time_budget' ELSE 'operator_closed' END, UpdatedAtUtc = @NowUtc
        WHERE Generation = @Generation AND State = 'active'
        """, SqlDataScope.Global);
}
