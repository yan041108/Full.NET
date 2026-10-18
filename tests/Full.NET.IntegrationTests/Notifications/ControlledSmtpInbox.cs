using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Full.NET.Modules.Notifications.Providers.Smtp;
using MailKit.Net.Smtp;
using MimeKit;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>只信任本次测试根证书的本机 SMTP 收件箱，复用真实握手与 MIME 验收。</summary>
internal sealed class ControlledSmtpInbox : IAsyncDisposable
{
    private readonly X509Certificate2 root;
    private readonly X509Certificate2 certificate;
    private readonly TcpListener listener;
    private readonly bool startTls;
    private readonly CancellationTokenSource shutdown = new();
    private readonly List<Task<Observation>> sessions = [];

    public ControlledSmtpInbox(bool startTls)
    {
        this.startTls = startTls;
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Full.NET test root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddHours(1));
        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest("CN=localhost", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        leafRequest.CertificateExtensions.Add(names.Build());
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var signed = leafRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        using var withKey = signed.CopyWithPrivateKey(leafKey);
        certificate = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);

        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
    }

    public object Configuration => new
    {
        host = "127.0.0.1",
        port = ((IPEndPoint)listener.LocalEndpoint).Port,
        secureSocketMode = startTls ? "starttls" : "ssl_on_connect",
        username = "protocol-user",
        fromAddress = "sender@example.test",
    };

    public MailKitSmtpTransport CreateTransport()
    {
            return new MailKitSmtpTransport(() => new SmtpClient
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

    }

    public Task<Observation> ReceiveAsync(string outcome, CancellationToken token)
    {
        var session = ReceiveCoreAsync(outcome, token);
        sessions.Add(session);
        return session;
    }

    private async Task<Observation> ReceiveCoreAsync(string outcome, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, shutdown.Token);
        var observation = new Observation();
        await ServeAsync(listener, certificate, startTls, outcome, observation, cancellation.Token);
        return observation;
    }

    public async ValueTask DisposeAsync()
    {
        // HTTP 或客户端先失败时也必须取消并观察已接收连接，证书只能在所有握手结束后释放。
        await shutdown.CancelAsync();
        listener.Stop();
        try { await Task.WhenAll(sessions); }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally
        {
            certificate.Dispose();
            root.Dispose();
            shutdown.Dispose();
        }
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

    internal sealed class Observation
    {
        public bool Authenticated { get; set; }
        public MimeMessage? Message { get; set; }
    }

    /// <summary>夹具秘密只存在于本次测试内存，不读取环境凭据。</summary>
    internal sealed class ProtocolSecretResolver : INotificationSecretResolver
    {
        public ValueTask<string?> ResolveAsync(string providerTypeKey, string? secretReference, CancellationToken cancellationToken)
            => ValueTask.FromResult<string?>("protocol-password");
    }

}
