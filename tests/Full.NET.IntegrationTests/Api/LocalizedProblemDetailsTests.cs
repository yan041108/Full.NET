using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Api;

internal static class LocalizedProblemDetailsTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string TraceParent =
        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    public static async Task VerifyAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        var chinese = await SendValidationFailureAsync(
            client,
            "zh-CN",
            cancellationToken);
        var english = await SendValidationFailureAsync(
            client,
            "en-US",
            cancellationToken);
        var fallback = await SendValidationFailureAsync(
            client,
            "fr-FR",
            cancellationToken);

        Assert.AreEqual(HttpStatusCode.BadRequest, chinese.StatusCode);
        Assert.AreEqual(chinese.StatusCode, english.StatusCode);
        Assert.AreEqual(chinese.Code, english.Code);
        Assert.AreEqual(TraceId, ExtractCorrelationTraceId(chinese.TraceId));
        Assert.AreEqual(
            ExtractCorrelationTraceId(chinese.TraceId),
            ExtractCorrelationTraceId(english.TraceId));
        Assert.AreEqual(
            ExtractCorrelationTraceId(chinese.TraceId),
            ExtractCorrelationTraceId(fallback.TraceId));
        Assert.AreEqual("zh-CN", chinese.ContentLanguage);
        Assert.AreEqual("en-US", english.ContentLanguage);
        Assert.AreEqual("zh-CN", fallback.ContentLanguage);
        Assert.AreNotEqual(chinese.Title, english.Title);
        Assert.AreEqual(chinese.Title, fallback.Title);
        Assert.IsTrue(chinese.VaryByAcceptLanguage);
        Assert.IsTrue(english.VaryByAcceptLanguage);
        Assert.AreEqual(chinese.ViolationsJson, english.ViolationsJson);
        Assert.AreEqual(chinese.ViolationsJson, fallback.ViolationsJson);
    }

    private static async Task<LocalizedFailure> SendValidationFailureAsync(
        HttpClient client,
        string locale,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                username = string.Empty,
                password = string.Empty,
            }),
        };
        request.Headers.TryAddWithoutValidation("Accept-Language", locale);
        request.Headers.TryAddWithoutValidation("traceparent", TraceParent);
        request.Headers.TryAddWithoutValidation("Origin", "http://localhost");

        using var response = await client.SendAsync(request, cancellationToken);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        Assert.IsTrue(root.TryGetProperty("violations", out var violations));
        Assert.IsTrue(root.TryGetProperty("errors", out _));

        return new LocalizedFailure(
            response.StatusCode,
            root.GetProperty("code").GetString() ?? string.Empty,
            root.GetProperty("traceId").GetString() ?? string.Empty,
            root.GetProperty("title").GetString() ?? string.Empty,
            response.Content.Headers.ContentLanguage.SingleOrDefault() ?? string.Empty,
            response.Headers.Vary.Any(value => string.Equals(
                value,
                "Accept-Language",
                StringComparison.OrdinalIgnoreCase)),
            violations.GetRawText());
    }

    private static string ExtractCorrelationTraceId(string traceIdentifier)
    {
        var segments = traceIdentifier.Split('-');
        return segments.Length == 4
               && string.Equals(segments[0], "00", StringComparison.Ordinal)
            ? segments[1]
            : traceIdentifier;
    }

    private sealed record LocalizedFailure(
        HttpStatusCode StatusCode,
        string Code,
        string TraceId,
        string Title,
        string ContentLanguage,
        bool VaryByAcceptLanguage,
        string ViolationsJson);
}
