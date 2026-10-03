using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Full.NET.Hosting.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class HttpOperationMetadataTests
{
    [TestMethod]
    public void Minimal_endpoint_uses_static_metadata_without_inventing_mvc_fields()
    {
        var context = NewContext();
        context.Request.Headers["traceparent"] =
            "00-11111111111111111111111111111111-2222222222222222-01";
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/orders/{id}"),
            0,
            new EndpointMetadataCollection(new EndpointNameMetadata("orders.get")),
            "Get order"));

        var metadata = HttpOperationMetadataBuilder.Capture(context);

        Assert.AreEqual("/api/v1/orders/{id}", metadata.RouteTemplate);
        Assert.AreEqual("orders.get", metadata.EndpointName);
        Assert.AreEqual("Get order", metadata.EndpointDisplayName);
        Assert.IsNull(metadata.Controller);
        Assert.IsNull(metadata.Action);
        Assert.IsNull(metadata.Area);
        Assert.IsNull(metadata.TraceId);
        Assert.IsNull(metadata.SpanId);
        Assert.AreEqual("request-123", metadata.RequestId);
    }

    [TestMethod]
    public void Mvc_endpoint_reads_controller_action_and_area_metadata()
    {
        var context = NewContext();
        var descriptor = new ControllerActionDescriptor
        {
            ControllerName = "Orders",
            ActionName = "Approve",
            RouteValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["area"] = "BackOffice",
            },
        };
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/orders/{id}/approve"),
            0,
            new EndpointMetadataCollection(descriptor),
            "Approve order"));

        var metadata = HttpOperationMetadataBuilder.Capture(context);

        Assert.AreEqual("Orders", metadata.Controller);
        Assert.AreEqual("Approve", metadata.Action);
        Assert.AreEqual("BackOffice", metadata.Area);
    }

    [TestMethod]
    public void Header_spoofing_does_not_set_client_identity_or_ip()
    {
        var context = NewContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.20";
        context.Request.Headers["X-Client-Id"] = "forged-client";
        context.Request.Headers["User-Agent"] = "browser\r\nsecret";
        context.Request.Headers["Accept-Language"] = "zh-CN\r\nsecret";
        context.Request.Headers.Referer =
            "https://user:secret@example.test/private?token=abc#fragment";

        var metadata = HttpOperationMetadataBuilder.Capture(context);

        Assert.IsNull(metadata.ClientIdFingerprint);
        Assert.AreEqual(
            HttpOperationLogSanitizer.FingerprintClientIp("127.0.0.1"),
            metadata.ClientIpFingerprint);
        Assert.AreEqual("Other", metadata.UserAgentSummary);
        Assert.IsNull(metadata.AcceptLanguageSummary);
        Assert.AreEqual(
            HttpOperationLogSanitizer.FingerprintClientIp("https://example.test"),
            metadata.SourceOriginFingerprint);
    }

    [TestMethod]
    public void Free_text_headers_and_claims_cannot_put_secrets_in_b2_metadata()
    {
        var context = NewContext();
        context.Request.Headers.UserAgent = "Chrome/120 Bearer token-secret";
        context.Request.Headers.AcceptLanguage = "en-US, token-secret";
        context.Request.Headers.Origin = "https://token-secret.example.test";
        context.Request.Method = "token-secret";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("client_id", "token-secret")],
            authenticationType: "test"));

        var metadata = HttpOperationMetadataBuilder.Capture(context);

        Assert.AreEqual("OTHER", metadata.HttpMethod);
        Assert.IsFalse(metadata.ToString().Contains("token-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Activity_and_authenticated_client_claim_are_kept_separate_from_request_id()
    {
        using var activity = new Activity("http-test")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        var context = NewContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("client_id", "trusted-client")],
            authenticationType: "test"));

        var metadata = HttpOperationMetadataBuilder.Capture(context);

        Assert.AreEqual(activity.TraceId.ToString(), metadata.TraceId);
        Assert.AreEqual(activity.SpanId.ToString(), metadata.SpanId);
        Assert.AreEqual("request-123", metadata.RequestId);
        Assert.AreEqual(
            HttpOperationLogSanitizer.FingerprintClientIp("trusted-client"),
            metadata.ClientIdFingerprint);
    }

    [TestMethod]
    public void Long_user_agent_is_bounded_and_thread_id_is_opt_in()
    {
        var context = NewContext();
        context.Request.Headers.UserAgent = new string('a', 511) + "😀" + "tail";

        var defaultMetadata = HttpOperationMetadataBuilder.Capture(context);
        var diagnosticMetadata = HttpOperationMetadataBuilder.Capture(
            context,
            captureThreadId: true);

        Assert.AreEqual("Other", defaultMetadata.UserAgentSummary);
        Assert.IsNull(defaultMetadata.CaptureThreadId);
        Assert.AreEqual(
            Environment.CurrentManagedThreadId,
            diagnosticMetadata.CaptureThreadId);
    }

    [TestMethod]
    public void Oversized_source_url_is_rejected_before_parsing()
    {
        var context = NewContext();
        context.Request.Headers.Referer =
            "https://example.test/" + new string('x', 4096);

        var metadata = HttpOperationMetadataBuilder.Capture(context);

        Assert.IsNull(metadata.SourceOriginFingerprint);
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "request-123";
        context.Request.Path = "/api/v1/orders/42";
        context.Request.Method = HttpMethods.Get;
        context.Request.Protocol = "HTTP/2";
        return context;
    }
}
