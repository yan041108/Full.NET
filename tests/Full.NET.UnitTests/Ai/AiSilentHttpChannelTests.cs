using System.Diagnostics.Metrics;
using Full.NET.AI.Providers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

namespace Full.NET.UnitTests.Ai;

/// <summary>静默请求不继承默认 HttpClient 日志或通用 OTel 观测；策略拒绝仍保持原异常。</summary>
[TestClass, DoNotParallelize]
public sealed class AiSilentHttpChannelTests
{
    [TestMethod]
    public async Task Silent_handler_suppresses_only_its_async_transport_and_restores_context()
    {
        var handler = new ScopeHandler();
        using var client = new HttpClient(new AiSilentHttpHandler { InnerHandler = handler });
        Assert.IsFalse(Sdk.SuppressInstrumentation);
        using var response = await client.GetAsync("https://provider.test");
        Assert.IsTrue(handler.Suppressed); Assert.IsFalse(Sdk.SuppressInstrumentation);
    }

    [TestMethod]
    public void Silent_meter_never_publishes_even_to_a_listener_of_all_http_instruments()
    {
        var published = 0; using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) => { if (instrument.Meter.Name == "Full.NET.Ai.SilentTest") published++; };
        listener.Start(); using var factory = new AiSilentMeterFactory(); using var meter = factory.Create(new("Full.NET.Ai.SilentTest"));
        var counter = meter.CreateCounter<long>("http.client.requests"); counter.Add(1);
        Assert.IsFalse(counter.Enabled); Assert.AreEqual(0, published);
    }

    [TestMethod]
    [DataRow("openai_compatible")] [DataRow("ollama")] [DataRow("azure_openai")]
    public async Task Frozen_silent_client_has_no_default_request_logger_on_policy_rejection(string provider)
    {
        var logger = new CaptureLogger(); var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logger)); services.AddAiProviderHttpClients(new ConfigurationBuilder().Build());
        using var root = services.BuildServiceProvider();
        using var client = root.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderHttpClients.Name(provider, false, false, false, false));
        // 元数据/回环目的地在策略层拒绝，测试不发生 DNS、TCP 或收费模型请求。
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync("http://127.0.0.1"));
        Assert.AreEqual(0, logger.RequestLogs);
    }

    private sealed class ScopeHandler : HttpMessageHandler
    {
        internal bool Suppressed;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { await Task.Yield(); Suppressed = Sdk.SuppressInstrumentation; return new(System.Net.HttpStatusCode.OK); }
    }
    private sealed class CaptureLogger : ILoggerProvider, ILogger
    {
        internal int RequestLogs;
        public ILogger CreateLogger(string categoryName) => categoryName.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal) ? this : new NullLogger();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Interlocked.Increment(ref RequestLogs);
        public void Dispose() { }
    }
    private sealed class NullLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
