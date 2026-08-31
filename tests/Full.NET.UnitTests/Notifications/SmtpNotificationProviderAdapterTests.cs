using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class SmtpNotificationProviderAdapterTests
{
    private const string ValidConfig =
        "{\"fromAddress\":\"sender@example.com\",\"fromDisplayName\":\"Full.NET\",\"host\":\"smtp.example.com\",\"port\":465,\"secureSocketMode\":\"ssl_on_connect\",\"username\":\"sender@example.com\"}";

    [TestMethod]
    public void Descriptor_exposes_closed_email_smtp_schema()
    {
        var adapter = new SmtpNotificationProviderAdapter(
            new StubSecretResolver("authorization-code"),
            new RecordingSmtpTransport());

        Assert.AreEqual("email.smtp", adapter.Descriptor.ProviderTypeKey);
        Assert.AreEqual("1.0.0", adapter.Descriptor.AdapterVersion);
        CollectionAssert.AreEqual(new[] { "email" }, adapter.Descriptor.SupportedChannelKeys.ToArray());
        CollectionAssert.AreEqual(
            new[] { "host", "port", "secureSocketMode", "username", "fromAddress", "fromDisplayName" },
            adapter.Descriptor.NonSecretFields.Select(field => field.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "password" }, adapter.Descriptor.SecretFieldKeys.ToArray());
        Assert.AreEqual("none", adapter.Descriptor.ReceiptModeKey);
        Assert.AreEqual("email", adapter.RecipientEndpointKindKey);
        Assert.IsTrue(adapter.Descriptor.SupportsNativeAot);
    }

    [TestMethod]
    public async Task Valid_config_sends_one_plain_text_message()
    {
        var transport = new RecordingSmtpTransport("provider-message-id");
        var adapter = new SmtpNotificationProviderAdapter(
            new StubSecretResolver("authorization-code"),
            transport);

        var result = await adapter.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.IsTrue(result.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Succeeded, result.ResultCategory);
        Assert.AreEqual("provider-message-id", result.ProviderMessageId);
        Assert.IsNotNull(transport.Command);
        Assert.AreEqual("smtp.example.com", transport.Command.Host);
        Assert.AreEqual(465, transport.Command.Port);
        Assert.AreEqual(SmtpSecureSocketMode.SslOnConnect, transport.Command.SecureSocketMode);
        Assert.AreEqual("sender@example.com", transport.Command.Username);
        Assert.AreEqual("authorization-code", transport.Command.Password);
        Assert.AreEqual("sender@example.com", transport.Command.FromAddress);
        Assert.AreEqual("Full.NET", transport.Command.FromDisplayName);
        Assert.AreEqual("recipient@example.com", transport.Command.RecipientAddress);
        Assert.AreEqual("Test subject", transport.Command.Subject);
        Assert.AreEqual("Test body", transport.Command.Body);
        Assert.AreEqual("delivery-idempotency-key", transport.Command.IdempotencyKey);
        Assert.IsFalse(
            transport.Command.ToString().Contains("authorization-code", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Missing_secret_fails_permanently_without_transport_io()
    {
        var transport = new RecordingSmtpTransport();
        var adapter = new SmtpNotificationProviderAdapter(
            new StubSecretResolver(null),
            transport);

        var result = await adapter.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Permanent, result.ResultCategory);
        Assert.IsNull(transport.Command);
    }

    [TestMethod]
    [DataRow("{")]
    [DataRow("[]")]
    [DataRow("{\"fromAddress\":\"sender@example.com\",\"host\":\"smtp.example.com\",\"port\":0,\"secureSocketMode\":\"ssl_on_connect\",\"username\":\"sender@example.com\"}")]
    [DataRow("{\"fromAddress\":\"sender@example.com\",\"host\":\"smtp.example.com\",\"port\":465,\"secureSocketMode\":\"none\",\"username\":\"sender@example.com\"}")]
    [DataRow("{\"fromAddress\":\"invalid\",\"host\":\"smtp.example.com\",\"port\":465,\"secureSocketMode\":\"ssl_on_connect\",\"username\":\"sender@example.com\"}")]
    [DataRow("{\"fromAddress\":\"sender@example.com\",\"host\":\"https://smtp.example.com\",\"port\":465,\"secureSocketMode\":\"ssl_on_connect\",\"username\":\"sender@example.com\"}")]
    [DataRow("{\"fromAddress\":\"sender@example.com\",\"host\":\"smtp.example.com\",\"port\":465,\"secureSocketMode\":\"ssl_on_connect\",\"unexpected\":true,\"username\":\"sender@example.com\"}")]
    public async Task Invalid_config_fails_permanently_without_transport_io(string config)
    {
        var transport = new RecordingSmtpTransport();
        var adapter = new SmtpNotificationProviderAdapter(
            new StubSecretResolver("authorization-code"),
            transport);

        var result = await adapter.SendAsync(CreateRequest(config), CancellationToken.None);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(NotificationDeliveryRetry.Permanent, result.ResultCategory);
        Assert.IsNull(transport.Command);
    }

    [TestMethod]
    [DataRow((int)SmtpTransportFailureKind.Authentication, "permanent")]
    [DataRow((int)SmtpTransportFailureKind.Permanent, "permanent")]
    [DataRow((int)SmtpTransportFailureKind.Transient, "transient")]
    [DataRow((int)SmtpTransportFailureKind.RateLimited, "rate_limited")]
    public async Task Transport_failures_are_classified(
        int failureKind,
        string expectedCategory)
    {
        var adapter = new SmtpNotificationProviderAdapter(
            new StubSecretResolver("authorization-code"),
            new ThrowingSmtpTransport((SmtpTransportFailureKind)failureKind));

        var result = await adapter.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.IsFalse(result.Accepted);
        Assert.AreEqual(expectedCategory, result.ResultCategory);
    }

    [TestMethod]
    public async Task Cancellation_propagates_without_becoming_a_provider_failure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var adapter = new SmtpNotificationProviderAdapter(
            new StubSecretResolver("authorization-code"),
            new RecordingSmtpTransport());

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => adapter.SendAsync(CreateRequest(), cancellation.Token).AsTask());
    }

    [TestMethod]
    public async Task Environment_resolver_accepts_only_valid_env_references()
    {
        const string variableName = "FULLNET_UNIT_SMTP_SECRET_01991F89";
        var previous = Environment.GetEnvironmentVariable(variableName);
        try
        {
            Environment.SetEnvironmentVariable(variableName, "runtime-secret");
            var resolver = new EnvironmentNotificationSecretResolver();

            Assert.AreEqual(
                "runtime-secret",
                await resolver.ResolveAsync($"env://{variableName}", CancellationToken.None));
            Assert.IsNull(await resolver.ResolveAsync(variableName, CancellationToken.None));
            Assert.IsNull(await resolver.ResolveAsync("env://INVALID-NAME", CancellationToken.None));
            Assert.IsNull(await resolver.ResolveAsync("vault://secret", CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previous);
        }
    }

    [TestMethod]
    public void Authentication_stage_protocol_disconnect_is_not_retried_as_network_failure()
    {
        Assert.AreEqual(
            SmtpTransportFailureKind.Authentication,
            MailKitSmtpTransport.ClassifyProtocolFailure(SmtpTransportStage.Authenticate));
        Assert.AreEqual(
            SmtpTransportFailureKind.Transient,
            MailKitSmtpTransport.ClassifyProtocolFailure(SmtpTransportStage.Connect));
        Assert.AreEqual(
            SmtpTransportFailureKind.Transient,
            MailKitSmtpTransport.ClassifyProtocolFailure(SmtpTransportStage.Send));
    }

    [TestMethod]
    public void Disconnect_failure_does_not_undo_an_already_accepted_message()
    {
        Assert.IsTrue(MailKitSmtpTransport.IsMessageAlreadyAccepted(SmtpTransportStage.Disconnect));
        Assert.IsFalse(MailKitSmtpTransport.IsMessageAlreadyAccepted(SmtpTransportStage.Send));
    }

    private static NotificationProviderRequest CreateRequest(string config = ValidConfig) =>
        new(
            Guid.Parse("01991f89-9110-7a77-9804-58cb3fa86edb"),
            "email",
            "recipient@example.com",
            config,
            "env://FULLNET_TEST_SMTP_PASSWORD",
            "Test subject",
            "Test body",
            "delivery-idempotency-key");

    private sealed class StubSecretResolver(string? secret) : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(
            string? secretReference,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(secret);
        }
    }

    private sealed class RecordingSmtpTransport(string messageId = "message-id") : ISmtpMailTransport
    {
        public SmtpSendCommand? Command { get; private set; }

        public ValueTask<string> SendAsync(
            SmtpSendCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Command = command;
            return ValueTask.FromResult(messageId);
        }
    }

    private sealed class ThrowingSmtpTransport(SmtpTransportFailureKind failureKind) : ISmtpMailTransport
    {
        public ValueTask<string> SendAsync(
            SmtpSendCommand command,
            CancellationToken cancellationToken) =>
            throw new SmtpTransportException(failureKind);
    }
}
