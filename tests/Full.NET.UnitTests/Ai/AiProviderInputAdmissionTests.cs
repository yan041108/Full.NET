using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Credentials;
using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.AzureOpenAI;
using Full.NET.AI.Providers.Http;
using Full.NET.AI.Providers.Ollama;
using Full.NET.AI.Providers.OpenAI;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>没有可信 Chat 请求计量时严格模式必须拒绝，任何供应商都不能用估算降级。</summary>
[TestClass]
public sealed class AiProviderInputAdmissionTests
{
    [TestMethod]
    [DataRow("openai_compatible")] [DataRow("ollama")] [DataRow("azure_openai")]
    public async Task Strict_input_policy_rejects_before_credentials_or_client_creation(string key)
    {
        var http = Substitute.For<IHttpClientFactory>(); var credentials = Substitute.For<IProtectedModelCredentialStore>();
        var protection = new EphemeralDataProtectionProvider(); var strict = new AiChatInputAdmissionPolicy(true);
        IAiModelClientFactory factory = key switch
        {
            "openai_compatible" => new OpenAiModelClientFactory(http, protection, credentials, new OpenAiGatewayPolicy(new ConfigurationBuilder().Build()), inputPolicy: strict),
            "ollama" => new OllamaModelClientFactory(http, protection, credentials, inputPolicy: strict),
            _ => new AzureOpenAiModelClientFactory(http, protection, credentials, inputPolicy: strict),
        };
        var error = await Assert.ThrowsExactlyAsync<AiBudgetException>(async () =>
        { using var client = await factory.CreateChatClientAsync(new(Guid.CreateVersion7(), 1, key, "model", new Uri("https://provider.test"), null), default); });
        Assert.AreEqual("ai.budget.hard_input_unavailable", error.Code);
        Assert.HasCount(0, credentials.ReceivedCalls().ToArray()); Assert.HasCount(0, http.ReceivedCalls().ToArray());
    }

    [TestMethod]
    [DataRow("true", true)] [DataRow("false", false)]
    public void Host_boolean_is_frozen_and_compatibility_mode_is_explicit(string value, bool required)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FullNet:Ai:InputAdmission:RequireExactChatTokens"] = value }).Build();
        var services = new ServiceCollection(); services.AddAiProviderHttpClients(config); config["FullNet:Ai:InputAdmission:RequireExactChatTokens"] = "false";
        using var provider = services.BuildServiceProvider(); var policy = provider.GetRequiredService<AiChatInputAdmissionPolicy>();
        Assert.AreEqual(required, policy.RequireExactTokens);
        if (required) Assert.ThrowsExactly<AiBudgetException>(policy.RequireChatDispatch);
        else policy.RequireChatDispatch();
    }

    [TestMethod]
    [DataRow("", "true")] [DataRow(":RequireExactChatTokens", "")]
    [DataRow(":RequireExactChatTokens", "estimate")] [DataRow(":RequireExactChatTokens:0", "true")]
    [DataRow(":Unsupported", "true")]
    public void Invalid_explicit_input_policy_rejects_registration(string suffix, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FullNet:Ai:InputAdmission" + suffix] = value }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddAiProviderHttpClients(config));
    }
}
