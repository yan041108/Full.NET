using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Features.WriteOutboundCallLogs;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class OutboundCallAuditHandlerTests
{
    [TestMethod]
    public void Sanitize_redacts_bearer_token_from_safe_error_code()
    {
        var sanitized = OutboundCallAuditHandler.Sanitize(
            CreateRequest(safeErrorCode: "Bearer eyJhbGciOiJIUzI1NiJ9.payload"));

        Assert.AreEqual("error.redacted", sanitized.Record.SafeErrorCode);
        Assert.IsTrue(sanitized.HadSensitiveInput);
    }

    [TestMethod]
    public void Sanitize_redacts_connection_string_from_destination_category()
    {
        var sanitized = OutboundCallAuditHandler.Sanitize(
            CreateRequest(
                destinationHostCategory:
                "Server=db.local;Database=pay;User Id=sa;Password=secret"));

        Assert.AreEqual("host.redacted", sanitized.Record.DestinationHostCategory);
        Assert.IsTrue(sanitized.HadSensitiveInput);
    }

    [TestMethod]
    public void Sanitize_strips_url_query_from_destination_category()
    {
        var sanitized = OutboundCallAuditHandler.Sanitize(
            CreateRequest(
                destinationHostCategory: "https://payments.example.com/v1/charge?token=abc"));

        Assert.AreEqual("payments.example.com", sanitized.Record.DestinationHostCategory);
    }

    [TestMethod]
    public void Sanitize_rejects_api_key_marker_in_provider_key()
    {
        var sanitized = OutboundCallAuditHandler.Sanitize(
            CreateRequest(providerKey: "api_key=fnk_deadbeef"));

        Assert.AreEqual("provider.unknown", sanitized.Record.ProviderKey);
        Assert.IsTrue(sanitized.HadSensitiveInput);
    }

    [TestMethod]
    public void Sanitize_truncates_and_normalizes_stable_keys()
    {
        var longKey = new string('a', 80);
        var sanitized = OutboundCallAuditHandler.Sanitize(
            CreateRequest(providerKey: longKey, operationKey: "Charge.Create"));

        Assert.AreEqual(64, sanitized.Record.ProviderKey.Length);
        Assert.AreEqual("charge.create", sanitized.Record.OperationKey);
    }

    [TestMethod]
    public void Sanitize_does_not_accept_multiline_exception_text_as_error_code()
    {
        var sanitized = OutboundCallAuditHandler.Sanitize(
            CreateRequest(safeErrorCode: "System.InvalidOperationException: boom\r\n at Program.Main()"));

        Assert.AreEqual("error.redacted", sanitized.Record.SafeErrorCode);
    }

    private static OutboundCallAuditRequest CreateRequest(
        string providerKey = "payments.stripe",
        string operationKey = "charge.create",
        string destinationHostCategory = "api.stripe.com",
        string? safeErrorCode = null) =>
        new(
            providerKey,
            operationKey,
            destinationHostCategory,
            StatusCode: 502,
            Succeeded: false,
            DurationMs: 120,
            RetryCount: 2,
            TraceId: "trace-123",
            SafeErrorCode: safeErrorCode);
}
