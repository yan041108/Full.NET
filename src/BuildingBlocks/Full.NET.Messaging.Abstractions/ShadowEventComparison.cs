using System.Security.Cryptography;

namespace Full.NET.Messaging.Abstractions;

/// <summary>
/// SHA-256 payload digest for shadow comparison; same algorithm as Inbox.
/// </summary>
public static class IntegrationEventPayloadHash
{
    public static byte[] Compute(ReadOnlySpan<byte> payload) => SHA256.HashData(payload);

    public static bool Equals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.SequenceEqual(right);
}

/// <summary>
/// Shadow comparison fingerprint without broker position metadata.
/// </summary>
public sealed class ShadowEventFingerprint
{
    public Guid EventId { get; }

    public string MessageType { get; }

    public int SchemaVersion { get; }

    public string PartitionKey { get; }

    public byte[] PayloadHash { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    private ShadowEventFingerprint(
        Guid eventId,
        string messageType,
        int schemaVersion,
        string partitionKey,
        byte[] payloadHash,
        DateTimeOffset occurredAtUtc)
    {
        EventId = eventId;
        MessageType = messageType;
        SchemaVersion = schemaVersion;
        PartitionKey = partitionKey;
        PayloadHash = payloadHash;
        OccurredAtUtc = occurredAtUtc;
    }

    public static ShadowEventFingerprint FromEnvelope(IntegrationEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return new ShadowEventFingerprint(
            envelope.EventId,
            envelope.MessageType,
            envelope.SchemaVersion,
            envelope.PartitionKey,
            IntegrationEventPayloadHash.Compute(envelope.Payload.Span),
            envelope.OccurredAtUtc);
    }

    public static ShadowEventFingerprint Create(
        Guid eventId,
        string messageType,
        int schemaVersion,
        string partitionKey,
        ReadOnlySpan<byte> payload,
        DateTimeOffset occurredAtUtc)
    {
        IntegrationEventEnvelope.ValidateMessageType(messageType);
        IntegrationEventEnvelope.ValidateSchemaVersion(schemaVersion);
        IntegrationEventMetadata.ValidatePartitionKey(partitionKey);
        if (payload.IsEmpty)
        {
            throw new ArgumentException(
                IntegrationEventFailureCodes.PayloadRequired,
                nameof(payload));
        }

        return new ShadowEventFingerprint(
            eventId,
            messageType,
            schemaVersion,
            partitionKey,
            IntegrationEventPayloadHash.Compute(payload),
            occurredAtUtc);
    }
}

/// <summary>
/// Monotonic CDC or shadow-consumer source position within a provider stream.
/// </summary>
public readonly struct ShadowSourcePosition
    : IComparable<ShadowSourcePosition>, IEquatable<ShadowSourcePosition>
{
    public string Provider { get; }

    public string StreamKey { get; }

    public long Sequence { get; }

    public ShadowSourcePosition(string provider, string streamKey, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamKey);
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                sequence,
                "The sequence must be non-negative.");
        }

        Provider = provider;
        StreamKey = streamKey;
        Sequence = sequence;
    }

    public int CompareTo(ShadowSourcePosition other)
    {
        var providerCompare = StringComparer.Ordinal.Compare(Provider, other.Provider);
        if (providerCompare != 0)
        {
            return providerCompare;
        }

        var streamCompare = StringComparer.Ordinal.Compare(StreamKey, other.StreamKey);
        if (streamCompare != 0)
        {
            return streamCompare;
        }

        return Sequence.CompareTo(other.Sequence);
    }

    public bool Equals(ShadowSourcePosition other) =>
        string.Equals(Provider, other.Provider, StringComparison.Ordinal)
        && string.Equals(StreamKey, other.StreamKey, StringComparison.Ordinal)
        && Sequence == other.Sequence;

    public override bool Equals(object? obj) =>
        obj is ShadowSourcePosition other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Provider, StreamKey, Sequence);
}

public enum ShadowComparisonOutcome
{
    Match = 0,
    MissingExpected = 1,
    FieldMismatch = 2,
    PayloadMismatch = 3,
    DuplicateObserved = 4,
    PositionRegression = 5,
}

/// <summary>
/// Shadow comparison evidence; never invokes business handlers.
/// </summary>
public sealed class ShadowEventComparisonResult
{
    public ShadowComparisonOutcome Outcome { get; }

    public string? MismatchField { get; }

    public ShadowEventFingerprint? Expected { get; }

    public ShadowEventFingerprint? Observed { get; }

    public ShadowSourcePosition? ObservedPosition { get; }

    private ShadowEventComparisonResult(
        ShadowComparisonOutcome outcome,
        string? mismatchField,
        ShadowEventFingerprint? expected,
        ShadowEventFingerprint? observed,
        ShadowSourcePosition? observedPosition)
    {
        Outcome = outcome;
        MismatchField = mismatchField;
        Expected = expected;
        Observed = observed;
        ObservedPosition = observedPosition;
    }

    public bool IsMatch => Outcome == ShadowComparisonOutcome.Match;

    public static ShadowEventComparisonResult Match(
        ShadowEventFingerprint fingerprint,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.Match,
            mismatchField: null,
            expected: fingerprint,
            observed: fingerprint,
            observedPosition: position);

    public static ShadowEventComparisonResult MissingExpected(
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.MissingExpected,
            mismatchField: null,
            expected: null,
            observed: observed,
            observedPosition: position);

    public static ShadowEventComparisonResult FieldMismatch(
        string mismatchField,
        ShadowEventFingerprint expected,
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.FieldMismatch,
            mismatchField: mismatchField,
            expected: expected,
            observed: observed,
            observedPosition: position);

    public static ShadowEventComparisonResult PayloadMismatch(
        ShadowEventFingerprint expected,
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.PayloadMismatch,
            mismatchField: nameof(ShadowEventFingerprint.PayloadHash),
            expected: expected,
            observed: observed,
            observedPosition: position);

    public static ShadowEventComparisonResult DuplicateObserved(
        ShadowEventFingerprint observed,
        ShadowSourcePosition? position) =>
        new(
            ShadowComparisonOutcome.DuplicateObserved,
            mismatchField: null,
            expected: null,
            observed: observed,
            observedPosition: position);

    public static ShadowEventComparisonResult PositionRegression(
        ShadowSourcePosition previous,
        ShadowSourcePosition current) =>
        new(
            ShadowComparisonOutcome.PositionRegression,
            mismatchField: nameof(ShadowSourcePosition.Sequence),
            expected: null,
            observed: null,
            observedPosition: current);
}

/// <summary>
/// Compares authoritative outbox fingerprints with shadow topic observations.
/// </summary>
public sealed class ShadowEventComparator
{
    public ShadowEventComparisonResult CompareExpectedToObserved(
        ShadowEventFingerprint? expected,
        ShadowEventFingerprint observed,
        ShadowSourcePosition? observedPosition,
        bool duplicateObserved = false)
    {
        ArgumentNullException.ThrowIfNull(observed);
        if (duplicateObserved)
        {
            return ShadowEventComparisonResult.DuplicateObserved(observed, observedPosition);
        }

        if (expected is null)
        {
            return ShadowEventComparisonResult.MissingExpected(observed, observedPosition);
        }

        if (expected.EventId != observed.EventId)
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.EventId),
                expected,
                observed,
                observedPosition);
        }

        if (!string.Equals(
                expected.MessageType,
                observed.MessageType,
                StringComparison.Ordinal))
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.MessageType),
                expected,
                observed,
                observedPosition);
        }

        if (expected.SchemaVersion != observed.SchemaVersion)
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.SchemaVersion),
                expected,
                observed,
                observedPosition);
        }

        if (!string.Equals(
                expected.PartitionKey,
                observed.PartitionKey,
                StringComparison.Ordinal))
        {
            return ShadowEventComparisonResult.FieldMismatch(
                nameof(ShadowEventFingerprint.PartitionKey),
                expected,
                observed,
                observedPosition);
        }

        if (!IntegrationEventPayloadHash.Equals(
                expected.PayloadHash.AsSpan(),
                observed.PayloadHash.AsSpan()))
        {
            return ShadowEventComparisonResult.PayloadMismatch(
                expected,
                observed,
                observedPosition);
        }

        return ShadowEventComparisonResult.Match(expected, observedPosition);
    }

    public ShadowEventComparisonResult ValidateMonotonicPosition(
        ShadowSourcePosition? previous,
        ShadowSourcePosition current)
    {
        if (previous is ShadowSourcePosition previousPosition
            && string.Equals(previousPosition.Provider, current.Provider, StringComparison.Ordinal)
            && string.Equals(previousPosition.StreamKey, current.StreamKey, StringComparison.Ordinal)
            && current.Sequence <= previousPosition.Sequence)
        {
            return ShadowEventComparisonResult.PositionRegression(previousPosition, current);
        }

        return ShadowEventComparisonResult.Match(
            ShadowEventFingerprint.Create(
                Guid.Empty,
                "fullnet.messaging.shadow.position",
                1,
                "shadow-position",
                [0x00],
                DateTimeOffset.UnixEpoch),
            current);
    }
}
