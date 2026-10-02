using System.Text;
using Full.NET.LogConsumer;

namespace Full.NET.UnitTests.Logging;

[TestClass]
public sealed class KafkaLogRecordParserTests
{
    private const string EventId = "0199aa18-3e3b-7000-8000-5a8ab7a1f404";

    [TestMethod]
    public void ValidCompactJsonKeepsFrozenUtcTimeAndOwnsPayload()
    {
        var source = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T23:59:59+08:00","@mt":"completed","LogEventId":"{{EventId}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-30T15:59:59Z","ExpiresAtUtc":"2026-10-30T15:59:59Z","IndexRouteVersion":2}
            """);

        var result = KafkaLogRecordParser.TryParse(EventId, source, 1024, out var record);

        Assert.AreEqual(LogRecordValidationResult.Valid, result);
        Assert.IsNotNull(record);
        Assert.AreEqual(EventId, record.LogEventId);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 30, 15, 59, 59, TimeSpan.Zero), record.OccurredAtUtc);
        Assert.AreEqual("diagnostic", record.LogClass);
        Assert.AreEqual(2, record.IndexRouteVersion);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 30, 15, 59, 59, TimeSpan.Zero), record.ExpiresAtUtc);
        Assert.IsTrue(record.TryGetIndexName(new Dictionary<int, int> { [2] = 30 },
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), out var indexName));
        Assert.AreEqual("fn-logs-2-diagnostic-2026.09.30", indexName);
        Assert.IsFalse(record.TryGetIndexName(new Dictionary<int, int> { [3] = 30 },
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), out _));
        Assert.IsFalse(record.TryGetIndexName(new Dictionary<int, int> { [2] = 30 }, record.ExpiresAtUtc, out _));
        source[0] = (byte)'!';
        Assert.AreEqual((byte)'{', record.Utf8Json[0]);
    }

    [TestMethod]
    public void KeyMismatchOrMissingKeyFailsClosed()
    {
        var payload = ValidPayload();
        Assert.AreEqual(LogRecordValidationResult.MissingKey,
            KafkaLogRecordParser.TryParse(null, payload, 1024, out _));
        Assert.AreEqual(LogRecordValidationResult.IdMismatch,
            KafkaLogRecordParser.TryParse(Guid.CreateVersion7().ToString("D"), payload, 1024, out _));
    }

    [TestMethod]
    public void OversizeAndMalformedJsonFailBeforeRetainingPayload()
    {
        var payload = ValidPayload();
        Assert.AreEqual(LogRecordValidationResult.Oversize,
            KafkaLogRecordParser.TryParse(EventId, payload, payload.Length - 1, out var oversize));
        Assert.IsNull(oversize);
        Assert.AreEqual(LogRecordValidationResult.InvalidJson,
            KafkaLogRecordParser.TryParse(EventId, Encoding.UTF8.GetBytes("{"), 1024, out _));
    }

    [TestMethod]
    public void MultilineJsonCannotEnterNdjsonBulkBody()
    {
        var compact = Encoding.UTF8.GetString(ValidPayload());
        var multiline = Encoding.UTF8.GetBytes(compact.Replace(",\"@mt\"", ",\n\"@mt\"", StringComparison.Ordinal));
        Assert.AreEqual(LogRecordValidationResult.InvalidEnvelope,
            KafkaLogRecordParser.TryParse(EventId, multiline, 1024, out _));
    }

    [TestMethod]
    public void DuplicateIdentityAndUnknownClassFailClosed()
    {
        var duplicate = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"x","LogEventId":"{{EventId}}","LogEventId":"{{EventId}}","log.class":"diagnostic"}
            """);
        var unknownClass = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"x","LogEventId":"{{EventId}}","log.class":"user-supplied"}
            """);

        Assert.AreEqual(LogRecordValidationResult.InvalidEnvelope,
            KafkaLogRecordParser.TryParse(EventId, duplicate, 1024, out _));
        Assert.AreEqual(LogRecordValidationResult.InvalidClass,
            KafkaLogRecordParser.TryParse(EventId, unknownClass, 1024, out _));
    }

    [TestMethod]
    public void MissingOrInvalidTimestampFailsClosed()
    {
        var missing = Encoding.UTF8.GetBytes($$"""
            {"@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic"}
            """);
        var invalid = Encoding.UTF8.GetBytes($$"""
            {"@t":"yesterday","@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic"}
            """);

        Assert.AreEqual(LogRecordValidationResult.InvalidTimestamp,
            KafkaLogRecordParser.TryParse(EventId, missing, 1024, out _));
        Assert.AreEqual(LogRecordValidationResult.InvalidTimestamp,
            KafkaLogRecordParser.TryParse(EventId, invalid, 1024, out _));
    }

    [TestMethod]
    public void OffsetlessTimestampCannotDependOnConsumerLocalTimeZone()
    {
        var offsetless = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00","@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic"}
            """);

        Assert.AreEqual(LogRecordValidationResult.InvalidTimestamp,
            KafkaLogRecordParser.TryParse(EventId, offsetless, 1024, out var record));
        Assert.IsNull(record);
    }

    [TestMethod]
    public void ValidatedPayloadOnlyExposesAReadOnlySpan()
    {
        Assert.AreEqual(typeof(ReadOnlySpan<byte>),
            typeof(ParsedLogRecord).GetProperty(nameof(ParsedLogRecord.Utf8Json))!.PropertyType);
    }

    [TestMethod]
    public void MissingFrozenRouteDoesNotQualifyForIndexing()
    {
        var withoutRoute = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic"}
            """);
        Assert.AreEqual(LogRecordValidationResult.InvalidRoute,
            KafkaLogRecordParser.TryParse(EventId, withoutRoute, 1024, out var record));
        Assert.IsNull(record);
    }

    [TestMethod]
    public void MismatchedOrExtendedExpiryCannotBecomeARoute()
    {
        var mismatched = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-29T00:00:00Z","ExpiresAtUtc":"2026-10-30T00:00:00Z","IndexRouteVersion":2}
            """);
        var extended = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-30T00:00:00Z","ExpiresAtUtc":"2099-10-30T00:00:00Z","IndexRouteVersion":2}
            """);

        Assert.AreEqual(LogRecordValidationResult.InvalidRoute,
            KafkaLogRecordParser.TryParse(EventId, mismatched, 1024, out _));
        Assert.AreEqual(LogRecordValidationResult.InvalidRoute,
            KafkaLogRecordParser.TryParse(EventId, extended, 1024, out _));
    }

    [TestMethod]
    public void PlausibleButExtendedExpiryDoesNotMatchApprovedVersionPolicy()
    {
        var extended = Encoding.UTF8.GetBytes($$"""
            {"@t":"2026-09-30T00:00:00Z","@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-30T00:00:00Z","ExpiresAtUtc":"2026-10-31T00:00:00Z","IndexRouteVersion":2}
            """);

        Assert.AreEqual(LogRecordValidationResult.Valid,
            KafkaLogRecordParser.TryParse(EventId, extended, 1024, out var record));
        Assert.IsNotNull(record);
        Assert.IsFalse(record.TryGetIndexName(new Dictionary<int, int> { [2] = 30 },
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), out _));
    }

    private static byte[] ValidPayload() => Encoding.UTF8.GetBytes($$"""
        {"@t":"2026-09-30T00:00:00Z","@mt":"x","LogEventId":"{{EventId}}","log.class":"diagnostic","OccurredAtUtc":"2026-09-30T00:00:00Z","ExpiresAtUtc":"2026-10-30T00:00:00Z","IndexRouteVersion":2}
        """);
}
