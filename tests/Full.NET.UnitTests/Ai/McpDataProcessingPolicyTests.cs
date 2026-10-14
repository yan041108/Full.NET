using Full.NET.AI.Abstractions.Models;
using Full.NET.AgenticWeb.Mcp.Client;
using Full.NET.Agents.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>远端工具不能绕过 restricted 分类去向、私网与遥测策略。</summary>
[TestClass]
public sealed class McpDataProcessingPolicyTests
{
    [TestMethod]
    public async Task Denied_mcp_stage_does_not_read_credentials_or_advertise_executable_tools()
    {
        var http = Substitute.For<IHttpClientFactory>();
        var catalog = Substitute.For<IMcpRemoteToolCatalog>();
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["mcp"] = ["public"] });
        await using var manager = new McpClientConnectionManager(http, Options.Create(new McpClientOptions { DataPolicy = policy }));

        var definitions = await new McpRemoteToolAdapter(catalog, manager).BuildDefinitionsAsync(default);

        Assert.HasCount(0, definitions);
        Assert.HasCount(0, catalog.ReceivedCalls().ToArray());
        Assert.HasCount(0, http.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public async Task Denied_mcp_stage_never_creates_http_client()
    {
        var http = Substitute.For<IHttpClientFactory>();
        var policy = new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["mcp"] = ["public"] });
        await using var manager = new McpClientConnectionManager(http, Options.Create(new McpClientOptions
            { ApprovedOrigins = ["https://provider.test"], DataPolicy = policy }));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => manager.GetClientAsync(Guid.CreateVersion7(), new("https://provider.test"), "secret", default).AsTask());
        Assert.HasCount(0, http.ReceivedCalls().ToArray());
    }

    [TestMethod]
    public void Private_classification_strengthens_frozen_mcp_options()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FullNet:Ai:Mcp:Client:ApprovedOrigins:0"] = "https://10.2.3.4" }).Build();
        var policy = new AiDataProcessingPolicy(privateClassifications: ["restricted"]);
        var services = new ServiceCollection(); services.AddFullNetMcpClient(config, policy);
        using var provider = services.BuildServiceProvider(); var options = provider.GetRequiredService<IOptions<McpClientOptions>>().Value;
        Assert.IsTrue(options.PrivateOnly); Assert.AreSame(policy, options.DataPolicy);
    }

    [TestMethod]
    public void Explicit_data_policy_without_frozen_host_binding_refuses_standalone_registration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["FullNet:Ai:DataPolicy:mcp:0"] = "public" }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddFullNetMcpClient(config));
    }
}
