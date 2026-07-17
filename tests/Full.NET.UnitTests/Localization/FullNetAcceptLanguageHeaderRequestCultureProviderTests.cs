using System.Globalization;
using Full.NET.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class FullNetAcceptLanguageHeaderRequestCultureProviderTests
{
    [TestMethod]
    [DataRow("en-GB", "en-US")]
    [DataRow("zh-Hans", "zh-CN")]
    public async Task Provider_maps_registered_aliases_to_canonical_locales(
        string requestedLocale,
        string expectedLocale)
    {
        var (provider, _) = CreateProvider();
        var context = CreateHttpContext(requestedLocale);

        var result = await provider.DetermineProviderCultureResult(context);

        Assert.IsNotNull(result);
        Assert.AreEqual(expectedLocale, result.Cultures[0].Value);
        Assert.AreEqual(expectedLocale, result.UICultures[0].Value);
    }

    [TestMethod]
    public async Task Provider_preserves_quality_order_and_skips_unknown_candidates()
    {
        var (provider, _) = CreateProvider();
        var context = CreateHttpContext("fr-FR,zh-Hans;q=0.3,en-GB;q=0.9");

        var result = await provider.DetermineProviderCultureResult(context);

        Assert.IsNotNull(result);
        Assert.AreEqual("en-US", result.Cultures[0].Value);
        Assert.AreEqual("zh-CN", result.Cultures[1].Value);
    }

    [TestMethod]
    public async Task Provider_applies_the_standard_candidate_count_bound_before_sorting()
    {
        var (provider, _) = CreateProvider();
        provider.MaximumAcceptLanguageHeaderValuesToTry = 1;
        var context = CreateHttpContext("fr-FR;q=0.1,en-US;q=1.0");

        var result = await provider.DetermineProviderCultureResult(context);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void Registration_keeps_only_the_bounded_accept_language_provider()
    {
        var (provider, options) = CreateProvider();

        Assert.AreEqual(1, options.RequestCultureProviders.Count);
        Assert.AreSame(provider, options.RequestCultureProviders[0]);
        Assert.IsInstanceOfType<AcceptLanguageHeaderRequestCultureProvider>(provider);
        Assert.AreEqual("zh-CN", options.DefaultRequestCulture.Culture.Name);
        CollectionAssert.AreEqual(
            new[] { "zh-CN", "en-US" },
            options.SupportedCultures!.Select(culture => culture.Name).ToArray());
    }

    [TestMethod]
    public async Task Middleware_uses_default_when_every_accept_language_candidate_is_unknown()
    {
        var observedLocale = await ExecutePipelineAsync(
            acceptLanguage: "fr-FR,de-DE;q=0.9",
            queryString: null,
            cookie: null);

        Assert.AreEqual("zh-CN", observedLocale);
    }

    [TestMethod]
    public async Task Middleware_ignores_query_and_cookie_culture_sources()
    {
        var observedLocale = await ExecutePipelineAsync(
            acceptLanguage: null,
            queryString: "?culture=en-US&ui-culture=en-US",
            cookie: ".AspNetCore.Culture=c=en-US|uic=en-US");

        Assert.AreEqual("zh-CN", observedLocale);
    }

    private static (
        FullNetAcceptLanguageHeaderRequestCultureProvider Provider,
        RequestLocalizationOptions Options) CreateProvider()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddFullNetLocalization()
            .BuildServiceProvider();
        var options = services
            .GetRequiredService<IOptions<RequestLocalizationOptions>>()
            .Value;
        var provider = Assert.IsInstanceOfType<
            FullNetAcceptLanguageHeaderRequestCultureProvider>(
                options.RequestCultureProviders.Single());
        return (provider, options);
    }

    private static DefaultHttpContext CreateHttpContext(string acceptLanguage)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = acceptLanguage;
        return context;
    }

    private static async Task<string> ExecutePipelineAsync(
        string? acceptLanguage,
        string? queryString,
        string? cookie)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddFullNetLocalization()
            .BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        string? observedLocale = null;
        builder.UseFullNetLocalization();
        builder.Run(context =>
        {
            observedLocale = CultureInfo.CurrentUICulture.Name;
            return Task.CompletedTask;
        });

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
        };
        if (acceptLanguage is not null)
        {
            httpContext.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        if (queryString is not null)
        {
            httpContext.Request.QueryString = new QueryString(queryString);
        }

        if (cookie is not null)
        {
            httpContext.Request.Headers.Cookie = cookie;
        }

        await builder.Build()(httpContext);
        Assert.IsNotNull(observedLocale);
        return observedLocale;
    }
}
