using System.Text.Json;
using Full.NET.Hosting.Api;
using Full.NET.Localization;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class MessagingProducerFenceHttpTests
{
    [TestMethod]
    [DataRow("zh-CN")]
    [DataRow("en-US")]
    public async Task Producer_fence_is_a_sanitized_retryable_http_failure(string locale)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddOptions();
        services.AddSingleton<ILocaleContext>(new Locale(locale));
        services.AddSingleton<IErrorMessageLocalizer>(new ResourceErrorMessageLocalizer([new MessagingErrorResourceSource()], new NamedMessageFormatter()));
        using var provider = services.BuildServiceProvider();
        var builder = new ApplicationBuilder(provider);
        ((IFullNetModule)new MessagingModule()).UseModuleMiddleware(builder, ModulePipelineStage.BeforeEndpoints);
        builder.Run(_ => throw new EventDeliveryProducerFencedException("fullnet.private.stream", 27));
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();
        await builder.Build()(context);
        Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.AreEqual("1", context.Response.Headers.RetryAfter.ToString());
        Assert.AreEqual(locale, context.Response.Headers.ContentLanguage.ToString());
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        Assert.AreEqual("messaging.delivery.producer_fenced", document.RootElement.GetProperty("code").GetString());
        Assert.IsFalse(body.Contains("fullnet.private.stream", StringComparison.Ordinal));
        Assert.IsFalse(body.Contains("schema 27", StringComparison.Ordinal));
    }
    private sealed record Locale(string CurrentLocale) : ILocaleContext;
}
