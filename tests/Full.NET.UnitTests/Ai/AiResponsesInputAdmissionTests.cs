using System.Net;
using System.Text.Json;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.OpenAI;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>真实Provider与受控HTTP证明同协议计数、冻结请求及计数后的失败关闭；不访问外部模型。</summary>
[TestClass]
public sealed class AiResponsesInputAdmissionTests
{
    [TestMethod]
    public async Task Count_and_generation_have_identical_frozen_input_and_real_receipt()
    {
        using var handler = new Handler(); using var client = await CreateAsync(handler);
        using var scope = AiChatInputBudgetScope.Begin(10, _ => Task.CompletedTask);
        var result = await client.GetResponseAsync([new(ChatRole.User, "hello")], new() { MaxOutputTokens = 37 });
        Assert.HasCount(2, handler.Bodies);
        using var count = JsonDocument.Parse(handler.Bodies[0]); using var generation = JsonDocument.Parse(handler.Bodies[1]);
        Assert.AreEqual(count.RootElement.GetProperty("input").GetRawText(), generation.RootElement.GetProperty("input").GetRawText());
        Assert.IsFalse(generation.RootElement.GetProperty("store").GetBoolean());
        Assert.AreEqual(37, generation.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.AreEqual("ok", result.Text); Assert.AreEqual(7L, result.Usage!.InputTokenCount);
        CollectionAssert.AreEqual(new[] { "/v1/responses/input_tokens", "/v1/responses" }, handler.Paths);
    }

    [TestMethod]
    public async Task Count_over_limit_never_sends_a_generation_request()
    {
        using var handler = new Handler(); using var client = await CreateAsync(handler);
        using var scope = AiChatInputBudgetScope.Begin(6, _ => Task.CompletedTask);
        var error = await Assert.ThrowsExactlyAsync<AiBudgetException>(() => client.GetResponseAsync([new(ChatRole.User, "hello")]));
        Assert.AreEqual("ai.budget.limit_exceeded", error.Code); Assert.HasCount(1, handler.Paths);
    }

    [TestMethod]
    public async Task Authority_revoked_during_count_prevents_generation()
    {
        using var handler = new Handler(); using var client = await CreateAsync(handler); var allowed = true;
        handler.AfterCount = () => allowed = false;
        using var scope = AiChatInputBudgetScope.Begin(10, _ => allowed ? Task.CompletedTask : throw new UnauthorizedAccessException());
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => client.GetResponseAsync([new(ChatRole.User, "hello")]));
        Assert.HasCount(1, handler.Paths);
    }

    [TestMethod]
    public async Task Cancellation_after_count_prevents_generation()
    {
        using var handler = new Handler(); using var client = await CreateAsync(handler); using var cancellation = new CancellationTokenSource();
        handler.AfterCount = cancellation.Cancel;
        using var scope = AiChatInputBudgetScope.Begin(10, _ => Task.CompletedTask);
        await Assert.ThrowsAsync<OperationCanceledException>(() => client.GetResponseAsync([new(ChatRole.User, "hello")], cancellationToken: cancellation.Token));
        Assert.HasCount(1, handler.Paths);
    }

    [TestMethod]
    public async Task Caller_history_mutation_during_count_does_not_change_generation()
    {
        using var handler = new Handler(); using var client = await CreateAsync(handler); var message = new ChatMessage(ChatRole.User, "original");
        handler.AfterCount = () => { message.Contents.Clear(); message.Contents.Add(new TextContent("changed")); };
        using var scope = AiChatInputBudgetScope.Begin(10, _ => Task.CompletedTask);
        await client.GetResponseAsync([message]);
        using var generated = JsonDocument.Parse(handler.Bodies[1]);
        Assert.AreEqual("original", generated.RootElement.GetProperty("input")[0].GetProperty("content").GetString());
    }

    [TestMethod]
    [DataRow("{\"object\":\"wrong\",\"input_tokens\":7}")]
    [DataRow("{\"object\":\"response.input_tokens\",\"input_tokens\":-1}")]
    [DataRow("{\"object\":\"response.input_tokens\",\"input_tokens\":7.5}")]
    [DataRow("{\"object\":\"response.input_tokens\",\"input_tokens\":\"7\"}")]
    [DataRow("{\"object\":\"response.input_tokens\"}")]
    [DataRow("{\"object\":\"response.input_tokens\",\"input_tokens\":7,\"input_tokens\":1}")]
    public async Task Invalid_counter_result_never_authorizes_generation(string payload)
    {
        using var handler = new Handler { CountJson = payload }; using var client = await CreateAsync(handler);
        using var scope = AiChatInputBudgetScope.Begin(10, _ => Task.CompletedTask);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => client.GetResponseAsync([new(ChatRole.User, "hello")]));
        Assert.HasCount(1, handler.Paths);
    }

    [TestMethod]
    public async Task Missing_budget_scope_does_not_send_source_to_counter()
    {
        using var handler = new Handler(); using var client = await CreateAsync(handler);
        await Assert.ThrowsExactlyAsync<AiBudgetException>(() => client.GetResponseAsync([new(ChatRole.User, "hello")]));
        Assert.HasCount(0, handler.Paths);
    }

    [TestMethod]
    public async Task Count_is_not_substituted_for_missing_billing_usage()
    {
        using var handler = new Handler { CompletionUsage = "{\"input_tokens\":null,\"output_tokens\":1}" }; using var client = await CreateAsync(handler);
        using var scope = AiChatInputBudgetScope.Begin(10, _ => Task.CompletedTask);
        var result = await client.GetResponseAsync([new(ChatRole.User, "hello")]);
        Assert.IsNull(result.Usage!.InputTokenCount); Assert.AreEqual(1L, result.Usage.OutputTokenCount);
    }

    private static async Task<IChatClient> CreateAsync(Handler handler)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FullNet:Ai:OpenAiGateways:0:Endpoint"] = "https://provider.test/v1",
            ["FullNet:Ai:OpenAiGateways:0:ModelId"] = "approved-model",
            ["FullNet:Ai:OpenAiGateways:0:SupportsStreamingUsage"] = "false",
            ["FullNet:Ai:OpenAiGateways:0:SupportsResponsesInputTokens"] = "true",
        }).Build();
        var http = Substitute.For<IHttpClientFactory>(); http.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler, false));
        var admission = new AiChatInputAdmissionPolicy(true, [new("openai_compatible", "approved-model")]);
        using var credentials = new AiModelBindingScope(inputPolicy: admission); var protection = new EphemeralDataProtectionProvider();
        var factory = new OpenAiModelClientFactory(http, protection, credentials, new(config), inputPolicy: admission);
        return await factory.CreateChatClientAsync(credentials.Create(new AiModelConfigRecord { Id = Guid.CreateVersion7(), Version = 1,
            ProviderKey = "openai_compatible", ModelId = "approved-model", EndpointBaseUrl = "https://provider.test/v1",
            ApiKeyProtected = protection.CreateProtector("Full.NET.Ai.ModelConfigApiKey.v1").Protect("test-key") }), default);
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<string> Paths = []; internal readonly List<string> Bodies = [];
        internal Action? AfterCount;
        internal string CountJson = "{\"object\":\"response.input_tokens\",\"input_tokens\":7}";
        internal string CompletionUsage = "{\"input_tokens\":7,\"output_tokens\":1}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath); Bodies.Add(await request.Content!.ReadAsStringAsync(token));
            if (request.RequestUri.AbsolutePath.EndsWith("input_tokens", StringComparison.Ordinal))
            { AfterCount?.Invoke(); return new(HttpStatusCode.OK) { Content = new StringContent(CountJson) }; }
            var stream = "data: {\"type\":\"response.output_text.delta\",\"delta\":\"ok\"}\n\n"
                + "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"model\":\"approved-model\",\"usage\":" + CompletionUsage + "}}\n\n";
            return new(HttpStatusCode.OK) { Content = new StringContent(stream) };
        }
    }
}
