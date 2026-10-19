using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Observability;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Auditing.Features.QueryLogTimeline;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Middleware;
using Full.NET.Modules.ObservabilityAdmin.Features.QueryLogTimeline;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class LogTimelineTests
{
    private static LogTimelineQuery Query() => new(DateTimeOffset.Parse("2026-10-16T00:00:00Z"),
        DateTimeOffset.Parse("2026-10-16T01:00:00Z"), new string('a', 32), null, null, null, 100);

    [TestMethod]
    public void Query_RequiresBoundedTimeAndAtLeastOneExactSelector()
    {
        var query = Query();
        Assert.IsNull(LogTimelineQueryService.Validate(query));
        Assert.IsNotNull(LogTimelineQueryService.Validate(query with { TraceId = null }));
        Assert.IsNotNull(LogTimelineQueryService.Validate(query with { ToUtc = query.FromUtc.AddDays(2) }));
        Assert.IsNotNull(LogTimelineQueryService.Validate(query with { TraceId = "request-1" }));
        Assert.IsNotNull(LogTimelineQueryService.Validate(query with { TraceId = new string('0', 32) }));
        Assert.IsNotNull(LogTimelineQueryService.Validate(query with { ClientIpFingerprint = "127.0.0.1" }));
        Assert.IsNull(LogTimelineQueryService.Validate(query with { TraceId = null, SessionId = Guid.NewGuid() }));
    }

    [TestMethod]
    public async Task HttpContext_OnlyCapturesAuthenticatedClaimsAndFreezesBeforeExit()
    {
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        var user = Guid.NewGuid(); var session = Guid.NewGuid();
        var claims = new[] { new Claim(FullNetIdentityClaimTypes.Subject, user.ToString()), new Claim("sid", session.ToString()) };
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims)) };
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers.Authorization = "Bearer should-never-be-recorded";
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.8";
        Assert.IsNull(LogCorrelationMiddleware.Capture(context).UserId);
        var middleware = new LogCorrelationMiddleware(_ =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "verified"));
            Assert.AreEqual(session, LogCorrelationContext.Capture().SessionId);
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        var frozen = HttpLogCorrelation.Capture(context);
        Assert.AreEqual(user, frozen.UserId);
        Assert.AreEqual(session, frozen.SessionId);
        Assert.AreEqual(activity.TraceId.ToString(), frozen.TraceId);
        Assert.AreEqual(HttpOperationLogSanitizer.FingerprintClientIp("127.0.0.1"), frozen.ClientIpFingerprint);
        Assert.IsFalse(frozen.ToJson().Contains("should-never", StringComparison.Ordinal));
        Assert.IsNull(LogCorrelationContext.Capture().UserId);
    }

    [TestMethod]
    [DataRow("ordinary event")]
    [DataRow("Authorization: Bearer sensitive-value")]
    public void B2Envelope_KeepsSessionIdentifiersAndIpForOrdinaryLogs(string template)
    {
        var session = Guid.NewGuid(); var user = Guid.NewGuid();
        using var context = LogCorrelationContext.BeginScope(() => new() { SessionId = session, UserId = user, ClientIpFingerprint = new string('c', 64) });
        var sink = new Sink();
        using var logger = new LoggerConfiguration().Enrich.With(new LogEventMetadataEnricher(Guid.NewGuid().ToString()))
            .WriteTo.Sink(sink).CreateLogger();
        logger.Information(template);
        Assert.IsNotNull(sink.Event);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(sink.Event, 4096, out var envelope));
        using var json = JsonDocument.Parse(envelope!.Utf8Json);
        Assert.AreEqual(session.ToString(), json.RootElement.GetProperty("SessionId").GetString());
        Assert.AreEqual(user.ToString(), json.RootElement.GetProperty("UserId").GetString());
        Assert.AreEqual(new string('c', 64), json.RootElement.GetProperty("ClientIpFingerprint").GetString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void B2Metadata_MissingTrustedValuesRemoveCallerCorrelation(bool establishEmptyScope)
    {
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            using var scope = establishEmptyScope ? LogCorrelationContext.BeginScope(() => new()) : null;
            var log = EmitCallerCorrelation();
            foreach (var name in CorrelationMetadataNames)
                Assert.IsFalse(log.Properties.ContainsKey(name), $"Unverified correlation property retained: {name}");
        }
        finally { Activity.Current = previous; }
    }

    [TestMethod]
    public void B2Metadata_PartialTrustedSnapshotDoesNotInheritOtherCallerIdentities()
    {
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            var user = Guid.NewGuid();
            using var scope = LogCorrelationContext.BeginScope(() => new() { UserId = user });
            var log = EmitCallerCorrelation();
            Assert.AreEqual(user, ((ScalarValue)log.Properties["UserId"]).Value);
            foreach (var name in CorrelationMetadataNames.Where(name => name != "UserId"))
                Assert.IsFalse(log.Properties.ContainsKey(name), $"Missing trusted value inherited caller property: {name}");
        }
        finally { Activity.Current = previous; }
    }

    [TestMethod]
    public void B2Metadata_TrustedSnapshotAndCurrentActivityOverrideCallerCorrelation()
    {
        using var activity = new Activity("metadata").SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom()).Start();
        var snapshot = new LogCorrelationSnapshot
        {
            UserId = Guid.NewGuid(), TenantId = Guid.NewGuid(), SessionId = Guid.NewGuid(),
            CenterSessionId = Guid.NewGuid(), ApplicationSessionId = Guid.NewGuid(),
            RequestId = "verified-request", ClientIpFingerprint = new string('f', 64),
        };
        using var scope = LogCorrelationContext.BeginScope(() => snapshot);
        var log = EmitCallerCorrelation();
        var expected = new Dictionary<string, object?>
        {
            ["TraceId"] = activity.TraceId.ToHexString(), ["SpanId"] = activity.SpanId.ToHexString(),
            ["ParentSpanId"] = activity.ParentSpanId.ToHexString(), ["RequestId"] = snapshot.RequestId,
            ["UserId"] = snapshot.UserId, ["TenantId"] = snapshot.TenantId, ["SessionId"] = snapshot.SessionId,
            ["CenterSessionId"] = snapshot.CenterSessionId, ["ApplicationSessionId"] = snapshot.ApplicationSessionId,
            ["ClientIpFingerprint"] = snapshot.ClientIpFingerprint,
        };
        foreach (var (name, value) in expected)
            Assert.AreEqual(value, ((ScalarValue)log.Properties[name]).Value, name);
    }

    private static readonly string[] CorrelationMetadataNames =
    [
        "TraceId", "SpanId", "ParentSpanId", "RequestId", "UserId", "TenantId",
        "SessionId", "CenterSessionId", "ApplicationSessionId", "ClientIpFingerprint",
    ];

    private static LogEvent EmitCallerCorrelation()
    {
        var properties = CorrelationMetadataNames.Select(name => new LogEventProperty(name,
            new ScalarValue(name switch
            {
                "TraceId" => new string('a', 32),
                "SpanId" or "ParentSpanId" => new string('b', 16),
                "ClientIpFingerprint" => new string('c', 64),
                "RequestId" => "caller-request",
                _ => (object)Guid.NewGuid(),
            })));
        var log = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplate("ordinary event", []), properties);
        var sink = new Sink();
        using var logger = new LoggerConfiguration().Enrich.With(new LogEventMetadataEnricher(Guid.NewGuid().ToString()))
            .WriteTo.Sink(sink).CreateLogger();
        logger.Write(log);
        Assert.IsNotNull(sink.Event);
        return sink.Event;
    }

    [TestMethod]
    public void ExplicitAuditTrace_RemainsCompatibleWhileSnapshotUsesActualActivity()
    {
        using var activity = new Activity("audit").SetIdFormat(ActivityIdFormat.W3C).Start();
        const string supplied = "outbound-explicit-reference";
        var outbound = Full.NET.Modules.Auditing.Features.WriteOutboundCallLogs.OutboundCallAuditHandler.Sanitize(
            new Full.NET.Modules.Auditing.Contracts.OutboundCallAuditRequest("provider", "operation", "api.example.com",
                200, true, 1, 1, supplied));
        Assert.AreEqual(supplied, outbound.Record.TraceId);
        Assert.AreEqual(activity.TraceId.ToHexString(), LogCorrelationSnapshot.FromJson(outbound.Record.CorrelationJson)!.TraceId);
        var authentication = new Full.NET.Modules.Identity.Domain.AuthAuditEvent(Guid.NewGuid(), null, null,
            "fingerprint", "login", "ok", true, null, null, null, DateTimeOffset.UtcNow, TraceId: supplied);
        Assert.AreEqual(supplied, authentication.TraceId);
        Assert.AreEqual(activity.TraceId.ToHexString(), LogCorrelationSnapshot.FromJson(authentication.CorrelationJson)!.TraceId);
    }

    [TestMethod]
    public void AuditBatch_UsesIndividualCaptureTimesAndCountsAllParameters()
    {
        var first = new OperationLogWriteModel("update", "POST", "/test", 200, 1, true, null, null, null, null, null)
            { CapturedAtUtc = Query().FromUtc };
        var second = first with { CapturedAtUtc = first.CapturedAtUtc.AddMilliseconds(1) };
        var batch = AuditWriteBatchSql.BuildOperations([(Guid.NewGuid(), first), (Guid.NewGuid(), second)], DateTimeOffset.UtcNow);
        Assert.AreEqual(first.CapturedAtUtc, batch.Parameters["o0_OccurredAtUtc"]);
        Assert.AreEqual(second.CapturedAtUtc, batch.Parameters["o1_OccurredAtUtc"]);
        Assert.AreEqual(32, batch.ParameterCount);
        Assert.AreEqual(first.CorrelationJson, batch.Parameters["o0_CorrelationJson"]);
        Assert.IsGreaterThan(AuditWriteEnvelope.ForOperation(first with { CorrelationJson = null }).EstimatedBytes,
            AuditWriteEnvelope.ForOperation(first).EstimatedBytes);
    }

    [TestMethod]
    public void RuntimeParser_UsesNativeTraceFieldsAndAppliesAllSelectors()
    {
        var query = Query() with { UserId = Guid.NewGuid(), SessionId = Guid.NewGuid(), ClientIpFingerprint = new string('c', 64) };
        var json = "{\"@t\":\"2026-10-16T00:30:00Z\",\"LogEventId\":\"" + Guid.NewGuid()
            + "\",\"@tr\":\"" + query.TraceId + "\",\"UserId\":\"" + query.UserId
            + "\",\"ApplicationSessionId\":\"" + query.SessionId + "\",\"ClientIpFingerprint\":\"" + query.ClientIpFingerprint
            + "\",\"@mt\":\"private-body-not-returned\"}";
        using var document = JsonDocument.Parse(json);
        var result = RuntimeLogTimelineParser.Parse(document.RootElement, "runtime.test", query);
        Assert.IsNotNull(result);
        Assert.AreEqual(query.TraceId, result.Correlation.TraceId);
        Assert.AreEqual("diagnostic", result.Summary);
        Assert.IsNull(RuntimeLogTimelineParser.Parse(document.RootElement, "runtime.test", query with { UserId = Guid.NewGuid() }));
    }

    [TestMethod]
    public void EsQuery_IsMetadataOnlyAndRejectsUnsafeEndpoints()
    {
        var config = new RuntimeLogTimelineOptions { ElasticsearchEndpoint = "https://logs.example.test/" };
        Assert.IsTrue(ElasticsearchTimelineReader.ValidConfiguration(config));
        config.ElasticsearchEndpoint = "https://user:password@logs.example.test/";
        Assert.IsFalse(ElasticsearchTimelineReader.ValidConfiguration(config));
        config.ElasticsearchEndpoint = "https://logs.example.test/"; config.ElasticsearchIndexPattern = "*";
        Assert.IsFalse(ElasticsearchTimelineReader.ValidConfiguration(config));
        using var document = JsonDocument.Parse(ElasticsearchTimelineReader.BuildQuery(Query()));
        var root = document.RootElement;
        Assert.AreEqual(101, root.GetProperty("size").GetInt32());
        Assert.IsFalse(root.GetProperty("track_total_hits").GetBoolean());
        Assert.IsFalse(root.GetProperty("_source").EnumerateArray().Any(value => value.GetString() == "@mt"));
    }

    private sealed class Sink : ILogEventSink
    {
        public LogEvent? Event { get; private set; }
        public void Emit(LogEvent logEvent) => Event = logEvent;
    }
}
