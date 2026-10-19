using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Full.NET.AI.Providers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试：Provider HTTP 遥测只统计传输尝试，不把 HTTP 成功当作模型或结算成功。</summary>
[TestClass, DoNotParallelize]
public sealed class AiProviderTelemetryTests
{
    private const string Name = "Full.NET.Ai.Providers";

    [TestMethod]
    [DataRow("ollama", false, false, "chat")]
    [DataRow("ollama", false, true, "embedding")]
    [DataRow("ollama", true, false, "connectivity")]
    [DataRow("openai_compatible", false, false, "chat")]
    [DataRow("openai_compatible", false, true, "embedding")]
    [DataRow("openai_compatible", true, false, "connectivity")]
    [DataRow("azure_openai", false, false, "chat")]
    [DataRow("azure_openai", false, true, "embedding")]
    [DataRow("azure_openai", true, false, "connectivity")]
    public async Task Registered_policy_denial_is_observed_without_payload_or_endpoint(string key, bool connectivity, bool embedding, string operation)
    {
        using var capture = new Capture();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddAiProviderHttpClients(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["FullNet:Ai:Network:PrivateOnly"] = "true" }).Build());
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderHttpClients.Name(key, connectivity, embedding));
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.GetAsync("https://8.8.8.8/secret-model?key=private-key"));
        Assert.AreEqual("AI destination is blocked by host network policy.", error.Message);
        capture.Verify(key, operation, "policy_rejected", ActivityStatusCode.Error);
    }

    [TestMethod]
    [DataRow(200, "succeeded")] [DataRow(204, "succeeded")] [DataRow(299, "succeeded")]
    [DataRow(301, "redirected")] [DataRow(399, "redirected")]
    [DataRow(400, "rejected")] [DataRow(401, "rejected")] [DataRow(403, "rejected")]
    [DataRow(404, "rejected")] [DataRow(499, "rejected")]
    [DataRow(429, "rate_limited")]
    [DataRow(500, "server_error")] [DataRow(503, "server_error")] [DataRow(599, "server_error")]
    [DataRow(100, "invalid_status")] [DataRow(199, "invalid_status")] [DataRow(600, "invalid_status")]
    public async Task Status_groups_preserve_original_response_and_use_fixed_outcomes(int status, string outcome)
    {
        using var capture = new Capture();
        using var response = new HttpResponseMessage((HttpStatusCode)status) { ReasonPhrase = "provider-secret" };
        using var invoker = Invoker((_, _) => Task.FromResult(response));
        using var request = Request();
        Assert.AreSame(response, await invoker.SendAsync(request, default));
        capture.Verify("ollama", "chat", outcome, outcome == "succeeded" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Sdk_wrapped_dns_policy_rejection_retains_policy_identity(bool mixed)
    {
        using var capture = new Capture(); var connections = 0;
        var connector = new AiNetworkConnector(new(["https://dns-policy.test"], true), true,
            (_, _) => Task.FromResult(mixed ? new[] { IPAddress.Loopback, IPAddress.Parse("8.8.8.8") } : new[] { IPAddress.Parse("8.8.8.8") }),
            (_, _) => { connections++; return ValueTask.FromResult<Stream>(new MemoryStream()); });
        using var sockets = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false,
            ConnectCallback = (context, token) => connector.ConnectAsync(context.InitialRequestMessage.RequestUri!, context.DnsEndPoint, token),
        };
        using var invoker = new HttpMessageInvoker(new AiProviderTelemetryHandler("ollama", "chat") { InnerHandler = sockets });
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://dns-policy.test");
        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => invoker.SendAsync(request, default));
        Assert.IsNotNull(error.InnerException);
        Assert.AreEqual(0, connections);
        capture.Verify("ollama", "chat", "policy_rejected", ActivityStatusCode.Error);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Parent_current_changed_failure_cannot_replace_response_or_policy_error(bool rejected)
    {
        using var capture = new Capture();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var original = AiEndpointPolicy.Blocked();
        using var invoker = Invoker((_, _) => rejected ? Task.FromException<HttpResponseMessage>(original) : Task.FromResult(response));
        using var request = Request();
        EventHandler<ActivityChangedEventArgs> callback = (_, change) =>
        { if (ReferenceEquals(change.Current, capture.Root)) throw new InvalidOperationException("observer-secret"); };
        Activity.CurrentChanged += callback;
        try
        {
            if (rejected) Assert.AreSame(original, await Assert.ThrowsExactlyAsync<HttpRequestException>(() => invoker.SendAsync(request, default)));
            else Assert.AreSame(response, await invoker.SendAsync(request, default));
            Assert.AreSame(capture.Root, Activity.Current);
        }
        finally { Activity.CurrentChanged -= callback; }
        capture.Verify("ollama", "chat", rejected ? "policy_rejected" : "succeeded", rejected ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
    }

    [TestMethod]
    public async Task Publication_recursion_preserves_request_and_finishes_outer_measurement()
    {
        using var capture = new Capture(); var armed = false; var recursive = 0;
        using var publisher = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (!armed || instrument.Meter.Name != Name || instrument.Name != "fullnet.ai.provider.http.requests") return;
                recursive++;
                using var nested = AiProviderTelemetry.Start("ollama", "connectivity");
                nested.Outcome = "succeeded";
            },
        };
        publisher.Start(); armed = true;
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var invoker = Invoker((_, _) => Task.FromResult(response));
        using var request = Request();
        Assert.AreSame(response, await invoker.SendAsync(request, default));
        if (Environment.GetEnvironmentVariable("FULLNET_AI_TELEMETRY_FRESH_PROBE") == "1") Assert.AreEqual(1, recursive);
        Assert.HasCount(2, capture.Measurements);
        Assert.AreEqual(1d, capture.Measurements.Single(m => m.Name.EndsWith("requests", StringComparison.Ordinal)).Value);
        Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)]
    public async Task Exception_identity_is_preserved_without_reading_diagnostic_payload(int fault)
    {
        using var capture = new Capture();
        Exception original = fault switch { 0 => new HttpRequestException("AI destination is blocked by host network policy."),
            1 => new PoisonException(), _ => new IOException("provider-body-and-key") };
        using var invoker = Invoker((_, _) => Task.FromException<HttpResponseMessage>(original));
        using var request = Request();
        Exception? caught = null;
        try { await invoker.SendAsync(request, default); } catch (Exception error) { caught = error; }
        Assert.AreSame(original, caught);
        capture.Verify("ollama", "chat", fault == 2 ? "failed" : "network_error", ActivityStatusCode.Error);
    }

    [TestMethod]
    [DataRow(7, "policy_rejected")] [DataRow(8, "network_error")]
    public async Task Exception_identity_chain_is_bounded(int wrappers, string outcome)
    {
        using var capture = new Capture(); Exception original = AiEndpointPolicy.Blocked();
        for (var i = 0; i < wrappers; i++) original = new HttpRequestException("wrapper-secret", original);
        using var invoker = Invoker((_, _) => Task.FromException<HttpResponseMessage>(original));
        using var request = Request();
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<HttpRequestException>(() => invoker.SendAsync(request, default)));
        capture.Verify("ollama", "chat", outcome, ActivityStatusCode.Error);
    }

    [TestMethod]
    public async Task Unknown_labels_and_unreadable_content_cannot_enter_telemetry()
    {
        using var capture = new Capture();
        using var body = new PoisonContent(); using var output = new PoisonContent();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = output, ReasonPhrase = "secret" };
        using var invoker = Invoker((_, _) => Task.FromResult(response), "tenant-user-secret", "prompt-model-secret");
        using var request = Request(); request.Content = body;
        request.Headers.Authorization = new("Bearer", "private-key");
        Assert.AreSame(response, await invoker.SendAsync(request, default));
        Assert.AreEqual(0, body.Reads); Assert.AreEqual(0, output.Reads);
        capture.Verify("other", "other", "succeeded", ActivityStatusCode.Ok);
    }

    [TestMethod]
    public async Task Sampling_none_preserves_metrics_without_creating_span()
    {
        using var capture = new Capture(sampleNone: true);
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var invoker = Invoker((_, _) => Task.FromResult(response)); using var request = Request();
        Assert.AreSame(response, await invoker.SendAsync(request, default));
        Assert.HasCount(2, capture.Measurements); Assert.HasCount(0, capture.Spans);
        Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Measurement_callback_failure_does_not_suppress_other_instrument_or_response(bool duration)
    {
        using var capture = new Capture(throwMeasurement: duration ? "duration" : "requests");
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var invoker = Invoker((_, _) => Task.FromResult(response)); using var request = Request();
        Assert.AreSame(response, await invoker.SendAsync(request, default));
        capture.Verify("ollama", "chat", "succeeded", ActivityStatusCode.Ok);
    }

    [TestMethod]
    [DataRow("sample")] [DataRow("started")] [DataRow("stopped")]
    public async Task Trace_callback_failure_preserves_network_result_and_parent(string callback)
    {
        using var capture = new Capture(throwTrace: callback);
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var invoker = Invoker((_, _) => Task.FromResult(response)); using var request = Request();
        Assert.AreSame(response, await invoker.SendAsync(request, default));
        Assert.HasCount(2, capture.Measurements);
        Assert.AreSame(capture.Root, Activity.Current);
        Assert.IsTrue(capture.Spans.All(s => s.TagObjects.All(t => t.Key is "ai.provider" or "ai.operation" or "ai.outcome")));
    }

    [TestMethod]
    public async Task Precancelled_attempt_is_observed_and_never_reaches_inner_handler()
    {
        using var capture = new Capture(); var calls = 0;
        using var invoker = Invoker((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); });
        using var request = Request(); using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => invoker.SendAsync(request, stop.Token));
        Assert.AreEqual(0, calls); capture.Verify("ollama", "chat", "cancelled", ActivityStatusCode.Error);
    }

    [TestMethod]
    public async Task Cancellation_while_waiting_headers_preserves_cancellation_and_records_once()
    {
        using var capture = new Capture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var invoker = Invoker(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return new HttpResponseMessage(); });
        using var request = Request(); using var stop = new CancellationTokenSource();
        var pending = invoker.SendAsync(request, stop.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.HasCount(0, capture.Measurements); stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        capture.Verify("ollama", "chat", "cancelled", ActivityStatusCode.Error);
    }

    [TestMethod]
    public async Task Observation_ends_at_headers_and_does_not_read_or_claim_body_success()
    {
        using var capture = new Capture();
        var gate = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var content = new PoisonContent(); using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        using var invoker = Invoker((_, _) => gate.Task); using var request = Request();
        var pending = invoker.SendAsync(request, default); Assert.HasCount(0, capture.Measurements);
        gate.SetResult(response); Assert.AreSame(response, await pending);
        capture.Verify("ollama", "chat", "succeeded", ActivityStatusCode.Ok); Assert.AreEqual(0, content.Reads);
        await Assert.ThrowsAsync<IOException>(() => response.Content.ReadAsStringAsync());
        Assert.HasCount(2, capture.Measurements);
    }

    private static HttpRequestMessage Request() => new(HttpMethod.Post, "https://tenant-secret.test/model-secret?key=private-key");
    private static HttpMessageInvoker Invoker(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action,
        string provider = "ollama", string operation = "chat") =>
        new(new AiProviderTelemetryHandler(provider, operation) { InnerHandler = new Stub(action) });
    private sealed class Stub(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, token); }
    private sealed class PoisonException : HttpRequestException
    {
        public override string Message => throw new InvalidOperationException("Message must not be read.");
        public override string ToString() => throw new InvalidOperationException("ToString must not be called.");
        public override int GetHashCode() => throw new InvalidOperationException("User hashing must not be called.");
        public override bool Equals(object? other) => throw new InvalidOperationException("User equality must not be called.");
    }
    private sealed class PoisonContent : HttpContent
    {
        internal int Reads;
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        { Reads++; return Task.FromException(new IOException("body-secret")); }
        protected override bool TryComputeLength(out long length) { Reads++; length = 0; throw new IOException("body-secret"); }
    }

    private sealed class Capture : IDisposable
    {
        internal readonly Activity Root = new Activity("test.root").SetIdFormat(ActivityIdFormat.W3C).Start();
        internal readonly List<(string Name, double Value, KeyValuePair<string, object?>[] Tags)> Measurements = [];
        internal readonly List<Activity> Spans = [];
        private readonly MeterListener meter = new();
        private readonly ActivityListener trace;

        internal Capture(bool sampleNone = false, string? throwMeasurement = null, string? throwTrace = null)
        {
            meter.InstrumentPublished = (instrument, listener) =>
            { if (instrument.Meter.Name == Name) listener.EnableMeasurementEvents(instrument); };
            meter.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            { if (Activity.Current?.TraceId == Root.TraceId) Measurements.Add((instrument.Name, value, tags.ToArray()));
                if (instrument.Name.EndsWith(throwMeasurement ?? "never", StringComparison.Ordinal)) throw new IOException("listener-secret"); });
            meter.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            { if (Activity.Current?.TraceId == Root.TraceId) Measurements.Add((instrument.Name, value, tags.ToArray()));
                if (instrument.Name.EndsWith(throwMeasurement ?? "never", StringComparison.Ordinal)) throw new IOException("listener-secret"); });
            meter.Start();
            trace = new ActivityListener
            {
                ShouldListenTo = source => source.Name == Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => throwTrace == "sample" ? throw new IOException("listener-secret")
                    : sampleNone ? ActivitySamplingResult.None : ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => { if (throwTrace == "started") throw new IOException("listener-secret"); },
                ActivityStopped = activity => { if (activity.TraceId == Root.TraceId) Spans.Add(activity);
                    if (throwTrace == "stopped") throw new IOException("listener-secret"); },
            };
            ActivitySource.AddActivityListener(trace);
        }

        internal void Verify(string provider, string operation, string outcome, ActivityStatusCode status)
        {
            Assert.HasCount(2, Measurements);
            Assert.AreEqual(1d, Measurements.Single(m => m.Name == "fullnet.ai.provider.http.requests").Value);
            Assert.IsTrue(Measurements.Single(m => m.Name == "fullnet.ai.provider.http.duration").Value >= 0);
            foreach (var measurement in Measurements) VerifyTags(measurement.Tags, provider, operation, outcome);
            Assert.HasCount(1, Spans);
            Assert.AreEqual("ai.provider.http", Spans[0].OperationName);
            Assert.AreEqual(ActivityKind.Client, Spans[0].Kind);
            Assert.AreEqual(Root.SpanId, Spans[0].ParentSpanId);
            Assert.AreEqual(status, Spans[0].Status);
            Assert.IsNull(Spans[0].StatusDescription);
            Assert.IsFalse(Spans[0].Events.Any());
            VerifyTags(Spans[0].TagObjects.ToArray(), provider, operation, outcome);
            Assert.AreSame(Root, Activity.Current);
        }

        private static void VerifyTags(KeyValuePair<string, object?>[] tags, string provider, string operation, string outcome)
        {
            Assert.HasCount(3, tags);
            Assert.AreEqual(provider, tags.Single(t => t.Key == "ai.provider").Value);
            Assert.AreEqual(operation, tags.Single(t => t.Key == "ai.operation").Value);
            Assert.AreEqual(outcome, tags.Single(t => t.Key == "ai.outcome").Value);
        }

        public void Dispose() { trace.Dispose(); meter.Dispose(); Root.Dispose(); }
    }
}
