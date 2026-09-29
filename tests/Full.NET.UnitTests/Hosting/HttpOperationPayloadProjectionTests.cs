using System.Diagnostics;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Hosting.Observability;
using Full.NET.Modules.Auditing.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class HttpOperationPayloadProjectionTests
{
    [TestMethod]
    public void Request_and_response_summaries_keep_separate_admission_and_results()
    {
        var options = NewOptions();
        options.MaxResponsePayloadBytes = 256;
        var service = CreateService(options);
        var context = NewContext();

        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var requestLease));
        using (requestLease)
        {
            Assert.AreEqual(HttpLogCaptureState.Captured,
                requestLease!.Capture(
                    () => new HttpPaginationLogProjection(2, 20),
                    HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection).State);
        }

        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.PaginationResult, out var responseLease));
        using (responseLease)
        {
            Assert.AreEqual(HttpLogCaptureState.Captured,
                responseLease!.Capture(
                    () => new HttpPaginationResultLogProjection(2, 20, 42, 20),
                    HttpLogCaptureJsonContext.Default.HttpPaginationResultLogProjection).State);
        }

        Assert.IsTrue(HttpOperationPayloadProjection.TryReadB2Result(context, out var requestJson));
        Assert.IsTrue(HttpOperationPayloadProjection.TryReadB2ResponseResult(context, out var responseJson));
        using var request = JsonDocument.Parse(requestJson!);
        using var response = JsonDocument.Parse(responseJson!);
        Assert.AreEqual(2, request.RootElement.GetProperty("page").GetInt32());
        Assert.AreEqual(42, response.RootElement.GetProperty("totalCount").GetInt64());

        var otherRequest = NewContext();
        otherRequest.Items[HttpOperationPayloadProjection.B2ResponseResultItemKey] =
            context.Items[HttpOperationPayloadProjection.B2ResponseResultItemKey];
        Assert.IsFalse(HttpOperationPayloadProjection.TryReadB2ResponseResult(otherRequest, out _));
    }

    [TestMethod]
    public void Response_lease_rejects_request_projection_before_factory_runs()
    {
        var options = NewOptions();
        options.MaxResponsePayloadBytes = 256;
        var service = CreateService(options);
        var context = NewContext();
        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.PaginationResult, out var lease));
        var factoryCalls = 0;
        using (lease)
        {
            Assert.AreEqual(HttpLogCaptureState.NotAllowed,
                lease!.Capture(
                    () =>
                    {
                        factoryCalls++;
                        return new HttpPaginationLogProjection(2, 20);
                    },
                    HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection).State);
        }

        Assert.AreEqual(0, factoryCalls);
        Assert.IsFalse(HttpOperationPayloadProjection.TryReadB2ResponseResult(context, out _));
    }

    [TestMethod]
    public void Response_summary_disabled_by_default_and_invalid_values_do_not_escape()
    {
        var options = NewOptions();
        var service = CreateService(options);
        var context = NewContext();
        Assert.IsFalse(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.PaginationResult, out var disabledLease, out var disabledState));
        Assert.IsNull(disabledLease);
        Assert.AreEqual(HttpLogCaptureState.NotEnabled, disabledState);

        options.MaxResponsePayloadBytes = 256;
        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.PaginationResult, out var lease));
        using (lease)
        {
            Assert.AreEqual(HttpLogCaptureState.NotAllowed,
                lease!.Capture(
                    () => new HttpPaginationResultLogProjection(1, 20, -1, 1),
                    HttpLogCaptureJsonContext.Default.HttpPaginationResultLogProjection).State);
        }
        Assert.IsFalse(HttpOperationPayloadProjection.TryReadB2ResponseResult(context, out _));
    }

    [TestMethod]
    public void Allowed_b2_projection_runs_factory_after_admission_and_returns_valid_json()
    {
        var options = NewOptions();
        var service = CreateService(options);
        var context = NewContext();
        var factoryCalls = 0;

        Assert.IsTrue(service.TryBeginCapture(
            context,
            HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination,
            out var lease,
            out var state));
        Assert.AreEqual(HttpLogCaptureState.Captured, state);
        Assert.IsNotNull(lease);
        using (lease)
        {
            var result = lease.Capture(
                () =>
                {
                    factoryCalls++;
                    return new HttpPaginationLogProjection(3, 25);
                },
                HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
            Assert.AreEqual(HttpLogCaptureState.Captured, result.State);
            using var document = JsonDocument.Parse(result.PayloadJson!);
            Assert.AreEqual(3, document.RootElement.GetProperty("page").GetInt32());
            Assert.AreEqual(25, document.RootElement.GetProperty("pageSize").GetInt32());
        }

        Assert.AreEqual(1, factoryCalls);
    }

    [TestMethod]
    public void Disabled_unknown_and_b1_targets_never_admit_factory()
    {
        var options = NewOptions();
        var service = CreateService(options);
        var context = NewContext();

        options.CaptureMode = HttpOperationCaptureMode.Disabled;
        Assert.IsFalse(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var disabledLease, out var disabledState));
        Assert.IsNull(disabledLease);
        Assert.AreEqual(HttpLogCaptureState.NotEnabled, disabledState);

        options.CaptureMode = HttpOperationCaptureMode.SanitizedPayload;
        Assert.IsFalse(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            "unregistered", out var unknownLease, out var unknownState));
        Assert.IsNull(unknownLease);
        Assert.AreEqual(HttpLogCaptureState.NotAllowed, unknownState);

        Assert.IsFalse(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B1RestrictedDetail,
            HttpLogCaptureProjectionKeys.Pagination, out var b1Lease, out var b1State));
        Assert.IsNull(b1Lease);
        Assert.AreEqual(HttpLogCaptureState.NotApplicable, b1State);
    }

    [TestMethod]
    public void Exhausted_budget_rejects_before_factory_and_abandoned_lease_refunds()
    {
        var options = NewOptions();
        options.CaptureMaxEventsPerSecond = 1;
        options.CaptureMaxBytesPerSecond = 256;
        options.MaxRequestPayloadBytes = 256;
        var service = CreateService(options);
        var context = NewContext();

        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var heldLease, out _));
        Assert.IsFalse(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var rejectedLease, out var state));
        Assert.IsNull(rejectedLease);
        Assert.AreEqual(HttpLogCaptureState.BudgetExceeded, state);

        heldLease!.Dispose();

        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var recoveredLease, out _));
        recoveredLease!.Dispose();
    }

    [TestMethod]
    public void Factory_failure_consumes_attempt_budget_without_exposing_exception()
    {
        var options = NewOptions();
        options.CaptureMaxEventsPerSecond = 1;
        options.CaptureMaxBytesPerSecond = 256;
        options.MaxRequestPayloadBytes = 256;
        var service = CreateService(options);
        var context = NewContext();

        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out _));
        Assert.IsNotNull(lease);
        var result = lease.Capture<HttpPaginationLogProjection>(
            () => throw new InvalidOperationException("token=never-log-this"),
            HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
        Assert.AreEqual(HttpLogCaptureState.Failed, result.State);
        Assert.IsNull(result.PayloadJson);
        lease.Dispose();

        Assert.IsFalse(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var rejectedLease, out var state));
        Assert.IsNull(rejectedLease);
        Assert.AreEqual(HttpLogCaptureState.BudgetExceeded, state);
    }

    [TestMethod]
    public void Oversize_serialization_consumes_attempt_budget()
    {
        var options = NewOptions();
        options.MaxRequestPayloadBytes = 1;
        options.CaptureMaxEventsPerSecond = 1;
        options.CaptureMaxBytesPerSecond = 1;
        var service = CreateService(options);
        var context = NewContext();
        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out _));

        var result = lease!.Capture(
            () => new HttpPaginationLogProjection(1, 10),
            HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
        Assert.AreEqual(HttpLogCaptureState.BudgetExceeded, result.State);
        lease.Dispose();

        Assert.IsFalse(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out _, out var state));
        Assert.AreEqual(HttpLogCaptureState.BudgetExceeded, state);
    }

    [TestMethod]
    public void Cancelled_request_does_not_invoke_factory_or_hold_budget()
    {
        var options = NewOptions();
        options.CaptureMaxEventsPerSecond = 1;
        var service = CreateService(options);
        var context = NewContext();
        using var cancellation = new CancellationTokenSource();
        context.RequestAborted = cancellation.Token;
        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out _));
        cancellation.Cancel();
        var called = 0;

        var result = lease!.Capture(
            () =>
            {
                called++;
                return new HttpPaginationLogProjection(1, 10);
            },
            HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);

        Assert.AreEqual(0, called);
        Assert.AreEqual(HttpLogCaptureState.Failed, result.State);
        lease.Dispose();
        var next = NewContext();
        Assert.IsTrue(service.TryBeginCapture(
            next, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var recoveredLease, out _));
        recoveredLease!.Dispose();
    }

    [TestMethod]
    public void Different_json_metadata_is_rejected_before_factory()
    {
        var service = CreateService(NewOptions());
        var context = NewContext();
        Assert.IsTrue(service.TryBeginCapture(
            context, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out _));
        var called = 0;
        var foreignMetadata = new HttpLogCaptureJsonContext(new JsonSerializerOptions())
            .HttpPaginationLogProjection;

        var result = lease!.Capture(
            () =>
            {
                called++;
                return new HttpPaginationLogProjection(1, 10);
            },
            foreignMetadata);

        Assert.AreEqual(0, called);
        Assert.AreEqual(HttpLogCaptureState.NotAllowed, result.State);
        lease.Dispose();
    }

    [TestMethod]
    public void B2_result_cannot_be_reused_by_another_request()
    {
        var service = CreateService(NewOptions());
        var owner = NewContext();
        Assert.IsTrue(service.TryBeginCapture(
            owner, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out _));
        using (lease)
        {
            var result = lease!.Capture(
                () => new HttpPaginationLogProjection(1, 10),
                HttpLogCaptureJsonContext.Default.HttpPaginationLogProjection);
            Assert.AreEqual(HttpLogCaptureState.Captured, result.State);

            var otherRequest = NewContext();
            otherRequest.Items[HttpOperationPayloadProjection.B2ResultItemKey] = result;
            Assert.IsFalse(HttpOperationPayloadProjection.TryReadB2Result(otherRequest, out var payload));
            Assert.IsNull(payload);
            Assert.IsTrue(HttpOperationPayloadProjection.TryReadB2Result(owner, out _));
        }
    }

    [TestMethod]
    public void Operation_log_list_endpoint_projects_only_normalized_pagination()
    {
        var options = NewOptions();
        options.PayloadRouteAllowList = ["/api/v1/auditing/operation-logs/"];
        options.MaxResponsePayloadBytes = 256;
        var service = CreateService(options);
        var context = NewContext("/api/v1/auditing/operation-logs/");
        context.Request.QueryString = new QueryString("?pathContains=token%3Dsecret&page=999");
        var result = Result<PagedResult<OperationLogResponse>>.Success(
            new PagedResult<OperationLogResponse>([], 3, 25, 0));

        Full.NET.Modules.Auditing.Features.QueryHostOperationLogs.Endpoint
            .CapturePaginationForLog(result, service, context);
        Full.NET.Modules.Auditing.Features.QueryHostOperationLogs.Endpoint
            .CapturePaginationResultForLog(result, service, context);

        Assert.IsTrue(HttpOperationPayloadProjection.TryReadB2Result(context, out var payload));
        Assert.IsNotNull(payload);
        using var document = JsonDocument.Parse(payload);
        Assert.AreEqual(3, document.RootElement.GetProperty("page").GetInt32());
        Assert.AreEqual(25, document.RootElement.GetProperty("pageSize").GetInt32());
        Assert.IsFalse(payload.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(payload.Contains("999", StringComparison.Ordinal));

        Assert.IsTrue(HttpOperationPayloadProjection.TryReadB2ResponseResult(
            context, out var responsePayload));
        Assert.IsNotNull(responsePayload);
        using var responseDocument = JsonDocument.Parse(responsePayload);
        Assert.AreEqual(3, responseDocument.RootElement.GetProperty("page").GetInt32());
        Assert.AreEqual(25, responseDocument.RootElement.GetProperty("pageSize").GetInt32());
        Assert.AreEqual(0, responseDocument.RootElement.GetProperty("totalCount").GetInt64());
        Assert.AreEqual(0, responseDocument.RootElement.GetProperty("itemCount").GetInt32());
        Assert.IsFalse(responsePayload.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(responsePayload.Contains("999", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Operation_log_list_projection_fault_does_not_replace_query_result()
    {
        var result = Result<PagedResult<OperationLogResponse>>.Success(
            new PagedResult<OperationLogResponse>([], 1, 20, 0));

        Full.NET.Modules.Auditing.Features.QueryHostOperationLogs.Endpoint
            .CapturePaginationForLog(result, null!, NewContext());
        Full.NET.Modules.Auditing.Features.QueryHostOperationLogs.Endpoint
            .CapturePaginationResultForLog(result, null!, NewContext());

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void Unmatched_route_and_zero_sampling_are_denied()
    {
        var options = NewOptions();
        var service = CreateService(options);
        var unmatched = new DefaultHttpContext();
        Assert.IsFalse(service.TryBeginCapture(
            unmatched, HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out _, out var routeState));
        Assert.AreEqual(HttpLogCaptureState.NotAllowed, routeState);

        options.SuccessSampleRate = 0;
        Assert.IsFalse(service.TryBeginCapture(
            NewContext(), HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out _, out var sampleState));
        Assert.AreEqual(HttpLogCaptureState.NotAllowed, sampleState);
    }

    [TestMethod]
    public void Truncated_route_prefix_does_not_grant_another_endpoint_capture()
    {
        var approvedRoute = "/api/" + new string('a', 260) + "/approved";
        var otherRoute = "/api/" + new string('a', 260) + "/other";
        var options = NewOptions();
        options.PayloadRouteAllowList =
            [HttpOperationMetadataBuilder.ResolveRouteTemplate(NewContext(approvedRoute))];
        var service = CreateService(options);

        Assert.IsFalse(service.TryBeginCapture(
            NewContext(otherRoute), HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out var state));
        Assert.IsNull(lease);
        Assert.AreEqual(HttpLogCaptureState.NotAllowed, state);
    }

    [TestMethod]
    public void Middleware_excluded_path_does_not_spend_projection_budget()
    {
        var options = NewOptions();
        options.ExcludePathPrefixes = ["/api/items"];
        var service = CreateService(options);

        Assert.IsFalse(service.TryBeginCapture(
            NewContext(), HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out var state));
        Assert.IsNull(lease);
        Assert.AreEqual(HttpLogCaptureState.NotAllowed, state);
    }

    [TestMethod]
    public void Disabled_information_logging_does_not_consume_capture_budget()
    {
        var options = NewOptions();
        options.CaptureMaxEventsPerSecond = 1;
        var monitor = new StaticOptionsMonitor(options);
        var budget = new HttpLogCaptureBudget(TimeProvider.System);
        var emitter = new HttpOperationLogEmitter(
            monitor, new DefaultDiagnosticPolicyStore());
        var disabled = new HttpOperationPayloadProjection(
            monitor, budget, emitter, new ProjectionLogger(enabled: false));
        var enabled = new HttpOperationPayloadProjection(
            monitor, budget, emitter, new ProjectionLogger());

        Assert.IsFalse(disabled.TryBeginCapture(
            NewContext(), HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var deniedLease, out var state));
        Assert.IsNull(deniedLease);
        Assert.AreEqual(HttpLogCaptureState.NotAllowed, state);
        Assert.IsTrue(enabled.TryBeginCapture(
            NewContext(), HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var admittedLease));
        admittedLease!.Dispose();
    }

    [TestMethod]
    public void Admission_uses_the_same_sanitized_sample_key_as_final_emission()
    {
        var previousActivity = Activity.Current;
        Activity.Current = null;
        try
        {
            var options = NewOptions();
            options.SuccessSampleRate = 0.5;
            var monitor = new StaticOptionsMonitor(options);
            var emitter = new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore());
            var service = new HttpOperationPayloadProjection(
                monitor,
                new HttpLogCaptureBudget(TimeProvider.System),
                emitter,
                new ProjectionLogger());
            var traceIdentifier = Enumerable.Range(0, 100)
                .Select(index => $"capture-{index}")
                .First(value => emitter.ShouldSampleSuccess("/api/items", value)
                    != emitter.ShouldSampleSuccess("/api/items", value + "\n"));
            var context = NewContext();
            context.TraceIdentifier = traceIdentifier + "\n";
            var expected = emitter.ShouldSampleSuccess("/api/items", traceIdentifier);

            var admitted = service.TryBeginCapture(
                context, HttpLogCaptureTarget.B2InternalSummary,
                HttpLogCaptureProjectionKeys.Pagination, out var lease, out _);

            Assert.AreEqual(expected, admitted);
            lease?.Dispose();
        }
        finally
        {
            Activity.Current = previousActivity;
        }
    }

    [TestMethod]
    public void Options_failure_is_fail_open_and_does_not_admit_capture()
    {
        var monitor = new ThrowingOptionsMonitor();
        var service = new HttpOperationPayloadProjection(
            monitor,
            new HttpLogCaptureBudget(TimeProvider.System),
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            new ProjectionLogger());

        var admitted = service.TryBeginCapture(
            NewContext(), HttpLogCaptureTarget.B2InternalSummary,
            HttpLogCaptureProjectionKeys.Pagination, out var lease, out var state);

        Assert.IsFalse(admitted);
        Assert.IsNull(lease);
        Assert.AreEqual(HttpLogCaptureState.Failed, state);
    }

    private static HttpOperationLogOptions NewOptions() => new()
    {
        Enabled = true,
        CaptureMode = HttpOperationCaptureMode.SanitizedPayload,
        SuccessSampleRate = 1,
        PayloadRouteAllowList = ["/api/items"],
        MaxRequestPayloadBytes = 256,
        CaptureMaxEventsPerSecond = 10,
        CaptureMaxBytesPerSecond = 2_560,
    };

    private static HttpOperationPayloadProjection CreateService(HttpOperationLogOptions options)
    {
        var monitor = new StaticOptionsMonitor(options);
        return new HttpOperationPayloadProjection(
            monitor,
            new HttpLogCaptureBudget(TimeProvider.System),
            new HttpOperationLogEmitter(monitor, new DefaultDiagnosticPolicyStore()),
            new ProjectionLogger());
    }

    private static DefaultHttpContext NewContext(string route = "/api/items")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = route;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            0,
            EndpointMetadataCollection.Empty,
            "items"));
        return context;
    }

    private sealed class StaticOptionsMonitor(HttpOperationLogOptions options)
        : IOptionsMonitor<HttpOperationLogOptions>
    {
        public HttpOperationLogOptions CurrentValue => options;

        public HttpOperationLogOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<HttpOperationLogOptions, string?> listener) => null;
    }

    private sealed class ThrowingOptionsMonitor : IOptionsMonitor<HttpOperationLogOptions>
    {
        public HttpOperationLogOptions CurrentValue =>
            throw new InvalidOperationException("secret=config-value");

        public HttpOperationLogOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<HttpOperationLogOptions, string?> listener) => null;
    }

    private sealed class ProjectionLogger(bool enabled = true)
        : ILogger<HttpOperationLogMiddleware>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => enabled;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
