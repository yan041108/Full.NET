using Full.NET.Hosting.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class HttpOperationLogTests
{
    [TestMethod]
    public void Middleware_activation_requires_typed_ingress_registration()
    {
        var options = new StaticOptionsMonitor(new HttpOperationLogOptions());
        var services = new ServiceCollection()
            .AddSingleton<IOptionsMonitor<HttpOperationLogOptions>>(options)
            .AddSingleton(new HttpOperationLogEmitter(options, new DefaultDiagnosticPolicyStore()))
            .AddSingleton<ILogger<HttpOperationLogMiddleware>>(new ListLogger());
        using var provider = services.BuildServiceProvider();

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ActivatorUtilities.CreateInstance<HttpOperationLogMiddleware>(
                provider,
                new RequestDelegate(_ => Task.CompletedTask)));
    }

    [TestMethod]
    public void Profile_maps_10k_to_xl()
    {
        Assert.AreEqual(
            LoggingCapacityProfile.XL,
            HttpOperationLogProfile.MapConcurrentInFlight(10_000));
        Assert.AreEqual(
            LoggingCapacityProfile.L,
            HttpOperationLogProfile.MapConcurrentInFlight(9_999));
    }

    [TestMethod]
    public void Sanitizer_redacts_sensitive_query_and_strips_crlf()
    {
        var sanitized = HttpOperationLogSanitizer.SanitizeUrl(
            "/api/v1/orders?token=abc\r\nInjected&id=1");
        StringAssert.Contains(sanitized, "token=" + HttpOperationLogSanitizer.Redacted);
        StringAssert.Contains(sanitized, "id=1");
        Assert.IsFalse(sanitized.Contains('\r'));
        Assert.IsFalse(sanitized.Contains('\n'));
    }

    [TestMethod]
    public void Sanitizer_projects_whitelist_and_redacts_password()
    {
        var json = """{"id":"1","password":"secret","status":"ok","nested":{"x":1}}""";
        var projected = HttpOperationLogSanitizer.ProjectJsonPayload(
            json,
            ["id", "password", "status"],
            maxBytes: 2048);
        Assert.IsNotNull(projected);
        StringAssert.Contains(projected, "\"id\":\"1\"");
        StringAssert.Contains(projected, HttpOperationLogSanitizer.Redacted);
        Assert.IsFalse(projected.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Oversize_projection_does_not_return_broken_json()
    {
        var projected = HttpOperationLogSanitizer.ProjectJsonPayload(
            """{"id":"你好世界"}""",
            ["id"],
            maxBytes: 15);

        if (projected is not null)
        {
            using var _ = JsonDocument.Parse(projected);
        }

        Assert.IsNull(projected);
    }

    [TestMethod]
    public void Legacy_capture_does_not_retain_oversize_request_json()
    {
        var context = new DefaultHttpContext();

        HttpOperationLogPayloadCapture.CaptureRequestJson(
            context,
            "{\"id\":\"" + new string('x', 65_536) + "\"}");

        Assert.IsFalse(context.Items.ContainsKey(HttpOperationLogPayloadCapture.ItemKey));
    }

    [TestMethod]
    public async Task Legacy_raw_json_cannot_reach_b2_even_on_allowlisted_route()
    {
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.SanitizedPayload,
            SuccessSampleRate = 1,
            PayloadRouteAllowList = ["/api/v1/items"],
        };
        var monitor = new StaticOptionsMonitor(options);
        var collector = new ListLogger();
        var middleware = CreateMiddleware(
            context =>
            {
                HttpOperationLogPayloadCapture.CaptureRequestJson(
                    context,
                    "{\"id\":\"Bearer token-secret\"}");
                return Task.CompletedTask;
            },
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            collector);
        var context = NewItemsRouteContext();

        await middleware.InvokeAsync(context);

        Assert.HasCount(1, collector.Scopes);
        Assert.IsNull(collector.Scopes[0]["RequestPayload"]);
        Assert.IsFalse(context.Items.ContainsKey(HttpOperationLogPayloadCapture.ItemKey));
    }

    [TestMethod]
    public async Task Middleware_uses_typed_ingress_without_publishing_http_fields_to_logger_scope()
    {
        var options = new HttpOperationLogOptions { SuccessSampleRate = 1 };
        var monitor = new StaticOptionsMonitor(options);
        var collector = new ListLogger();
        var ingress = new HttpOperationLogIngress();
        HttpOperationLogRecord? captured = null;
        ingress.Attach((record, _) =>
        {
            captured = record;
            return true;
        });
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            collector,
            ingress);
        var context = NewItemsRouteContext();
        context.Request.Path = "/api/v1/items/private-123";

        await middleware.InvokeAsync(context);

        Assert.IsNotNull(captured);
        Assert.IsEmpty(collector.Scopes);
        Assert.AreEqual("http.operation",
            captured.Fields.Single(field => field.Key == "log.class").Value);
        Assert.IsFalse(captured.Fields.Any(field =>
            field.Value is string value && value.Contains("private-123", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Middleware_reads_only_admitted_request_and_response_projections()
    {
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.SanitizedPayload,
            SuccessSampleRate = 1,
            PayloadRouteAllowList = ["/api/v1/items"],
            MaxRequestPayloadBytes = 256,
            MaxResponsePayloadBytes = 256,
        };
        var monitor = new StaticOptionsMonitor(options);
        var emitter = new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore());
        var projection = new HttpOperationPayloadProjection(
            monitor,
            new HttpLogCaptureBudget(TimeProvider.System),
            emitter,
            new ListLogger());
        var collector = new ListLogger();
        var middleware = CreateMiddleware(
            context =>
            {
                context.SetEndpoint(new RouteEndpoint(
                    _ => Task.CompletedTask,
                    RoutePatternFactory.Parse("/api/v1/items"),
                    0,
                    EndpointMetadataCollection.Empty,
                    "items"));
                Assert.IsTrue(projection.TryBeginCapture(
                    context,
                    HttpLogCaptureTarget.B2InternalSummary,
                    HttpLogCaptureProjectionKeys.Pagination,
                    out var lease,
                    out _));
                using (lease)
                {
                    var result = lease!.Capture(
                        () => new HttpPaginationLogProjection(2, 20),
                        HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
                    Assert.AreEqual(HttpLogCaptureState.Captured, result.State);
                }

                Assert.IsTrue(projection.TryBeginCapture(
                    context,
                    HttpLogCaptureTarget.B2InternalSummary,
                    HttpLogCaptureProjectionKeys.PaginationResult,
                    out var responseLease));
                using (responseLease)
                {
                    var responseResult = responseLease!.Capture(
                        () => new HttpPaginationResultLogProjection(2, 20, 42, 20),
                        HttpLogCaptureJsonContext.Default.HttpPaginationResultLogProjection);
                    Assert.AreEqual(HttpLogCaptureState.Captured, responseResult.State);
                }

                return Task.CompletedTask;
            },
            monitor,
            emitter,
            collector);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/items";

        await middleware.InvokeAsync(context);

        Assert.HasCount(1, collector.Scopes);
        Assert.AreEqual(
            "{\"page\":2,\"pageSize\":20}",
            collector.Scopes[0]["RequestPayload"]);
        Assert.AreEqual(
            "{\"page\":2,\"pageSize\":20,\"totalCount\":42,\"itemCount\":20}",
            collector.Scopes[0]["ResponsePayload"]);
    }

    [TestMethod]
    public async Task Fully_allowlisted_long_route_emits_admitted_projection()
    {
        var route = "/api/" + new string('a', 260) + "/approved";
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.SanitizedPayload,
            SuccessSampleRate = 1,
            PayloadRouteAllowList = [route],
            MaxRequestPayloadBytes = 256,
        };
        var monitor = new StaticOptionsMonitor(options);
        var emitter = new HttpOperationLogEmitter(
            monitor, new DefaultDiagnosticPolicyStore());
        var collector = new ListLogger();
        var projection = new HttpOperationPayloadProjection(
            monitor, new HttpLogCaptureBudget(TimeProvider.System), emitter, collector);
        var middleware = CreateMiddleware(
            context =>
            {
                context.SetEndpoint(new RouteEndpoint(
                    _ => Task.CompletedTask,
                    RoutePatternFactory.Parse(route),
                    0,
                    EndpointMetadataCollection.Empty,
                    "long-route"));
                Assert.IsTrue(projection.TryBeginCapture(
                    context, HttpLogCaptureTarget.B2InternalSummary,
                    HttpLogCaptureProjectionKeys.Pagination, out var lease));
                using (lease)
                {
                    Assert.AreEqual(HttpLogCaptureState.Captured,
                        lease!.Capture(
                            () => new HttpPaginationLogProjection(1, 20),
                            HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection).State);
                }

                return Task.CompletedTask;
            },
            monitor, emitter, collector);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = route;

        await middleware.InvokeAsync(httpContext);

        Assert.HasCount(1, collector.Scopes);
        Assert.AreEqual("{\"page\":1,\"pageSize\":20}",
            collector.Scopes[0]["RequestPayload"]);
    }

    [TestMethod]
    public async Task Active_request_keeps_sampling_decision_after_options_change()
    {
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.SanitizedPayload,
            SuccessSampleRate = 1,
            PayloadRouteAllowList = ["/api/v1/items"],
            MaxRequestPayloadBytes = 256,
        };
        var monitor = new StaticOptionsMonitor(options);
        var emitter = new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore());
        var projection = new HttpOperationPayloadProjection(
            monitor,
            new HttpLogCaptureBudget(TimeProvider.System),
            emitter,
            new ListLogger());
        var collector = new ListLogger();
        var middleware = CreateMiddleware(
            context =>
            {
                context.SetEndpoint(NewItemsRouteContext().GetEndpoint());
                Assert.IsTrue(projection.TryBeginCapture(
                    context, HttpLogCaptureTarget.B2InternalSummary,
                    HttpLogCaptureProjectionKeys.Pagination, out var lease));
                using (lease)
                {
                    lease!.Capture(
                        () => new HttpPaginationLogProjection(2, 20),
                        HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
                }

                options.SuccessSampleRate = 0;
                return Task.CompletedTask;
            },
            monitor,
            emitter,
            collector);

        await middleware.InvokeAsync(NewItemsRouteContext());

        Assert.HasCount(1, collector.Scopes);
        Assert.AreEqual(
            "{\"page\":2,\"pageSize\":20}",
            collector.Scopes[0]["RequestPayload"]);
    }

    [TestMethod]
    public async Task Rejected_b2_capture_cannot_fall_back_to_legacy_json()
    {
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.SanitizedPayload,
            SuccessSampleRate = 1,
            PayloadRouteAllowList = ["/api/v1/items"],
            MaxRequestPayloadBytes = 256,
            CaptureMaxEventsPerSecond = 1,
            CaptureMaxBytesPerSecond = 256,
        };
        var monitor = new StaticOptionsMonitor(options);
        var emitter = new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore());
        var projection = new HttpOperationPayloadProjection(
            monitor,
            new HttpLogCaptureBudget(TimeProvider.System),
            emitter,
            new ListLogger());
        var heldContext = NewItemsRouteContext();
        Assert.IsTrue(projection.TryBeginCapture(
            heldContext, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var heldLease, out _));
        using (heldLease)
        {
            var collector = new ListLogger();
            var middleware = CreateMiddleware(
                context =>
                {
                    context.SetEndpoint(NewItemsRouteContext().GetEndpoint());
                    HttpOperationLogPayloadCapture.CaptureRequestJson(
                        context,
                        """{"id":"legacy-secret"}""");
                    Assert.IsFalse(projection.TryBeginCapture(
                        context, HttpLogCaptureTarget.B2InternalSummary,
                        HttpLogCaptureProjectionKeys.Pagination, out var rejectedLease, out var state));
                    Assert.IsNull(rejectedLease);
                    Assert.AreEqual(HttpLogCaptureState.BudgetExceeded, state);
                    return Task.CompletedTask;
                },
                monitor,
                emitter,
                collector);
            var context = NewItemsRouteContext();

            await middleware.InvokeAsync(context);

            Assert.HasCount(1, collector.Scopes);
            Assert.IsNull(collector.Scopes[0]["RequestPayload"]);
        }
    }

    [TestMethod]
    public void Payload_limit_above_log_string_boundary_is_rejected_at_startup()
    {
        var result = new HttpOperationLogOptionsValidator().Validate(
            null,
            new HttpOperationLogOptions { MaxRequestPayloadBytes = 2_049 });

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public void Response_projection_limit_must_fit_shared_capture_byte_budget()
    {
        var result = new HttpOperationLogOptionsValidator().Validate(
            null,
            new HttpOperationLogOptions
            {
                MaxRequestPayloadBytes = 0,
                MaxResponsePayloadBytes = 257,
                CaptureMaxBytesPerSecond = 256,
            });

        Assert.IsFalse(result.Succeeded);
    }

    private static DefaultHttpContext NewItemsRouteContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/items";
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/items"),
            0,
            EndpointMetadataCollection.Empty,
            "items"));
        return context;
    }

    [TestMethod]
    public void Success_sampling_is_deterministic_for_route_and_trace()
    {
        var options = new HttpOperationLogOptions
        {
            SuccessSampleRate = 0.5,
        };
        var emitter = new HttpOperationLogEmitter(new StaticOptionsMonitor(options), new DefaultDiagnosticPolicyStore());
        var first = emitter.ShouldSampleSuccess("orders/{id}", "trace-a");
        var second = emitter.ShouldSampleSuccess("orders/{id}", "trace-a");
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void Active_diagnostic_rule_overrides_cached_success_sampling_for_its_endpoint()
    {
        var options = new HttpOperationLogOptions { SuccessSampleRate = 0 };
        var now = DateTimeOffset.UtcNow;
        var snapshot = new DiagnosticPolicySnapshot(
            1,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.Endpoint,
                    "/api/items",
                    SuccessSampleRateOverride: 1.0,
                    BestEffortCapacityOverride: null,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(10)),
            ],
            now,
            IsDefault: false);
        var emitter = new HttpOperationLogEmitter(
            new StaticOptionsMonitor(options),
            new StaticDiagnosticPolicyStore(snapshot));
        var matching = NewItemsRouteContext();
        var other = NewItemsRouteContext();
        other.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/other"),
            0,
            EndpointMetadataCollection.Empty,
            "other"));

        Assert.IsTrue(HttpOperationLogSampleKey.ShouldSampleSuccess(
            matching, "/api/items", emitter));
        Assert.IsFalse(HttpOperationLogSampleKey.ShouldSampleSuccess(
            other, "/api/other", emitter));
    }

    [TestMethod]
    public void Cached_request_sample_decision_is_rechecked_when_its_rule_expires()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new MutableTimeProvider(now);
        var snapshot = new DiagnosticPolicySnapshot(
            1,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.Endpoint,
                    "/api/v1/items",
                    SuccessSampleRateOverride: 1.0,
                    BestEffortCapacityOverride: null,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(1)),
            ],
            now,
            IsDefault: false);
        var emitter = new HttpOperationLogEmitter(
            new StaticOptionsMonitor(new HttpOperationLogOptions { SuccessSampleRate = 0 }),
            new StaticDiagnosticPolicyStore(snapshot),
            clock);
        var context = NewItemsRouteContext();

        Assert.IsTrue(HttpOperationLogSampleKey.ShouldSampleSuccess(
            context, "/api/v1/items", emitter));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.IsFalse(HttpOperationLogSampleKey.ShouldSampleSuccess(
            context, "/api/v1/items", emitter));
    }

    [TestMethod]
    public void Diagnostic_group_rule_matches_http_operation_group()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new DiagnosticPolicySnapshot(
            1,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.DiagnosticGroup,
                    HttpOperationLogMiddleware.DiagnosticGroup,
                    SuccessSampleRateOverride: 1.0,
                    BestEffortCapacityOverride: null,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(5)),
            ],
            now,
            IsDefault: false);
        var emitter = new HttpOperationLogEmitter(
            new StaticOptionsMonitor(new HttpOperationLogOptions { SuccessSampleRate = 0 }),
            new StaticDiagnosticPolicyStore(snapshot));

        Assert.IsTrue(HttpOperationLogSampleKey.ShouldSampleSuccess(
            NewItemsRouteContext(), "/api/v1/items", emitter));
    }

    [TestMethod]
    public void Tenant_rule_reads_trusted_context_not_request_header()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var snapshot = new DiagnosticPolicySnapshot(
            1,
            LoggingPressureState.Normal,
            [
                new DiagnosticPolicyRule(
                    DiagnosticPolicyScopeKind.Tenant,
                    tenantId.ToString(),
                    SuccessSampleRateOverride: 1.0,
                    BestEffortCapacityOverride: null,
                    MaxRequestPayloadBytesOverride: null,
                    MaxResponsePayloadBytesOverride: null,
                    ExpiresAtUtc: now.AddMinutes(5)),
            ],
            now,
            IsDefault: false);
        var emitter = new HttpOperationLogEmitter(
            new StaticOptionsMonitor(new HttpOperationLogOptions { SuccessSampleRate = 0 }),
            new StaticDiagnosticPolicyStore(snapshot));
        var untrusted = NewItemsRouteContext();
        untrusted.Request.Headers["X-Tenant-Id"] = tenantId.ToString();
        var trusted = NewItemsRouteContext();
        trusted.Items["FullNet.TenantId"] = tenantId;

        Assert.IsFalse(HttpOperationLogSampleKey.ShouldSampleSuccess(
            untrusted, "/api/v1/items", emitter));
        Assert.IsTrue(HttpOperationLogSampleKey.ShouldSampleSuccess(
            trusted, "/api/v1/items", emitter));
    }

    [TestMethod]
    public void Trace_rule_requires_actual_activity_trace_not_request_identifier()
    {
        var previousActivity = System.Diagnostics.Activity.Current;
        System.Diagnostics.Activity.Current = null;
        try
        {
            using var activity = new System.Diagnostics.Activity("diagnostic-scope-test")
                .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C)
                .Start();
            var traceId = activity.TraceId.ToString();
            var now = DateTimeOffset.UtcNow;
            var snapshot = new DiagnosticPolicySnapshot(
                1,
                LoggingPressureState.Normal,
                [
                    new DiagnosticPolicyRule(
                        DiagnosticPolicyScopeKind.Trace,
                        traceId,
                        SuccessSampleRateOverride: 1.0,
                        BestEffortCapacityOverride: null,
                        MaxRequestPayloadBytesOverride: null,
                        MaxResponsePayloadBytesOverride: null,
                        ExpiresAtUtc: now.AddMinutes(5)),
                ],
                now,
                IsDefault: false);
            var emitter = new HttpOperationLogEmitter(
                new StaticOptionsMonitor(new HttpOperationLogOptions { SuccessSampleRate = 0 }),
                new StaticDiagnosticPolicyStore(snapshot));
            var trusted = NewItemsRouteContext();
            var untrusted = NewItemsRouteContext();
            untrusted.TraceIdentifier = traceId;

            Assert.IsTrue(HttpOperationLogSampleKey.ShouldSampleSuccess(
                trusted, "/api/v1/items", emitter));
            activity.Stop();
            Assert.IsFalse(HttpOperationLogSampleKey.ShouldSampleSuccess(
                untrusted, "/api/v1/items", emitter));

            using var source = new System.Diagnostics.ActivitySource("fullnet.remote-trace-test");
            using var listener = new System.Diagnostics.ActivityListener
            {
                ShouldListenTo = candidate => candidate.Name == source.Name,
                Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                    System.Diagnostics.ActivitySamplingResult.AllData,
            };
            System.Diagnostics.ActivitySource.AddActivityListener(listener);
            var remoteContext = new System.Diagnostics.ActivityContext(
                System.Diagnostics.ActivityTraceId.CreateFromString(traceId.AsSpan()),
                System.Diagnostics.ActivitySpanId.CreateFromString("1111111111111111".AsSpan()),
                System.Diagnostics.ActivityTraceFlags.Recorded,
                isRemote: true);
            using var remote = source.StartActivity(
                "remote-trace-test",
                System.Diagnostics.ActivityKind.Server,
                remoteContext);
            Assert.IsNotNull(remote);
            Assert.IsTrue(remote.HasRemoteParent);
            Assert.IsFalse(HttpOperationLogSampleKey.ShouldSampleSuccess(
                NewItemsRouteContext(), "/api/v1/items", emitter));
            remote.Stop();

            using var legacyRemote = new System.Diagnostics.Activity("remote-fallback-test")
                .SetIdFormat(System.Diagnostics.ActivityIdFormat.W3C)
                .SetParentId($"00-{traceId}-2222222222222222-01")
                .Start();
            Assert.IsFalse(legacyRemote.HasRemoteParent);
            Assert.IsFalse(HttpOperationLogSampleKey.ShouldSampleSuccess(
                NewItemsRouteContext(), "/api/v1/items", emitter));
        }
        finally
        {
            System.Diagnostics.Activity.Current = previousActivity;
        }
    }

    [TestMethod]
    public async Task Middleware_emits_at_most_one_completed_event()
    {
        var collector = new ListLogger();
        var options = new HttpOperationLogOptions
        {
            Enabled = true,
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 1.0,
            SlowRequestThreshold = TimeSpan.FromSeconds(30),
        };
        var middleware = CreateMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.CompletedTask;
            },
            new StaticOptionsMonitor(options),
            new HttpOperationLogEmitter(new StaticOptionsMonitor(options), new DefaultDiagnosticPolicyStore()),
            collector);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/v1/demo";
        httpContext.Request.Method = "GET";

        await middleware.InvokeAsync(httpContext);
        Assert.HasCount(1, collector.Entries);
        Assert.AreEqual(HttpOperationLogMiddleware.EventName, collector.Entries[0].EventName);
    }

    [TestMethod]
    public async Task Disabled_mode_emits_nothing_but_still_runs_pipeline()
    {
        var ran = false;
        var collector = new ListLogger();
        var options = new HttpOperationLogOptions
        {
            Enabled = true,
            CaptureMode = HttpOperationCaptureMode.Disabled,
            SuccessSampleRate = 1.0,
        };
        var middleware = CreateMiddleware(
            context =>
            {
                ran = true;
                context.Response.StatusCode = 200;
                return Task.CompletedTask;
            },
            new StaticOptionsMonitor(options),
            new HttpOperationLogEmitter(new StaticOptionsMonitor(options), new DefaultDiagnosticPolicyStore()),
            collector);

        await middleware.InvokeAsync(new DefaultHttpContext
        {
            Request = { Path = "/api/v1/demo", Method = "GET" },
        });
        Assert.IsTrue(ran);
        Assert.IsEmpty(collector.Entries);
    }

    [TestMethod]
    public async Task Errors_bypass_success_sampling()
    {
        var collector = new ListLogger();
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 0,
            AlwaysRecordErrors = true,
        };
        var middleware = CreateMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            new StaticOptionsMonitor(options),
            new HttpOperationLogEmitter(new StaticOptionsMonitor(options), new DefaultDiagnosticPolicyStore()),
            collector);

        await middleware.InvokeAsync(new DefaultHttpContext
        {
            Request = { Path = "/api/v1/demo", Method = "GET" },
        });
        Assert.HasCount(1, collector.Entries);
        Assert.AreEqual(LogLevel.Error, collector.Entries[0].Level);
    }

    [TestMethod]
    public async Task Error_with_always_record_disabled_is_best_effort()
    {
        var options = new StaticOptionsMonitor(new HttpOperationLogOptions
        {
            AlwaysRecordErrors = false,
            SuccessSampleRate = 1,
            SlowRequestThreshold = TimeSpan.FromSeconds(30),
        });
        var ingress = new HttpOperationLogIngress();
        HttpOperationLogRecord? captured = null;
        ingress.Attach((record, _) =>
        {
            captured = record;
            return true;
        });
        var middleware = CreateMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            options,
            new HttpOperationLogEmitter(options, new DefaultDiagnosticPolicyStore()),
            new ListLogger(),
            ingress);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/orders";

        await middleware.InvokeAsync(context);

        Assert.IsNotNull(captured);
        Assert.AreEqual("BestEffort",
            captured.Fields.Single(field => field.Key == "reliability.class").Value);
        Assert.AreEqual(500,
            captured.Fields.Single(field => field.Key == "http.status_code").Value);
    }

    [TestMethod]
    public async Task Mapped_exception_is_recorded_with_original_route_and_exception_outcome()
    {
        var collector = new ListLogger();
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 0,
            AlwaysRecordErrors = true,
        };
        var monitor = new StaticOptionsMonitor(options);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddMetrics();
        services.AddProblemDetails();
        services.AddSingleton(new System.Diagnostics.DiagnosticListener("http-operation-test"));
        using var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.Use(next =>
        {
            var middleware = CreateMiddleware(
                next,
                monitor,
                new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
                collector);
            return middleware.InvokeAsync;
        });
        app.UseExceptionHandler(handler => handler.Run(context =>
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        }));
        app.Run(context =>
        {
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse("/api/v1/orders/{id}"),
                0,
                EndpointMetadataCollection.Empty,
                "order"));
            throw new InvalidOperationException("mapped exception");
        });

        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Path = "/api/v1/orders/42";
        await app.Build()(httpContext);

        Assert.AreEqual(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.HasCount(1, collector.Entries);
        Assert.AreEqual(LogLevel.Error, collector.Entries[0].Level);
        Assert.AreEqual("Exception", collector.Scopes[0]["Outcome"]);
        Assert.AreEqual("/api/v1/orders/{id}", collector.Scopes[0]["http.route"]);
    }

    [TestMethod]
    public async Task Request_id_does_not_impersonate_missing_trace_id()
    {
        var collector = new ListLogger();
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 1,
        };
        var monitor = new StaticOptionsMonitor(options);
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            collector);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";
        context.TraceIdentifier = "request-123";

        await middleware.InvokeAsync(context);

        Assert.HasCount(1, collector.Scopes);
        Assert.IsNull(collector.Scopes[0]["TraceId"]);
        Assert.AreEqual("request-123", collector.Scopes[0]["RequestId"]);
    }

    [TestMethod]
    public void Source_url_drops_credentials_query_and_fragment()
    {
        Assert.AreEqual(
            "https://example.test",
            HttpOperationLogSanitizer.SanitizeSourceUrl(
                "https://user:secret@example.test/private?token=abc#fragment"));
    }

    [TestMethod]
    public async Task Summary_uses_route_template_and_safe_metadata_without_path_values()
    {
        var collector = new ListLogger();
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 1,
        };
        var monitor = new StaticOptionsMonitor(options);
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            collector);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders/private-value";
        context.Request.QueryString = new QueryString("?name=private-value");
        context.Request.Protocol = "HTTP/2";
        context.Request.Headers.UserAgent = "Chrome/120 Bearer token-secret";
        context.Request.Headers.AcceptLanguage = "en-US, token-secret";
        context.Request.Headers.Origin = "https://token-secret.example.test";
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/orders/{id}"),
            0,
            new EndpointMetadataCollection(new EndpointNameMetadata("orders.get")),
            "Get order"));

        await middleware.InvokeAsync(context);

        Assert.HasCount(1, collector.Scopes);
        var scope = collector.Scopes[0];
        Assert.AreEqual("/api/v1/orders/{id}", scope["url"]);
        Assert.AreEqual("orders.get", scope["EndpointName"]);
        Assert.AreEqual("HTTP/2", scope["HttpProtocol"]);
        Assert.AreEqual("Chrome", scope["UserAgentSummary"]);
        Assert.AreEqual("en-US", scope["AcceptLanguageSummary"]);
        Assert.AreEqual(
            HttpOperationLogSanitizer.FingerprintClientIp("https://token-secret.example.test"),
            scope["SourceOriginFingerprint"]);
        Assert.IsFalse(scope.Values.OfType<string>().Any(
            value => value.Contains("token-secret", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Disabled_log_level_does_not_construct_scope_or_count_emission()
    {
        var collector = new ListLogger { Enabled = false };
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 1,
        };
        var monitor = new StaticOptionsMonitor(options);
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            collector);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context);

        Assert.IsEmpty(collector.Scopes);
        Assert.IsEmpty(collector.Entries);
    }

    [TestMethod]
    public async Task Emit_failure_does_not_pass_raw_exception_to_debug_provider()
    {
        var logger = new EmitFailureLogger();
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 1,
        };
        var monitor = new StaticOptionsMonitor(options);
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/items";

        await middleware.InvokeAsync(context);

        Assert.IsTrue(logger.DebugEmitted);
        Assert.IsNull(logger.DebugException);
        Assert.IsFalse(logger.DebugMessage.Contains("token-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Debug_provider_failure_does_not_replace_business_response()
    {
        var logger = new EmitFailureLogger { ThrowOnDebug = true };
        var options = new HttpOperationLogOptions
        {
            CaptureMode = HttpOperationCaptureMode.Summary,
            SuccessSampleRate = 1,
        };
        var monitor = new StaticOptionsMonitor(options);
        var middleware = CreateMiddleware(
            _ => Task.CompletedTask,
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            logger);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/items";

        await middleware.InvokeAsync(context);
    }

    [TestMethod]
    public async Task Invalid_hot_reload_options_do_not_block_business_request()
    {
        var monitor = new ThrowingOptionsMonitor();
        var called = false;
        var middleware = CreateMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            monitor,
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            new ListLogger());

        await middleware.InvokeAsync(new DefaultHttpContext());

        Assert.IsTrue(called);
    }

    private static HttpOperationLogMiddleware CreateMiddleware(
        RequestDelegate next,
        IOptionsMonitor<HttpOperationLogOptions> options,
        HttpOperationLogEmitter emitter,
        ILogger<HttpOperationLogMiddleware> logger,
        HttpOperationLogIngress? ingress = null)
    {
        if (ingress is null)
        {
            ingress = new HttpOperationLogIngress();
            ingress.Attach((record, level) =>
            {
                using (logger.BeginScope(record.Fields.ToDictionary(
                    field => field.Key,
                    field => field.Value)))
                {
                    logger.Log(
                        level switch
                        {
                            Serilog.Events.LogEventLevel.Error => LogLevel.Error,
                            Serilog.Events.LogEventLevel.Warning => LogLevel.Warning,
                            _ => LogLevel.Information,
                        },
                        "{EventName}",
                        HttpOperationLogMiddleware.EventName);
                }

                return true;
            });
        }

        return new HttpOperationLogMiddleware(next, options, emitter, logger, ingress);
    }

    private sealed class EmitFailureLogger : ILogger<HttpOperationLogMiddleware>
    {
        public bool ThrowOnDebug { get; init; }

        public bool DebugEmitted { get; private set; }

        public Exception? DebugException { get; private set; }

        public string DebugMessage { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            throw new InvalidOperationException("token-secret");

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Debug)
            {
                if (ThrowOnDebug)
                {
                    throw new InvalidOperationException("debug-provider-failed");
                }

                DebugEmitted = true;
                DebugException = exception;
                DebugMessage = formatter(state, exception);
            }
        }
    }

    private sealed class StaticOptionsMonitor(HttpOperationLogOptions current)
        : IOptionsMonitor<HttpOperationLogOptions>
    {
        public HttpOperationLogOptions CurrentValue { get; } = current;

        public HttpOperationLogOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<HttpOperationLogOptions, string?> listener) => null;
    }

    private sealed class ThrowingOptionsMonitor : IOptionsMonitor<HttpOperationLogOptions>
    {
        public HttpOperationLogOptions CurrentValue =>
            throw new InvalidOperationException("invalid-options");

        public HttpOperationLogOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<HttpOperationLogOptions, string?> listener) => null;
    }

    private sealed class StaticDiagnosticPolicyStore(DiagnosticPolicySnapshot snapshot)
        : IDiagnosticPolicyStore
    {
        public DiagnosticPolicySnapshot Current => snapshot;

        public ValueTask<DiagnosticPolicySnapshot> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(snapshot);

        public ValueTask RefreshAsync(long minimumVersion, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class ListLogger : ILogger<HttpOperationLogMiddleware>
    {
        public List<(LogLevel Level, string? EventName)> Entries { get; } = [];

        public List<Dictionary<string, object?>> Scopes { get; } = [];


        public bool Enabled { get; init; } = true;

        private string? _scopeEventName;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                var scope = values.ToDictionary(pair => pair.Key, pair => pair.Value);
                Scopes.Add(scope);
                _scopeEventName = scope
                    .FirstOrDefault(pair => pair.Key == "EventName").Value as string;
            }

            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => Enabled;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, _scopeEventName ?? HttpOperationLogMiddleware.EventName));
            _scopeEventName = null;
        }
    }
}
