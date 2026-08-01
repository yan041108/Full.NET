using Full.NET.Hosting.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class HttpOperationLogTests
{
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
    public void Success_sampling_is_deterministic_for_route_and_trace()
    {
        var options = new HttpOperationLogOptions
        {
            SuccessSampleRate = 0.5,
        };
        var emitter = new HttpOperationLogEmitter(new StaticOptionsMonitor(options));
        var first = emitter.ShouldSampleSuccess("orders/{id}", "trace-a");
        var second = emitter.ShouldSampleSuccess("orders/{id}", "trace-a");
        Assert.AreEqual(first, second);
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
        var middleware = new HttpOperationLogMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.CompletedTask;
            },
            new StaticOptionsMonitor(options),
            new HttpOperationLogEmitter(new StaticOptionsMonitor(options)),
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
        var middleware = new HttpOperationLogMiddleware(
            context =>
            {
                ran = true;
                context.Response.StatusCode = 200;
                return Task.CompletedTask;
            },
            new StaticOptionsMonitor(options),
            new HttpOperationLogEmitter(new StaticOptionsMonitor(options)),
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
        var middleware = new HttpOperationLogMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return Task.CompletedTask;
            },
            new StaticOptionsMonitor(options),
            new HttpOperationLogEmitter(new StaticOptionsMonitor(options)),
            collector);

        await middleware.InvokeAsync(new DefaultHttpContext
        {
            Request = { Path = "/api/v1/demo", Method = "GET" },
        });
        Assert.HasCount(1, collector.Entries);
        Assert.AreEqual(LogLevel.Error, collector.Entries[0].Level);
    }

    private sealed class StaticOptionsMonitor(HttpOperationLogOptions current)
        : IOptionsMonitor<HttpOperationLogOptions>
    {
        public HttpOperationLogOptions CurrentValue { get; } = current;

        public HttpOperationLogOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<HttpOperationLogOptions, string?> listener) => null;
    }

    private sealed class ListLogger : ILogger<HttpOperationLogMiddleware>
    {
        public List<(LogLevel Level, string? EventName)> Entries { get; } = [];

        private string? _scopeEventName;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                _scopeEventName = values
                    .FirstOrDefault(pair => pair.Key == "EventName").Value as string;
            }

            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

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
