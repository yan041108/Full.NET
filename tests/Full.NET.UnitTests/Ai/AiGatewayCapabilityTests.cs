using System.Net;
using System.Text.Json;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.AI.Providers.OpenAI;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiGatewayCapabilityTests
{
    [TestMethod]
    public async Task Unknown_gateway_does_not_request_optional_streaming_usage()
    {
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(client);
        var result = await TestAiProviders.Streamer(http).StreamAsync(Model(), [], _ => Task.CompletedTask);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.IsFalse(payload.RootElement.TryGetProperty("stream_options", out _));
        Assert.IsNull(result.PromptTokens);
        Assert.IsNull(result.CompletionTokens);
    }

    [TestMethod]
    [DataRow("https://gateway.test/v1/", "test", "true", true)]
    [DataRow("https://gateway.test/v1", "test", "false", false)]
    [DataRow("https://gateway.test/v2", "test", "true", false)]
    [DataRow("https://other.test/v1", "test", "true", false)]
    [DataRow("https://gateway.test/v1", "another", "true", false)]
    [DataRow("https://gateway.test/v1", "TEST", "true", false)]
    public async Task Usage_extension_requires_exact_explicit_capability(string endpoint, string model, string enabled, bool expected)
    {
        var config = Values(endpoint, model, enabled);
        var policy = new OpenAiGatewayPolicy(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        var handler = new Handler();
        using var client = new HttpClient(handler);
        var http = Substitute.For<IHttpClientFactory>();
        http.CreateClient(Arg.Any<string>()).Returns(client);
        await TestAiProviders.Streamer(http, policy).StreamAsync(Model(), [], _ => Task.CompletedTask);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual(expected, payload.RootElement.TryGetProperty("stream_options", out var options));
        if (expected) Assert.IsTrue(options.GetProperty("include_usage").GetBoolean());
        Assert.AreEqual(4096, payload.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [TestMethod]
    [DataRow("Endpoint", "http://gateway.test/v1")]
    [DataRow("Endpoint", "https://user:secret@gateway.test/v1")]
    [DataRow("Endpoint", "https://gateway.test/v1?query=1")]
    [DataRow("Endpoint", "https://gateway.test/v1#fragment")]
    [DataRow("ModelId", "*")]
    [DataRow("ModelId", " ")]
    [DataRow("SupportsStreamingUsage", "yes")]
    [DataRow("SupportsStreamingUsage", null)]
    [DataRow("SupportsTools", "true")]
    public void Invalid_or_unknown_capabilities_fail_during_configuration(string field, string? value)
    {
        var values = Values();
        values[$"FullNet:Ai:OpenAiGateways:0:{field}"] = value;
        Assert.ThrowsExactly<InvalidOperationException>(() => new OpenAiGatewayPolicy(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    [TestMethod]
    public void Duplicate_normalized_bindings_are_rejected()
    {
        var values = Values();
        values["FullNet:Ai:OpenAiGateways:1:Endpoint"] = "https://gateway.test/v1/";
        values["FullNet:Ai:OpenAiGateways:1:ModelId"] = "test";
        values["FullNet:Ai:OpenAiGateways:1:SupportsStreamingUsage"] = "false";
        Assert.ThrowsExactly<InvalidOperationException>(() => new OpenAiGatewayPolicy(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
    }

    private static Dictionary<string, string?> Values(string endpoint = "https://gateway.test/v1", string model = "test", string enabled = "true") => new()
    {
        ["FullNet:Ai:OpenAiGateways:0:Endpoint"] = endpoint,
        ["FullNet:Ai:OpenAiGateways:0:ModelId"] = model,
        ["FullNet:Ai:OpenAiGateways:0:SupportsStreamingUsage"] = enabled,
    };

    private static AiModelConfigRecord Model() => new()
    {
        Id = Guid.NewGuid(), Version = 1, IsEnabled = true, ProviderKey = "openai_compatible",
        ModelId = "test", EndpointBaseUrl = "https://gateway.test/v1", ApiKeyProtected = TestAiProviders.Protect("test-key"),
    };

    private sealed class Handler : HttpMessageHandler
    {
        internal string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("data: [DONE]\n\n") };
        }
    }
}
