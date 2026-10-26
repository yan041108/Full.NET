using System.Security.Cryptography;
using System.Text;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Messaging.Auditing;
using Full.NET.Modules.Messaging.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Messaging.Features.ChangeDeliveryOwner;

/// <summary>从权威样本及真实链路建立短期切流证明；HTTP 不接受任何就绪布尔值。</summary>
internal sealed class DeliveryCutoverEvidenceService(IQueryExecutor query, ICommandExecutor command,
    IOptions<DatabaseOptions> databaseOptions, IEventDeliveryCutoverProbe probe, IIdGenerator ids, IClock clock,
    IDatabaseSessionLock sessionLock, IHttpContextAccessor context)
{
    internal async Task<Result<CutoverEvidenceResponse>> VerifyAsync(VerifyCutoverRequest request, CancellationToken cancellationToken)
    {
        if (MessagingOperationActor.Validate(MessagingOperationActor.Resolve(context), request.Reason) is { } error)
            return Result<CutoverEvidenceResponse>.Failure(error);
        if (string.IsNullOrWhiteSpace(request.EventType) || request.EventType.Length > MessagingNames.MessageTypeMaxLength
            || !MessagingNames.MessageTypePattern.IsMatch(request.EventType) || request.SchemaVersion <= 0)
            return Result<CutoverEvidenceResponse>.Failure(new(MessagingErrorCodes.CutoverPreconditionFailed, "Invalid event stream.", ErrorType.Validation));
        await using var control = await sessionLock.TryAcquireAsync(EventDeliveryControlResources.SessionLock, cancellationToken).ConfigureAwait(false);
        if (control is null) return Failure("Another delivery control operation is running.");
        var samples = await LoadSamplesAsync(request.EventType, request.SchemaVersion, cancellationToken).ConfigureAwait(false);
        if (!IsFreshCompleteSample(samples)) return Failure("A complete bounded shadow sample with recent source events is required.");
        var fingerprint = await probe.ReadTargetFingerprintAsync(request.EventType, request.SchemaVersion, cancellationToken).ConfigureAwait(false);
        if (fingerprint is null) return Failure("Target Connector and all catalog subscriptions must be running.");
        var offsets = await probe.CompareShadowAsync(request.EventType, request.SchemaVersion, samples, cancellationToken).ConfigureAwait(false);
        if (offsets is null || fingerprint != await probe.ReadTargetFingerprintAsync(request.EventType, request.SchemaVersion, cancellationToken).ConfigureAwait(false))
            return Failure("Shadow verification was incomplete or target resources changed.");
        var current = await LoadSamplesAsync(request.EventType, request.SchemaVersion, cancellationToken).ConfigureAwait(false);
        var sourceDigest = Digest(samples);
        if (sourceDigest != Digest(current)) return Failure("Source data changed during shadow verification.");
        var now = clock.UtcNow;
        var id = ids.NewId();
        var affected = await command.ExecuteAsync(CutoverEvidenceSql.Insert, new Dictionary<string, object?>
        {
            ["Id"] = id, ["EventType"] = request.EventType, ["SchemaVersion"] = request.SchemaVersion,
            ["ResourceFingerprint"] = fingerprint, ["SourceDigest"] = sourceDigest, ["ShadowPositionDigest"] = offsets,
            ["SampleCount"] = samples.Count, ["VerifiedAtUtc"] = now, ["ExpiresAtUtc"] = now.AddMinutes(5),
            ["OperatorUserId"] = MessagingOperationActor.Resolve(context)!.UserId, ["Reason"] = request.Reason,
        }, cancellationToken).ConfigureAwait(false);
        if (affected != 1) throw new InvalidOperationException("切流证明未能持久化。");
        return Result<CutoverEvidenceResponse>.Success(new(id, request.EventType, request.SchemaVersion, samples.Count, now, now.AddMinutes(5)));
    }

    internal async Task<CutoverEvidenceRow?> FindReadyAsync(string eventType, int schemaVersion, CancellationToken cancellationToken)
    {
        var fingerprint = await probe.ReadTargetFingerprintAsync(eventType, schemaVersion, cancellationToken).ConfigureAwait(false);
        if (fingerprint is null) return null;
        var evidence = await query.QuerySingleOrDefaultAsync<CutoverEvidenceRow>(databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                ? CutoverEvidenceSql.FindSqlServer : CutoverEvidenceSql.FindMySql,
            new Dictionary<string, object?> { ["EventType"] = eventType, ["SchemaVersion"] = schemaVersion }, cancellationToken).ConfigureAwait(false);
        if (evidence is null || evidence.ResourceFingerprint != fingerprint || evidence.VerifiedAtUtc > clock.UtcNow || evidence.ExpiresAtUtc <= clock.UtcNow)
            return null;
        return evidence;
    }

    internal async Task<bool> IsSourceUnchangedAsync(CutoverEvidenceRow evidence, CancellationToken cancellationToken)
    {
        var samples = await LoadSamplesAsync(evidence.EventType, evidence.SchemaVersion, cancellationToken).ConfigureAwait(false);
        return IsFreshCompleteSample(samples) && samples.Count == evidence.SampleCount && Digest(samples) == evidence.SourceDigest;
    }

    private async Task<IReadOnlyList<CutoverShadowEvent>> LoadSamplesAsync(string eventType, int schemaVersion, CancellationToken cancellationToken) =>
        await query.QueryAsync<CutoverShadowEvent>(databaseOptions.Value.Provider == DatabaseProvider.SqlServer
                ? CutoverEvidenceSql.SamplesSqlServer : CutoverEvidenceSql.SamplesMySql,
            new Dictionary<string, object?> { ["EventType"] = eventType, ["SchemaVersion"] = schemaVersion }, cancellationToken).ConfigureAwait(false);

    private bool IsFreshCompleteSample(IReadOnlyList<CutoverShadowEvent> samples) => samples.Count is > 0 and <= 10000
        && samples[0].CaptureGeneration != Guid.Empty && samples.All(item => item.CaptureGeneration == samples[0].CaptureGeneration)
        && samples.Max(item => item.OccurredAtUtc) <= clock.UtcNow && clock.UtcNow - samples.Max(item => item.OccurredAtUtc) <= TimeSpan.FromMinutes(5);
    private static string Digest(IReadOnlyList<CutoverShadowEvent> samples)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string? value)
        {
            var bytes = value is null ? [] : Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value is null ? -1 : bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        foreach (var item in samples.OrderBy(item => item.Id))
        {
            Add(item.CaptureGeneration.ToString("D")); Add(item.Id.ToString("D")); Add(item.PartitionKey);
            Add(item.OccurredAtUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add(Convert.ToHexStringLower(SHA256.HashData(item.Payload))); Add(item.ContentType);
            Add(item.TenantId?.ToString("D")); Add(item.CorrelationId); Add(item.CausationId?.ToString("D"));
            Add(item.TraceParent); Add(item.Producer);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static Result<CutoverEvidenceResponse> Failure(string message) => Result<CutoverEvidenceResponse>.Failure(new(
        MessagingErrorCodes.CutoverPreconditionFailed, message, ErrorType.BusinessRule));
}

/// <summary>切流证明持久投影；过期或资源变化不可复用。</summary>
internal sealed record CutoverEvidenceRow(Guid Id, string EventType, int SchemaVersion, string ResourceFingerprint,
    string SourceDigest, int SampleCount, DateTimeOffset VerifiedAtUtc, DateTimeOffset ExpiresAtUtc);

/// <summary>仅访问 Messaging 自有 Host 表的证明 SQL。</summary>
internal static class CutoverEvidenceSql
{
    internal static readonly SqlStatement SamplesSqlServer = new("messaging.cutover_evidence.samples.sql_server", """
        WITH SampleWindow AS (
            SELECT events.Id, events.PartitionKey, events.Payload, events.OccurredAtUtc, events.ContentType, events.TenantId,
                events.CorrelationId, events.CausationId, events.TraceParent, events.Producer, events.CaptureGeneration,
                capture.CapturedCount AS ExpectedCount, capture.CapturedBytes AS ExpectedBytes,
                SUM(CAST(DATALENGTH(events.Payload) AS bigint)) OVER () AS PayloadBytes, COUNT_BIG(*) OVER () AS SampleCount
            FROM fn_messaging_shadow_event events
            JOIN fn_messaging_shadow_capture capture ON capture.Generation = events.CaptureGeneration
                AND capture.MessageType = events.MessageType AND capture.SchemaVersion = events.SchemaVersion
            WHERE capture.MessageType = @EventType AND capture.SchemaVersion = @SchemaVersion
                AND capture.State = 'closed' AND capture.MissingMetadataCount = 0
        )
        SELECT Id, PartitionKey, Payload, OccurredAtUtc, ContentType, TenantId, CorrelationId, CausationId, TraceParent, Producer, CaptureGeneration
        FROM SampleWindow WHERE SampleCount BETWEEN 1 AND 10000 AND PayloadBytes BETWEEN 1 AND 33554432
            AND SampleCount = ExpectedCount AND PayloadBytes = ExpectedBytes ORDER BY Id
        """, SqlDataScope.Global);
    internal static readonly SqlStatement SamplesMySql = new("messaging.cutover_evidence.samples.my_sql", """
        WITH SampleWindow AS (
            SELECT events.Id, events.PartitionKey, events.Payload, events.OccurredAtUtc, events.ContentType, events.TenantId,
                events.CorrelationId, events.CausationId, events.TraceParent, events.Producer, events.CaptureGeneration,
                capture.CapturedCount AS ExpectedCount, capture.CapturedBytes AS ExpectedBytes,
                SUM(OCTET_LENGTH(events.Payload)) OVER () AS PayloadBytes, COUNT(*) OVER () AS SampleCount
            FROM fn_messaging_shadow_event events
            JOIN fn_messaging_shadow_capture capture ON capture.Generation = events.CaptureGeneration
                AND capture.MessageType = events.MessageType AND capture.SchemaVersion = events.SchemaVersion
            WHERE capture.MessageType = @EventType AND capture.SchemaVersion = @SchemaVersion
                AND capture.State = 'closed' AND capture.MissingMetadataCount = 0
        )
        SELECT Id, PartitionKey, Payload, OccurredAtUtc, ContentType, TenantId, CorrelationId, CausationId, TraceParent, Producer, CaptureGeneration
        FROM SampleWindow WHERE SampleCount BETWEEN 1 AND 10000 AND PayloadBytes BETWEEN 1 AND 33554432
            AND SampleCount = ExpectedCount AND PayloadBytes = ExpectedBytes ORDER BY Id
        """, SqlDataScope.Global);
    internal static readonly SqlStatement FindSqlServer = new("messaging.cutover_evidence.find.sql_server", """
        SELECT TOP (1) Id, EventType, SchemaVersion, ResourceFingerprint, SourceDigest, SampleCount, VerifiedAtUtc, ExpiresAtUtc
        FROM fn_messaging_cutover_evidence WHERE EventType = @EventType AND SchemaVersion = @SchemaVersion ORDER BY VerifiedAtUtc DESC, Id DESC
        """, SqlDataScope.Global);
    internal static readonly SqlStatement FindMySql = new("messaging.cutover_evidence.find.my_sql", """
        SELECT Id, EventType, SchemaVersion, ResourceFingerprint, SourceDigest, SampleCount, VerifiedAtUtc, ExpiresAtUtc
        FROM fn_messaging_cutover_evidence WHERE EventType = @EventType AND SchemaVersion = @SchemaVersion ORDER BY VerifiedAtUtc DESC, Id DESC LIMIT 1
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Insert = new("messaging.cutover_evidence.insert", """
        INSERT INTO fn_messaging_cutover_evidence
            (Id, EventType, SchemaVersion, ResourceFingerprint, SourceDigest, ShadowPositionDigest, SampleCount, VerifiedAtUtc, ExpiresAtUtc, OperatorUserId, Reason)
        VALUES (@Id, @EventType, @SchemaVersion, @ResourceFingerprint, @SourceDigest, @ShadowPositionDigest, @SampleCount, @VerifiedAtUtc, @ExpiresAtUtc, @OperatorUserId, @Reason)
        """, SqlDataScope.Global);
}
