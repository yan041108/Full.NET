using System.Net;
using System.Text.Json;
using Full.NET.AgenticWeb.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Full.NET.UnitTests.Ai;

/// <summary>发现元数据必须来自可用的真实发行配置，不能凭请求 Host 拼造。</summary>
[TestClass]
public sealed class McpMetadataConfigurationTests
{
    [TestMethod]
    [DataRow("https://issuer.example/identity", "https://mcp.example/api/v1/ai/mcp", true)]
    [DataRow("http://issuer.example/identity", "https://mcp.example/api/v1/ai/mcp", false)]
    [DataRow("https://issuer.example/identity", "fullnet://ai/mcp", false)]
    public async Task Metadata_is_only_published_for_valid_https_configuration(string issuer, string resource, bool valid)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:Oidc:Enable"] = "true",
            ["Identity:Oidc:Issuer"] = issuer,
        });
        builder.Services.AddOptions<McpAuthorizationOptions>().Configure(options => options.ResourceIdentifier = resource);
        await using var app = builder.Build();
        McpProtectedResourceMetadataEndpoint.Map(app);
        await app.StartAsync();
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.Single()) };
            using var response = await client.GetAsync("/.well-known/oauth-protected-resource/ai/mcp");
            Assert.AreEqual(valid ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            if (valid)
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.AreEqual(resource, body.RootElement.GetProperty("resource").GetString());
                Assert.AreEqual(issuer, body.RootElement.GetProperty("authorization_servers")[0].GetString());
            }
        }
        finally { await app.StopAsync(); }
    }
}
