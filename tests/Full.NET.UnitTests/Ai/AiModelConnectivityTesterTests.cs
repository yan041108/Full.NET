using Full.NET.Modules.Ai.Security;
using System.Collections.Concurrent;
using System.Net;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Connectivity;
using Full.NET.Modules.Ai.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiModelConnectivityTesterTests
{
    [TestMethod]
    [DataRow("{\"models\":[{\"name\":\"llama3-other\"}]}")]
    [DataRow("{\"message\":\"llama3\"}")]
    [DataRow("invalid llama3 catalog")]
    public async Task Catalog_requires_an_exact_structured_model_entry(string body)
    {
        var (tester, _) = Create(body);
        Assert.IsFalse((await tester.TestAsync(Model("ollama", "llama3"))).Succeeded);
    }

    [TestMethod]
    public void Connectivity_orchestration_does_not_depend_on_http_transport()
    {
        Assert.IsFalse(typeof(AiModelConnectivityTester).GetConstructors()
            .SelectMany(item => item.GetParameters()).Any(item => item.ParameterType == typeof(IHttpClientFactory)));
    }

    [TestMethod]
    [DataRow("openai_compatible", "{\"data\":[{\"id\":\"test\"}]}")]
    [DataRow("ollama", "{\"models\":[{\"name\":\"test\"}]}")]
    public async Task Exact_model_in_provider_catalog_succeeds(string provider, string body)
    {
        var (tester, handler) = Create(body);
        Assert.IsTrue((await tester.TestAsync(Model(provider))).Succeeded);
        Assert.AreEqual(1, handler.Disposals);
    }

    [TestMethod]
    public async Task Trailing_slash_does_not_duplicate_catalog_separator()
    {
        var (tester, handler) = Create("{\"data\":[]}");
        Assert.IsTrue((await tester.TestAsync(Model("openai_compatible"))).Succeeded);
        Assert.AreEqual("https://provider.test/v1/models", handler.Requests.Single().Uri);
    }

    [TestMethod]
    [DataRow("openai_compatible", "{\"models\":[{\"name\":\"test\"}]}")]
    [DataRow("ollama", "{\"data\":[{\"id\":\"test\"}]}")]
    [DataRow("openai_compatible", "<html>test</html>")]
    [DataRow("openai_compatible", "{\"data\":[null]}")]
    [DataRow("ollama", "{\"models\":[{\"name\":17}]}")]
    public async Task Malformed_or_other_protocol_catalog_is_not_success(string provider, string body)
    {
        var (tester, handler) = Create(body);
        var result = await tester.TestAsync(Model(provider));
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(1, handler.Disposals);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("corrupted-protected-credential")]
    public async Task Missing_or_invalid_openai_credential_never_dispatches(string? credential)
    {
        var (tester, handler) = Create("{\"data\":[]}");
        var model = new AiModelConfigRecord
        {
            ProviderKey = "openai_compatible", ModelId = "test",
            EndpointBaseUrl = "https://provider.test/v1", ApiKeyProtected = credential,
        };
        var result = await tester.TestAsync(model);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, handler.Requests.Count);
        Assert.DoesNotContain("corrupted-protected-credential", result.Message, StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task Concurrent_configurations_and_rotated_key_keep_request_credentials_isolated()
    {
        var handler = new Handler("{\"data\":[]}");
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var tester = TestAiProviders.Tester(http);
        var first = CredentialModel("first", "first-key");
        var second = CredentialModel("second", "second-key");
        await Task.WhenAll(tester.TestAsync(first), tester.TestAsync(second));
        await tester.TestAsync(CredentialModel("first", "rotated-key", first.Id, 2));
        CollectionAssert.AreEquivalent(new[]
        {
            new SentRequest("https://first.test/v1/models", "Bearer first-key", "first-org"),
            new SentRequest("https://second.test/v1/models", "Bearer second-key", "second-org"),
            new SentRequest("https://first.test/v1/models", "Bearer rotated-key", "first-org"),
        }, handler.Requests.ToArray());
    }

    [TestMethod]
    public async Task In_flight_cancellation_releases_client_and_remains_cancellation()
    {
        var handler = new Handler("{}", waitForCancellation: true);
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        var tester = TestAiProviders.Tester(http);
        using var cancellation = new CancellationTokenSource();
        var pending = tester.TestAsync(Model("ollama"), cancellation.Token);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.AreEqual(1, handler.Disposals);
    }

    [TestMethod]
    public async Task Unknown_provider_never_dispatches()
    {
        var (tester, handler) = Create("{}");
        Assert.IsFalse((await tester.TestAsync(Model("unregistered"))).Succeeded);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    [DataRow("openai_compatible")]
    [DataRow("ollama")]
    public async Task Provider_rejects_mismatched_binding_before_network(string provider)
    {
        var http = Substitute.For<IHttpClientFactory>();
        var probe = TestAiProviders.CreateProbes(http, new AiModelBindingScope()).Single(item => item.ProviderKey == provider);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => probe.TestConnectivityAsync(
            new ModelBinding(Guid.NewGuid(), 1, "wrong", "test", new Uri("https://provider.test"), null)));
        http.DidNotReceive().CreateClient(Arg.Any<string>());
    }

    private static (AiModelConnectivityTester Tester, Handler Handler) Create(string body)
    {
        var handler = new Handler(body);
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        return (TestAiProviders.Tester(http), handler);
    }

    private static AiModelConfigRecord Model(string provider, string modelId = "test") => new()
    {
        Id = Guid.NewGuid(), Version = 1, ProviderKey = provider, ModelId = modelId,
        EndpointBaseUrl = "https://provider.test/v1/", ApiKeyProtected = TestAiProviders.Protect("test-key"),
    };

    private static AiModelConfigRecord CredentialModel(string name, string key, Guid? id = null, int version = 1) => new()
    {
        Id = id ?? Guid.NewGuid(), Version = version, ProviderKey = "openai_compatible", ModelId = "test",
        EndpointBaseUrl = $"https://{name}.test/v1", ApiKeyProtected = TestAiProviders.Protect(key), OrganizationId = $"{name}-org",
    };

    private sealed record SentRequest(string Uri, string? Authorization, string? Organization);

    /// <summary>模拟共享连接池并捕获逐请求头；用取消信号验证真实挂起请求的清理。</summary>
    private sealed class Handler(string body, bool waitForCancellation = false) : HttpMessageHandler
    {
        internal ConcurrentBag<SentRequest> Requests { get; } = [];
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("OpenAI-Organization", out var values) ? values.Single() : null));
            Entered.TrySetResult();
            await Task.Yield();
            if (waitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposals++;
            base.Dispose(disposing);
        }
    }
}
