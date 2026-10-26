using System.Diagnostics;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>同一业务事务内采集真实 Legacy 信封；仅写影子源，绝不写正式追加表。</summary>
internal sealed class DapperShadowCaptureWriter(ICommandExecutor command, IClock clock,
    IOptions<ShadowCaptureOptions> options)
{
    private readonly ShadowCaptureOptions _options = options.Value;

    internal bool Matches(string eventType, int schemaVersion) => _options.Enabled
        && _options.EventType == eventType && _options.SchemaVersion == schemaVersion;

    internal async Task CaptureAsync(OutboxMessage message, IntegrationEventMetadata metadata, CancellationToken token)
    {
        if (!Matches(message.MessageType, message.SchemaVersion)) return;
        var trace = Activity.Current?.Id;
        if (trace is not null && !MessagingNames.TraceParentPattern.IsMatch(trace)) trace = null;
        var parameters = DapperSqlParameters.Create(("Id", message.Id), ("MessageType", message.MessageType),
            ("SchemaVersion", message.SchemaVersion), ("ContentType", message.ContentType), ("TenantId", message.TenantId),
            ("PartitionKey", metadata.PartitionKey), ("CorrelationId", metadata.CorrelationId), ("CausationId", metadata.CausationId),
            ("TraceParent", trace), ("Producer", metadata.Producer), ("Payload", message.Payload),
            ("OccurredAtUtc", message.OccurredAtUtc), ("PayloadBytes", message.Payload.LongLength), ("NowUtc", clock.UtcNow));
        var affected = await command.ExecuteAsync(ShadowCaptureWriteSql.ReserveAndInsert, parameters, token).ConfigureAwait(false);
        // 2 表示预留与插入，1 表示仅关闭超界窗口，0 表示本代已结束或未开始。
        // 同一行锁内预留成功就必然匹配后续 INSERT；任何 SQL 错误都会回滚整个业务事务。
        if (affected is < 0 or > 2) throw new InvalidOperationException("影子采集命令返回了不符合原子协议的影响行数。");
    }

    internal Task RecordMissingMetadataAsync(string eventType, int schemaVersion, CancellationToken token) =>
        !Matches(eventType, schemaVersion) ? Task.CompletedTask : command.ExecuteAsync(ShadowCaptureWriteSql.MetadataGap,
            DapperSqlParameters.Create(("MessageType", eventType), ("SchemaVersion", schemaVersion), ("NowUtc", clock.UtcNow)), token);
}

/// <summary>按流唯一控制行串行预留；调用方先持有生产者锁，控制操作遵守相同锁顺序。</summary>
internal static class ShadowCaptureWriteSql
{
    internal static readonly SqlStatement ReserveAndInsert = new("messaging.shadow_capture.reserve_and_insert", """
        UPDATE fn_messaging_shadow_capture
        SET State = CASE WHEN CapturedCount + 1 >= MaxEvents THEN 'closed' ELSE 'active' END,
            ClosedAtUtc = CASE WHEN CapturedCount + 1 >= MaxEvents THEN @NowUtc ELSE ClosedAtUtc END,
            EndReason = CASE WHEN CapturedCount + 1 >= MaxEvents THEN 'message_budget' ELSE EndReason END,
            CapturedCount = CapturedCount + 1, CapturedBytes = CapturedBytes + @PayloadBytes,
            LastReservedEventId = @Id, UpdatedAtUtc = @NowUtc
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion AND State = 'active'
            AND ExpiresAtUtc > @NowUtc AND CapturedCount < MaxEvents AND CapturedBytes + @PayloadBytes <= MaxPayloadBytes;
        INSERT INTO fn_messaging_shadow_event
            (Id, CaptureGeneration, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey, CorrelationId,
             CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
        SELECT @Id, Generation, @MessageType, @SchemaVersion, @ContentType, @TenantId, @PartitionKey, @CorrelationId,
            @CausationId, @TraceParent, @Producer, @Payload, @OccurredAtUtc
        FROM fn_messaging_shadow_capture
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion AND LastReservedEventId = @Id;
        UPDATE fn_messaging_shadow_capture
        SET State = 'closed', ClosedAtUtc = @NowUtc, UpdatedAtUtc = @NowUtc,
            EndReason = CASE WHEN ExpiresAtUtc <= @NowUtc THEN 'time_budget' ELSE 'byte_budget' END
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion AND State = 'active'
            AND (LastReservedEventId IS NULL OR LastReservedEventId <> @Id)
            AND (ExpiresAtUtc <= @NowUtc OR CapturedBytes + @PayloadBytes > MaxPayloadBytes)
        """, SqlDataScope.Global);
    internal static readonly SqlStatement MetadataGap = new("messaging.shadow_capture.metadata_gap", """
        UPDATE fn_messaging_shadow_capture
        SET MissingMetadataCount = MissingMetadataCount + 1, State = 'blocked', EndReason = 'metadata_missing',
            ClosedAtUtc = @NowUtc, UpdatedAtUtc = @NowUtc
        WHERE MessageType = @MessageType AND SchemaVersion = @SchemaVersion AND State = 'active' AND ExpiresAtUtc > @NowUtc
        """, SqlDataScope.Global);
}

/// <summary>部署策略在所有宿主启动时校验；默认关闭路径无需事件目录或额外数据库访问。</summary>
internal sealed class ShadowCaptureOptionsValidator : IValidateOptions<ShadowCaptureOptions>
{
    public ValidateOptionsResult Validate(string? name, ShadowCaptureOptions value) => !value.Enabled
        || (!string.IsNullOrWhiteSpace(value.EventType) && value.EventType.Length <= MessagingNames.MessageTypeMaxLength
            && MessagingNames.MessageTypePattern.IsMatch(value.EventType) && value.SchemaVersion > 0
            && value.MaximumEvents is >= 1 and <= 10000 && value.MaximumPayloadBytes is >= 1 and <= 33554432
            && value.MaximumWindowSeconds is >= 1 and <= 300)
        ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail("影子采集必须指定单个有效事件流及有界数量、字节和时间预算。");
}
