using Full.NET.Modules.Identity.DependencyInjection;
using Full.NET.Modules.Identity.Oidc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Identity;

/// <summary>真实客户端工厂及宿主策略保留，仅模拟传输失败以检查协议重放边界。</summary>
[TestClass]
public sealed class IdentityProtocolHttpSafetyTests
{
    [TestMethod]
    [DataRow("OpenIddict.Client.SystemNetHttp:single-use", true)]
    [DataRow(IdentityOidcLogoutDelivery.HttpClientName, true)]
    [DataRow("ordinary-client", false)]
    public async Task Protocol_clients_send_once_while_ordinary_clients_keep_standard_retries(string clientName, bool protocolClient)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpClientDefaults(client => client.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 2;
            options.Retry.Delay = TimeSpan.Zero;
            options.Retry.UseJitter = false;
        }));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Identity:Oidc:Enable"] = "true" }).Build();
        services.AddIdentityOidcLogout(configuration, worker: true);
        var transport = new FailingTransport();
        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();
        using var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        using var content = new StringContent("single-use-test-value");

        await Assert.ThrowsAsync<HttpRequestException>(() => http.PostAsync("https://identity.invalid/protocol", content));

        Assert.AreEqual(protocolClient ? 1 : 3, transport.Attempts);
    }

    private sealed class FailingTransport : HttpMessageHandler
    {
        private int attempts;
        internal int Attempts => attempts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("模拟响应丢失，不连接外部服务。"));
        }
    }
}
