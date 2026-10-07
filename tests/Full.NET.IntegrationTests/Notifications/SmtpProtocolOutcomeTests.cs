using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Full.NET.Modules.Notifications.Domain;
using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;
using MailKit.Net.Smtp;
using MimeKit;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>使用局部受信根和真实 SMTP 会话验证接受、确认丢失以及明确拒收，不改变系统信任。</summary>
[TestClass]
public sealed class SmtpProtocolOutcomeTests
{
    /// <summary>两种 TLS 模式覆盖 DATA 接受、确认丢失、接受后断线、临时拒收与认证拒绝。</summary>
    [TestMethod]
    [DataRow(false, "accepted")]
    [DataRow(true, "accepted")]
    [DataRow(false, "ack_lost")]
    [DataRow(true, "ack_lost")]
    [DataRow(false, "quit_lost")]
    [DataRow(true, "quit_lost")]
    [DataRow(false, "temporary_rejection")]
    [DataRow(true, "temporary_rejection")]
    [DataRow(false, "authentication_rejection")]
    [DataRow(true, "authentication_rejection")]
    public async Task Real_protocol_outcome_is_classified_without_duplicate_retry(bool startTls, string outcome)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Full.NET test root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddHours(1));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var signed = leafRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        using var withKey = signed.CopyWithPrivateKey(leafKey);
        using var certificate = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var observation = new Observation();
            var server = ServeAsync(listener, certificate, startTls, outcome, observation, timeout.Token);
            var transport = new MailKitSmtpTransport(() => new SmtpClient
            {
                ServerCertificateValidationCallback = (_, presented, _, errors) =>
                {
                    if (presented is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0
                        || presented.GetCertHashString() != certificate.GetCertHashString()) return false;
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(root);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.DisableCertificateDownloads = true;
                    using var leaf = new X509Certificate2(presented);
                    return chain.Build(leaf);
                },
            });
            var adapter = new SmtpNotificationProviderAdapter(new ProtocolSecretResolver(), transport);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var config = $$"""{"host":"127.0.0.1","port":{{port}},"secureSocketMode":"{{(startTls ? "starttls" : "ssl_on_connect")}}","username":"protocol-user","fromAddress":"sender@example.test"}""";
            var request = new NotificationProviderRequest(Guid.NewGuid(), "email", "receiver@example.test", config,
                "test-secret", "Verification", "protocol-private-code", "stable-protocol-key", []);
            var result = await adapter.SendAsync(request, timeout.Token);
            await server;
            var accepted = outcome is "accepted" or "quit_lost";
            Assert.AreEqual(accepted, result.Accepted);
            Assert.AreEqual(outcome switch
            {
                "ack_lost" => NotificationDeliveryRetry.Unknown,
                "temporary_rejection" => NotificationDeliveryRetry.Transient,
                "authentication_rejection" => NotificationDeliveryRetry.Permanent,
                _ => NotificationDeliveryRetry.Succeeded,
            }, result.ResultCategory);
            Assert.IsTrue(observation.Authenticated || outcome == "authentication_rejection");
            if (outcome == "authentication_rejection") Assert.IsNull(observation.Message);
            else
            {
                Assert.IsNotNull(observation.Message);
                Assert.AreEqual("receiver@example.test", observation.Message.To.Mailboxes.Single().Address);
                Assert.AreEqual("Verification", observation.Message.Subject);
                var textBody = observation.Message.TextBody;
                var messageId = observation.Message.MessageId;
                Assert.IsNotNull(textBody);
                Assert.IsNotNull(messageId);
                Assert.AreEqual("protocol-private-code", textBody.Trim());
                Assert.IsTrue(messageId.StartsWith("fullnet-", StringComparison.Ordinal));
                if (accepted) Assert.AreEqual(messageId, result.ProviderMessageId);
            }
            Assert.IsFalse(result.ToString().Contains("protocol-private", StringComparison.Ordinal));
            Assert.IsFalse(result.ToString().Contains("protocol-password", StringComparison.Ordinal));
        }
        finally { listener.Stop(); }
    }

    private static async Task ServeAsync(TcpListener listener, X509Certificate2 certificate, bool startTls,
        string outcome, Observation observation, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        await using var network = client.GetStream();
        if (startTls)
        {
            using var reader = new StreamReader(network, Encoding.ASCII, false, 1024, true);
            using var writer = Writer(network);
            await writer.WriteLineAsync("220 localhost SMTP".AsMemory(), token);
            Assert.IsTrue((await reader.ReadLineAsync(token))!.StartsWith("EHLO", StringComparison.Ordinal));
            await writer.WriteLineAsync("250-localhost\r\n250 STARTTLS".AsMemory(), token);
            Assert.AreEqual("STARTTLS", await reader.ReadLineAsync(token));
            await writer.WriteLineAsync("220 Ready".AsMemory(), token);
        }
        await using var tls = new SslStream(network, true);
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, token);
        using var encryptedReader = new StreamReader(tls, Encoding.ASCII, false, 1024, true);
        using var encryptedWriter = Writer(tls);
        if (!startTls) await encryptedWriter.WriteLineAsync("220 localhost SMTP".AsMemory(), token);
        while (await encryptedReader.ReadLineAsync(token) is { } line)
        {
            var verb = line.Split(' ', 2)[0];
            var response = verb switch
            {
                "EHLO" => "250-localhost\r\n250 AUTH PLAIN",
                "MAIL" or "RCPT" or "RSET" => "250 OK",
                "QUIT" => "221 Goodbye",
                "DATA" => "354 End with dot",
                "AUTH" => outcome == "authentication_rejection" ? "535 Authentication failed" : "235 Authenticated",
                _ => throw new InvalidOperationException("Unexpected SMTP verb."),
            };
            if (verb == "AUTH" && outcome != "authentication_rejection")
            {
                var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(line.Split(' ').Last())).Split('\0');
                Assert.IsTrue(credentials.Length == 3 && credentials[1] == "protocol-user" && credentials[2] == "protocol-password");
                observation.Authenticated = true;
            }
            await encryptedWriter.WriteLineAsync(response.AsMemory(), token);
            if (verb == "QUIT") return;
            if (verb != "DATA") continue;
            var mime = new StringBuilder();
            while (await encryptedReader.ReadLineAsync(token) is { } bodyLine && bodyLine != ".")
                mime.Append(bodyLine.StartsWith("..", StringComparison.Ordinal) ? bodyLine[1..] : bodyLine).Append("\r\n");
            using var content = new MemoryStream(Encoding.ASCII.GetBytes(mime.ToString()));
            observation.Message = await MimeMessage.LoadAsync(content, token);
            if (outcome == "ack_lost") return;
            await encryptedWriter.WriteLineAsync((outcome == "temporary_rejection" ? "451 Temporary failure" : "250 Accepted").AsMemory(), token);
            if (outcome == "quit_lost") return;
        }
    }

    private static StreamWriter Writer(Stream stream) => new(stream, Encoding.ASCII, 1024, true) { NewLine = "\r\n", AutoFlush = true };

    private sealed class Observation
    {
        public bool Authenticated { get; set; }
        public MimeMessage? Message { get; set; }
    }

    /// <summary>夹具秘密只存在于本次测试内存，不读取环境凭据。</summary>
    private sealed class ProtocolSecretResolver : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string providerTypeKey, string? secretReference, CancellationToken cancellationToken)
            => ValueTask.FromResult<string?>("protocol-password");
    }

    public TestContext TestContext { get; set; } = null!;
}
