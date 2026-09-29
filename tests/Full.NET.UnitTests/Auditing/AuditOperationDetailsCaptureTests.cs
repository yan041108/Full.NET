using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Time;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Middleware;
using Full.NET.Modules.Auditing.Retention;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditOperationDetailsCaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Healthy_allowlisted_operation_captures_only_fixed_network_fields()
    {
        var clock = new FixedClock { UtcNow = Now };
        var options = new AuditDetailsCaptureOptions
        {
            Enabled = true,
            RetentionHours = 24,
            CaptureRouteAllowList = ["/api/v1/orders/{id:guid}"],
        };
        var monitor = new StaticOptionsMonitor<AuditDetailsCaptureOptions>(options);
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        var capture = CreateCapture(cache, clock, monitor);
        var context = CreateContext();
        context.Request.QueryString = new QueryString("?token=secret-query");
        context.Request.Headers.Authorization = "Bearer secret-header";

        var details = capture.TryCapture(context);

        Assert.IsNotNull(details);
        Assert.AreEqual(Now.AddHours(24), details.ExpiresAtUtc);
        using var json = JsonDocument.Parse(details.ContextJson);
        Assert.AreEqual(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual("2001:db8::1", json.RootElement.GetProperty("clientIp").GetString());
        Assert.AreEqual(443, json.RootElement.GetProperty("serverPort").GetInt32());
        Assert.IsFalse(details.ContextJson.Contains("secret-query", StringComparison.Ordinal));
        Assert.IsFalse(details.ContextJson.Contains("secret-header", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Middleware_injects_capture_service_and_attaches_detail_to_B1_operation()
    {
        var clock = new FixedClock { UtcNow = Now };
        var options = new StaticOptionsMonitor<AuditDetailsCaptureOptions>(
            new AuditDetailsCaptureOptions
            {
                Enabled = true,
                CaptureRouteAllowList = ["/api/v1/orders/{id:guid}"],
            });
        var cache = new AuditDetailsCapturePolicyCache(clock, options);
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        var buffer = new AuditWriteBuffer();
        var services = new ServiceCollection();
        services.AddSingleton(new OperationLogWriter(buffer));
        services.AddSingleton(CreateCapture(cache, clock, options));
        using var provider = services.BuildServiceProvider();
        var context = CreateContext();
        context.RequestServices = provider;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(FullNetIdentityClaimTypes.Subject, Guid.CreateVersion7().ToString())],
            authenticationType: "test"));
        var app = new ApplicationBuilder(provider);
        app.UseMiddleware<OperationLogMiddleware>();
        app.Run(_ => Task.CompletedTask);

        await app.Build()(context);

        Assert.IsNotNull(buffer.Snapshot().Operation?.Details);
    }

    [TestMethod]
    public void Disabled_unknown_or_unlisted_route_never_creates_details()
    {
        var clock = new FixedClock { UtcNow = Now };
        var options = new AuditDetailsCaptureOptions
        {
            Enabled = true,
            CaptureRouteAllowList = ["/api/v1/orders/{id:guid}"],
        };
        var monitor = new StaticOptionsMonitor<AuditDetailsCaptureOptions>(options);
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        var capture = CreateCapture(cache, clock, monitor);
        var context = CreateContext();

        Assert.IsNull(capture.TryCapture(context));
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        context.SetEndpoint(null);
        Assert.IsNull(capture.TryCapture(context));
        context.SetEndpoint(new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/other/{id:guid}"),
            0).Build());
        Assert.IsNull(capture.TryCapture(context));
        context.SetEndpoint(CreateContext().GetEndpoint());
        context.User = new ClaimsPrincipal();
        Assert.IsNull(capture.TryCapture(context));
        context.User = CreateContext().User;
        context.Request.Method = HttpMethods.Get;
        Assert.IsNull(capture.TryCapture(context));
        context.Request.Method = HttpMethods.Put;
        options.Enabled = false;
        Assert.IsNull(capture.TryCapture(context));
    }

    [TestMethod]
    public void Detail_retention_must_not_outlive_operation_row()
    {
        var invalid = new AuditDetailsCaptureOptionsValidator().Validate(null,
            new AuditDetailsCaptureOptions
            {
                RetentionHours = 0,
                CaptureRouteAllowList = ["/api/v1/orders/*"],
            });
        Assert.IsTrue(invalid.Failed);

        var clock = new FixedClock { UtcNow = Now };
        var options = new AuditDetailsCaptureOptions
        {
            Enabled = true,
            RetentionHours = 48,
            CaptureRouteAllowList = ["/api/v1/orders/{id:guid}"],
        };
        var monitor = new StaticOptionsMonitor<AuditDetailsCaptureOptions>(options);
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        var capture = CreateCapture(cache, clock, monitor,
            new AuditingRetentionOptions { OperationRetentionDays = 1 });

        Assert.IsNull(capture.TryCapture(CreateContext()));
    }

    [TestMethod]
    public void Allowed_export_projects_only_fixed_request_and_response_fields()
    {
        var clock = new FixedClock { UtcNow = Now };
        var options = new AuditDetailsCaptureOptions
        {
            Enabled = true,
            CaptureRouteAllowList = ["/api/v1/auditing/operation-logs/exports"],
            MaxResponsePayloadBytes = 256,
        };
        var monitor = new StaticOptionsMonitor<AuditDetailsCaptureOptions>(options);
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        var capture = CreateCapture(cache, clock, monitor);
        var context = CreateContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/auditing/operation-logs/exports";
        context.SetEndpoint(new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/auditing/operation-logs/exports"),
            0).Build());
        var request = new AuditLogExportRequest(Now.AddHours(-2), Now,
            PathContains: "secret-filter", StatusCode: 200, Succeeded: true);
        var metadata = new AuditLogExportMetadataResponse(12, false, true,
            request.FromUtc, request.ToUtc, "secret-filename.xlsx");

        capture.TryCaptureExportSummary(context, () => request, () => metadata);
        var details = capture.TryCapture(context);

        Assert.IsNotNull(details);
        using var json = JsonDocument.Parse(details.ContextJson);
        Assert.AreEqual("captured", json.RootElement.GetProperty("requestCaptureState").GetString());
        Assert.AreEqual(200, json.RootElement.GetProperty("requestSummary")
            .GetProperty("statusCode").GetInt32());
        Assert.AreEqual("captured", json.RootElement.GetProperty("responseCaptureState").GetString());
        Assert.AreEqual(12, json.RootElement.GetProperty("responseSummary")
            .GetProperty("rowCount").GetInt32());
        Assert.IsFalse(details.ContextJson.Contains("secret-filter", StringComparison.Ordinal));
        Assert.IsFalse(details.ContextJson.Contains("secret-filename", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Export_projection_factory_runs_only_after_detail_admission_and_response_opt_in()
    {
        var clock = new FixedClock { UtcNow = Now };
        var options = new AuditDetailsCaptureOptions
        {
            Enabled = true,
            CaptureRouteAllowList = ["/api/v1/auditing/operation-logs/exports"],
        };
        var monitor = new StaticOptionsMonitor<AuditDetailsCaptureOptions>(options);
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        var capture = CreateCapture(cache, clock, monitor);
        var context = CreateContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/auditing/operation-logs/exports";
        context.SetEndpoint(new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/auditing/operation-logs/exports"),
            0).Build());
        var requestCalls = 0;
        var responseCalls = 0;
        AuditLogExportRequest Request() { requestCalls++; return new(Now.AddHours(-1), Now); }
        AuditLogExportMetadataResponse Response()
        {
            responseCalls++;
            return new(1, false, false, Now.AddHours(-1), Now, "ignored.xlsx");
        }

        capture.TryCaptureExportSummary(context, Request, Response);
        Assert.AreEqual(0, requestCalls);
        Assert.AreEqual(0, responseCalls);

        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        capture.TryCaptureExportSummary(context, Request, Response);
        Assert.AreEqual(1, requestCalls);
        Assert.AreEqual(0, responseCalls);
        var details = capture.TryCapture(context);
        Assert.IsNotNull(details);
        using var json = JsonDocument.Parse(details.ContextJson);
        Assert.AreEqual("not_enabled", json.RootElement
            .GetProperty("responseCaptureState").GetString());
    }

    [TestMethod]
    public void Export_projection_enforces_independent_byte_limits()
    {
        var clock = new FixedClock { UtcNow = Now };
        var options = new AuditDetailsCaptureOptions
        {
            Enabled = true,
            CaptureRouteAllowList = ["/api/v1/auditing/operation-logs/exports"],
            MaxRequestPayloadBytes = 1,
            MaxResponsePayloadBytes = 1,
        };
        var monitor = new StaticOptionsMonitor<AuditDetailsCaptureOptions>(options);
        var cache = new AuditDetailsCapturePolicyCache(clock, monitor);
        cache.RecordSuccessfulRead(new AuditDetailsCleanupCheckpointSnapshot(Now, null));
        var capture = CreateCapture(cache, clock, monitor);
        var context = CreateContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/auditing/operation-logs/exports";
        context.SetEndpoint(new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/auditing/operation-logs/exports"),
            0).Build());

        capture.TryCaptureExportSummary(context,
            () => new AuditLogExportRequest(Now.AddHours(-1), Now),
            () => new AuditLogExportMetadataResponse(1, false, false,
                Now.AddHours(-1), Now, "ignored.xlsx"));
        var details = capture.TryCapture(context);

        Assert.IsNotNull(details);
        using var json = JsonDocument.Parse(details.ContextJson);
        Assert.AreEqual("budget_exceeded", json.RootElement
            .GetProperty("requestCaptureState").GetString());
        Assert.AreEqual("budget_exceeded", json.RootElement
            .GetProperty("responseCaptureState").GetString());
        Assert.AreEqual(JsonValueKind.Null, json.RootElement
            .GetProperty("requestSummary").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, json.RootElement
            .GetProperty("responseSummary").ValueKind);

        var invalid = new AuditDetailsCaptureOptionsValidator().Validate(null,
            new AuditDetailsCaptureOptions { MaxResponsePayloadBytes = 2_049 });
        Assert.IsTrue(invalid.Failed);
    }

    private static AuditOperationDetailsCapture CreateCapture(
        AuditDetailsCapturePolicyCache cache,
        IClock clock,
        IOptionsMonitor<AuditDetailsCaptureOptions> options,
        AuditingRetentionOptions? retention = null) =>
        new(cache, clock, options,
            new StaticOptionsMonitor<AuditingRetentionOptions>(
                retention ?? new AuditingRetentionOptions()));

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/v1/orders/123";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(FullNetIdentityClaimTypes.Subject, Guid.CreateVersion7().ToString())],
            authenticationType: "test"));
        context.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1");
        context.Connection.RemotePort = 51515;
        context.Connection.LocalIpAddress = IPAddress.Parse("10.1.2.3");
        context.Connection.LocalPort = 443;
        context.SetEndpoint(new RouteEndpointBuilder(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/orders/{id:guid}"),
            0).Build());
        return context;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
