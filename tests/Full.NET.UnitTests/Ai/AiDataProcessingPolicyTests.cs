using Full.NET.AI.Abstractions.Models;
using Full.NET.AI.Providers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>宿主配置必须冻结且失败关闭，不把未知分类、阶段或数组形状降级为兼容许可。</summary>
[TestClass]
public sealed class AiDataProcessingPolicyTests
{
    [TestMethod]
    public void Legacy_binding_constructor_and_deconstruction_preserve_restricted_default()
    {
        var binding = new ModelBinding(Guid.CreateVersion7(), 1, "ollama", "model", new("https://provider.test"), null);
        var (id, version, provider, model, endpoint, reference, options) = binding;
        Assert.AreEqual(binding.ConfigId, id); Assert.AreEqual(1, version); Assert.AreEqual("restricted", binding.DataClassification);
        Assert.AreEqual("ollama", provider); Assert.AreEqual("model", model); Assert.AreEqual(binding.Endpoint, endpoint);
        Assert.IsNull(reference); Assert.IsNull(options);
        Assert.AreEqual("Full.NET.Ai.ollama.Chat", AiProviderHttpClients.Name("ollama", false));
    }
    [TestMethod]
    [DataRow("chat")] [DataRow("embedding")] [DataRow("rerank")]
    [DataRow("context")] [DataRow("telemetry")] [DataRow("connectivity")]
    public void Explicit_stage_list_restricts_only_that_stage_and_freezes_input(string stage)
    {
        var list = new[] { "public" };
        var settings = new Dictionary<string, IReadOnlyList<string>> { [stage] = list };
        var privateClasses = new[] { "restricted" };
        var policy = new AiDataProcessingPolicy(settings, privateClasses);
        list[0] = "restricted"; settings.Clear(); privateClasses[0] = "public";
        Assert.IsTrue(policy.Allows(stage, "public")); Assert.IsFalse(policy.Allows(stage, "restricted"));
        Assert.IsTrue(policy.RequiresPrivateNetwork("restricted")); Assert.IsFalse(policy.RequiresPrivateNetwork("public"));
        Assert.ThrowsExactly<InvalidOperationException>(() => policy.Require(stage, "restricted"));
    }

    [TestMethod]
    [DataRow("unknown")] [DataRow("INTERNAL")] [DataRow("internal ")] [DataRow("")] [DataRow(null)]
    public void Unknown_classification_is_not_public(string? classification)
    {
        Assert.IsFalse(AiDataProcessingPolicy.Default.Allows("chat", classification!));
        Assert.ThrowsExactly<InvalidOperationException>(() => AiDataProcessingPolicy.Strongest(["public", classification!]));
        Assert.ThrowsExactly<InvalidOperationException>(() => AiDataProcessingPolicy.Default.RequiresPrivateNetwork(classification!));
    }

    [TestMethod]
    public void Explicit_empty_list_denies_and_all_sources_affect_strongest()
    {
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["chat"] = [] });
        Assert.IsFalse(policy.Allows("chat", "public"));
        Assert.AreEqual("restricted", AiDataProcessingPolicy.Strongest(["public", "internal", "restricted", "public"]));
        Assert.AreEqual("restricted", AiDataProcessingPolicy.Strongest(["public"], "restricted"));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["Chat"] = ["public"] }));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AiDataProcessingPolicy(privateClassifications: ["public", "public"]));
    }

    [TestMethod]
    [DataRow("chat", "public")] [DataRow("embedding", "internal")] [DataRow("connectivity", "restricted")]
    public void Named_client_applies_stage_network_and_telemetry_policy(string stage, string classification)
    {
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["telemetry"] = [] }, [classification]);
        Assert.AreEqual(AiProviderHttpClients.Name("ollama", stage == "connectivity", stage == "embedding", true, false),
            AiProviderHttpClients.NameForData("ollama", stage, classification, policy));
    }

    [TestMethod]
    [DataRow("Unknown:0", "public")] [DataRow("chat", "public")]
    [DataRow("chat:1", "public")] [DataRow("chat:0:unexpected", "public")]
    [DataRow("chat:0", "INTERNAL")] [DataRow("PrivateClassifications:0", "unknown")]
    public void Invalid_host_configuration_rejects_registration(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FullNet:Ai:DataPolicy:" + key] = value }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddAiProviderHttpClients(configuration));
    }

    [TestMethod]
    public void Scalar_root_cannot_restore_open_defaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FullNet:Ai:DataPolicy"] = "restricted" }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddAiProviderHttpClients(configuration));
    }

    [TestMethod]
    public void Registration_freezes_configuration_and_registers_all_bounded_clients()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FullNet:Ai:DataPolicy:chat:0"] = "public", ["FullNet:Ai:DataPolicy:PrivateClassifications:0"] = "restricted" }).Build();
        var services = new ServiceCollection(); services.AddAiProviderHttpClients(configuration);
        using var provider = services.BuildServiceProvider(); configuration["FullNet:Ai:DataPolicy:chat:0"] = "restricted";
        var policy = provider.GetRequiredService<AiDataProcessingPolicy>();
        Assert.IsTrue(policy.Allows("chat", "public")); Assert.IsFalse(policy.Allows("chat", "restricted"));
        var http = provider.GetRequiredService<IHttpClientFactory>();
        foreach (var key in new[] { "openai_compatible", "ollama", "azure_openai" })
        foreach (var stage in new[] { "chat", "embedding", "connectivity" })
        foreach (var privateData in new[] { false, true })
        foreach (var telemetry in new[] { false, true })
        {
            using var client = http.CreateClient(AiProviderHttpClients.Name(key, stage == "connectivity", stage == "embedding", privateData, telemetry));
            Assert.AreEqual(stage == "connectivity" ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(3), client.Timeout);
        }
    }
}
