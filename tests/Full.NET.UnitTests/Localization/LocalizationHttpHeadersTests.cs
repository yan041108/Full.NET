using Full.NET.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Full.NET.UnitTests.Localization;

[TestClass]
public sealed class LocalizationHttpHeadersTests
{
    [TestMethod]
    public void Apply_sets_content_language_and_adds_accept_language_vary()
    {
        var response = new DefaultHttpContext().Response;

        LocalizationHttpHeaders.Apply(response, "zh-CN", varyByAcceptLanguage: true);

        Assert.AreEqual("zh-CN", response.Headers.ContentLanguage.ToString());
        Assert.AreEqual(HeaderNames.AcceptLanguage, response.Headers.Vary.ToString());
    }

    [TestMethod]
    public void Apply_does_not_duplicate_accept_language_across_comma_separated_values()
    {
        var response = new DefaultHttpContext().Response;
        response.Headers.Vary = new StringValues(
            ["Origin, accept-language", "Accept-Encoding"]);

        LocalizationHttpHeaders.Apply(response, "en-US", varyByAcceptLanguage: true);

        var tokens = response.Headers.Vary
            .SelectMany(value => value?.Split(',') ?? [])
            .Select(value => value.Trim())
            .ToArray();
        Assert.AreEqual(
            1,
            tokens.Count(value => string.Equals(
                value,
                HeaderNames.AcceptLanguage,
                StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Apply_does_not_add_vary_when_the_response_is_not_negotiated()
    {
        var response = new DefaultHttpContext().Response;
        response.Headers.Vary = HeaderNames.Origin;

        LocalizationHttpHeaders.Apply(response, "zh-CN", varyByAcceptLanguage: false);

        Assert.AreEqual("zh-CN", response.Headers.ContentLanguage.ToString());
        Assert.AreEqual(HeaderNames.Origin, response.Headers.Vary.ToString());
    }
}
