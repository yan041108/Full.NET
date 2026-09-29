using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Full.NET.Hosting.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Serilog.Sinks.Async;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class HighPriorityLoggingTests
{
    [TestMethod]
    public void General_queue_saturation_does_not_delay_error_delivery()
    {
        using var monitors = new FullNetLoggingMonitors();
        using var generalSink = new BlockingSink();
        var highPrioritySink = new CollectingSink();
        using var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 1,
            highPriorityBufferSize: 4);

        logger.Information("block general channel");
        Assert.IsTrue(generalSink.WaitUntilEntered());
        for (var index = 0; index < 128; index++)
        {
            logger.Information("general event {Index}", index);
        }

        Assert.IsTrue(SpinWait.SpinUntil(
            () => monitors.General.Snapshot.DroppedMessagesCount > 0,
            TimeSpan.FromSeconds(2)));

        logger.Error("must use independent channel");

        Assert.IsTrue(highPrioritySink.WaitForCount(1));
        Assert.AreEqual(
            "must use independent channel",
            highPrioritySink.Events.Single().RenderMessage());
        generalSink.Release();
    }

    [TestMethod]
    public void Byte_budget_remains_reserved_while_sink_is_blocked_and_is_released_on_completion()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        using var downstream = new BlockingSink();
        var source = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("bounded event"),
            []);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 1024, out var sample, retainLegacyEvent: true));
        Assert.IsNotNull(sample);
        var sink = new FullNetBoundedAsyncSink(
            downstream,
            4,
            "test byte budget",
            monitor,
            maxEventBytes: 1024,
            queueMaxBytes: LogEnvelope.MaxChargeBytes(1024, true));

        sink.Emit(source);
        Assert.IsTrue(downstream.WaitUntilEntered());
        Assert.AreEqual(sample.ChargeBytes, sink.ReservedBytes);

        sink.Emit(source);
        Assert.AreEqual(1, sink.DroppedMessagesCount);
        Assert.AreEqual(sample.ChargeBytes, sink.ReservedBytes);

        sink.Complete();
        Assert.IsFalse(sink.WaitForCompletion(TimeSpan.FromMilliseconds(20)));
        Assert.AreEqual(sample.ChargeBytes, sink.ReservedBytes);
        downstream.Release();
        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(0, sink.ReservedBytes);
    }

    [TestMethod]
    public void Byte_admission_precedes_snapshot_construction_when_budget_is_full()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        using var downstream = new BlockingSink();
        var parser = new MessageTemplateParser();
        var small = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            parser.Parse("small"),
            []);
        var oversized = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            parser.Parse("oversized"),
            [new LogEventProperty("LogEventId", new ScalarValue(new string('x', 4096)))]);
        var sink = new FullNetBoundedAsyncSink(
            downstream,
            bufferSize: 4,
            workerName: "test prebuild admission",
            monitor,
            maxEventBytes: 256,
            queueMaxBytes: LogEnvelope.MaxChargeBytes(256, true));

        sink.Emit(small);
        Assert.IsTrue(downstream.WaitUntilEntered());
        sink.Emit(oversized);

        Assert.AreEqual(1L, sink.ByteBudgetDroppedCount);
        Assert.AreEqual(0L, sink.OversizeCount);
        downstream.Release();
        sink.Complete();
        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
    }

    [TestMethod]
    public void Snapshot_consumer_receives_preformatted_bytes_without_legacy_reserialization()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var legacy = new CollectingSink();
        var captured = new ConcurrentBag<byte[]>();
        var source = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("already formatted {Value}"),
            [new LogEventProperty("Value", new ScalarValue("safe"))]);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(source, 1024, out var expected));
        Assert.IsNotNull(expected);
        var sink = new FullNetBoundedAsyncSink(
            legacy,
            4,
            "test snapshot output",
            monitor,
            maxEventBytes: 1024,
            queueMaxBytes: 4096,
            emitSnapshot: envelope => captured.Add(envelope.Utf8Json.ToArray()),
            emitLegacySink: false);

        sink.Emit(source);
        sink.Complete();

        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
        CollectionAssert.AreEqual(expected.Utf8Json.ToArray(), captured.Single());
        Assert.IsEmpty(legacy.Events);
    }

    [TestMethod]
    public void External_snapshot_contract_carries_matching_event_id_and_priority_without_legacy_event()
    {
        using var monitors = new FullNetLoggingMonitors();
        var snapshots = new ConcurrentBag<HostLogSnapshot>();
        var configuration = new LoggerConfiguration();
        FullNetLoggingPipeline.Configure(
            configuration,
            "Full.NET.UnitTests",
            new LoggingOptions(),
            monitors,
            _ => { },
            _ => { },
            emitLegacySink: false,
            emitExternalSnapshot: snapshots.Add);

        using (var logger = configuration.CreateLogger())
        {
            logger.Information("general snapshot");
            logger.Error("priority snapshot");
        }

        Assert.HasCount(2, snapshots);
        Assert.HasCount(1, snapshots.Where(snapshot => snapshot.IsHighPriority));
        foreach (var snapshot in snapshots)
        {
            Assert.IsTrue(Guid.TryParseExact(snapshot.LogEventId, "D", out _));
            StringAssert.Contains(
                System.Text.Encoding.UTF8.GetString(snapshot.Utf8Json.Span),
                snapshot.LogEventId);
        }
    }

    [TestMethod]
    public void External_snapshot_rejects_an_unenriched_event_without_inventing_a_second_id()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var captured = new ConcurrentBag<HostLogSnapshot>();
        var source = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("unenriched"),
            []);
        var sink = new FullNetBoundedAsyncSink(
            new CollectingSink(),
            4,
            "test missing external id",
            monitor,
            maxEventBytes: 1024,
            queueMaxBytes: 4096,
            emitLegacySink: false,
            emitExternalSnapshot: captured.Add);

        sink.Emit(source);
        sink.Complete();

        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
        Assert.IsEmpty(captured);
        Assert.AreEqual(1L, sink.DroppedMessagesCount);
    }

    [TestMethod]
    public void Legacy_worker_receives_safe_copy_with_formatted_date_and_dictionary()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var downstream = new CollectingSink();
        var source = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("at {Time:yyyy} {Metadata}"),
            [
                new LogEventProperty(
                    "Time",
                    new ScalarValue(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero))),
                new LogEventProperty(
                    "Metadata",
                    new DictionaryValue(
                    [new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        new ScalarValue("code"),
                        new ScalarValue(42))])),
            ]);
        var sink = new FullNetBoundedAsyncSink(
            downstream,
            bufferSize: 4,
            workerName: "test legacy compatibility",
            monitor,
            maxEventBytes: 4096,
            queueMaxBytes: LogEnvelope.MaxChargeBytes(4096, true));

        sink.Emit(source);
        sink.Complete();

        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
        var restored = downstream.Events.Single();
        Assert.AreEqual(source.RenderMessage(), restored.RenderMessage());
        Assert.IsInstanceOfType<DictionaryValue>(restored.Properties["Metadata"]);
        Assert.IsInstanceOfType<DateTimeOffset>(((ScalarValue)restored.Properties["Time"]).Value);
    }

    [TestMethod]
    public void Abandoning_queued_snapshots_keeps_blocked_sink_bytes_reserved()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        using var downstream = new BlockingSink();
        var source = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("queued event"),
            []);
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(
            source, 1024, out var sample, retainLegacyEvent: true));
        Assert.IsNotNull(sample);
        var sink = new FullNetBoundedAsyncSink(
            downstream,
            4,
            "test abandonment",
            monitor,
            maxEventBytes: 1024,
            queueMaxBytes: LogEnvelope.MaxChargeBytes(1024, true) + sample.ChargeBytes);

        sink.Emit(source);
        Assert.IsTrue(downstream.WaitUntilEntered());
        sink.Emit(source);
        Assert.AreEqual(sample.ChargeBytes * 2, sink.ReservedBytes);

        sink.Complete();
        Assert.IsFalse(sink.WaitForCompletion(TimeSpan.FromMilliseconds(20)));
        sink.AbandonPending();

        Assert.AreEqual(sample.ChargeBytes, sink.ReservedBytes);
        downstream.Release();
        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(0, sink.ReservedBytes);
    }

    [TestMethod]
    public void Oversize_event_is_rejected_without_retaining_snapshot_bytes()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var downstream = new CollectingSink();
        var source = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("small"),
            [new LogEventProperty(
                "LogEventId",
                new ScalarValue(new string('x', 4096)))]);
        var sink = new FullNetBoundedAsyncSink(
            downstream,
            4,
            "test oversize",
            monitor,
            maxEventBytes: 256,
            queueMaxBytes: LogEnvelope.MaxChargeBytes(256, true));

        sink.Emit(source);
        sink.Complete();

        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
        Assert.AreEqual(1, sink.DroppedMessagesCount);
        Assert.AreEqual(1, sink.OversizeCount);
        Assert.AreEqual(0, sink.ReservedBytes);
        Assert.IsEmpty(downstream.Events);
    }

    [TestMethod]
    public void Ordinary_events_receive_diagnostic_class_and_stable_unique_id_before_routing()
    {
        using var monitors = new FullNetLoggingMonitors();
        var generalSink = new CollectingSink();
        var prioritySink = new CollectingSink();
        using var logger = CreateLogger(
            monitors,
            generalSink,
            prioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 4);

        logger.ForContext("LogEventId", "caller-controlled")
            .Information("ordinary event");
        logger.ForContext("log.class", LogClassification.Security)
            .Error("security event");

        Assert.IsTrue(generalSink.WaitForCount(1));
        Assert.IsTrue(prioritySink.WaitForCount(1));
        var ordinary = generalSink.Events.Single();
        var security = prioritySink.Events.Single();
        Assert.AreEqual(
            LogClassification.Diagnostic,
            ((ScalarValue)ordinary.Properties["log.class"]).Value);
        Assert.AreEqual(
            LogClassification.Security,
            ((ScalarValue)security.Properties["log.class"]).Value);
        var ordinaryId = (string)((ScalarValue)ordinary.Properties["LogEventId"]).Value!;
        var securityId = (string)((ScalarValue)security.Properties["LogEventId"]).Value!;
        Assert.IsTrue(Guid.TryParse(ordinaryId, out var first));
        Assert.IsTrue(Guid.TryParse(securityId, out var second));
        Assert.AreEqual('7', ordinaryId[14]);
        Assert.AreEqual('7', securityId[14]);
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void Resource_metadata_is_captured_once_and_events_share_only_its_instance_id()
    {
        using var monitors = new FullNetLoggingMonitors();
        var generalSink = new CollectingSink();
        var resource = LoggingResourceMetadata.Create(
            "Full.NET.Host.Api",
            "Production");
        using var logger = CreateLogger(
            monitors,
            generalSink,
            new CollectingSink(),
            generalBufferSize: 4,
            highPriorityBufferSize: 4,
            resource: resource);

        logger.ForContext("Instance", "forged-instance").Information("first");
        logger.Information("second");

        Assert.IsTrue(generalSink.WaitForCount(2));
        foreach (var logEvent in generalSink.Events)
        {
            Assert.AreEqual(
                resource.Instance,
                ((ScalarValue)logEvent.Properties["Instance"]).Value);
            Assert.IsFalse(logEvent.Properties.ContainsKey("OSDescription"));
            Assert.IsFalse(logEvent.Properties.ContainsKey("RuntimeVersion"));
            Assert.IsFalse(logEvent.Properties.ContainsKey("FrameworkVersion"));
        }

        Assert.AreEqual("Api", resource.HostRole);
        Assert.AreEqual("Production", resource.Environment);
        Assert.IsFalse(string.IsNullOrWhiteSpace(resource.OSDescription));
        Assert.IsFalse(string.IsNullOrWhiteSpace(resource.RuntimeVersion));
        Assert.IsFalse(string.IsNullOrWhiteSpace(resource.FrameworkVersion));
    }

    [TestMethod]
    public async Task Resource_announcement_is_written_only_once_at_host_start()
    {
        var sink = new CollectingSink();
        using var serilog = new LoggerConfiguration()
            .WriteTo.Sink(sink)
            .CreateLogger();
        using var loggerFactory = LoggerFactory.Create(
            builder => builder.AddSerilog(serilog));
        var resource = LoggingResourceMetadata.Create(
            "Full.NET.Host.Worker",
            "Production");
        var service = new LoggingResourceAnnouncementService(
            resource,
            loggerFactory.CreateLogger<LoggingResourceAnnouncementService>());

        await service.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);

        Assert.IsTrue(sink.WaitForCount(1));
        var announced = sink.Events.Single();
        Assert.AreEqual(
            resource.Instance,
            ((ScalarValue)announced.Properties["Instance"]).Value);
        Assert.AreEqual(
            "Worker",
            ((ScalarValue)announced.Properties["HostRole"]).Value);
        Assert.IsTrue(announced.Properties.ContainsKey("OSDescription"));
    }

    [TestMethod]
    public void Typed_http_ingress_emits_only_middleware_record_into_bounded_pipeline()
    {
        var sink = new CollectingSink();
        using var monitors = new FullNetLoggingMonitors();
        var ingress = new HttpOperationLogIngress();
        using var pipeline = new FullNetLoggingPipelineSink(
            sink,
            new CollectingSink(),
            new LoggingOptions(),
            monitors,
            resource: LoggingResourceMetadata.Create("Full.NET.Host.Api", "Test"),
            httpOperationIngress: ingress);
        var fields = new Dictionary<string, object?>
        {
            ["log.class"] = LogClassification.HttpOperation,
            ["http.route"] = "/orders/{id}",
        };
        ingress.Emit(new HttpOperationLogRecord(fields), LogEventLevel.Information);

        Assert.IsTrue(sink.WaitForCount(1));
        Assert.IsTrue(LogEnvelopeBuilder.TryBuild(sink.Events.Single(), 4096, out var envelope));
        Assert.IsNotNull(envelope);
        using var json = System.Text.Json.JsonDocument.Parse(envelope.Utf8Json);
        Assert.AreEqual(LogClassification.Diagnostic,
            json.RootElement.GetProperty("log.class").GetString());
        Assert.IsFalse(json.RootElement.TryGetProperty("http.route", out _));
        Assert.AreEqual("/orders/{id}",
            ((ScalarValue)sink.Events.Single().Properties["http.route"]).Value);
    }

    [TestMethod]
    public void Typed_http_priority_warning_uses_high_priority_lane()
    {
        using var monitors = new FullNetLoggingMonitors();
        var general = new CollectingSink();
        var priority = new CollectingSink();
        var ingress = new HttpOperationLogIngress();
        using var pipeline = new FullNetLoggingPipelineSink(
            general,
            priority,
            new LoggingOptions(),
            monitors,
            resource: LoggingResourceMetadata.Create("Full.NET.Host.Api", "Test"),
            httpOperationIngress: ingress);

        Assert.AreEqual(true, ingress.Emit(new HttpOperationLogRecord([
            new("log.class", LogClassification.HttpOperation),
            new("reliability.class", "Priority"),
            new("http.route", "/api/orders/{id}"),
        ]), LogEventLevel.Warning));

        Assert.IsTrue(priority.WaitForCount(1));
        Assert.IsEmpty(general.Events);
    }

    [TestMethod]
    public void Typed_http_best_effort_error_uses_general_lane()
    {
        using var monitors = new FullNetLoggingMonitors();
        var general = new CollectingSink();
        var priority = new CollectingSink();
        var ingress = new HttpOperationLogIngress();
        using var pipeline = new FullNetLoggingPipelineSink(
            general,
            priority,
            new LoggingOptions(),
            monitors,
            resource: LoggingResourceMetadata.Create("Full.NET.Host.Api", "Test"),
            httpOperationIngress: ingress);

        Assert.AreEqual(true, ingress.Emit(new HttpOperationLogRecord([
            new("log.class", LogClassification.HttpOperation),
            new("reliability.class", "BestEffort"),
            new("http.route", "/api/orders/{id}"),
        ]), LogEventLevel.Error));

        Assert.IsTrue(general.WaitForCount(1));
        Assert.IsEmpty(priority.Events);
        Assert.AreEqual(LogEventLevel.Error, general.Events.Single().Level);
    }

    [TestMethod]
    public async Task Saturated_general_lane_does_not_hold_typed_slow_request()
    {
        using var monitors = new FullNetLoggingMonitors();
        using var general = new BlockingSink();
        var priority = new CollectingSink();
        var ingress = new HttpOperationLogIngress();
        var configuration = new LoggerConfiguration();
        FullNetLoggingPipeline.Configure(
            configuration,
            "Full.NET.Host.Api",
            new LoggingOptions
            {
                AsyncBufferSize = 1,
                HighPriorityAsyncBufferSize = 4,
            },
            monitors,
            sink => sink.Sink(general),
            sink => sink.Sink(priority),
            resource: LoggingResourceMetadata.Create("Full.NET.Host.Api", "Test"),
            httpOperationIngress: ingress);

        using var serilog = configuration.CreateLogger();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(serilog));
        try
        {
            serilog.Information("occupy general consumer");
            Assert.IsTrue(general.WaitUntilEntered());
            for (var index = 0; index < 64; index++)
            {
                serilog.Information("fill general {Index}", index);
            }

            Assert.IsTrue(SpinWait.SpinUntil(
                () => monitors.General.Snapshot.DroppedMessagesCount > 0,
                TimeSpan.FromSeconds(2)));
            var options = new FixedHttpOptionsMonitor(new HttpOperationLogOptions
            {
                SlowRequestThreshold = TimeSpan.Zero,
            });
            var middleware = new HttpOperationLogMiddleware(
                _ => Task.CompletedTask,
                options,
                new HttpOperationLogEmitter(options, new DefaultDiagnosticPolicyStore()),
                loggerFactory.CreateLogger<HttpOperationLogMiddleware>(),
                ingress);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/orders/123";
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/orders/{id}"),
                0,
                EndpointMetadataCollection.Empty,
                "order"));

            var stopwatch = Stopwatch.StartNew();
            await middleware.InvokeAsync(context);
            stopwatch.Stop();

            Assert.IsLessThan(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
            Assert.IsTrue(priority.WaitForCount(1));
            Assert.AreEqual("Priority",
                ((ScalarValue)priority.Events.Single()
                    .Properties["reliability.class"]).Value);
        }
        finally
        {
            general.Release();
        }
    }

    [TestMethod]
    public void Typed_http_ingress_reports_rejection_when_priority_queue_is_full()
    {
        using var monitors = new FullNetLoggingMonitors();
        using var priority = new BlockingSink();
        var ingress = new HttpOperationLogIngress();
        using var pipeline = new FullNetLoggingPipelineSink(
            new CollectingSink(),
            priority,
            new LoggingOptions { HighPriorityAsyncBufferSize = 1 },
            monitors,
            resource: LoggingResourceMetadata.Create("Full.NET.Host.Api", "Test"),
            httpOperationIngress: ingress);
        var record = new HttpOperationLogRecord([
            new("log.class", LogClassification.HttpOperation),
            new("reliability.class", "Priority"),
            new("http.route", "/api/orders/{id}"),
        ]);

        try
        {
            Assert.AreEqual(true, ingress.Emit(record, LogEventLevel.Warning));
            Assert.IsTrue(priority.WaitUntilEntered());
            Assert.AreEqual(true, ingress.Emit(record, LogEventLevel.Warning));

            Assert.AreEqual(false, ingress.Emit(record, LogEventLevel.Warning));
            Assert.AreEqual(1L, monitors.HighPriority.Snapshot.DroppedMessagesCount);
        }
        finally
        {
            priority.Release();
        }
    }

    [TestMethod]
    public void Exact_max_event_bytes_are_queued_without_zero_byte_release()
    {
        const int maxEventBytes = 512;
        var parser = new MessageTemplateParser();
        LogEvent? exact = null;
        for (var length = 0; length <= maxEventBytes; length++)
        {
            var candidate = new LogEvent(
                DateTimeOffset.UtcNow,
                LogEventLevel.Information,
                null,
                parser.Parse("payload {Value}"),
                [new LogEventProperty("Value", new ScalarValue(new string('x', length)))]);
            if (LogEnvelopeBuilder.TryBuild(candidate, maxEventBytes, out var envelope)
                && envelope!.Utf8Json.Length == maxEventBytes)
            {
                exact = candidate;
                break;
            }
        }

        Assert.IsNotNull(exact);
        using var monitor = new FullNetAsyncLogMonitor();
        var captured = new ConcurrentBag<byte[]>();
        var sink = new FullNetBoundedAsyncSink(
            new CollectingSink(),
            bufferSize: 1,
            workerName: "test exact event budget",
            monitor,
            maxEventBytes,
            queueMaxBytes: LogEnvelope.MaxChargeBytes(maxEventBytes, false),
            emitSnapshot: envelope => captured.Add(envelope.Utf8Json.ToArray()),
            emitLegacySink: false);
        try
        {
            sink.Emit(exact);
            sink.Complete();
            Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
            Assert.HasCount(1, captured);
            Assert.AreEqual(0L, sink.ReservedBytes);
        }
        finally
        {
            sink.Complete();
            sink.WaitForCompletion(TimeSpan.FromSeconds(2));
            sink.AbandonPending();
        }
    }

    [TestMethod]
    public async Task Middleware_and_forged_logger_event_keep_distinct_snapshot_bytes()
    {
        using var monitors = new FullNetLoggingMonitors();
        var ingress = new HttpOperationLogIngress();
        var snapshots = new ConcurrentBag<byte[]>();
        var legacy = new CollectingSink();
        var priorityLegacy = new CollectingSink();
        var configuration = new LoggerConfiguration();
        FullNetLoggingPipeline.Configure(
            configuration,
            "Full.NET.Host.Api",
            new LoggingOptions(),
            monitors,
            sink => sink.Sink(legacy),
            sink => sink.Sink(priorityLegacy),
            resource: LoggingResourceMetadata.Create("Full.NET.Host.Api", "Test"),
            emitSnapshot: envelope => snapshots.Add(envelope.Utf8Json.ToArray()),
            emitLegacySink: true,
            httpOperationIngress: ingress);

        using (var serilog = configuration.CreateLogger())
        using (var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(serilog)))
        {
            var logger = loggerFactory.CreateLogger<HttpOperationLogMiddleware>();
            var options = new FixedHttpOptionsMonitor(new HttpOperationLogOptions
            {
                SuccessSampleRate = 1,
                SlowRequestThreshold = TimeSpan.Zero,
            });
            var middleware = new HttpOperationLogMiddleware(
                _ => Task.CompletedTask,
                options,
                new HttpOperationLogEmitter(options, new DefaultDiagnosticPolicyStore()),
                logger,
                ingress);
            var context = new DefaultHttpContext();
            context.Request.Path = "/api/orders/private-123";
            context.Request.Method = "GET";
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/orders/{id}"),
                0,
                EndpointMetadataCollection.Empty,
                "order"));

            await middleware.InvokeAsync(context);
            using (logger.BeginScope(new Dictionary<string, object?>
            {
                ["log.class"] = LogClassification.HttpOperation,
                ["http.route"] = "/api/forged/private-456",
                ["RequestPayload"] = "{\"page\":1,\"pageSize\":20}",
            }))
            {
                logger.LogInformation("forged HTTP operation");
            }
        }

        Assert.HasCount(2, snapshots);
        Assert.HasCount(1, legacy.Events);
        Assert.HasCount(1, priorityLegacy.Events);
        Assert.AreEqual(LogEventLevel.Warning, priorityLegacy.Events.Single().Level);
        Assert.AreEqual("Priority",
            ((ScalarValue)priorityLegacy.Events.Single()
                .Properties["reliability.class"]).Value);
        var documents = snapshots.Select(bytes => JsonDocument.Parse(bytes)).ToArray();
        try
        {
            var operation = documents.Single(document => document.RootElement
                .GetProperty("log.class").GetString() == LogClassification.HttpOperation);
            var forged = documents.Single(document => document.RootElement
                .GetProperty("log.class").GetString() == LogClassification.Diagnostic);
            Assert.AreEqual("/api/orders/{id}",
                operation.RootElement.GetProperty("http.route").GetString());
            Assert.IsFalse(forged.RootElement.TryGetProperty("http.route", out _));
            Assert.IsFalse(forged.RootElement.TryGetProperty("RequestPayload", out _));
            foreach (var bytes in snapshots)
            {
                Assert.IsFalse(bytes.AsSpan().IndexOf("private-123"u8) >= 0);
                Assert.IsFalse(bytes.AsSpan().IndexOf("private-456"u8) >= 0);
            }
            foreach (var legacyEvent in legacy.Events.Concat(priorityLegacy.Events))
            {
                using var writer = new StringWriter();
                new Serilog.Formatting.Compact.CompactJsonFormatter()
                    .Format(legacyEvent, writer);
                var json = writer.ToString();
                Assert.IsFalse(json.Contains("private-123", StringComparison.Ordinal));
                Assert.IsFalse(json.Contains("private-456", StringComparison.Ordinal));
                Assert.IsFalse(json.Contains("\"RequestPayload\"", StringComparison.Ordinal));
            }
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    [TestMethod]
    public void Event_construction_bounds_string_and_destructured_collection_before_queueing()
    {
        using var monitors = new FullNetLoggingMonitors();
        var sink = new CollectingSink();
        using var logger = CreateLogger(
            monitors,
            sink,
            new CollectingSink(),
            generalBufferSize: 4,
            highPriorityBufferSize: 4);

        logger.Information(
            "bounded {Text} {@Items}",
            new string('x', 4096),
            Enumerable.Range(0, 64).ToArray());
        logger.Information(
            "surrogate {Text}",
            new string('x', 2047) + "😀tail");

        Assert.IsTrue(sink.WaitForCount(2));
        var logEvent = sink.Events.Single(logEvent =>
            logEvent.Properties.ContainsKey("Items"));
        var text = (string)((ScalarValue)logEvent.Properties["Text"]).Value!;
        var items = (SequenceValue)logEvent.Properties["Items"];
        Assert.IsLessThanOrEqualTo(2048, text.Length);
        Assert.IsLessThanOrEqualTo(16, items.Elements.Count);
        var surrogate = sink.Events.Single(logEvent =>
            !logEvent.Properties.ContainsKey("Items"));
        var safeText = (string)((ScalarValue)surrogate.Properties["Text"]).Value!;
        Assert.AreEqual(2047, safeText.Length);
        Assert.IsFalse(char.IsHighSurrogate(safeText[^1]));
    }

    [TestMethod]
    public void High_priority_queue_saturation_never_blocks_callers()
    {
        using var monitors = new FullNetLoggingMonitors();
        var generalSink = new CollectingSink();
        using var highPrioritySink = new BlockingSink();
        using var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 1);

        logger.Error("block high priority channel");
        Assert.IsTrue(highPrioritySink.WaitUntilEntered());
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < 256; index++)
        {
            logger.Error("high priority event {Index}", index);
        }

        stopwatch.Stop();
        Assert.IsLessThan(TimeSpan.FromSeconds(2), stopwatch.Elapsed);
        Assert.IsTrue(SpinWait.SpinUntil(
            () => monitors.HighPriority.Snapshot.DroppedMessagesCount > 0,
            TimeSpan.FromSeconds(2)));
        highPrioritySink.Release();
    }

    [TestMethod]
    public void General_worker_continues_after_one_sink_failure()
    {
        using var monitors = new FullNetLoggingMonitors();
        var delivered = new CollectingSink();
        var generalSink = new ThrowOnceSink(delivered);
        using var logger = CreateLogger(
            monitors,
            generalSink,
            new CollectingSink(),
            generalBufferSize: 4,
            highPriorityBufferSize: 4);

        logger.Information("discard first general event");
        Assert.IsTrue(generalSink.WaitUntilAttempted());
        logger.Information("deliver second general event");

        Assert.IsTrue(delivered.WaitForCount(1));
        Assert.AreEqual(
            "deliver second general event",
            delivered.Events.Single().RenderMessage());
        Assert.AreEqual(1, monitors.General.Snapshot.DroppedMessagesCount);
    }

    [TestMethod]
    public void High_priority_worker_continues_after_one_sink_failure()
    {
        using var monitors = new FullNetLoggingMonitors();
        var delivered = new CollectingSink();
        var highPrioritySink = new ThrowOnceSink(delivered);
        using var logger = CreateLogger(
            monitors,
            new CollectingSink(),
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 4);

        logger.Error("discard first high priority event");
        Assert.IsTrue(highPrioritySink.WaitUntilAttempted());
        logger.Error("deliver second high priority event");

        Assert.IsTrue(delivered.WaitForCount(1));
        Assert.AreEqual(
            "deliver second high priority event",
            delivered.Events.Single().RenderMessage());
        Assert.AreEqual(1, monitors.HighPriority.Snapshot.DroppedMessagesCount);
    }

    [TestMethod]
    public void Metrics_use_only_bounded_channel_tags()
    {
        using var monitors = new FullNetLoggingMonitors();
        var channels = new ConcurrentBag<string>();
        var instruments = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == FullNetAsyncLogMonitor.MeterName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) =>
            {
                instruments.Add(instrument.Name);
                var channel = tags.ToArray()
                    .Single(tag => tag.Key == "channel")
                    .Value?.ToString();
                if (channel is not null)
                {
                    channels.Add(channel);
                }
            });
        monitors.General.StartMonitoring(new FakeInspector(2, 10, 1));
        monitors.HighPriority.StartMonitoring(new FakeInspector(1, 5, 0));

        listener.Start();
        listener.RecordObservableInstruments();

        CollectionAssert.AreEquivalent(
            new[] { "general", "high_priority" },
            channels.Distinct(StringComparer.Ordinal).ToArray());
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "fullnet.logging.queue.bytes",
                "fullnet.logging.queue.bytes.capacity",
                "fullnet.logging.events.oversize",
            },
            instruments.Distinct(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task Health_degrades_only_for_high_priority_near_capacity()
    {
        using var monitors = new FullNetLoggingMonitors();
        var healthCheck = new HighPriorityLoggingHealthCheck(monitors);
        monitors.General.StartMonitoring(new FakeInspector(10, 10, 5));
        monitors.HighPriority.StartMonitoring(new FakeInspector(1, 10, 0));

        var healthy = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Healthy, healthy.Status);

        monitors.HighPriority.StartMonitoring(new FakeInspector(9, 10, 0));
        var degraded = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.AreEqual(HealthStatus.Degraded, degraded.Status);

        monitors.HighPriority.StartMonitoring(new FakeInspector(
            1,
            10,
            0,
            ReservedBytes: 9,
            ByteCapacity: 10));
        var byteDegraded = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        Assert.AreEqual(HealthStatus.Degraded, byteDegraded.Status);
    }

    [TestMethod]
    public void Service_defaults_reject_non_positive_high_priority_capacity()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:HighPriorityAsyncBufferSize"] = "0",
            });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => builder.AddFullNetServiceDefaults());
    }

    [TestMethod]
    public void Service_defaults_reject_event_or_byte_budget_that_cannot_hold_one_event()
    {
        foreach (var configuration in new[]
        {
            new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:MaxEventBytes"] = "0",
            },
            new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:MaxEventBytes"] = "16384",
                [$"{LoggingOptions.SectionName}:GeneralQueueMaxBytes"] = "16384",
            },
            new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:HighPriorityQueueMaxBytes"] = "1",
            },
        })
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(configuration);

            Assert.ThrowsExactly<OptionsValidationException>(
                () => builder.AddFullNetServiceDefaults());
        }
    }

    [TestMethod]
    public void Service_defaults_reject_legacy_es_budget_that_cannot_hold_safe_event_copy()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{ElasticsearchLoggingOptions.SectionName}:Enabled"] = "true",
                [$"{ElasticsearchLoggingOptions.SectionName}:NodeUris:0"] = "http://localhost:9200",
                [$"{LoggingOptions.SectionName}:HighPriorityQueueMaxBytes"] = "16512",
            });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => builder.AddFullNetServiceDefaults());
    }

    [TestMethod]
    public void Explicit_delivery_mode_rejects_legacy_elasticsearch_sink()
    {
        foreach (var mode in new[] { "Local", "Collector", "ApplicationKafka" })
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:DeliveryMode"] = mode,
                [$"{ElasticsearchLoggingOptions.SectionName}:Enabled"] = "true",
                [$"{ElasticsearchLoggingOptions.SectionName}:NodeUris:0"] = "http://localhost:9200",
            });

            var error = Assert.ThrowsExactly<OptionsValidationException>(
                () => builder.AddFullNetServiceDefaults());
            StringAssert.Contains(error.Message, "DeliveryMode");
        }
    }

    [TestMethod]
    public void Application_kafka_mode_rejects_missing_adapter_without_falling_back_to_console()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{LoggingOptions.SectionName}:DeliveryMode"] = "ApplicationKafka",
            [$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] = "ApplicationKafka",
        });

        var error = Assert.ThrowsExactly<OptionsValidationException>(
            () => builder.AddFullNetServiceDefaults());
        StringAssert.Contains(error.Message, "adapter");
    }

    [TestMethod]
    public void Explicit_local_and_collector_modes_keep_legacy_sink_disabled()
    {
        foreach (var mode in new[] { "Local", "Collector" })
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:DeliveryMode"] = mode,
                [$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] = mode,
            });
            builder.AddFullNetServiceDefaults();
            using var host = builder.Build();

            Assert.IsFalse(host.Services
                .GetRequiredService<IElasticsearchLogPipelineStatus>().IsEnabled);
            Assert.IsFalse(host.Services
                .GetRequiredService<IElasticsearchLogPipelineStatus>().IsSinkRegistered);
        }
    }

    [TestMethod]
    public void Explicit_delivery_mode_requires_matching_deployment_expectation()
    {
        foreach (var expected in new string?[] { null, "Local" })
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LoggingOptions.SectionName}:DeliveryMode"] = "Collector",
                [$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] = expected,
            });

            var error = Assert.ThrowsExactly<OptionsValidationException>(
                () => builder.AddFullNetServiceDefaults());
            StringAssert.Contains(error.Message, "ExpectedDeliveryMode");
        }
    }

    [TestMethod]
    public void Deployment_expectation_without_explicit_mode_is_rejected()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] = "Collector",
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => builder.AddFullNetServiceDefaults());
    }

    [TestMethod]
    public void Legacy_elasticsearch_mode_registers_one_migration_warning()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ElasticsearchLoggingOptions.SectionName}:Enabled"] = "true",
            [$"{ElasticsearchLoggingOptions.SectionName}:NodeUris:0"] = "http://localhost:9200",
        });
        builder.AddFullNetServiceDefaults();
        using var host = builder.Build();

        Assert.AreEqual(1, host.Services.GetServices<IHostedService>()
            .Count(service => service is LegacyElasticsearchLoggingWarningService));
    }

    [TestMethod]
    public void Unsupported_numeric_delivery_mode_is_rejected()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{LoggingOptions.SectionName}:DeliveryMode"] = "999",
        });

        Assert.ThrowsExactly<OptionsValidationException>(
            () => builder.AddFullNetServiceDefaults());
    }

    [TestMethod]
    public void Late_delivery_configuration_change_is_rejected_before_logger_construction()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddFullNetServiceDefaults();
        builder.Configuration[$"{ElasticsearchLoggingOptions.SectionName}:Enabled"] = "true";
        builder.Configuration[$"{ElasticsearchLoggingOptions.SectionName}:NodeUris:0"] = "http://localhost:9200";

        Assert.ThrowsExactly<OptionsValidationException>(() => builder.Build());
    }

    [TestMethod]
    public void Service_defaults_register_dual_logging_monitors_and_ready_check()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.AddFullNetServiceDefaults();
        using var host = builder.Build();

        Assert.IsNotNull(host.Services.GetService<FullNetLoggingMonitors>());
        Assert.IsNotNull(host.Services.GetService<LoggingResourceMetadata>());
        Assert.IsNotNull(host.Services.GetService<HttpOperationLogIngress>());
        Assert.IsTrue(host.Services.GetServices<IHostedService>()
            .Any(service => service is LoggingResourceAnnouncementService));
        var healthOptions = host.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value;
        var registration = healthOptions.Registrations.Single(
            candidate => candidate.Name == "high_priority_logging");
        CollectionAssert.Contains(registration.Tags.ToArray(), "ready");
    }

    [TestMethod]
    public void Di_registered_sink_cannot_bypass_host_logging_pipeline()
    {
        var directSink = new CollectingSink();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILogEventSink>(directSink);
        builder.AddFullNetServiceDefaults();
        using var host = builder.Build();

        host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("single-ingress-check")
            .LogInformation("single ingress check");

        Assert.IsEmpty(directSink.Events);
    }

    [TestMethod]
    public void Logger_disposal_uses_one_total_timeout_for_both_blocked_channels()
    {
        using var monitors = new FullNetLoggingMonitors();
        using var generalSink = new BlockingSink();
        using var highPrioritySink = new BlockingSink();
        var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 4,
            shutdownFlushTimeout: TimeSpan.FromMilliseconds(100));
        logger.Information("block general channel during shutdown");
        logger.Error("block high priority channel during shutdown");
        Assert.IsTrue(generalSink.WaitUntilEntered());
        Assert.IsTrue(highPrioritySink.WaitUntilEntered());

        var disposeTask = Task.Run(logger.Dispose);
        var completedWithinBudget = disposeTask.Wait(TimeSpan.FromSeconds(1));
        generalSink.Release();
        highPrioritySink.Release();
        var completedAfterRelease = disposeTask.Wait(TimeSpan.FromSeconds(2));

        Assert.IsTrue(completedAfterRelease);
        Assert.IsTrue(
            completedWithinBudget,
            "Logger disposal exceeded the shared shutdown flush budget.");
    }

    [TestMethod]
    public void Logger_disposal_drains_both_channels_before_timeout()
    {
        using var monitors = new FullNetLoggingMonitors();
        var generalSink = new CollectingSink();
        var highPrioritySink = new CollectingSink();
        var logger = CreateLogger(
            monitors,
            generalSink,
            highPrioritySink,
            generalBufferSize: 4,
            highPriorityBufferSize: 4,
            shutdownFlushTimeout: TimeSpan.FromSeconds(1));
        logger.Information("drain general channel");
        logger.Error("drain high priority channel");

        logger.Dispose();

        Assert.AreEqual(
            "drain general channel",
            generalSink.Events.Single().RenderMessage());
        Assert.AreEqual(
            "drain high priority channel",
            highPrioritySink.Events.Single().RenderMessage());
    }

    [TestMethod]
    public void Worker_exit_survives_legacy_sink_disposal_failure()
    {
        using var monitor = new FullNetAsyncLogMonitor();
        var sink = new FullNetBoundedAsyncSink(
            new ThrowOnDisposeSink(),
            bufferSize: 1,
            workerName: "test throwing sink disposal",
            monitor,
            maxEventBytes: 1024,
            queueMaxBytes: LogEnvelope.MaxChargeBytes(1024, true));

        sink.Complete();

        Assert.IsTrue(sink.WaitForCompletion(TimeSpan.FromSeconds(2)));
        sink.AbandonPending();
        Assert.AreEqual(0L, sink.ReservedBytes);
    }

    [TestMethod]
    public void Service_defaults_reject_out_of_range_shutdown_flush_timeout()
    {
        foreach (var invalidValue in new[] { "00:00:00", "00:00:31" })
        {
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"{LoggingOptions.SectionName}:ShutdownFlushTimeout"] = invalidValue,
                });

            var exception = Assert.ThrowsExactly<OptionsValidationException>(
                () => builder.AddFullNetServiceDefaults());
            StringAssert.Contains(
                exception.Message,
                nameof(LoggingOptions.ShutdownFlushTimeout));
        }
    }

    private static Serilog.Core.Logger CreateLogger(
        FullNetLoggingMonitors monitors,
        ILogEventSink generalSink,
        ILogEventSink highPrioritySink,
        int generalBufferSize,
        int highPriorityBufferSize,
        TimeSpan? shutdownFlushTimeout = null,
        LoggingResourceMetadata? resource = null)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose();
        FullNetLoggingPipeline.Configure(
            configuration,
            "Full.NET.UnitTests",
            new LoggingOptions
            {
                AsyncBufferSize = generalBufferSize,
                HighPriorityAsyncBufferSize = highPriorityBufferSize,
                ShutdownFlushTimeout =
                    shutdownFlushTimeout ?? TimeSpan.FromSeconds(5),
            },
            monitors,
            sink => sink.Sink(generalSink),
            sink => sink.Sink(highPrioritySink),
            resource: resource);
        return configuration.CreateLogger();
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ManualResetEventSlim _received = new();

        public ConcurrentBag<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
            _received.Set();
        }

        public bool WaitForCount(int count) =>
            SpinWait.SpinUntil(
                () => Events.Count >= count,
                TimeSpan.FromSeconds(2));
    }

    private sealed class BlockingSink : ILogEventSink, IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();

        public void Emit(LogEvent logEvent)
        {
            _entered.Set();
            _release.Wait(TimeSpan.FromSeconds(10));
        }

        public bool WaitUntilEntered() => _entered.Wait(TimeSpan.FromSeconds(2));

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _entered.Dispose();
            _release.Dispose();
        }
    }

    private sealed class ThrowOnceSink(ILogEventSink next) :
        ILogEventSink,
        IDisposable
    {
        private readonly ManualResetEventSlim _attempted = new();
        private int _attempts;

        public void Emit(LogEvent logEvent)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                _attempted.Set();
                throw new InvalidOperationException("simulated sink failure");
            }

            next.Emit(logEvent);
        }

        public bool WaitUntilAttempted() =>
            _attempted.Wait(TimeSpan.FromSeconds(2));

        public void Dispose() => _attempted.Dispose();
    }

    private sealed class FixedHttpOptionsMonitor(HttpOperationLogOptions value)
        : IOptionsMonitor<HttpOperationLogOptions>
    {
        public HttpOperationLogOptions CurrentValue => value;

        public HttpOperationLogOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<HttpOperationLogOptions, string?> listener) => null;
    }

    private sealed class ThrowOnDisposeSink : ILogEventSink, IDisposable
    {
        public void Emit(LogEvent logEvent) { }

        public void Dispose() => throw new InvalidOperationException("simulated disposal failure");
    }

    private sealed record FakeInspector(
        int Count,
        int BufferSize,
        long DroppedMessagesCount,
        long ReservedBytes = 0,
        long ByteCapacity = 0) : IAsyncLogEventSinkInspector, ILogByteBudgetInspector
    {
        public long OversizeCount => 0;

        public long ByteBudgetDroppedCount => 0;
    }
}
