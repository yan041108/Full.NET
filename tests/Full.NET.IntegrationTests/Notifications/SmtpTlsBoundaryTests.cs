using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Full.NET.Modules.Notifications.Providers.Smtp;

namespace Full.NET.IntegrationTests.Notifications;

/// <summary>本机受控 SMTP 服务验证真实 MailKit TLS 边界，不安装根证书、不绕过证书校验。</summary>
[TestClass]
public sealed class SmtpTlsBoundaryTests
{
    /// <summary>两种正式 TLS 模式都必须在证书不受信任时停止，不能发送认证信息或挑战正文。</summary>
    /// <param name="startTls">是否在明文问候后显式升级为 TLS。</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Untrusted_certificate_stops_before_authentication_and_challenge_body(bool startTls)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        // Windows Schannel 需要可导入的私钥句柄；重载仅用于受控服务，不修改系统信任存储。
        using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var server = ServeAsync(listener, certificate, startTls, timeout.Token);
            var command = new SmtpSendCommand("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
                startTls ? SmtpSecureSocketMode.StartTls : SmtpSecureSocketMode.SslOnConnect,
                "boundary-user", "boundary-private-password", "sender@example.test", null,
                "receiver@example.test", "Verification", "boundary-private-code", "boundary-key", []);

            var exception = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await new MailKitSmtpTransport().SendAsync(command, timeout.Token);
            });
            var observation = await server;

            var handshake = exception as MailKit.Security.SslHandshakeException
                ?? exception.InnerException as MailKit.Security.SslHandshakeException;
            Assert.IsNotNull(handshake);
            Assert.IsNotNull(handshake.ServerCertificate, $"TLS 服务端诊断类型：{observation.ServerFaultType}");
            Assert.AreEqual(certificate.GetCertHashString(), handshake.ServerCertificate.GetCertHashString());
            Assert.IsTrue(observation.TlsAttempted);
            Assert.IsFalse(observation.Commands.Any(line => line.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(observation.Commands.Any(line => line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(observation.Commands.Any(line => line.Contains("boundary-private", StringComparison.Ordinal)));
            if (startTls)
            {
                Assert.IsTrue(observation.Commands.Any(line => line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)));
                Assert.IsTrue(observation.Commands.Any(line => line.Equals("STARTTLS", StringComparison.OrdinalIgnoreCase)));
            }
            Assert.IsInstanceOfType<SmtpTransportException>(exception);
            var failure = (SmtpTransportException)exception;
            Assert.AreEqual(SmtpTransportStage.Connect, failure.FailureStage);
            Assert.AreEqual(SmtpTransportFailureKind.Transient, failure.FailureKind);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<Observation> ServeAsync(
        TcpListener listener, X509Certificate2 certificate, bool startTls, CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var network = client.GetStream();
        var commands = new List<string>();
        if (startTls)
        {
            using var reader = new StreamReader(network, Encoding.ASCII, false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(network, Encoding.ASCII, 1024, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 localhost controlled SMTP".AsMemory(), cancellationToken);
            commands.Add(await reader.ReadLineAsync(cancellationToken) ?? string.Empty);
            await writer.WriteLineAsync("250-localhost\r\n250 STARTTLS".AsMemory(), cancellationToken);
            commands.Add(await reader.ReadLineAsync(cancellationToken) ?? string.Empty);
            await writer.WriteLineAsync("220 Ready for TLS".AsMemory(), cancellationToken);
        }

        await using var tls = new SslStream(network, leaveInnerStreamOpen: true);
        string? serverFaultType = null;
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, cancellationToken);
            // 个别 TLS 实现会先完成服务端握手，再收到客户端拒绝通知；继续读取才能观察越界发送。
            using var reader = new StreamReader(tls, Encoding.ASCII, false, 1024, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is not null)
            {
                commands.Add(line);
            }
        }
        catch (Exception exception) when (exception is AuthenticationException or IOException)
        {
            // 不记录 TLS 异常原文，受控服务只保留是否进入握手及 SMTP 命令阶段。
            serverFaultType = exception.GetType().Name;
        }

        return new Observation(true, commands, serverFaultType);
    }

    private sealed record Observation(bool TlsAttempted, IReadOnlyList<string> Commands, string? ServerFaultType);

    public TestContext TestContext { get; set; } = null!;
}
