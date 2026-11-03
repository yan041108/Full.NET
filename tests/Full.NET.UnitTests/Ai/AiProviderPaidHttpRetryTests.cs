using System.Net;
using Full.NET.AI.Providers.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Ai;

/// <summary>经过真实 HttpClientFactory 韧性链，收费请求不能被宿主的默认重试放大。</summary>
[TestClass]
public sealed class AiProviderPaidHttpRetryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Paid_channels_send_once_for_all_registered_channels(bool defaultsLast)
    {
        var services = new ServiceCollection();
        if (!defaultsLast) AddDefaults(services);
        services.AddAiProviderHttpClients(new ConfigurationBuilder().Build());
        if (defaultsLast) AddDefaults(services);
        foreach (var key in new[] { "openai_compatible", "ollama", "azure_openai" })
        foreach (var embedding in new[] { false, true })
        foreach (var privateData in new[] { false, true })
        foreach (var telemetry in new[] { false, true })
        {
            var name = AiProviderHttpClients.Name(key, false, embedding, privateData, telemetry);
            foreach (var failure in new[] { 503, 429, 0 })
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Get })
            {
                var transport = new FailedTransport(failure);
                // 只替换最终传输；保留宿主默认和命名客户端的实际韧性处理器。
                IServiceCollection channelServices = new ServiceCollection();
                foreach (var descriptor in services) channelServices.Add(descriptor);
                channelServices.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => transport);
                await using var root = channelServices.BuildServiceProvider();
                using var client = root.GetRequiredService<IHttpClientFactory>().CreateClient(name);
                using var request = new HttpRequestMessage(method, "https://provider.test/paid");
                if (failure == 0)
                    await Assert.ThrowsExactlyAsync<HttpRequestException>(() => client.SendAsync(request));
                else
                {
                    using var response = await client.SendAsync(request);
                    Assert.AreEqual((HttpStatusCode)failure, response.StatusCode);
                }
                Assert.AreEqual(1, transport.Attempts, name + ":" + failure + ":" + method);
            }
        }
    }

    [TestMethod]
    public async Task Connectivity_and_unrelated_get_keep_host_retries()
    {
        var services = new ServiceCollection();
        AddDefaults(services);
        services.AddAiProviderHttpClients(new ConfigurationBuilder().Build());
        var probe = new FailedTransport(503);
        var unrelated = new FailedTransport(503);
        var probeName = AiProviderHttpClients.Name("openai_compatible", true);
        services.AddHttpClient(probeName).ConfigurePrimaryHttpMessageHandler(() => probe);
        services.AddHttpClient("unrelated").ConfigurePrimaryHttpMessageHandler(() => unrelated);
        await using var root = services.BuildServiceProvider();
        foreach (var name in new[] { probeName, "unrelated" })
        {
            using var client = root.GetRequiredService<IHttpClientFactory>().CreateClient(name);
            using var response = await client.GetAsync("https://provider.test/models");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        Assert.AreEqual(4, probe.Attempts);
        Assert.AreEqual(4, unrelated.Attempts);
    }

    private static void AddDefaults(IServiceCollection services) => services.ConfigureHttpClientDefaults(builder =>
        builder.AddStandardResilienceHandler(options => { options.Retry.Delay = TimeSpan.Zero; options.Retry.UseJitter = false; }));

    private sealed class FailedTransport(int failure) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return failure == 0 ? Task.FromException<HttpResponseMessage>(new HttpRequestException("受控传输失败"))
                : Task.FromResult(new HttpResponseMessage((HttpStatusCode)failure));
        }
    }
}
